using AirportSmith.Models;
using AirportSmith.Services;

namespace AirportSmith.Tests.Services;

public class AirportDiagramProjectorTests
{
    private const int Precision = 3;

    private static AirportDetails Airport(params Action<AirportDetails>[] configure)
    {
        var airport = new AirportDetails { Latitude = 0, Longitude = 0 };
        foreach (var c in configure) c(airport);
        return airport;
    }

    [Fact]
    public void Project_SingleRunway_ThresholdsAndCornersMatchHandCalculatedCoordinates()
    {
        var airport = Airport(a => a.Runways.Add(new Runway
        {
            PrimaryDesignation = "09L",
            SecondaryDesignation = "27R",
            Latitude = 0,
            Longitude = 0,
            HeadingDeg = 0,
            LengthMeters = 1000,
            WidthMeters = 100,
        }));

        var diagram = AirportDiagramProjector.Project(airport);

        Assert.Equal(200, diagram.CanvasWidth, Precision);
        Assert.Equal(1100, diagram.CanvasHeight, Precision);

        var runway = Assert.Single(diagram.Runways);
        // HeadingDeg=0 means primary is used flying heading 0 (north), so the
        // primary threshold physically sits at the SOUTH end (you touch down
        // there and roll out heading north) — the higher-Y/lower-on-screen end.
        Assert.Equal(100, runway.Threshold1.X, Precision);
        Assert.Equal(1050, runway.Threshold1.Y, Precision);
        Assert.Equal(100, runway.Threshold2.X, Precision);
        Assert.Equal(50, runway.Threshold2.Y, Precision);

        Assert.Equal(4, runway.Corners.Count);
        Assert.Equal(150, runway.Corners[0].X, Precision);
        Assert.Equal(1050, runway.Corners[0].Y, Precision);
        Assert.Equal(150, runway.Corners[1].X, Precision);
        Assert.Equal(50, runway.Corners[1].Y, Precision);
        Assert.Equal(50, runway.Corners[2].X, Precision);
        Assert.Equal(50, runway.Corners[2].Y, Precision);
        Assert.Equal(50, runway.Corners[3].X, Precision);
        Assert.Equal(1050, runway.Corners[3].Y, Precision);

        Assert.Equal(100, runway.PrimaryLabelPosition.X, Precision);
        Assert.Equal(1010, runway.PrimaryLabelPosition.Y, Precision);
        Assert.Equal(100, runway.SecondaryLabelPosition.X, Precision);
        Assert.Equal(90, runway.SecondaryLabelPosition.Y, Precision);

        // No PrimaryThreshold/BlastPad/Overrun/ApproachLights (or Secondary
        // equivalents) set on this fixture — ENABLE==0 in the real API, all
        // should be absent.
        Assert.Null(runway.PrimaryFeatures.ThresholdMarking);
        Assert.Null(runway.PrimaryFeatures.BlastPad);
        Assert.Null(runway.PrimaryFeatures.Overrun);
        Assert.Null(runway.PrimaryFeatures.ApproachLights);
        Assert.Null(runway.SecondaryFeatures.ThresholdMarking);
        Assert.Null(runway.SecondaryFeatures.BlastPad);
        Assert.Null(runway.SecondaryFeatures.Overrun);
        Assert.Null(runway.SecondaryFeatures.ApproachLights);
    }

    [Fact]
    public void Project_Runway_PrimaryThresholdMarking_MatchesHandCalculatedCoordinates()
    {
        var airport = Airport(a => a.Runways.Add(new Runway
        {
            Latitude = 0,
            Longitude = 0,
            HeadingDeg = 0,
            LengthMeters = 1000,
            WidthMeters = 100,
            PrimaryThreshold = new RunwayPavementFeature(LengthMeters: 100, WidthMeters: 0),
        }));

        var diagram = AirportDiagramProjector.Project(airport);

        var runway = Assert.Single(diagram.Runways);
        // Threshold marking sits WITHIN the runway's own width (not the
        // feature's WidthMeters, which is irrelevant here), inset inward
        // from the primary threshold (the south end, per the heading=0
        // convention already established above). Per FAA AIM 2-3-3, this is
        // all WHITE: a zone overlay, a threshold bar at the displaced
        // threshold, and an arrow pointing at that bar.
        var marking = runway.PrimaryFeatures.ThresholdMarking;
        Assert.NotNull(marking);

        Assert.Equal(4, marking!.ZoneCorners.Count);
        Assert.Equal(150, marking.ZoneCorners[0].X, Precision);
        Assert.Equal(1050, marking.ZoneCorners[0].Y, Precision);
        Assert.Equal(150, marking.ZoneCorners[1].X, Precision);
        Assert.Equal(950, marking.ZoneCorners[1].Y, Precision);
        Assert.Equal(50, marking.ZoneCorners[2].X, Precision);
        Assert.Equal(950, marking.ZoneCorners[2].Y, Precision);
        Assert.Equal(50, marking.ZoneCorners[3].X, Precision);
        Assert.Equal(1050, marking.ZoneCorners[3].Y, Precision);

        // 10ft (~3m) threshold bar right at the displaced threshold line.
        Assert.Equal(4, marking.ThresholdBar.Count);
        Assert.Equal(150, marking.ThresholdBar[0].X, Precision);
        Assert.Equal(953, marking.ThresholdBar[0].Y, Precision);
        Assert.Equal(150, marking.ThresholdBar[1].X, Precision);
        Assert.Equal(950, marking.ThresholdBar[1].Y, Precision);
        Assert.Equal(50, marking.ThresholdBar[2].X, Precision);
        Assert.Equal(950, marking.ThresholdBar[2].Y, Precision);
        Assert.Equal(50, marking.ThresholdBar[3].X, Precision);
        Assert.Equal(953, marking.ThresholdBar[3].Y, Precision);

        Assert.Equal(100, marking.ArrowShaftStart.X, Precision);
        Assert.Equal(1035, marking.ArrowShaftStart.Y, Precision);
        Assert.Equal(100, marking.ArrowShaftEnd.X, Precision);
        Assert.Equal(953, marking.ArrowShaftEnd.Y, Precision);

        Assert.Equal(3, marking.ArrowHead.Count);
        Assert.Equal(100, marking.ArrowHead[0].X, Precision);
        Assert.Equal(953, marking.ArrowHead[0].Y, Precision);
        Assert.Equal(125, marking.ArrowHead[1].X, Precision);
        Assert.Equal(961, marking.ArrowHead[1].Y, Precision);
        Assert.Equal(75, marking.ArrowHead[2].X, Precision);
        Assert.Equal(961, marking.ArrowHead[2].Y, Precision);

        Assert.Null(runway.PrimaryFeatures.BlastPad);
        Assert.Null(runway.PrimaryFeatures.Overrun);
        Assert.Null(runway.SecondaryFeatures.ThresholdMarking);
    }

    [Fact]
    public void Project_Runway_PrimaryBlastPadAndOverrun_ExtendOutwardFromThreshold()
    {
        var airport = Airport(a => a.Runways.Add(new Runway
        {
            Latitude = 0,
            Longitude = 0,
            HeadingDeg = 0,
            LengthMeters = 1000,
            WidthMeters = 100,
            PrimaryBlastPad = new RunwayPavementFeature(LengthMeters: 30, WidthMeters: 20),
            // WidthMeters=0 exercises the fallback to the runway's own width.
            PrimaryOverrun = new RunwayPavementFeature(LengthMeters: 15, WidthMeters: 0),
        }));

        var diagram = AirportDiagramProjector.Project(airport);

        var runway = Assert.Single(diagram.Runways);
        // Per FAA AIM 2-3-3: the extension footprint and demarcation bar are
        // yellow; chevrons meet at a 90° tip with zero gap between them —
        // each chevron's armHalfWidth/depth is featureHalfWidth*0.85, capped
        // at the feature's own LengthMeters. Blast pad: featureHalfWidth=10,
        // depth=8.5, 3 chevrons fit in 30m. Overrun: featureHalfWidth=50
        // (fallback to runway width), but capped at LengthMeters=15, so
        // depth=15 and exactly 1 chevron fits, sized 15x15 — not spanning
        // the full runway-width fallback, since it doesn't fit at that size.
        var blastPad = runway.PrimaryFeatures.BlastPad;
        Assert.NotNull(blastPad);
        Assert.Equal(4, blastPad!.Corners.Count);
        Assert.Equal(110, blastPad.Corners[0].X, Precision);
        Assert.Equal(1050, blastPad.Corners[0].Y, Precision);
        Assert.Equal(110, blastPad.Corners[1].X, Precision);
        Assert.Equal(1080, blastPad.Corners[1].Y, Precision);
        Assert.Equal(90, blastPad.Corners[2].X, Precision);
        Assert.Equal(1080, blastPad.Corners[2].Y, Precision);
        Assert.Equal(90, blastPad.Corners[3].X, Precision);
        Assert.Equal(1050, blastPad.Corners[3].Y, Precision);

        Assert.Equal(4, blastPad.DemarcationBar.Count);
        Assert.Equal(110, blastPad.DemarcationBar[0].X, Precision);
        Assert.Equal(1050, blastPad.DemarcationBar[0].Y, Precision);
        Assert.Equal(110, blastPad.DemarcationBar[1].X, Precision);
        Assert.Equal(1051, blastPad.DemarcationBar[1].Y, Precision);

        Assert.Equal(3, blastPad.Chevrons.Count);
        Assert.Equal(3, blastPad.Chevrons[0].Count);
        // Chevron 0: vertex at the threshold itself (dist 0), arm-ends at dist
        // 8.5 — a 90° tip (armHalfWidth == depth == 8.5).
        Assert.Equal(108.5, blastPad.Chevrons[0][0].X, Precision);
        Assert.Equal(1058.5, blastPad.Chevrons[0][0].Y, Precision);
        Assert.Equal(100, blastPad.Chevrons[0][1].X, Precision);
        Assert.Equal(1050, blastPad.Chevrons[0][1].Y, Precision);
        Assert.Equal(91.5, blastPad.Chevrons[0][2].X, Precision);
        Assert.Equal(1058.5, blastPad.Chevrons[0][2].Y, Precision);
        // Chevron 1's vertex sits exactly at chevron 0's arm-end depth (no
        // gap) — same tip-adjacency check for chevron 2 against chevron 1.
        Assert.Equal(100, blastPad.Chevrons[1][1].X, Precision);
        Assert.Equal(1058.5, blastPad.Chevrons[1][1].Y, Precision);
        Assert.Equal(108.5, blastPad.Chevrons[1][0].X, Precision);
        Assert.Equal(1067, blastPad.Chevrons[1][0].Y, Precision);
        Assert.Equal(100, blastPad.Chevrons[2][1].X, Precision);
        Assert.Equal(1067, blastPad.Chevrons[2][1].Y, Precision);
        Assert.Equal(108.5, blastPad.Chevrons[2][0].X, Precision);
        Assert.Equal(1075.5, blastPad.Chevrons[2][0].Y, Precision);

        var overrun = runway.PrimaryFeatures.Overrun;
        Assert.NotNull(overrun);
        Assert.Equal(4, overrun!.Corners.Count);
        Assert.Equal(150, overrun.Corners[0].X, Precision);
        Assert.Equal(1050, overrun.Corners[0].Y, Precision);
        Assert.Equal(150, overrun.Corners[1].X, Precision);
        Assert.Equal(1065, overrun.Corners[1].Y, Precision);
        Assert.Equal(50, overrun.Corners[2].X, Precision);
        Assert.Equal(1065, overrun.Corners[2].Y, Precision);
        Assert.Equal(50, overrun.Corners[3].X, Precision);
        Assert.Equal(1050, overrun.Corners[3].Y, Precision);

        // Overrun's chevron is capped at LengthMeters=15 (not the runway-width
        // fallback's 42.5) — its arm-ends land exactly on the extension's own
        // far edge (Corners[1]/[2] above), not beyond it.
        Assert.Single(overrun.Chevrons);
        Assert.Equal(3, overrun.Chevrons[0].Count);
        Assert.Equal(115, overrun.Chevrons[0][0].X, Precision);
        Assert.Equal(1065, overrun.Chevrons[0][0].Y, Precision);
        Assert.Equal(100, overrun.Chevrons[0][1].X, Precision);
        Assert.Equal(1050, overrun.Chevrons[0][1].Y, Precision);
        Assert.Equal(85, overrun.Chevrons[0][2].X, Precision);
        Assert.Equal(1065, overrun.Chevrons[0][2].Y, Precision);

        Assert.Null(runway.PrimaryFeatures.ThresholdMarking);
    }

    [Fact]
    public void Project_Runway_LongBlastPad_ChevronsFillEntireLength_NotTruncatedByCountCap()
    {
        // Regression test: chevronDepth is fixed (derived from width, not
        // length), so an earlier low count-cap here just left the back
        // portion of a long extension blank instead of scaling anything —
        // exactly the bug reported against OIBK's blast pads. 68m at a 4.25m
        // chevron depth needs 16 chevrons, comfortably past the old cap of 10.
        var airport = Airport(a => a.Runways.Add(new Runway
        {
            Latitude = 0,
            Longitude = 0,
            HeadingDeg = 0,
            LengthMeters = 1000,
            WidthMeters = 100,
            PrimaryBlastPad = new RunwayPavementFeature(LengthMeters: 68, WidthMeters: 10),
        }));

        var diagram = AirportDiagramProjector.Project(airport);

        var blastPad = Assert.Single(diagram.Runways).PrimaryFeatures.BlastPad;
        Assert.NotNull(blastPad);
        Assert.Equal(16, blastPad!.Chevrons.Count);

        // The last chevron's arm-ends must land exactly on the extension's
        // own far edge (Corners[1]/[2]) — i.e. the chevrons actually reach
        // the end of the pavement, not stop partway through it.
        var lastChevron = blastPad.Chevrons[^1];
        Assert.Equal(blastPad.Corners[1].Y, lastChevron[0].Y, Precision);
        Assert.Equal(blastPad.Corners[2].Y, lastChevron[2].Y, Precision);
    }

    [Fact]
    public void Project_Runway_PrimaryApproachLights_Alsf2_RailAndRedCrossBar_MatchHandCalculatedCoordinates()
    {
        var airport = Airport(a => a.Runways.Add(new Runway
        {
            Latitude = 0,
            Longitude = 0,
            HeadingDeg = 0,
            LengthMeters = 1000,
            WidthMeters = 100,
            PrimaryApproachLights = new ApproachLightSystem(ApproachLightSystemType.Alsf2),
        }));

        var diagram = AirportDiagramProjector.Project(airport);

        // Approach lights extend the canvas south of the runway itself
        // (CanvasHeight grows from the base single-runway case's 1100 to
        // cover the 730m-long rail); CanvasWidth is unaffected since the
        // rail/crossbar stay within the runway's own X span.
        Assert.Equal(200, diagram.CanvasWidth, Precision);
        Assert.Equal(1820, diagram.CanvasHeight, Precision);

        var runway = Assert.Single(diagram.Runways);
        var lights = runway.PrimaryFeatures.ApproachLights;
        Assert.NotNull(lights);

        // Full-length (ALSF-2) category: 730m at 30m spacing = 25 rail
        // lights, starting at the threshold itself and ending 720m out.
        Assert.Equal(25, lights!.RailLights.Count);
        Assert.Equal(100, lights.RailLights[0].X, Precision);
        Assert.Equal(1050, lights.RailLights[0].Y, Precision);
        Assert.Equal(100, lights.RailLights[24].X, Precision);
        Assert.Equal(1770, lights.RailLights[24].Y, Precision);

        // ALSF-1/ALSF-2's defining red side-row barrettes / decision bar,
        // 300m (~1000ft) out from the threshold, spanning the crossbar's
        // own half-width (15m) either side of the centerline.
        Assert.Equal(2, lights.CrossBar.Count);
        Assert.Equal(115, lights.CrossBar[0].X, Precision);
        Assert.Equal(1350, lights.CrossBar[0].Y, Precision);
        Assert.Equal(85, lights.CrossBar[1].X, Precision);
        Assert.Equal(1350, lights.CrossBar[1].Y, Precision);

        Assert.Null(runway.SecondaryFeatures.ApproachLights);
    }

    [Fact]
    public void Project_Runway_PrimaryApproachLights_Malsr_FullCategoryWithoutRedCrossBar()
    {
        var airport = Airport(a => a.Runways.Add(new Runway
        {
            Latitude = 0,
            Longitude = 0,
            HeadingDeg = 0,
            LengthMeters = 1000,
            WidthMeters = 100,
            PrimaryApproachLights = new ApproachLightSystem(ApproachLightSystemType.Malsr),
        }));

        var diagram = AirportDiagramProjector.Project(airport);

        var lights = Assert.Single(diagram.Runways).PrimaryFeatures.ApproachLights;
        Assert.NotNull(lights);
        // Same Full-length rail as ALSF-2, but MALSR has no red side-row
        // barrettes, so CrossBar stays empty.
        Assert.Equal(25, lights!.RailLights.Count);
        Assert.Empty(lights.CrossBar);
    }

    [Fact]
    public void Project_Runway_PrimaryApproachLights_Mals_ShortCategory_FewerRailLightsThanFull()
    {
        var airport = Airport(a => a.Runways.Add(new Runway
        {
            Latitude = 0,
            Longitude = 0,
            HeadingDeg = 0,
            LengthMeters = 1000,
            WidthMeters = 100,
            PrimaryApproachLights = new ApproachLightSystem(ApproachLightSystemType.Mals),
        }));

        var diagram = AirportDiagramProjector.Project(airport);

        var lights = Assert.Single(diagram.Runways).PrimaryFeatures.ApproachLights;
        Assert.NotNull(lights);
        // Short category: 430m at 30m spacing = 15 rail lights.
        Assert.Equal(15, lights!.RailLights.Count);
        Assert.Empty(lights.CrossBar);
    }

    [Fact]
    public void Project_Runway_PrimaryApproachLights_Odals_SparseCategory_WidelySpacedLights()
    {
        var airport = Airport(a => a.Runways.Add(new Runway
        {
            Latitude = 0,
            Longitude = 0,
            HeadingDeg = 0,
            LengthMeters = 1000,
            WidthMeters = 100,
            PrimaryApproachLights = new ApproachLightSystem(ApproachLightSystemType.Odals),
        }));

        var diagram = AirportDiagramProjector.Project(airport);

        var lights = Assert.Single(diagram.Runways).PrimaryFeatures.ApproachLights;
        Assert.NotNull(lights);
        // Sparse category: 465m at 92m spacing = 6 lights.
        Assert.Equal(6, lights!.RailLights.Count);
        Assert.Empty(lights.CrossBar);
    }

    [Fact]
    public void Project_Runway_ApproachLights_NullFeatureOrNoneSystemType_ProducesNoShape()
    {
        var airport = Airport(a =>
        {
            // No PrimaryApproachLights set at all (null).
            a.Runways.Add(new Runway { Latitude = 0, Longitude = 0, HeadingDeg = 0, LengthMeters = 1000, WidthMeters = 100 });
            // SystemType 0 == NONE, same as absent.
            a.Runways.Add(new Runway { Latitude = 0, Longitude = 0.01, HeadingDeg = 0, LengthMeters = 1000, WidthMeters = 100, PrimaryApproachLights = new ApproachLightSystem(SystemType: 0) });
        });

        var diagram = AirportDiagramProjector.Project(airport);

        Assert.All(diagram.Runways, r => Assert.Null(r.PrimaryFeatures.ApproachLights));
    }

    [Fact]
    public void Project_MultipleRunways_CanvasBoundsCoverAll()
    {
        var airport = Airport(a =>
        {
            a.Runways.Add(new Runway { Latitude = 0, Longitude = 0, HeadingDeg = 0, LengthMeters = 1000, WidthMeters = 100 });
            a.Runways.Add(new Runway { Latitude = 0, Longitude = 0.01, HeadingDeg = 0, LengthMeters = 200, WidthMeters = 20 });
        });

        var diagram = AirportDiagramProjector.Project(airport);

        Assert.Equal(1273.2, diagram.CanvasWidth, 1);
        Assert.Equal(1100, diagram.CanvasHeight, Precision);
        Assert.Equal(2, diagram.Runways.Count);
    }

    [Fact]
    public void Project_TaxiwaySegment_NamedAndResolved_IsIncludedWithHasNameTrue()
    {
        var taxiName = new TaxiName { Value = "A" };
        var airport = Airport(a =>
        {
            a.TaxiNames.Add(taxiName);
            a.TaxiPaths.Add(new TaxiPathSegment
            {
                Type = TaxiPathType.Taxi,
                TaxiNameId = taxiName.Id,
                StartXMeters = 0,
                StartZMeters = 0,
                EndXMeters = 200,
                EndZMeters = 0,
                WidthMeters = 20,
            });
        });

        var diagram = AirportDiagramProjector.Project(airport);

        var segment = Assert.Single(diagram.TaxiwaySegments);
        Assert.True(segment.HasName);
        Assert.Equal("A", segment.Name);
        Assert.Equal(0, segment.SourceIndex);
        Assert.Equal(150, segment.MidPoint.X, Precision);
        Assert.Equal(100, segment.MidPoint.Y, Precision);
        Assert.Equal(4, segment.WidthCorners.Count);
        Assert.Equal(50, segment.WidthCorners[0].X, Precision);
        Assert.Equal(90, segment.WidthCorners[0].Y, Precision);
        Assert.Equal(250, segment.WidthCorners[1].X, Precision);
        Assert.Equal(90, segment.WidthCorners[1].Y, Precision);
        Assert.Equal(250, segment.WidthCorners[2].X, Precision);
        Assert.Equal(110, segment.WidthCorners[2].Y, Precision);
        Assert.Equal(50, segment.WidthCorners[3].X, Precision);
        Assert.Equal(110, segment.WidthCorners[3].Y, Precision);
        Assert.Equal(50, segment.Start.X, Precision);
        Assert.Equal(100, segment.Start.Y, Precision);
        Assert.Equal(250, segment.End.X, Precision);
        Assert.Equal(100, segment.End.Y, Precision);
    }

    [Fact]
    public void Project_TaxiwaySegment_NoTaxiNameId_HasNameFalse()
    {
        var airport = Airport(a => a.TaxiPaths.Add(new TaxiPathSegment
        {
            Type = TaxiPathType.Taxi,
            TaxiNameId = null,
            StartXMeters = 0,
            StartZMeters = 0,
            EndXMeters = 200,
            EndZMeters = 0,
        }));

        var diagram = AirportDiagramProjector.Project(airport);

        Assert.False(Assert.Single(diagram.TaxiwaySegments).HasName);
    }

    [Fact]
    public void Project_TaxiwaySegment_TaxiNameIdWithNoMatchingEntry_HasNameFalse()
    {
        // A TaxiNameId that doesn't resolve against AirportDetails.TaxiNames
        // (e.g. the entry was deleted) is treated the same as unnamed, not a
        // crash or a stale label.
        var airport = Airport(a => a.TaxiPaths.Add(new TaxiPathSegment
        {
            Type = TaxiPathType.Taxi,
            TaxiNameId = Guid.NewGuid(),
            StartXMeters = 0,
            StartZMeters = 0,
            EndXMeters = 200,
            EndZMeters = 0,
        }));

        var diagram = AirportDiagramProjector.Project(airport);

        Assert.False(Assert.Single(diagram.TaxiwaySegments).HasName);
    }

    [Fact]
    public void Project_TaxiwaySegment_RunwayType_IsExcluded()
    {
        var airport = Airport(a => a.TaxiPaths.Add(new TaxiPathSegment
        {
            Type = TaxiPathType.Runway,
            StartXMeters = 0,
            StartZMeters = 0,
            EndXMeters = 200,
            EndZMeters = 0,
        }));

        var diagram = AirportDiagramProjector.Project(airport);

        Assert.Empty(diagram.TaxiwaySegments);
    }

    [Fact]
    public void Project_TaxiwaySegment_UnresolvedCoordinates_IsExcluded_NoCrash()
    {
        var airport = Airport(a => a.TaxiPaths.Add(new TaxiPathSegment
        {
            Type = TaxiPathType.Taxi,
            StartXMeters = null,
            StartZMeters = null,
            EndXMeters = 200,
            EndZMeters = 0,
        }));

        var diagram = AirportDiagramProjector.Project(airport);

        Assert.Empty(diagram.TaxiwaySegments);
    }

    [Fact]
    public void Project_TaxiwaySegments_SourceIndexSkipsExcludedSegments()
    {
        // A Runway-typed path at index 0 is excluded from TaxiwaySegments —
        // the surviving Taxi-typed path's SourceIndex must still be 1 (its
        // real position in AirportDetails.TaxiPaths), not 0 (its position in
        // the filtered TaxiwaySegments list) — this is what lets the Edit
        // tab's diagram click map a shape back to the right TaxiPathEditViewModel.
        var airport = Airport(a =>
        {
            a.TaxiPaths.Add(new TaxiPathSegment { Type = TaxiPathType.Runway, StartXMeters = 0, StartZMeters = 0, EndXMeters = 10, EndZMeters = 0 });
            a.TaxiPaths.Add(new TaxiPathSegment { Type = TaxiPathType.Taxi, StartXMeters = 20, StartZMeters = 0, EndXMeters = 30, EndZMeters = 0 });
        });

        var diagram = AirportDiagramProjector.Project(airport);

        Assert.Equal(1, Assert.Single(diagram.TaxiwaySegments).SourceIndex);
    }

    [Fact]
    public void Project_ParkingSpot_CenterAndHeadingTipMatchHandCalculatedCoordinates()
    {
        var airport = Airport(a => a.ParkingSpots.Add(new TaxiParkingSpot
        {
            BiasXMeters = 100,
            BiasZMeters = 50,
            HeadingDeg = 0,
            RadiusMeters = 10,
        }));

        var diagram = AirportDiagramProjector.Project(airport);

        var spot = Assert.Single(diagram.ParkingSpots);
        Assert.Equal(10, spot.RadiusMeters, Precision);
        Assert.Equal(100, spot.Center.X, Precision);
        Assert.Equal(100, spot.Center.Y, Precision);
        Assert.Equal(100, spot.HeadingTip.X, Precision);
        Assert.Equal(85, spot.HeadingTip.Y, Precision);
    }

    [Fact]
    public void Project_EmptyAirport_ReturnsEmptyCollections_NonDegenerateCanvas_NoException()
    {
        var airport = Airport();

        var diagram = AirportDiagramProjector.Project(airport);

        Assert.Empty(diagram.Runways);
        Assert.Empty(diagram.TaxiwaySegments);
        Assert.Empty(diagram.ParkingSpots);
        Assert.Equal(200, diagram.CanvasWidth, Precision);
        Assert.Equal(200, diagram.CanvasHeight, Precision);
    }
}
