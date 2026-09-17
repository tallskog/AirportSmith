using AirportSmith.Models;
using AirportSmith.Models.Diagram;

namespace AirportSmith.Services;

// Pure computation (no I/O), so it's tested directly with plain xUnit facts
// rather than via the interface+fake pattern reserved for I/O boundaries
// like ISimConnectService.
//
// Coordinate approach: project runway lat/lon into the same local-meters
// plane TAXI_POINT/TAXI_PARKING's BIAS_X/BIAS_Z already use (a flat-earth/
// equirectangular approximation anchored on the airport reference point —
// accurate enough at airport scale), then normalize everything into
// canvas/screen-space meters as the final step. The Z-flip for north-up
// (screenY = maxZ - z) is a documented assumption pending live-sim
// confirmation of BIAS_X/BIAS_Z's actual axis convention — see
// TaxiPathSegment/TaxiParkingSpot. The runway-heading-to-threshold
// convention (which end is "primary") was corrected after live testing
// against OIBK showed it backwards — see the comment at threshold1/2 below.
//
// Displaced-threshold/blast-pad/overrun marking geometry (colors, threshold
// bar, demarcation bar, arrow, chevrons) follows FAA AIM 2-3-3 — see the
// link in requirements.md's reference list.
public static class AirportDiagramProjector
{
    private const double CanvasMarginMeters = 50;
    private const double MinCanvasSpanMeters = 100;

    // Approach-light-system length/spacing buckets, per FAA AIM 2-1-3's
    // categorization of the SDK's 14 SYSTEM types — round meter
    // approximations of the real ICAO/FAA standard lengths (~2400ft/1400ft/
    // 1500ft), not exact conversions, since this is a diagram-level
    // schematic (one simplified rail per category, not each type's literal
    // light layout) in the same spirit as the chevron/threshold-arrow
    // simplifications above.
    private const double FullApproachLightsLengthMeters = 730;   // ALSF-1/ALSF-2/MALSR/SSALR/RAIL/CALVERT/CALVERT2 (~2400ft)
    private const double ShortApproachLightsLengthMeters = 430;  // MALSF/SSALF/MALS/SALS/SALSF/SSALS (~1400ft)
    private const double SparseApproachLightsLengthMeters = 465; // ODALS (~1500ft)
    private const double ApproachLightsSpacingMeters = 30;       // full/short rail spacing (~100ft)
    private const double SparseApproachLightsSpacingMeters = 92; // ODALS's wider single-light spacing (~300ft)
    private const double RedCrossBarDistanceMeters = 300;        // ALSF-1/ALSF-2 red bar distance from threshold (~1000ft)
    private const double RedCrossBarHalfWidthMeters = 15;

    // Raw local-meters coordinate in the flat-earth tangent plane, before
    // normalization to canvas space. Not the public Point2D (screen-space)
    // type — kept private so callers can't confuse the two coordinate spaces.
    private readonly record struct LocalPoint(double X, double Z);

    // AIM 2-3-3: the displaced-threshold zone (still usable runway pavement,
    // just not for landing) is marked entirely in WHITE — a threshold bar at
    // the displaced threshold itself, and an arrow along the centerline
    // pointing at it. One arrow drawn, not the repeated arrowheads AIM's
    // figure shows across the full width — a diagram-level simplification.
    private sealed record ThresholdMarkingWorking(
        LocalPoint[] ZoneCorners,
        LocalPoint[] ThresholdBar,
        LocalPoint ArrowShaftStart,
        LocalPoint ArrowShaftEnd,
        LocalPoint[] ArrowHead);

    // AIM 2-3-3: a blast pad or overrun/stopway (pavement NOT usable for
    // landing/takeoff/taxi) is marked entirely in YELLOW — a demarcation bar
    // at the boundary with the runway, and chevrons within it. Chevron count
    // is capped/spaced by this code, not a literal reproduction of AIM's
    // figure spacing.
    private sealed record ExtensionWorking(
        LocalPoint[] Corners,
        LocalPoint[] DemarcationBar,
        LocalPoint[][] Chevrons);

    // A category's schematic rail of light dots extending outward from the
    // threshold, plus an optional red crossbar (empty when the category has
    // none) — see Categorize below for which SYSTEM values map where.
    private sealed record ApproachLightsWorking(
        LocalPoint[] RailLights,
        LocalPoint[] CrossBar);

    // Working data for one runway during projection, before the final
    // ToScreen pass.
    private sealed record RunwayWorkingData(
        Runway Runway,
        int SourceIndex,
        LocalPoint Threshold1,
        LocalPoint Threshold2,
        LocalPoint[] Corners,
        LocalPoint PrimaryLabelPosition,
        LocalPoint SecondaryLabelPosition,
        ThresholdMarkingWorking? PrimaryThresholdMarking,
        ExtensionWorking? PrimaryBlastPad,
        ExtensionWorking? PrimaryOverrun,
        ApproachLightsWorking? PrimaryApproachLights,
        ThresholdMarkingWorking? SecondaryThresholdMarking,
        ExtensionWorking? SecondaryBlastPad,
        ExtensionWorking? SecondaryOverrun,
        ApproachLightsWorking? SecondaryApproachLights);

    // FAA AIM 2-1-3 categorizes approach light systems by length and
    // whether they carry the red side-row barrettes ("decision bar") that
    // distinguish ALSF-1/ALSF-2 — this buckets the SDK's 14 SYSTEM values
    // into that categorization for the diagram's schematic rendering.
    // SystemType 0 (NONE) and any unrecognized value fall through to None.
    private enum ApproachLightCategory { None, Sparse, Short, Full, FullWithRedBar }

    private static ApproachLightCategory Categorize(ApproachLightSystemType systemType) => systemType switch
    {
        ApproachLightSystemType.Odals => ApproachLightCategory.Sparse,
        ApproachLightSystemType.Malsf or ApproachLightSystemType.Ssalf or ApproachLightSystemType.Mals
            or ApproachLightSystemType.Sals or ApproachLightSystemType.Salsf or ApproachLightSystemType.Ssals
            => ApproachLightCategory.Short,
        ApproachLightSystemType.Alsf1 or ApproachLightSystemType.Alsf2 => ApproachLightCategory.FullWithRedBar,
        ApproachLightSystemType.Malsr or ApproachLightSystemType.Ssalr or ApproachLightSystemType.Rail
            or ApproachLightSystemType.Calvert or ApproachLightSystemType.Calvert2
            => ApproachLightCategory.Full,
        _ => ApproachLightCategory.None,
    };

    public static AirportDiagram Project(AirportDetails airport)
    {
        LocalPoint ProjectLatLon(double lat, double lon)
        {
            var (x, z) = GeoProjection.ProjectLatLon(airport.Latitude, airport.Longitude, lat, lon);
            return new LocalPoint(x, z);
        }

        // Both endpoints must already be in the local-meters plane — builds
        // the 4 corners of a rectangle spanning between them, offset by
        // +/-halfWidthOffset on each end. Shared by every pavement rectangle
        // below (the runway itself, threshold bars, extensions, demarcation
        // bars).
        static LocalPoint[] BuildRectangle(LocalPoint a, LocalPoint b, (double X, double Z) halfWidthOffset) =>
        [
            new LocalPoint(a.X + halfWidthOffset.X, a.Z + halfWidthOffset.Z),
            new LocalPoint(b.X + halfWidthOffset.X, b.Z + halfWidthOffset.Z),
            new LocalPoint(b.X - halfWidthOffset.X, b.Z - halfWidthOffset.Z),
            new LocalPoint(a.X - halfWidthOffset.X, a.Z - halfWidthOffset.Z),
        ];

        var runways = new List<RunwayWorkingData>();
        for (var runwaySourceIndex = 0; runwaySourceIndex < airport.Runways.Count; runwaySourceIndex++)
        {
            var runway = airport.Runways[runwaySourceIndex];
            var center = ProjectLatLon(runway.Latitude, runway.Longitude);
            var headingRad = DegToRad(runway.HeadingDeg);
            var forward = (X: Math.Sin(headingRad), Z: Math.Cos(headingRad));
            var halfLength = runway.LengthMeters / 2;
            var halfWidth = runway.WidthMeters / 2;

            // HeadingDeg is the heading of travel when using the primary
            // end — i.e. the direction you roll after touching down at the
            // primary threshold (or depart toward). That means the primary
            // threshold itself sits at the -heading end of the runway (you
            // start there and travel +heading), not the +heading end. Only
            // affects which end a label points at on the diagram, not the
            // rectangle's shape.
            var threshold1 = new LocalPoint(center.X - forward.X * halfLength, center.Z - forward.Z * halfLength);
            var threshold2 = new LocalPoint(center.X + forward.X * halfLength, center.Z + forward.Z * halfLength);

            var right = (X: Math.Cos(headingRad), Z: -Math.Sin(headingRad));
            var widthOffset = (X: right.X * halfWidth, Z: right.Z * halfWidth);

            var corners = BuildRectangle(threshold1, threshold2, widthOffset);

            // Nudge each designation label inward from its threshold along
            // the centerline so it sits on the pavement, not the runway's
            // very edge. Capped at 40m so a short runway doesn't push both
            // labels past each other into the middle.
            var labelInset = Math.Min(halfLength * 0.3, 40);
            var primaryLabelPosition = new LocalPoint(threshold1.X + forward.X * labelInset, threshold1.Z + forward.Z * labelInset);
            var secondaryLabelPosition = new LocalPoint(threshold2.X - forward.X * labelInset, threshold2.Z - forward.Z * labelInset);

            // Displaced threshold: WITHIN the runway's own pavement, inset
            // from that end's threshold inward by LengthMeters. Uses the
            // runway's own width (widthOffset), not the feature's — AIM's
            // markings span the full runway.
            ThresholdMarkingWorking? ThresholdMarking(RunwayPavementFeature? feature, LocalPoint threshold, (double X, double Z) inwardDir)
            {
                if (feature is not { LengthMeters: > 0 } f) return null;

                var innerPoint = new LocalPoint(threshold.X + inwardDir.X * f.LengthMeters, threshold.Z + inwardDir.Z * f.LengthMeters);
                var zoneCorners = BuildRectangle(threshold, innerPoint, widthOffset);

                // 10ft (~3m) threshold bar, right at the displaced threshold.
                var barDepth = Math.Min(3, f.LengthMeters * 0.15);
                var barNearEdge = new LocalPoint(innerPoint.X - inwardDir.X * barDepth, innerPoint.Z - inwardDir.Z * barDepth);
                var thresholdBar = BuildRectangle(barNearEdge, innerPoint, widthOffset);

                // Centerline arrow, shaft starting a little in from the very
                // edge, head pointing at (just before) the bar.
                var shaftStart = new LocalPoint(threshold.X + inwardDir.X * f.LengthMeters * 0.15, threshold.Z + inwardDir.Z * f.LengthMeters * 0.15);
                var shaftEnd = barNearEdge;
                var headDepth = Math.Min(8, f.LengthMeters * 0.2);
                var headHalfWidth = halfWidth * 0.5;
                var headBase = new LocalPoint(shaftEnd.X - inwardDir.X * headDepth, shaftEnd.Z - inwardDir.Z * headDepth);
                var arrowHead = new[]
                {
                    shaftEnd,
                    new LocalPoint(headBase.X + right.X * headHalfWidth, headBase.Z + right.Z * headHalfWidth),
                    new LocalPoint(headBase.X - right.X * headHalfWidth, headBase.Z - right.Z * headHalfWidth),
                };

                return new ThresholdMarkingWorking(zoneCorners, thresholdBar, shaftStart, shaftEnd, arrowHead);
            }

            // Blast pad/overrun: pavement extending OUTWARD, beyond that
            // end's threshold — using the feature's own WidthMeters (falling
            // back to the runway's width if unset/zero), per the standard
            // apt.dat-style convention that both are attached to, and extend
            // away from, the specific end they're named after.
            ExtensionWorking? Extension(RunwayPavementFeature? feature, LocalPoint threshold, (double X, double Z) outwardDir)
            {
                if (feature is not { LengthMeters: > 0 } f) return null;

                var featureHalfWidth = (f.WidthMeters > 0 ? f.WidthMeters : runway.WidthMeters) / 2;
                var featureWidthOffset = (X: right.X * featureHalfWidth, Z: right.Z * featureHalfWidth);
                var outer = new LocalPoint(threshold.X + outwardDir.X * f.LengthMeters, threshold.Z + outwardDir.Z * f.LengthMeters);
                var extCorners = BuildRectangle(threshold, outer, featureWidthOffset);

                // 3ft (~1m) demarcation bar at the runway/extension boundary.
                var barDepth = Math.Min(1, f.LengthMeters * 0.1);
                var barFarEdge = new LocalPoint(threshold.X + outwardDir.X * barDepth, threshold.Z + outwardDir.Z * barDepth);
                var demarcationBar = BuildRectangle(threshold, barFarEdge, featureWidthOffset);

                // AIM chevrons meet at a 90° tip and tile with no gap — the
                // tip of one chevron sits exactly where the previous
                // chevron's arm-ends are. A 90° vertex angle means each arm
                // makes a 45° angle with the centerline, which requires
                // armHalfWidth == chevronDepth (opposite/adjacent legs equal
                // in a 45-45-90 triangle); the tile period is then just
                // chevronDepth, so consecutive chevrons directly adjoin.
                // Capped at the feature's own LengthMeters so a short, wide
                // extension (e.g. a stubby overrun on a wide runway) gets a
                // proportioned chevron that fits within its own footprint,
                // rather than one sized off the width alone and overflowing
                // past the actual pavement.
                // The upper clamp is only a safety ceiling against corrupt/
                // garbage data (e.g. a bogus multi-kilometer LengthMeters) —
                // it must stay far above any real pavement's chevron count,
                // since chevronDepth is fixed (derived from width, not
                // length): an earlier low cap here just left the rest of a
                // long extension blank instead of scaling anything, which is
                // exactly the bug the user reported on OIBK's blast pads.
                var armHalfWidth = Math.Min(featureHalfWidth * 0.85, f.LengthMeters);
                var chevronDepth = armHalfWidth;
                var chevronCount = Math.Clamp((int)(f.LengthMeters / chevronDepth), 1, 500);
                var chevrons = new LocalPoint[chevronCount][];
                for (var i = 0; i < chevronCount; i++)
                {
                    var vertexDist = i * chevronDepth;
                    var armDist = (i + 1) * chevronDepth;
                    var vertex = new LocalPoint(threshold.X + outwardDir.X * vertexDist, threshold.Z + outwardDir.Z * vertexDist);
                    var armBase = new LocalPoint(threshold.X + outwardDir.X * armDist, threshold.Z + outwardDir.Z * armDist);
                    chevrons[i] =
                    [
                        new LocalPoint(armBase.X + right.X * armHalfWidth, armBase.Z + right.Z * armHalfWidth),
                        vertex,
                        new LocalPoint(armBase.X - right.X * armHalfWidth, armBase.Z - right.Z * armHalfWidth),
                    ];
                }

                return new ExtensionWorking(extCorners, demarcationBar, chevrons);
            }

            // Approach lights sit OUTWARD from the threshold, along the
            // extended centerline opposite the runway itself — the
            // direction landing traffic approaches from, same "outward"
            // direction Extension uses for blast pads/overruns.
            ApproachLightsWorking? ApproachLights(ApproachLightSystem? feature, LocalPoint threshold, (double X, double Z) outwardDir)
            {
                if (feature is null) return null;
                var category = Categorize(feature.SystemType);
                if (category == ApproachLightCategory.None) return null;

                var (length, spacing) = category switch
                {
                    ApproachLightCategory.Sparse => (SparseApproachLightsLengthMeters, SparseApproachLightsSpacingMeters),
                    ApproachLightCategory.Short => (ShortApproachLightsLengthMeters, ApproachLightsSpacingMeters),
                    _ => (FullApproachLightsLengthMeters, ApproachLightsSpacingMeters),
                };

                var lightCount = (int)(length / spacing) + 1;
                var railLights = new LocalPoint[lightCount];
                for (var i = 0; i < lightCount; i++)
                {
                    var dist = i * spacing;
                    railLights[i] = new LocalPoint(threshold.X + outwardDir.X * dist, threshold.Z + outwardDir.Z * dist);
                }

                LocalPoint[] crossBar = [];
                if (category == ApproachLightCategory.FullWithRedBar && length > RedCrossBarDistanceMeters)
                {
                    var barCenter = new LocalPoint(threshold.X + outwardDir.X * RedCrossBarDistanceMeters, threshold.Z + outwardDir.Z * RedCrossBarDistanceMeters);
                    crossBar =
                    [
                        new LocalPoint(barCenter.X + right.X * RedCrossBarHalfWidthMeters, barCenter.Z + right.Z * RedCrossBarHalfWidthMeters),
                        new LocalPoint(barCenter.X - right.X * RedCrossBarHalfWidthMeters, barCenter.Z - right.Z * RedCrossBarHalfWidthMeters),
                    ];
                }

                return new ApproachLightsWorking(railLights, crossBar);
            }

            runways.Add(new RunwayWorkingData(
                runway, runwaySourceIndex, threshold1, threshold2, corners, primaryLabelPosition, secondaryLabelPosition,
                PrimaryThresholdMarking: ThresholdMarking(runway.PrimaryThreshold, threshold1, forward),
                PrimaryBlastPad: Extension(runway.PrimaryBlastPad, threshold1, (-forward.X, -forward.Z)),
                PrimaryOverrun: Extension(runway.PrimaryOverrun, threshold1, (-forward.X, -forward.Z)),
                PrimaryApproachLights: ApproachLights(runway.PrimaryApproachLights, threshold1, (-forward.X, -forward.Z)),
                SecondaryThresholdMarking: ThresholdMarking(runway.SecondaryThreshold, threshold2, (-forward.X, -forward.Z)),
                SecondaryBlastPad: Extension(runway.SecondaryBlastPad, threshold2, forward),
                SecondaryOverrun: Extension(runway.SecondaryOverrun, threshold2, forward),
                SecondaryApproachLights: ApproachLights(runway.SecondaryApproachLights, threshold2, forward)));
        }

        var taxiways = new List<(TaxiPathSegment Segment, int SourceIndex, LocalPoint Start, LocalPoint End, LocalPoint[] WidthCorners, LocalPoint MidPoint)>();
        for (var sourceIndex = 0; sourceIndex < airport.TaxiPaths.Count; sourceIndex++)
        {
            var segment = airport.TaxiPaths[sourceIndex];
            var type = (TaxiPathType)segment.Type;
            // Runway included alongside Taxi/Path so a point only reachable
            // via a runway entrance/exit stub still renders as visibly
            // connected — see TaxiwaySegmentShape.IsRunwayType's own doc
            // comment for why this matters (a real OIBK anomaly this was
            // fixed for). Parking stays excluded: its End doesn't reference
            // a taxi point at all (see BuildTaxiwayPoints below), so there's
            // no sensible line endpoint to draw for it, and its connection
            // to the network is already shown via the ParkingSpot marker.
            if (type is not (TaxiPathType.Taxi or TaxiPathType.Path or TaxiPathType.Runway))
                continue;
            if (segment.StartXMeters is not { } sx || segment.StartZMeters is not { } sz ||
                segment.EndXMeters is not { } ex || segment.EndZMeters is not { } ez)
                continue;

            var start = new LocalPoint(sx, sz);
            var end = new LocalPoint(ex, ez);
            var midPoint = new LocalPoint((sx + ex) / 2, (sz + ez) / 2);

            // Pavement footprint band from TaxiPathSegment.WidthMeters — the
            // centerline (Start/End) stays as-is for the existing
            // named/unnamed line styling. Perpendicular is the segment
            // direction rotated 90°; a zero-length segment (degenerate
            // Start == End) falls back to an arbitrary perpendicular since
            // there's no direction to rotate — it'll render as a point either way.
            var dx = ex - sx;
            var dz = ez - sz;
            var length = Math.Sqrt(dx * dx + dz * dz);
            var (perpX, perpZ) = length > 0 ? (-dz / length, dx / length) : (1.0, 0.0);
            var halfWidth = segment.WidthMeters / 2;
            var widthCorners = new[]
            {
                new LocalPoint(start.X + perpX * halfWidth, start.Z + perpZ * halfWidth),
                new LocalPoint(end.X + perpX * halfWidth, end.Z + perpZ * halfWidth),
                new LocalPoint(end.X - perpX * halfWidth, end.Z - perpZ * halfWidth),
                new LocalPoint(start.X - perpX * halfWidth, start.Z - perpZ * halfWidth),
            };

            taxiways.Add((segment, sourceIndex, start, end, widthCorners, midPoint));
        }

        // Same distinct-index point synthesis AirportXmlExporter.BuildTaxiwayPoints
        // uses (every path's Start, plus its End unless the path is
        // Type==Parking — see that method's own comment for why) — kept as an
        // independent copy rather than a shared helper since the exporter
        // operates on its own already-filtered "will actually be exported"
        // path list and returns XML elements, not diagram shapes. Deliberately
        // NOT filtered to TaxiPathType.Taxi/.Path like the taxiwayS segment
        // loop above: a point can only be usefully sanity-checked on the
        // diagram (see requirements.md's open RUNWAY-type-path investigation)
        // if it's shown regardless of which path type resolved it.
        var taxiwayPointsByIndex = new Dictionary<int, LocalPoint>();
        foreach (var segment in airport.TaxiPaths)
        {
            if (segment.StartXMeters is { } sx && segment.StartZMeters is { } sz)
                taxiwayPointsByIndex.TryAdd(segment.StartIndex, new LocalPoint(sx, sz));
            if (segment.Type != TaxiPathType.Parking && segment.EndXMeters is { } ex && segment.EndZMeters is { } ez)
                taxiwayPointsByIndex.TryAdd(segment.EndIndex, new LocalPoint(ex, ez));
        }

        var parkingSpots = new List<(TaxiParkingSpot Spot, LocalPoint Center, LocalPoint HeadingTip)>();
        foreach (var spot in airport.ParkingSpots)
        {
            var center = new LocalPoint(spot.BiasXMeters, spot.BiasZMeters);
            var headingRad = DegToRad(spot.HeadingDeg);
            var tipDistance = spot.RadiusMeters * 1.5;
            var tip = new LocalPoint(
                center.X + Math.Sin(headingRad) * tipDistance,
                center.Z + Math.Cos(headingRad) * tipDistance);

            parkingSpots.Add((spot, center, tip));
        }

        var (minX, maxX, minZ, maxZ) = ComputeBounds(runways, taxiways, parkingSpots, taxiwayPointsByIndex.Values);

        Point2D ToScreen(LocalPoint p) => new(
            p.X - minX + CanvasMarginMeters,
            (maxZ - p.Z) + CanvasMarginMeters);

        RunwayThresholdMarkingShape? ToScreenMarking(ThresholdMarkingWorking? m) => m is null ? null : new RunwayThresholdMarkingShape(
            m.ZoneCorners.Select(ToScreen).ToList(),
            m.ThresholdBar.Select(ToScreen).ToList(),
            ToScreen(m.ArrowShaftStart),
            ToScreen(m.ArrowShaftEnd),
            m.ArrowHead.Select(ToScreen).ToList());

        RunwayPavementExtensionShape? ToScreenExtension(ExtensionWorking? e) => e is null ? null : new RunwayPavementExtensionShape(
            e.Corners.Select(ToScreen).ToList(),
            e.DemarcationBar.Select(ToScreen).ToList(),
            e.Chevrons.Select(c => (IReadOnlyList<Point2D>)c.Select(ToScreen).ToList()).ToList());

        ApproachLightSystemShape? ToScreenApproachLights(ApproachLightsWorking? a) => a is null ? null : new ApproachLightSystemShape(
            a.RailLights.Select(ToScreen).ToList(),
            a.CrossBar.Select(ToScreen).ToList());

        return new AirportDiagram
        {
            CanvasWidth = (maxX - minX) + 2 * CanvasMarginMeters,
            CanvasHeight = (maxZ - minZ) + 2 * CanvasMarginMeters,
            Runways = runways.Select(r => new RunwayShape
            {
                PrimaryDesignation = r.Runway.PrimaryDesignation,
                SecondaryDesignation = r.Runway.SecondaryDesignation,
                Threshold1 = ToScreen(r.Threshold1),
                Threshold2 = ToScreen(r.Threshold2),
                Corners = r.Corners.Select(ToScreen).ToList(),
                PrimaryLabelPosition = ToScreen(r.PrimaryLabelPosition),
                SecondaryLabelPosition = ToScreen(r.SecondaryLabelPosition),
                PrimaryFeatures = new RunwayEndFeatures(
                    ToScreenMarking(r.PrimaryThresholdMarking),
                    ToScreenExtension(r.PrimaryBlastPad),
                    ToScreenExtension(r.PrimaryOverrun),
                    ToScreenApproachLights(r.PrimaryApproachLights)),
                SecondaryFeatures = new RunwayEndFeatures(
                    ToScreenMarking(r.SecondaryThresholdMarking),
                    ToScreenExtension(r.SecondaryBlastPad),
                    ToScreenExtension(r.SecondaryOverrun),
                    ToScreenApproachLights(r.SecondaryApproachLights)),
                SourceIndex = r.SourceIndex,
            }).ToList(),
            TaxiwaySegments = taxiways.Select(t =>
            {
                var name = ResolveTaxiName(airport, t.Segment.TaxiNameId);
                return new TaxiwaySegmentShape
                {
                    Start = ToScreen(t.Start),
                    End = ToScreen(t.End),
                    HasName = !string.IsNullOrWhiteSpace(name),
                    Name = name,
                    WidthCorners = t.WidthCorners.Select(ToScreen).ToList(),
                    MidPoint = ToScreen(t.MidPoint),
                    SourceIndex = t.SourceIndex,
                    IsRunwayType = t.Segment.Type == TaxiPathType.Runway,
                };
            }).ToList(),
            ParkingSpots = parkingSpots.Select(p => new ParkingSpotShape(
                ToScreen(p.Center),
                p.Spot.RadiusMeters,
                ToScreen(p.HeadingTip))).ToList(),
            TaxiwayPoints = taxiwayPointsByIndex
                .OrderBy(kvp => kvp.Key)
                .Select(kvp => new TaxiwayPointShape { Center = ToScreen(kvp.Value), Index = kvp.Key })
                .ToList(),
        };
    }

    // TaxiPathSegment only carries a TaxiNameId (a reference into
    // AirportDetails.TaxiNames, itself keyed by a stable Guid rather than
    // array position so renaming/deleting a name never silently misaligns
    // other segments' references) — resolving it to the actual display
    // string needs the parent AirportDetails, unlike this project's other
    // enum-backed fields that render fine on their own.
    private static string ResolveTaxiName(AirportDetails airport, Guid? taxiNameId) =>
        taxiNameId is { } id ? airport.TaxiNames.FirstOrDefault(n => n.Id == id)?.Value ?? string.Empty : string.Empty;

    private static (double MinX, double MaxX, double MinZ, double MaxZ) ComputeBounds(
        List<RunwayWorkingData> runways,
        List<(TaxiPathSegment Segment, int SourceIndex, LocalPoint Start, LocalPoint End, LocalPoint[] WidthCorners, LocalPoint MidPoint)> taxiways,
        List<(TaxiParkingSpot Spot, LocalPoint Center, LocalPoint HeadingTip)> parkingSpots,
        IEnumerable<LocalPoint> taxiwayPoints)
    {
        var points = new List<LocalPoint>();

        static void AddMarking(List<LocalPoint> points, ThresholdMarkingWorking? m)
        {
            if (m is null) return;
            points.AddRange(m.ZoneCorners);
            points.AddRange(m.ThresholdBar);
            points.Add(m.ArrowShaftStart);
            points.Add(m.ArrowShaftEnd);
            points.AddRange(m.ArrowHead);
        }

        static void AddExtension(List<LocalPoint> points, ExtensionWorking? e)
        {
            if (e is null) return;
            points.AddRange(e.Corners);
            points.AddRange(e.DemarcationBar);
            foreach (var chevron in e.Chevrons)
                points.AddRange(chevron);
        }

        static void AddApproachLights(List<LocalPoint> points, ApproachLightsWorking? a)
        {
            if (a is null) return;
            points.AddRange(a.RailLights);
            points.AddRange(a.CrossBar);
        }

        foreach (var r in runways)
        {
            points.AddRange(r.Corners);
            AddMarking(points, r.PrimaryThresholdMarking);
            AddExtension(points, r.PrimaryBlastPad);
            AddExtension(points, r.PrimaryOverrun);
            AddApproachLights(points, r.PrimaryApproachLights);
            AddMarking(points, r.SecondaryThresholdMarking);
            AddExtension(points, r.SecondaryBlastPad);
            AddExtension(points, r.SecondaryOverrun);
            AddApproachLights(points, r.SecondaryApproachLights);
        }
        foreach (var t in taxiways)
            points.AddRange(t.WidthCorners);
        // Ensures a point only resolved via a path type the taxiway-segment
        // loop above excludes (e.g. RUNWAY) still expands the canvas to cover
        // it, rather than landing off-screen — see this method's caller for
        // why taxiwayPointsByIndex isn't filtered by path type.
        points.AddRange(taxiwayPoints);
        foreach (var p in parkingSpots)
        {
            var radius = p.Spot.RadiusMeters;
            points.Add(new LocalPoint(p.Center.X - radius, p.Center.Z - radius));
            points.Add(new LocalPoint(p.Center.X + radius, p.Center.Z + radius));
        }

        double minX = 0, maxX = 0, minZ = 0, maxZ = 0;
        if (points.Count > 0)
        {
            minX = points.Min(p => p.X);
            maxX = points.Max(p => p.X);
            minZ = points.Min(p => p.Z);
            maxZ = points.Max(p => p.Z);
        }

        if (maxX - minX < MinCanvasSpanMeters)
        {
            var cx = (minX + maxX) / 2;
            minX = cx - MinCanvasSpanMeters / 2;
            maxX = cx + MinCanvasSpanMeters / 2;
        }
        if (maxZ - minZ < MinCanvasSpanMeters)
        {
            var cz = (minZ + maxZ) / 2;
            minZ = cz - MinCanvasSpanMeters / 2;
            maxZ = cz + MinCanvasSpanMeters / 2;
        }

        return (minX, maxX, minZ, maxZ);
    }

    private static double DegToRad(double deg) => deg * Math.PI / 180;
}
