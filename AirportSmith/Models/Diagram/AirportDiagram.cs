namespace AirportSmith.Models.Diagram;

// A point already in final canvas/screen-space meters (X right, Y down,
// origin at the diagram's top-left) — NOT raw lat/lon or raw BIAS_X/BIAS_Z.
// Kept as a plain struct (not System.Windows.Point) so AirportDiagramProjector
// has zero WPF dependency and is testable as plain C#.
public readonly record struct Point2D(double X, double Y);

// A displaced threshold's markings, per FAA AIM 2-3-3 (faa.gov/air_traffic/
// publications/atpubs/aim/aim0203.html — see requirements.md's reference
// list): WHITE throughout, since this pavement is still usable runway (just
// not for landing) — ZoneCorners is the overlay spanning from the physical
// runway end inward to the threshold bar; ThresholdBar is the solid 10ft-wide
// bar AT the displaced threshold itself; the arrow (shaft + head) runs along
// the centerline pointing at that bar. Simplified vs. the real marking (AIM
// shows arrowheads repeated across the runway width; this draws one).
public record RunwayThresholdMarkingShape(
    IReadOnlyList<Point2D> ZoneCorners,
    IReadOnlyList<Point2D> ThresholdBar,
    Point2D ArrowShaftStart,
    Point2D ArrowShaftEnd,
    IReadOnlyList<Point2D> ArrowHead);

// A blast pad or overrun/stopway's markings, per FAA AIM 2-3-3: YELLOW
// throughout, since this pavement is explicitly NOT usable for landing,
// takeoff, or taxiing — Corners is the extension's own footprint (outside the
// runway); DemarcationBar is the solid 3ft-wide bar at the boundary with the
// runway; Chevrons are the yellow "unusable pavement" markings within it. A
// fixed, capped chevron count is a diagram-level simplification, not a
// literal reproduction of AIM figure spacing.
public record RunwayPavementExtensionShape(
    IReadOnlyList<Point2D> Corners,
    IReadOnlyList<Point2D> DemarcationBar,
    IReadOnlyList<IReadOnlyList<Point2D>> Chevrons);

// An approach lighting system at one runway end, from Runway.PrimaryApproachLights/
// SecondaryApproachLights, per FAA AIM 2-1-3 (see requirements.md's
// reference list) — a schematic simplification, not a literal light-by-light
// reproduction: RailLights is a row of evenly-spaced dots extending outward
// from the threshold along the extended centerline, length/spacing bucketed
// by the system's category (see AirportDiagramProjector.Categorize) rather
// than each of the SDK's 14 system types' exact real-world layout. CrossBar
// is empty unless the category has one (ALSF-1/ALSF-2's defining red
// side-row barrettes / decision bar ~1000ft out), in which case it holds
// exactly the bar's two endpoints — same empty-list-means-absent convention
// as RunwayPavementExtensionShape.Chevrons.
public record ApproachLightSystemShape(
    IReadOnlyList<Point2D> RailLights,
    IReadOnlyList<Point2D> CrossBar);

// One end's optional pavement extras, from Runway.PrimaryThreshold/BlastPad/
// Overrun (or the Secondary equivalents) — each null when that
// RunwayPavementFeature wasn't present (ENABLE was 0). ThresholdMarking sits
// WITHIN the runway's own Corners; BlastPad/Overrun/ApproachLights extend
// OUTWARD, beyond the runway's edge.
public record RunwayEndFeatures(
    RunwayThresholdMarkingShape? ThresholdMarking,
    RunwayPavementExtensionShape? BlastPad,
    RunwayPavementExtensionShape? Overrun,
    ApproachLightSystemShape? ApproachLights);

// PrimaryLabelPosition/SecondaryLabelPosition are each threshold nudged
// inward along the runway centerline, so the designation text sits visibly
// on the pavement rather than exactly on the runway's edge.
public record RunwayShape(
    string PrimaryDesignation,
    string SecondaryDesignation,
    Point2D Threshold1,
    Point2D Threshold2,
    IReadOnlyList<Point2D> Corners,
    Point2D PrimaryLabelPosition,
    Point2D SecondaryLabelPosition,
    RunwayEndFeatures PrimaryFeatures,
    RunwayEndFeatures SecondaryFeatures);

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
