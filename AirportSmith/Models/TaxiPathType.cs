namespace AirportSmith.Models;

// Maps TAXI_PATH.TYPE (TaxiPathSegment.Type) per the SDK's Facility Data
// reference. UNCONFIRMED against a live sim — verify the raw int values
// actually observed before trusting this exact label-to-int mapping, same
// discipline already applied to other previously-guessed fields in this
// project. One real data point so far: the OIBK export pulled earlier this
// project (479 taxi path segments) had Type == 4 on every single row, no
// other value observed — consistent with 4 meaning "taxi"/"path" here
// (AirportDiagramProjector treats both Taxi and Path as drawable, so this
// value is covered either way), but not proof of what every other value
// means. Used only by AirportDiagramProjector to decide what counts as a
// drawable taxiway centerline (as opposed to a runway/parking/closed path
// row) — kept out of SimConnectService/TaxiPathSegment.Type itself, which
// stays a raw int.
public enum TaxiPathType
{
    Unknown = 0,
    Taxi = 1,
    Runway = 2,
    Parking = 3,
    Path = 4,
    Closed = 5,
    Vehicle = 6,
}
