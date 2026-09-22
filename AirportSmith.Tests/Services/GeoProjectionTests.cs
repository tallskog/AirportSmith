using AirportSmith.Services;

namespace AirportSmith.Tests.Services;

public class GeoProjectionTests
{
    // Loose enough to absorb the flat-earth approximation's own error at
    // airport scale (a few km), tight enough to catch a broken inverse.
    private const double LatLonPrecision = 6;

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(47.4502, 8.5616, 500, -300)]
    [InlineData(-33.9461, 151.1772, -1200, 800)]
    [InlineData(60, -10, 2000, 2000)]
    public void UnprojectLocalPoint_IsExactInverseOfProjectLatLon(double refLat, double refLon, double x, double z)
    {
        var (lat, lon) = GeoProjection.UnprojectLocalPoint(refLat, refLon, x, z);
        var (roundTrippedX, roundTrippedZ) = GeoProjection.ProjectLatLon(refLat, refLon, lat, lon);

        Assert.Equal(x, roundTrippedX, LatLonPrecision);
        Assert.Equal(z, roundTrippedZ, LatLonPrecision);
    }

    [Fact]
    public void ProjectLatLon_ReferencePointItself_ReturnsOrigin()
    {
        var (x, z) = GeoProjection.ProjectLatLon(47.4502, 8.5616, 47.4502, 8.5616);

        Assert.Equal(0, x, LatLonPrecision);
        Assert.Equal(0, z, LatLonPrecision);
    }

    [Fact]
    public void UnprojectLocalPoint_Origin_ReturnsReferencePoint()
    {
        var (lat, lon) = GeoProjection.UnprojectLocalPoint(47.4502, 8.5616, 0, 0);

        Assert.Equal(47.4502, lat, LatLonPrecision);
        Assert.Equal(8.5616, lon, LatLonPrecision);
    }

    // Pins absolute accuracy, not just round-trip consistency (the tests
    // above would pass even for a wrong-but-self-consistent formula).
    // Expected meters-per-degree values are independently computed from the
    // standard WGS84 meridian/prime-vertical closed-form formulas, not by
    // running GeoProjection itself.
    [Theory]
    [InlineData(0, 110574.28, 111319.49)]        // equator
    [InlineData(47.4502, 111179.62, 75414.79)]    // Zurich (this file's usual reference point)
    [InlineData(60, 111412.29, 55800.00)]
    [InlineData(-33.9461, 110921.41, 92443.10)]   // Sydney
    public void ProjectLatLon_OneDegreeOffset_MatchesKnownWgs84MetersPerDegree(
        double refLat, double expectedMetersPerDegLat, double expectedMetersPerDegLon)
    {
        var (xEast, _) = GeoProjection.ProjectLatLon(refLat, 0, refLat, 1);
        var (_, zNorth) = GeoProjection.ProjectLatLon(refLat, 0, refLat + 1, 0);

        Assert.Equal(expectedMetersPerDegLon, xEast, 1);
        Assert.Equal(expectedMetersPerDegLat, zNorth, 1);
    }

    [Fact]
    public void ProjectLatLon_AtNonEquatorialLatitude_DiffersFromOldFlatEquatorialConstant()
    {
        // Regression guard: the previous implementation used a single
        // constant (111_320 m/deg, the equatorial figure) for latitude
        // distance at every latitude. At 60N the correct meridian-based
        // figure is ~111412.29 m/deg (per the Theory above) — noticeably
        // different from the old flat constant, so this would fail if the
        // fix were ever reverted.
        const double oldFlatConstant = 111_320;

        var (_, zNorth) = GeoProjection.ProjectLatLon(60, 0, 61, 0);

        Assert.NotEqual(oldFlatConstant, zNorth, 0);
        Assert.Equal(111412.29, zNorth, 1);
    }
}
