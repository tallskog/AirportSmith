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
    private const double ZoomStep = 1.15;

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

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => EndPan();

    private void OnLostMouseCapture(object sender, MouseEventArgs e) => EndPan();

    private void EndPan()
    {
        if (!_isPanning) return;

        _isPanning = false;
        Cursor = Cursors.Arrow;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }
}
