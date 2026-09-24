using System.Windows.Input;
using AirportSmith.Helpers;
using AirportSmith.Services;

namespace AirportSmith.ViewModels;

/// <summary>
/// Detects newly published AirportSmith releases. On startup
/// (CheckInBackgroundAsync) it silently checks and pre-downloads any newer
/// version, then shows an "update ready" badge; the user restarts into it
/// when convenient. "Check for Updates" does the same on demand but always
/// reports the outcome in StatusText. Update failures are never fatal —
/// no network, GitHub down, etc. just leave the app running as-is.
/// </summary>
public class UpdateViewModel : ViewModelBase
{
    private readonly IUpdateService _updateService;
    private bool _isBusy;
    private string? _readyVersion;
    private string? _statusText;

    public UpdateViewModel(IUpdateService updateService)
    {
        _updateService = updateService;
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckNowAsync);
        RestartToUpdateCommand = new RelayCommand(() => _updateService.ApplyUpdateAndRestart(), () => IsUpdateReady);
    }

    public string CurrentVersionText => $"v{_updateService.CurrentVersion}";

    /// <summary>Set once a newer version has been downloaded and is ready to install.</summary>
    public string? ReadyVersion
    {
        get => _readyVersion;
        private set
        {
            if (!SetField(ref _readyVersion, value)) return;
            OnPropertyChanged(nameof(IsUpdateReady));
            OnPropertyChanged(nameof(UpdateReadyText));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsUpdateReady => _readyVersion != null;

    public string? UpdateReadyText => _readyVersion == null ? null : $"⬆ v{_readyVersion} ready — click to restart";

    /// <summary>Outcome of the last manual check (null after a silent background check).</summary>
    public string? StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public ICommand CheckForUpdatesCommand { get; }
    public ICommand RestartToUpdateCommand { get; }

    /// <summary>Startup check: silent on "no update" and on any failure.</summary>
    public async Task CheckInBackgroundAsync()
    {
        if (!_updateService.IsInstalled) return;
        try { await FindAndDownloadAsync(); }
        catch { /* update failures are non-fatal */ }
    }

    private async Task CheckNowAsync()
    {
        if (!_updateService.IsInstalled)
        {
            StatusText = "Updates are only available in the installed version.";
            return;
        }

        StatusText = "Checking for updates…";
        try
        {
            string? version = await FindAndDownloadAsync();
            StatusText = version == null
                ? $"You are running the latest version ({CurrentVersionText})."
                : null; // the "ready" badge says it all
        }
        catch (Exception ex)
        {
            StatusText = $"Update check failed: {ex.Message}";
        }
    }

    private async Task<string?> FindAndDownloadAsync()
    {
        if (_isBusy) return null;
        _isBusy = true;
        try
        {
            string? version = await _updateService.CheckForUpdateAsync();
            if (version == null) return null;
            await _updateService.DownloadUpdateAsync();
            ReadyVersion = version;
            return version;
        }
        finally
        {
            _isBusy = false;
        }
    }
}
