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
    public int Type { get; set; }
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
    public double WidthMeters { get; set; }
    public Guid? TaxiNameId { get; set; }

    // TAXI_PATH.LEFT_EDGE_LIGHTED/RIGHT_EDGE_LIGHTED per the SDK docs —
    // default false, matching the SDK's own documented default of 0.
    public bool LeftEdgeLighted { get; set; }
    public bool RightEdgeLighted { get; set; }

    public double? StartXMeters { get; set; }
    public double? StartZMeters { get; set; }
    public double? EndXMeters { get; set; }
    public double? EndZMeters { get; set; }
}
