using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using AirportSmith.Models;

namespace AirportSmith.Services;

// Result of the pure, static Build step — kept separate from Export's file
// write so the core mapping logic is unit-testable with no I/O, the same
// split AirportDiagramProjector.Project (pure) vs. AirportProjectStore.Save
// (I/O) already uses elsewhere in this codebase.
public sealed record AirportXmlExportResult(XDocument Document, IReadOnlyList<string> Warnings);

// Builds and writes the <FSData><Airport>...</Airport></FSData> XML this
// project's requirements.md "Generate SDK-compatible <Airport> XML" epic
// describes. Every element/attribute here is checked directly against the
// locally installed MSFS 2024 SDK's Tools/bin/bglcomp.xsd (the schema
// bglcomp itself validates against) and the airport/runway/taxiway-xml-
// properties documentation pages under Documentation/public/flighting/
// content-configuration/environment/airports-and-facilities/ — not memory.
//
// Overriding the stock airport at the same ICAO is done by emitting a
// <DeleteAirport deleteAllRunways="TRUE" deleteAllTaxiways="TRUE" /> before
// re-adding fresh <Runway>/<TaxiwayPoint>/<TaxiwayParking>/<TaxiName>/
// <TaxiwayPath> elements — per the docs' explicit callout that data is
// otherwise ADDED to an existing airport, not replaced. Frequencies and
// Jetways are deliberately left untouched (no deleteAllFrequencies/
// deleteAllJetways, no <Com>/<Jetway> elements at all) — see
// requirements.md's "Generate SDK-compatible <Airport> XML" epic for why.
//
// System.Xml.Linq (XDocument/XElement) is used directly rather than
// XmlSerializer over annotated classes: attribute presence is conditional
// per-field (e.g. a null VASI slot must omit the whole <Vasi> element, not
// emit an empty/default one) and every enum needs explicit mapping to the
// schema's string form anyway, so a serializer's declarative attributes
// would add nothing over building elements directly.
public class AirportXmlExporter : IAirportXmlExporter
{
    public AirportXmlExportOutcome Export(AirportDetails airport, string filePath)
    {
        var result = Build(airport);
        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
        using (var writer = XmlWriter.Create(filePath, settings))
            result.Document.Save(writer);
        return new AirportXmlExportOutcome(filePath, result.Warnings);
    }

    public static AirportXmlExportResult Build(AirportDetails airport)
    {
        var warnings = new List<string>();

        var airportElement = new XElement("Airport",
            new XAttribute("ident", airport.Icao),
            new XAttribute("lat", F(airport.Latitude)),
            new XAttribute("lon", F(airport.Longitude)),
            new XAttribute("alt", F(airport.ElevationMeters)),
            new XAttribute("name", airport.Name),
            new XAttribute("magvar", F(airport.MagneticVariationDeg)),
            new XElement("DeleteAirport",
                new XAttribute("deleteAllRunways", "TRUE"),
                new XAttribute("deleteAllTaxiways", "TRUE")));

        foreach (var runway in airport.Runways)
            airportElement.Add(BuildRunway(runway, warnings));

        // Decide which taxi paths can actually be exported BEFORE building
        // any <TaxiwayPoint> elements. A <TaxiwayPoint> must only be emitted
        // when at least one exported <TaxiwayPath> actually connects to it —
        // otherwise the Scenery Editor flags it as "not linked to the main
        // graph" (confirmed against a real import). Building the point set
        // from every path regardless of whether that path itself ends up
        // exported — the original approach here — orphans a point whenever
        // its path has one resolved end and one unresolved one, or an
        // unmappable type: the point still gets a coordinate from the
        // resolved end, but the only path that would have connected it was
        // dropped.
        var exportablePaths = new List<(TaxiPathSegment Path, string XmlType)>();
        foreach (var path in airport.TaxiPaths)
        {
            if (path.StartXMeters is null || path.StartZMeters is null || path.EndXMeters is null || path.EndZMeters is null)
            {
                warnings.Add($"Taxi path {path.StartIndex}->{path.EndIndex} has an unresolved point (see " +
                    "TaxiPathSegment.StartXMeters/EndXMeters) and was skipped.");
                continue;
            }

            var xmlType = MapTaxiPathType(path.Type);
            if (xmlType is null)
            {
                warnings.Add($"Taxi path {path.StartIndex}->{path.EndIndex} has type {path.Type}, which has no XML " +
                    "equivalent — skipped from the export.");
                continue;
            }

            exportablePaths.Add((path, xmlType));
        }

        foreach (var point in BuildTaxiwayPoints(exportablePaths.Select(p => p.Path)))
            airportElement.Add(point);

        var parkingIndices = ComputeParkingIndices(airport.ParkingSpots, warnings);
        for (var i = 0; i < airport.ParkingSpots.Count; i++)
            airportElement.Add(BuildTaxiwayParking(airport.ParkingSpots[i], parkingIndices[i], warnings));

        var taxiNameIndices = new Dictionary<Guid, int>();
        for (var i = 0; i < airport.TaxiNames.Count; i++)
        {
            taxiNameIndices[airport.TaxiNames[i].Id] = i;
            airportElement.Add(BuildTaxiName(airport.TaxiNames[i], i, warnings));
        }

        // See BuildTaxiwayPath's comment on DefaultTaxiwayPathSurface for why
        // every exported path gets the same synthesized surface — one warning
        // covering the whole export rather than one per path (a real airport
        // can have hundreds).
        if (exportablePaths.Count > 0)
            warnings.Add("Taxiway paths have no surface data available from SimConnect (TAXI_PATH has no " +
                $"SURFACE field) — every exported <TaxiwayPath> was given a synthesized \"{DefaultTaxiwayPathSurface}\" " +
                "surface so the Scenery Editor can construct the path object at all.");

        foreach (var (path, xmlType) in exportablePaths)
            airportElement.Add(BuildTaxiwayPath(path, xmlType, taxiNameIndices, warnings));

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("FSData", new XAttribute("version", "9.0"), airportElement));

        return new AirportXmlExportResult(document, warnings);
    }

    // ---- Runway -------------------------------------------------------

    private static XElement BuildRunway(Runway r, List<string> warnings)
    {
        var (primaryNumber, primaryDesignator) = ParseDesignation(r.PrimaryDesignation);
        var (_, secondaryDesignator) = ParseDesignation(r.SecondaryDesignation);

        var element = new XElement("Runway",
            new XAttribute("lat", F(r.Latitude)),
            new XAttribute("lon", F(r.Longitude)),
            new XAttribute("alt", F(r.ElevationMeters)),
            new XAttribute("surface", MapSurface(r.SurfaceType, warnings)),
            new XAttribute("heading", F(r.HeadingDeg)),
            new XAttribute("length", F(r.LengthMeters)),
            new XAttribute("width", F(r.WidthMeters)),
            new XAttribute("number", primaryNumber.ToString(CultureInfo.InvariantCulture)));

        if (primaryDesignator != null) element.Add(new XAttribute("designator", primaryDesignator));
        if (primaryDesignator != null) element.Add(new XAttribute("primaryDesignator", primaryDesignator));
        if (secondaryDesignator != null) element.Add(new XAttribute("secondaryDesignator", secondaryDesignator));

        // Sub-elements MUST appear in this order per bglcomp.xsd's ctRunway
        // sequence: ...Lights, OffsetThreshold, BlastPad, Overrun,
        // ApproachLights, Vasi, ...RunwayStart...
        element.Add(new XElement("Lights", new XAttribute("edge", MapLightIntensity(r.EdgeLightIntensity))));

        AddPavementFeature(element, "OffsetThreshold", "PRIMARY", r.PrimaryThreshold);
        AddPavementFeature(element, "OffsetThreshold", "SECONDARY", r.SecondaryThreshold);
        AddPavementFeature(element, "BlastPad", "PRIMARY", r.PrimaryBlastPad);
        AddPavementFeature(element, "BlastPad", "SECONDARY", r.SecondaryBlastPad);
        AddPavementFeature(element, "Overrun", "PRIMARY", r.PrimaryOverrun);
        AddPavementFeature(element, "Overrun", "SECONDARY", r.SecondaryOverrun);

        AddApproachLights(element, "PRIMARY", r.PrimaryApproachLights?.SystemType, r.PrimaryApproachLightsStrobeCount,
            r.PrimaryApproachLightsHasEndLights, r.PrimaryApproachLightsHasReilLights, r.PrimaryApproachLightsHasTouchdownLights);
        AddApproachLights(element, "SECONDARY", r.SecondaryApproachLights?.SystemType, r.SecondaryApproachLightsStrobeCount,
            r.SecondaryApproachLightsHasEndLights, r.SecondaryApproachLightsHasReilLights, r.SecondaryApproachLightsHasTouchdownLights);

        AddVasi(element, "PRIMARY", "LEFT", r.PrimaryLeftVasiType, r.PrimaryLeftVasiAngleDeg,
            r.PrimaryLeftVasiBiasXMeters, r.PrimaryLeftVasiBiasZMeters, r.PrimaryLeftVasiSpacingMeters, r.LengthMeters, warnings);
        AddVasi(element, "PRIMARY", "RIGHT", r.PrimaryRightVasiType, r.PrimaryRightVasiAngleDeg,
            r.PrimaryRightVasiBiasXMeters, r.PrimaryRightVasiBiasZMeters, r.PrimaryRightVasiSpacingMeters, r.LengthMeters, warnings);
        AddVasi(element, "SECONDARY", "LEFT", r.SecondaryLeftVasiType, r.SecondaryLeftVasiAngleDeg,
            r.SecondaryLeftVasiBiasXMeters, r.SecondaryLeftVasiBiasZMeters, r.SecondaryLeftVasiSpacingMeters, r.LengthMeters, warnings);
        AddVasi(element, "SECONDARY", "RIGHT", r.SecondaryRightVasiType, r.SecondaryRightVasiAngleDeg,
            r.SecondaryRightVasiBiasXMeters, r.SecondaryRightVasiBiasZMeters, r.SecondaryRightVasiSpacingMeters, r.LengthMeters, warnings);

        foreach (var start in BuildRunwayStarts(r))
            element.Add(start);

        return element;
    }

    private static void AddPavementFeature(XElement runwayElement, string elementName, string end, RunwayPavementFeature? feature)
    {
        if (feature is null) return;
        runwayElement.Add(new XElement(elementName,
            new XAttribute("end", end),
            new XAttribute("length", F(feature.LengthMeters)),
            new XAttribute("width", F(feature.WidthMeters))));
    }

    // systemType and strobeCount/hasEndLights/hasReilLights/hasTouchdownLights
    // are independent (see Runway.PrimaryApproachLightsStrobeCount's own doc
    // comment) — the element itself is only emitted when there's SOMETHING
    // to report (matching <Vasi>/<BlastPad>/<Overrun>'s own "omit entirely
    // when nothing installed" convention), which now includes a runway end
    // with REIL/end/touchdown lights or strobes but no full approach light
    // system at all — the `system` attribute is genuinely optional per the
    // SDK's own <ApproachLights/> docs and is simply left off in that case.
    // reil/endLights/touchdown are always written once the element exists
    // (TRUE/FALSE via ToXmlBool), matching centerLineLighted/leftEdgeLighted/
    // rightEdgeLighted's own "always explicit, never omitted" convention on
    // <TaxiwayPath> — strobes likewise always written (even "0"), since a
    // Positive Integer attribute stating a real, meaningful "none" is more
    // useful than an ambiguous omission.
    private static void AddApproachLights(XElement runwayElement, string end, ApproachLightSystemType? systemType,
        int strobeCount, bool hasEndLights, bool hasReilLights, bool hasTouchdownLights)
    {
        if (systemType is null && strobeCount <= 0 && !hasEndLights && !hasReilLights && !hasTouchdownLights) return;

        var element = new XElement("ApproachLights",
            new XAttribute("end", end),
            new XAttribute("reil", ToXmlBool(hasReilLights)),
            new XAttribute("strobes", strobeCount.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("endLights", ToXmlBool(hasEndLights)),
            new XAttribute("touchdown", ToXmlBool(hasTouchdownLights)));
        if (systemType is not null) element.Add(new XAttribute("system", MapApproachLightSystem(systemType.Value)));

        runwayElement.Add(element);
    }

    private static void AddVasi(XElement runwayElement, string end, string side, VasiType? type, double? angleDeg,
        double? biasX, double? biasZ, double? spacing, double lengthMeters, List<string> warnings)
    {
        if (type is null) return;

        // biasX/biasZ/spacing are required attributes per bglcomp.xsd's
        // ctVasi — if extraction never populated them (a project saved
        // before this epic added BIAS_X/BIAS_Z/SPACING to the SimConnect
        // request), fall back to 0 with a warning rather than silently
        // emitting a <Vasi> the compiler might reject for missing attributes.
        var missing = biasX is null || biasZ is null || spacing is null;
        if (missing)
            warnings.Add($"Runway {end} {side} VASI/PAPI has no stored position (biasX/biasZ/spacing) — " +
                "re-extract this airport to pick up the new fields; defaulted to 0 for now.");

        // Two conversions confirmed the hard way against a live MSFS 2024
        // Scenery Editor import (a real OIBK 09L LEFT PAPI): the SDK's own
        // <Vasi> docs (content-configuration/environment/airports-and-
        // facilities/runway-xml-properties) define biasZ as "distance along
        // the runway FROM THE RUNWAY CENTER POINT to the VASI reference
        // point" — NOT from the threshold, which is what Runway's own
        // *VasiBiasZMeters (and the Edit tab's Z column/diagram/click-to-
        // place — all still "distance inward from that end's threshold",
        // unchanged) actually store. halfLength - biasZ converts between the
        // two: a point `biasZ` meters inward from this end's threshold sits
        // `halfLength - biasZ` meters from the center, toward this end —
        // symmetric for both PRIMARY and SECONDARY since each is relative to
        // its own end's threshold. And biasX is documented as a plain
        // "distance ... across the runway width" (side already carries
        // which physical side) — exporting AirportSmith's OWN signed value
        // (positive/negative only meaningful as an internal diagram-drawing
        // convention, unrelated to Left/Right) produced biasX=-54.75 in a
        // real export, which the Scenery Editor silently reset to 0 on
        // import instead of erroring — Math.Abs fixes that.
        var halfLength = lengthMeters / 2;
        var biasXMagnitude = Math.Abs(biasX ?? 0);
        var biasZFromCenter = halfLength - (biasZ ?? 0);

        runwayElement.Add(new XElement("Vasi",
            new XAttribute("end", end),
            new XAttribute("type", MapVasiType(type.Value)),
            new XAttribute("side", side),
            new XAttribute("biasX", F(biasXMagnitude)),
            new XAttribute("biasZ", F(biasZFromCenter)),
            new XAttribute("spacing", F(spacing ?? 0)),
            new XAttribute("pitch", F(angleDeg ?? 0))));
    }

    // <RunwayStart> only accepts lat/lon (no biasX/biasZ), and AirportSmith
    // stores no start-point data at all — computed here from the runway's
    // own center/heading/length, mirroring AirportDiagramProjector's
    // threshold1/threshold2 geometry (including its live-sim-corrected
    // "primary threshold sits at the -heading end" convention) and using
    // GeoProjection.UnprojectLocalPoint for the local-meters -> lat/lon step
    // ProjectLatLon doesn't provide.
    private static IEnumerable<XElement> BuildRunwayStarts(Runway r)
    {
        var headingRad = r.HeadingDeg * Math.PI / 180;
        var forward = (X: Math.Sin(headingRad), Z: Math.Cos(headingRad));
        var halfLength = r.LengthMeters / 2;

        var (primaryLat, primaryLon) = GeoProjection.UnprojectLocalPoint(
            r.Latitude, r.Longitude, -forward.X * halfLength, -forward.Z * halfLength);
        var (secondaryLat, secondaryLon) = GeoProjection.UnprojectLocalPoint(
            r.Latitude, r.Longitude, forward.X * halfLength, forward.Z * halfLength);

        yield return new XElement("RunwayStart",
            new XAttribute("end", "PRIMARY"),
            new XAttribute("lat", F(primaryLat)),
            new XAttribute("lon", F(primaryLon)),
            new XAttribute("alt", F(r.ElevationMeters)),
            new XAttribute("heading", F(r.HeadingDeg)));

        yield return new XElement("RunwayStart",
            new XAttribute("end", "SECONDARY"),
            new XAttribute("lat", F(secondaryLat)),
            new XAttribute("lon", F(secondaryLon)),
            new XAttribute("alt", F(r.ElevationMeters)),
            new XAttribute("heading", F((r.HeadingDeg + 180) % 360)));
    }

    // ---- Taxiways -------------------------------------------------------

    // Synthesizes one <TaxiwayPoint> per distinct StartIndex/EndIndex seen
    // across the already-filtered exportable paths (see Build) —
    // TaxiPathSegment.StartIndex/EndIndex already uniquely identify the same
    // original sim taxi point across every segment that shares it, so
    // they're reused directly as the exported index rather than assigning
    // fresh ones. Every path passed in here is guaranteed to have resolved
    // Start/End coordinates (Build filters that before calling this), so
    // every point built here is guaranteed to have at least one <TaxiwayPath>
    // connecting to it.
    //
    // type/orientation come from TaxiPathSegment.Start/EndPointType and
    // Start/EndPointOrientation (the underlying TAXI_POINT row's own fields,
    // resolved by SimConnectService) rather than a hardcoded "NORMAL" —
    // confirmed against a real MSFS 2024 SDK Scenery Editor import that
    // marking every point NORMAL breaks the taxiway network's validation
    // ("point not linked to a hold short" / "no hold short within 200m of
    // runway"): hold-short points aren't cosmetic, the network structurally
    // needs them preserved. Falls back to NORMAL when a point's type never
    // resolved (an older saved project, or a genuinely un-typed point).
    //
    // A Type == Parking path's EndIndex is NOT a taxiway point — it's a
    // <TaxiwayParking> ItemIndex (see SimConnectService.ResolveTaxiPathPoints
    // and ComputeParkingIndices) — so it's deliberately excluded here even
    // though its coordinates did resolve; synthesizing a same-indexed
    // <TaxiwayPoint> would be redundant with the <TaxiwayParking> element
    // that already covers that index, and was the original source of the
    // "point not linked to the main graph" import errors before this was
    // understood.
    private static IEnumerable<XElement> BuildTaxiwayPoints(IEnumerable<TaxiPathSegment> exportablePaths)
    {
        var pointsByIndex = new Dictionary<int, (double X, double Z, TaxiPointType? Type, TaxiPointOrientation? Orientation)>();

        foreach (var path in exportablePaths)
        {
            pointsByIndex.TryAdd(path.StartIndex, (path.StartXMeters!.Value, path.StartZMeters!.Value, path.StartPointType, path.StartPointOrientation));
            if (path.Type != TaxiPathType.Parking)
                pointsByIndex.TryAdd(path.EndIndex, (path.EndXMeters!.Value, path.EndZMeters!.Value, path.EndPointType, path.EndPointOrientation));
        }

        return pointsByIndex.OrderBy(kvp => kvp.Key).Select(kvp =>
        {
            var xmlType = kvp.Value.Type is { } type ? MapTaxiPointType(type) : "NORMAL";
            var element = new XElement("TaxiwayPoint",
                new XAttribute("index", kvp.Key),
                new XAttribute("type", xmlType),
                new XAttribute("biasX", F(kvp.Value.X)),
                new XAttribute("biasZ", F(kvp.Value.Z)));

            // ORIENTATION is only meaningful for hold-short-family points per
            // the SDK docs.
            if (IsHoldShortType(kvp.Value.Type) && kvp.Value.Orientation is { } orientation)
                element.Add(new XAttribute("orientation", MapTaxiPointOrientation(orientation)));

            return element;
        });
    }

    // Normally each spot's own ItemIndex — a Type == Parking taxi path's
    // EndIndex references it directly (see
    // SimConnectService.ResolveTaxiPathPoints), so exporting that same value
    // as this element's index is what makes such a <TaxiwayPath end="N">
    // actually resolve to the right <TaxiwayParking index="N">. Falls back to
    // sequential renumbering (the pre-ItemIndex behavior) only when
    // ItemIndex values collide — the signature of a project saved before
    // this field existed, where every spot defaults to 0 — since exporting
    // duplicate indices would be worse than a taxi path possibly not
    // resolving to the right spot.
    private static IReadOnlyList<int> ComputeParkingIndices(IReadOnlyList<TaxiParkingSpot> spots, List<string> warnings)
    {
        var itemIndices = spots.Select(s => s.ItemIndex).ToList();
        if (itemIndices.Distinct().Count() == spots.Count)
            return itemIndices;

        warnings.Add("Parking spot ItemIndex values are missing or not unique (likely a project saved before " +
            "ItemIndex was tracked) — renumbered sequentially instead. Taxi paths connecting to a parking spot " +
            "may not resolve to the correct one as a result; re-load this airport from SimConnect and " +
            "re-export to fix.");
        return Enumerable.Range(0, spots.Count).ToList();
    }

    private static XElement BuildTaxiwayParking(TaxiParkingSpot spot, int index, List<string> warnings)
    {
        return new XElement("TaxiwayParking",
            new XAttribute("index", index),
            new XAttribute("biasX", F(spot.BiasXMeters)),
            new XAttribute("biasZ", F(spot.BiasZMeters)),
            new XAttribute("heading", F(spot.HeadingDeg)),
            new XAttribute("radius", F(spot.RadiusMeters)),
            new XAttribute("type", MapParkingType(spot.Type, warnings)),
            new XAttribute("name", MapParkingName(spot.NameCode, warnings)),
            new XAttribute("number", spot.Number),
            MapParkingSuffix(spot.SuffixCode) is { } suffix ? new XAttribute("suffix", suffix) : null);
    }

    private static XElement BuildTaxiName(TaxiName name, int index, List<string> warnings)
    {
        var value = name.Value;
        if (value.Length > 8)
        {
            warnings.Add($"Taxi name \"{value}\" is longer than the XML format's 8-character limit — truncated to \"{value[..8]}\".");
            value = value[..8];
        }

        return new XElement("TaxiName", new XAttribute("index", index), new XAttribute("name", value));
    }

    // Root cause of the 2026-09-15 "Scenery Editor imports zero <TaxiwayPath>
    // elements" investigation (see requirements.md): every exported path
    // omitted `surface` entirely, because SimConnect's TAXI_PATH facility
    // data has no SURFACE field to read it from (confirmed against the SDK's
    // own AddToFacilityDefinition reference — TAXI_PATH exposes TYPE/WIDTH/
    // .../START/END/NAME_INDEX only). `surface` is schema-optional
    // (bglcomp.xsd's stSurface), so this was never caught by validation, but
    // a real Scenery-Editor-authored <TaxiwayPath> (obtained by manually
    // drawing a path in the Editor and inspecting its saved XML) always
    // carries one — strong evidence the Editor can't construct a taxiway
    // path object without a resolvable surface material and silently drops
    // the element instead of erroring. "ASPHALT" is the same plain-string
    // fallback MapSurface already uses for an unrecognized Runway surface
    // code, and plain names are confirmed working there (Runways already
    // import fine) — used here too rather than hardcoding the specific GUID
    // seen in that one sample, which could be a version-specific internal
    // material id rather than a stable public name.
    private const string DefaultTaxiwayPathSurface = "ASPHALT";

    // path is guaranteed resolved and mappable — Build already filtered
    // exportablePaths before calling this (see its comment for why that
    // filtering has to happen before <TaxiwayPoint> construction too).
    private static XElement BuildTaxiwayPath(TaxiPathSegment path, string xmlType, IReadOnlyDictionary<Guid, int> taxiNameIndices, List<string> warnings)
    {
        // Attribute order below matches a real, confirmed-importing
        // AirportSmith-exported <TaxiwayPath> the user inspected directly
        // (2026-09-16) — XML attribute order has no schema/parsing meaning,
        // but matching it keeps this output diffable against future
        // Scenery-Editor-authored samples. weightLimit wasn't emitted at all
        // before this: TAXI_PATH's WEIGHT field isn't requested/stored by
        // SimConnectService (TaxiPathSegment has no WeightLimit property), so
        // "0" (the schema/SDK-documented "no limit" default, also what the
        // confirmed sample itself used) is emitted as a fixed default, same
        // treatment as the drawSurface/etc. attributes below.
        var element = new XElement("TaxiwayPath",
            new XAttribute("type", xmlType),
            new XAttribute("surface", DefaultTaxiwayPathSurface),
            new XAttribute("centerLine", ToXmlBool(path.CenterLine)),
            new XAttribute("centerLineLighted", ToXmlBool(path.CenterLineLighted)),
            new XAttribute("leftEdgeLighted", ToXmlBool(path.LeftEdgeLighted)),
            new XAttribute("rightEdgeLighted", ToXmlBool(path.RightEdgeLighted)),
            new XAttribute("start", path.StartIndex),
            new XAttribute("end", path.EndIndex),
            new XAttribute("width", F(path.WidthMeters)),
            new XAttribute("weightLimit", 0),
            new XAttribute("leftEdge", MapEdgeType(path.LeftEdge)),
            new XAttribute("rightEdge", MapEdgeType(path.RightEdge)),
            // drawSurface/drawDetail/groundMerging/excludeVegetation* match
            // the values a real Scenery-Editor-authored <TaxiwayPath> carries
            // (see the DefaultTaxiwayPathSurface comment above for how that
            // sample was obtained) — also absent from TAXI_PATH's SimConnect
            // fields, so there's no per-path data to source these from
            // either; these are the Editor's own apparent defaults for a
            // freshly drawn taxiway path, not a per-airport setting.
            new XAttribute("drawSurface", "FALSE"),
            new XAttribute("drawDetail", "TRUE"),
            new XAttribute("groundMerging", "TRUE"),
            new XAttribute("excludeVegetationAround", "TRUE"),
            new XAttribute("excludeVegetationInside", "TRUE"));

        // name is documented as valid only when type is NOT "RUNWAY" (flagged
        // directly by the user against a real exported file, same source as
        // the number/designator restriction below, just the opposite
        // direction) — a RUNWAY-type path's identity comes from
        // number/designator (which runway it's associated with), not a taxi
        // name. Previously emitted unconditionally whenever TaxiNameId
        // happened to resolve, which put an invalid `name` attribute on
        // every RUNWAY-type path pointing at a (usually blank/shared)
        // TaxiName — the Scenery Editor's own validation for this case
        // wasn't confirmed directly, but per the project's own prior
        // "point not linked to a hold short" investigation, an invalid
        // attribute rejecting the whole <TaxiwayPath> element would silently
        // orphan any point ONLY reachable through it, matching exactly the
        // "point 0/12 shows unlinked" symptom reported against OIBK.
        if (xmlType != "RUNWAY" && path.TaxiNameId is { } id && taxiNameIndices.TryGetValue(id, out var nameIndex))
            element.Add(new XAttribute("name", nameIndex));
        else if (xmlType == "RUNWAY" && path.TaxiNameId is { } runwayNameId && taxiNameIndices.ContainsKey(runwayNameId))
            warnings.Add($"Taxi path {path.StartIndex}->{path.EndIndex} has type RUNWAY with a resolvable taxi " +
                "name — the XML format only allows `name` on non-RUNWAY paths, so this was left unset.");

        // number/designator are documented as valid ONLY when type="RUNWAY"
        // (confirmed against the SDK's taxiway-xml-properties doc, flagged
        // directly by the user against a real exported file) — previously
        // emitted here whenever RunwayNumber was in range regardless of
        // path type, which put an invalid `number` attribute on every
        // TAXI-type path SimConnect reports a runway association for (e.g.
        // an entrance/exit taxiway near a specific runway — a real, common
        // case, not an edge case). TAXI_PATH.RUNWAY_NUMBER's documented
        // range also covers 37-45 (compass headings for helipad-associated
        // paths, plus a LAST sentinel) — stRunwayNumber only accepts 0-36,
        // so those values have no valid XML encoding either way.
        if (xmlType == "RUNWAY" && path.RunwayNumber is >= 0 and <= 36)
        {
            element.Add(new XAttribute("number", path.RunwayNumber));
            var designator = MapRunwayDesignator(path.RunwayDesignator);
            if (designator != null) element.Add(new XAttribute("designator", designator));
        }
        else if (path.RunwayNumber != 0)
        {
            warnings.Add(xmlType == "RUNWAY"
                ? $"Taxi path {path.StartIndex}->{path.EndIndex}'s runway association (RunwayNumber " +
                    $"{path.RunwayNumber}) is outside the XML format's 0-36 range and was left unset."
                : $"Taxi path {path.StartIndex}->{path.EndIndex} has type {xmlType} with a runway association " +
                    $"(RunwayNumber {path.RunwayNumber}) — the XML format only allows number/designator on " +
                    "type=\"RUNWAY\" paths, so this was left unset.");
        }

        return element;
    }

    // ---- Enum/int -> XML string mapping ---------------------------------
    // Each mapped directly against bglcomp.xsd's st* simpleType enumerations.

    private static string MapLightIntensity(RunwayLightIntensity intensity) => intensity switch
    {
        RunwayLightIntensity.None => "NONE",
        RunwayLightIntensity.Low => "LOW",
        RunwayLightIntensity.Medium => "MEDIUM",
        RunwayLightIntensity.High => "HIGH",
        _ => "NONE",
    };

    private static string MapVasiType(VasiType type) => type switch
    {
        VasiType.Vasi21 => "VASI21",
        VasiType.Vasi22 => "VASI22",
        VasiType.Vasi23 => "VASI23",
        VasiType.Vasi31 => "VASI31",
        VasiType.Vasi32 => "VASI32",
        VasiType.Vasi33 => "VASI33",
        VasiType.Papi2 => "PAPI2",
        VasiType.Papi4 => "PAPI4",
        VasiType.TriColor => "TRICOLOR",
        VasiType.PVasi => "PVASI",
        VasiType.TVasi => "TVASI",
        VasiType.Ball => "BALL",
        VasiType.Apap => "APAP",
        _ => "PAPI4",
    };

    private static string MapApproachLightSystem(ApproachLightSystemType type) => type switch
    {
        ApproachLightSystemType.Odals => "ODALS",
        ApproachLightSystemType.Malsf => "MALSF",
        ApproachLightSystemType.Malsr => "MALSR",
        ApproachLightSystemType.Ssalf => "SSALF",
        ApproachLightSystemType.Ssalr => "SSALR",
        ApproachLightSystemType.Alsf1 => "ALSF1",
        ApproachLightSystemType.Alsf2 => "ALSF2",
        ApproachLightSystemType.Rail => "RAIL",
        ApproachLightSystemType.Calvert => "CALVERT",
        ApproachLightSystemType.Calvert2 => "CALVERT2",
        ApproachLightSystemType.Mals => "MALS",
        ApproachLightSystemType.Sals => "SALS",
        ApproachLightSystemType.Salsf => "SALSF",
        ApproachLightSystemType.Ssals => "SSALS",
        _ => "NONE",
    };

    private static string MapEdgeType(TaxiEdgeType type) => type switch
    {
        TaxiEdgeType.None => "NONE",
        TaxiEdgeType.Solid => "SOLID",
        TaxiEdgeType.Dashed => "DASHED",
        TaxiEdgeType.SolidDashed => "SOLID_DASHED",
        _ => "NONE",
    };

    private static string? MapRunwayDesignator(TaxiPathRunwayDesignator designator) => designator switch
    {
        TaxiPathRunwayDesignator.None => null,
        TaxiPathRunwayDesignator.Left => "LEFT",
        TaxiPathRunwayDesignator.Right => "RIGHT",
        TaxiPathRunwayDesignator.Center => "CENTER",
        TaxiPathRunwayDesignator.Water => "WATER",
        TaxiPathRunwayDesignator.A => "A",
        TaxiPathRunwayDesignator.B => "B",
        _ => null,
    };

    private static string MapTaxiPointType(TaxiPointType type) => type switch
    {
        TaxiPointType.Normal => "NORMAL",
        TaxiPointType.HoldShort => "HOLD_SHORT",
        TaxiPointType.IlsHoldShort => "ILS_HOLD_SHORT",
        TaxiPointType.HoldShortNoDraw => "HOLD_SHORT_NO_DRAW",
        TaxiPointType.IlsHoldShortNoDraw => "ILS_HOLD_SHORT_NO_DRAW",
        _ => "NORMAL",
    };

    private static bool IsHoldShortType(TaxiPointType? type) => type is
        TaxiPointType.HoldShort or TaxiPointType.IlsHoldShort or
        TaxiPointType.HoldShortNoDraw or TaxiPointType.IlsHoldShortNoDraw;

    private static string MapTaxiPointOrientation(TaxiPointOrientation orientation) => orientation switch
    {
        TaxiPointOrientation.Forward => "FORWARD",
        TaxiPointOrientation.Reverse => "REVERSE",
        _ => "FORWARD",
    };

    // Unlike the enums above, TaxiPathType.Unknown/.PaintedLine genuinely
    // have no equivalent in the schema's stTaxiwayPathType — null here means
    // "skip this path", not "default to something".
    // TaxiPathType.Path (SimConnect TAXI_PATH.TYPE == 4) deliberately maps to
    // XML type="TAXI", not the schema's own literal "PATH" value. Confirmed
    // against a real import (2026-09-16): a hand-authored comparison file
    // used to diagnose the "Scenery Editor imports zero <TaxiwayPath>
    // elements" bug (see requirements.md) imported cleanly with type="TAXI"
    // on every ordinary taxi route; this project's own export — which had
    // been emitting type="PATH" for exactly these rows, since that's what
    // SimConnect reports for effectively every real taxi path (see
    // TaxiPathType.cs's comment: the OIBK sample had Type == Path on all 479
    // rows, no TAXI rows observed at all) — kept failing to import even
    // after the surface/drawSurface/etc. fix above. Whatever "PATH" means in
    // `stTaxiwayPathType`, it isn't treated as part of the drivable taxi
    // network graph by the Scenery Editor's importer the way "TAXI" is.
    private static string? MapTaxiPathType(TaxiPathType type) => type switch
    {
        TaxiPathType.Taxi => "TAXI",
        TaxiPathType.Runway => "RUNWAY",
        TaxiPathType.Parking => "PARKING",
        TaxiPathType.Path => "TAXI",
        TaxiPathType.Closed => "CLOSED",
        TaxiPathType.Vehicle => "VEHICLE",
        TaxiPathType.Road => "ROAD",
        _ => null,
    };

    private static string MapParkingType(int typeCode, List<string> warnings)
    {
        if (AirportDataTreeBuilder.TaxiParkingTypeLabels.TryGetValue(typeCode, out var label))
            return label;
        warnings.Add($"Parking spot type code {typeCode} is unrecognized — defaulted to NONE.");
        return "NONE";
    }

    private static string MapParkingName(int nameCode, List<string> warnings)
    {
        if (AirportDataTreeBuilder.TaxiParkingNameLabels.TryGetValue(nameCode, out var label))
            return label;
        warnings.Add($"Parking spot name code {nameCode} is unrecognized — defaulted to NONE.");
        return "NONE";
    }

    // Unlike TAXI_PARKING's NAME field, the XML schema's stParkingSuffix only
    // accepts GATE_A..GATE_Z or NONE — codes 1-11 (PARKING/N_PARKING/DOCK/
    // etc, valid for NAME) have no valid *suffix* encoding, so they (and 0/
    // NONE) are left off the optional suffix attribute entirely rather than
    // emitting a value bglcomp would reject.
    private static string? MapParkingSuffix(int suffixCode) =>
        suffixCode is >= 12 and <= 37 && AirportDataTreeBuilder.TaxiParkingNameLabels.TryGetValue(suffixCode, out var label)
            ? label
            : null;

    // The MSFS 2024 SDK's own Facility Data reference documents RUNWAY.SURFACE
    // as "The return value will be one of the following:" and then gives no
    // list at all (confirmed absent from the local SDK docs) — the same real
    // documentation gap AirportDataTreeBuilder's own comment calls out for
    // why it doesn't attempt a label there either. This table is the
    // long-established SIMCONNECT_SURFACE enumeration used across FSX/P3D/
    // MSFS (unchanged for two decades, and consistent with this project's
    // own "0-32 pavement types, 254=UNKNOWN, 255=UNDEFINED" comment on
    // Runway.SurfaceType) — worth cross-checking against a live sim before
    // fully trusting, same as this project's other unconfirmed SimConnect
    // assumptions.
    private static readonly IReadOnlyDictionary<int, string> SurfaceLabels = new Dictionary<int, string>
    {
        [0] = "CONCRETE", [1] = "GRASS", [2] = "WATER", [3] = "GRASS_BUMPY", [4] = "ASPHALT",
        [5] = "SHORT_GRASS", [6] = "LONG_GRASS", [7] = "HARD_TURF", [8] = "SNOW", [9] = "ICE",
        [10] = "URBAN", [11] = "FOREST", [12] = "DIRT", [13] = "CORAL", [14] = "GRAVEL",
        [15] = "OIL_TREATED", [16] = "STEEL_MATS", [17] = "BITUMINOUS", [18] = "BRICK", [19] = "MACADAM",
        [20] = "PLANKS", [21] = "SAND", [22] = "SHALE", [23] = "TARMAC", [24] = "WRIGHT_FLYER_TRACK",
        [254] = "UNKNOWN", [255] = "UNDEFINED",
    };

    private static string MapSurface(int surfaceCode, List<string> warnings)
    {
        if (SurfaceLabels.TryGetValue(surfaceCode, out var label))
            return label;
        warnings.Add($"Runway surface code {surfaceCode} is unrecognized — defaulted to ASPHALT.");
        return "ASPHALT";
    }

    // ---- Small helpers ---------------------------------------------------

    private static string ToXmlBool(bool value) => value ? "TRUE" : "FALSE";

    // Default ToString() round-trips a double's shortest exact representation
    // on .NET Core 3.0+ (this project targets .NET 8) — no format specifier
    // needed, and "R" is discouraged/can throw on modern .NET.
    private static string F(double value) => value.ToString(CultureInfo.InvariantCulture);

    // Splits e.g. "09L" into (9, "LEFT"), "27" into (27, null), "36R" into
    // (36, "RIGHT") — AirportSmith stores Runway.Primary/SecondaryDesignation
    // as free strings in this format (not a separate number+designator
    // pair), matching what the extracted SDK data actually looks like.
    private static (int Number, string? Designator) ParseDesignation(string designation)
    {
        if (string.IsNullOrWhiteSpace(designation)) return (0, null);

        var trimmed = designation.Trim();
        var lastChar = trimmed[^1];
        var hasLetter = char.IsLetter(lastChar);
        var digits = hasLetter ? trimmed[..^1] : trimmed;
        var number = int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

        var designator = hasLetter ? char.ToUpperInvariant(lastChar) switch
        {
            'L' => "LEFT",
            'R' => "RIGHT",
            'C' => "CENTER",
            'W' => "WATER",
            _ => null,
        } : null;

        return (number, designator);
    }
}
