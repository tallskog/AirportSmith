using AirportSmith.Models.Diagram;

namespace AirportSmith.Services;

// Standard Web Mercator ("slippy map" / EPSG:3857) XYZ tile math, plus the
// glue to place a tile in AirportDiagram's local-meters/screen space (via
// GeoProjection and the OriginXMeters/OriginZMeters convention documented on
// AirportDiagram — screenX = localX + OriginXMeters, screenY = OriginZMeters
// - localZ, the same convention every other diagram shape uses). Pure/
// unit-testable, no WPF or HTTP dependency, in the same style as
// AirportDiagramProjector. The diagram is a flat local-meters plane, north-up,
// so tiles are only ever scaled/placed here, never rotated (see
// background-map-research.md).
public static class MapTileMath
{
    // OSM's own standard tile layer's practical zoom range — requesting
    // above MaxZoom returns nothing useful from most tile servers.
    public const int MinZoom = 0;
    public const int MaxZoom = 19;

    // Defensive ceiling on GetVisibleTiles' result. A normal screenful at
    // typical diagram zoom is a few dozen tiles; this only guards against a
    // degenerate huge viewport rect, and structurally enforces this
    // feature's "only fetch what's visible, never the whole airport" rule.
    public const int MaxVisibleTiles = 400;

    private const int TileSizePixels = 256;
    private const double EquatorialCircumferenceMeters = 40_075_016.686;

    public readonly record struct TileId(int X, int Y, int Zoom);

    // Continuous (non-integer) Web Mercator tile coordinate for (lat, lon) at
    // the given zoom - the standard slippy-map formula. X depends only on
    // lon, Y only on lat (Mercator is a cylindrical, non-rotated projection).
    public static (double X, double Y) LatLonToTileFraction(double lat, double lon, int zoom)
    {
        var n = Math.Pow(2, zoom);
        var latRad = DegToRad(Math.Clamp(lat, -85.05112878, 85.05112878));
        var x = (lon + 180.0) / 360.0 * n;
        var y = (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n;
        return (x, y);
    }

    // The tile that contains (lat, lon) - the floor of LatLonToTileFraction,
    // clamped to the valid [0, 2^zoom) index range.
    public static (int X, int Y) LatLonToTile(double lat, double lon, int zoom)
    {
        var (xFrac, yFrac) = LatLonToTileFraction(lat, lon, zoom);
        var maxIndex = (int)Math.Pow(2, zoom) - 1;
        return (Math.Clamp((int)Math.Floor(xFrac), 0, maxIndex), Math.Clamp((int)Math.Floor(yFrac), 0, maxIndex));
    }

    // NW (top-left) corner lat/lon of tile (x, y, zoom) - the inverse of
    // LatLonToTileFraction at integer tile coordinates.
    public static (double Lat, double Lon) TileToLatLon(int x, int y, int zoom)
    {
        var n = Math.Pow(2, zoom);
        var lon = x / n * 360.0 - 180.0;
        var latRad = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y / n)));
        return (RadToDeg(latRad), lon);
    }

    // Web Mercator ground resolution at (lat, zoom), in meters/pixel, for the
    // standard 256px tile size.
    public static double MetersPerPixel(double lat, int zoom)
        => EquatorialCircumferenceMeters * Math.Cos(DegToRad(lat)) / (TileSizePixels * Math.Pow(2, zoom));

    // Picks the OSM zoom whose ground resolution is closest to the diagram's
    // current on-screen scale (diagramMetersPerPixel = 1 / the current WPF
    // ScaleTransform factor, since the diagram is already in meters),
    // clamped to [MinZoom, MaxZoom].
    public static int SelectZoom(double diagramMetersPerPixel, double refLat)
    {
        var bestZoom = MinZoom;
        var bestDiff = double.MaxValue;
        for (var zoom = MinZoom; zoom <= MaxZoom; zoom++)
        {
            var diff = Math.Abs(MetersPerPixel(refLat, zoom) - diagramMetersPerPixel);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                bestZoom = zoom;
            }
        }
        return bestZoom;
    }

    // The screen-space rect tile (x, y, zoom) occupies, via
    // GeoProjection.ProjectLatLon on its NW/SE corners, through the same
    // AirportDiagram screen-space convention every other shape uses.
    public static (Point2D TopLeft, Point2D BottomRight) TileScreenRect(
        int x, int y, int zoom, double refLat, double refLon, double originXMeters, double originZMeters)
    {
        var (nwLat, nwLon) = TileToLatLon(x, y, zoom);
        var (seLat, seLon) = TileToLatLon(x + 1, y + 1, zoom);

        var topLeft = ToScreen(GeoProjection.ProjectLatLon(refLat, refLon, nwLat, nwLon), originXMeters, originZMeters);
        var bottomRight = ToScreen(GeoProjection.ProjectLatLon(refLat, refLon, seLat, seLon), originXMeters, originZMeters);

        return (topLeft, bottomRight);
    }

    // Every tile at `zoom` whose screen rect intersects the given visible
    // screen-space rect (viewportTopLeft/BottomRight, in the same screen
    // space as TileScreenRect's output), capped at MaxVisibleTiles. Unprojects
    // the viewport's top-left/bottom-right corners back to lat/lon to find
    // the tile index range, then intersects with the valid [0, 2^zoom) range.
    public static IReadOnlyList<TileId> GetVisibleTiles(
        Point2D viewportTopLeft, Point2D viewportBottomRight, int zoom,
        double refLat, double refLon, double originXMeters, double originZMeters)
    {
        var (nwLat, nwLon) = ToLatLon(viewportTopLeft, refLat, refLon, originXMeters, originZMeters);
        var (seLat, seLon) = ToLatLon(viewportBottomRight, refLat, refLon, originXMeters, originZMeters);

        var (xNw, yNw) = LatLonToTileFraction(nwLat, nwLon, zoom);
        var (xSe, ySe) = LatLonToTileFraction(seLat, seLon, zoom);

        var maxIndex = (int)Math.Pow(2, zoom) - 1;
        var xMin = Math.Clamp((int)Math.Floor(Math.Min(xNw, xSe)), 0, maxIndex);
        var xMax = Math.Clamp((int)Math.Floor(Math.Max(xNw, xSe)), 0, maxIndex);
        var yMin = Math.Clamp((int)Math.Floor(Math.Min(yNw, ySe)), 0, maxIndex);
        var yMax = Math.Clamp((int)Math.Floor(Math.Max(yNw, ySe)), 0, maxIndex);

        var tiles = new List<TileId>();
        for (var ty = yMin; ty <= yMax; ty++)
        {
            for (var tx = xMin; tx <= xMax; tx++)
            {
                if (tiles.Count >= MaxVisibleTiles) return tiles;
                tiles.Add(new TileId(tx, ty, zoom));
            }
        }
        return tiles;
    }

    private static Point2D ToScreen((double X, double Z) local, double originXMeters, double originZMeters)
        => new(local.X + originXMeters, originZMeters - local.Z);

    private static (double Lat, double Lon) ToLatLon(
        Point2D screen, double refLat, double refLon, double originXMeters, double originZMeters)
    {
        var localX = screen.X - originXMeters;
        var localZ = originZMeters - screen.Y;
        return GeoProjection.UnprojectLocalPoint(refLat, refLon, localX, localZ);
    }

    private static double DegToRad(double deg) => deg * Math.PI / 180;
    private static double RadToDeg(double rad) => rad * 180 / Math.PI;
}
