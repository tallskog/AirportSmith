using System.IO;
using System.Text.Json;
using AirportSmith.Models;

namespace AirportSmith.Services;

// Writes to the OS temp directory, not AppDataHelper.AppDataPath — this data
// is throwaway debugging output, never a persisted project/settings file, so
// it must stay out of the AppData safety guardrail entirely. Files are left
// in place across app runs (not cleaned up on exit) so a previously exported
// airport can be reloaded via Import without MSFS running.
public class DebugDataStore : IDebugDataStore
{
    public static readonly string ExportDirectory = Path.Combine(Path.GetTempPath(), "AirportSmith-dev");

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public string Export(AirportDetails airport)
    {
        Directory.CreateDirectory(ExportDirectory);
        var fileName = $"{airport.Icao}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        var path = Path.Combine(ExportDirectory, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(airport, SerializerOptions));
        return path;
    }

    public AirportDetails? Import(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<AirportDetails>(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
