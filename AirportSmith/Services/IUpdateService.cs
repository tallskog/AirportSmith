namespace AirportSmith.Services;

/// <summary>
/// Checks GitHub Releases for a newer published AirportSmith version and
/// installs it. Abstracted so UpdateViewModel's logic is testable with a fake
/// — the real implementation (VelopackUpdateService) makes network calls.
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// False when running from a plain build output folder (dev/Debug runs,
    /// `dotnet run`) rather than a Velopack-installed copy — updates can only
    /// be applied to an installed app.
    /// </summary>
    bool IsInstalled { get; }

    /// <summary>The running app's version, e.g. "0.0.1".</summary>
    string CurrentVersion { get; }

    /// <summary>
    /// Returns the newer version string if one is published, otherwise null.
    /// Throws on network/feed failure.
    /// </summary>
    Task<string?> CheckForUpdateAsync();

    /// <summary>Downloads the update found by the last CheckForUpdateAsync call.</summary>
    Task DownloadUpdateAsync();

    /// <summary>Applies the downloaded update and restarts the app.</summary>
    void ApplyUpdateAndRestart();
}
