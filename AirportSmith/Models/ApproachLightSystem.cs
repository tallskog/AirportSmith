namespace AirportSmith.Models;

// One of RUNWAY's PRIMARY_APPROACH_LIGHTS/SECONDARY_APPROACH_LIGHTS
// sub-structures (the SDK's APPROACHLIGHTS facility data type) — the
// approach lighting system installed at one end of a runway. Field
// names/order and ApproachLightSystemType confirmed directly against the
// local MSFS 2024 SDK's own docs (Documentation/public/retail/.../
// simconnect_addtofacilitydefinition).
public record ApproachLightSystem(ApproachLightSystemType SystemType);
