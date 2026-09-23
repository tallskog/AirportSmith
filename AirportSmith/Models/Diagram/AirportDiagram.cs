using System.ComponentModel;
using System.Runtime.CompilerServices;
using AirportSmith.Models;

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
// on the pavement rather than exactly on the runway's edge. SourceIndex is
// this shape's index into AirportDetails.Runways, and IsVisible backs the
// Edit tab's per-row "hide from diagram" toggle — same rationale as
// TaxiwaySegmentShape's own SourceIndex/IsVisible (see its doc comment):
// mutable/observable so MainViewModel can toggle it live without
// re-projecting the whole diagram.
public class RunwayShape : INotifyPropertyChanged
{
    public required string PrimaryDesignation { get; init; }
    public required string SecondaryDesignation { get; init; }
    public required Point2D Threshold1 { get; init; }
    public required Point2D Threshold2 { get; init; }
    public required IReadOnlyList<Point2D> Corners { get; init; }
    public required Point2D PrimaryLabelPosition { get; init; }
    public required Point2D SecondaryLabelPosition { get; init; }

    // Mutable (unlike every other geometry field above, which stays fixed
    // once projected) so MainViewModel can swap in a recomputed
    // ApproachLights after an Edit tab SystemType change — via a `with`
    // expression on the immutable RunwayEndFeatures record itself, e.g.
    // `PrimaryFeatures = PrimaryFeatures with { ApproachLights = updated }`
    // — without re-running the whole projection (would reset zoom/pan/
    // selection, same rationale as every other live-updatable shape field in
    // this file). ThresholdMarking/BlastPad/Overrun ride along unchanged
    // inside the same record since nothing in the Edit tab can change them
    // yet; only ApproachLights currently needs this.
    private RunwayEndFeatures _primaryFeatures = null!;
    public required RunwayEndFeatures PrimaryFeatures
    {
        get => _primaryFeatures;
        set
        {
            if (_primaryFeatures == value) return;
            _primaryFeatures = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PrimaryFeatures)));
        }
    }

    private RunwayEndFeatures _secondaryFeatures = null!;
    public required RunwayEndFeatures SecondaryFeatures
    {
        get => _secondaryFeatures;
        set
        {
            if (_secondaryFeatures == value) return;
            _secondaryFeatures = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SecondaryFeatures)));
        }
    }

    public required int SourceIndex { get; init; }

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

// WidthCorners is the taxiway's physical pavement footprint (a band, sized
// from TaxiPathSegment.WidthMeters) — Start/End remain the bare centerline,
// unchanged, for the existing named/unnamed line styling. MidPoint is where
// a name label is placed for named segments. SourceIndex is this shape's
// index into the source AirportDetails.TaxiPaths — needed so the Edit tab's
// diagram can map a clicked shape back to its TaxiPathEditViewModel (the
// projector skips non-taxiway/unresolved segments, so this can't be
// recovered from TaxiwaySegments' own list position). Unlike every other
// diagram shape, this one is a mutable class (not a record): HasName/Name are
// still set once by the projector at load time but stay externally settable
// so MainViewModel can push a live update straight onto the shape when the
// user renames a taxi path (or the taxi name it points at) — re-running the
// whole projection on every edit would also reset zoom/pan/selection, which
// nothing else in the Edit tab does. IsSelected is the Edit tab's
// click-to-select highlight; IsVisible backs its per-row "hide from diagram"
// toggle. AirportDiagramView's bindings/DataTriggers need PropertyChanged to
// react to all of these.
public class TaxiwaySegmentShape : INotifyPropertyChanged
{
    // Never changes after projection — Start is always a taxi point, and the
    // Edit tab has no way to move one (TaxiwayPointEditViewModel only exposes
    // Type/Orientation, not X/Z), so unlike End/MidPoint/WidthCorners below
    // it doesn't need a live-updatable setter.
    public required Point2D Start { get; init; }

    // End/MidPoint/WidthCorners are mutable (unlike Start/SourceIndex/
    // IsRunwayType/IsParkingType) so a Parking-type segment's line can track
    // its linked parking spot live when the spot moves — see
    // AirportDiagramProjector.ComputeTaxiwaySegmentPlacement and
    // MainViewModel.RefreshParkingShape, which calls it. Before this existed,
    // moving a spot updated its own ParkingSpotShape dot but left this
    // segment's line stranded at the spot's old position — invisible while
    // Parking-type paths weren't drawn at all, but a visible desync once they
    // were (see IsParkingType's own doc comment).
    private Point2D _end;
    public required Point2D End { get => _end; set => SetField(ref _end, value); }

    private IReadOnlyList<Point2D> _widthCorners = [];
    public required IReadOnlyList<Point2D> WidthCorners { get => _widthCorners; set => SetField(ref _widthCorners, value); }

    private Point2D _midPoint;
    public required Point2D MidPoint { get => _midPoint; set => SetField(ref _midPoint, value); }

    public required int SourceIndex { get; init; }

    // True for a TaxiPathType.Runway segment — these render with a distinct
    // style (see AirportDiagramView's TaxiwaySegments template) rather than
    // the ordinary named/unnamed blue/gray line, so they read as "this is
    // the taxi path onto/off a runway, not an ordinary taxiway" at a glance.
    // Included here specifically so a point ONLY reachable via a Runway-type
    // path — e.g. a runway entrance/exit stub — still shows as visibly
    // connected instead of looking like an orphaned dot next to the
    // TaxiwayPoints red marker, which is exactly how a real OIBK data
    // anomaly (points 0 and 12 only linked via Runway-type paths) went
    // unnoticed in the diagram despite the Taxi Paths grid already showing
    // the connecting row.
    public required bool IsRunwayType { get; init; }

    // True for a TaxiPathType.Parking segment — the short stub connecting a
    // taxiway point to a parking stand. These were entirely excluded from
    // TaxiwaySegments until a user reported the resulting gap: the parking
    // spot's own orange dot (ParkingSpotShape) rendered fine, but the line
    // leading into it didn't, even though its End resolves to a real point
    // (SimConnectService.ResolveTaxiPathPoints sets it to the parking spot's
    // own BiasX/BiasZ — see AirportDiagramProjector's inclusion comment for
    // the full history). Styled distinctly (see AirportDiagramView's
    // TaxiwaySegments template) rather than the ordinary named/unnamed
    // blue/gray line, using a color that matches the parking spot dots so it
    // reads as "this is a stand's own lead-in," not an ordinary taxiway.
    public required bool IsParkingType { get; init; }

    private bool _hasName;
    public required bool HasName
    {
        get => _hasName;
        set => SetField(ref _hasName, value);
    }

    private string _name = string.Empty;
    public required string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set => SetField(ref _isVisible, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // Deliberately not reusing AirportSmith.ViewModels.ViewModelBase's
    // identical helper — Models shouldn't depend on the ViewModels layer.
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

// Raised by AirportDiagramView when the user clicks a taxiway shape (see its
// TaxiwayClickCommand) and consumed by MainViewModel.ToggleTaxiwaySelectionCommand.
// ExtendSelection is true for a Ctrl+click (toggle this shape's membership in
// the current multi-selection) and false for a plain click (replace the
// selection with just this shape) — the modifier-key check itself is a
// WPF/view concern and stays in AirportDiagramView's code-behind; this record
// is a plain DTO so the ViewModel layer doesn't need to know about
// System.Windows.Input.
public sealed record TaxiwaySelectionRequest(TaxiwaySegmentShape Shape, bool ExtendSelection);

// One TAXI_PARKING spot. SourceIndex is this shape's index into
// AirportDetails.ParkingSpots (which is also its row's position in
// MainViewModel.ParkingSpotEdits — both are built 1:1 in the same order), so a
// clicked shape maps straight back to its Edit tab row. A mutable class (not a
// record) for the same reason as TaxiwaySegmentShape/VasiShape: Center/
// HeadingTip/RadiusMeters/Label change live when the Edit tab's grid fields
// change or a diagram click places the spot, and IsSelected/IsVisible back the
// click-to-select highlight and the "hide all" toggle — none of which should
// need to re-run the whole projection (that would reset zoom/pan/selection).
// Label is the spot's user-facing Number.
public class ParkingSpotShape : INotifyPropertyChanged
{
    public required int SourceIndex { get; init; }

    private Point2D _center;
    public required Point2D Center { get => _center; set => SetField(ref _center, value); }

    private double _radiusMeters;
    public required double RadiusMeters { get => _radiusMeters; set => SetField(ref _radiusMeters, value); }

    private Point2D _headingTip;
    public required Point2D HeadingTip { get => _headingTip; set => SetField(ref _headingTip, value); }

    private string _label = string.Empty;
    public string Label { get => _label; set => SetField(ref _label, value); }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }

    private bool _isVisible = true;
    public bool IsVisible { get => _isVisible; set => SetField(ref _isVisible, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

// Same idea as TaxiwayPointSelectionRequest, for a click on a ParkingSpotShape.
public sealed record ParkingSpotSelectionRequest(ParkingSpotShape Shape, bool ExtendSelection);

// One distinct sim TAXI_POINT index, resolved from the airport's taxi paths —
// the same distinct-index synthesis AirportXmlExporter.BuildTaxiwayPoints
// uses to build <TaxiwayPoint> elements (every path's Start, plus its End
// unless the path is Type==Parking, whose End references a TaxiwayParking
// item rather than a taxi point), reused here so the diagram shows exactly
// the points that would actually export. Index is the point's original sim
// TAXI_POINT index — matches the Edit tab's Taxiway Points grid rows
// (TaxiwayPointEditViewModel.Index) and the exported
// <TaxiwayPoint index="...">; since both this list and that one are built
// from the same distinct-index dedup in the same ascending order, matching a
// clicked shape back to its grid row is done by Index directly rather than a
// TaxiwaySegmentShape-style SourceIndex/list-position. A mutable class (not a
// record) for the same reason as TaxiwaySegmentShape: IsSelected/IsVisible
// need to change live without re-running the whole projection (which would
// reset zoom/pan/selection).
public class TaxiwayPointShape : INotifyPropertyChanged
{
    public required Point2D Center { get; init; }
    public required int Index { get; init; }

    // True when this index's resolved TaxiPointType (from whichever
    // TaxiPathSegment.StartPointType/EndPointType referenced it first — see
    // AirportDiagramProjector.Project) is one of the four hold-short variants
    // rather than Normal/unresolved, so the diagram can render it in a
    // distinct color instead of looking identical to an ordinary taxi point.
    public required bool IsHoldShort { get; init; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set => SetField(ref _isVisible, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

// Same idea as TaxiwaySelectionRequest, for a click on a TaxiwayPointShape —
// see that record's own doc comment for ExtendSelection's meaning.
public sealed record TaxiwayPointSelectionRequest(TaxiwayPointShape Shape, bool ExtendSelection);

// One of a runway's four VASI/PAPI slots (VasiSlot.PrimaryLeft/Right,
// SecondaryLeft/Right) — unlike TaxiwaySegments/TaxiwayPoints (which only
// exist for what the airport actually has), AirportDiagramProjector.Project
// always creates exactly one VasiShape per slot per runway, and IsInstalled
// (that slot's Runway.*VasiType != null) drives whether it's drawn. That way
// enabling a VASI purely through the Edit tab's Type picker (no reload) just
// flips an existing shape visible instead of needing one created on the fly
// — the same "pre-create, then mutate" approach TaxiwaySegmentShape/
// TaxiwayPointShape use for their own live-without-re-projecting updates.
// Position/WingBarStart/WingBarEnd are screen-space (via
// AirportDiagramProjector.ComputeVasiPlacement, which converts the slot's
// runway-relative Bias X/Z into this same screen space using
// AirportDiagram's OriginXMeters/OriginZMeters below) and mutable so
// MainViewModel can push a recomputed position straight onto the shape
// whenever the Edit tab's Bias X/Z/Spacing fields change, or a diagram click
// sets them via click-to-place — re-running the whole projection on every
// edit would reset zoom/pan/selection, same rationale as every other
// mutable shape in this file.
public class VasiShape : INotifyPropertyChanged
{
    public required int SourceRunwayIndex { get; init; }
    public required VasiSlot Slot { get; init; }

    private Point2D _position;
    public Point2D Position { get => _position; set => SetField(ref _position, value); }

    // Endpoints of a short bar drawn perpendicular to the runway centerline
    // through Position, sized from SpacingMeters — a diagram-level schematic
    // (not a literal per-light-unit reproduction of PAPI's 2/4 lights or
    // VASI's near/far bars), same simplification precedent as
    // ApproachLightSystemShape's bucketed rail.
    private Point2D _wingBarStart;
    public Point2D WingBarStart { get => _wingBarStart; set => SetField(ref _wingBarStart, value); }

    private Point2D _wingBarEnd;
    public Point2D WingBarEnd { get => _wingBarEnd; set => SetField(ref _wingBarEnd, value); }

    private bool _isInstalled;
    public bool IsInstalled { get => _isInstalled; set => SetField(ref _isInstalled, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

public class AirportDiagram
{
    public double CanvasWidth { get; init; }
    public double CanvasHeight { get; init; }
    public List<RunwayShape> Runways { get; init; } = [];
    public List<TaxiwaySegmentShape> TaxiwaySegments { get; init; } = [];
    public List<ParkingSpotShape> ParkingSpots { get; init; } = [];
    public List<TaxiwayPointShape> TaxiwayPoints { get; init; } = [];
    public List<VasiShape> VasiLights { get; init; } = [];

    // Local-meters -> screen-space offsets captured from this projection's
    // own bounds (see AirportDiagramProjector.Project's ToScreen local
    // function: screenX = localX - minX + margin, screenY = maxZ - localZ +
    // margin — OriginXMeters/OriginZMeters below are just margin-minX and
    // maxZ+margin, so screenX = localX + OriginXMeters and
    // screenY = OriginZMeters - localZ). Needed so
    // AirportDiagramProjector.ComputeVasiPlacement/ComputeVasiBias can
    // convert a VASI/PAPI's runway-relative Bias X/Z into/out of this same
    // screen space AFTER the initial projection (a live Edit tab field
    // change, or a diagram click while placing), without re-running the
    // whole projection.
    public double OriginXMeters { get; init; }
    public double OriginZMeters { get; init; }

    // The airport's own reference point (AirportDetails.Latitude/Longitude) —
    // the same (lat, lon) GeoProjection.ProjectLatLon/UnprojectLocalPoint use
    // as their reference throughout this diagram. Needed to place a map tile
    // layer (see MapTileMath) in the same local-meters plane as every other
    // shape here.
    public double ReferenceLatitude { get; init; }
    public double ReferenceLongitude { get; init; }
}
