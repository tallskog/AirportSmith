using AirportSmith.Models.Diagram;
using AirportSmith.Services;

namespace AirportSmith.Tests.Services;

public class MapTileMathTests
{
    // Expected values below are independently hand-computed from the
    // standard published Web Mercator slippy-map formulas
    // (https://wiki.openstreetmap.org/wiki/Slippy_map_tilenames), not by
    // running MapTileMath itself.

    [Fact]
    public void LatLonToTileFraction_MatchesHandComputedValue()
    {
        var (x, y) = MapTileMath.LatLonToTileFraction(47.4502, 8.5616, 15);

        Assert.Equal(17163.295858, x, 4);
        Assert.Equal(11465.024200, y, 4);
    }

    [Fact]
    public void LatLonToTileFraction_Equator_Zoom0_ReturnsCenterOfSingleTile()
    {
        var (x, y) = MapTileMath.LatLonToTileFraction(0, 0, 0);

        Assert.Equal(0.5, x, 6);
        Assert.Equal(0.5, y, 6);
    }

    [Fact]
    public void LatLonToTile_FloorsToIntegerTile()
    {
        var (x, y) = MapTileMath.LatLonToTile(47.4502, 8.5616, 15);

        Assert.Equal(17163, x);
        Assert.Equal(11465, y);
    }

    [Fact]
    public void TileToLatLon_MatchesHandComputedValue()
    {
        var (lat, lon) = MapTileMath.TileToLatLon(17163, 11465, 15);

        Assert.Equal(47.450380, lat, 4);
        Assert.Equal(8.558350, lon, 3);
    }

    [Fact]
    public void MetersPerPixel_Zoom15_MatchesHandComputedValue()
    {
        Assert.Equal(3.2306, MapTileMath.MetersPerPixel(47.4502, 15), 3);
    }

    [Fact]
    public void MetersPerPixel_Zoom18_MatchesHandComputedValue()
    {
        Assert.Equal(0.4038, MapTileMath.MetersPerPixel(47.4502, 18), 3);
    }

    [Fact]
    public void SelectZoom_ExactMatch_ReturnsThatZoom()
    {
        var metersPerPixel = MapTileMath.MetersPerPixel(47.4502, 15);

        Assert.Equal(15, MapTileMath.SelectZoom(metersPerPixel, 47.4502));
    }

    [Fact]
    public void SelectZoom_VeryFineScale_ClampsToMaxZoom()
    {
        Assert.Equal(MapTileMath.MaxZoom, MapTileMath.SelectZoom(0.0001, 47.4502));
    }

    [Fact]
    public void SelectZoom_VeryCoarseScale_ClampsToMinZoom()
    {
        Assert.Equal(MapTileMath.MinZoom, MapTileMath.SelectZoom(1_000_000, 47.4502));
    }

    [Fact]
    public void GetVisibleTiles_ZeroAreaViewportInsideATile_ReturnsExactlyThatTile()
    {
        const int zoom = 15;
        var (topLeft, bottomRight) = MapTileMath.TileScreenRect(17163, 11465, zoom, 47.4502, 8.5616, 0, 0);
        var center = new Point2D((topLeft.X + bottomRight.X) / 2, (topLeft.Y + bottomRight.Y) / 2);

        var tiles = MapTileMath.GetVisibleTiles(center, center, zoom, 47.4502, 8.5616, 0, 0);

        var tile = Assert.Single(tiles);
        Assert.Equal(new MapTileMath.TileId(17163, 11465, zoom), tile);
    }

    [Fact]
    public void GetVisibleTiles_ViewportSpanningFourTileCenters_ReturnsExactlyThoseFourTiles()
    {
        const int zoom = 15;
        const double refLat = 47.4502, refLon = 8.5616;

        Point2D CenterOf(int x, int y)
        {
            var (topLeft, bottomRight) = MapTileMath.TileScreenRect(x, y, zoom, refLat, refLon, 0, 0);
            return new Point2D((topLeft.X + bottomRight.X) / 2, (topLeft.Y + bottomRight.Y) / 2);
        }

        var nwCenter = CenterOf(17163, 11465);
        var seCenter = CenterOf(17164, 11466);

        var tiles = MapTileMath.GetVisibleTiles(nwCenter, seCenter, zoom, refLat, refLon, 0, 0);

        Assert.Equal(4, tiles.Count);
        Assert.Contains(new MapTileMath.TileId(17163, 11465, zoom), tiles);
        Assert.Contains(new MapTileMath.TileId(17164, 11465, zoom), tiles);
        Assert.Contains(new MapTileMath.TileId(17163, 11466, zoom), tiles);
        Assert.Contains(new MapTileMath.TileId(17164, 11466, zoom), tiles);
    }

    [Fact]
    public void GetVisibleTiles_HugeViewport_IsCappedAtMaxVisibleTiles()
    {
        var tiles = MapTileMath.GetVisibleTiles(
            new Point2D(-50_000_000, -50_000_000), new Point2D(50_000_000, 50_000_000),
            zoom: 10, refLat: 0, refLon: 0, originXMeters: 0, originZMeters: 0);

        Assert.Equal(MapTileMath.MaxVisibleTiles, tiles.Count);
    }
}
