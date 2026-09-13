using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private readonly IAirportProjectStore? _projectStore;

    private string _icaoInput = string.Empty;
    private bool _isLoading;
    private string? _errorMessage;
    private AirportDetails? _airport;
    private AirportDiagram? _diagram;
    private IReadOnlyList<DataNode> _airportDataTree = [];
    private IReadOnlyList<TaxiPathEditViewModel> _taxiPathEdits = [];
    private IReadOnlyList<RunwayEditViewModel> _runwayEdits = [];
    private string? _lastExportPath;
    private string? _lastProjectSavePath;

    public string IcaoInput
    {
        get => _icaoInput;
        set
        {
            if (SetField(ref _icaoInput, value.ToUpperInvariant()))
            {
                LoadCommand.RaiseCanExecuteChanged();
                LoadProjectCommand.RaiseCanExecuteChanged();
            }
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

    // Editable per-item wrappers for the Edit tab, rebuilt alongside Diagram/
    // AirportDataTree whenever Airport changes. Edits made through these
    // write straight into Airport (see TaxiPathEditViewModel/
    // RunwayEditViewModel), so SaveProjectCommand always persists the latest
    // state with no separate "apply" step.
    public IReadOnlyList<TaxiPathEditViewModel> TaxiPathEdits
    {
        get => _taxiPathEdits;
        private set => SetField(ref _taxiPathEdits, value);
    }

    public IReadOnlyList<RunwayEditViewModel> RunwayEdits
    {
        get => _runwayEdits;
        private set => SetField(ref _runwayEdits, value);
    }

    // The shared, user-managed taxi name list backing every TaxiPathEditViewModel
    // .TaxiNameId picker. A single long-lived collection (cleared/repopulated on
    // each load, not replaced) so the Edit tab's ComboBoxes don't need to
    // rebind their ItemsSource on every airport load.
    public ObservableCollection<TaxiNameEditViewModel> TaxiNames { get; } = [];

    // Staging values for the "batch edit selected taxi paths" popover — see
    // TaxiwayBatchEditViewModel.
    public TaxiwayBatchEditViewModel TaxiwayBatchEdit { get; } = new();

    public string? LastProjectSavePath
    {
        get => _lastProjectSavePath;
        private set => SetField(ref _lastProjectSavePath, value);
    }

    // Backs the Edit tab's diagram multi-select + batch-edit popover.
    public int SelectedTaxiwayCount => SelectedTaxiwayShapes.Count();
    public bool HasTaxiwaySelection => SelectedTaxiwayCount > 0;

    // The popover only makes sense for a selection made by clicking the
    // diagram directly — selecting rows in the Taxi Paths grid (see
    // SyncTaxiwaySelectionFromRows) also highlights the matching shapes for
    // visual context, but shouldn't pop it open. Tracks which of the two
    // selection-changing paths fired most recently.
    private bool _selectionCameFromDiagram;
    public bool ShowTaxiwayBatchEditPopover => HasTaxiwaySelection && _selectionCameFromDiagram;

    private IEnumerable<TaxiwaySegmentShape> SelectedTaxiwayShapes =>
        Diagram?.TaxiwaySegments.Where(s => s.IsSelected) ?? [];

    public bool IsConnected => _simConnect.IsConnected;

    // True only when the dev-mode services were injected (Debug builds — see
    // App.xaml.cs). Drives the Export/Load Debug Data buttons' visibility so
    // the feature is invisible, not just disabled, in Release builds.
    public bool IsDevModeExportAvailable => _debugDataStore != null;
    public bool IsDevModeImportAvailable => _debugDataStore != null && _fileDialogService != null;

    // Drives the Edit tab's Save/Load Project buttons' visibility. Unlike the
    // Debug-mode export/import above, the project store is a real, always-on
    // feature — this is only nullable so it can be omitted in tests that
    // don't exercise it.
    public bool IsProjectStoreAvailable => _projectStore != null;

    // Exposed so MainWindow can drive Connect/Disconnect around the window
    // lifecycle without the ViewModel needing to know about HWNDs.
    public ISimConnectService SimConnect => _simConnect;

    public AsyncRelayCommand LoadCommand { get; }
    public RelayCommand ExportDebugDataCommand { get; }
    public RelayCommand LoadFromFileCommand { get; }
    public RelayCommand SaveProjectCommand { get; }
    public RelayCommand LoadProjectCommand { get; }
    public RelayCommand AddTaxiNameCommand { get; }
    public RelayCommand<TaxiNameEditViewModel> DeleteTaxiNameCommand { get; }
    public RelayCommand<TaxiwaySelectionRequest> ToggleTaxiwaySelectionCommand { get; }
    public RelayCommand ApplyTaxiwayBatchEditCommand { get; }
    public RelayCommand CloseTaxiwayBatchEditCommand { get; }

    public MainViewModel(ISimConnectService simConnect, IDebugDataStore? debugDataStore = null, IFileDialogService? fileDialogService = null, IAirportProjectStore? projectStore = null)
    {
        _simConnect = simConnect;
        _debugDataStore = debugDataStore;
        _fileDialogService = fileDialogService;
        _projectStore = projectStore;
        _simConnect.ConnectionChanged += (_, _) => OnPropertyChanged(nameof(IsConnected));
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => IsValidIcao(IcaoInput));
        ExportDebugDataCommand = new RelayCommand(ExportDebugData, () => _debugDataStore != null && Airport != null);
        LoadFromFileCommand = new RelayCommand(LoadFromFile, () => IsDevModeImportAvailable);
        SaveProjectCommand = new RelayCommand(SaveProject, () => _projectStore != null && Airport != null);
        LoadProjectCommand = new RelayCommand(LoadProject, () => _projectStore != null && IsValidIcao(IcaoInput));
        AddTaxiNameCommand = new RelayCommand(AddTaxiName, () => Airport != null);
        DeleteTaxiNameCommand = new RelayCommand<TaxiNameEditViewModel>(DeleteTaxiName, name => name != null);
        ToggleTaxiwaySelectionCommand = new RelayCommand<TaxiwaySelectionRequest>(ToggleTaxiwaySelection, request => request != null);
        ApplyTaxiwayBatchEditCommand = new RelayCommand(ApplyTaxiwayBatchEdit, () => ShowTaxiwayBatchEditPopover);
        CloseTaxiwayBatchEditCommand = new RelayCommand(CloseTaxiwayBatchEdit, () => ShowTaxiwayBatchEditPopover);
    }

    private static bool IsValidIcao(string icao) => icao.Length is >= 3 and <= 4;

    // Shared by every path that replaces the loaded airport (live SimConnect
    // lookup, debug-file import, project load) so Diagram/AirportDataTree/the
    // Edit tab's wrappers never drift out of sync with Airport.
    private void SetAirport(AirportDetails? airport)
    {
        UnsubscribeDiagramSelection();
        UnsubscribeTaxiPathEdits();
        UnsubscribeTaxiNames();
        UnsubscribeRunwayEdits();

        Airport = airport;
        Diagram = airport != null ? AirportDiagramProjector.Project(airport) : null;
        AirportDataTree = airport != null ? AirportDataTreeBuilder.Build(airport) : [];
        TaxiPathEdits = airport != null ? airport.TaxiPaths.Select(t => new TaxiPathEditViewModel(t)).ToList() : [];
        RunwayEdits = airport != null ? airport.Runways.Select(r => new RunwayEditViewModel(r)).ToList() : [];

        TaxiNames.Clear();
        if (airport != null)
            foreach (var name in airport.TaxiNames)
                TaxiNames.Add(new TaxiNameEditViewModel(name));

        SubscribeDiagramSelection();
        SubscribeTaxiPathEdits();
        SubscribeTaxiNames();
        SubscribeRunwayEdits();
        RefreshTaxiwaySelectionState();
        SaveProjectCommand.RaiseCanExecuteChanged();
        AddTaxiNameCommand.RaiseCanExecuteChanged();
    }

    // Every TaxiwaySegmentShape's IsSelected is mutable (see its own doc
    // comment) so the diagram can highlight a click without rebuilding the
    // whole projection — this keeps SelectedTaxiwayCount/HasTaxiwaySelection/
    // the batch-edit popover's staged values in sync with it. Unsubscribed
    // before every SetAirport so a stale shape from a previous load can't
    // leak a subscription.
    private void SubscribeDiagramSelection()
    {
        foreach (var shape in Diagram?.TaxiwaySegments ?? [])
            shape.PropertyChanged += OnTaxiwaySelectionChanged;
    }

    private void UnsubscribeDiagramSelection()
    {
        foreach (var shape in Diagram?.TaxiwaySegments ?? [])
            shape.PropertyChanged -= OnTaxiwaySelectionChanged;
    }

    private void OnTaxiwaySelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TaxiwaySegmentShape.IsSelected))
            RefreshTaxiwaySelectionState();
    }

    private void RefreshTaxiwaySelectionState()
    {
        OnPropertyChanged(nameof(SelectedTaxiwayCount));
        OnPropertyChanged(nameof(HasTaxiwaySelection));
        OnPropertyChanged(nameof(ShowTaxiwayBatchEditPopover));
        ApplyTaxiwayBatchEditCommand.RaiseCanExecuteChanged();
        CloseTaxiwayBatchEditCommand.RaiseCanExecuteChanged();

        // Only reset the popover's staged values once a batch-edit session
        // has actually ENDED (popover not showing) — not on every selection
        // change while it's open. An earlier revision reset unconditionally
        // here, which silently wiped out whatever the user had already typed
        // into the popover the moment they Ctrl+clicked another taxiway to
        // extend the selection — a real, reproduced bug (the popover's
        // ComboBox can keep showing a stale picked name after the underlying
        // value resets to null, since WPF doesn't necessarily clear a
        // ComboBox's display just because SelectedValue no longer matches any
        // item). Resetting only on close means a whole session's staged
        // values survive however the selection is built up, and always start
        // clean the next time the popover opens.
        if (!ShowTaxiwayBatchEditPopover)
            TaxiwayBatchEdit.ResetToUntouched();
    }

    // Keeps the diagram's taxiway labels/visibility in sync with edits made
    // through the grid or the batch popover, without re-running
    // AirportDiagramProjector.Project (which would also reset zoom/pan/
    // selection — nothing else in the Edit tab does that on every keystroke).
    private void SubscribeTaxiPathEdits()
    {
        foreach (var edit in TaxiPathEdits)
            edit.PropertyChanged += OnTaxiPathEditChanged;
    }

    private void UnsubscribeTaxiPathEdits()
    {
        foreach (var edit in TaxiPathEdits)
            edit.PropertyChanged -= OnTaxiPathEditChanged;
    }

    private void SubscribeTaxiNames()
    {
        foreach (var name in TaxiNames)
            name.PropertyChanged += OnTaxiNameEditChanged;
    }

    private void UnsubscribeTaxiNames()
    {
        foreach (var name in TaxiNames)
            name.PropertyChanged -= OnTaxiNameEditChanged;
    }

    private void OnTaxiPathEditChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TaxiPathEditViewModel.TaxiNameId):
                RefreshAllTaxiwayLabels();
                break;
            case nameof(TaxiPathEditViewModel.IsHiddenFromDiagram):
                RefreshAllTaxiwayVisibility();
                break;
        }
    }

    // A TaxiName's Value can be shared by several taxi paths, so renaming one
    // (from the Taxi Names panel) needs every shape pointing at it relabeled,
    // not just whichever path happened to trigger the change.
    private void OnTaxiNameEditChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TaxiNameEditViewModel.Value))
            RefreshAllTaxiwayLabels();
    }

    private void RefreshAllTaxiwayLabels()
    {
        if (Diagram is null) return;

        foreach (var shape in Diagram.TaxiwaySegments)
        {
            if (shape.SourceIndex >= TaxiPathEdits.Count) continue;
            var nameId = TaxiPathEdits[shape.SourceIndex].TaxiNameId;
            var name = nameId is { } id ? TaxiNames.FirstOrDefault(n => n.Id == id)?.Value ?? string.Empty : string.Empty;
            shape.Name = name;
            shape.HasName = !string.IsNullOrWhiteSpace(name);
        }
    }

    // Same idea as SubscribeTaxiPathEdits, for RunwayEditViewModel's own
    // hide-from-diagram toggle.
    private void SubscribeRunwayEdits()
    {
        foreach (var edit in RunwayEdits)
            edit.PropertyChanged += OnRunwayEditChanged;
    }

    private void UnsubscribeRunwayEdits()
    {
        foreach (var edit in RunwayEdits)
            edit.PropertyChanged -= OnRunwayEditChanged;
    }

    private void OnRunwayEditChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RunwayEditViewModel.IsHiddenFromDiagram))
            RefreshAllRunwayVisibility();
    }

    private void RefreshAllRunwayVisibility()
    {
        if (Diagram is null) return;

        foreach (var shape in Diagram.Runways)
        {
            if (shape.SourceIndex >= RunwayEdits.Count) continue;
            shape.IsVisible = !RunwayEdits[shape.SourceIndex].IsHiddenFromDiagram;
        }
    }

    private void RefreshAllTaxiwayVisibility()
    {
        if (Diagram is null) return;

        foreach (var shape in Diagram.TaxiwaySegments)
        {
            if (shape.SourceIndex >= TaxiPathEdits.Count) continue;
            var hidden = TaxiPathEdits[shape.SourceIndex].IsHiddenFromDiagram;
            shape.IsVisible = !hidden;
            // A hidden shape has nothing visible to highlight, and would
            // otherwise linger as a phantom in the selection count/popover.
            if (hidden) shape.IsSelected = false;
        }
    }

    private void AddTaxiName()
    {
        if (Airport is null) return;

        var name = new TaxiName();
        Airport.TaxiNames.Add(name);
        var nameEdit = new TaxiNameEditViewModel(name);
        nameEdit.PropertyChanged += OnTaxiNameEditChanged;
        TaxiNames.Add(nameEdit);
    }

    private void DeleteTaxiName(TaxiNameEditViewModel? name)
    {
        if (name is null || Airport is null) return;

        Airport.TaxiNames.RemoveAll(n => n.Id == name.Id);
        name.PropertyChanged -= OnTaxiNameEditChanged;
        TaxiNames.Remove(name);

        // No path should be left silently referencing a name that no longer
        // exists.
        foreach (var edit in TaxiPathEdits)
            edit.ClearTaxiNameIfReferencing(name.Id);
    }

    private void ToggleTaxiwaySelection(TaxiwaySelectionRequest? request)
    {
        if (request is null || Diagram is null) return;

        _selectionCameFromDiagram = true;

        if (!request.ExtendSelection)
        {
            foreach (var shape in Diagram.TaxiwaySegments)
                if (!ReferenceEquals(shape, request.Shape))
                    shape.IsSelected = false;
            request.Shape.IsSelected = true;
        }
        else
        {
            request.Shape.IsSelected = !request.Shape.IsSelected;
        }
    }

    // Called from MainWindow's code-behind on the Taxi Paths grid's
    // SelectionChanged (WPF's DataGrid has no bindable SelectedItems, so this
    // can't be a plain command binding like ToggleTaxiwaySelectionCommand) —
    // highlights the matching shapes for spatial context, same as a diagram
    // click, but deliberately doesn't open the batch-edit popover (see
    // ShowTaxiwayBatchEditPopover).
    //
    // Deliberately a no-op while a diagram-driven batch-edit session is
    // active (the popover is open): WPF's DataGrid marks a row "selected" as
    // a side effect of clicking ANY cell in it, including just toggling an
    // unrelated row's Left/Right Edge Lighted checkbox or Hide checkbox —
    // without this guard, that incidental click fires SelectionChanged and
    // silently replaces the diagram's selection with whatever row was
    // clicked, so Apply would end up hitting rows the user never
    // deliberately selected on the diagram at all. This was a real,
    // reported bug: batch-editing a diagram selection ended up changing
    // mostly rows that were never selected. The user has to Apply or Cancel
    // the popover before grid clicks can drive the diagram highlight again.
    public void SyncTaxiwaySelectionFromRows(IEnumerable<TaxiPathEditViewModel> selectedRows)
    {
        if (Diagram is null || ShowTaxiwayBatchEditPopover) return;

        _selectionCameFromDiagram = false;
        var selected = new HashSet<TaxiPathEditViewModel>(selectedRows);
        foreach (var shape in Diagram.TaxiwaySegments)
        {
            var edit = shape.SourceIndex < TaxiPathEdits.Count ? TaxiPathEdits[shape.SourceIndex] : null;
            shape.IsSelected = edit != null && selected.Contains(edit);
        }
    }

    private void ApplyTaxiwayBatchEdit()
    {
        foreach (var shape in SelectedTaxiwayShapes.ToList())
        {
            if (shape.SourceIndex >= TaxiPathEdits.Count) continue;
            var edit = TaxiPathEdits[shape.SourceIndex];
            // Only fields the user actually touched in the popover are
            // applied — an untouched field is left exactly as it was on each
            // individual selected path, never stamped with some other
            // selected path's value.
            if (TaxiwayBatchEdit.IsTaxiNameIdTouched)
                edit.TaxiNameId = TaxiwayBatchEdit.TaxiNameId;
            if (TaxiwayBatchEdit.LeftEdgeLighted is { } left)
                edit.LeftEdgeLighted = left;
            if (TaxiwayBatchEdit.RightEdgeLighted is { } right)
                edit.RightEdgeLighted = right;
        }

        // Closes the popover — applying is a "done, move on" action, not a
        // staging step you'd keep tweaking against the same selection.
        CloseTaxiwayBatchEdit();
    }

    private void CloseTaxiwayBatchEdit()
    {
        foreach (var shape in Diagram?.TaxiwaySegments ?? [])
            shape.IsSelected = false;
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        LastExportPath = null;
        LastProjectSavePath = null;
        try
        {
            var result = await _simConnect.GetAirportDetailsAsync(IcaoInput);
            SetAirport(result.Status == AirportLookupStatus.Success ? result.Airport : null);
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

        SetAirport(airport);
        ErrorMessage = null;
        LastExportPath = null;
        LastProjectSavePath = null;
        ExportDebugDataCommand.RaiseCanExecuteChanged();
    }

    private void SaveProject()
    {
        if (_projectStore is null || Airport is null)
            return;

        LastProjectSavePath = _projectStore.Save(Airport);
    }

    private void LoadProject()
    {
        if (_projectStore is null)
            return;

        var airport = _projectStore.Load(IcaoInput);
        if (airport is null)
        {
            ErrorMessage = $"No saved project found for \"{IcaoInput}\".";
            return;
        }

        SetAirport(airport);
        ErrorMessage = null;
        LastExportPath = null;
        LastProjectSavePath = null;
    }
}
