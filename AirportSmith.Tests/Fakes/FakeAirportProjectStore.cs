using AirportSmith.Models;
using AirportSmith.Services;

namespace AirportSmith.Tests.Fakes;

public class FakeAirportProjectStore : IAirportProjectStore
{
    public AirportDetails? LastSaved { get; private set; }
    public string PathToReturn { get; set; } = "fake-project-path.json";
    public Dictionary<string, AirportDetails> SavedProjects { get; } = [];

    public string Save(AirportDetails airport)
    {
        LastSaved = airport;
        SavedProjects[airport.Icao] = airport;
        return PathToReturn;
    }

    public AirportDetails? Load(string icao) => SavedProjects.GetValueOrDefault(icao);

    public bool HasProject(string icao) => SavedProjects.ContainsKey(icao);
}
