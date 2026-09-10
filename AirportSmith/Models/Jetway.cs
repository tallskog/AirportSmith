namespace AirportSmith.Models;

// Sourced from the separate RequestJetwayData/OnRecvJetwayData API (not Facility
// Data), keyed by parking index. Struct layout for that API isn't verified against
// a real sim yet, so fields lean nullable rather than assuming a default.
public class Jetway
{
    public int ParkingIndex { get; set; }
    public int JetwayObjectId { get; set; }
    public int Status { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? HeadingDeg { get; set; }
}
