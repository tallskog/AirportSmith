using AirportSmith.Services;

namespace AirportSmith.Tests.Fakes;

public class FakeMapTileService : IMapTileService
{
    public Dictionary<(int X, int Y, int Zoom), byte[]?> TilesToReturn { get; } = [];
    public List<(int X, int Y, int Zoom)> RequestedTiles { get; } = [];

    public Task<byte[]?> GetTileAsync(int x, int y, int zoom, CancellationToken ct)
    {
        RequestedTiles.Add((x, y, zoom));
        return Task.FromResult(TilesToReturn.GetValueOrDefault((x, y, zoom)));
    }
}
