using AirportSmith.Services;
using AirportSmith.Tests.Fakes;

namespace AirportSmith.Tests.Services;

public class MapTileServiceTests
{
    [Fact]
    public async Task GetTileAsync_CacheHit_NeverCallsSource()
    {
        var cache = new FakeMapTileCache();
        var source = new FakeMapTileSource();
        cache.Stored[(1, 2, 3)] = [9, 9, 9];
        var service = new MapTileService(source, cache);

        var result = await service.GetTileAsync(1, 2, 3, CancellationToken.None);

        Assert.Equal([9, 9, 9], result);
        Assert.Empty(source.RequestedTiles);
    }

    [Fact]
    public async Task GetTileAsync_CacheMiss_FetchesFromSourceAndStores()
    {
        var cache = new FakeMapTileCache();
        var source = new FakeMapTileSource();
        var maxAge = TimeSpan.FromDays(30);
        source.ResultsToReturn[(1, 2, 3)] = new MapTileFetchResult([1, 2, 3], maxAge);
        var service = new MapTileService(source, cache);

        var result = await service.GetTileAsync(1, 2, 3, CancellationToken.None);

        Assert.Equal([1, 2, 3], result);
        var stored = Assert.Single(cache.StoreCalls);
        Assert.Equal((1, 2, 3), (stored.X, stored.Y, stored.Zoom));
        Assert.Equal([1, 2, 3], stored.Bytes);
        Assert.Equal(maxAge, stored.MaxAge);
    }

    [Fact]
    public async Task GetTileAsync_SourceFailure_ReturnsNullWithoutStoring()
    {
        var cache = new FakeMapTileCache();
        var source = new FakeMapTileSource(); // no entry configured -> null result
        var service = new MapTileService(source, cache);

        var result = await service.GetTileAsync(1, 2, 3, CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(cache.StoreCalls);
    }

    // Regression test for a real bug found against a live session: a pan/zoom
    // makes many tiles newly visible at once, and AirportDiagramView requests
    // all of them concurrently. Without a cap here, that burst against OSM's
    // real tile server left only one or two tiles ever rendering (the rest
    // silently rejected/throttled server-side) — confirmed by throttling
    // fetches through this service and re-testing manually. This test proves
    // the cap holds even when far more requests than the limit arrive at
    // once, using FakeMapTileSource's gate to hold every call open until
    // they've all had a chance to queue up.
    [Fact]
    public async Task GetTileAsync_ManyConcurrentRequests_NeverExceedsConcurrencyCap()
    {
        var cache = new FakeMapTileCache();
        var source = new FakeMapTileSource { ReleaseGate = new TaskCompletionSource<bool>() };
        var service = new MapTileService(source, cache);
        const int requestCount = 16;

        var tasks = Enumerable.Range(0, requestCount)
            .Select(i => service.GetTileAsync(i, 0, 5, CancellationToken.None))
            .ToArray();

        // Give every task a chance to reach (and queue behind) the
        // concurrency gate before inspecting how many actually got in.
        await Task.Delay(200);
        Assert.True(source.MaxObservedConcurrency > 0, "no request ever started");
        Assert.True(source.MaxObservedConcurrency <= 2, $"expected at most 2 concurrent fetches, observed {source.MaxObservedConcurrency}");

        source.ReleaseGate.SetResult(true);
        await Task.WhenAll(tasks);

        Assert.Equal(requestCount, source.RequestedTiles.Count);
    }
}
