using System.Globalization;
using System.IO;
using System.Xml.Linq;
using AirportSmith.Models;
using AirportSmith.Services;

namespace AirportSmith.Tests.Services;

public class AirportXmlExporterTests
{
    // AirportXmlExporter formats every numeric attribute with
    // CultureInfo.InvariantCulture (dot decimal separator) regardless of the
    // machine's locale — parse the same way here rather than with the
    // ambient culture, which uses a comma separator on some machines.
    private static double ParseD(string value) => double.Parse(value, CultureInfo.InvariantCulture);

    private static AirportDetails Airport(params Action<AirportDetails>[] configure)
    {
        var airport = new AirportDetails
        {
            Icao = "TEST",
            Name = "Test Airport",
            Latitude = 47.4502,
            Longitude = 8.5616,
            ElevationMeters = 432,
            MagneticVariationDeg = 2.1,
        };
        foreach (var c in configure) c(airport);
        return airport;
    }

    private static Runway BareRunway() => new()
    {
        PrimaryDesignation = "09L",
        SecondaryDesignation = "27R",
        Latitude = 47.4502,
        Longitude = 8.5616,
        ElevationMeters = 432,
        HeadingDeg = 90,
        LengthMeters = 2000,
        WidthMeters = 45,
        SurfaceType = 4, // ASPHALT
        EdgeLightIntensity = RunwayLightIntensity.Medium,
    };

    [Fact]
    public void Build_BasicAirport_RootAndAirportAttributesMatch()
    {
        var airport = Airport();

        var result = AirportXmlExporter.Build(airport);

        Assert.Equal("FSData", result.Document.Root!.Name);
        Assert.Equal("9.0", result.Document.Root.Attribute("version")!.Value);

        var airportElement = result.Document.Root.Element("Airport")!;
        Assert.Equal("TEST", airportElement.Attribute("ident")!.Value);
        Assert.Equal("Test Airport", airportElement.Attribute("name")!.Value);
        Assert.Equal(47.4502, ParseD(airportElement.Attribute("lat")!.Value), 6);
        Assert.Equal(8.5616, ParseD(airportElement.Attribute("lon")!.Value), 6);
        Assert.Equal(432, ParseD(airportElement.Attribute("alt")!.Value), 6);
        Assert.Equal(2.1, ParseD(airportElement.Attribute("magvar")!.Value), 6);
    }

    [Fact]
    public void Build_Always_DeleteAirportHasOnlyRunwaysAndTaxiwaysFlags()
    {
        var airport = Airport(a => a.Frequencies.Add(new Frequency { Type = 6, FrequencyHz = 118000000, Name = "TOWER" }));

        var deleteAirport = AirportXmlExporter.Build(airport).Document
            .Root!.Element("Airport")!.Element("DeleteAirport")!;

        Assert.Equal("TRUE", deleteAirport.Attribute("deleteAllRunways")!.Value);
        Assert.Equal("TRUE", deleteAirport.Attribute("deleteAllTaxiways")!.Value);
        Assert.Null(deleteAirport.Attribute("deleteAllFrequencies"));
        Assert.Null(deleteAirport.Attribute("deleteAllJetways"));
    }

    [Fact]
    public void Build_Always_NoJetwayOrComElementsEmitted()
    {
        var airport = Airport(a =>
        {
            a.Jetways.Add(new Jetway { ParkingIndex = 0, JetwayObjectId = 1, Status = 0 });
            a.Frequencies.Add(new Frequency { Type = 6, FrequencyHz = 118000000, Name = "TOWER" });
        });

        var airportElement = AirportXmlExporter.Build(airport).Document.Root!.Element("Airport")!;

        Assert.Empty(airportElement.Elements("Jetway"));
        Assert.Empty(airportElement.Elements("Com"));
    }

    [Fact]
    public void Build_FullyPopulatedRunway_MapsAttributesAndSubElementsWithCorrectEnumStrings()
    {
        var runway = BareRunway();
        runway.PrimaryThreshold = new RunwayPavementFeature(60, 45);
        runway.PrimaryBlastPad = new RunwayPavementFeature(30, 45);
        runway.PrimaryOverrun = new RunwayPavementFeature(20, 45);
        runway.SecondaryThreshold = new RunwayPavementFeature(10, 45);
        runway.SecondaryBlastPad = new RunwayPavementFeature(15, 45);
        runway.SecondaryOverrun = new RunwayPavementFeature(25, 45);
        runway.PrimaryApproachLights = new ApproachLightSystem(ApproachLightSystemType.Alsf2);
        runway.SecondaryApproachLights = new ApproachLightSystem(ApproachLightSystemType.Odals);
        runway.PrimaryLeftVasiType = VasiType.Papi4;
        runway.PrimaryLeftVasiAngleDeg = 3.0;
        runway.PrimaryLeftVasiBiasXMeters = -25;
        runway.PrimaryLeftVasiBiasZMeters = -300;
        runway.PrimaryLeftVasiSpacingMeters = 0;

        var airport = Airport(a => a.Runways.Add(runway));

        var runwayElement = AirportXmlExporter.Build(airport).Document
            .Root!.Element("Airport")!.Element("Runway")!;

        Assert.Equal("ASPHALT", runwayElement.Attribute("surface")!.Value);
        Assert.Equal("9", runwayElement.Attribute("number")!.Value);
        Assert.Equal("LEFT", runwayElement.Attribute("designator")!.Value);
        Assert.Equal("MEDIUM", runwayElement.Element("Lights")!.Attribute("edge")!.Value);

        var approachLights = runwayElement.Elements("ApproachLights").ToList();
        Assert.Equal(2, approachLights.Count);
        Assert.Equal("ALSF2", approachLights.Single(e => e.Attribute("end")!.Value == "PRIMARY").Attribute("system")!.Value);
        Assert.Equal("ODALS", approachLights.Single(e => e.Attribute("end")!.Value == "SECONDARY").Attribute("system")!.Value);

        var offsetThresholds = runwayElement.Elements("OffsetThreshold").ToList();
        var blastPads = runwayElement.Elements("BlastPad").ToList();
        var overruns = runwayElement.Elements("Overrun").ToList();
        Assert.Equal(2, offsetThresholds.Count);
        Assert.Equal(2, blastPads.Count);
        Assert.Equal(2, overruns.Count);
        Assert.Equal(60, ParseD(offsetThresholds.Single(e => e.Attribute("end")!.Value == "PRIMARY").Attribute("length")!.Value), 3);

        var vasi = Assert.Single(runwayElement.Elements("Vasi"));
        Assert.Equal("PRIMARY", vasi.Attribute("end")!.Value);
        Assert.Equal("LEFT", vasi.Attribute("side")!.Value);
        Assert.Equal("PAPI4", vasi.Attribute("type")!.Value);
        Assert.Equal(-25, ParseD(vasi.Attribute("biasX")!.Value), 3);
        Assert.Equal(-300, ParseD(vasi.Attribute("biasZ")!.Value), 3);
        Assert.Equal(3.0, ParseD(vasi.Attribute("pitch")!.Value), 3);

        Assert.Equal(2, runwayElement.Elements("RunwayStart").Count());
    }

    [Fact]
    public void Build_RunwayWithAllNullOptionalSlots_OmitsThoseElementsEntirely()
    {
        var airport = Airport(a => a.Runways.Add(BareRunway()));

        var runwayElement = AirportXmlExporter.Build(airport).Document
            .Root!.Element("Airport")!.Element("Runway")!;

        Assert.Empty(runwayElement.Elements("Vasi"));
        Assert.Empty(runwayElement.Elements("ApproachLights"));
        Assert.Empty(runwayElement.Elements("OffsetThreshold"));
        Assert.Empty(runwayElement.Elements("BlastPad"));
        Assert.Empty(runwayElement.Elements("Overrun"));
        Assert.Equal(2, runwayElement.Elements("RunwayStart").Count());
    }

    [Fact]
    public void Build_MissingVasiPosition_DefaultsToZeroAndWarns()
    {
        var runway = BareRunway();
        runway.PrimaryLeftVasiType = VasiType.Papi4;
        runway.PrimaryLeftVasiAngleDeg = 3.0;
        // BiasX/BiasZ/Spacing intentionally left null, as in a project saved
        // before this epic added them to extraction.
        var airport = Airport(a => a.Runways.Add(runway));

        var result = AirportXmlExporter.Build(airport);
        var vasi = result.Document.Root!.Element("Airport")!.Element("Runway")!.Element("Vasi")!;

        Assert.Equal(0, ParseD(vasi.Attribute("biasX")!.Value), 3);
        Assert.Contains(result.Warnings, w => w.Contains("VASI", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_RunwayStarts_ReprojectCloseToHandComputedThresholdPoints()
    {
        var runway = BareRunway(); // HeadingDeg = 90, LengthMeters = 2000
        var airport = Airport(a => a.Runways.Add(runway));

        var starts = AirportXmlExporter.Build(airport).Document
            .Root!.Element("Airport")!.Element("Runway")!.Elements("RunwayStart").ToList();

        var headingRad = runway.HeadingDeg * Math.PI / 180;
        var forward = (X: Math.Sin(headingRad), Z: Math.Cos(headingRad));
        var halfLength = runway.LengthMeters / 2;
        var expectedPrimary = (X: -forward.X * halfLength, Z: -forward.Z * halfLength);
        var expectedSecondary = (X: forward.X * halfLength, Z: forward.Z * halfLength);

        var primary = starts.Single(s => s.Attribute("end")!.Value == "PRIMARY");
        var secondary = starts.Single(s => s.Attribute("end")!.Value == "SECONDARY");

        var (px, pz) = GeoProjection.ProjectLatLon(runway.Latitude, runway.Longitude,
            ParseD(primary.Attribute("lat")!.Value), ParseD(primary.Attribute("lon")!.Value));
        var (sx, sz) = GeoProjection.ProjectLatLon(runway.Latitude, runway.Longitude,
            ParseD(secondary.Attribute("lat")!.Value), ParseD(secondary.Attribute("lon")!.Value));

        Assert.Equal(expectedPrimary.X, px, 3);
        Assert.Equal(expectedPrimary.Z, pz, 3);
        Assert.Equal(expectedSecondary.X, sx, 3);
        Assert.Equal(expectedSecondary.Z, sz, 3);
        Assert.Equal(90, ParseD(primary.Attribute("heading")!.Value), 3);
        Assert.Equal(270, ParseD(secondary.Attribute("heading")!.Value), 3);
    }

    [Fact]
    public void Build_TaxiwayElements_AppearInSchemaRequiredOrder()
    {
        var taxiName = new TaxiName { Value = "ALPHA" };
        var airport = Airport(a =>
        {
            a.TaxiNames.Add(taxiName);
            a.TaxiPaths.Add(new TaxiPathSegment
            {
                Type = TaxiPathType.Taxi,
                StartIndex = 0,
                EndIndex = 1,
                WidthMeters = 20,
                TaxiNameId = taxiName.Id,
                StartXMeters = 0,
                StartZMeters = 0,
                EndXMeters = 100,
                EndZMeters = 0,
            });
            a.ParkingSpots.Add(new TaxiParkingSpot { Number = 1, Type = 8, NameCode = 10, SuffixCode = 0, HeadingDeg = 90, RadiusMeters = 15, BiasXMeters = 50, BiasZMeters = 50 });
        });

        var airportElement = AirportXmlExporter.Build(airport).Document.Root!.Element("Airport")!;

        var order = airportElement.Elements()
            .Select(e => e.Name.LocalName)
            .Where(n => n is "TaxiwayPoint" or "TaxiwayParking" or "TaxiName" or "TaxiwayPath")
            .ToList();

        Assert.True(order.LastIndexOf("TaxiwayPoint") < order.IndexOf("TaxiwayParking"));
        Assert.True(order.IndexOf("TaxiwayParking") < order.IndexOf("TaxiName"));
        Assert.True(order.IndexOf("TaxiName") < order.IndexOf("TaxiwayPath"));
    }

    [Fact]
    public void Build_TaxiwayPoint_ReusesSegmentStartEndIndicesAndBiasCoordinates()
    {
        var airport = Airport(a => a.TaxiPaths.Add(new TaxiPathSegment
        {
            Type = TaxiPathType.Taxi,
            StartIndex = 5,
            EndIndex = 9,
            WidthMeters = 20,
            StartXMeters = 12.5,
            StartZMeters = -30,
            EndXMeters = 112.5,
            EndZMeters = -30,
        }));

        var points = AirportXmlExporter.Build(airport).Document
            .Root!.Element("Airport")!.Elements("TaxiwayPoint").ToList();

        Assert.Equal(2, points.Count);
        var start = points.Single(p => p.Attribute("index")!.Value == "5");
        Assert.Equal(12.5, ParseD(start.Attribute("biasX")!.Value), 3);
        Assert.Equal(-30, ParseD(start.Attribute("biasZ")!.Value), 3);
        Assert.Equal("NORMAL", start.Attribute("type")!.Value);
        Assert.NotNull(points.SingleOrDefault(p => p.Attribute("index")!.Value == "9"));
    }

    // Regression test for a real import failure: the MSFS 2024 SDK Scenery
    // Editor rejected the network with "point not linked to a hold short" /
    // "no hold short within 200m of runway" because every <TaxiwayPoint> was
    // hardcoded to type="NORMAL" — the underlying TAXI_POINT.TYPE was being
    // read from SimConnect and discarded rather than stored anywhere.
    [Fact]
    public void Build_TaxiwayPointWithHoldShortType_MapsTypeAndOrientation()
    {
        var airport = Airport(a => a.TaxiPaths.Add(new TaxiPathSegment
        {
            Type = TaxiPathType.Taxi,
            StartIndex = 0,
            EndIndex = 1,
            WidthMeters = 20,
            StartXMeters = 0,
            StartZMeters = 0,
            StartPointType = TaxiPointType.HoldShort,
            StartPointOrientation = TaxiPointOrientation.Reverse,
            EndXMeters = 50,
            EndZMeters = 0,
        }));

        var points = AirportXmlExporter.Build(airport).Document
            .Root!.Element("Airport")!.Elements("TaxiwayPoint").ToList();

        var holdShort = points.Single(p => p.Attribute("index")!.Value == "0");
        Assert.Equal("HOLD_SHORT", holdShort.Attribute("type")!.Value);
        Assert.Equal("REVERSE", holdShort.Attribute("orientation")!.Value);

        var normal = points.Single(p => p.Attribute("index")!.Value == "1");
        Assert.Equal("NORMAL", normal.Attribute("type")!.Value);
        Assert.Null(normal.Attribute("orientation"));
    }

    [Fact]
    public void Build_TaxiPathWithUnresolvedPoint_SkippedAndWarns()
    {
        var airport = Airport(a => a.TaxiPaths.Add(new TaxiPathSegment
        {
            Type = TaxiPathType.Taxi,
            StartIndex = 0,
            EndIndex = 1,
            WidthMeters = 20,
            StartXMeters = null, // unresolved
            EndXMeters = 10,
            EndZMeters = 10,
        }));

        var result = AirportXmlExporter.Build(airport);

        Assert.Empty(result.Document.Root!.Element("Airport")!.Elements("TaxiwayPath"));
        Assert.NotEmpty(result.Warnings);
    }

    // Regression test for a real import failure: the MSFS 2024 SDK Scenery
    // Editor flagged points as "not linked to the main graph". That happened
    // because a <TaxiwayPoint> used to be built for ANY path endpoint with a
    // resolved coordinate, even when that whole path was later dropped (e.g.
    // its other endpoint never resolved) — orphaning the resolved endpoint's
    // point with zero <TaxiwayPath> elements actually connecting to it.
    [Fact]
    public void Build_PathWithOneUnresolvedEnd_DoesNotEmitOrphanedTaxiwayPointForTheResolvedEnd()
    {
        var airport = Airport(a => a.TaxiPaths.Add(new TaxiPathSegment
        {
            Type = TaxiPathType.Taxi,
            StartIndex = 0, // resolved, but not shared by any other exportable path
            EndIndex = 1,   // unresolved
            WidthMeters = 20,
            StartXMeters = 5,
            StartZMeters = 5,
            EndXMeters = null,
            EndZMeters = null,
        }));

        var airportElement = AirportXmlExporter.Build(airport).Document.Root!.Element("Airport")!;

        Assert.Empty(airportElement.Elements("TaxiwayPoint"));
        Assert.Empty(airportElement.Elements("TaxiwayPath"));
    }

    [Fact]
    public void Build_MixOfExportableAndUnresolvedPaths_OnlyEmitsPointsUsedByExportedPaths()
    {
        var airport = Airport(a =>
        {
            // Fully resolved, exportable — both its points must be emitted.
            a.TaxiPaths.Add(new TaxiPathSegment
            {
                Type = TaxiPathType.Taxi,
                StartIndex = 0,
                EndIndex = 1,
                WidthMeters = 20,
                StartXMeters = 0,
                StartZMeters = 0,
                EndXMeters = 50,
                EndZMeters = 0,
            });
            // Shares index 1 with the path above (so 1 stays connected) but
            // its own other end (index 2) never resolved — the path itself
            // must be dropped, and index 2 must not appear at all.
            a.TaxiPaths.Add(new TaxiPathSegment
            {
                Type = TaxiPathType.Taxi,
                StartIndex = 1,
                EndIndex = 2,
                WidthMeters = 20,
                StartXMeters = 50,
                StartZMeters = 0,
                EndXMeters = null,
                EndZMeters = null,
            });
        });

        var airportElement = AirportXmlExporter.Build(airport).Document.Root!.Element("Airport")!;
        var pointIndices = airportElement.Elements("TaxiwayPoint").Select(p => p.Attribute("index")!.Value).ToList();

        Assert.Equal(["0", "1"], pointIndices.OrderBy(i => i));
        Assert.Single(airportElement.Elements("TaxiwayPath"));
    }

    [Theory]
    [InlineData(TaxiPathType.Unknown)]
    [InlineData(TaxiPathType.PaintedLine)]
    public void Build_TaxiPathWithUnmappableType_SkippedAndWarns(TaxiPathType type)
    {
        var airport = Airport(a => a.TaxiPaths.Add(new TaxiPathSegment
        {
            Type = type,
            StartIndex = 0,
            EndIndex = 1,
            WidthMeters = 20,
            StartXMeters = 0,
            StartZMeters = 0,
            EndXMeters = 10,
            EndZMeters = 10,
        }));

        var result = AirportXmlExporter.Build(airport);

        Assert.Empty(result.Document.Root!.Element("Airport")!.Elements("TaxiwayPath"));
        Assert.Contains(result.Warnings, w => w.Contains("no XML", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_MappableTaxiPath_ReferencesTaxiNameIndexAndEdgeAttributes()
    {
        var taxiName = new TaxiName { Value = "A" };
        var airport = Airport(a =>
        {
            a.TaxiNames.Add(taxiName);
            a.TaxiPaths.Add(new TaxiPathSegment
            {
                Type = TaxiPathType.Taxi,
                StartIndex = 0,
                EndIndex = 1,
                WidthMeters = 23,
                TaxiNameId = taxiName.Id,
                LeftEdge = TaxiEdgeType.SolidDashed,
                RightEdge = TaxiEdgeType.Solid,
                LeftEdgeLighted = true,
                StartXMeters = 0,
                StartZMeters = 0,
                EndXMeters = 50,
                EndZMeters = 0,
            });
        });

        var path = AirportXmlExporter.Build(airport).Document
            .Root!.Element("Airport")!.Elements("TaxiwayPath").Single();

        Assert.Equal("TAXI", path.Attribute("type")!.Value);
        Assert.Equal("0", path.Attribute("name")!.Value);
        Assert.Equal("SOLID_DASHED", path.Attribute("leftEdge")!.Value);
        Assert.Equal("SOLID", path.Attribute("rightEdge")!.Value);
        Assert.Equal("TRUE", path.Attribute("leftEdgeLighted")!.Value);
        Assert.Equal("FALSE", path.Attribute("rightEdgeLighted")!.Value);
    }

    // Regression test flagged by the user against a real exported file
    // (2026-09-16): number/designator are documented as valid only when
    // type="RUNWAY" — a TAXI-type path SimConnect reports a runway
    // association for (a common case: an entrance/exit taxiway near a
    // runway) must NOT get a `number` attribute, even though RunwayNumber
    // itself is populated and in range.
    [Fact]
    public void Build_TaxiTypePathWithRunwayAssociation_OmitsNumberAndDesignatorAndWarns()
    {
        var airport = Airport(a => a.TaxiPaths.Add(new TaxiPathSegment
        {
            Type = TaxiPathType.Taxi,
            StartIndex = 0,
            EndIndex = 1,
            WidthMeters = 20,
            RunwayNumber = 9,
            RunwayDesignator = TaxiPathRunwayDesignator.Left,
            StartXMeters = 0,
            StartZMeters = 0,
            EndXMeters = 50,
            EndZMeters = 0,
        }));

        var result = AirportXmlExporter.Build(airport);
        var path = result.Document.Root!.Element("Airport")!.Elements("TaxiwayPath").Single();

        Assert.Null(path.Attribute("number"));
        Assert.Null(path.Attribute("designator"));
        Assert.Contains(result.Warnings, w => w.Contains("TAXI", StringComparison.Ordinal) && w.Contains("RUNWAY", StringComparison.Ordinal));
    }

    // Counterpart: a RUNWAY-type path still gets number/designator, same as
    // before this fix.
    [Fact]
    public void Build_RunwayTypePathWithRunwayAssociation_IncludesNumberAndDesignator()
    {
        var airport = Airport(a => a.TaxiPaths.Add(new TaxiPathSegment
        {
            Type = TaxiPathType.Runway,
            StartIndex = 0,
            EndIndex = 1,
            WidthMeters = 30,
            RunwayNumber = 9,
            RunwayDesignator = TaxiPathRunwayDesignator.Left,
            StartXMeters = 0,
            StartZMeters = 0,
            EndXMeters = 50,
            EndZMeters = 0,
        }));

        var path = AirportXmlExporter.Build(airport).Document
            .Root!.Element("Airport")!.Elements("TaxiwayPath").Single();

        Assert.Equal("9", path.Attribute("number")!.Value);
        Assert.Equal("LEFT", path.Attribute("designator")!.Value);
    }

    // Regression test for the 2026-09-15/16 "Scenery Editor imports zero
    // <TaxiwayPath> elements" investigation (see requirements.md): a real
    // Editor-authored <TaxiwayPath> always carries a `surface` attribute
    // (plus drawSurface/drawDetail/groundMerging/excludeVegetation*), which
    // this project's export previously omitted entirely since SimConnect's
    // TAXI_PATH has no SURFACE field to source it from.
    [Fact]
    public void Build_MappableTaxiPath_HasSurfaceAndSceneryEditorDefaultAttributes()
    {
        var airport = Airport(a => a.TaxiPaths.Add(new TaxiPathSegment
        {
            Type = TaxiPathType.Taxi,
            StartIndex = 0,
            EndIndex = 1,
            WidthMeters = 20,
            StartXMeters = 0,
            StartZMeters = 0,
            EndXMeters = 50,
            EndZMeters = 0,
        }));

        var result = AirportXmlExporter.Build(airport);
        var path = result.Document.Root!.Element("Airport")!.Elements("TaxiwayPath").Single();

        Assert.Equal("ASPHALT", path.Attribute("surface")!.Value);
        Assert.Equal("FALSE", path.Attribute("drawSurface")!.Value);
        Assert.Equal("TRUE", path.Attribute("drawDetail")!.Value);
        Assert.Equal("TRUE", path.Attribute("groundMerging")!.Value);
        Assert.Equal("TRUE", path.Attribute("excludeVegetationAround")!.Value);
        Assert.Equal("TRUE", path.Attribute("excludeVegetationInside")!.Value);
        Assert.Equal("0", path.Attribute("weightLimit")!.Value);
        Assert.Contains(result.Warnings, w => w.Contains("surface", StringComparison.OrdinalIgnoreCase));
    }

    // Regression test for the 2026-09-16 follow-up to the "Scenery Editor
    // imports zero <TaxiwayPath> elements" investigation (see
    // requirements.md): SimConnect reports TYPE == Path (4) for effectively
    // every real taxi path, but a real import only succeeded once the export
    // mapped that to XML type="TAXI" instead of the schema's own literal
    // "PATH" value.
    [Fact]
    public void Build_TaxiPathWithPathType_MapsToTaxiXmlType()
    {
        var airport = Airport(a => a.TaxiPaths.Add(new TaxiPathSegment
        {
            Type = TaxiPathType.Path,
            StartIndex = 0,
            EndIndex = 1,
            WidthMeters = 20,
            StartXMeters = 0,
            StartZMeters = 0,
            EndXMeters = 50,
            EndZMeters = 0,
        }));

        var path = AirportXmlExporter.Build(airport).Document
            .Root!.Element("Airport")!.Elements("TaxiwayPath").Single();

        Assert.Equal("TAXI", path.Attribute("type")!.Value);
    }

    [Fact]
    public void Build_TaxiNameOver8Characters_TruncatesAndWarns()
    {
        var taxiName = new TaxiName { Value = "TAXIWAYALPHA" };
        var airport = Airport(a => a.TaxiNames.Add(taxiName));

        var result = AirportXmlExporter.Build(airport);
        var nameElement = result.Document.Root!.Element("Airport")!.Elements("TaxiName").Single();

        Assert.Equal("TAXIWAYA", nameElement.Attribute("name")!.Value);
        Assert.True(nameElement.Attribute("name")!.Value.Length <= 8);
        Assert.Contains(result.Warnings, w => w.Contains("TAXIWAYALPHA"));
    }

    [Fact]
    public void Build_KnownParkingCodes_MapToDocumentedXmlStrings()
    {
        var airport = Airport(a => a.ParkingSpots.Add(new TaxiParkingSpot
        {
            Number = 12,
            Type = 8, // GATE_SMALL
            NameCode = 14, // GATE_C (12=GATE_A, 13=GATE_B, 14=GATE_C)
            SuffixCode = 14, // GATE_C, valid stParkingSuffix
            HeadingDeg = 180,
            RadiusMeters = 20,
            BiasXMeters = 10,
            BiasZMeters = -10,
        }));

        var parking = AirportXmlExporter.Build(airport).Document
            .Root!.Element("Airport")!.Elements("TaxiwayParking").Single();

        Assert.Equal("GATE_SMALL", parking.Attribute("type")!.Value);
        Assert.Equal("GATE_C", parking.Attribute("name")!.Value);
        Assert.Equal("GATE_C", parking.Attribute("suffix")!.Value);
        Assert.Equal("12", parking.Attribute("number")!.Value);
    }

    // Regression test for a real import failure: TAXI_PATH.END for a
    // PARKING-type path references a TAXI_PARKING ItemIndex, not a
    // TAXI_POINT index (confirmed against real extracted airport data — see
    // TaxiParkingSpot.ItemIndex's comment). <TaxiwayParking> must be
    // exported with that same ItemIndex as its own `index` so the path's
    // `end` correctly resolves to it, and no redundant <TaxiwayPoint> should
    // be synthesized at that index.
    [Fact]
    public void Build_ParkingTypePathEnd_ResolvesToTaxiwayParkingNotASynthesizedTaxiwayPoint()
    {
        var airport = Airport(a =>
        {
            a.ParkingSpots.Add(new TaxiParkingSpot
            {
                ItemIndex = 7,
                Number = 3,
                Type = 8,
                NameCode = 10,
                SuffixCode = 0,
                HeadingDeg = 90,
                RadiusMeters = 15,
                BiasXMeters = 100,
                BiasZMeters = 50,
            });
            a.TaxiPaths.Add(new TaxiPathSegment
            {
                Type = TaxiPathType.Parking,
                StartIndex = 0,
                EndIndex = 7, // matches the parking spot's ItemIndex, not a TaxiwayPoint index
                WidthMeters = 15,
                StartXMeters = 90,
                StartZMeters = 40,
                // Coordinates as SimConnectService.ResolveTaxiPathPoints would now
                // resolve them: from the parking spot, not an unrelated taxi point.
                EndXMeters = 100,
                EndZMeters = 50,
            });
        });

        var airportElement = AirportXmlExporter.Build(airport).Document.Root!.Element("Airport")!;

        var parking = airportElement.Elements("TaxiwayParking").Single();
        Assert.Equal("7", parking.Attribute("index")!.Value);

        var path = airportElement.Elements("TaxiwayPath").Single();
        Assert.Equal("7", path.Attribute("end")!.Value);

        // No <TaxiwayPoint index="7"> — that index belongs to the parking
        // spot above, not a synthesized taxiway point.
        Assert.DoesNotContain(airportElement.Elements("TaxiwayPoint"), p => p.Attribute("index")!.Value == "7");
        // The Start end (a real taxiway point) is still emitted normally.
        Assert.Single(airportElement.Elements("TaxiwayPoint"), p => p.Attribute("index")!.Value == "0");
    }

    [Fact]
    public void Build_ParkingSpotItemIndicesCollide_FallsBackToSequentialNumberingAndWarns()
    {
        // Both default to ItemIndex 0 — the signature of a project saved
        // before ItemIndex was tracked.
        var airport = Airport(a =>
        {
            a.ParkingSpots.Add(new TaxiParkingSpot { Number = 1, HeadingDeg = 0, RadiusMeters = 10 });
            a.ParkingSpots.Add(new TaxiParkingSpot { Number = 2, HeadingDeg = 0, RadiusMeters = 10 });
        });

        var result = AirportXmlExporter.Build(airport);
        var indices = result.Document.Root!.Element("Airport")!.Elements("TaxiwayParking")
            .Select(p => p.Attribute("index")!.Value).ToList();

        Assert.Equal(["0", "1"], indices);
        Assert.Contains(result.Warnings, w => w.Contains("ItemIndex", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_ParkingSuffixCodeNotAGateLetter_OmitsSuffixAttribute()
    {
        var airport = Airport(a => a.ParkingSpots.Add(new TaxiParkingSpot
        {
            Number = 1,
            Type = 1,
            NameCode = 1, // "PARKING" — valid for name, NOT a valid stParkingSuffix value
            SuffixCode = 1,
            HeadingDeg = 0,
            RadiusMeters = 10,
        }));

        var parking = AirportXmlExporter.Build(airport).Document
            .Root!.Element("Airport")!.Elements("TaxiwayParking").Single();

        Assert.Null(parking.Attribute("suffix"));
    }

    [Fact]
    public void Build_UnmappedParkingCodes_FallToNoneAndWarns()
    {
        var airport = Airport(a => a.ParkingSpots.Add(new TaxiParkingSpot
        {
            Number = 1,
            Type = 999,
            NameCode = 999,
            SuffixCode = 0,
            HeadingDeg = 0,
            RadiusMeters = 10,
        }));

        var result = AirportXmlExporter.Build(airport);
        var parking = result.Document.Root!.Element("Airport")!.Elements("TaxiwayParking").Single();

        Assert.Equal("NONE", parking.Attribute("type")!.Value);
        Assert.Equal("NONE", parking.Attribute("name")!.Value);
        Assert.Equal(2, result.Warnings.Count(w => w.Contains("unrecognized", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Build_UnmappedSurfaceCode_FallsBackToAsphaltAndWarns()
    {
        var runway = BareRunway();
        runway.SurfaceType = 999;
        var airport = Airport(a => a.Runways.Add(runway));

        var result = AirportXmlExporter.Build(airport);
        var runwayElement = result.Document.Root!.Element("Airport")!.Element("Runway")!;

        Assert.Equal("ASPHALT", runwayElement.Attribute("surface")!.Value);
        Assert.Contains(result.Warnings, w => w.Contains("surface", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Export_WritesFileThatXDocumentCanReadBackWithSameStructure()
    {
        var airport = Airport(a => a.Runways.Add(BareRunway()));
        var exporter = new AirportXmlExporter();
        var path = Path.Combine(Path.GetTempPath(), $"AirportSmithTests_{Guid.NewGuid():N}.xml");

        try
        {
            var outcome = exporter.Export(airport, path);

            Assert.Equal(path, outcome.FilePath);
            Assert.True(File.Exists(path));

            var reloaded = XDocument.Load(path);
            Assert.Equal("FSData", reloaded.Root!.Name);
            Assert.NotNull(reloaded.Declaration);
            Assert.Equal("utf-8", reloaded.Declaration!.Encoding, ignoreCase: true);
            Assert.Single(reloaded.Root.Elements("Airport"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
