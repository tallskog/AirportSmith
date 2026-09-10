namespace AirportSmith.Models;

// Per the SDK's Facility Data reference, TAXI_PARKING has no LATITUDE/LONGITUDE
// field and no PUSHBACK_ANGLE field at all — both were present in an earlier,
// guessed version of this model and have been removed rather than kept as
// always-default placeholders. NAME and SUFFIX are enumerated codes (0-37),
// not free text, despite the field name "NAME" — there is no parking name
// string in this API.
//
// BiasXMeters/BiasZMeters are a different, real field (BIAS_X/BIAS_Z) — the
// spot's position as a local-meters offset, presumably relative to the
// airport reference point, not lat/lon. Units/axis convention are
// UNCONFIRMED against a live sim, same caveat as TaxiPathSegment's resolved
// coordinates.
public class TaxiParkingSpot
{
    public int Number { get; set; }
    public int Type { get; set; }
    public int NameCode { get; set; }
    public int SuffixCode { get; set; }
    public double HeadingDeg { get; set; }
    public double RadiusMeters { get; set; }
    public double BiasXMeters { get; set; }
    public double BiasZMeters { get; set; }
}
