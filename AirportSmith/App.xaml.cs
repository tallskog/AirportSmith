using System.Globalization;
using System.Windows;
using AirportSmith.Services;
using AirportSmith.ViewModels;
using Velopack;

namespace AirportSmith;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Must run before anything else so Velopack can handle its
        // install/update/uninstall hooks (it may exit the process for those).
        // A no-op when not running as a Velopack-installed app (dev builds).
        VelopackApp.Build().Run();

        // Forces every implicit WPF Binding string<->double conversion
        // (every numeric Edit tab grid column: VASI angle/X/Z/spacing,
        // parking spot heading/radius/X/Z, ...) to always accept "." as the
        // decimal separator, regardless of the machine's regional settings.
        // Fixes a real reported bug: on a comma-decimal Windows locale,
        // typing "2.9" into e.g. a VASI angle cell silently never got past
        // "2" — the "." key doesn't parse as a comma-locale decimal
        // separator, so only whole numbers were ever enterable.
        //
        // A Binding without an explicit ConverterCulture falls back to
        // CultureInfo.CurrentCulture (the UI thread's culture) for this —
        // NOT FrameworkElement.Language, despite that being the more
        // commonly suggested fix. An earlier attempt at this bug set
        // Window.Language="en-US" on MainWindow; live testing confirmed
        // that had NO effect on the actual typing behavior, so it was
        // removed rather than left as misleading dead documentation.
        // CultureInfo.CurrentCulture's setter (unlike
        // DefaultThreadCurrentCulture, which only seeds threads created
        // AFTER this point) directly repoints Thread.CurrentThread's
        // culture — the thread OnStartup and every WPF binding actually run
        // on — so it takes effect immediately, before MainWindow is even
        // constructed below. DefaultThreadCurrentCulture is set too, purely
        // for any future background/ThreadPool work this app might add.
        //
        // Matches this app's existing "always invariant/period, regardless
        // of the machine's locale" convention (AirportDataTreeBuilder's own
        // number formatting, the XML exporter's output) — this is what
        // makes TYPED grid input consistent with those, not just
        // displayed/exported numbers.
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

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

        // Detects newly published GitHub releases (see UpdateViewModel).
        IUpdateService updateService = new VelopackUpdateService();

        var viewModel = new MainViewModel(simConnect, debugDataStore, fileDialogService, projectStore, xmlExporter, mapTileService, confirmationService, updateService);
        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();

        _ = viewModel.Updates!.CheckInBackgroundAsync();
    }
}
