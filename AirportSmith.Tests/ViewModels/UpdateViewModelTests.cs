using System.Net.Http;
using AirportSmith.Tests.Fakes;
using AirportSmith.ViewModels;

namespace AirportSmith.Tests.ViewModels;

public class UpdateViewModelTests
{
    [Fact]
    public async Task BackgroundCheck_NewerVersionPublished_DownloadsAndShowsReadyBadge()
    {
        var service = new FakeUpdateService { AvailableVersion = "0.0.2" };
        var vm = new UpdateViewModel(service);

        await vm.CheckInBackgroundAsync();

        Assert.Equal(1, service.DownloadCount);
        Assert.True(vm.IsUpdateReady);
        Assert.Equal("0.0.2", vm.ReadyVersion);
        Assert.Contains("v0.0.2", vm.UpdateReadyText);
        Assert.True(vm.RestartToUpdateCommand.CanExecute(null));
    }

    [Fact]
    public async Task BackgroundCheck_UpToDate_ShowsNothing()
    {
        var service = new FakeUpdateService { AvailableVersion = null };
        var vm = new UpdateViewModel(service);

        await vm.CheckInBackgroundAsync();

        Assert.Equal(0, service.DownloadCount);
        Assert.False(vm.IsUpdateReady);
        Assert.Null(vm.StatusText);
        Assert.False(vm.RestartToUpdateCommand.CanExecute(null));
    }

    [Fact]
    public async Task BackgroundCheck_Failure_IsSilentAndNonFatal()
    {
        var service = new FakeUpdateService { CheckException = new HttpRequestException("offline") };
        var vm = new UpdateViewModel(service);

        await vm.CheckInBackgroundAsync(); // must not throw

        Assert.False(vm.IsUpdateReady);
        Assert.Null(vm.StatusText);
    }

    [Fact]
    public async Task BackgroundCheck_NotInstalled_NeverContactsFeed()
    {
        var service = new FakeUpdateService { IsInstalled = false, AvailableVersion = "0.0.2" };
        var vm = new UpdateViewModel(service);

        await vm.CheckInBackgroundAsync();

        Assert.Equal(0, service.CheckCount);
        Assert.False(vm.IsUpdateReady);
    }

    [Fact]
    public async Task ManualCheck_UpToDate_ReportsLatestVersion()
    {
        var service = new FakeUpdateService { CurrentVersion = "0.0.1" };
        var vm = new UpdateViewModel(service);

        await ExecuteAsync(vm.CheckForUpdatesCommand);

        Assert.Equal("You are running the latest version (v0.0.1).", vm.StatusText);
    }

    [Fact]
    public async Task ManualCheck_NewerVersion_ShowsReadyBadge()
    {
        var service = new FakeUpdateService { AvailableVersion = "0.1.0" };
        var vm = new UpdateViewModel(service);

        await ExecuteAsync(vm.CheckForUpdatesCommand);

        Assert.True(vm.IsUpdateReady);
        Assert.Null(vm.StatusText);
    }

    [Fact]
    public async Task ManualCheck_Failure_ReportsError()
    {
        var service = new FakeUpdateService { CheckException = new HttpRequestException("offline") };
        var vm = new UpdateViewModel(service);

        await ExecuteAsync(vm.CheckForUpdatesCommand);

        Assert.Equal("Update check failed: offline", vm.StatusText);
        Assert.False(vm.IsUpdateReady);
    }

    [Fact]
    public async Task ManualCheck_NotInstalled_ExplainsWhy()
    {
        var service = new FakeUpdateService { IsInstalled = false };
        var vm = new UpdateViewModel(service);

        await ExecuteAsync(vm.CheckForUpdatesCommand);

        Assert.Equal(0, service.CheckCount);
        Assert.Equal("Updates are only available in the installed version.", vm.StatusText);
    }

    [Fact]
    public async Task RestartToUpdate_AppliesDownloadedUpdate()
    {
        var service = new FakeUpdateService { AvailableVersion = "0.0.2" };
        var vm = new UpdateViewModel(service);
        await vm.CheckInBackgroundAsync();

        vm.RestartToUpdateCommand.Execute(null);

        Assert.Equal(1, service.ApplyCount);
    }

    [Fact]
    public void CurrentVersionText_PrefixesV()
    {
        var vm = new UpdateViewModel(new FakeUpdateService { CurrentVersion = "1.2.3" });
        Assert.Equal("v1.2.3", vm.CurrentVersionText);
    }

    [Fact]
    public void MainViewModel_ExposesUpdates_OnlyWhenServiceWired()
    {
        Assert.False(new MainViewModel(new FakeSimConnectService()).IsUpdateCheckAvailable);

        var withService = new MainViewModel(new FakeSimConnectService(), updateService: new FakeUpdateService());
        Assert.True(withService.IsUpdateCheckAvailable);
        Assert.NotNull(withService.Updates);
    }

    // AsyncRelayCommand.Execute is async void; with synchronously-completing
    // fakes it finishes inline, but yield once so any continuation settles.
    private static async Task ExecuteAsync(System.Windows.Input.ICommand command)
    {
        command.Execute(null);
        await Task.Yield();
    }
}
