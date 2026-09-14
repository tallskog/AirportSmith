namespace AirportSmith.Models;

// Maps TAXI_PATH.RUNWAY_DESIGNATOR — which runway a Runway-associated taxi
// path belongs to, paired with TaxiPathSegment.RunwayNumber. Distinct from
// Runway.PrimaryDesignation/SecondaryDesignation's own designator encoding
// (that one only ever sees NONE/LEFT/RIGHT/CENTER in practice and is kept as
// a raw int there) — TAXI_PATH's documented range is wider (it also covers
// water/A/B runways), so it gets its own named enum here since it's now a
// user-facing Edit tab picker value.
//
// The SDK docs list a 7th value, LAST = 7 — a bounds sentinel marking one
// past the final valid member (a common pattern in this SDK's enums), not a
// real designator anyone would pick. Deliberately not included as a
// selectable member; a raw value of 7 read back from the sim would just
// render as the plain number "7" (see AirportDataTreeBuilder's
// unrecognized-value fallback), which is honest rather than mislabeling it.
public enum TaxiPathRunwayDesignator
{
    None = 0,
    Left = 1,
    Right = 2,
    Center = 3,
    Water = 4,
    A = 5,
    B = 6,
}
