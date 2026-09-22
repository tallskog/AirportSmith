using AirportSmith.Services;

namespace AirportSmith.Tests.Fakes;

public class FakeMapTileSource : IMapTileSource
{
    public Dictionary<(int X, int Y, int Zoom), MapTileFetchResult?> ResultsToReturn { get; } = [];
    public List<(int X, int Y, int Zoom)> RequestedTiles { get; } = [];

    // Lets a test hold every in-flight FetchTileAsync call open until it
    // chooses to release them, so it can observe how many run concurrently -
    // see MapTileServiceTests' concurrency-throttling test.
    public TaskCompletionSource<bool>? ReleaseGate { get; set; }
    public int MaxObservedConcurrency { get; private set; }

    private int _current;

    public async Task<MapTileFetchResult?> FetchTileAsync(int x, int y, int zoom, CancellationToken ct)
    {
        lock (RequestedTiles) RequestedTiles.Add((x, y, zoom));
        var concurrent = Interlocked.Increment(ref _current);
        MaxObservedConcurrency = Math.Max(MaxObservedConcurrency, concurrent);

        if (ReleaseGate != null) await ReleaseGate.Task;

        Interlocked.Decrement(ref _current);
        return ResultsToReturn.GetValueOrDefault((x, y, zoom));
    }
}
