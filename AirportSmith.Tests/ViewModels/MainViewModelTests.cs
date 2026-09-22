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

    // Two chained segments sharing a middle point index (1), giving 3
    // distinct TaxiwayPointShapes (indices 0, 1, 2) — enough to exercise
    // multi-select and "everything else still hidden" filtering.
    // BuildAirportWithTwoTaxiways above can't be reused here since its
    // segments leave StartIndex/EndIndex at their default (0), which would
    // collapse every point onto a single index.
    private static AirportDetails BuildAirportWithThreeTaxiwayPoints()
    {
        var airport = new AirportDetails { Icao = "EFHK" };
        airport.TaxiPaths.Add(new TaxiPathSegment
        {
            Type = TaxiPathType.Taxi, WidthMeters = 10, StartIndex = 0, EndIndex = 1,
            StartXMeters = 0, StartZMeters = 0, EndXMeters = 10, EndZMeters = 0,
        });
        airport.TaxiPaths.Add(new TaxiPathSegment
        {
            Type = TaxiPathType.Taxi, WidthMeters = 10, StartIndex = 1, EndIndex = 2,
            StartXMeters = 10, StartZMeters = 0, EndXMeters = 20, EndZMeters = 0,
        });
        return airport;
    }

    // Two Taxi-typed segments differing in every filterable field — Name,
    // Type isn't varied here (both Taxi, so TaxiwaySegmentShapes exist for
    // both, needed by the selection+filter combination test), Start/End
    // index, runway number, left/right edge type/lighting, center
    // line/lighting, so each field's own filter can be exercised in
    // isolation against a real non-trivial dataset.
    private static AirportDetails BuildAirportForTaxiPathFiltering(out TaxiName nameA, out TaxiName nameB)
    {
        nameA = new TaxiName { Value = "Alpha" };
        nameB = new TaxiName { Value = "Bravo" };
        var airport = new AirportDetails { Icao = "EFHK" };
        airport.TaxiNames.Add(nameA);
        airport.TaxiNames.Add(nameB);
        airport.TaxiPaths.Add(new TaxiPathSegment
        {
            Type = TaxiPathType.Taxi, TaxiNameId = nameA.Id, WidthMeters = 10,
            StartIndex = 0, EndIndex = 1, RunwayNumber = 9,
            RunwayDesignator = TaxiPathRunwayDesignator.Left,
            LeftEdge = TaxiEdgeType.Solid, LeftEdgeLighted = true,
            RightEdge = TaxiEdgeType.Dashed, RightEdgeLighted = false,
            CenterLine = true, CenterLineLighted = false,
            StartXMeters = 0, StartZMeters = 0, EndXMeters = 10, EndZMeters = 0,
        });
        airport.TaxiPaths.Add(new TaxiPathSegment
        {
            Type = TaxiPathType.Taxi, TaxiNameId = nameB.Id, WidthMeters = 10,
            StartIndex = 5, EndIndex = 6, RunwayNumber = 27,
            RunwayDesignator = TaxiPathRunwayDesignator.Right,
            LeftEdge = TaxiEdgeType.Dashed, LeftEdgeLighted = false,
            RightEdge = TaxiEdgeType.Solid, RightEdgeLighted = true,
            CenterLine = false, CenterLineLighted = true,
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
        airport.TaxiPaths.Add(new TaxiPathSegment { TaxiNameId = taxiName.Id, StartIndex = 0, EndIndex = 1, StartXMeters = 0, StartZMeters = 0, EndXMeters = 10, EndZMeters = 0 });
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
        Assert.Equal(2, vm.TaxiwayPointEdits.Count);
        Assert.Equal([0, 1], vm.TaxiwayPointEdits.Select(p => p.Index));
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
        Assert.Empty(vm.TaxiwayPointEdits);
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

    // Regression test for a real crash: TaxiNamesPicker must be a genuinely
    // separate collection INSTANCE from TaxiNames (not just an equivalent
    // one), even though it mirrors the same items live — see
    // MainViewModel.TaxiNamesPicker's own doc comment. Sharing the exact
    // same ObservableCollection instance between the Taxi Names grid and
    // every Taxi Paths row's Name-column ComboBox meant they all shared one
    // WPF default CollectionView, and editing a row in the Taxi Names grid
    // (putting that shared view into an edit-item transaction) while any
    // Name ComboBox elsewhere re-attached its ItemsSource binding threw
    // InvalidOperationException and crashed the app — reproduced directly
    // and confirmed via a .NET Runtime crash log entry against a real
    // build. A different collection instance gets its own independent
    // default view, sidestepping the conflict entirely.
    [Fact]
    public void TaxiNamesPicker_IsASeparateInstanceFromTaxiNames_ButMirrorsItsContentsLive()
    {
        var airport = new AirportDetails { Icao = "EFHK" };
        var vm = CreateViewModelWithAirport(airport);

        Assert.NotSame(vm.TaxiNames, vm.TaxiNamesPicker);
        Assert.Empty(vm.TaxiNamesPicker);

        vm.AddTaxiNameCommand.Execute(null);
        Assert.Single(vm.TaxiNamesPicker);
        Assert.Equal(vm.TaxiNames[0].Id, vm.TaxiNamesPicker[0].Id);

        vm.DeleteTaxiNameCommand.Execute(vm.TaxiNames[0]);
        Assert.Empty(vm.TaxiNamesPicker);
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

    [Fact]
    public void IsXmlExportAvailable_RequiresBothExporterAndDialogService()
    {
        Assert.False(new MainViewModel(new FakeSimConnectService()).IsXmlExportAvailable);
        Assert.False(new MainViewModel(new FakeSimConnectService(), xmlExporter: new FakeAirportXmlExporter()).IsXmlExportAvailable);
        Assert.False(new MainViewModel(new FakeSimConnectService(), fileDialogService: new FakeFileDialogService()).IsXmlExportAvailable);
        Assert.True(new MainViewModel(new FakeSimConnectService(), fileDialogService: new FakeFileDialogService(), xmlExporter: new FakeAirportXmlExporter()).IsXmlExportAvailable);
    }

    [Fact]
    public void IsMapAvailable_ReflectsWhetherMapTileServiceWasInjected()
    {
        Assert.False(new MainViewModel(new FakeSimConnectService()).IsMapAvailable);
        Assert.True(new MainViewModel(new FakeSimConnectService(), mapTileService: new FakeMapTileService()).IsMapAvailable);
    }

    [Fact]
    public void ShowMap_DefaultsFalse_AndRaisesPropertyChangedWhenSet()
    {
        var vm = new MainViewModel(new FakeSimConnectService(), mapTileService: new FakeMapTileService());
        Assert.False(vm.ShowMap);

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.ShowMap = true;

        Assert.True(vm.ShowMap);
        Assert.Contains(nameof(MainViewModel.ShowMap), raised);
    }

    [Fact]
    public void ExportXmlCommand_CanExecute_FalseUntilAirportLoaded()
    {
        var vm = new MainViewModel(new FakeSimConnectService(), fileDialogService: new FakeFileDialogService(), xmlExporter: new FakeAirportXmlExporter());

        Assert.False(vm.ExportXmlCommand.CanExecute(null));

        var airport = new AirportDetails { Icao = "EFHK" };
        var projectStore = new FakeAirportProjectStore();
        projectStore.Save(airport);
        var vmWithProjectStore = new MainViewModel(new FakeSimConnectService(), projectStore: projectStore,
            fileDialogService: new FakeFileDialogService(), xmlExporter: new FakeAirportXmlExporter())
        { IcaoInput = "EFHK" };
        vmWithProjectStore.LoadProjectCommand.Execute(null);

        Assert.True(vmWithProjectStore.ExportXmlCommand.CanExecute(null));
    }

    [Fact]
    public void ExportXmlCommand_Execute_DelegatesToExporterAndSetsPathAndWarnings()
    {
        var airport = new AirportDetails { Icao = "EFHK" };
        var projectStore = new FakeAirportProjectStore();
        projectStore.Save(airport);
        var dialog = new FakeFileDialogService { PathToReturn = "C:/out/EFHK.xml" };
        var exporter = new FakeAirportXmlExporter
        {
            PathToReturn = "C:/out/EFHK.xml",
            WarningsToReturn = ["taxi path 0->1 skipped"],
        };
        var vm = new MainViewModel(new FakeSimConnectService(), projectStore: projectStore,
            fileDialogService: dialog, xmlExporter: exporter)
        { IcaoInput = "EFHK" };
        vm.LoadProjectCommand.Execute(null);

        vm.ExportXmlCommand.Execute(null);

        Assert.Same(airport, exporter.LastExported);
        Assert.Equal("C:/out/EFHK.xml", vm.LastXmlExportPath);
        Assert.Equal(["taxi path 0->1 skipped"], vm.LastXmlExportWarnings);
    }

    [Fact]
    public void ExportXmlCommand_UserCancelsDialog_LeavesPathAndWarningsUnchanged()
    {
        var airport = new AirportDetails { Icao = "EFHK" };
        var projectStore = new FakeAirportProjectStore();
        projectStore.Save(airport);
        var dialog = new FakeFileDialogService { PathToReturn = null };
        var exporter = new FakeAirportXmlExporter();
        var vm = new MainViewModel(new FakeSimConnectService(), projectStore: projectStore,
            fileDialogService: dialog, xmlExporter: exporter)
        { IcaoInput = "EFHK" };
        vm.LoadProjectCommand.Execute(null);

        vm.ExportXmlCommand.Execute(null);

        Assert.Null(exporter.LastExported);
        Assert.Null(vm.LastXmlExportPath);
        Assert.Empty(vm.LastXmlExportWarnings);
    }

    // Same "click a shape, see just that shape's data" pattern as
    // ClickingDiagramTaxiway_FiltersVisibleTaxiPathEditsToSelection, for
    // taxiway points — an independent selection from taxi paths, but
    // ClearTaxiwaySelectionCommand (the shared background-click handler)
    // resets both at once.
    [Fact]
    public void ClickingDiagramTaxiwayPoint_FiltersVisibleTaxiwayPointEditsToSelection()
    {
        var airport = BuildAirportWithThreeTaxiwayPoints();
        var vm = CreateViewModelWithAirport(airport);
        var point0 = vm.Diagram!.TaxiwayPoints[0];
        var point1 = vm.Diagram!.TaxiwayPoints[1];

        vm.ToggleTaxiwayPointSelectionCommand.Execute(new TaxiwayPointSelectionRequest(point0, ExtendSelection: false));
        Assert.Equal(vm.TaxiwayPointEdits[0], Assert.Single(vm.VisibleTaxiwayPointEdits));

        vm.ToggleTaxiwayPointSelectionCommand.Execute(new TaxiwayPointSelectionRequest(point1, ExtendSelection: true));
        Assert.Equal(2, vm.VisibleTaxiwayPointEdits.Count);
        Assert.Contains(vm.TaxiwayPointEdits[0], vm.VisibleTaxiwayPointEdits);
        Assert.Contains(vm.TaxiwayPointEdits[1], vm.VisibleTaxiwayPointEdits);

        vm.ClearTaxiwaySelectionCommand.Execute(null);
        Assert.Equal(3, vm.VisibleTaxiwayPointEdits.Count);
        Assert.All(vm.Diagram.TaxiwayPoints, p => Assert.False(p.IsSelected));
    }

    [Fact]
    public void TogglingTaxiwayPointSelection_PlainClickReplacesSelection_CtrlClickToggles()
    {
        var airport = BuildAirportWithThreeTaxiwayPoints();
        var vm = CreateViewModelWithAirport(airport);
        var point0 = vm.Diagram!.TaxiwayPoints[0];
        var point1 = vm.Diagram!.TaxiwayPoints[1];
        vm.ToggleTaxiwayPointSelectionCommand.Execute(new TaxiwayPointSelectionRequest(point0, ExtendSelection: false));

        // A plain click on a different point replaces the selection...
        vm.ToggleTaxiwayPointSelectionCommand.Execute(new TaxiwayPointSelectionRequest(point1, ExtendSelection: false));
        Assert.False(point0.IsSelected);
        Assert.True(point1.IsSelected);

        // ...while a Ctrl+click toggles membership without touching the rest.
        vm.ToggleTaxiwayPointSelectionCommand.Execute(new TaxiwayPointSelectionRequest(point0, ExtendSelection: true));
        Assert.True(point0.IsSelected);
        Assert.True(point1.IsSelected);
        vm.ToggleTaxiwayPointSelectionCommand.Execute(new TaxiwayPointSelectionRequest(point1, ExtendSelection: true));
        Assert.True(point0.IsSelected);
        Assert.False(point1.IsSelected);
    }

    [Fact]
    public void HideAllTaxiwayPoints_HidesEveryPointShape_AndClearsSelection()
    {
        var airport = BuildAirportWithThreeTaxiwayPoints();
        var vm = CreateViewModelWithAirport(airport);
        var point0 = vm.Diagram!.TaxiwayPoints[0];
        vm.ToggleTaxiwayPointSelectionCommand.Execute(new TaxiwayPointSelectionRequest(point0, ExtendSelection: false));

        vm.HideAllTaxiwayPoints = true;

        Assert.All(vm.Diagram.TaxiwayPoints, p => Assert.False(p.IsVisible));
        // A hidden point has nothing to highlight — same reasoning as
        // RefreshAllTaxiwayVisibility hiding a selected taxi path.
        Assert.False(point0.IsSelected);
        Assert.Equal(3, vm.VisibleTaxiwayPointEdits.Count);

        vm.HideAllTaxiwayPoints = false;
        Assert.All(vm.Diagram.TaxiwayPoints, p => Assert.True(p.IsVisible));
    }

    [Fact]
    public void LoadingNewAirport_ResetsHideAllTaxiwayPointsAndShapeVisibility()
    {
        var airport1 = BuildAirportWithThreeTaxiwayPoints();
        var store = new FakeAirportProjectStore();
        store.Save(airport1);
        var vm = new MainViewModel(new FakeSimConnectService(), projectStore: store) { IcaoInput = airport1.Icao };
        vm.LoadProjectCommand.Execute(null);
        vm.HideAllTaxiwayPoints = true;

        var airport2 = BuildAirportWithThreeTaxiwayPoints();
        airport2.Icao = "EGLL";
        store.Save(airport2);
        vm.IcaoInput = "EGLL";
        vm.LoadProjectCommand.Execute(null);

        Assert.False(vm.HideAllTaxiwayPoints);
        Assert.All(vm.Diagram!.TaxiwayPoints, p => Assert.True(p.IsVisible));
    }

    [Fact]
    public void TaxiPathFilter_NameFilter_MatchesResolvedDisplayNameCaseInsensitively()
    {
        var airport = BuildAirportForTaxiPathFiltering(out var nameA, out _);
        var vm = CreateViewModelWithAirport(airport);

        vm.TaxiPathFilter.NameFilter = "alp";

        var match = Assert.Single(vm.VisibleTaxiPathEdits);
        Assert.Equal(nameA.Id, match.TaxiNameId);
    }

    [Fact]
    public void TaxiPathFilter_StartAndEndIndexFilter_MatchesSubstringAgainstEachIndex()
    {
        var airport = BuildAirportForTaxiPathFiltering(out _, out _);
        var vm = CreateViewModelWithAirport(airport);

        vm.TaxiPathFilter.StartIndexFilter = "5";
        Assert.Equal(5, Assert.Single(vm.VisibleTaxiPathEdits).StartIndex);

        vm.TaxiPathFilter.StartIndexFilter = null;
        vm.TaxiPathFilter.EndIndexFilter = "1";
        Assert.Equal(1, Assert.Single(vm.VisibleTaxiPathEdits).EndIndex);
    }

    [Fact]
    public void TaxiPathFilter_RunwayNumberAndDesignatorFilters_MatchExactly()
    {
        var airport = BuildAirportForTaxiPathFiltering(out _, out _);
        var vm = CreateViewModelWithAirport(airport);

        vm.TaxiPathFilter.RunwayNumberFilter = "27";
        Assert.Equal(27, Assert.Single(vm.VisibleTaxiPathEdits).RunwayNumber);

        vm.TaxiPathFilter.RunwayNumberFilter = null;
        vm.TaxiPathFilter.RunwayDesignatorFilter = TaxiPathRunwayDesignator.Left;
        Assert.Equal(TaxiPathRunwayDesignator.Left, Assert.Single(vm.VisibleTaxiPathEdits).RunwayDesignator);
    }

    [Fact]
    public void TaxiPathFilter_EdgeTypeAndLightingBoolFilters_MatchExactly()
    {
        var airport = BuildAirportForTaxiPathFiltering(out _, out _);
        var vm = CreateViewModelWithAirport(airport);

        vm.TaxiPathFilter.LeftEdgeFilter = TaxiEdgeType.Dashed;
        Assert.Equal(TaxiEdgeType.Dashed, Assert.Single(vm.VisibleTaxiPathEdits).LeftEdge);

        vm.TaxiPathFilter.LeftEdgeFilter = null;
        vm.TaxiPathFilter.CenterLineLightedFilter = true;
        Assert.True(Assert.Single(vm.VisibleTaxiPathEdits).CenterLineLighted);
    }

    // The diagram click-to-select filter and the per-column field filters
    // combine (AND) rather than one replacing the other.
    [Fact]
    public void TaxiPathFilter_CombinesWithDiagramSelection()
    {
        var airport = BuildAirportForTaxiPathFiltering(out _, out var nameB);
        var vm = CreateViewModelWithAirport(airport);
        var shape0 = vm.Diagram!.TaxiwaySegments[0];
        var shape1 = vm.Diagram!.TaxiwaySegments[1];
        vm.ToggleTaxiwaySelectionCommand.Execute(new TaxiwaySelectionRequest(shape0, ExtendSelection: false));
        vm.ToggleTaxiwaySelectionCommand.Execute(new TaxiwaySelectionRequest(shape1, ExtendSelection: true));
        Assert.Equal(2, vm.VisibleTaxiPathEdits.Count); // both selected, no field filter yet

        vm.TaxiPathFilter.NameFilter = "Bravo";

        var match = Assert.Single(vm.VisibleTaxiPathEdits);
        Assert.Equal(nameB.Id, match.TaxiNameId);
    }

    [Fact]
    public void ClearTaxiPathFilterCommand_ResetsFiltersAndReflectsCanExecute()
    {
        var airport = BuildAirportForTaxiPathFiltering(out _, out _);
        var vm = CreateViewModelWithAirport(airport);
        Assert.False(vm.ClearTaxiPathFilterCommand.CanExecute(null));

        vm.TaxiPathFilter.RunwayNumberFilter = "9";
        Assert.True(vm.ClearTaxiPathFilterCommand.CanExecute(null));
        Assert.Single(vm.VisibleTaxiPathEdits);

        vm.ClearTaxiPathFilterCommand.Execute(null);

        Assert.False(vm.ClearTaxiPathFilterCommand.CanExecute(null));
        Assert.False(vm.TaxiPathFilter.HasAnyFilter);
        Assert.Equal(2, vm.VisibleTaxiPathEdits.Count);
    }

    // A filter must reflect current field values live — editing a row out
    // of matching a filter should drop it from VisibleTaxiPathEdits
    // immediately, same as if that value had been there when the filter was
    // first typed.
    [Fact]
    public void EditingAFilteredField_LiveRemovesRowFromVisibleWhenNoLongerMatching()
    {
        var airport = BuildAirportForTaxiPathFiltering(out _, out _);
        var vm = CreateViewModelWithAirport(airport);
        vm.TaxiPathFilter.TypeFilter = TaxiPathType.Taxi;
        Assert.Equal(2, vm.VisibleTaxiPathEdits.Count);

        vm.TaxiPathEdits[0].Type = TaxiPathType.Runway;

        Assert.Single(vm.VisibleTaxiPathEdits);
        Assert.Equal(TaxiPathType.Taxi, vm.VisibleTaxiPathEdits[0].Type);
    }

    [Fact]
    public void LoadingNewAirport_ResetsTaxiPathFilter()
    {
        var airport1 = BuildAirportForTaxiPathFiltering(out _, out _);
        var store = new FakeAirportProjectStore();
        store.Save(airport1);
        var vm = new MainViewModel(new FakeSimConnectService(), projectStore: store) { IcaoInput = airport1.Icao };
        vm.LoadProjectCommand.Execute(null);
        vm.TaxiPathFilter.NameFilter = "Alpha";
        Assert.Single(vm.VisibleTaxiPathEdits);

        var airport2 = BuildAirportForTaxiPathFiltering(out _, out _);
        airport2.Icao = "EGLL";
        store.Save(airport2);
        vm.IcaoInput = "EGLL";
        vm.LoadProjectCommand.Execute(null);

        Assert.False(vm.TaxiPathFilter.HasAnyFilter);
        Assert.Equal(2, vm.VisibleTaxiPathEdits.Count);
    }

    private static AirportDetails BuildAirportForVasiPlacement()
    {
        var airport = new AirportDetails { Icao = "EFHK", Latitude = 0, Longitude = 0 };
        airport.Runways.Add(new Runway
        {
            PrimaryDesignation = "09",
            SecondaryDesignation = "27",
            Latitude = 0,
            Longitude = 0,
            HeadingDeg = 0,
            LengthMeters = 1000,
            WidthMeters = 100,
        });
        return airport;
    }

    [Fact]
    public void ArmVasiPlacementCommand_ArmsPlacementAndSetsStatusText()
    {
        var vm = CreateViewModelWithAirport(BuildAirportForVasiPlacement());

        Assert.False(vm.IsVasiPlacementArmed);
        Assert.Null(vm.VasiPlacementStatusText);

        vm.ArmPrimaryLeftVasiPlacementCommand.Execute(vm.RunwayEdits[0]);

        Assert.True(vm.IsVasiPlacementArmed);
        Assert.Contains("09", vm.VasiPlacementStatusText);
        Assert.Contains("27", vm.VasiPlacementStatusText);
    }

    [Fact]
    public void PlaceVasiCommand_CanExecute_OnlyTrueWhilePlacementArmed()
    {
        var vm = CreateViewModelWithAirport(BuildAirportForVasiPlacement());

        Assert.False(vm.PlaceVasiCommand.CanExecute(new Point2D(0, 0)));

        vm.ArmPrimaryLeftVasiPlacementCommand.Execute(vm.RunwayEdits[0]);

        Assert.True(vm.PlaceVasiCommand.CanExecute(new Point2D(0, 0)));
    }

    [Fact]
    public void PlaceVasiCommand_WritesBiasIntoArmedSlot_UpdatesDiagramShape_AndDisarms()
    {
        var vm = CreateViewModelWithAirport(BuildAirportForVasiPlacement());
        vm.ArmPrimaryLeftVasiPlacementCommand.Execute(vm.RunwayEdits[0]);

        // Threshold1 (primary) for this fixture sits at screen (100, 1050) —
        // see AirportDiagramProjectorTests' identical fixture. Clicking 20
        // screen-px to the right and 30 above it should resolve to
        // BiasX=20, BiasZ=30 (see ComputeVasiBias_InvertsComputeVasiPlacementsPosition).
        vm.PlaceVasiCommand.Execute(new Point2D(120, 1020));

        Assert.Equal(20, vm.RunwayEdits[0].PrimaryLeftVasiBiasXMeters);
        Assert.Equal(30, vm.RunwayEdits[0].PrimaryLeftVasiBiasZMeters);

        var shape = vm.Diagram!.VasiLights.Single(v => v.SourceRunwayIndex == 0 && v.Slot == VasiSlot.PrimaryLeft);
        Assert.Equal(120, shape.Position.X, 3);
        Assert.Equal(1020, shape.Position.Y, 3);

        Assert.False(vm.IsVasiPlacementArmed);
        Assert.Null(vm.VasiPlacementStatusText);
    }

    [Fact]
    public void SettingVasiTypeThroughEditViewModel_MakesDiagramShapeInstalled()
    {
        var vm = CreateViewModelWithAirport(BuildAirportForVasiPlacement());
        var shape = vm.Diagram!.VasiLights.Single(v => v.SourceRunwayIndex == 0 && v.Slot == VasiSlot.PrimaryRight);
        Assert.False(shape.IsInstalled);

        vm.RunwayEdits[0].PrimaryRightVasiType = VasiType.Papi4;

        Assert.True(shape.IsInstalled);
        // The Type setter's own default-position suggestion (BiasX=0,
        // BiasZ=300 inward from Threshold1 at screen (100, 1050) — see
        // AirportDiagramProjectorTests' identical fixture) should already be
        // reflected on the diagram shape without any further action.
        Assert.Equal(100, shape.Position.X, 3);
        Assert.Equal(750, shape.Position.Y, 3);
    }

    // Three spots spread across the canvas; airport-reference (0,0) with no
    // runways, so the diagram's screen mapping is just
    // screenX = X + OriginXMeters, screenY = OriginZMeters - Z.
    private static AirportDetails BuildAirportWithThreeParkingSpots()
    {
        var airport = new AirportDetails { Icao = "EFHK", Latitude = 0, Longitude = 0 };
        airport.ParkingSpots.Add(new TaxiParkingSpot { ItemIndex = 0, Number = 1, BiasXMeters = 0, BiasZMeters = 0, RadiusMeters = 10 });
        airport.ParkingSpots.Add(new TaxiParkingSpot { ItemIndex = 1, Number = 2, BiasXMeters = 100, BiasZMeters = 0, RadiusMeters = 10 });
        airport.ParkingSpots.Add(new TaxiParkingSpot { ItemIndex = 2, Number = 3, BiasXMeters = 200, BiasZMeters = 0, RadiusMeters = 10 });
        return airport;
    }

    [Fact]
    public void ParkingSpotEdits_OneRowPerSpot_AndAllVisibleWhenNothingSelected()
    {
        var vm = CreateViewModelWithAirport(BuildAirportWithThreeParkingSpots());

        Assert.Equal(3, vm.ParkingSpotEdits.Count);
        Assert.Equal(3, vm.VisibleParkingSpotEdits.Count);
        Assert.Equal(3, vm.Diagram!.ParkingSpots.Count);
    }

    [Fact]
    public void ToggleParkingSpotSelection_PlainClick_SelectsOnlyThatSpot_AndFiltersGrid()
    {
        var vm = CreateViewModelWithAirport(BuildAirportWithThreeParkingSpots());
        var shapes = vm.Diagram!.ParkingSpots;

        vm.ToggleParkingSpotSelectionCommand.Execute(new ParkingSpotSelectionRequest(shapes[1], ExtendSelection: false));
        Assert.Equal([false, true, false], shapes.Select(s => s.IsSelected));
        Assert.Equal([2], vm.VisibleParkingSpotEdits.Select(e => e.Number));

        // A plain click on another spot replaces the selection.
        vm.ToggleParkingSpotSelectionCommand.Execute(new ParkingSpotSelectionRequest(shapes[2], ExtendSelection: false));
        Assert.Equal([3], vm.VisibleParkingSpotEdits.Select(e => e.Number));
    }

    [Fact]
    public void ToggleParkingSpotSelection_CtrlClick_ExtendsAndTogglesSelection()
    {
        var vm = CreateViewModelWithAirport(BuildAirportWithThreeParkingSpots());
        var shapes = vm.Diagram!.ParkingSpots;

        vm.ToggleParkingSpotSelectionCommand.Execute(new ParkingSpotSelectionRequest(shapes[0], ExtendSelection: false));
        vm.ToggleParkingSpotSelectionCommand.Execute(new ParkingSpotSelectionRequest(shapes[2], ExtendSelection: true));
        Assert.Equal([1, 3], vm.VisibleParkingSpotEdits.Select(e => e.Number));

        vm.ToggleParkingSpotSelectionCommand.Execute(new ParkingSpotSelectionRequest(shapes[0], ExtendSelection: true));
        Assert.Equal([3], vm.VisibleParkingSpotEdits.Select(e => e.Number));
    }

    [Fact]
    public void ClearTaxiwaySelection_AlsoClearsParkingSelection_AndShowsEveryRowAgain()
    {
        var vm = CreateViewModelWithAirport(BuildAirportWithThreeParkingSpots());
        vm.ToggleParkingSpotSelectionCommand.Execute(new ParkingSpotSelectionRequest(vm.Diagram!.ParkingSpots[1], false));

        vm.ClearTaxiwaySelectionCommand.Execute(null);

        Assert.All(vm.Diagram.ParkingSpots, s => Assert.False(s.IsSelected));
        Assert.Equal(3, vm.VisibleParkingSpotEdits.Count);
    }

    [Fact]
    public void EditingParkingSpotPosition_MovesTheMatchingDiagramShapeOnly()
    {
        var vm = CreateViewModelWithAirport(BuildAirportWithThreeParkingSpots());
        var diagram = vm.Diagram!;
        var untouchedBefore = diagram.ParkingSpots[2].Center;

        vm.ParkingSpotEdits[1].BiasXMeters = 60;
        vm.ParkingSpotEdits[1].BiasZMeters = 25;

        var moved = diagram.ParkingSpots[1];
        Assert.Equal(60 + diagram.OriginXMeters, moved.Center.X, 3);
        Assert.Equal(diagram.OriginZMeters - 25, moved.Center.Y, 3);
        Assert.Equal(untouchedBefore, diagram.ParkingSpots[2].Center);
        // The edit went into the real airport model too.
        Assert.Equal(60, vm.Airport!.ParkingSpots[1].BiasXMeters);
        Assert.Equal(25, vm.Airport.ParkingSpots[1].BiasZMeters);
    }

    [Fact]
    public void EditingParkingSpotHeadingRadiusAndNumber_UpdatesDiagramShape()
    {
        var vm = CreateViewModelWithAirport(BuildAirportWithThreeParkingSpots());
        var diagram = vm.Diagram!;
        var shape = diagram.ParkingSpots[0];

        vm.ParkingSpotEdits[0].RadiusMeters = 20;
        vm.ParkingSpotEdits[0].HeadingDeg = 90;
        vm.ParkingSpotEdits[0].Number = 42;

        Assert.Equal(20, shape.RadiusMeters);
        Assert.Equal("42", shape.Label);
        // Heading 90 = east: tip is 1.5 radii (30m) to the right of center, same row.
        Assert.Equal(shape.Center.X + 30, shape.HeadingTip.X, 3);
        Assert.Equal(shape.Center.Y, shape.HeadingTip.Y, 3);
    }

    [Fact]
    public void EditingParkingSpotTypeOrName_DoesNotDisturbDiagramGeometry()
    {
        var vm = CreateViewModelWithAirport(BuildAirportWithThreeParkingSpots());
        var shape = vm.Diagram!.ParkingSpots[0];
        var centerBefore = shape.Center;

        vm.ParkingSpotEdits[0].Type = 10;
        vm.ParkingSpotEdits[0].NameCode = 10;

        Assert.Equal(centerBefore, shape.Center);
        Assert.Equal(10, vm.Airport!.ParkingSpots[0].Type);
    }

    [Fact]
    public void HideAllParkingSpots_HidesEveryShape_ClearsSelection_AndRestores()
    {
        var vm = CreateViewModelWithAirport(BuildAirportWithThreeParkingSpots());
        vm.ToggleParkingSpotSelectionCommand.Execute(new ParkingSpotSelectionRequest(vm.Diagram!.ParkingSpots[0], false));

        vm.HideAllParkingSpots = true;

        Assert.All(vm.Diagram.ParkingSpots, s => Assert.False(s.IsVisible));
        Assert.All(vm.Diagram.ParkingSpots, s => Assert.False(s.IsSelected));
        Assert.Equal(3, vm.VisibleParkingSpotEdits.Count);

        vm.HideAllParkingSpots = false;

        Assert.All(vm.Diagram.ParkingSpots, s => Assert.True(s.IsVisible));
    }

    [Fact]
    public void ArmParkingPlacementCommand_ArmsPlacementAndSetsStatusText()
    {
        var vm = CreateViewModelWithAirport(BuildAirportWithThreeParkingSpots());

        Assert.False(vm.IsParkingPlacementArmed);
        Assert.Null(vm.ParkingPlacementStatusText);
        Assert.False(vm.PlaceParkingCommand.CanExecute(new Point2D(0, 0)));

        vm.ArmParkingPlacementCommand.Execute(vm.ParkingSpotEdits[1]);

        Assert.True(vm.IsParkingPlacementArmed);
        Assert.Contains("2", vm.ParkingPlacementStatusText);
        Assert.True(vm.PlaceParkingCommand.CanExecute(new Point2D(0, 0)));
    }

    [Fact]
    public void PlaceParkingCommand_WritesBiasIntoArmedSpot_MovesShape_AndDisarms()
    {
        var vm = CreateViewModelWithAirport(BuildAirportWithThreeParkingSpots());
        var diagram = vm.Diagram!;
        vm.ArmParkingPlacementCommand.Execute(vm.ParkingSpotEdits[1]);

        var click = new Point2D(diagram.OriginXMeters + 70, diagram.OriginZMeters - 40);
        vm.PlaceParkingCommand.Execute(click);

        Assert.Equal(70, vm.ParkingSpotEdits[1].BiasXMeters, 3);
        Assert.Equal(40, vm.ParkingSpotEdits[1].BiasZMeters, 3);
        Assert.Equal(click.X, diagram.ParkingSpots[1].Center.X, 3);
        Assert.Equal(click.Y, diagram.ParkingSpots[1].Center.Y, 3);
        // Other spots untouched.
        Assert.Equal(0, vm.ParkingSpotEdits[0].BiasXMeters);
        Assert.Equal(200, vm.ParkingSpotEdits[2].BiasXMeters);

        Assert.False(vm.IsParkingPlacementArmed);
        Assert.Null(vm.ParkingPlacementStatusText);
        Assert.False(vm.PlaceParkingCommand.CanExecute(click));
    }

    [Fact]
    public void ArmingParkingAndVasiPlacement_AreMutuallyExclusive()
    {
        var airport = BuildAirportForVasiPlacement();
        airport.ParkingSpots.Add(new TaxiParkingSpot { ItemIndex = 0, Number = 1, RadiusMeters = 10 });
        var vm = CreateViewModelWithAirport(airport);

        vm.ArmParkingPlacementCommand.Execute(vm.ParkingSpotEdits[0]);
        vm.ArmPrimaryLeftVasiPlacementCommand.Execute(vm.RunwayEdits[0]);

        Assert.True(vm.IsVasiPlacementArmed);
        Assert.False(vm.IsParkingPlacementArmed);
        Assert.False(vm.PlaceParkingCommand.CanExecute(new Point2D(0, 0)));

        vm.ArmParkingPlacementCommand.Execute(vm.ParkingSpotEdits[0]);

        Assert.True(vm.IsParkingPlacementArmed);
        Assert.False(vm.IsVasiPlacementArmed);
        Assert.False(vm.PlaceVasiCommand.CanExecute(new Point2D(0, 0)));
    }

    [Fact]
    public void LoadingAnotherAirport_DisarmsParkingPlacement_AndResetsHideAll()
    {
        var store = new FakeAirportProjectStore();
        store.Save(BuildAirportWithThreeParkingSpots());
        var vm = new MainViewModel(new FakeSimConnectService(), projectStore: store) { IcaoInput = "EFHK" };
        vm.LoadProjectCommand.Execute(null);
        vm.ArmParkingPlacementCommand.Execute(vm.ParkingSpotEdits[0]);
        vm.HideAllParkingSpots = true;

        vm.LoadProjectCommand.Execute(null);

        Assert.False(vm.IsParkingPlacementArmed);
        Assert.False(vm.HideAllParkingSpots);
        Assert.All(vm.Diagram!.ParkingSpots, s => Assert.True(s.IsVisible));
    }
}
