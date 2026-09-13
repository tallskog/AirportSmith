namespace AirportSmith.Models;

// RUNWAY.EDGE_LIGHTS per the SDK docs — unlike VasiType/ApproachLightSystemType,
// 0 (None) is a normal, always-present value here (every runway reports an
// edge-light status), not a "field wasn't installed" sentinel, so this enum
// has a real None member and Runway.EdgeLightIntensity is non-nullable.
public enum RunwayLightIntensity
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}
