namespace AirportSmith.Models;

// One of RUNWAY's PAVEMENT sub-structures — a displaced threshold, blast pad,
// or overrun/stopway attached to one end of a runway. LengthMeters/WidthMeters
// are straight passthroughs of the SDK's PAVEMENT.LENGTH/WIDTH (FLOAT32) —
// UNCONFIRMED against a live sim like the rest of this project's newly-added
// fields, verify before trusting.
public record RunwayPavementFeature(double LengthMeters, double WidthMeters);
