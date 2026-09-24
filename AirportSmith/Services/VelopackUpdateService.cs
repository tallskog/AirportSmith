using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace AirportSmith.Services;

/// <summary>
/// Real IUpdateService backed by Velopack, reading the public GitHub Releases
/// feed that .github/workflows/release.yml publishes on every `vX.Y.Z` tag.
/// No access token: the repo is public, so the release feed is readable
/// anonymously. Same approach as DestinationPlanner.
/// </summary>
public class VelopackUpdateService : IUpdateService
{
    public const string RepositoryUrl = "https://github.com/tallskog/AirportSmith";

    private readonly UpdateManager _updateManager =
        new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));

    private UpdateInfo? _pendingUpdate;

    public bool IsInstalled => _updateManager.IsInstalled;

    public string CurrentVersion =>
        _updateManager.CurrentVersion?.ToString()
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "unknown";

    public async Task<string?> CheckForUpdateAsync()
    {
        _pendingUpdate = await _updateManager.CheckForUpdatesAsync();
        return _pendingUpdate?.TargetFullRelease.Version.ToString();
    }

    public async Task DownloadUpdateAsync()
    {
        if (_pendingUpdate == null)
            throw new InvalidOperationException("No update has been found to download.");
        await _updateManager.DownloadUpdatesAsync(_pendingUpdate);
    }

    public void ApplyUpdateAndRestart()
    {
        if (_pendingUpdate == null)
            throw new InvalidOperationException("No update has been downloaded.");
        _updateManager.ApplyUpdatesAndRestart(_pendingUpdate);
    }
}
