using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AirportSmith.Models.Diagram;

namespace AirportSmith.Controls;

// Zoom (mouse wheel, anchored on the cursor) and pan (left-button drag) over
// the projected diagram. Mouse interaction is inherently view-layer behaviour,
// so it lives in code-behind here rather than in MainViewModel — the shapes
// themselves stay pure data from AirportDiagramProjector.
//
// Mouse handlers are wired on the UserControl root, not the inner Border,
// because panning captures the mouse on this control: while captured, input
// routes to the capture target, so handlers on a child would stop firing
// mid-drag.
public partial class AirportDiagramView : UserControl
{
    // Bound only by the Edit tab's instance of this control (the read-only
    // Diagram tab leaves it unset) — see TaxiwayShape_MouseLeftButtonDown for
    // why leaving it null preserves that tab's existing pan-everywhere
    // behavior unchanged.
    public static readonly DependencyProperty TaxiwayClickCommandProperty =
        DependencyProperty.Register(nameof(TaxiwayClickCommand), typeof(ICommand), typeof(AirportDiagramView));

    public ICommand? TaxiwayClickCommand
    {
        get => (ICommand?)GetValue(TaxiwayClickCommandProperty);
        set => SetValue(TaxiwayClickCommandProperty, value);
    }

    // Fired by a plain click (no drag) on empty diagram space — see EndPan.
    // Same Edit-tab-only null-guard convention as TaxiwayClickCommand.
    public static readonly DependencyProperty TaxiwayClearSelectionCommandProperty =
        DependencyProperty.Register(nameof(TaxiwayClearSelectionCommand), typeof(ICommand), typeof(AirportDiagramView));

    public ICommand? TaxiwayClearSelectionCommand
    {
        get => (ICommand?)GetValue(TaxiwayClearSelectionCommandProperty);
        set => SetValue(TaxiwayClearSelectionCommandProperty, value);
    }

    // Fired by a right-click anywhere on the diagram — see OnMouseRightButtonDown.
    // Same Edit-tab-only null-guard convention as TaxiwayClickCommand.
    public static readonly DependencyProperty TaxiwayContextMenuCommandProperty =
        DependencyProperty.Register(nameof(TaxiwayContextMenuCommand), typeof(ICommand), typeof(AirportDiagramView));

    public ICommand? TaxiwayContextMenuCommand
    {
        get => (ICommand?)GetValue(TaxiwayContextMenuCommandProperty);
        set => SetValue(TaxiwayContextMenuCommandProperty, value);
    }

    // Fired by a click on a taxiway point marker — see
    // TaxiwayPointShape_MouseLeftButtonDown. Same Edit-tab-only null-guard
    // convention as TaxiwayClickCommand; the read-only Diagram tab leaves
    // this unset, so a click there still just pans (same as clicking a
    // taxiway path there).
    public static readonly DependencyProperty TaxiwayPointClickCommandProperty =
        DependencyProperty.Register(nameof(TaxiwayPointClickCommand), typeof(ICommand), typeof(AirportDiagramView));

    public ICommand? TaxiwayPointClickCommand
    {
        get => (ICommand?)GetValue(TaxiwayPointClickCommandProperty);
        set => SetValue(TaxiwayPointClickCommandProperty, value);
    }

    // Fired by a plain click (no drag) on empty diagram space, same as
    // TaxiwayClearSelectionCommand — but only while MainViewModel has a VASI/
    // PAPI slot armed for click-to-place (its CanExecute reflects that), in
    // which case EndPan below tries this FIRST and only falls back to
    // clearing the taxiway selection when nothing is armed. CommandParameter
    // is the click position in the same untransformed canvas/diagram-space
    // meters as every shape's own Point2D (see OnMouseWheel's anchor for the
    // same GetPosition(DiagramCanvas) convention). Same Edit-tab-only
    // null-guard convention as TaxiwayClickCommand.
    public static readonly DependencyProperty VasiPlacementCommandProperty =
        DependencyProperty.Register(nameof(VasiPlacementCommand), typeof(ICommand), typeof(AirportDiagramView));

    public ICommand? VasiPlacementCommand
    {
        get => (ICommand?)GetValue(VasiPlacementCommandProperty);
        set => SetValue(VasiPlacementCommandProperty, value);
    }

    private const double ZoomStep = 1.15;

    // A mouse-down/mouse-up pair with less movement than this (device-
    // independent pixels) between them counts as a click rather than a
    // pan-drag, for the empty-space-click-clears-selection behavior below.
    private const double ClickMaxDragDistance = 3;

    // Zoom limits are relative to the fit-to-view scale rather than absolute,
    // so they behave the same for a small field and a large international
    // airport (canvas units are meters, so absolute limits wouldn't).
    private const double MinScaleFactor = 0.25;
    private const double MaxScaleFactor = 40;

    private double _fitScale = 1;
    private bool _userAdjustedView;
    private bool _isPanning;
    private Point _panStart;
    private double _panStartTranslateX;
    private double _panStartTranslateY;

    public AirportDiagramView()
    {
        InitializeComponent();

        Loaded += (_, _) => FitToView();
        // A resize re-fits only until the user takes over — after that their
        // chosen zoom/pan is preserved across window resizes.
        SizeChanged += (_, _) => { if (!_userAdjustedView) FitToView(); };
        // A newly loaded airport starts fitted again.
        DataContextChanged += (_, _) => FitToView();
    }

    private void FitToView()
    {
        if (DataContext is not AirportDiagram diagram) return;
        if (diagram.CanvasWidth <= 0 || diagram.CanvasHeight <= 0) return;
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        _fitScale = Math.Min(ActualWidth / diagram.CanvasWidth, ActualHeight / diagram.CanvasHeight);
        DiagramScale.ScaleX = DiagramScale.ScaleY = _fitScale;
        DiagramTranslate.X = (ActualWidth - diagram.CanvasWidth * _fitScale) / 2;
        DiagramTranslate.Y = (ActualHeight - diagram.CanvasHeight * _fitScale) / 2;
        _userAdjustedView = false;
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not AirportDiagram) return;

        var oldScale = DiagramScale.ScaleX;
        var target = e.Delta > 0 ? oldScale * ZoomStep : oldScale / ZoomStep;
        var newScale = Math.Clamp(target, _fitScale * MinScaleFactor, _fitScale * MaxScaleFactor);
        if (newScale == oldScale) return;

        // Keep whatever is under the cursor pinned there. GetPosition against
        // the canvas gives the point in untransformed canvas space, and
        // screen = canvas * scale + translate, so holding screen fixed while
        // scale changes means translate shifts by canvas * (oldScale - newScale).
        var anchor = e.GetPosition(DiagramCanvas);
        DiagramTranslate.X += anchor.X * (oldScale - newScale);
        DiagramTranslate.Y += anchor.Y * (oldScale - newScale);
        DiagramScale.ScaleX = DiagramScale.ScaleY = newScale;

        _userAdjustedView = true;
        e.Handled = true;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not AirportDiagram) return;

        _isPanning = true;
        _panStart = e.GetPosition(this);
        _panStartTranslateX = DiagramTranslate.X;
        _panStartTranslateY = DiagramTranslate.Y;
        Cursor = Cursors.SizeAll;
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;

        var current = e.GetPosition(this);
        DiagramTranslate.X = _panStartTranslateX + (current.X - _panStart.X);
        DiagramTranslate.Y = _panStartTranslateY + (current.Y - _panStart.Y);
        _userAdjustedView = true;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => EndPan(e);

    // Mouse-capture loss (e.g. alt-tab mid-drag) isn't a click, so no
    // MouseButtonEventArgs is available here to check drag distance —
    // deliberately passes null rather than treating capture loss as a click.
    private void OnLostMouseCapture(object sender, MouseEventArgs e) => EndPan(null);

    private void EndPan(MouseButtonEventArgs? e)
    {
        if (!_isPanning) return;

        // _isPanning is only ever set true by OnMouseLeftButtonDown on THIS
        // control (the root) — a mouse-down that instead landed on a taxiway
        // shape is marked Handled by TaxiwayShape_MouseLeftButtonDown before
        // it gets here (see that handler's own comment), so _isPanning stays
        // false and this whole method is a no-op for a taxiway click. So
        // reaching here with minimal movement means empty diagram space was
        // clicked, not dragged — clear the Edit tab's taxiway selection.
        var wasBackgroundClick = e != null && (e.GetPosition(this) - _panStart).Length <= ClickMaxDragDistance;

        _isPanning = false;
        Cursor = Cursors.Arrow;
        if (IsMouseCaptured) ReleaseMouseCapture();

        if (!wasBackgroundClick) return;

        // While a VASI/PAPI slot is armed for click-to-place, a background
        // click places it there instead of clearing the taxiway selection —
        // VasiPlacementCommand's CanExecute is false whenever nothing is
        // armed, so this falls through to the ordinary clear-selection
        // behavior the rest of the time.
        var clickPoint = e!.GetPosition(DiagramCanvas);
        var diagramPoint = new Point2D(clickPoint.X, clickPoint.Y);
        if (VasiPlacementCommand?.CanExecute(diagramPoint) == true)
            VasiPlacementCommand.Execute(diagramPoint);
        else if (TaxiwayClearSelectionCommand?.CanExecute(null) == true)
            TaxiwayClearSelectionCommand.Execute(null);
    }

    // Right-click opens the Edit tab's batch-edit popover for whatever's
    // currently selected — it never changes the selection itself, regardless
    // of what's directly under the cursor (a taxiway or empty space), so no
    // per-shape wiring is needed the way TaxiwayShape_MouseLeftButtonDown
    // needs for left-click; this root-level handler covers the whole canvas.
    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (TaxiwayContextMenuCommand?.CanExecute(null) == true)
            TaxiwayContextMenuCommand.Execute(null);
        e.Handled = true;
    }

    // Wired on the taxiway pavement-band Polygon in the DataTemplate (the
    // wider hit target — the centerline Line is a friendlier visual but too
    // thin to click reliably). Only marks the event Handled — suppressing the
    // root's pan-start above, since this fires first during bubbling — when
    // TaxiwayClickCommand is actually bound, so the read-only Diagram tab
    // (which never binds it) keeps its current behavior of panning even when
    // the drag starts on top of a taxiway.
    private void TaxiwayShape_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (TaxiwayClickCommand is null) return;
        if (sender is not FrameworkElement { DataContext: TaxiwaySegmentShape shape }) return;

        var request = new TaxiwaySelectionRequest(shape, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        if (TaxiwayClickCommand.CanExecute(request))
            TaxiwayClickCommand.Execute(request);
        e.Handled = true;
    }

    // Wired on each taxiway point marker's Path (see AirportDiagramView.xaml's
    // TaxiwayPoints template) — same pattern as TaxiwayShape_MouseLeftButtonDown
    // above, a separate command/DTO since points and taxi paths are
    // independent selections (see MainViewModel.ToggleTaxiwayPointSelection).
    private void TaxiwayPointShape_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (TaxiwayPointClickCommand is null) return;
        if (sender is not FrameworkElement { DataContext: TaxiwayPointShape shape }) return;

        var request = new TaxiwayPointSelectionRequest(shape, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        if (TaxiwayPointClickCommand.CanExecute(request))
            TaxiwayPointClickCommand.Execute(request);
        e.Handled = true;
    }
}
