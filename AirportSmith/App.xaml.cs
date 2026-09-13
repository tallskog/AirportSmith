using System.Windows;
using AirportSmith.Services;
using AirportSmith.ViewModels;

namespace AirportSmith;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var simConnect = new SimConnectService();

        // Debug-Data export/import is a dev-mode-only debugging aid — never
        // wired up in Release builds.
        IDebugDataStore? debugDataStore = null;
        IFileDialogService? fileDialogService = null;
#if DEBUG
        debugDataStore = new DebugDataStore();
        fileDialogService = new FileDialogService();
#endif

        // Unlike the Debug-only services above, project save/load is a real
        // user-facing feature — always wired up.
        var projectStore = new AirportProjectStore();

        var viewModel = new MainViewModel(simConnect, debugDataStore, fileDialogService, projectStore);
        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();
    }
}
