namespace AirportSmith.Models;

// One of RUNWAY's PRIMARY_APPROACH_LIGHTS/SECONDARY_APPROACH_LIGHTS
// sub-structures (the SDK's APPROACHLIGHTS facility data type) — the
// approach lighting system installed at one end of a runway. SystemType is
// the SDK's raw SYSTEM enum value (0=NONE, 1=ODALS, 2=MALSF, 3=MALSR,
// 4=SSALF, 5=SSALR, 6=ALSF1, 7=ALSF2, 8=RAIL, 9=CALVERT, 10=CALVERT2,
// 11=MALS, 12=SALS, 13=SALSF, 14=SSALS) — field names/order and this enum
// confirmed directly against the local MSFS 2024 SDK's own docs
// (Documentation/public/retail/.../simconnect_addtofacilitydefinition),
// not just the earlier online mirror — kept raw for the same reason as
// Runway.SurfaceType/VasiType above.
public record ApproachLightSystem(int SystemType);
