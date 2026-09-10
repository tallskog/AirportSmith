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
        Assert.Equal(100, runway.Threshold1.X, Precision);
        Assert.Equal(50, runway.Threshold1.Y, Precision);
        Assert.Equal(100, runway.Threshold2.X, Precision);
        Assert.Equal(1050, runway.Threshold2.Y, Precision);

        Assert.Equal(4, runway.Corners.Count);
        Assert.Equal(150, runway.Corners[0].X, Precision);
        Assert.Equal(50, runway.Corners[0].Y, Precision);
        Assert.Equal(150, runway.Corners[1].X, Precision);
        Assert.Equal(1050, runway.Corners[1].Y, Precision);
        Assert.Equal(50, runway.Corners[2].X, Precision);
        Assert.Equal(1050, runway.Corners[2].Y, Precision);
        Assert.Equal(50, runway.Corners[3].X, Precision);
        Assert.Equal(50, runway.Corners[3].Y, Precision);
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
        var airport = Airport(a => a.TaxiPaths.Add(new TaxiPathSegment
        {
            Type = (int)TaxiPathType.Taxi,
            Name = "A",
            StartXMeters = 0,
            StartZMeters = 0,
            EndXMeters = 200,
            EndZMeters = 0,
            WidthMeters = 20,
        }));

        var diagram = AirportDiagramProjector.Project(airport);

        var segment = Assert.Single(diagram.TaxiwaySegments);
        Assert.True(segment.HasName);
        Assert.Equal("A", segment.Name);
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
    public void Project_TaxiwaySegment_EmptyName_HasNameFalse()
    {
        var airport = Airport(a => a.TaxiPaths.Add(new TaxiPathSegment
        {
            Type = (int)TaxiPathType.Taxi,
            Name = "",
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
            Type = (int)TaxiPathType.Runway,
            Name = "09L",
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
            Type = (int)TaxiPathType.Taxi,
            Name = "A",
            StartXMeters = null,
            StartZMeters = null,
            EndXMeters = 200,
            EndZMeters = 0,
        }));

        var diagram = AirportDiagramProjector.Project(airport);

        Assert.Empty(diagram.TaxiwaySegments);
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
