namespace AirportSmith.Models;

// RUNWAY's VASI sub-structure's TYPE field per the SDK docs. No None/0 member —
// SimConnectService only assigns this (via Runway.*VasiType, which stays
// nullable) when the sim reports a nonzero type; 0 means "no VASI/PAPI
// installed" and leaves the wrapping property null, same convention as
// ApproachLightSystemType.
public enum VasiType
{
    Vasi21 = 1,
    Vasi22 = 2,
    Vasi23 = 3,
    Vasi31 = 4,
    Vasi32 = 5,
    Vasi33 = 6,
    Papi2 = 7,
    Papi4 = 8,
    TriColor = 9,
    PVasi = 10,
    TVasi = 11,
    Ball = 12,
    Apap = 13,
}
