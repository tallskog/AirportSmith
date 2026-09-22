using System.IO;
using AirportSmith.Services;

namespace AirportSmith.Tests.Services;

// Every test uses its own temp directory (never AppDataHelper.AppDataPath,
// which even in this test project resolves to the real AirportSmith-dev
// folder a real dev build uses) — see CLAUDE.md's data-safety guardrail and
// AppDataHelperTests, and the identical pattern in AirportProjectStoreTests.
public class MapTileDiskCacheTests
{
    private static string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "AirportSmith-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task TryGetAsync_NeverStored_ReturnsNull()
    {
        var cache = new MapTileDiskCache(CreateTempDirectory());

        Assert.Null(await cache.TryGetAsync(1, 2, 3, CancellationToken.None));
    }

    [Fact]
    public async Task StoreThenTryGet_RoundTripsSameBytes()
    {
        var cache = new MapTileDiskCache(CreateTempDirectory());
        byte[] bytes = [1, 2, 3, 4];

        await cache.StoreAsync(1, 2, 3, bytes, TimeSpan.FromDays(30), CancellationToken.None);
        var result = await cache.TryGetAsync(1, 2, 3, CancellationToken.None);

        Assert.Equal(bytes, result);
    }

    [Fact]
    public async Task TryGetAsync_NoMaxAgeHeader_StillServedWithinSevenDayFloor()
    {
        var baseDir = CreateTempDirectory();
        var cache = new MapTileDiskCache(baseDir);
        byte[] bytes = [1];
        await cache.StoreAsync(1, 2, 3, bytes, maxAge: null, CancellationToken.None);
        BackdateCacheEntry(baseDir, 1, 2, 3, TimeSpan.FromDays(6));

        var result = await cache.TryGetAsync(1, 2, 3, CancellationToken.None);

        Assert.Equal(bytes, result);
    }

    [Fact]
    public async Task TryGetAsync_ShortServerMaxAge_StillHonorsSevenDayFloor()
    {
        var baseDir = CreateTempDirectory();
        var cache = new MapTileDiskCache(baseDir);
        byte[] bytes = [1];
        await cache.StoreAsync(1, 2, 3, bytes, maxAge: TimeSpan.FromHours(1), CancellationToken.None);
        BackdateCacheEntry(baseDir, 1, 2, 3, TimeSpan.FromDays(6));

        var result = await cache.TryGetAsync(1, 2, 3, CancellationToken.None);

        Assert.Equal(bytes, result);
    }

    [Fact]
    public async Task TryGetAsync_LongerServerMaxAge_IsHonoredPastSevenDayFloor()
    {
        var baseDir = CreateTempDirectory();
        var cache = new MapTileDiskCache(baseDir);
        byte[] bytes = [1];
        await cache.StoreAsync(1, 2, 3, bytes, maxAge: TimeSpan.FromDays(30), CancellationToken.None);
        BackdateCacheEntry(baseDir, 1, 2, 3, TimeSpan.FromDays(20));

        var result = await cache.TryGetAsync(1, 2, 3, CancellationToken.None);

        Assert.Equal(bytes, result);
    }

    [Fact]
    public async Task TryGetAsync_PastBothMaxAgeAndFloor_ReturnsNull()
    {
        var baseDir = CreateTempDirectory();
        var cache = new MapTileDiskCache(baseDir);
        await cache.StoreAsync(1, 2, 3, [1], maxAge: TimeSpan.FromDays(30), CancellationToken.None);
        BackdateCacheEntry(baseDir, 1, 2, 3, TimeSpan.FromDays(31));

        var result = await cache.TryGetAsync(1, 2, 3, CancellationToken.None);

        Assert.Null(result);
    }

    // Rewrites the meta file's CachedAtUtc to simulate a tile stored `age`
    // ago, without needing the test itself to sleep for days.
    private static void BackdateCacheEntry(string baseDir, int x, int y, int zoom, TimeSpan age)
    {
        var metaPath = Path.Combine(baseDir, "MapTiles", zoom.ToString(), x.ToString(), $"{y}.meta.json");
        var json = File.ReadAllText(metaPath);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var maxAgeSeconds = document.RootElement.TryGetProperty("MaxAgeSeconds", out var el) && el.ValueKind != System.Text.Json.JsonValueKind.Null
            ? el.GetDouble()
            : (double?)null;

        var backdated = $$"""
        {
          "CachedAtUtc": "{{(DateTime.UtcNow - age):O}}",
          "MaxAgeSeconds": {{(maxAgeSeconds is { } s ? s.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null")}}
        }
        """;
        File.WriteAllText(metaPath, backdated);
    }
}
