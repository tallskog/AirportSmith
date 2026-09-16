namespace AirportSmith.Models;

// A raw TAXI_PATH row. Start/End reference indices into the sim's internal
// TAXI_POINT list for the airport, resolved to local-meters offsets in
// SimConnectService.ResolveTaxiPathPoints once all TAXI_POINT rows for the
// request have arrived (order isn't guaranteed, same reason Name is resolved
// separately below). Null Start*/End* coordinates mean the index didn't
// resolve against any TAXI_POINT row returned for this airport.
//
// TAXI_POINT.BIAS_X/BIAS_Z's units and axis convention (which axis is which,
// positive direction, meters vs. feet) are UNCONFIRMED against a live sim —
// implemented per the SDK's documented field order, not yet verified.
//
// TAXI_PATH itself has no free-text NAME field — only NAME_INDEX, an index
// into the separate TAXI_NAME facility type. TaxiNameId is set by
// SimConnectService (see ResolveTaxiPathNames) to the matching AirportDetails
// .TaxiNames entry's Id, or left null if NAME_INDEX falls outside the
// TAXI_NAME rows returned for the airport. A previous revision flattened
// this into a per-segment Name string, which silently broke the sim's own
// "many paths share one taxiway name" relationship — renaming one path left
// every other path with the same original name untouched. Referencing the
// shared AirportDetails.TaxiNames list by Id fixes that: renaming or
// reassigning propagates to every segment pointing at that Id, with no
// separate resolution step needed here.
public class TaxiPathSegment
{
    // A real enum, not a raw int — see TaxiPathType.cs. Now a user-facing
    // Edit tab picker value, same promotion VasiType/ApproachLightSystemType
    // went through when they became editable.
    public TaxiPathType Type { get; set; }
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
    public double WidthMeters { get; set; }
    public Guid? TaxiNameId { get; set; }

    // TAXI_PATH.RUNWAY_NUMBER/RUNWAY_DESIGNATOR — which runway this path is
    // associated with (e.g. an entrance/exit taxiway near a specific
    // runway), confirmed against the local MSFS 2024 SDK's Facility Data
    // reference. RUNWAY_NUMBER kept as a raw int rather than an enum: the
    // SDK documents 0 (none), 1-36 as literal runway numbers (self-
    // explanatory as plain numbers, an enum with 36 numeric-named members
    // would add nothing), and 37-44 as compass headings (NORTH..NORTHWEST,
    // for helipad-associated paths) plus a 45 (LAST) bounds sentinel — that
    // small non-numeric tail is given a friendly label in
    // AirportDataTreeBuilder instead of a dedicated enum type, same
    // treatment as TaxiParkingSpot.NameCode's GATE_A..GATE_Z labels.
    // RUNWAY_DESIGNATOR is a real enum (TaxiPathRunwayDesignator) since it's
    // a small, meaningful named set and a user-facing Edit tab picker value.
    public int RunwayNumber { get; set; }
    public TaxiPathRunwayDesignator RunwayDesignator { get; set; }

    // TAXI_PATH.LEFT_EDGE/RIGHT_EDGE — the edge paint marking (see
    // TaxiEdgeType.cs), distinct from LeftEdgeLighted/RightEdgeLighted below
    // (how it's painted vs. whether it's lit) and from CenterLine (the
    // path's own centerline, not its edges).
    public TaxiEdgeType LeftEdge { get; set; }
    public TaxiEdgeType RightEdge { get; set; }

    // TAXI_PATH.LEFT_EDGE_LIGHTED/RIGHT_EDGE_LIGHTED per the SDK docs —
    // default false, matching the SDK's own documented default of 0.
    public bool LeftEdgeLighted { get; set; }
    public bool RightEdgeLighted { get; set; }

    // TAXI_PATH.CENTER_LINE/CENTER_LINE_LIGHTED per the SDK docs — default
    // false/false, same documented-default convention as the edge-lighted
    // fields above.
    public bool CenterLine { get; set; }
    public bool CenterLineLighted { get; set; }

    public double? StartXMeters { get; set; }
    public double? StartZMeters { get; set; }
    public double? EndXMeters { get; set; }
    public double? EndZMeters { get; set; }

    // The underlying TAXI_POINT row's own TYPE/ORIENTATION for this segment's
    // Start/End — requested and marshaled by SimConnectService all along, but
    // previously discarded rather than stored anywhere (see TaxiPointType.cs).
    // Confirmed the hard way: exporting every point as type="NORMAL" produced
    // XML the MSFS 2024 SDK Scenery Editor rejected on import ("point not
    // linked to a hold short" / "no hold short within 200m of runway") — this
    // isn't cosmetic, a real airport's taxiway network structurally needs its
    // hold-short points preserved. Null means either the point never resolved
    // at all (see StartXMeters/EndXMeters above) or the sim reported TYPE 0
    // (NONE), which has no meaningful named value here — same convention as
    // this class's other nullable fields. Orientation is only meaningful when
    // the corresponding Type is one of the hold-short variants, per the SDK
    // docs.
    public TaxiPointType? StartPointType { get; set; }
    public TaxiPointType? EndPointType { get; set; }
    public TaxiPointOrientation? StartPointOrientation { get; set; }
    public TaxiPointOrientation? EndPointOrientation { get; set; }
}
