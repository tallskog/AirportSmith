namespace AirportSmith.Models;

// Aggregate root returned by ISimConnectService.GetAirportDetailsAsync for one ICAO.
public class AirportDetails
{
    public string Icao { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double ElevationMeters { get; set; }
    public double MagneticVariationDeg { get; set; }

    public List<Runway> Runways { get; set; } = [];
    public List<Frequency> Frequencies { get; set; } = [];
    public List<TaxiParkingSpot> ParkingSpots { get; set; } = [];
    public List<TaxiPathSegment> TaxiPaths { get; set; } = [];
    public List<Jetway> Jetways { get; set; } = [];
}
