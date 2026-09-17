using AirportSmith.Models;

namespace AirportSmith.ViewModels;

// Per-column filter criteria for the Edit tab's Taxi Paths grid
// (MainViewModel.VisibleTaxiPathEdits) — every column has its own filter,
// entered directly in that column's header (see MainWindow.xaml). A string
// filter matches as a case-insensitive substring against the field's
// displayed text (including numeric fields, compared via their own
// ToString()); an enum/bool filter is null for "(any)" and an exact value
// otherwise — see MainViewModel.MatchesTaxiPathFilter for the actual
// matching. Combines with (AND), not replaces, the Edit tab's existing
// diagram click-to-select filter (RefreshTaxiwayFilter intersects both).
//
// A plain property bag, not itself responsible for matching a
// TaxiPathEditViewModel — NameFilter needs to resolve TaxiNameId against the
// shared TaxiNames list, which only MainViewModel has access to, so the
// actual per-row matching logic lives there instead of duplicating that
// lookup here.
public class TaxiPathFilterViewModel : ViewModelBase
{
    private string? _nameFilter;
    public string? NameFilter
    {
        get => _nameFilter;
        set => SetField(ref _nameFilter, value);
    }

    private TaxiPathType? _typeFilter;
    public TaxiPathType? TypeFilter
    {
        get => _typeFilter;
        set => SetField(ref _typeFilter, value);
    }

    private string? _startIndexFilter;
    public string? StartIndexFilter
    {
        get => _startIndexFilter;
        set => SetField(ref _startIndexFilter, value);
    }

    private string? _endIndexFilter;
    public string? EndIndexFilter
    {
        get => _endIndexFilter;
        set => SetField(ref _endIndexFilter, value);
    }

    private string? _runwayNumberFilter;
    public string? RunwayNumberFilter
    {
        get => _runwayNumberFilter;
        set => SetField(ref _runwayNumberFilter, value);
    }

    private TaxiPathRunwayDesignator? _runwayDesignatorFilter;
    public TaxiPathRunwayDesignator? RunwayDesignatorFilter
    {
        get => _runwayDesignatorFilter;
        set => SetField(ref _runwayDesignatorFilter, value);
    }

    private TaxiEdgeType? _leftEdgeFilter;
    public TaxiEdgeType? LeftEdgeFilter
    {
        get => _leftEdgeFilter;
        set => SetField(ref _leftEdgeFilter, value);
    }

    private bool? _leftEdgeLightedFilter;
    public bool? LeftEdgeLightedFilter
    {
        get => _leftEdgeLightedFilter;
        set => SetField(ref _leftEdgeLightedFilter, value);
    }

    private TaxiEdgeType? _rightEdgeFilter;
    public TaxiEdgeType? RightEdgeFilter
    {
        get => _rightEdgeFilter;
        set => SetField(ref _rightEdgeFilter, value);
    }

    private bool? _rightEdgeLightedFilter;
    public bool? RightEdgeLightedFilter
    {
        get => _rightEdgeLightedFilter;
        set => SetField(ref _rightEdgeLightedFilter, value);
    }

    private bool? _centerLineFilter;
    public bool? CenterLineFilter
    {
        get => _centerLineFilter;
        set => SetField(ref _centerLineFilter, value);
    }

    private bool? _centerLineLightedFilter;
    public bool? CenterLineLightedFilter
    {
        get => _centerLineLightedFilter;
        set => SetField(ref _centerLineLightedFilter, value);
    }

    // Filters on the workspace-only "Hide from Diagram" toggle
    // (TaxiPathEditViewModel.IsHiddenFromDiagram) alongside every real
    // TaxiPathSegment field above — it's a column in the grid like any
    // other, even though it isn't persisted.
    private bool? _hiddenFromDiagramFilter;
    public bool? HiddenFromDiagramFilter
    {
        get => _hiddenFromDiagramFilter;
        set => SetField(ref _hiddenFromDiagramFilter, value);
    }

    // Backs the "Clear Filters" button's enabled state — not itself bindable
    // (no property-changed notification of its own), since callers only ever
    // need its current value at the moment a command's CanExecute is
    // queried, not a live-updating binding.
    public bool HasAnyFilter =>
        !string.IsNullOrEmpty(NameFilter) || TypeFilter != null ||
        !string.IsNullOrEmpty(StartIndexFilter) || !string.IsNullOrEmpty(EndIndexFilter) ||
        !string.IsNullOrEmpty(RunwayNumberFilter) || RunwayDesignatorFilter != null ||
        LeftEdgeFilter != null || LeftEdgeLightedFilter != null ||
        RightEdgeFilter != null || RightEdgeLightedFilter != null ||
        CenterLineFilter != null || CenterLineLightedFilter != null ||
        HiddenFromDiagramFilter != null;

    public void Reset()
    {
        NameFilter = null;
        TypeFilter = null;
        StartIndexFilter = null;
        EndIndexFilter = null;
        RunwayNumberFilter = null;
        RunwayDesignatorFilter = null;
        LeftEdgeFilter = null;
        LeftEdgeLightedFilter = null;
        RightEdgeFilter = null;
        RightEdgeLightedFilter = null;
        CenterLineFilter = null;
        CenterLineLightedFilter = null;
        HiddenFromDiagramFilter = null;
    }
}
