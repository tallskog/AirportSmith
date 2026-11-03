using System.Collections.Specialized;
using AirportSmith.Models;
using AirportSmith.Models.Diagram;
using AirportSmith.Tests.Fakes;
using AirportSmith.ViewModels;

namespace AirportSmith.Tests.ViewModels;

// The Edit tab's Taxi Names panel: alphabetical order and hiding taxi paths
// from the diagram by name (TaxiNameEditViewModel.IsHiddenFromDiagram).
public class MainViewModelTaxiNameTests
{
    // Names deliberately stored out of order; paths 0 and 1 are both "B",
    // path 2 is "A2", path 3 has no TaxiNameId at all.
    private static AirportDetails Airport(out TaxiName b, out TaxiName a10, out TaxiName a2, out TaxiName blank)
    {
        b = new TaxiName { Value = "B" };
        a10 = new TaxiName { Value = "A10" };
        a2 = new TaxiName { Value = "A2" };
        blank = new TaxiName { Value = "" };
        var airport = new AirportDetails { Icao = "EFHK", TaxiNames = [b, a10, a2, blank] };
        airport.TaxiPaths.Add(Path(b, 0));
        airport.TaxiPaths.Add(Path(b, 20));
        airport.TaxiPaths.Add(Path(a2, 40));
        airport.TaxiPaths.Add(Path(null, 60));
        return airport;
    }

    private static TaxiPathSegment Path(TaxiName? name, double x) => new()
    {
        Type = TaxiPathType.Taxi, TaxiNameId = name?.Id, WidthMeters = 10,
        StartXMeters = x, StartZMeters = 0, EndXMeters = x + 10, EndZMeters = 0,
    };

    private static MainViewModel Load(AirportDetails airport)
    {
        var store = new FakeAirportProjectStore();
        store.Save(airport);
        var vm = new MainViewModel(new FakeSimConnectService(), projectStore: store, confirmationService: new FakeConfirmationService()) { IcaoInput = airport.Icao };
        vm.LoadProjectCommand.Execute(null);
        return vm;
    }

    private static bool IsShapeVisible(MainViewModel vm, int pathIndex) =>
        vm.Diagram!.TaxiwaySegments.Single(s => s.SourceIndex == pathIndex).IsVisible;

    [Fact]
    public void Load_ListsNamesInNaturalAlphabeticalOrder_WithoutReorderingTheAirportsOwnList()
    {
        var vm = Load(Airport(out _, out _, out _, out _));

        Assert.Equal(["", "A2", "A10", "B"], vm.TaxiNames.Select(n => n.Value));
        Assert.Equal(["", "A2", "A10", "B"], vm.TaxiNamesPicker.Select(n => n.Value));
        Assert.Equal(["B", "A10", "A2", ""], vm.Airport!.TaxiNames.Select(n => n.Value));
    }

    [Fact]
    public void SortTaxiNames_AfterARename_MovesRowsInPlace_WithoutRemovingOrResetting()
    {
        var vm = Load(Airport(out var b, out _, out _, out _));
        var actions = new List<NotifyCollectionChangedAction>();
        vm.TaxiNames.CollectionChanged += (_, e) => actions.Add(e.Action);
        var bEdit = vm.TaxiNames.Single(n => n.Id == b.Id);

        bEdit.Value = "A1";
        Assert.Equal("A1", vm.TaxiNames[^1].Value); // no jump while typing
        vm.SortTaxiNames();

        Assert.Equal(["", "A1", "A2", "A10"], vm.TaxiNames.Select(n => n.Value));
        Assert.Same(bEdit, vm.TaxiNames[1]);
        // A Remove/Reset would make the Taxi Paths grid's Name pickers clear
        // their selection — see MainViewModel.SyncTaxiNames.
        Assert.NotEmpty(actions);
        Assert.All(actions, a => Assert.Equal(NotifyCollectionChangedAction.Move, a));
    }

    [Fact]
    public void AddTaxiName_SortsPendingRenames_AndAppendsTheNewRowAtTheBottom()
    {
        var vm = Load(Airport(out var b, out _, out _, out _));
        vm.TaxiNames.Single(n => n.Id == b.Id).Value = "A1";

        vm.AddTaxiNameCommand.Execute(null);

        Assert.Equal(["", "A1", "A2", "A10", ""], vm.TaxiNames.Select(n => n.Value));
    }

    // Reloading the same project gives new TaxiName objects with the same Ids
    // (a real load deserializes from disk; here: a fresh copy put back into
    // the fake store). The rows must wrap the NEW objects, or a rename after
    // the reload never reaches Airport and Save/Export write the old name.
    [Fact]
    public void ReloadingTheSameProject_RenameStillReachesTheAirport()
    {
        var airport = Airport(out var b, out _, out _, out _);
        var store = new FakeAirportProjectStore();
        store.Save(airport);
        var vm = new MainViewModel(new FakeSimConnectService(), projectStore: store) { IcaoInput = airport.Icao };
        vm.LoadProjectCommand.Execute(null);
        vm.TaxiNames.Single(n => n.Id == b.Id).IsHiddenFromDiagram = true;
        store.Save(new AirportDetails
        {
            Icao = airport.Icao,
            TaxiNames = airport.TaxiNames.Select(n => new TaxiName { Id = n.Id, Value = n.Value }).ToList(),
            TaxiPaths = airport.TaxiPaths,
        });

        vm.LoadProjectCommand.Execute(null);
        vm.TaxiNames.Single(n => n.Id == b.Id).Value = "RENAMED";

        Assert.Equal("RENAMED", vm.Airport!.TaxiNames.Single(n => n.Id == b.Id).Value);
        Assert.Equal(["", "A2", "A10", "RENAMED"], vm.TaxiNames.Select(n => n.Value)); // still sorted after the rebuild
        Assert.All(vm.TaxiNames, n => Assert.False(n.IsHiddenFromDiagram)); // a load resets Hide
        Assert.True(IsShapeVisible(vm, 0));
    }

    [Fact]
    public void HidingAName_HidesEveryPathUsingIt_AndShowingItRestoresThem()
    {
        var vm = Load(Airport(out var b, out _, out _, out _));
        var bEdit = vm.TaxiNames.Single(n => n.Id == b.Id);

        bEdit.IsHiddenFromDiagram = true;

        Assert.False(IsShapeVisible(vm, 0));
        Assert.False(IsShapeVisible(vm, 1));
        Assert.True(IsShapeVisible(vm, 2));
        Assert.True(IsShapeVisible(vm, 3));
        // Workspace-only: the per-path toggle and the airport data are untouched.
        Assert.False(vm.TaxiPathEdits[0].IsHiddenFromDiagram);

        bEdit.IsHiddenFromDiagram = false;

        Assert.True(IsShapeVisible(vm, 0));
        Assert.True(IsShapeVisible(vm, 1));
    }

    [Fact]
    public void ShowingAName_KeepsPathsHiddenByTheirOwnToggleHidden()
    {
        var vm = Load(Airport(out var b, out _, out _, out _));
        var bEdit = vm.TaxiNames.Single(n => n.Id == b.Id);
        vm.TaxiPathEdits[0].IsHiddenFromDiagram = true;
        bEdit.IsHiddenFromDiagram = true;

        bEdit.IsHiddenFromDiagram = false;

        Assert.False(IsShapeVisible(vm, 0));
        Assert.True(IsShapeVisible(vm, 1));
    }

    [Fact]
    public void HidingAName_ClearsTheSelectionOfItsPaths()
    {
        var vm = Load(Airport(out var b, out _, out _, out _));
        var shape0 = vm.Diagram!.TaxiwaySegments.Single(s => s.SourceIndex == 0);
        vm.ToggleTaxiwaySelectionCommand.Execute(new TaxiwaySelectionRequest(shape0, ExtendSelection: false));

        vm.TaxiNames.Single(n => n.Id == b.Id).IsHiddenFromDiagram = true;

        Assert.False(shape0.IsSelected);
        Assert.False(vm.HasTaxiwaySelection);
    }

    [Fact]
    public void RenamingAPathToAHiddenName_HidesIt_AndBackToAVisibleNameShowsIt()
    {
        var vm = Load(Airport(out var b, out _, out var a2, out _));
        vm.TaxiNames.Single(n => n.Id == b.Id).IsHiddenFromDiagram = true;

        vm.TaxiPathEdits[2].TaxiNameId = b.Id;
        Assert.False(IsShapeVisible(vm, 2));

        vm.TaxiPathEdits[2].TaxiNameId = a2.Id;
        Assert.True(IsShapeVisible(vm, 2));
    }

    [Fact]
    public void HideAllAndShowAll_ToggleEveryName_AndAreOnlyEnabledWhenTheyWouldChangeSomething()
    {
        var vm = Load(Airport(out _, out _, out _, out _));
        Assert.True(vm.HideAllTaxiNamesCommand.CanExecute(null));
        Assert.False(vm.ShowAllTaxiNamesCommand.CanExecute(null));

        vm.HideAllTaxiNamesCommand.Execute(null);

        Assert.All(vm.TaxiNames, n => Assert.True(n.IsHiddenFromDiagram));
        Assert.False(IsShapeVisible(vm, 0));
        Assert.False(IsShapeVisible(vm, 2));
        Assert.True(IsShapeVisible(vm, 3)); // no name to hide it by
        Assert.False(vm.HideAllTaxiNamesCommand.CanExecute(null));
        Assert.True(vm.ShowAllTaxiNamesCommand.CanExecute(null));

        vm.ShowAllTaxiNamesCommand.Execute(null);

        Assert.All(vm.TaxiNames, n => Assert.False(n.IsHiddenFromDiagram));
        Assert.True(IsShapeVisible(vm, 0));
        Assert.True(IsShapeVisible(vm, 2));
    }

    // Deleting a parking spot re-runs SetAirport on the same airport, which
    // re-projects the diagram (every shape visible again) but keeps the same
    // TaxiNames rows — so a hidden name must still hide its paths afterwards,
    // and the sorted list must not count as "changed" and get rebuilt.
    [Fact]
    public void InPlaceStructuralEdit_KeepsNamesSortedAndHidden_WithoutRebuildingThem()
    {
        var airport = Airport(out var b, out _, out _, out _);
        airport.ParkingSpots.Add(new TaxiParkingSpot { ItemIndex = 0, Number = 1, BiasXMeters = 0, BiasZMeters = 100, RadiusMeters = 10 });
        var vm = Load(airport);
        var rowsBefore = vm.TaxiNames.ToList();
        vm.TaxiNames.Single(n => n.Id == b.Id).IsHiddenFromDiagram = true;
        vm.ToggleParkingSpotSelectionCommand.Execute(new ParkingSpotSelectionRequest(vm.Diagram!.ParkingSpots[0], false));

        vm.DeleteSelectedParkingSpotsCommand.Execute(null);

        Assert.Empty(vm.Airport!.ParkingSpots);
        Assert.Equal(rowsBefore, vm.TaxiNames);
        Assert.False(IsShapeVisible(vm, 0));
        Assert.True(IsShapeVisible(vm, 2));
    }
}
