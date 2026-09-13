using AirportSmith.Models;

namespace AirportSmith.Services;

// Persists the user's edits to an airport (taxi path naming/lighting, runway
// lighting/VASI/approach lights, etc.) as a real project file under
// AppDataHelper.AppDataPath — unlike IDebugDataStore, this is a always-on,
// user-facing feature (not a Debug-only dev aid), so it's wired up in both
// Debug and Release builds.
public interface IAirportProjectStore
{
    // Returns the full path the project was written to.
    string Save(AirportDetails airport);

    // Returns null if no project exists for this ICAO, or the file isn't
    // valid project JSON.
    AirportDetails? Load(string icao);

    bool HasProject(string icao);
}
