using System.Windows;
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
