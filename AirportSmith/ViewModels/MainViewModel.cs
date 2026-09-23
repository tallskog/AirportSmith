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
    private readonly IAirportXmlExporter? _xmlExporter;
    private readonly IMapTileService? _mapTileService;
    private readonly IConfirmationService? _confirmationService;

    private string _icaoInput = string.Empty;
    private bool _isLoading;
    private string? _errorMessage;
    private AirportDetails? _airport;
    private AirportDiagram? _diagram;
    private IReadOnlyList<DataNode> _airportDataTree = [];
    private IReadOnlyList<TaxiPathEditViewModel> _taxiPathEdits = [];
    private IReadOnlyList<RunwayEditViewModel> _runwayEdits = [];
    private IReadOnlyList<TaxiwayPointEditViewModel> _taxiwayPointEdits = [];
    private string? _lastExportPath;
    private string? _lastProjectSavePath;
    private string? _lastXmlExportPath;
    private IReadOnlyList<string> _lastXmlExportWarnings = [];
    private bool _showMap;

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

    // What the Taxi Paths grid actually displays — all of TaxiPathEdits when
    // nothing is selected on the Edit tab's diagram, or just the row(s)
    // matching the current diagram selection otherwise (see
    // RefreshTaxiwayFilter). Deliberately a separate property from
    // TaxiPathEdits rather than filtering that one in place: everything else
    // that indexes TaxiPathEdits (ApplyTaxiwayBatchEdit,
    // RefreshAllTaxiwayLabels/Visibility) does so by SourceIndex against the
    // full, unfiltered list.
    private IReadOnlyList<TaxiPathEditViewModel> _visibleTaxiPathEdits = [];
    public IReadOnlyList<TaxiPathEditViewModel> VisibleTaxiPathEdits
    {
        get => _visibleTaxiPathEdits;
        private set => SetField(ref _visibleTaxiPathEdits, value);
    }

    public IReadOnlyList<RunwayEditViewModel> RunwayEdits
    {
        get => _runwayEdits;
        private set => SetField(ref _runwayEdits, value);
    }

    // Read/edit view over every distinct sim TAXI_POINT index resolved from
    // Airport.TaxiPaths — see TaxiwayPointEditViewModel.BuildAll. The
    // diagram's own TaxiwayPoints are computed independently by
    // AirportDiagramProjector straight from Airport (matched back to these
    // rows by Index, not list position — see TaxiwayPointShape's own doc
    // comment), so nothing here needs to push updates onto diagram shapes;
    // it's the other direction (a diagram click/hide-all toggle) that drives
    // VisibleTaxiwayPointEdits below.
    public IReadOnlyList<TaxiwayPointEditViewModel> TaxiwayPointEdits
    {
        get => _taxiwayPointEdits;
        private set => SetField(ref _taxiwayPointEdits, value);
    }

    // What the Taxiway Points grid actually displays — all of
    // TaxiwayPointEdits when nothing is selected on the Edit tab's diagram,
    // or just the row(s) matching the current point selection otherwise
    // (see RefreshTaxiwayPointFilter) — same "click a shape, see just that
    // shape's data" pattern as VisibleTaxiPathEdits, but as an independent
    // selection (selecting a taxiway path doesn't affect this, and vice
    // versa).
    private IReadOnlyList<TaxiwayPointEditViewModel> _visibleTaxiwayPointEdits = [];
    public IReadOnlyList<TaxiwayPointEditViewModel> VisibleTaxiwayPointEdits
    {
        get => _visibleTaxiwayPointEdits;
        private set => SetField(ref _visibleTaxiwayPointEdits, value);
    }

    // Editable per-spot wrappers for the Edit tab's Parking grid, built 1:1
    // (same order) from Airport.ParkingSpots — so a ParkingSpotShape's
    // SourceIndex is also its row's index here, no lookup needed. Edits write
    // straight into Airport, and OnParkingSpotEditChanged pushes the new
    // geometry onto the matching diagram shape.
    private IReadOnlyList<ParkingSpotEditViewModel> _parkingSpotEdits = [];
    public IReadOnlyList<ParkingSpotEditViewModel> ParkingSpotEdits
    {
        get => _parkingSpotEdits;
        private set => SetField(ref _parkingSpotEdits, value);
    }

    // What the Parking grid actually displays — every spot when none is
    // selected on the Edit tab's diagram, or just the selected one(s) (see
    // RefreshParkingSpotFilter). Same "click a shape, see just that shape's
    // data" pattern as VisibleTaxiwayPointEdits, as an independent selection.
    private IReadOnlyList<ParkingSpotEditViewModel> _visibleParkingSpotEdits = [];
    public IReadOnlyList<ParkingSpotEditViewModel> VisibleParkingSpotEdits
    {
        get => _visibleParkingSpotEdits;
        private set => SetField(ref _visibleParkingSpotEdits, value);
    }

    // Same all-or-nothing display-only toggle as HideAllTaxiwayPoints, for the
    // same reason (an airport can have hundreds of spots, so a per-row
    // checkbox would be impractical for decluttering) — never persisted,
    // never touches Airport, and hiding also clears any parking selection.
    private bool _hideAllParkingSpots;
    public bool HideAllParkingSpots
    {
        get => _hideAllParkingSpots;
        set
        {
            if (!SetField(ref _hideAllParkingSpots, value)) return;
            if (Diagram is null) return;

            foreach (var shape in Diagram.ParkingSpots)
            {
                shape.IsVisible = !value;
                if (value) shape.IsSelected = false;
            }
            RefreshParkingSpotFilter();
        }
    }

    // Set by ArmParkingPlacementCommand (a per-row "Place" button in the
    // Parking grid) and consumed by PlaceParkingCommand, which the diagram's
    // next background click fires — same click-to-place idea as
    // _armedVasiPlacement below. At most one of the two is ever armed: arming
    // either disarms the other, since a single background click can only
    // place one thing.
    private ParkingSpotEditViewModel? _armedParkingPlacement;
    public bool IsParkingPlacementArmed => _armedParkingPlacement != null;
    public string? ParkingPlacementStatusText => _armedParkingPlacement is { } armed
        ? $"Click the diagram to place parking spot {armed.Number}"
        : null;

    // A single all-or-nothing toggle for the Taxiway Points grid's "Hide All
    // from Diagram" checkbox — unlike TaxiPathEdits/RunwayEdits, individual
    // points have no per-row Edit tab checkbox of their own to hide just one,
    // since there can be hundreds of them (per-row would be impractical to
    // use for decluttering). A display-only workspace convenience like every
    // other IsHiddenFromDiagram/IsVisible toggle in this class — never
    // persisted, never touches Airport. Hiding also clears any current point
    // selection (a hidden point has nothing to highlight), same reasoning as
    // RefreshAllTaxiwayVisibility.
    private bool _hideAllTaxiwayPoints;
    public bool HideAllTaxiwayPoints
    {
        get => _hideAllTaxiwayPoints;
        set
        {
            if (!SetField(ref _hideAllTaxiwayPoints, value)) return;
            if (Diagram is null) return;

            foreach (var shape in Diagram.TaxiwayPoints)
            {
                shape.IsVisible = !value;
                if (value) shape.IsSelected = false;
            }
            RefreshTaxiwayPointFilter();
        }
    }

    // Set by one of the ArmVasiPlacementCommand family (a per-slot "Place"
    // button next to that VASI/PAPI's Bias X/Z/Spacing columns in the
    // Runways grid) and consumed by PlaceVasiCommand, which
    // AirportDiagramView's next background click on the Edit tab's diagram
    // fires (see its own VasiPlacementCommand). A workspace/interaction
    // state like HideAllTaxiwayPoints below — never persisted, reset
    // whenever a new airport loads.
    private (int RunwayIndex, VasiSlot Slot)? _armedVasiPlacement;

    // Drives a status label near the Edit tab's diagram so the user knows
    // placement mode is active and what they're about to place, since a
    // silent "click the diagram to set a value" mode would otherwise be easy
    // to forget about mid-session.
    public bool IsVasiPlacementArmed => _armedVasiPlacement != null;

    public string? VasiPlacementStatusText => _armedVasiPlacement is { } armed && armed.RunwayIndex < RunwayEdits.Count
        ? $"Click the diagram to place the {RunwayEdits[armed.RunwayIndex].PrimaryDesignation}/{RunwayEdits[armed.RunwayIndex].SecondaryDesignation} {DescribeVasiSlot(armed.Slot)} VASI/PAPI"
        : null;

    private static string DescribeVasiSlot(VasiSlot slot) => slot switch
    {
        VasiSlot.PrimaryLeft => "primary-end left",
        VasiSlot.PrimaryRight => "primary-end right",
        VasiSlot.SecondaryLeft => "secondary-end left",
        VasiSlot.SecondaryRight => "secondary-end right",
        _ => slot.ToString(),
    };

    // The shared, user-managed taxi name list, directly editable via the Edit
    // tab's own Taxi Names grid (add/rename/delete) — a single long-lived
    // collection (cleared/repopulated on each load, not replaced) so that
    // grid doesn't need to rebind its ItemsSource on every airport load.
    public ObservableCollection<TaxiNameEditViewModel> TaxiNames { get; } = [];

    // A SEPARATE view over the exact same items, for every OTHER "Name"
    // picker (the Taxi Paths grid's Name column ComboBox, one per row — up
    // to hundreds for a real airport) to bind against instead of TaxiNames
    // directly. Necessary, not cosmetic: WPF's CollectionViewSource caches
    // one shared default ICollectionView per SOURCE COLLECTION INSTANCE, so
    // if every one of those ComboBoxes bound to TaxiNames directly, they'd
    // all share the Taxi Names grid's own default view — and the DataGrid
    // puts that view into an "edit item" transaction while a row is being
    // edited there. If WPF then reattaches even one Name-column ComboBox's
    // ItemsSource binding to that same shared view during that window (e.g.
    // ordinary row-container virtualization as the grid scrolls, or
    // VisibleTaxiPathEdits being reassigned by RefreshTaxiwayFilter — both
    // completely unrelated to Taxi Names), WPF throws
    // InvalidOperationException ("'DeferRefresh' is not allowed during an
    // AddNew or Edit Item transaction") and the app crashes. Confirmed via a
    // real repro and a .NET Runtime crash log entry: clicking "Add Name" and
    // then clicking the new row to start editing it. A
    // ReadOnlyObservableCollection wrapping the same underlying TaxiNames
    // list is a genuinely separate collection instance, so it gets its own
    // independent default view — while still forwarding every
    // add/remove/CollectionChanged live, so renaming, adding, or deleting a
    // name here is still reflected immediately in every picker.
    public ReadOnlyObservableCollection<TaxiNameEditViewModel> TaxiNamesPicker { get; }

    // Staging values for the "batch edit selected taxi paths" popover — see
    // TaxiwayBatchEditViewModel.
    public TaxiwayBatchEditViewModel TaxiwayBatchEdit { get; } = new();

    // Per-column filter criteria for the Taxi Paths grid, entered directly
    // in each column's header — see TaxiPathFilterViewModel. Subscribed once
    // in the constructor (not per-load, like TaxiwayBatchEdit above) since
    // it's a single long-lived object; RefreshTaxiwayFilter re-runs on every
    // change so the grid updates as the user types/picks a filter value.
    public TaxiPathFilterViewModel TaxiPathFilter { get; } = new();

    public string? LastProjectSavePath
    {
        get => _lastProjectSavePath;
        private set => SetField(ref _lastProjectSavePath, value);
    }

    public string? LastXmlExportPath
    {
        get => _lastXmlExportPath;
        private set => SetField(ref _lastXmlExportPath, value);
    }

    // Skipped/defaulted/truncated data noticed while building the last XML
    // export (see AirportXmlExporter.Build) — shown to the user rather than
    // hidden, per this project's "surface gaps, don't hide them" style.
    public IReadOnlyList<string> LastXmlExportWarnings
    {
        get => _lastXmlExportWarnings;
        private set => SetField(ref _lastXmlExportWarnings, value);
    }

    // Backs the Edit tab's diagram multi-select + batch-edit popover.
    public int SelectedTaxiwayCount => SelectedTaxiwayShapes.Count();
    public bool HasTaxiwaySelection => SelectedTaxiwayCount > 0;

    // The popover no longer opens just because a selection exists — a plain
    // or Ctrl+click on the diagram only builds the selection (and filters
    // the Taxi Paths grid down to it, see RefreshTaxiwayFilter); the popover
    // only shows once the user explicitly right-clicks the diagram
    // (OpenTaxiwayBatchEditCommand) while that selection is non-empty. Reset
    // back to false whenever the popover actually closes (selection cleared,
    // Apply, or Cancel — see RefreshTaxiwaySelectionState) so a freshly built
    // selection always needs its own right-click, rather than reopening on
    // its own.
    private bool _batchEditPopoverRequested;
    public bool ShowTaxiwayBatchEditPopover => HasTaxiwaySelection && _batchEditPopoverRequested;

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

    // Drives the Export Airport XML button's visibility — a real, always-on
    // feature like the project store above (nullable only so it can be
    // omitted in tests that don't exercise it).
    public bool IsXmlExportAvailable => _xmlExporter != null && _fileDialogService != null;

    // Drives the Diagram/Edit tabs' "Show map" checkbox visibility — a real,
    // always-on feature like the project store/XML export above (nullable
    // only so it can be omitted in tests that don't exercise it).
    public bool IsMapAvailable => _mapTileService != null;

    // Bound to both the Diagram tab's and Edit tab's AirportDiagramView
    // instances (see MainWindow.xaml) and to their own "Show map" checkboxes
    // — one shared toggle for both. Off by default and deliberately
    // session-only: NOT persisted to the project file or any settings store,
    // since turning it on is this app's first outbound network call (sends
    // the loaded airport's coordinates and the user's IP to OpenStreetMap's
    // tile servers) — see requirements.md's phase-0 "OpenStreetMap tile
    // layer" entry and background-map-research.md.
    public bool ShowMap
    {
        get => _showMap;
        set => SetField(ref _showMap, value);
    }

    // Exposed for AirportDiagramView's MapTileService DependencyProperty
    // binding (see MainWindow.xaml) — the view itself does the actual
    // fetch/cache orchestration; this ViewModel only carries the reference.
    public IMapTileService? MapTileService => _mapTileService;

    // Exposed so MainWindow can drive Connect/Disconnect around the window
    // lifecycle without the ViewModel needing to know about HWNDs.
    public ISimConnectService SimConnect => _simConnect;

    public AsyncRelayCommand LoadCommand { get; }
    public RelayCommand ExportDebugDataCommand { get; }
    public RelayCommand LoadFromFileCommand { get; }
    public RelayCommand SaveProjectCommand { get; }
    public RelayCommand LoadProjectCommand { get; }
    public RelayCommand ExportXmlCommand { get; }
    public RelayCommand AddTaxiNameCommand { get; }
    public RelayCommand<TaxiNameEditViewModel> DeleteTaxiNameCommand { get; }
    public RelayCommand<TaxiwaySelectionRequest> ToggleTaxiwaySelectionCommand { get; }
    public RelayCommand<TaxiwayPointSelectionRequest> ToggleTaxiwayPointSelectionCommand { get; }
    public RelayCommand ClearTaxiwaySelectionCommand { get; }
    public RelayCommand OpenTaxiwayBatchEditCommand { get; }
    public RelayCommand ApplyTaxiwayBatchEditCommand { get; }
    public RelayCommand CloseTaxiwayBatchEditCommand { get; }
    public RelayCommand ClearTaxiPathFilterCommand { get; }
    public RelayCommand<RunwayEditViewModel> ArmPrimaryLeftVasiPlacementCommand { get; }
    public RelayCommand<RunwayEditViewModel> ArmPrimaryRightVasiPlacementCommand { get; }
    public RelayCommand<RunwayEditViewModel> ArmSecondaryLeftVasiPlacementCommand { get; }
    public RelayCommand<RunwayEditViewModel> ArmSecondaryRightVasiPlacementCommand { get; }
    public RelayCommand<Point2D> PlaceVasiCommand { get; }
    public RelayCommand<ParkingSpotSelectionRequest> ToggleParkingSpotSelectionCommand { get; }
    public RelayCommand<ParkingSpotEditViewModel> ArmParkingPlacementCommand { get; }
    public RelayCommand<Point2D> PlaceParkingCommand { get; }
    public RelayCommand DeleteSelectedParkingSpotsCommand { get; }

    public MainViewModel(ISimConnectService simConnect, IDebugDataStore? debugDataStore = null, IFileDialogService? fileDialogService = null, IAirportProjectStore? projectStore = null, IAirportXmlExporter? xmlExporter = null, IMapTileService? mapTileService = null, IConfirmationService? confirmationService = null)
    {
        _simConnect = simConnect;
        _debugDataStore = debugDataStore;
        _fileDialogService = fileDialogService;
        _projectStore = projectStore;
        _xmlExporter = xmlExporter;
        _mapTileService = mapTileService;
        _confirmationService = confirmationService;
        TaxiNamesPicker = new ReadOnlyObservableCollection<TaxiNameEditViewModel>(TaxiNames);
        _simConnect.ConnectionChanged += (_, _) => OnPropertyChanged(nameof(IsConnected));
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => IsValidIcao(IcaoInput));
        ExportDebugDataCommand = new RelayCommand(ExportDebugData, () => _debugDataStore != null && Airport != null);
        LoadFromFileCommand = new RelayCommand(LoadFromFile, () => IsDevModeImportAvailable);
        SaveProjectCommand = new RelayCommand(SaveProject, () => _projectStore != null && Airport != null);
        LoadProjectCommand = new RelayCommand(LoadProject, () => _projectStore != null && IsValidIcao(IcaoInput));
        ExportXmlCommand = new RelayCommand(ExportXml, () => IsXmlExportAvailable && Airport != null);
        AddTaxiNameCommand = new RelayCommand(AddTaxiName, () => Airport != null);
        DeleteTaxiNameCommand = new RelayCommand<TaxiNameEditViewModel>(DeleteTaxiName, name => name != null);
        ToggleTaxiwaySelectionCommand = new RelayCommand<TaxiwaySelectionRequest>(ToggleTaxiwaySelection, request => request != null);
        ToggleTaxiwayPointSelectionCommand = new RelayCommand<TaxiwayPointSelectionRequest>(ToggleTaxiwayPointSelection, request => request != null);
        ClearTaxiwaySelectionCommand = new RelayCommand(ClearTaxiwaySelection, () => Diagram != null);
        OpenTaxiwayBatchEditCommand = new RelayCommand(OpenTaxiwayBatchEdit, () => HasTaxiwaySelection);
        ApplyTaxiwayBatchEditCommand = new RelayCommand(ApplyTaxiwayBatchEdit, () => ShowTaxiwayBatchEditPopover);
        CloseTaxiwayBatchEditCommand = new RelayCommand(CloseTaxiwayBatchEdit, () => ShowTaxiwayBatchEditPopover);
        ClearTaxiPathFilterCommand = new RelayCommand(TaxiPathFilter.Reset, () => TaxiPathFilter.HasAnyFilter);
        ArmPrimaryLeftVasiPlacementCommand = new RelayCommand<RunwayEditViewModel>(edit => ArmVasiPlacement(edit, VasiSlot.PrimaryLeft), edit => edit != null);
        ArmPrimaryRightVasiPlacementCommand = new RelayCommand<RunwayEditViewModel>(edit => ArmVasiPlacement(edit, VasiSlot.PrimaryRight), edit => edit != null);
        ArmSecondaryLeftVasiPlacementCommand = new RelayCommand<RunwayEditViewModel>(edit => ArmVasiPlacement(edit, VasiSlot.SecondaryLeft), edit => edit != null);
        ArmSecondaryRightVasiPlacementCommand = new RelayCommand<RunwayEditViewModel>(edit => ArmVasiPlacement(edit, VasiSlot.SecondaryRight), edit => edit != null);
        PlaceVasiCommand = new RelayCommand<Point2D>(PlaceVasi, _ => _armedVasiPlacement != null);
        ToggleParkingSpotSelectionCommand = new RelayCommand<ParkingSpotSelectionRequest>(ToggleParkingSpotSelection, request => request != null);
        ArmParkingPlacementCommand = new RelayCommand<ParkingSpotEditViewModel>(ArmParkingPlacement, edit => edit != null);
        PlaceParkingCommand = new RelayCommand<Point2D>(PlaceParking, _ => _armedParkingPlacement != null);
        DeleteSelectedParkingSpotsCommand = new RelayCommand(DeleteSelectedParkingSpots, () => Diagram != null && Diagram.ParkingSpots.Any(p => p.IsSelected));
        // A single long-lived object (unlike TaxiwayBatchEdit, which is
        // reset per selection, not per subscription) — subscribed once here
        // rather than per-SetAirport, so a filter typed before an airport is
        // even loaded still applies once one is.
        TaxiPathFilter.PropertyChanged += (_, _) =>
        {
            RefreshTaxiwayFilter();
            ClearTaxiPathFilterCommand.RaiseCanExecuteChanged();
        };
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
        UnsubscribeParkingSpotEdits();

        Airport = airport;
        Diagram = airport != null ? AirportDiagramProjector.Project(airport) : null;
        AirportDataTree = airport != null ? AirportDataTreeBuilder.Build(airport) : [];
        TaxiPathEdits = airport != null ? airport.TaxiPaths.Select(t => new TaxiPathEditViewModel(t)).ToList() : [];
        RunwayEdits = airport != null ? airport.Runways.Select(r => new RunwayEditViewModel(r)).ToList() : [];
        TaxiwayPointEdits = airport != null ? TaxiwayPointEditViewModel.BuildAll(airport) : [];
        ParkingSpotEdits = airport != null ? ParkingSpotEditViewModel.BuildAll(airport) : [];

        // Bypasses the HideAllTaxiwayPoints setter's apply-to-diagram side
        // effect: a freshly projected Diagram's TaxiwayPoints already default
        // to IsVisible=true, so there's nothing to "un-hide" here — this just
        // resets the checkbox itself back to unchecked for the new airport.
        // Same for HideAllParkingSpots.
        if (_hideAllTaxiwayPoints)
        {
            _hideAllTaxiwayPoints = false;
            OnPropertyChanged(nameof(HideAllTaxiwayPoints));
        }
        if (_hideAllParkingSpots)
        {
            _hideAllParkingSpots = false;
            OnPropertyChanged(nameof(HideAllParkingSpots));
        }

        if (_armedVasiPlacement != null)
        {
            _armedVasiPlacement = null;
            RaiseVasiPlacementChanged();
        }
        if (_armedParkingPlacement != null)
        {
            _armedParkingPlacement = null;
            RaiseParkingPlacementChanged();
        }

        // Skipped entirely when the Id set/order already matches — see
        // SyncTaxiNames's own doc comment for why unconditionally
        // Clear()-ing this collection (even when nothing about it actually
        // changed) silently corrupts unrelated data.
        SyncTaxiNames(airport);

        SubscribeDiagramSelection();
        SubscribeTaxiPathEdits();
        SubscribeTaxiNames();
        SubscribeRunwayEdits();
        SubscribeParkingSpotEdits();
        RefreshTaxiwaySelectionState();
        // A new airport starts with every Taxi Paths column filter cleared —
        // a filter value typed against the previous airport (e.g. a taxi
        // name that doesn't exist on this one) would otherwise silently
        // leave the grid looking empty with no obvious reason why.
        TaxiPathFilter.Reset();
        RefreshTaxiwayFilter();
        RefreshTaxiwayPointFilter();
        RefreshParkingSpotFilter();
        SaveProjectCommand.RaiseCanExecuteChanged();
        ExportXmlCommand.RaiseCanExecuteChanged();
        AddTaxiNameCommand.RaiseCanExecuteChanged();
        ClearTaxiwaySelectionCommand.RaiseCanExecuteChanged();
        PlaceVasiCommand.RaiseCanExecuteChanged();
        PlaceParkingCommand.RaiseCanExecuteChanged();
    }

    // Rebuilds TaxiNames (the shared, user-editable name list backing the
    // Taxi Names panel AND every Taxi Paths grid row's Name ComboBox via
    // TaxiNamesPicker) from airport.TaxiNames — but ONLY when the Id
    // set/order has actually changed. Unconditionally Clear()-ing and
    // re-Add()-ing this collection on every SetAirport call (the previous
    // behavior) fires a CollectionChanged Reset on TaxiNamesPicker even when
    // nothing about the names changed — and the Taxi Paths grid's Name
    // ComboBox is two-way bound with UpdateSourceTrigger=PropertyChanged
    // (SelectedValue="{Binding TaxiNameId, ...}", MainWindow.xaml). A
    // ComboBox reacts to its ItemsSource momentarily having nothing matching
    // its current selection by clearing SelectedValue, which — with that
    // trigger — writes null straight back into whatever TaxiPathSegment the
    // still-live grid row's DataContext currently wraps.
    //
    // On every OTHER SetAirport caller (a genuine new-airport load —
    // LoadAsync/LoadFromFile/LoadProjectCommand) this was harmless: the row
    // being momentarily cleared belongs to the PREVIOUS airport's
    // TaxiPathSegment objects, discarded regardless. But SetAirport is now
    // also called after an in-place structural edit to the SAME loaded
    // airport (DeleteSelectedParkingSpotsCommand), where the "old" row wraps
    // the SAME, SURVIVING TaxiPathSegment — permanently wiping its real
    // TaxiNameId. Confirmed against a real OIBK export: deleting one
    // unrelated parking spot silently cleared the exported `name` attribute
    // off several on-screen taxi paths that had nothing to do with the
    // deleted spot, orphaning their taxi points' visible names in the
    // Scenery Editor. Skipping the rebuild when nothing changed avoids the
    // whole failure mode rather than trying to out-race WPF's own
    // virtualization/selection timing.
    private void SyncTaxiNames(AirportDetails? airport)
    {
        var airportNames = airport?.TaxiNames ?? [];
        if (airportNames.Count == TaxiNames.Count && airportNames.Select(n => n.Id).SequenceEqual(TaxiNames.Select(vm => vm.Id)))
            return;

        TaxiNames.Clear();
        foreach (var name in airportNames)
            TaxiNames.Add(new TaxiNameEditViewModel(name));
    }

    private void SubscribeParkingSpotEdits()
    {
        foreach (var edit in ParkingSpotEdits)
            edit.PropertyChanged += OnParkingSpotEditChanged;
    }

    private void UnsubscribeParkingSpotEdits()
    {
        foreach (var edit in ParkingSpotEdits)
            edit.PropertyChanged -= OnParkingSpotEditChanged;
    }

    // Number (label text), Heading (tip), Radius (size + tip distance) and
    // Bias X/Z (position) all affect what's drawn; Type/Name/Suffix don't.
    private void OnParkingSpotEditChanged(object? sender, PropertyChangedEventArgs e)
    {
        var affectsDiagram = e.PropertyName is nameof(ParkingSpotEditViewModel.Number) or nameof(ParkingSpotEditViewModel.HeadingDeg)
            or nameof(ParkingSpotEditViewModel.RadiusMeters) or nameof(ParkingSpotEditViewModel.BiasXMeters)
            or nameof(ParkingSpotEditViewModel.BiasZMeters);
        if (!affectsDiagram || sender is not ParkingSpotEditViewModel edit) return;

        RefreshParkingShape(edit);

        // Only a Bias X/Z change moves any linked Parking-type path's line —
        // Number/Heading/Radius only affect the spot's own dot/label/tip.
        if (e.PropertyName is nameof(ParkingSpotEditViewModel.BiasXMeters) or nameof(ParkingSpotEditViewModel.BiasZMeters))
            RefreshParkingLeadingTaxiways(edit);
    }

    // Pushes recomputed geometry straight onto the one ParkingSpotShape this
    // edit corresponds to instead of re-running the whole projection — see
    // ParkingSpotShape's own doc comment for why (would reset zoom/pan/
    // selection).
    private void RefreshParkingShape(ParkingSpotEditViewModel edit)
    {
        if (Diagram is null || Airport is null) return;
        var index = IndexOfParkingSpotEdit(edit);
        if (index < 0 || index >= Diagram.ParkingSpots.Count) return;

        var shape = Diagram.ParkingSpots[index];
        var (center, tip) = AirportDiagramProjector.ComputeParkingPlacement(Diagram, Airport.ParkingSpots[index]);
        shape.Center = center;
        shape.HeadingTip = tip;
        shape.RadiusMeters = edit.RadiusMeters;
        shape.Label = edit.Number.ToString();
    }

    // Moves every Parking-type taxi path's line that leads into this spot,
    // after its Bias X/Z changed — otherwise the spot's own dot (above) moves
    // but its lead-in stub stays stranded at the old position, a visible
    // desync now that Parking-type paths are drawn at all (see
    // TaxiwaySegmentShape.IsParkingType's own doc comment). edit.LinkedParkingPaths
    // is the same TaxiPathSegment list ParkingSpotEditViewModel's own
    // BiasXMeters/BiasZMeters setters already wrote EndXMeters/EndZMeters
    // through to, so their coordinates are already current here — this just
    // needs to find each linked path's TaxiwaySegmentShape (by matching
    // SourceIndex, since Airport.TaxiPaths.IndexOf finds the source list
    // position by reference) and push the recomputed screen geometry onto it.
    private void RefreshParkingLeadingTaxiways(ParkingSpotEditViewModel edit)
    {
        if (Diagram is null || Airport is null) return;

        foreach (var path in edit.LinkedParkingPaths)
        {
            var sourceIndex = Airport.TaxiPaths.IndexOf(path);
            if (sourceIndex < 0) continue;

            var shape = Diagram.TaxiwaySegments.FirstOrDefault(s => s.SourceIndex == sourceIndex);
            if (shape is null) continue;

            var placement = AirportDiagramProjector.ComputeTaxiwaySegmentPlacement(Diagram, path);
            if (placement is not { } p) continue;

            shape.End = p.End;
            shape.MidPoint = p.MidPoint;
            shape.WidthCorners = p.WidthCorners;
        }
    }

    private int IndexOfParkingSpotEdit(ParkingSpotEditViewModel edit)
    {
        for (var i = 0; i < ParkingSpotEdits.Count; i++)
            if (ReferenceEquals(ParkingSpotEdits[i], edit)) return i;
        return -1;
    }

    // Same click-to-select/Ctrl+click-to-extend pattern as
    // ToggleTaxiwayPointSelection, as its own independent selection.
    private void ToggleParkingSpotSelection(ParkingSpotSelectionRequest? request)
    {
        if (request is null || Diagram is null) return;

        if (!request.ExtendSelection)
        {
            foreach (var shape in Diagram.ParkingSpots)
                if (!ReferenceEquals(shape, request.Shape))
                    shape.IsSelected = false;
            request.Shape.IsSelected = true;
        }
        else
        {
            request.Shape.IsSelected = !request.Shape.IsSelected;
        }

        RefreshParkingSpotFilter();
    }

    // Matched by list position (ParkingSpotShape.SourceIndex == index into
    // ParkingSpotEdits) — see ParkingSpotEdits.
    private void RefreshParkingSpotFilter()
    {
        if (Diagram is null)
        {
            VisibleParkingSpotEdits = ParkingSpotEdits;
            return;
        }

        var selectedIndexes = Diagram.ParkingSpots.Where(p => p.IsSelected).Select(p => p.SourceIndex).ToHashSet();
        VisibleParkingSpotEdits = selectedIndexes.Count == 0
            ? ParkingSpotEdits
            : ParkingSpotEdits.Where((_, i) => selectedIndexes.Contains(i)).ToList();
    }

    private void ArmParkingPlacement(ParkingSpotEditViewModel? edit)
    {
        if (edit is null || IndexOfParkingSpotEdit(edit) < 0) return;

        _armedVasiPlacement = null;
        RaiseVasiPlacementChanged();
        PlaceVasiCommand.RaiseCanExecuteChanged();

        _armedParkingPlacement = edit;
        RaiseParkingPlacementChanged();
        PlaceParkingCommand.RaiseCanExecuteChanged();
    }

    private void PlaceParking(Point2D point)
    {
        if (_armedParkingPlacement is not { } armed || Diagram is null) return;

        var (biasX, biasZ) = AirportDiagramProjector.ComputeParkingBias(Diagram, point);
        armed.BiasXMeters = biasX;
        armed.BiasZMeters = biasZ;

        _armedParkingPlacement = null;
        RaiseParkingPlacementChanged();
        PlaceParkingCommand.RaiseCanExecuteChanged();
    }

    private void RaiseParkingPlacementChanged()
    {
        OnPropertyChanged(nameof(IsParkingPlacementArmed));
        OnPropertyChanged(nameof(ParkingPlacementStatusText));
    }

    // Deletes every currently-selected parking spot (diagram click/Ctrl+click
    // selection — see ToggleParkingSpotSelection), after confirming with the
    // user first (no undo, and this is triggered by a Delete keypress — much
    // easier to hit by accident than a button click). Also removes any
    // Type==Parking taxi path whose EndIndex references a deleted spot's
    // ItemIndex, since a path left pointing at a spot that no longer exists
    // is genuinely invalid data — unlike DeleteTaxiName's "detach, don't
    // cascade" precedent, which works because a taxi name has a valid "no
    // name" state that a parking path's endpoint doesn't.
    private void DeleteSelectedParkingSpots()
    {
        if (Airport is null || Diagram is null) return;

        var selectedPositions = Diagram.ParkingSpots.Where(p => p.IsSelected).Select(p => p.SourceIndex).ToList();
        if (selectedPositions.Count == 0) return;

        var message = selectedPositions.Count == 1
            ? "Delete 1 parking spot? Any taxi path connecting to it will be deleted too."
            : $"Delete {selectedPositions.Count} parking spots? Any taxi path connecting to them will be deleted too.";
        if (_confirmationService != null && !_confirmationService.Confirm(message)) return;

        // Only cascade-delete paths for spots whose ItemIndex is actually
        // unique among the airport's spots — same guard
        // ParkingSpotEditViewModel.BuildAll uses, since a colliding
        // ItemIndex (pre-ItemIndex-migration/legacy data) can't be safely
        // attributed to just the spot being deleted here.
        var indexCounts = Airport.ParkingSpots.GroupBy(s => s.ItemIndex).ToDictionary(g => g.Key, g => g.Count());
        var itemIndexesToCascade = selectedPositions
            .Select(i => Airport.ParkingSpots[i].ItemIndex)
            .Where(itemIndex => indexCounts.GetValueOrDefault(itemIndex) == 1)
            .ToHashSet();

        Airport.TaxiPaths.RemoveAll(p => p.Type == TaxiPathType.Parking && itemIndexesToCascade.Contains(p.EndIndex));

        // Descending, so removing one position doesn't shift the meaning of
        // any position still to be removed.
        foreach (var position in selectedPositions.OrderByDescending(i => i))
            Airport.ParkingSpots.RemoveAt(position);

        // Rebuilds Diagram/ParkingSpotEdits/TaxiPathEdits/every filter and
        // subscription from the mutated Airport — the same path every
        // airport load/reload already uses (SetAirport's own doc comment).
        // Also resets other session-only state (taxiway/point selection,
        // Hide All checkboxes, the Taxi Path filter) as a known, accepted
        // side effect rather than hand-patching every derived collection
        // surgically for this first pass.
        SetAirport(Airport);
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
        OpenTaxiwayBatchEditCommand.RaiseCanExecuteChanged();
        ApplyTaxiwayBatchEditCommand.RaiseCanExecuteChanged();
        CloseTaxiwayBatchEditCommand.RaiseCanExecuteChanged();

        // Only reset the popover's staged values (and the right-click
        // "requested" flag itself) once a batch-edit session has actually
        // ENDED (popover not showing) — not on every selection change while
        // it's open. An earlier revision reset unconditionally here, which
        // silently wiped out whatever the user had already typed into the
        // popover the moment they Ctrl+clicked another taxiway to extend the
        // selection — a real, reproduced bug (the popover's ComboBox can
        // keep showing a stale picked name after the underlying value resets
        // to null, since WPF doesn't necessarily clear a ComboBox's display
        // just because SelectedValue no longer matches any item). Resetting
        // only on close means a whole session's staged values survive
        // however the selection is built up, and always start clean the
        // next time the popover opens — and resetting
        // _batchEditPopoverRequested here means a brand-new selection always
        // needs its own right-click, rather than the popover popping back
        // open on its own the moment HasTaxiwaySelection turns true again.
        if (!ShowTaxiwayBatchEditPopover)
        {
            _batchEditPopoverRequested = false;
            TaxiwayBatchEdit.ResetToUntouched();
        }
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

        // Any edited field could be the one an active Taxi Paths column
        // filter is checking — e.g. changing a path's Type away from the
        // value a Type filter is set to should drop it out of
        // VisibleTaxiPathEdits immediately, same as typing that filter value
        // in the first place would have. Unconditional (not just for the two
        // cases above) since every grid column is filterable.
        RefreshTaxiwayFilter();
    }

    // A TaxiName's Value can be shared by several taxi paths, so renaming one
    // (from the Taxi Names panel) needs every shape pointing at it relabeled,
    // not just whichever path happened to trigger the change.
    private void OnTaxiNameEditChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TaxiNameEditViewModel.Value)) return;

        RefreshAllTaxiwayLabels();
        // A Name filter matches the resolved display value, not TaxiNameId
        // itself — renaming the entry a filtered-in path points at can change
        // whether it still matches.
        RefreshTaxiwayFilter();
    }

    private void RefreshAllTaxiwayLabels()
    {
        if (Diagram is null) return;

        foreach (var shape in Diagram.TaxiwaySegments)
        {
            if (shape.SourceIndex >= TaxiPathEdits.Count) continue;
            var name = ResolveTaxiNameValue(TaxiPathEdits[shape.SourceIndex].TaxiNameId);
            shape.Name = name;
            shape.HasName = !string.IsNullOrWhiteSpace(name);
        }
    }

    // Shared by RefreshAllTaxiwayLabels (diagram label text) and
    // MatchesTaxiPathFilter (Name filter matching) — both need the same
    // TaxiNameId -> display string resolution against the shared TaxiNames
    // list.
    private string ResolveTaxiNameValue(Guid? taxiNameId) =>
        taxiNameId is { } id ? TaxiNames.FirstOrDefault(n => n.Id == id)?.Value ?? string.Empty : string.Empty;

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

        if (sender is RunwayEditViewModel edit && VasiSlotForProperty(e.PropertyName) is { } slot)
            RefreshVasiShape(edit, slot);
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

    // Maps a changed RunwayEditViewModel property back to the VASI slot it
    // affects the diagram geometry of — Type (installed/not), BiasX/BiasZ
    // (position), and Spacing (wing bar width) all matter; AngleDeg doesn't
    // affect anything drawn, so it's deliberately not mapped here.
    private static VasiSlot? VasiSlotForProperty(string? propertyName) => propertyName switch
    {
        nameof(RunwayEditViewModel.PrimaryLeftVasiType) or nameof(RunwayEditViewModel.PrimaryLeftVasiBiasXMeters)
            or nameof(RunwayEditViewModel.PrimaryLeftVasiBiasZMeters) or nameof(RunwayEditViewModel.PrimaryLeftVasiSpacingMeters)
            => VasiSlot.PrimaryLeft,
        nameof(RunwayEditViewModel.PrimaryRightVasiType) or nameof(RunwayEditViewModel.PrimaryRightVasiBiasXMeters)
            or nameof(RunwayEditViewModel.PrimaryRightVasiBiasZMeters) or nameof(RunwayEditViewModel.PrimaryRightVasiSpacingMeters)
            => VasiSlot.PrimaryRight,
        nameof(RunwayEditViewModel.SecondaryLeftVasiType) or nameof(RunwayEditViewModel.SecondaryLeftVasiBiasXMeters)
            or nameof(RunwayEditViewModel.SecondaryLeftVasiBiasZMeters) or nameof(RunwayEditViewModel.SecondaryLeftVasiSpacingMeters)
            => VasiSlot.SecondaryLeft,
        nameof(RunwayEditViewModel.SecondaryRightVasiType) or nameof(RunwayEditViewModel.SecondaryRightVasiBiasXMeters)
            or nameof(RunwayEditViewModel.SecondaryRightVasiBiasZMeters) or nameof(RunwayEditViewModel.SecondaryRightVasiSpacingMeters)
            => VasiSlot.SecondaryRight,
        _ => null,
    };

    // Pushes a recomputed Position/WingBarStart/WingBarEnd/IsInstalled
    // straight onto the one VasiShape this edit/slot corresponds to, instead
    // of re-running the whole projection — see VasiShape's own doc comment
    // for why (would reset zoom/pan/selection).
    private void RefreshVasiShape(RunwayEditViewModel edit, VasiSlot slot)
    {
        if (Diagram is null || Airport is null) return;
        var runwayIndex = IndexOfRunwayEdit(edit);
        if (runwayIndex < 0) return;

        var shape = Diagram.VasiLights.FirstOrDefault(v => v.SourceRunwayIndex == runwayIndex && v.Slot == slot);
        if (shape is null) return;

        var placement = AirportDiagramProjector.ComputeVasiPlacement(Diagram, Airport, runwayIndex, slot);
        shape.Position = placement.Position;
        shape.WingBarStart = placement.WingBarStart;
        shape.WingBarEnd = placement.WingBarEnd;
        shape.IsInstalled = placement.IsInstalled;
    }

    // RunwayEdits is built 1:1, unfiltered, from Airport.Runways (see
    // SetAirport) — an ordinary reference-equality search is the simplest
    // way back to that same position, and runway counts are small enough
    // that this never needs to be an indexed lookup.
    private int IndexOfRunwayEdit(RunwayEditViewModel edit)
    {
        for (var i = 0; i < RunwayEdits.Count; i++)
            if (ReferenceEquals(RunwayEdits[i], edit)) return i;
        return -1;
    }

    // Arms click-to-place for one VASI/PAPI slot — the next background click
    // on the Edit tab's diagram (see PlaceVasi) writes that slot's Bias X/Z
    // from the clicked position instead of clearing the taxiway selection.
    private void ArmVasiPlacement(RunwayEditViewModel? edit, VasiSlot slot)
    {
        if (edit is null) return;
        var runwayIndex = IndexOfRunwayEdit(edit);
        if (runwayIndex < 0) return;

        if (_armedParkingPlacement != null)
        {
            _armedParkingPlacement = null;
            RaiseParkingPlacementChanged();
            PlaceParkingCommand.RaiseCanExecuteChanged();
        }

        _armedVasiPlacement = (runwayIndex, slot);
        RaiseVasiPlacementChanged();
        PlaceVasiCommand.RaiseCanExecuteChanged();
    }

    private void PlaceVasi(Point2D point)
    {
        if (_armedVasiPlacement is not { } armed || Airport is null || Diagram is null) return;
        if (armed.RunwayIndex >= RunwayEdits.Count)
        {
            _armedVasiPlacement = null;
            RaiseVasiPlacementChanged();
            return;
        }

        var (biasX, biasZ) = AirportDiagramProjector.ComputeVasiBias(Diagram, Airport, armed.RunwayIndex, armed.Slot, point);
        SetVasiBias(RunwayEdits[armed.RunwayIndex], armed.Slot, biasX, biasZ);

        _armedVasiPlacement = null;
        RaiseVasiPlacementChanged();
        PlaceVasiCommand.RaiseCanExecuteChanged();
    }

    private void RaiseVasiPlacementChanged()
    {
        OnPropertyChanged(nameof(IsVasiPlacementArmed));
        OnPropertyChanged(nameof(VasiPlacementStatusText));
    }

    private static void SetVasiBias(RunwayEditViewModel edit, VasiSlot slot, double biasX, double biasZ)
    {
        switch (slot)
        {
            case VasiSlot.PrimaryLeft:
                edit.PrimaryLeftVasiBiasXMeters = biasX;
                edit.PrimaryLeftVasiBiasZMeters = biasZ;
                break;
            case VasiSlot.PrimaryRight:
                edit.PrimaryRightVasiBiasXMeters = biasX;
                edit.PrimaryRightVasiBiasZMeters = biasZ;
                break;
            case VasiSlot.SecondaryLeft:
                edit.SecondaryLeftVasiBiasXMeters = biasX;
                edit.SecondaryLeftVasiBiasZMeters = biasZ;
                break;
            case VasiSlot.SecondaryRight:
                edit.SecondaryRightVasiBiasXMeters = biasX;
                edit.SecondaryRightVasiBiasZMeters = biasZ;
                break;
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

        RefreshTaxiwayFilter();
    }

    // Same click-to-select/Ctrl+click-to-extend pattern as
    // ToggleTaxiwaySelection, for taxiway point markers — kept as an
    // independent selection (its own HashSet-equivalent of IsSelected flags
    // on Diagram.TaxiwayPoints, its own filtered grid) rather than sharing
    // state with the taxi-path selection, since they're separate shapes in
    // separate grids with no batch-edit popover of their own.
    private void ToggleTaxiwayPointSelection(TaxiwayPointSelectionRequest? request)
    {
        if (request is null || Diagram is null) return;

        if (!request.ExtendSelection)
        {
            foreach (var shape in Diagram.TaxiwayPoints)
                if (!ReferenceEquals(shape, request.Shape))
                    shape.IsSelected = false;
            request.Shape.IsSelected = true;
        }
        else
        {
            request.Shape.IsSelected = !request.Shape.IsSelected;
        }

        RefreshTaxiwayPointFilter();
    }

    // Bound to a plain click (no drag) on empty diagram space — see
    // AirportDiagramView.TaxiwayClearSelectionCommand/EndPan. Deselects every
    // taxiway, taxiway point AND parking spot (all three selections live on
    // the same diagram, so a background click resets them at once), which in
    // turn drops the Taxi Paths/Taxiway Points/Parking grids' filters back to "show
    // everything" and closes the batch-edit popover if it was open
    // (ShowTaxiwayBatchEditPopover requires a non-empty taxiway selection).
    private void ClearTaxiwaySelection()
    {
        if (Diagram is null) return;

        foreach (var shape in Diagram.TaxiwaySegments)
            shape.IsSelected = false;
        foreach (var shape in Diagram.TaxiwayPoints)
            shape.IsSelected = false;
        foreach (var shape in Diagram.ParkingSpots)
            shape.IsSelected = false;
        RefreshTaxiwayFilter();
        RefreshTaxiwayPointFilter();
        RefreshParkingSpotFilter();
    }

    // Bound to a right-click on the diagram (see
    // AirportDiagramView.TaxiwayContextMenuCommand/OnMouseRightButtonDown).
    // Right-clicking never changes the selection itself, only whether the
    // popover is shown for whatever's currently selected — see
    // ShowTaxiwayBatchEditPopover.
    private void OpenTaxiwayBatchEdit()
    {
        _batchEditPopoverRequested = true;
        RefreshTaxiwaySelectionState();
    }

    // The Taxi Paths grid shows every row when nothing is selected on the
    // diagram, or just the row(s) whose SourceIndex matches a selected shape
    // otherwise — "click a taxiway, see just that taxiway's data" per the
    // Edit tab's diagram-driven selection. Deliberately only called from the
    // diagram-selection-changing paths above (ToggleTaxiwaySelection/
    // ClearTaxiwaySelection/CloseTaxiwayBatchEdit), NOT from
    // SyncTaxiwaySelectionFromRows — selecting rows directly in an
    // already-rendered grid to highlight the diagram shouldn't also collapse
    // the grid down to just those rows out from under the user.
    //
    // Reassigning VisibleTaxiPathEdits swaps the Taxi Paths grid's
    // ItemsSource, and WPF's DataGrid implicitly clears its own selection and
    // raises SelectionChanged as a result of that — asynchronously (a later
    // Dispatcher pass, not inline with this method call) and more than once
    // (instrumenting it showed a burst of several such events within a
    // couple of milliseconds of the swap). MainWindow's code-behind wires
    // that event straight into SyncTaxiwaySelectionFromRows, so without a
    // guard there, this was a real, reproduced bug: clicking a taxiway
    // correctly filtered the grid and selected the shape, but the
    // ItemsSource swap's own delayed SelectionChanged burst then fired
    // SyncTaxiwaySelectionFromRows with the grid's now-empty selection,
    // which stamped EVERY shape's IsSelected back to false — silently
    // undoing the very selection that triggered the filter change in the
    // first place. Because the async, multi-event timing rules out a simple
    // same-call-stack flag here, the guard lives in MainWindow.xaml.cs's
    // TaxiPathsGrid_SelectionChanged instead (it tracks the grid's
    // ItemsSource reference and debounces for a short settle window after
    // it changes) — see that handler's doc comment for the full
    // explanation. VisibleTaxiPathEdits itself stayed correct throughout,
    // since it isn't recomputed by that second call, which is what made it
    // look like "the grid filters correctly but nothing ever highlights,
    // Ctrl+click multi-select does nothing, and right-click has no
    // selection to open a popover for."
    private void RefreshTaxiwayFilter()
    {
        // The diagram click-to-select filter narrows TaxiPathEdits down to
        // the selected shape(s) first (or leaves every row as candidates if
        // nothing's selected); TaxiPathFilter's per-column criteria then
        // narrow THAT further — the two combine (AND), neither replaces the
        // other. A field filter with no diagram selection active still
        // applies against every row.
        IEnumerable<TaxiPathEditViewModel> candidates = TaxiPathEdits;
        if (Diagram != null)
        {
            var selectedIndexes = SelectedTaxiwayShapes.Select(s => s.SourceIndex).ToHashSet();
            if (selectedIndexes.Count > 0)
                candidates = TaxiPathEdits.Where((_, i) => selectedIndexes.Contains(i));
        }

        VisibleTaxiPathEdits = candidates.Where(MatchesTaxiPathFilter).ToList();
    }

    // Every column in the Taxi Paths grid has its own filter (TaxiPathFilter)
    // — a string filter (Name/Start/End/Rwy #) matches as a case-insensitive
    // substring against the field's displayed text, an enum/bool filter
    // (null meaning "(any)") requires an exact match. An empty/null filter
    // value always matches, so a row with every filter left blank always
    // passes.
    private bool MatchesTaxiPathFilter(TaxiPathEditViewModel edit)
    {
        var f = TaxiPathFilter;

        if (!string.IsNullOrEmpty(f.NameFilter) &&
            !ResolveTaxiNameValue(edit.TaxiNameId).Contains(f.NameFilter, StringComparison.OrdinalIgnoreCase))
            return false;
        if (f.TypeFilter is { } type && edit.Type != type) return false;
        if (!string.IsNullOrEmpty(f.StartIndexFilter) &&
            !edit.StartIndex.ToString().Contains(f.StartIndexFilter, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrEmpty(f.EndIndexFilter) &&
            !edit.EndIndex.ToString().Contains(f.EndIndexFilter, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrEmpty(f.RunwayNumberFilter) &&
            !edit.RunwayNumber.ToString().Contains(f.RunwayNumberFilter, StringComparison.OrdinalIgnoreCase))
            return false;
        if (f.RunwayDesignatorFilter is { } rd && edit.RunwayDesignator != rd) return false;
        if (f.LeftEdgeFilter is { } le && edit.LeftEdge != le) return false;
        if (f.LeftEdgeLightedFilter is { } lel && edit.LeftEdgeLighted != lel) return false;
        if (f.RightEdgeFilter is { } re && edit.RightEdge != re) return false;
        if (f.RightEdgeLightedFilter is { } rel && edit.RightEdgeLighted != rel) return false;
        if (f.CenterLineFilter is { } cl && edit.CenterLine != cl) return false;
        if (f.CenterLineLightedFilter is { } cll && edit.CenterLineLighted != cll) return false;
        if (f.HiddenFromDiagramFilter is { } hfd && edit.IsHiddenFromDiagram != hfd) return false;

        return true;
    }

    // Same idea as RefreshTaxiwayFilter, for the Taxiway Points grid —
    // matched by TaxiwayPointShape.Index/TaxiwayPointEditViewModel.Index
    // (the shared sim TAXI_POINT index both lists are keyed by), not list
    // position: unlike TaxiPathEdits/TaxiPathSegment, TaxiwayPointEdits isn't
    // a 1:1, same-order wrapper over a raw AirportDetails list that a plain
    // SourceIndex could point into.
    private void RefreshTaxiwayPointFilter()
    {
        if (Diagram is null)
        {
            VisibleTaxiwayPointEdits = TaxiwayPointEdits;
            return;
        }

        var selectedIndexes = Diagram.TaxiwayPoints.Where(p => p.IsSelected).Select(p => p.Index).ToHashSet();
        VisibleTaxiwayPointEdits = selectedIndexes.Count == 0
            ? TaxiwayPointEdits
            : TaxiwayPointEdits.Where(e => selectedIndexes.Contains(e.Index)).ToList();
    }

    // Called from MainWindow's code-behind on the Taxi Paths grid's
    // SelectionChanged (WPF's DataGrid has no bindable SelectedItems, so this
    // can't be a plain command binding like ToggleTaxiwaySelectionCommand) —
    // highlights the matching shapes for spatial context, same as a diagram
    // click. Deliberately does NOT call RefreshTaxiwayFilter: a diagram click
    // filters the grid down to just what you clicked, but the reverse
    // (selecting rows already visible in the grid re-filtering the grid down
    // to only those same rows) would make normal DataGrid row selection
    // collapse the rest of the grid out of view, which is a plain grid
    // browsing/multi-select gesture, not a request to filter anything. Also
    // doesn't open the batch-edit popover — that now only ever happens via an
    // explicit right-click on the diagram (see OpenTaxiwayBatchEditCommand).
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
    //
    // Also relies on MainWindow.xaml.cs's TaxiPathsGrid_SelectionChanged
    // filtering out SelectionChanged events that are themselves a side
    // effect of RefreshTaxiwayFilter swapping VisibleTaxiPathEdits (the
    // grid's ItemsSource) rather than a genuine row click — see that
    // handler's doc comment for the feedback-loop bug this avoids: WPF's
    // DataGrid raises its own SelectionChanged (asynchronously) as a side
    // effect of an ItemsSource change, which would otherwise reach this
    // method with an empty selection and immediately clear the diagram
    // selection that filter change was reacting to.
    public void SyncTaxiwaySelectionFromRows(IEnumerable<TaxiPathEditViewModel> selectedRows)
    {
        if (Diagram is null || ShowTaxiwayBatchEditPopover) return;

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
        RefreshTaxiwayFilter();
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        LastExportPath = null;
        LastProjectSavePath = null;
        LastXmlExportPath = null;
        LastXmlExportWarnings = [];
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
        LastXmlExportPath = null;
        LastXmlExportWarnings = [];
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
        LastXmlExportPath = null;
        LastXmlExportWarnings = [];
    }

    private void ExportXml()
    {
        if (_xmlExporter is null || _fileDialogService is null || Airport is null)
            return;

        var path = _fileDialogService.ShowSaveXmlFileDialog($"{Airport.Icao}.xml", string.Empty);
        if (path is null)
            return;

        var outcome = _xmlExporter.Export(Airport, path);
        LastXmlExportPath = outcome.FilePath;
        LastXmlExportWarnings = outcome.Warnings;
    }
}
