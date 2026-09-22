using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AirportSmith.Models.Diagram;
using AirportSmith.Services;

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

    // Fired by a click on a parking spot marker — see
    // ParkingSpotShape_MouseLeftButtonDown. Same Edit-tab-only null-guard
    // convention as TaxiwayClickCommand.
    public static readonly DependencyProperty ParkingSpotClickCommandProperty =
        DependencyProperty.Register(nameof(ParkingSpotClickCommand), typeof(ICommand), typeof(AirportDiagramView));

    public ICommand? ParkingSpotClickCommand
    {
        get => (ICommand?)GetValue(ParkingSpotClickCommandProperty);
        set => SetValue(ParkingSpotClickCommandProperty, value);
    }

    // Same idea as VasiPlacementCommand, for a parking spot armed via the
    // Parking grid's Place button — tried alongside it in EndPan (at most one
    // of the two is ever armed, see MainViewModel.ArmParkingPlacement).
    public static readonly DependencyProperty ParkingPlacementCommandProperty =
        DependencyProperty.Register(nameof(ParkingPlacementCommand), typeof(ICommand), typeof(AirportDiagramView));

    public ICommand? ParkingPlacementCommand
    {
        get => (ICommand?)GetValue(ParkingPlacementCommandProperty);
        set => SetValue(ParkingPlacementCommandProperty, value);
    }

    // The map-tile fetch/cache boundary (see IMapTileService's doc comment) -
    // null on the Diagram/Edit tab's shared MainViewModel.MapTileService when
    // it wasn't constructed (never expected in practice, but kept nullable so
    // this view degrades to "no map" rather than throwing). Bound from
    // MainWindow.xaml alongside ShowMap below.
    public static readonly DependencyProperty MapTileServiceProperty =
        DependencyProperty.Register(nameof(MapTileService), typeof(IMapTileService), typeof(AirportDiagramView));

    public IMapTileService? MapTileService
    {
        get => (IMapTileService?)GetValue(MapTileServiceProperty);
        set => SetValue(MapTileServiceProperty, value);
    }

    // Off by default (see MainViewModel.ShowMap's own doc comment - a
    // session-only preference, opt-in since turning it on is this app's first
    // outbound network call). Toggling it immediately refreshes/clears the
    // tile layer rather than waiting for the next pan/zoom.
    public static readonly DependencyProperty ShowMapProperty =
        DependencyProperty.Register(nameof(ShowMap), typeof(bool), typeof(AirportDiagramView),
            new PropertyMetadata(false, OnShowMapChanged));

    public bool ShowMap
    {
        get => (bool)GetValue(ShowMapProperty);
        set => SetValue(ShowMapProperty, value);
    }

    private static void OnShowMapChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not AirportDiagramView view) return;

        if ((bool)e.NewValue)
        {
            _ = view.RefreshVisibleTilesAsync();
        }
        else
        {
            // Any in-flight fetch that completes after this just has its
            // result discarded on arrival (LoadTileAsync re-checks ShowMap) -
            // no cancellation plumbing for this phase, see
            // background-map-research.md's phase-0 scope.
            view._mapTiles.Clear();
        }
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

    // Currently-shown OSM tiles, bound to MapTiles.ItemsSource in code
    // (constructor) rather than XAML, since it's driven by ShowMap/
    // MapTileService rather than the AirportDiagram DataContext every other
    // ItemsControl in this view binds against.
    private readonly ObservableCollection<MapTileViewModel> _mapTiles = [];

    // Tile ids with a fetch already in flight, so a rapid succession of
    // RefreshVisibleTilesAsync calls (e.g. ShowMap toggling on right before a
    // debounced pan/zoom refresh fires) never starts a second concurrent
    // request for the same tile.
    private readonly HashSet<MapTileMath.TileId> _pendingTileRequests = [];

    // Coalesces the flood of pan (MouseMove) / zoom events into a single
    // visible-tile recompute + fetch pass once input goes idle, so dragging
    // across the diagram doesn't fire a tile request per pixel moved.
    private readonly DispatcherTimer _tileRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    public AirportDiagramView()
    {
        InitializeComponent();
        MapTiles.ItemsSource = _mapTiles;
        _tileRefreshTimer.Tick += (_, _) =>
        {
            _tileRefreshTimer.Stop();
            _ = RefreshVisibleTilesAsync();
        };

        Loaded += (_, _) => FitToView();
        // A resize re-fits only until the user takes over — after that their
        // chosen zoom/pan is preserved across window resizes.
        SizeChanged += (_, _) => { if (!_userAdjustedView) FitToView(); ScheduleTileRefresh(); };
        // A newly loaded airport starts fitted again — but re-fit ONLY when
        // it's actually a different airport, not just a new AirportDiagram
        // instance for the SAME one. MainViewModel.SetAirport re-runs
        // AirportDiagramProjector.Project (a fresh AirportDiagram, a new
        // DataContext reference) after any structural in-place edit too, not
        // just a genuine load — e.g. DeleteSelectedParkingSpotsCommand — and
        // without this check, deleting a single spot would reset the user's
        // zoom/pan back to fit-to-view every time, which is exactly the
        // "resets the whole view for a small edit" annoyance FitToView is
        // meant to avoid on every OTHER live edit (moving a spot, renaming a
        // taxiway, etc. mutate shapes in place with no DataContext change at
        // all). ReferenceLatitude/Longitude are copied straight from
        // AirportDetails.Latitude/Longitude and never change for the same
        // loaded airport, so an exact match is a reliable "same airport"
        // signal; a real airport's canvas can still shift/resize slightly
        // after a structural edit (e.g. deleting the spot that was defining
        // one edge of the bounding box), which may nudge the view a little
        // even with zoom/pan preserved — an acceptable tradeoff against
        // losing the zoom level entirely.
        DataContextChanged += (_, e) =>
        {
            var isSameAirport = e.OldValue is AirportDiagram oldDiagram && e.NewValue is AirportDiagram newDiagram
                && oldDiagram.ReferenceLatitude == newDiagram.ReferenceLatitude
                && oldDiagram.ReferenceLongitude == newDiagram.ReferenceLongitude;
            if (!isSameAirport) FitToView();

            // The old diagram's tiles are meaningless for a genuinely new
            // airport (different reference point/origin); even for the same
            // airport, the canvas origin may have shifted (see above), so
            // tile positions need recomputing either way — clear immediately
            // rather than waiting for the debounced refresh.
            _mapTiles.Clear();
            _pendingTileRequests.Clear();
            _ = RefreshVisibleTilesAsync();
        };
    }

    // Restarts the debounce timer — see _tileRefreshTimer's doc comment.
    private void ScheduleTileRefresh()
    {
        _tileRefreshTimer.Stop();
        _tileRefreshTimer.Start();
    }

    // Recomputes which OSM tiles are visible for the diagram's current
    // zoom/pan and ShowMap/MapTileService state, drops any shown tile that's
    // no longer visible, and asynchronously fetches/adds any newly-visible
    // one. Never blocks the UI thread — each fetch runs as its own
    // fire-and-forget task (LoadTileAsync), since MapTileService.GetTileAsync
    // may hit the network.
    private async Task RefreshVisibleTilesAsync()
    {
        if (!ShowMap || MapTileService is not { } tileService || DataContext is not AirportDiagram diagram)
        {
            _mapTiles.Clear();
            return;
        }
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        var scale = DiagramScale.ScaleX;
        if (scale <= 0) return;

        // screen = canvas * scale + translate (see the XAML's RenderTransform
        // comment), so canvas/diagram-space = (screen - translate) / scale -
        // the same untransformed meters space every diagram shape's own
        // Point2D lives in.
        var viewportTopLeft = new Point2D(-DiagramTranslate.X / scale, -DiagramTranslate.Y / scale);
        var viewportBottomRight = new Point2D(
            (ActualWidth - DiagramTranslate.X) / scale,
            (ActualHeight - DiagramTranslate.Y) / scale);

        var zoom = MapTileMath.SelectZoom(1.0 / scale, diagram.ReferenceLatitude);
        var visibleTiles = MapTileMath.GetVisibleTiles(
            viewportTopLeft, viewportBottomRight, zoom,
            diagram.ReferenceLatitude, diagram.ReferenceLongitude,
            diagram.OriginXMeters, diagram.OriginZMeters);
        var visibleIds = visibleTiles.Select(t => t).ToHashSet();

        for (var i = _mapTiles.Count - 1; i >= 0; i--)
        {
            if (!visibleIds.Contains(_mapTiles[i].Id)) _mapTiles.RemoveAt(i);
        }

        var alreadyShown = _mapTiles.Select(t => t.Id).ToHashSet();
        foreach (var tileId in visibleTiles)
        {
            if (alreadyShown.Contains(tileId) || !_pendingTileRequests.Add(tileId)) continue;
            _ = LoadTileAsync(tileId, diagram, tileService);
        }
    }

    private async Task LoadTileAsync(MapTileMath.TileId id, AirportDiagram diagram, IMapTileService tileService)
    {
        try
        {
            var bytes = await tileService.GetTileAsync(id.X, id.Y, id.Zoom, CancellationToken.None);
            // A tile that fails to fetch is simply not drawn - see
            // IMapTileSource's doc comment; no error surfaced to the user.
            if (bytes is null) return;

            // ShowMap may have been toggled off, or a different airport
            // loaded, while this fetch was in flight - discard a now-stale
            // result rather than adding it (see OnShowMapChanged's comment).
            if (!ShowMap || DataContext != diagram) return;

            var image = DecodeImage(bytes);
            if (image is null) return;

            var (topLeft, bottomRight) = MapTileMath.TileScreenRect(
                id.X, id.Y, id.Zoom, diagram.ReferenceLatitude, diagram.ReferenceLongitude,
                diagram.OriginXMeters, diagram.OriginZMeters);
            var width = bottomRight.X - topLeft.X;
            var height = bottomRight.Y - topLeft.Y;
            // Degenerate (e.g. a tile right at a pole-clamped latitude) -
            // draw nothing rather than risk Rect's constructor throwing on a
            // negative width/height.
            if (width <= 0 || height <= 0) return;

            // ImageBrush + an absolute-coordinate RectangleGeometry (see the
            // XAML template and MapTileViewModel's doc comment) rather than
            // Canvas.Left/Top + Width/Height on an <Image> - the latter hit
            // the same "every item stacks near canvas (0,0)" bug already
            // documented/fixed for the TaxiwayPoints template.
            var fill = new ImageBrush(image) { Stretch = Stretch.Fill };
            fill.Freeze();

            _mapTiles.Add(new MapTileViewModel { Id = id, Fill = fill, Rect = new Rect(topLeft.X, topLeft.Y, width, height) });
        }
        finally
        {
            _pendingTileRequests.Remove(id);
        }
    }

    // Null (never a thrown exception) for bytes that don't decode as an
    // image, so a corrupt/unexpected tile response is treated the same as a
    // failed fetch - just not drawn.
    private static BitmapImage? DecodeImage(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (NotSupportedException)
        {
            return null;
        }
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
        ScheduleTileRefresh();
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
        ScheduleTileRefresh();

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
        else if (ParkingPlacementCommand?.CanExecute(diagramPoint) == true)
            ParkingPlacementCommand.Execute(diagramPoint);
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

    // Wired on each parking spot marker's Path (see AirportDiagramView.xaml's
    // ParkingSpots template) — same pattern again, its own command/DTO since
    // parking spots are an independent selection too. While a placement is
    // armed the click is deliberately NOT consumed here, so it bubbles to the
    // root and lands as a background click that places the spot (placing it
    // right on top of another spot must still work).
    private void ParkingSpotShape_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ParkingSpotClickCommand is null) return;
        if (ParkingPlacementCommand?.CanExecute(new Point2D(0, 0)) == true) return;
        if (sender is not FrameworkElement { DataContext: ParkingSpotShape shape }) return;

        var request = new ParkingSpotSelectionRequest(shape, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        if (ParkingSpotClickCommand.CanExecute(request))
            ParkingSpotClickCommand.Execute(request);
        e.Handled = true;
    }
}
