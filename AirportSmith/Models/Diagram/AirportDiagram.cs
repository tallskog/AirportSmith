namespace AirportSmith.Models.Diagram;

// A point already in final canvas/screen-space meters (X right, Y down,
// origin at the diagram's top-left) — NOT raw lat/lon or raw BIAS_X/BIAS_Z.
// Kept as a plain struct (not System.Windows.Point) so AirportDiagramProjector
// has zero WPF dependency and is testable as plain C#.
public readonly record struct Point2D(double X, double Y);

public record RunwayShape(
    string PrimaryDesignation,
    string SecondaryDesignation,
    Point2D Threshold1,
    Point2D Threshold2,
    IReadOnlyList<Point2D> Corners);

// WidthCorners is the taxiway's physical pavement footprint (a band, sized
// from TaxiPathSegment.WidthMeters) — Start/End remain the bare centerline,
// unchanged, for the existing named/unnamed line styling. MidPoint is where
// a name label is placed for named segments.
public record TaxiwaySegmentShape(
    Point2D Start,
    Point2D End,
    bool HasName,
    string Name,
    IReadOnlyList<Point2D> WidthCorners,
    Point2D MidPoint);

public record ParkingSpotShape(Point2D Center, double RadiusMeters, Point2D HeadingTip);

public class AirportDiagram
{
    public double CanvasWidth { get; init; }
    public double CanvasHeight { get; init; }
    public List<RunwayShape> Runways { get; init; } = [];
    public List<TaxiwaySegmentShape> TaxiwaySegments { get; init; } = [];
    public List<ParkingSpotShape> ParkingSpots { get; init; } = [];
}
