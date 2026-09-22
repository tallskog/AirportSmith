using System.Windows;
using System.Windows.Media;
using AirportSmith.Services;

namespace AirportSmith.Controls;

// One decoded, screen-positioned OSM tile - what AirportDiagramView's MapTiles
// ItemsControl actually binds to, built from MapTileMath.TileScreenRect once
// a tile's bytes have been fetched and decoded. Id is kept so
// AirportDiagramView can diff "currently shown" against "currently visible"
// without re-decoding anything.
//
// Positioned via an absolute-coordinate RectangleGeometry (Fill/Rect below),
// NOT Canvas.Left/Top on the template root - see AirportDiagramView.xaml's
// TaxiwayPoints template comment: that attached-property-on-template-root
// approach is confirmed broken for at least one ItemsControl in this view
// (every item rendering stacked near canvas (0,0) instead of its own
// position, root cause not identified) and MapTiles hit the exact same
// symptom (all tiles overlapping near the canvas origin). The
// absolute-coordinate-geometry workaround already used for TaxiwayPoints/
// ParkingSpots/VasiLights sidesteps it here too.
internal class MapTileViewModel
{
    public required MapTileMath.TileId Id { get; init; }
    public required Brush Fill { get; init; }
    public required Rect Rect { get; init; }
}
