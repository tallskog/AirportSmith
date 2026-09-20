namespace AirportSmith.Models;

// Identifies one of Runway's four VASI/PAPI slots generically, for code that
// needs to address "this specific slot" as data (AirportDiagramProjector's
// position/bias geometry, MainViewModel's click-to-place arming) instead of
// switching on four separate *VasiType/*VasiBiasXMeters/etc. property names
// each time. Left/Right and Primary/Secondary match RunwayEditViewModel's
// existing property name prefixes 1:1.
public enum VasiSlot
{
    PrimaryLeft,
    PrimaryRight,
    SecondaryLeft,
    SecondaryRight,
}
