namespace AirportSmith.Models;

// Maps TAXI_PATH.LEFT_EDGE/RIGHT_EDGE — the paint marking drawn along that
// side of the taxi path (distinct from TaxiPathSegment.CenterLine, which is
// the path's own centerline, and from LeftEdgeLighted/RightEdgeLighted,
// which is whether that edge is lit, not how it's painted). Confirmed
// against the local MSFS 2024 SDK's Facility Data reference alongside
// RUNWAY_NUMBER/RUNWAY_DESIGNATOR/CENTER_LINE/CENTER_LINE_LIGHTED — see
// TaxiPathSegment.cs.
public enum TaxiEdgeType
{
    None = 0,
    Solid = 1,
    Dashed = 2,
    SolidDashed = 3,
}
