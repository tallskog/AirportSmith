using AirportSmith.Helpers;
using AirportSmith.Models;
using AirportSmith.Models.Diagram;
using AirportSmith.Models.Inspector;
using AirportSmith.Services;

namespace AirportSmith.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly ISimConnectService _simConnect;
    private readonly IDebugDataStore? _debugDataStore;
    private readonly IFileDialogService? _fileDialogService;

    private string _icaoInput = string.Empty;
    private bool _isLoading;
    private string? _errorMessage;
    private AirportDetails? _airport;
    private AirportDiagram? _diagram;
    private IReadOnlyList<DataNode> _airportDataTree = [];
    private string? _lastExportPath;

    public string IcaoInput
    {
        get => _icaoInput;
        set
        {
            if (SetField(ref _icaoInput, value.ToUpperInvariant()))
                LoadCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    public AirportDetails? Airport
    {
        get => _airport;
        private set => SetField(ref _airport, value);
    }

    public AirportDiagram? Diagram
    {
        get => _diagram;
        private set => SetField(ref _diagram, value);
    }

    // Raw, expandable dump of everything on the loaded AirportDetails (see
    // AirportDataTreeBuilder) — backs the "Airport Data" tab, so the user
    // can check exactly what data is/isn't present without relying on the
    // per-tab DataGrids staying in sync with every model field added.
    public IReadOnlyList<DataNode> AirportDataTree
    {
        get => _airportDataTree;
        private set => SetField(ref _airportDataTree, value);
    }

    public string? LastExportPath
    {
        get => _lastExportPath;
        private set => SetField(ref _lastExportPath, value);
    }

    public bool IsConnected => _simConnect.IsConnected;

    // True only when the dev-mode services were injected (Debug builds — see
    // App.xaml.cs). Drives the Export/Load Debug Data buttons' visibility so
    // the feature is invisible, not just disabled, in Release builds.
    public bool IsDevModeExportAvailable => _debugDataStore != null;
    public bool IsDevModeImportAvailable => _debugDataStore != null && _fileDialogService != null;

    // Exposed so MainWindow can drive Connect/Disconnect around the window
    // lifecycle without the ViewModel needing to know about HWNDs.
    public ISimConnectService SimConnect => _simConnect;

    public AsyncRelayCommand LoadCommand { get; }
    public RelayCommand ExportDebugDataCommand { get; }
    public RelayCommand LoadFromFileCommand { get; }

    public MainViewModel(ISimConnectService simConnect, IDebugDataStore? debugDataStore = null, IFileDialogService? fileDialogService = null)
    {
        _simConnect = simConnect;
        _debugDataStore = debugDataStore;
        _fileDialogService = fileDialogService;
        _simConnect.ConnectionChanged += (_, _) => OnPropertyChanged(nameof(IsConnected));
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => IsValidIcao(IcaoInput));
        ExportDebugDataCommand = new RelayCommand(ExportDebugData, () => _debugDataStore != null && Airport != null);
        LoadFromFileCommand = new RelayCommand(LoadFromFile, () => IsDevModeImportAvailable);
    }

    private static bool IsValidIcao(string icao) => icao.Length is >= 3 and <= 4;

    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        LastExportPath = null;
        try
        {
            var result = await _simConnect.GetAirportDetailsAsync(IcaoInput);
            Airport = result.Status == AirportLookupStatus.Success ? result.Airport : null;
            Diagram = Airport != null ? AirportDiagramProjector.Project(Airport) : null;
            AirportDataTree = Airport != null ? AirportDataTreeBuilder.Build(Airport) : [];
            ErrorMessage = result.Status switch
            {
                AirportLookupStatus.Success => null,
                AirportLookupStatus.NotConnected => "MSFS is not running or not reachable. Start the sim and try again.",
                AirportLookupStatus.NotFound => $"No airport found for ICAO \"{IcaoInput}\".",
                AirportLookupStatus.Timeout => "Timed out waiting for a response from the sim.",
                _ => result.ErrorMessage ?? "An unexpected error occurred.",
            };
        }
        finally
        {
            IsLoading = false;
            ExportDebugDataCommand.RaiseCanExecuteChanged();
        }
    }

    private void ExportDebugData()
    {
        if (_debugDataStore is null || Airport is null)
            return;

        LastExportPath = _debugDataStore.Export(Airport);
    }

    private void LoadFromFile()
    {
        if (_debugDataStore is null || _fileDialogService is null)
            return;

        var path = _fileDialogService.ShowOpenJsonFileDialog(DebugDataStore.ExportDirectory);
        if (path is null)
            return;

        var airport = _debugDataStore.Import(path);
        if (airport is null)
        {
            ErrorMessage = $"Could not load debug data from \"{path}\".";
            return;
        }

        Airport = airport;
        Diagram = AirportDiagramProjector.Project(airport);
        AirportDataTree = AirportDataTreeBuilder.Build(airport);
        ErrorMessage = null;
        LastExportPath = null;
        ExportDebugDataCommand.RaiseCanExecuteChanged();
    }
}
