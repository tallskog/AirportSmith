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
    private void TaxiPathsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid grid)
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
