namespace AirportSmith.Models;

// RUNWAY's APPROACHLIGHTS sub-structure's SYSTEM field per the SDK docs. No
// None/0 member — ApproachLightSystem is only constructed (see
// SimConnectService) when the sim reports a nonzero SYSTEM; 0 means "no
// approach light system installed" and the wrapping Runway property stays
// null instead.
public enum ApproachLightSystemType
{
    Odals = 1,
    Malsf = 2,
    Malsr = 3,
    Ssalf = 4,
    Ssalr = 5,
    Alsf1 = 6,
    Alsf2 = 7,
    Rail = 8,
    Calvert = 9,
    Calvert2 = 10,
    Mals = 11,
    Sals = 12,
    Salsf = 13,
    Ssals = 14,
}
