namespace AirportSmith.Models;

// Maps TAXI_POINT.TYPE per the SDK's Facility Data reference
// (facilities/simconnect_addtofacilitydefinition, #taxi_point): 0 NONE,
// 1 NORMAL, 2 HOLD_SHORT, 4 ILS_HOLD_SHORT, 5 HOLD_SHORT_NO_DRAW,
// 6 ILS_HOLD_SHORT_NO_DRAW (3 is not used). No None/0 member — same
// convention as VasiType/ApproachLightSystemType: 0 has no valid encoding in
// bglcomp.xsd's stTaxiPointType enum either, so a raw 0 (or a point that
// never resolved at all) leaves the wrapping TaxiPathSegment property null
// rather than inventing a fake "none" member.
public enum TaxiPointType
{
    Normal = 1,
    HoldShort = 2,
    IlsHoldShort = 4,
    HoldShortNoDraw = 5,
    IlsHoldShortNoDraw = 6,
}
