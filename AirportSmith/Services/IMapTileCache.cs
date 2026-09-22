namespace AirportSmith.Services;

// A local on-disk cache of previously-fetched map tiles, so a tile already
// seen doesn't need re-fetching from the network every time it scrolls back
// into view - and so the map layer still works with no network access at all
// for tiles already cached.
public interface IMapTileCache
{
    // Null if the tile has never been cached, or was cached but has since
    // passed its retention window (see MapTileDiskCache's doc comment).
    Task<byte[]?> TryGetAsync(int x, int y, int zoom, CancellationToken ct);

    Task StoreAsync(int x, int y, int zoom, byte[] bytes, TimeSpan? maxAge, CancellationToken ct);
}
