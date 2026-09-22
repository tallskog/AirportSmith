using System.IO;
using System.Text.Json;
using AirportSmith.Helpers;

namespace AirportSmith.Services;

// Disk cache under {baseDirectory}\MapTiles\{zoom}\{x}\{y}.png (plus a
// sibling {y}.meta.json holding when it was cached and the server's own
// Cache-Control: max-age, if any).
//
// baseDirectory defaults to AppDataHelper.AppDataPath (AirportSmith-dev in
// Debug builds, AirportSmith in Release) but is overridable via the
// constructor, so tests never touch the real dev AppData folder - the same
// pattern AirportProjectStore uses, per CLAUDE.md's data-safety guardrail.
//
// Retention: OSM's tile usage policy requires caching a tile for at least 7
// days regardless of what the server's own Cache-Control header says (a
// short/absent header does not excuse re-fetching sooner) - MinRetention
// enforces that floor. A longer server-supplied MaxAge is honored past that
// floor. No active/background eviction in this phase - an expired tile is
// simply re-fetched (and overwritten) on next request, never proactively
// deleted.
public class MapTileDiskCache : IMapTileCache
{
    private static readonly TimeSpan MinRetention = TimeSpan.FromDays(7);
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _root;

    public MapTileDiskCache(string? baseDirectory = null)
    {
        _root = Path.Combine(baseDirectory ?? AppDataHelper.AppDataPath, "MapTiles");
    }

    public async Task<byte[]?> TryGetAsync(int x, int y, int zoom, CancellationToken ct)
    {
        var (tilePath, metaPath) = PathsFor(x, y, zoom);
        if (!File.Exists(tilePath) || !File.Exists(metaPath)) return null;

        try
        {
            var meta = JsonSerializer.Deserialize<MapTileCacheMeta>(await File.ReadAllTextAsync(metaPath, ct));
            if (meta is null) return null;

            var maxAge = meta.MaxAgeSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : (TimeSpan?)null;
            var retention = maxAge is { } age && age > MinRetention ? age : MinRetention;
            if (DateTime.UtcNow - meta.CachedAtUtc > retention) return null;

            return await File.ReadAllBytesAsync(tilePath, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public async Task StoreAsync(int x, int y, int zoom, byte[] bytes, TimeSpan? maxAge, CancellationToken ct)
    {
        var (tilePath, metaPath) = PathsFor(x, y, zoom);
        Directory.CreateDirectory(Path.GetDirectoryName(tilePath)!);

        await File.WriteAllBytesAsync(tilePath, bytes, ct);
        var meta = new MapTileCacheMeta(DateTime.UtcNow, maxAge?.TotalSeconds);
        await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta, SerializerOptions), ct);
    }

    private (string TilePath, string MetaPath) PathsFor(int x, int y, int zoom)
    {
        var dir = Path.Combine(_root, zoom.ToString(), x.ToString());
        return (Path.Combine(dir, $"{y}.png"), Path.Combine(dir, $"{y}.meta.json"));
    }

    private record MapTileCacheMeta(DateTime CachedAtUtc, double? MaxAgeSeconds);
}
