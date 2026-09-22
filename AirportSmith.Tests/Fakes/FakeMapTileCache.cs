using AirportSmith.Services;

namespace AirportSmith.Tests.Fakes;

public class FakeMapTileCache : IMapTileCache
{
    public Dictionary<(int X, int Y, int Zoom), byte[]> Stored { get; } = [];
    public List<(int X, int Y, int Zoom, byte[] Bytes, TimeSpan? MaxAge)> StoreCalls { get; } = [];

    public Task<byte[]?> TryGetAsync(int x, int y, int zoom, CancellationToken ct)
        => Task.FromResult(Stored.GetValueOrDefault((x, y, zoom)));

    public Task StoreAsync(int x, int y, int zoom, byte[] bytes, TimeSpan? maxAge, CancellationToken ct)
    {
        Stored[(x, y, zoom)] = bytes;
        StoreCalls.Add((x, y, zoom, bytes, maxAge));
        return Task.CompletedTask;
    }
}
