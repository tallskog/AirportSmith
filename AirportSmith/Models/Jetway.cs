namespace AirportSmith.Models;

// Sourced from the separate RequestJetwayData/OnRecvJetwayData API (not Facility
// Data), keyed by parking index. Confirmed returning data for a real airport
// (OEDF, 2026-09-27: the Airport Data tab's Jetways list is populated); the
// individual field values haven't been checked in detail, so fields still
// lean nullable rather than assuming a default.
public class Jetway
{
    public int ParkingIndex { get; set; }
    public int JetwayObjectId { get; set; }
    public int Status { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? HeadingDeg { get; set; }

    // The gate this jetway served when the airport was loaded from the sim,
    // captured once (JetwayGateCheck.CaptureGates) and never edited. The
    // export leaves the stock jetways in place (no deleteAllJetways), and the
    // sim attaches a jetway to its gate by name + number + suffix — so these
    // are what JetwayGateCheck compares the edited parking spots against, to
    // warn when a gate was renamed/renumbered/deleted or moved away. Null in
    // project files saved before this existed.
    public int? GateNameCode { get; set; }
    public int? GateNumber { get; set; }
    public int? GateSuffixCode { get; set; }
    public double? GateBiasXMeters { get; set; }
    public double? GateBiasZMeters { get; set; }
}
