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
// into the separate TAXI_NAME facility type. Name here is resolved from that
// lookup by SimConnectService (see ResolveTaxiPathNames) and left blank if
// the index falls outside the TAXI_NAME rows returned for the airport.
public class TaxiPathSegment
{
    public int Type { get; set; }
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
    public double WidthMeters { get; set; }
    public string Name { get; set; } = string.Empty;

    public double? StartXMeters { get; set; }
    public double? StartZMeters { get; set; }
    public double? EndXMeters { get; set; }
    public double? EndZMeters { get; set; }
}
