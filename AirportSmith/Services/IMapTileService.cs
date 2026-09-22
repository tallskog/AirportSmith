namespace AirportSmith.Services;

// The single map-tile boundary AirportDiagramView/MainViewModel depend on -
// composes an IMapTileCache (checked first) and an IMapTileSource
// (fetch-on-miss, then stored back into the cache) so callers never deal
// with HTTP/disk separately. Null return means "don't draw this tile," never
// an exception - see IMapTileSource's doc comment.
public interface IMapTileService
{
    Task<byte[]?> GetTileAsync(int x, int y, int zoom, CancellationToken ct);
}
