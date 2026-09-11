using AirportSmith.Models;
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
}
