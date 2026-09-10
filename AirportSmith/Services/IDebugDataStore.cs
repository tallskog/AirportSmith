using AirportSmith.Models;

namespace AirportSmith.Services;

// Dev-mode-only escape hatch: lets a developer save the currently loaded
// airport to disk for manual inspection (e.g. comparing against open-data
// sources), and later reload a previously saved file without needing MSFS
// running. See DebugDataStore for the real implementation; only wired up in
// Debug builds (App.xaml.cs).
public interface IDebugDataStore
{
    // Returns the full path the data was written to.
    string Export(AirportDetails airport);

    // Returns null if the file doesn't exist or isn't valid airport JSON.
    AirportDetails? Import(string filePath);
}
