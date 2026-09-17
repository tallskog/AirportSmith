using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using AirportSmith.ViewModels;

namespace AirportSmith;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    // WPF's DataGrid has no bindable SelectedItems, so a code-behind handler
    // is the pragmatic way to forward multi-row selection to the ViewModel —
    // see MainViewModel.SyncTaxiwaySelectionFromRows for what it does with it.
    //
    // TaxiPathsGrid.ItemsSource is bound to MainViewModel.VisibleTaxiPathEdits,
    // which a diagram click/clear filters (see RefreshTaxiwayFilter). Swapping
    // a DataGrid's ItemsSource makes WPF clear its own selection and raise
    // SelectionChanged as a side effect — and not just once: instrumenting
    // this showed a short burst of several SelectionChanged events (all
    // reporting the grid's now-empty selection) within a couple of
    // milliseconds of the swap, on a later Dispatcher pass rather than
    // inline with the swap itself. Left unguarded, those events reached
    // SyncTaxiwaySelectionFromRows with an empty selection and immediately
    // stamped every diagram shape's IsSelected back to false — undoing the
    // very selection that triggered the filter change, which is why
    // clicking a taxiway correctly filtered the grid but never visibly
    // highlighted anything, Ctrl+click multi-select never stuck, and
    // right-click had no selection left to open a popover for. Tracking the
    // ItemsSource reference alone (skip only the first event after it
    // changes) caught the first event in the burst but not the rest, so
    // this also debounces for a short settle window after the reference
    // changes — long enough to absorb the whole burst (observed within ~2ms)
    // but short enough that a genuine later user action (a real click is
    // never sub-300ms after an unrelated diagram click) still goes through.
    private object? _lastTaxiPathsItemsSource;
    private DateTime _lastTaxiPathsItemsSourceChangedAt;
    private static readonly TimeSpan TaxiPathsItemsSourceSettleWindow = TimeSpan.FromMilliseconds(300);

    // DataGridColumn.Header content doesn't reliably participate in the
    // normal visual-tree RelativeSource walk the way a plain sibling control
    // does — confirmed directly: neither RelativeSource AncestorType=Window
    // nor routing through the DataGrid's own Tag (RelativeSource
    // AncestorType=DataGrid) let a header-hosted TwoWay-bound TextBox push
    // its edits back to TaxiPathFilter, even though the exact same binding
    // pattern on an ordinary (non-header) control elsewhere in this window
    // works fine. Explicitly setting each filter header's own DataContext
    // here — once, when it loads — sidesteps whatever that RelativeSource
    // limitation is; every filter control in a header can then use a plain
    // one-level {Binding PropertyName}, same as a DataGrid cell template
    // binds directly to its row item.
    private void TaxiPathFilterHeader_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
            element.DataContext = _viewModel.TaxiPathFilter;
    }

    private void TaxiPathsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid grid) return;

        if (!ReferenceEquals(grid.ItemsSource, _lastTaxiPathsItemsSource))
        {
            _lastTaxiPathsItemsSource = grid.ItemsSource;
            _lastTaxiPathsItemsSourceChangedAt = DateTime.UtcNow;
            return;
        }

        if (DateTime.UtcNow - _lastTaxiPathsItemsSourceChangedAt < TaxiPathsItemsSourceSettleWindow)
            return;

        _viewModel.SyncTaxiwaySelectionFromRows(grid.SelectedItems.Cast<TaxiPathEditViewModel>());
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Captures the window handle and hooks the SimConnect message pump.
        // Allowed to silently stay disconnected if MSFS isn't running yet — no
        // background reconnect timer; the Load button retries on every click.
        _viewModel.SimConnect.Connect(new WindowInteropHelper(this).Handle);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.SimConnect.Disconnect();
        base.OnClosed(e);
    }
}
