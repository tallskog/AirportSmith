namespace AirportSmith.Services;

public class MapTileService : IMapTileService
{
    // A pan/zoom can make dozens of tiles newly visible at once (up to
    // MapTileMath.MaxVisibleTiles), and AirportDiagramView fires a
    // GetTileAsync for every one of them concurrently. Confirmed against a
    // live OIBK session that firing them all at once against OSM's tile
    // server leaves only one or two tiles ever rendering (most of the burst
    // gets silently rejected/throttled server-side, indistinguishable from
    // an ordinary failed fetch per IMapTileSource's contract) — this gate
    // caps how many of THIS service's fetches (cache reads included, for
    // simplicity - their cost is negligible next to a network round trip) run
    // at once, so a big newly-visible batch is serviced a few at a time
    // instead of as one burst. 2 matches the classic "per-host connection"
    // convention most polite tile-consuming desktop clients use, and brings
    // the app closer to OSM's tile usage policy, which discourages exactly
    // this kind of burst.
    private const int MaxConcurrentFetches = 2;

    private readonly IMapTileSource _source;
    private readonly IMapTileCache _cache;
    private readonly SemaphoreSlim _concurrencyGate = new(MaxConcurrentFetches);

    public MapTileService(IMapTileSource source, IMapTileCache cache)
    {
        _source = source;
        _cache = cache;
    }

    public async Task<byte[]?> GetTileAsync(int x, int y, int zoom, CancellationToken ct)
    {
        await _concurrencyGate.WaitAsync(ct);
        try
        {
            var cached = await _cache.TryGetAsync(x, y, zoom, ct);
            if (cached is not null) return cached;

            var fetched = await _source.FetchTileAsync(x, y, zoom, ct);
            if (fetched is null) return null;

            await _cache.StoreAsync(x, y, zoom, fetched.Bytes, fetched.MaxAge, ct);
            return fetched.Bytes;
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }
}
