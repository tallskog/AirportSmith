using AirportSmith.Services;

namespace AirportSmith.Tests.Fakes;

// Returns a canned result. Set Pending to hold the call open until the test
// completes it, to observe the in-flight (busy) state.
public class FakeOsmTaxiwaySource : IOsmTaxiwaySource
{
    public OsmTaxiwayFetchResult Result { get; set; } = new([], null);
    public TaskCompletionSource<OsmTaxiwayFetchResult>? Pending { get; set; }
    public int CallCount { get; private set; }
    public double LastRadiusMeters { get; private set; }

    public Task<OsmTaxiwayFetchResult> FetchTaxiwaysAsync(double lat, double lon, double radiusMeters, CancellationToken ct)
    {
        CallCount++;
        LastRadiusMeters = radiusMeters;
        return Pending?.Task ?? Task.FromResult(Result);
    }
}
