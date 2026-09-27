using AirportSmith.Models;
using AirportSmith.Services;

namespace AirportSmith.Tests.Services;

public class TaxiwayNameSuggesterTests
{
    private const double Lat = 60.3172;
    private const double Lon = 24.9633;

    private static AirportDetails Airport(params TaxiPathSegment[] paths)
    {
        var airport = new AirportDetails { Icao = "EFHK", Latitude = Lat, Longitude = Lon };
        airport.TaxiPaths.AddRange(paths);
        return airport;
    }

    private static TaxiPathSegment Path(double x1, double z1, double x2, double z2, TaxiPathType type = TaxiPathType.Taxi, Guid? nameId = null) =>
        new() { Type = type, StartXMeters = x1, StartZMeters = z1, EndXMeters = x2, EndZMeters = z2, TaxiNameId = nameId };

    // An OSM way given in the airport's local meters, converted to lat/lon.
    private static OsmTaxiway Way(string? name, params (double X, double Z)[] points) =>
        new(name, points.Select(p => GeoProjection.UnprojectLocalPoint(Lat, Lon, p.X, p.Z)).ToList());

    private static string NoNames(Guid? _) => string.Empty;

    [Fact]
    public void PathAlongANamedWay_GetsItsName()
    {
        var airport = Airport(Path(0, 0, 100, 0));

        var suggestion = Assert.Single(TaxiwayNameSuggester.Suggest(airport, [Way("A", (-50, 5), (200, 5))], NoNames));

        Assert.Equal(new TaxiwayNameSuggestion(0, "A"), suggestion);
    }

    [Fact]
    public void NearerUnnamedWay_Wins_SoNoSuggestion()
    {
        var airport = Airport(Path(0, 0, 100, 0));
        OsmTaxiway[] ways = [Way(null, (-50, 2), (200, 2)), Way("A", (-50, 12), (200, 12))];

        Assert.Empty(TaxiwayNameSuggester.Suggest(airport, ways, NoNames));
    }

    [Fact]
    public void NearestOfTwoNamedWays_Wins()
    {
        var airport = Airport(Path(0, 0, 100, 0));
        OsmTaxiway[] ways = [Way("B", (-50, 15), (200, 15)), Way("A", (-50, -3), (200, -3))];

        Assert.Equal("A", Assert.Single(TaxiwayNameSuggester.Suggest(airport, ways, NoNames)).Name);
    }

    [Fact]
    public void FarWay_IsIgnored()
    {
        var airport = Airport(Path(0, 0, 100, 0));

        Assert.Empty(TaxiwayNameSuggester.Suggest(airport, [Way("A", (-50, 40), (200, 40))], NoNames));
    }

    // A taxiway crossing at the path's end passes within 0 m of it there,
    // but runs the wrong way (and far from the rest of the path).
    [Fact]
    public void CrossingWay_IsIgnored()
    {
        var airport = Airport(Path(0, 0, 100, 0));

        Assert.Empty(TaxiwayNameSuggester.Suggest(airport, [Way("C", (100, -200), (100, 200))], NoNames));
    }

    // A short path near a crossing way, fully within the distance limits, is
    // still rejected by the direction check.
    [Fact]
    public void ShortPathAcrossAWay_IsRejectedByDirection()
    {
        var airport = Airport(Path(0, -8, 0, 8));

        Assert.Empty(TaxiwayNameSuggester.Suggest(airport, [Way("C", (-100, 0), (100, 0))], NoNames));
    }

    [Fact]
    public void WayDrawnInTheOppositeDirection_StillMatches()
    {
        var airport = Airport(Path(0, 0, 100, 0));

        Assert.Single(TaxiwayNameSuggester.Suggest(airport, [Way("A", (200, 0), (-50, 0))], NoNames));
    }

    [Theory]
    [InlineData(TaxiPathType.Runway)]
    [InlineData(TaxiPathType.Parking)]
    [InlineData(TaxiPathType.Vehicle)]
    public void NonTaxiPaths_AreSkipped(TaxiPathType type)
    {
        var airport = Airport(Path(0, 0, 100, 0, type));

        Assert.Empty(TaxiwayNameSuggester.Suggest(airport, [Way("A", (-50, 0), (200, 0))], NoNames));
    }

    [Fact]
    public void PathWithUnresolvedEnd_IsSkipped()
    {
        var airport = Airport(new TaxiPathSegment { Type = TaxiPathType.Taxi, StartXMeters = 0, StartZMeters = 0 });

        Assert.Empty(TaxiwayNameSuggester.Suggest(airport, [Way("A", (-50, 0), (200, 0))], NoNames));
    }

    [Fact]
    public void PathAlreadyHavingThatName_IsSkipped_ButADifferentNameIsSuggested()
    {
        var existing = Guid.NewGuid();
        var airport = Airport(Path(0, 0, 100, 0, nameId: existing), Path(0, 50, 100, 50, nameId: existing));
        OsmTaxiway[] ways = [Way("A", (-50, 0), (200, 0)), Way("B", (-50, 50), (200, 50))];

        var suggestion = Assert.Single(TaxiwayNameSuggester.Suggest(airport, ways, id => id == existing ? "a" : string.Empty));

        Assert.Equal(new TaxiwayNameSuggestion(1, "B"), suggestion);
    }
}
