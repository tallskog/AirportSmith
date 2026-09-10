namespace AirportSmith.Models;

public class Frequency
{
    // Enum-backed integer per SimConnect (tower/ground/ATIS/approach/departure/...);
    // kept raw until real sim data confirms the enum values to map against.
    public int Type { get; set; }
    public double FrequencyHz { get; set; }
    public string Name { get; set; } = string.Empty;
}
