using AirportSmith.Services;

namespace AirportSmith.Tests.Fakes;

public class FakeSimConnectService : ISimConnectService
{
    public bool IsConnected { get; set; }

#pragma warning disable CS0067
    public event EventHandler? ConnectionChanged;
#pragma warning restore CS0067

    public void Connect(nint windowHandle) { }
    public void Disconnect() { }

    // Configured per-test. When set, GetAirportDetailsAsync awaits it and returns
    // its result — lets tests hold a lookup "in flight" (e.g. to assert CanExecute
    // is false mid-request) by controlling when the TaskCompletionSource completes.
    public Func<string, Task<AirportLookupResult>>? OnGetAirportDetails;

    public Task<AirportLookupResult> GetAirportDetailsAsync(string icao, CancellationToken cancellationToken = default)
        => OnGetAirportDetails?.Invoke(icao)
            ?? Task.FromResult(new AirportLookupResult(AirportLookupStatus.Error, null, "Fake not configured"));

    public void RaiseConnectionChanged() => ConnectionChanged?.Invoke(this, EventArgs.Empty);
}
