using System.ComponentModel;
using AirportSmith.Helpers;
using AirportSmith.Models;
using AirportSmith.Models.Diagram;
using AirportSmith.Services;

namespace AirportSmith.ViewModels;

// The Edit tab's Taxiway Signs feature (see requirements.md, "author taxiway
// signs"): the grid, the diagram's sign plates, click-to-place, and
// auto-placement via TaxiwaySignPlanner. Kept in its own file only because
// MainViewModel.cs is already very long; it follows the same patterns as the
// parking spot code there (edit wrappers writing straight into Airport,
// shapes updated in place rather than re-projecting the diagram).
public partial class MainViewModel
{
    // One wrapper per Airport.TaxiwaySigns entry, same order. Rebuilt (with
    // the diagram's sign shapes) whenever a sign is added or removed — see
    // RebuildTaxiwaySigns.
    private IReadOnlyList<TaxiwaySignEditViewModel> _taxiwaySignEdits = [];
    public IReadOnlyList<TaxiwaySignEditViewModel> TaxiwaySignEdits
    {
        get => _taxiwaySignEdits;
        private set => SetField(ref _taxiwaySignEdits, value);
    }

    // Every sign, or just the ones selected on the diagram — same pattern as
    // VisibleParkingSpotEdits.
    private IReadOnlyList<TaxiwaySignEditViewModel> _visibleTaxiwaySignEdits = [];
    public IReadOnlyList<TaxiwaySignEditViewModel> VisibleTaxiwaySignEdits
    {
        get => _visibleTaxiwaySignEdits;
        private set => SetField(ref _visibleTaxiwaySignEdits, value);
    }

    // Display-only, never saved — same as HideAllParkingSpots.
    private bool _hideAllTaxiwaySigns;
    public bool HideAllTaxiwaySigns
    {
        get => _hideAllTaxiwaySigns;
        set
        {
            if (!SetField(ref _hideAllTaxiwaySigns, value)) return;
            if (Diagram is null) return;

            foreach (var shape in Diagram.TaxiwaySigns)
            {
                shape.IsVisible = !value;
                if (value) shape.IsSelected = false;
            }
            RefreshTaxiwaySignFilter();
        }
    }

    // Click-to-place for signs: either a new sign (Add Sign) or an existing
    // one being moved (a row's Place button). Like the VASI/parking
    // placements, at most one placement of any kind is armed at a time.
    private bool _isAddingTaxiwaySign;
    private TaxiwaySignEditViewModel? _armedTaxiwaySignMove;
    public bool IsTaxiwaySignPlacementArmed => _isAddingTaxiwaySign || _armedTaxiwaySignMove != null;
    public string? TaxiwaySignPlacementStatusText =>
        _isAddingTaxiwaySign ? "Click the diagram to place a new taxiway sign"
        : _armedTaxiwaySignMove is { } armed ? $"Click the diagram to move sign {armed.Label}"
        : null;

    // Result of the last auto-placement run, shown under the Taxiway Signs
    // buttons; cleared on airport load.
    private string? _taxiwaySignStatus;
    public string? TaxiwaySignStatus
    {
        get => _taxiwaySignStatus;
        private set => SetField(ref _taxiwaySignStatus, value);
    }

    private IReadOnlyList<string> _taxiwaySignWarnings = [];
    public IReadOnlyList<string> TaxiwaySignWarnings
    {
        get => _taxiwaySignWarnings;
        private set
        {
            if (SetField(ref _taxiwaySignWarnings, value)) OnPropertyChanged(nameof(HasTaxiwaySignWarnings));
        }
    }
    public bool HasTaxiwaySignWarnings => TaxiwaySignWarnings.Count > 0;

    public RelayCommand AddTaxiwaySignCommand { get; }
    public RelayCommand<TaxiwaySignEditViewModel> ArmTaxiwaySignPlacementCommand { get; }
    public RelayCommand<Point2D> PlaceTaxiwaySignCommand { get; }
    public RelayCommand<TaxiwaySignSelectionRequest> ToggleTaxiwaySignSelectionCommand { get; }
    public RelayCommand AutoPlaceTaxiwaySignsCommand { get; }
    public RelayCommand GenerateTaxiwaySignsForSelectedPointsCommand { get; }
    public RelayCommand DeleteSelectedTaxiwaySignsCommand { get; }

    // Called from SetAirport: a fresh wrapper/shape set for the new airport,
    // with every sign-related session state reset.
    private void ResetTaxiwaySignsForNewAirport()
    {
        if (_hideAllTaxiwaySigns)
        {
            _hideAllTaxiwaySigns = false;
            OnPropertyChanged(nameof(HideAllTaxiwaySigns));
        }
        DisarmTaxiwaySignPlacement();
        TaxiwaySignStatus = null;
        TaxiwaySignWarnings = [];
        RebuildTaxiwaySigns(refreshDataTree: false);
    }

    // Brings TaxiwaySignEdits and Diagram.TaxiwaySigns back in line with
    // Airport.TaxiwaySigns after signs were added/removed, without
    // re-projecting the diagram. Surviving signs keep their shape (and so
    // their selection/visibility). The Airport Data tree is rebuilt too so a
    // new sign shows there right away (SetAirport builds it itself, hence
    // refreshDataTree: false from there).
    private void RebuildTaxiwaySigns(bool refreshDataTree = true)
    {
        foreach (var edit in TaxiwaySignEdits)
            edit.PropertyChanged -= OnTaxiwaySignEditChanged;

        var signs = Airport?.TaxiwaySigns ?? [];
        TaxiwaySignEdits = signs.Select(s => new TaxiwaySignEditViewModel(s)).ToList();
        foreach (var edit in TaxiwaySignEdits)
            edit.PropertyChanged += OnTaxiwaySignEditChanged;

        if (Diagram != null)
        {
            var shapesById = Diagram.TaxiwaySigns.ToDictionary(s => s.SignId);
            var ids = signs.Select(s => s.Id).ToHashSet();
            foreach (var stale in Diagram.TaxiwaySigns.Where(s => !ids.Contains(s.SignId)).ToList())
                Diagram.TaxiwaySigns.Remove(stale);
            foreach (var sign in signs)
            {
                if (shapesById.ContainsKey(sign.Id)) continue;
                var shape = AirportDiagramProjector.BuildTaxiwaySignShape(Diagram, sign);
                shape.IsVisible = !_hideAllTaxiwaySigns;
                Diagram.TaxiwaySigns.Add(shape);
            }
        }

        if (refreshDataTree && Airport != null) AirportDataTree = AirportDataTreeBuilder.Build(Airport);
        RefreshTaxiwaySignFilter();
    }

    private void OnTaxiwaySignEditChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TaxiwaySignEditViewModel edit || Diagram is null) return;
        if (e.PropertyName is nameof(TaxiwaySignEditViewModel.Size) or nameof(TaxiwaySignEditViewModel.Justification)
            or nameof(TaxiwaySignEditViewModel.IsAutoPlaced)) return;

        var shape = Diagram.TaxiwaySigns.FirstOrDefault(s => s.SignId == edit.Sign.Id);
        if (shape != null) AirportDiagramProjector.RefreshTaxiwaySignShape(Diagram, shape, edit.Sign);
    }

    private void RefreshTaxiwaySignFilter()
    {
        var selected = Diagram?.TaxiwaySigns.Where(s => s.IsSelected).Select(s => s.SignId).ToHashSet() ?? [];
        VisibleTaxiwaySignEdits = selected.Count == 0
            ? TaxiwaySignEdits
            : TaxiwaySignEdits.Where(e => selected.Contains(e.Sign.Id)).ToList();
    }

    private void ToggleTaxiwaySignSelection(TaxiwaySignSelectionRequest? request)
    {
        if (request is null || Diagram is null) return;

        if (!request.ExtendSelection)
        {
            foreach (var shape in Diagram.TaxiwaySigns)
                if (!ReferenceEquals(shape, request.Shape))
                    shape.IsSelected = false;
            request.Shape.IsSelected = true;
        }
        else
        {
            request.Shape.IsSelected = !request.Shape.IsSelected;
        }

        RefreshTaxiwaySignFilter();
    }

    private void ClearTaxiwaySignSelection()
    {
        if (Diagram is null) return;
        foreach (var shape in Diagram.TaxiwaySigns)
            shape.IsSelected = false;
        RefreshTaxiwaySignFilter();
    }

    // ---- Click-to-place ------------------------------------------------

    private void ArmAddTaxiwaySign()
    {
        DisarmOtherPlacements();
        _armedTaxiwaySignMove = null;
        _isAddingTaxiwaySign = true;
        RaiseTaxiwaySignPlacementChanged();
    }

    private void ArmTaxiwaySignPlacement(TaxiwaySignEditViewModel? edit)
    {
        if (edit is null || !TaxiwaySignEdits.Contains(edit)) return;
        DisarmOtherPlacements();
        _isAddingTaxiwaySign = false;
        _armedTaxiwaySignMove = edit;
        RaiseTaxiwaySignPlacementChanged();
    }

    private void PlaceTaxiwaySign(Point2D point)
    {
        if (Airport is null || Diagram is null || !IsTaxiwaySignPlacementArmed) return;
        var (x, z) = AirportDiagramProjector.ComputeTaxiwaySignPosition(Diagram, point);
        x = Math.Round(x, 2);
        z = Math.Round(z, 2);

        if (_armedTaxiwaySignMove is { } move)
        {
            move.MoveTo(x, z);
        }
        else
        {
            var suggestion = TaxiwaySignPlanner.SuggestLocationSign(Airport, x, z);
            Airport.TaxiwaySigns.Add(new TaxiwaySign
            {
                XMeters = x,
                ZMeters = z,
                Label = suggestion?.Label ?? string.Empty,
                HeadingDeg = suggestion?.HeadingDeg ?? 0,
            });
            RebuildTaxiwaySigns();
        }

        DisarmTaxiwaySignPlacement();
    }

    private void DisarmTaxiwaySignPlacement()
    {
        if (!IsTaxiwaySignPlacementArmed) return;
        _isAddingTaxiwaySign = false;
        _armedTaxiwaySignMove = null;
        RaiseTaxiwaySignPlacementChanged();
    }

    // Arming a sign placement cancels a pending VASI/parking placement — one
    // background click can only place one thing. The reverse direction is in
    // ArmVasiPlacement/ArmParkingPlacement.
    private void DisarmOtherPlacements()
    {
        if (_armedVasiPlacement != null)
        {
            _armedVasiPlacement = null;
            RaiseVasiPlacementChanged();
            PlaceVasiCommand.RaiseCanExecuteChanged();
        }
        if (_armedParkingPlacement != null)
        {
            _armedParkingPlacement = null;
            RaiseParkingPlacementChanged();
            PlaceParkingCommand.RaiseCanExecuteChanged();
        }
    }

    private void RaiseTaxiwaySignPlacementChanged()
    {
        OnPropertyChanged(nameof(IsTaxiwaySignPlacementArmed));
        OnPropertyChanged(nameof(TaxiwaySignPlacementStatusText));
        PlaceTaxiwaySignCommand.RaiseCanExecuteChanged();
    }

    // ---- Auto-placement -------------------------------------------------

    private IReadOnlySet<int> SelectedTaxiwayPointIndices() =>
        Diagram?.TaxiwayPoints.Where(p => p.IsSelected).Select(p => p.Index).ToHashSet() ?? [];

    private void AutoPlaceTaxiwaySigns() => RunTaxiwaySignPlanner(null);

    private void GenerateTaxiwaySignsForSelectedPoints()
    {
        var points = SelectedTaxiwayPointIndices();
        if (points.Count > 0) RunTaxiwaySignPlanner(points);
    }

    // Replaces the Auto signs the run covers and keeps every manual or edited
    // sign (TaxiwaySignPlanner.Merge), asking first whenever an existing Auto
    // sign would be replaced.
    private void RunTaxiwaySignPlanner(IReadOnlySet<int>? onlyPoints)
    {
        if (Airport is null) return;

        var replaced = TaxiwaySignPlanner.CountReplaced(Airport.TaxiwaySigns, onlyPoints);
        if (replaced > 0 && _confirmationService != null)
        {
            var scope = onlyPoints is null ? "" : " at the selected points";
            var message = replaced == 1
                ? $"Replace 1 auto-placed taxiway sign{scope}? Signs you placed or edited yourself are kept."
                : $"Replace {replaced} auto-placed taxiway signs{scope}? Signs you placed or edited yourself are kept.";
            if (!_confirmationService.Confirm(message)) return;
        }

        var plan = TaxiwaySignPlanner.Plan(Airport, onlyPoints);
        var merged = TaxiwaySignPlanner.Merge(Airport.TaxiwaySigns, plan.Signs, onlyPoints);
        Airport.TaxiwaySigns.Clear();
        Airport.TaxiwaySigns.AddRange(merged);
        RebuildTaxiwaySigns();

        TaxiwaySignStatus = plan.Signs.Count == 0
            ? "No signs to place — signs need named taxiways, hold-short points or runway exits."
            : $"Placed {plan.Signs.Count} sign(s)" + (replaced > 0 ? $", replacing {replaced} earlier auto-placed sign(s)." : ".");
        TaxiwaySignWarnings = plan.Warnings;
    }

    // ---- Delete ---------------------------------------------------------

    private bool HasTaxiwaySignSelection() => Diagram != null && Diagram.TaxiwaySigns.Any(s => s.IsSelected);

    private void DeleteSelectedTaxiwaySigns()
    {
        if (Airport is null || Diagram is null) return;
        var selected = Diagram.TaxiwaySigns.Where(s => s.IsSelected).Select(s => s.SignId).ToHashSet();
        if (selected.Count == 0) return;

        var message = selected.Count == 1 ? "Delete 1 taxiway sign?" : $"Delete {selected.Count} taxiway signs?";
        if (_confirmationService != null && !_confirmationService.Confirm(message)) return;

        if (_armedTaxiwaySignMove is { } armed && selected.Contains(armed.Sign.Id))
            DisarmTaxiwaySignPlacement();
        Airport.TaxiwaySigns.RemoveAll(s => selected.Contains(s.Id));
        RebuildTaxiwaySigns();
    }
}
