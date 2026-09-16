namespace AirportSmith.Models;

// Maps TAXI_POINT.ORIENTATION per the SDK's Facility Data reference —
// documented as meaningful only when the point's Type is one of the
// hold-short variants (see TaxiPointType).
public enum TaxiPointOrientation
{
    Forward = 0,
    Reverse = 1,
}
