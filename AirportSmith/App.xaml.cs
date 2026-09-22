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
#if DEBUG
        debugDataStore = new DebugDataStore();
#endif

        // FileDialogService used to be constructed only alongside the
        // Debug-only debugDataStore above (its only caller at the time), but
        // Export Airport XML below is a real, always-on feature that also
        // needs a save-file dialog — always wired up now, not Debug-only.
        IFileDialogService fileDialogService = new FileDialogService();

        // Unlike debugDataStore above, project save/load and XML export are
        // real user-facing features — always wired up.
        var projectStore = new AirportProjectStore();
        var xmlExporter = new AirportXmlExporter();

        // The map tile service is cheap/inert until MainViewModel.ShowMap is
        // toggled on — constructing it here does not itself make a network
        // call. SPIKE-ONLY endpoint/User-Agent — see OsmMapTileSource's own
        // doc comment for why this must be revisited before the app is
        // distributed beyond the author's own manual testing.
        IMapTileSource mapTileSource = new OsmMapTileSource(userAgent: "AirportSmith/0.1 (dev build)");
        IMapTileCache mapTileCache = new MapTileDiskCache();
        IMapTileService mapTileService = new MapTileService(mapTileSource, mapTileCache);

        // Real, always-on feature (e.g. confirming a parking-spot delete) —
        // always wired up, same as projectStore/xmlExporter above.
        IConfirmationService confirmationService = new ConfirmationService();

        var viewModel = new MainViewModel(simConnect, debugDataStore, fileDialogService, projectStore, xmlExporter, mapTileService, confirmationService);
        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();
    }
}
