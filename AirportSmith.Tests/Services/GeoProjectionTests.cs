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
}
