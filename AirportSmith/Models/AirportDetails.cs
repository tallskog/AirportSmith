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

    // The sim's TAXI_NAME rows, referenced by TaxiPathSegment.TaxiNameId — see
    // TaxiName.cs for why references use a stable Id rather than list position.
    public List<TaxiName> TaxiNames { get; set; } = [];

    // User export option, not sim data (SimConnect never sets it): when true,
    // the exported XML's <DeleteAirport> also carries deleteAllTaxiwaySigns=
    // "TRUE". The stock airport's taxiway signs are separate objects that
    // deleteAllTaxiways doesn't remove, so after renaming taxiways this is the
    // only way to get rid of the default signs showing the old names. Saved
    // with the project; a project file from before this existed loads it as
    // false.
    public bool DeleteAllTaxiwaySigns { get; set; }
}
