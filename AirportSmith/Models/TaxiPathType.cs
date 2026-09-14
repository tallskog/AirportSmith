namespace AirportSmith.Models;

// Maps TAXI_PATH.TYPE (TaxiPathSegment.Type). Confirmed directly against the
// local MSFS 2024 SDK docs' full 0-8 enumeration (the same source consulted
// for the Edit tab's left/right-edge/runway-association fields — see
// TaxiPathSegment.cs) — this now matches AirportDataTreeBuilder's
// independently-transcribed TaxiPathTypeLabels dictionary exactly, which is
// how the gap (this enum previously stopped at 6, missing ROAD/PAINTEDLINE)
// was caught. One real data point on plausibility: the OIBK export pulled
// earlier in this project (479 taxi path segments) had Type == Path on every
// single row, no other value observed — consistent with Path/Taxi being the
// common "drawable taxiway" values (AirportDiagramProjector treats both as
// drawable), but not proof of what every other value looks like in practice.
// TaxiPathSegment.Type is this enum directly (not a raw int with a separate
// label dictionary) since it's now a user-facing Edit tab picker value, same
// promotion VasiType/ApproachLightSystemType went through.
public enum TaxiPathType
{
    Unknown = 0,
    Taxi = 1,
    Runway = 2,
    Parking = 3,
    Path = 4,
    Closed = 5,
    Vehicle = 6,
    Road = 7,
    PaintedLine = 8,
}
