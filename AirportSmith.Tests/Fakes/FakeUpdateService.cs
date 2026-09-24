using AirportSmith.Services;

namespace AirportSmith.Tests.Fakes;

public class FakeUpdateService : IUpdateService
{
    public bool IsInstalled { get; set; } = true;
    public string CurrentVersion { get; set; } = "0.0.1";

    /// <summary>Version CheckForUpdateAsync reports; null = up to date.</summary>
    public string? AvailableVersion { get; set; }
    public Exception? CheckException { get; set; }

    public int CheckCount { get; private set; }
    public int DownloadCount { get; private set; }
    public int ApplyCount { get; private set; }

    public Task<string?> CheckForUpdateAsync()
    {
        CheckCount++;
        if (CheckException != null) throw CheckException;
        return Task.FromResult(AvailableVersion);
    }

    public Task DownloadUpdateAsync()
    {
        DownloadCount++;
        return Task.CompletedTask;
    }

    public void ApplyUpdateAndRestart() => ApplyCount++;
}
