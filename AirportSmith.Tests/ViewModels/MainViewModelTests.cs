using AirportSmith.Models;
using AirportSmith.Models.Diagram;
using AirportSmith.Services;
using AirportSmith.Tests.Fakes;
using AirportSmith.ViewModels;

namespace AirportSmith.Tests.ViewModels;

public class MainViewModelTests
{
    private static MainViewModel CreateViewModel(FakeSimConnectService fake, string icao = "EFHK")
    {
        var vm = new MainViewModel(fake) { IcaoInput = icao };
        return vm;
    }

    // Two resolvable Taxi-typed segments (AirportDiagramProjector only
    // includes Taxi/Path-typed segments with resolved coordinates), each
    // named, so tests below get real TaxiwaySegmentShapes to select/batch-edit.
    private static AirportDetails BuildAirportWithTwoTaxiways(out TaxiName name1, out TaxiName name2)
    {
        name1 = new TaxiName { Value = "A" };
        name2 = new TaxiName { Value = "B" };
        var airport = new AirportDetails { Icao = "EFHK" };
        airport.TaxiNames.Add(name1);
        airport.TaxiNames.Add(name2);
        airport.TaxiPaths.Add(new TaxiPathSegment
        {
            Type = TaxiPathType.Taxi, TaxiNameId = name1.Id, WidthMeters = 10,
            StartXMeters = 0, StartZMeters = 0, EndXMeters = 10, EndZMeters = 0,
        });
        airport.TaxiPaths.Add(new TaxiPathSegment
        {
            Type = TaxiPathType.Taxi, TaxiNameId = name2.Id, WidthMeters = 10,
            StartXMeters = 20, StartZMeters = 0, EndXMeters = 30, EndZMeters = 0,
        });
        return airport;
    }

    // Synchronous alternative to LoadCommand for tests that just need an
    // airport loaded (project load doesn't await a SimConnect round trip).
    private static MainViewModel CreateViewModelWithAirport(AirportDetails airport)
    {
        var store = new FakeAirportProjectStore();
        store.Save(airport);
        var vm = new MainViewModel(new FakeSimConnectService(), projectStore: store) { IcaoInput = airport.Icao };
        vm.LoadProjectCommand.Execute(null);
        return vm;
    }

    [Fact]
    public async Task LoadCommand_Success_PopulatesAirportAndClearsError()
    {
        var airport = new AirportDetails { Icao = "EFHK", Name = "Helsinki-Vantaa" };
        var fake = new FakeSimConnectService
        {
            OnGetAirportDetails = _ => Task.FromResult(new AirportLookupResult(AirportLookupStatus.Success, airport, null))
        };
        var vm = CreateViewModel(fake);

        vm.LoadCommand.Execute(null);
        await Task.Delay(50);

        Assert.Same(airport, vm.Airport);
        Assert.NotNull(vm.Diagram);
        Assert.NotEmpty(vm.AirportDataTree);
        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task LoadCommand_Success_PopulatesTaxiPathAndRunwayEdits()
    {
        var taxiName = new TaxiName { Value = "A" };
        var airport = new AirportDetails { Icao = "EFHK" };
        airport.TaxiNames.Add(taxiName);
        airport.TaxiPaths.Add(new TaxiPathSegment { TaxiNameId = taxiName.Id });
        airport.Runways.Add(new Runway { PrimaryDesignation = "04L" });
        var fake = new FakeSimConnectService
        {
            OnGetAirportDetails = _ => Task.FromResult(new AirportLookupResult(AirportLookupStatus.Success, airport, null))
        };
        var vm = CreateViewModel(fake);

        vm.LoadCommand.Execute(null);
        await Task.Delay(50);

        Assert.Single(vm.TaxiPathEdits);
        Assert.Equal(taxiName.Id, vm.TaxiPathEdits[0].TaxiNameId);
        Assert.Single(vm.TaxiNames);
        Assert.Equal("A", vm.TaxiNames[0].Value);
        Assert.Single(vm.RunwayEdits);
        Assert.Equal("04L", vm.RunwayEdits[0].PrimaryDesignation);
    }

    [Fact]
    public async Task LoadCommand_NotConnected_LeavesTaxiPathAndRunwayEditsEmpty()
    {
        var fake = new FakeSimConnectService
        {
            OnGetAirportDetails = _ => Task.FromResult(new AirportLookupResult(AirportLookupStatus.NotConnected, null, null))
        };
        var vm = CreateViewModel(fake);

        vm.LoadCommand.Execute(null);
        await Task.Delay(50);

        Assert.Empty(vm.TaxiPathEdits);
        Assert.Empty(vm.RunwayEdits);
    }

    [Fact]
    public async Task LoadCommand_NotConnected_SetsErrorMessage_AndLeavesAirportNull()
    {
        var fake = new FakeSimConnectService
        {
            OnGetAirportDetails = _ => Task.FromResult(new AirportLookupResult(AirportLookupStatus.NotConnected, null, null))
        };
        var vm = CreateViewModel(fake);

        vm.LoadCommand.Execute(null);
        await Task.Delay(50);

        Assert.Null(vm.Airport);
        Assert.Null(vm.Diagram);
        Assert.Empty(vm.AirportDataTree);
        Assert.Contains("not running", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadCommand_NotFound_SetsErrorMessage_WithIcaoInMessage()
    {
        var fake = new FakeSimConnectService
        {
            OnGetAirportDetails = _ => Task.FromResult(new AirportLookupResult(AirportLookupStatus.NotFound, null, null))
        };
        var vm = CreateViewModel(fake, "ZZZZ");

        vm.LoadCommand.Execute(null);
        await Task.Delay(50);

        Assert.Null(vm.Airport);
        Assert.Contains("ZZZZ", vm.ErrorMessage);
    }

    [Fact]
    public async Task LoadCommand_Timeout_SetsGenericTimeoutMessage()
    {
        var fake = new FakeSimConnectService
        {
            OnGetAirportDetails = _ => Task.FromResult(new AirportLookupResult(AirportLookupStatus.Timeout, null, null))
        };
        var vm = CreateViewModel(fake);

        vm.LoadCommand.Execute(null);
        await Task.Delay(50);

        Assert.Null(vm.Airport);
        Assert.Contains("Timed out", vm.ErrorMessage);
    }

    [Fact]
    public async Task LoadCommand_CanExecute_FalseWhileLoading()
    {
        var tcs = new TaskCompletionSource<AirportLookupResult>();
        var fake = new FakeSimConnectService
        {
            OnGetAirportDetails = _ => tcs.Task
        };
        var vm = CreateViewModel(fake);

        Assert.True(vm.LoadCommand.CanExecute(null));
        vm.LoadCommand.Execute(null);

        Assert.False(vm.LoadCommand.CanExecute(null));

        tcs.SetResult(new AirportLookupResult(AirportLookupStatus.Success, new AirportDetails(), null));
        await Task.Delay(50);

        Assert.True(vm.LoadCommand.CanExecute(null));
    }

    [Fact]
    public void IcaoInput_Setter_UppercasesInput()
    {
        var vm = CreateViewModel(new FakeSimConnectService(), icao: "");
        vm.IcaoInput = "efhk";

        Assert.Equal("EFHK", vm.IcaoInput);
    }

    [Theory]
    [InlineData("")]
    [InlineData("E")]
    public void LoadCommand_CanExecute_FalseForInvalidIcaoLength(string icao)
    {
        var vm = CreateViewModel(new FakeSimConnectService(), icao);

        Assert.False(vm.LoadCommand.CanExecute(null));
    }

    [Fact]
    public void IsConnected_ReflectsFakeService_AndUpdatesOnConnectionChanged()
    {
        var fake = new FakeSimConnectService { IsConnected = false };
        var vm = CreateViewModel(fake);
        Assert.False(vm.IsConnected);

        fake.IsConnected = true;
        var raised = false;
        vm.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(MainViewModel.IsConnected);
        fake.RaiseConnectionChanged();

        Assert.True(raised);
        Assert.True(vm.IsConnected);
    }

    [Fact]
    public void IsDevModeExportAvailable_FalseWhenNoStoreInjected()
    {
        var vm = new MainViewModel(new FakeSimConnectService());

        Assert.False(vm.IsDevModeExportAvailable);
        Assert.False(vm.ExportDebugDataCommand.CanExecute(null));
    }

    [Fact]
    public void IsDevModeExportAvailable_TrueWhenStoreInjected()
    {
        var vm = new MainViewModel(new FakeSimConnectService(), new FakeDebugDataStore());

        Assert.True(vm.IsDevModeExportAvailable);
    }

    [Fact]
    public async Task ExportDebugDataCommand_CanExecute_FalseUntilAirportLoaded()
    {
        var airport = new AirportDetails { Icao = "EFHK" };
        var fake = new FakeSimConnectService
        {
            OnGetAirportDetails = _ => Task.FromResult(new AirportLookupResult(AirportLookupStatus.Success, airport, null))
        };
        var store = new FakeDebugDataStore();
        var vm = new MainViewModel(fake, store) { IcaoInput = "EFHK" };

        Assert.False(vm.ExportDebugDataCommand.CanExecute(null));

        vm.LoadCommand.Execute(null);
        await Task.Delay(50);

        Assert.True(vm.ExportDebugDataCommand.CanExecute(null));
    }

    [Fact]
    public async Task ExportDebugDataCommand_Execute_WritesAirportAndSetsLastExportPath()
    {
        var airport = new AirportDetails { Icao = "EFHK" };
        var fake = new FakeSimConnectService
        {
            OnGetAirportDetails = _ => Task.FromResult(new AirportLookupResult(AirportLookupStatus.Success, airport, null))
        };
        var store = new FakeDebugDataStore { PathToReturn = "temp/EFHK_20260910.json" };
        var vm = new MainViewModel(fake, store) { IcaoInput = "EFHK" };
        vm.LoadCommand.Execute(null);
        await Task.Delay(50);

        vm.ExportDebugDataCommand.Execute(null);

        Assert.Same(airport, store.LastExported);
        Assert.Equal("temp/EFHK_20260910.json", vm.LastExportPath);
    }

    [Fact]
    public async Task LoadCommand_NewLoad_ClearsPreviousLastExportPath()
    {
        var airport = new AirportDetails { Icao = "EFHK" };
        var fake = new FakeSimConnectService
        {
            OnGetAirportDetails = _ => Task.FromResult(new AirportLookupResult(AirportLookupStatus.Success, airport, null))
        };
        var store = new FakeDebugDataStore();
        var vm = new MainViewModel(fake, store) { IcaoInput = "EFHK" };
        vm.LoadCommand.Execute(null);
        await Task.Delay(50);
        vm.ExportDebugDataCommand.Execute(null);
        Assert.NotNull(vm.LastExportPath);

        vm.LoadCommand.Execute(null);
        await Task.Delay(50);

        Assert.Null(vm.LastExportPath);
    }

    [Fact]
    public void IsDevModeImportAvailable_RequiresBothStoreAndDialogService()
    {
        Assert.False(new MainViewModel(new FakeSimConnectService()).IsDevModeImportAvailable);
        Assert.False(new MainViewModel(new FakeSimConnectService(), new FakeDebugDataStore()).IsDevModeImportAvailable);
        Assert.False(new MainViewModel(new FakeSimConnectService(), fileDialogService: new FakeFileDialogService()).IsDevModeImportAvailable);
        Assert.True(new MainViewModel(new FakeSimConnectService(), new FakeDebugDataStore(), new FakeFileDialogService()).IsDevModeImportAvailable);
    }

    [Fact]
    public void LoadFromFileCommand_UserCancelsDialog_LeavesAirportAndErrorUnchanged()
    {
        var store = new FakeDebugDataStore { AirportToImport = new AirportDetails { Icao = "EFHK" } };
        var dialog = new FakeFileDialogService { PathToReturn = null };
        var vm = new MainViewModel(new FakeSimConnectService(), store, dialog);

        vm.LoadFromFileCommand.Execute(null);

        Assert.Null(vm.Airport);
        Assert.Null(store.LastImportedPath);
    }

    [Fact]
    public void LoadFromFileCommand_InvalidFile_SetsErrorMessage_AndLeavesAirportNull()
    {
        var store = new FakeDebugDataStore { AirportToImport = null };
        var dialog = new FakeFileDialogService { PathToReturn = "bad.json" };
        var vm = new MainViewModel(new FakeSimConnectService(), store, dialog);

        vm.LoadFromFileCommand.Execute(null);

        Assert.Null(vm.Airport);
        Assert.Null(vm.Diagram);
        Assert.Empty(vm.AirportDataTree);
        Assert.Contains("bad.json", vm.ErrorMessage);
    }

    [Fact]
    public void LoadFromFileCommand_ValidFile_PopulatesAirport_AndClearsErrorAndExportPath()
    {
        var airport = new AirportDetails { Icao = "OIBK" };
        var store = new FakeDebugDataStore { AirportToImport = airport, PathToReturn = "OIBK_x.json" };
        var dialog = new FakeFileDialogService { PathToReturn = "OIBK_x.json" };
        var vm = new MainViewModel(new FakeSimConnectService(), store, dialog);

        vm.LoadFromFileCommand.Execute(null);

        Assert.Same(airport, vm.Airport);
        Assert.NotNull(vm.Diagram);
        Assert.NotEmpty(vm.AirportDataTree);
        Assert.Null(vm.ErrorMessage);
        Assert.Null(vm.LastExportPath);
        Assert.Equal("OIBK_x.json", store.LastImportedPath);
    }

    [Fact]
    public void IsProjectStoreAvailable_FalseWhenNoStoreInjected()
    {
        var vm = new MainViewModel(new FakeSimConnectService());

        Assert.False(vm.IsProjectStoreAvailable);
        Assert.False(vm.SaveProjectCommand.CanExecute(null));
        Assert.False(vm.LoadProjectCommand.CanExecute(null));
    }

    [Fact]
    public void IsProjectStoreAvailable_TrueWhenStoreInjected()
    {
        var vm = new MainViewModel(new FakeSimConnectService(), projectStore: new FakeAirportProjectStore());

        Assert.True(vm.IsProjectStoreAvailable);
    }

    [Fact]
    public async Task SaveProjectCommand_CanExecute_FalseUntilAirportLoaded()
    {
        var airport = new AirportDetails { Icao = "EFHK" };
        var fake = new FakeSimConnectService
        {
            OnGetAirportDetails = _ => Task.FromResult(new AirportLookupResult(AirportLookupStatus.Success, airport, null))
        };
        var store = new FakeAirportProjectStore();
        var vm = new MainViewModel(fake, projectStore: store) { IcaoInput = "EFHK" };

        Assert.False(vm.SaveProjectCommand.CanExecute(null));

        vm.LoadCommand.Execute(null);
        await Task.Delay(50);

        Assert.True(vm.SaveProjectCommand.CanExecute(null));
    }

    [Fact]
    public async Task SaveProjectCommand_Execute_DelegatesToStoreAndSetsLastProjectSavePath()
    {
        var airport = new AirportDetails { Icao = "EFHK" };
        var fake = new FakeSimConnectService
        {
            OnGetAirportDetails = _ => Task.FromResult(new AirportLookupResult(AirportLookupStatus.Success, airport, null))
        };
        var store = new FakeAirportProjectStore { PathToReturn = "Projects/EFHK.json" };
        var vm = new MainViewModel(fake, projectStore: store) { IcaoInput = "EFHK" };
        vm.LoadCommand.Execute(null);
        await Task.Delay(50);

        vm.SaveProjectCommand.Execute(null);

        Assert.Same(airport, store.LastSaved);
        Assert.Equal("Projects/EFHK.json", vm.LastProjectSavePath);
    }

    [Fact]
    public void LoadProjectCommand_NoSavedProject_SetsErrorMessage_AndLeavesAirportNull()
    {
        var store = new FakeAirportProjectStore();
        var vm = new MainViewModel(new FakeSimConnectService(), projectStore: store) { IcaoInput = "ZZZZ" };

        vm.LoadProjectCommand.Execute(null);

        Assert.Null(vm.Airport);
        Assert.Contains("ZZZZ", vm.ErrorMessage);
    }

    [Fact]
    public void LoadProjectCommand_SavedProjectExists_PopulatesAirportDiagramAndEdits()
    {
        var airport = new AirportDetails { Icao = "EFHK" };
        airport.Runways.Add(new Runway { PrimaryDesignation = "04L" });
        var store = new FakeAirportProjectStore();
        store.Save(airport);
        var vm = new MainViewModel(new FakeSimConnectService(), projectStore: store) { IcaoInput = "EFHK" };

        vm.LoadProjectCommand.Execute(null);

        Assert.Same(airport, vm.Airport);
        Assert.NotNull(vm.Diagram);
        Assert.NotEmpty(vm.AirportDataTree);
        Assert.Single(vm.RunwayEdits);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void AddTaxiNameCommand_CanExecute_FalseUntilAirportLoaded()
    {
        var vm = new MainViewModel(new FakeSimConnectService());

        Assert.False(vm.AddTaxiNameCommand.CanExecute(null));
    }

    [Fact]
    public void AddTaxiNameCommand_Execute_AddsBlankNameToAirportAndCollection()
    {
        var airport = new AirportDetails { Icao = "EFHK" };
        var vm = CreateViewModelWithAirport(airport);

        vm.AddTaxiNameCommand.Execute(null);

        Assert.Single(airport.TaxiNames);
        Assert.Single(vm.TaxiNames);
        Assert.Equal(airport.TaxiNames[0].Id, vm.TaxiNames[0].Id);
    }

    [Fact]
    public void DeleteTaxiNameCommand_Execute_RemovesFromAirportAndCollection_AndClearsReferencingPaths()
    {
        var airport = BuildAirportWithTwoTaxiways(out var name1, out var name2);
        var vm = CreateViewModelWithAirport(airport);
        var nameVm = vm.TaxiNames.Single(n => n.Id == name1.Id);

        vm.DeleteTaxiNameCommand.Execute(nameVm);

        Assert.DoesNotContain(airport.TaxiNames, n => n.Id == name1.Id);
        Assert.DoesNotContain(vm.TaxiNames, n => n.Id == name1.Id);
        Assert.Null(vm.TaxiPathEdits[0].TaxiNameId);
        Assert.Equal(name2.Id, vm.TaxiPathEdits[1].TaxiNameId);
    }

    [Fact]
    public void ToggleTaxiwaySelectionCommand_PlainClick_ReplacesSelection()
    {
        var airport = BuildAirportWithTwoTaxiways(out _, out _);
        var vm = CreateViewModelWithAirport(airport);
        var shape0 = vm.Diagram!.TaxiwaySegments[0];
        var shape1 = vm.Diagram!.TaxiwaySegments[1];
        vm.ToggleTaxiwaySelectionCommand.Execute(new TaxiwaySelectionRequest(shape0, ExtendSelection: false));

        vm.ToggleTaxiwaySelectionCommand.Execute(new TaxiwaySelectionRequest(shape1, ExtendSelection: false));

        Assert.False(shape0.IsSelected);
        Assert.True(shape1.IsSelected);
        Assert.Equal(1, vm.SelectedTaxiwayCount);
        Assert.True(vm.HasTaxiwaySelection);
    }

    [Fact]
    public void ToggleTaxiwaySelectionCommand_CtrlClick_ExtendsSelection()
    {
        var airport = BuildAirportWithTwoTaxiways(out _, out _);
        var vm = CreateViewModelWithAirport(airport);
        var shape0 = vm.Diagram!.TaxiwaySegments[0];
        var shape1 = vm.Diagram!.TaxiwaySegments[1];

        vm.ToggleTaxiwaySelectionCommand.Execute(new TaxiwaySelectionRequest(shape0, ExtendSelection: true));
        vm.ToggleTaxiwaySelectionCommand.Execute(new TaxiwaySelectionRequest(shape1, ExtendSelection: true));

        Assert.True(shape0.IsSelected);
        Assert.True(shape1.IsSelected);
        Assert.Equal(2, vm.SelectedTaxiwayCount);
    }

    [Fact]
    public void ApplyTaxiwayBatchEditCommand_WritesToEverySelectedPath_AndClearsSelection()
    {
        var airport = BuildAirportWithTwoTaxiways(out var name1, out _);
        var vm = CreateViewModelWithAirport(airport);
        var shape0 = vm.Diagram!.TaxiwaySegments[0];
        var shape1 = vm.Diagram!.TaxiwaySegments[1];
        vm.ToggleTaxiwaySelectionCommand.Execute(new TaxiwaySelectionRequest(shape0, ExtendSelection: true));
        vm.ToggleTaxiwaySelectionCommand.Execute(new TaxiwaySelectionRequest(shape1, ExtendSelection: true));
        vm.TaxiwayBatchEdit.TaxiNameId = name1.Id;
        vm.TaxiwayBatchEdit.LeftEdgeLighted = true;
        vm.TaxiwayBatchEdit.RightEdgeLighted = true;

        vm.ApplyTaxiwayBatchEditCommand.Execute(null);

        Assert.All(vm.TaxiPathEdits, edit =>
        {
            Assert.Equal(name1.Id, edit.TaxiNameId);
            Assert.True(edit.LeftEdgeLighted);
            Assert.True(edit.RightEdgeLighted);
        });
        Assert.False(vm.HasTaxiwaySelection);
    }

    [Fact]
    public void ApplyTaxiwayBatchEditCommand_UpdatesDiagramShapeLabelsImmediately()
    {
        var airport = BuildAirportWithTwoTaxiways(out var name1, out _);
        var vm = CreateViewModelWithAirport(airport);
        var shape0 = vm.Diagram!.TaxiwaySegments[0];
        var shape1 = vm.Diagram!.TaxiwaySegments[1]; // originally named "B"
        vm.ToggleTaxiwaySelectionCommand.Execute(new TaxiwaySelectionRequest(shape1, ExtendSelection: false));
        vm.TaxiwayBatchEdit.TaxiNameId = name1.Id;

        vm.ApplyTaxiwayBatchEditCommand.Execute(null);

        Assert.Equal("A", shape1.Name);
        Assert.True(shape1.HasName);
        Assert.Equal("A", shape0.Name); // unaffected, still its original name
    }

    [Fact]
    public void EditingTaxiNameId_ViaGridRow_UpdatesDiagramShapeLabelImmediately()
    {
        var airport = BuildAirportWithTwoTaxiways(out _, out var name2);
        var vm = CreateViewModelWithAirport(airport);
        var shape0 = vm.Diagram!.TaxiwaySegments[0]; // originally named "A"

        vm.TaxiPathEdits[0].TaxiNameId = name2.Id;

        Assert.Equal("B", shape0.Name);
        Assert.True(shape0.HasName);
    }

    [Fact]
    public void RenamingTaxiName_UpdatesEveryReferencingDiagramShape()
    {
        var name1 = new TaxiName { Value = "A" };
        var airport = new AirportDetails { Icao = "EFHK" };
        airport.TaxiNames.Add(name1);
        // Both paths share name1 — the exact relationship a flat per-segment
        // string used to break.
        airport.TaxiPaths.Add(new TaxiPathSegment { Type = TaxiPathType.Taxi, TaxiNameId = name1.Id, StartXMeters = 0, StartZMeters = 0, EndXMeters = 10, EndZMeters = 0 });
        airport.TaxiPaths.Add(new TaxiPathSegment { Type = TaxiPathType.Taxi, TaxiNameId = name1.Id, StartXMeters = 20, StartZMeters = 0, EndXMeters = 30, EndZMeters = 0 });
        var vm = CreateViewModelWithAirport(airport);

        vm.TaxiNames.Single(n => n.Id == name1.Id).Value = "Alpha";

        Assert.All(vm.Diagram!.TaxiwaySegments, shape => Assert.Equal("Alpha", shape.Name));
    }

    [Fact]
    public void DeletingTaxiName_ClearsDiagramShapeLabel()
    {
        var airport = BuildAirportWithTwoTaxiways(out var name1, out _);
        var vm = CreateViewModelWithAirport(airport);
        var shape0 = vm.Diagram!.TaxiwaySegments[0];

        vm.DeleteTaxiNameCommand.Execute(vm.TaxiNames.Single(n => n.Id == name1.Id));

        Assert.Equal(string.Empty, shape0.Name);
        Assert.False(shape0.HasName);
    }

    [Fact]
    public void TogglingIsHiddenFromDiagram_HidesShape_AndClearsItsSelection()
    {
        var airport = BuildAirportWithTwoTaxiways(out _, out _);
        var vm = CreateViewModelWithAirport(airport);
        var shape0 = vm.Diagram!.TaxiwaySegments[0];
        vm.ToggleTaxiwaySelectionCommand.Execute(new TaxiwaySelectionRequest(shape0, ExtendSelection: false));

        vm.TaxiPathEdits[0].IsHiddenFromDiagram = true;

        Assert.False(shape0.IsVisible);
        Assert.False(shape0.IsSelected);
        Assert.False(vm.HasTaxiwaySelection);
    }

    [Fact]
    public void UnhidingFromDiagram_MakesShapeVisibleAgain()
    {
        var airport = BuildAirportWithTwoTaxiways(out _, out _);
        var vm = CreateViewModelWithAirport(airport);
        var shape0 = vm.Diagram!.TaxiwaySegments[0];
        vm.TaxiPathEdits[0].IsHiddenFromDiagram = true;

        vm.TaxiPathEdits[0].IsHiddenFromDiagram = false;

        Assert.True(shape0.IsVisible);
    }

    [Fact]
    public void SyncTaxiwaySelectionFromRows_HighlightsMatchingShapes_ButDoesNotOpenPopover()
    {
        var airport = BuildAirportWithTwoTaxiways(out _, out _);
        var vm = CreateViewModelWithAirport(airport);
        var shape0 = vm.Diagram!.TaxiwaySegments[0];
        var shape1 = vm.Diagram!.TaxiwaySegments[1];

        vm.SyncTaxiwaySelectionFromRows([vm.TaxiPathEdits[0]]);

        Assert.True(shape0.IsSelected);
        Assert.False(shape1.IsSelected);
        Assert.True(vm.HasTaxiwaySelection);
        Assert.False(vm.ShowTaxiwayBatchEditPopover);
    }

    // A left-click on the diagram (ToggleTaxiwaySelectionCommand) only builds
    // the selection and filters the grid — it no longer opens the popover on
    // its own; only an explicit right-click (OpenTaxiwayBatchEditCommand)
    // does that. See ClickingDiagramTaxiway_FiltersVisibleTaxiPathEditsToSelection
    // for the filtering half of this behavior.
    [Fact]
    public void ToggleTaxiwaySelectionCommand_AloneDoesNotOpenPopover()
    {
        var airport = BuildAirportWithTwoTaxiways(out _, out _);
        var vm = CreateViewModelWithAirport(airport);
        var shape0 = vm.Diagram!.TaxiwaySegments[0];

        vm.ToggleTaxiwaySelectionCommand.Execute(new TaxiwaySelectionRequest(shape0, ExtendSelection: false));

        Assert.True(vm.HasTaxiwaySelection);
        Assert.False(vm.ShowTaxiwayBatchEditPopover);
    }

    [Fact]
    public void OpenTaxiwayBatchEditCommand_WithADiagramSelection_OpensPopover()
    {
        var airport = BuildAirportWithTwoTaxiways(out _, out _);
        var vm = CreateViewModelWithAirport(airport);
        var shape0 = vm.Diagram!.TaxiwaySegments[0];
        vm.ToggleTaxiwaySelectionCommand.Execute(new TaxiwaySelectionRequest(shape0, ExtendSelection: false));

        vm.OpenTaxiwayBatchEditCommand.Execute(null);

        Assert.True(vm.ShowTaxiwayBatchEditPopover);
    }

    [Fact]
    public void OpenTaxiwayBatchEditCommand_WithNoSelection_CanExecuteIsFalse()
    {
        var airport = BuildAirportWithTwoTaxiways(out _, out _);
        var vm = CreateViewModelWithAirport(airport);

        Assert.False(vm.OpenTaxiwayBatchEditCommand.CanExecute(null));
    }

    // The core of this feature: clicking a taxiway in the diagram filters the
    // Taxi Paths grid down to just that path, Ctrl+click extends the filter
    // to every selected path, and clearing the selection (background click,
    // via ClearTaxiwaySelectionCommand) shows every path again.
    [Fact]
    public void ClickingDiagramTaxiway_FiltersVisibleTaxiPathEditsToSelection()
    {
        var airport = BuildAirportWithTwoTaxiways(out _, out _);
        var vm = CreateViewModelWithAirport(airport);
        var shape0 = vm.Diagram!.TaxiwaySegments[0];
        var shape1 = vm.Diagram!.TaxiwaySegments[1];

        vm.ToggleTaxiwaySelectionCommand.Execute(new TaxiwaySelectionRequest(shape0, ExtendSelection: false));
        Assert.Equal(vm.TaxiPathEdits[0], Assert.Single(vm.VisibleTaxiPathEdits));

        vm.ToggleTaxiwaySelectionCommand.Execute(new TaxiwaySelectionRequest(shape1, ExtendSelection: true));
        Assert.Equal(2, vm.VisibleTaxiPathEdits.Count);
        Assert.Contains(vm.TaxiPathEdits[0], vm.VisibleTaxiPathEdits);
        Assert.Contains(vm.TaxiPathEdits[1], vm.VisibleTaxiPathEdits);

        vm.ClearTaxiwaySelectionCommand.Execute(null);
        Assert.Equal(2, vm.VisibleTaxiPathEdits.Count);
        Assert.False(vm.HasTaxiwaySelection);
    }

    [Fact]
    public void ClosingBatchEditPopover_ShowsEveryTaxiPathAgain()
    {
        var airport = BuildAirportWithTwoTaxiways(out _, out _);
        var vm = CreateViewModelWithAirport(airport);
        var shape0 = vm.Diagram!.TaxiwaySegments[0];
        vm.ToggleTaxiwaySelectionCommand.Execute(new TaxiwaySelectionRequest(shape0, ExtendSelection: false));
        vm.OpenTaxiwayBatchEditCommand.Execute(null);
        Assert.Single(vm.VisibleTaxiPathEdits);

        vm.CloseTaxiwayBatchEditCommand.Execute(null);

        Assert.Equal(2, vm.VisibleTaxiPathEdits.Count);
    }

    // Regression test for a reported bug: while a diagram-driven batch-edit
    // session is active (popover open), WPF marks a grid row "selected" as a
    // side effect of clicking ANY cell in it — including just toggling an
    // unrelated row's lighting checkbox — which used to silently replace the
    // diagram's selection via SyncTaxiwaySelectionFromRows, so Apply ended up
    // hitting whichever rows were incidentally clicked in the grid instead of
    // the ones actually selected on the diagram. SyncTaxiwaySelectionFromRows
    // must now be a no-op while the popover is open.
    [Fact]
    public void SyncTaxiwaySelectionFromRows_WhilePopoverOpen_IsIgnored()
    {
        var airport = BuildAirportWithTwoTaxiways(out _, out _);
        var vm = CreateViewModelWithAirport(airport);
        var shape0 = vm.Diagram!.TaxiwaySegments[0];
        var shape1 = vm.Diagram!.TaxiwaySegments[1];
        vm.ToggleTaxiwaySelectionCommand.Execute(new TaxiwaySelectionRequest(shape0, ExtendSelection: false));
        vm.OpenTaxiwayBatchEditCommand.Execute(null);
        Assert.True(vm.ShowTaxiwayBatchEditPopover);

        // Simulates an incidental grid click on the OTHER (unselected) row —
        // e.g. the user toggling its lighting checkbox mid-session.
        vm.SyncTaxiwaySelectionFromRows([vm.TaxiPathEdits[1]]);

        Assert.True(shape0.IsSelected); // untouched — still the diagram's own selection
        Assert.False(shape1.IsSelected); // not hijacked by the incidental grid click
        Assert.True(vm.ShowTaxiwayBatchEditPopover); // session stays open
    }

    [Fact]
    public void SyncTaxiwaySelectionFromRows_AfterPopoverCloses_WorksAgain()
    {
        var airport = BuildAirportWithTwoTaxiways(out _, out _);
        var vm = CreateViewModelWithAirport(airport);
        var shape0 = vm.Diagram!.TaxiwaySegments[0];
        var shape1 = vm.Diagram!.TaxiwaySegments[1];
        vm.ToggleTaxiwaySelectionCommand.Execute(new TaxiwaySelectionRequest(shape0, ExtendSelection: false));
        vm.OpenTaxiwayBatchEditCommand.Execute(null);
        vm.CloseTaxiwayBatchEditCommand.Execute(null);
        Assert.False(vm.ShowTaxiwayBatchEditPopover);

        vm.SyncTaxiwaySelectionFromRows([vm.TaxiPathEdits[1]]);

        Assert.False(shape0.IsSelected);
        Assert.True(shape1.IsSelected);
    }

    // Regression test for the other half of the same bug report: extending a
    // diagram selection (Ctrl+click) used to silently wipe out whatever the
    // user had already staged in the batch popover, because the popover was
    // reset to "untouched" on every single selection change rather than only
    // when a session actually ends.
    [Fact]
    public void ExtendingDiagramSelection_DoesNotResetAlreadyStagedBatchEditValues()
    {
        var airport = BuildAirportWithTwoTaxiways(out var name1, out _);
        var vm = CreateViewModelWithAirport(airport);
        var shape0 = vm.Diagram!.TaxiwaySegments[0];
        var shape1 = vm.Diagram!.TaxiwaySegments[1];
        vm.ToggleTaxiwaySelectionCommand.Execute(new TaxiwaySelectionRequest(shape0, ExtendSelection: false));
        vm.OpenTaxiwayBatchEditCommand.Execute(null);
        vm.TaxiwayBatchEdit.TaxiNameId = name1.Id;

        // Extend the selection to a second taxiway before applying — the
        // popover, already open from the right-click above, must stay open
        // and keep its staged value.
        vm.ToggleTaxiwaySelectionCommand.Execute(new TaxiwaySelectionRequest(shape1, ExtendSelection: true));

        Assert.True(vm.TaxiwayBatchEdit.IsTaxiNameIdTouched);
        Assert.Equal(name1.Id, vm.TaxiwayBatchEdit.TaxiNameId);

        vm.ApplyTaxiwayBatchEditCommand.Execute(null);

        Assert.Equal(name1.Id, vm.TaxiPathEdits[0].TaxiNameId);
        Assert.Equal(name1.Id, vm.TaxiPathEdits[1].TaxiNameId);
    }

    // Regression test for a reported bug: selecting two taxi paths that
    // didn't already agree on name/lighting, then touching only ONE field in
    // the batch popover (e.g. just Left Edge Lighted), must leave every OTHER
    // field on both selected paths exactly as it was — not stamp them with
    // whichever value the popover happened to show. See
    // TaxiwayBatchEditViewModel's doc comment for the earlier, buggy
    // "seed from the first selected path, then overwrite everything"
    // semantics this replaced.
    [Fact]
    public void ApplyTaxiwayBatchEditCommand_UntouchedFields_LeaveEachSelectedPathsOwnValueUnchanged()
    {
        var name1 = new TaxiName { Value = "A" };
        var name2 = new TaxiName { Value = "B" };
        var airport = new AirportDetails { Icao = "EFHK" };
        airport.TaxiNames.Add(name1);
        airport.TaxiNames.Add(name2);
        airport.TaxiPaths.Add(new TaxiPathSegment
        {
            Type = TaxiPathType.Taxi, TaxiNameId = name1.Id, RightEdgeLighted = true,
            StartXMeters = 0, StartZMeters = 0, EndXMeters = 10, EndZMeters = 0,
        });
        airport.TaxiPaths.Add(new TaxiPathSegment
        {
            Type = TaxiPathType.Taxi, TaxiNameId = name2.Id, RightEdgeLighted = false,
            StartXMeters = 20, StartZMeters = 0, EndXMeters = 30, EndZMeters = 0,
        });
        var vm = CreateViewModelWithAirport(airport);
        var shape0 = vm.Diagram!.TaxiwaySegments[0];
        var shape1 = vm.Diagram!.TaxiwaySegments[1];
        vm.ToggleTaxiwaySelectionCommand.Execute(new TaxiwaySelectionRequest(shape0, ExtendSelection: true));
        vm.ToggleTaxiwaySelectionCommand.Execute(new TaxiwaySelectionRequest(shape1, ExtendSelection: true));

        // Only touch LeftEdgeLighted — Name and RightEdgeLighted stay untouched.
        vm.TaxiwayBatchEdit.LeftEdgeLighted = true;
        vm.ApplyTaxiwayBatchEditCommand.Execute(null);

        Assert.Equal(name1.Id, vm.TaxiPathEdits[0].TaxiNameId); // unchanged
        Assert.Equal(name2.Id, vm.TaxiPathEdits[1].TaxiNameId); // unchanged, not stamped with name1
        Assert.True(vm.TaxiPathEdits[0].RightEdgeLighted); // unchanged
        Assert.False(vm.TaxiPathEdits[1].RightEdgeLighted); // unchanged, not stamped with path 0's value
        Assert.True(vm.TaxiPathEdits[0].LeftEdgeLighted); // the one field actually touched
        Assert.True(vm.TaxiPathEdits[1].LeftEdgeLighted);
    }

    [Fact]
    public void TogglingIsHiddenFromDiagram_ForRunway_HidesShape()
    {
        var airport = new AirportDetails { Icao = "EFHK" };
        airport.Runways.Add(new Runway { PrimaryDesignation = "04L" });
        airport.Runways.Add(new Runway { PrimaryDesignation = "22R" });
        var vm = CreateViewModelWithAirport(airport);
        var shape0 = vm.Diagram!.Runways[0];

        vm.RunwayEdits[0].IsHiddenFromDiagram = true;

        Assert.False(shape0.IsVisible);
        Assert.True(vm.Diagram!.Runways[1].IsVisible);
    }

    [Fact]
    public void UnhidingRunwayFromDiagram_MakesShapeVisibleAgain()
    {
        var airport = new AirportDetails { Icao = "EFHK" };
        airport.Runways.Add(new Runway { PrimaryDesignation = "04L" });
        var vm = CreateViewModelWithAirport(airport);
        var shape0 = vm.Diagram!.Runways[0];
        vm.RunwayEdits[0].IsHiddenFromDiagram = true;

        vm.RunwayEdits[0].IsHiddenFromDiagram = false;

        Assert.True(shape0.IsVisible);
    }
}
