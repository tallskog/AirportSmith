using System.IO;
using System.Linq;
using System.Text.Json;
using AirportSmith.Helpers;
using AirportSmith.Models;

namespace AirportSmith.Services;

// Real, always-on persistence for the user's airport edits — as opposed to
// DebugDataStore's throwaway temp-folder dump. Writes to
// {baseDirectory}\Projects\{ICAO}.json, one file per ICAO (a new Save simply
// overwrites the previous project for that airport; there's no version
// history in this first pass).
//
// baseDirectory defaults to AppDataHelper.AppDataPath (which already
// resolves to AirportSmith-dev in Debug builds vs AirportSmith in Release —
// see AppDataHelper) but is overridable via the constructor so tests never
// touch the real dev AppData folder, per CLAUDE.md's data-safety guardrail.
public class AirportProjectStore : IAirportProjectStore
{
    private const int CurrentSchemaVersion = 2;
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _projectsDirectory;

    public AirportProjectStore(string? baseDirectory = null)
    {
        _projectsDirectory = Path.Combine(baseDirectory ?? AppDataHelper.AppDataPath, "Projects");
    }

    public string Save(AirportDetails airport)
    {
        Directory.CreateDirectory(_projectsDirectory);
        var file = new AirportProjectFile
        {
            SchemaVersion = CurrentSchemaVersion,
            SavedAtUtc = DateTime.UtcNow,
            Airport = airport,
        };
        var path = ProjectPath(airport.Icao);
        File.WriteAllText(path, JsonSerializer.Serialize(file, SerializerOptions));
        return path;
    }

    public AirportDetails? Load(string icao)
    {
        var path = ProjectPath(icao);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            var file = JsonSerializer.Deserialize<AirportProjectFile>(json);
            if (file is null) return null;

            MigrateLegacyTaxiNames(file.Airport, json);
            return file.Airport;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public bool HasProject(string icao) => File.Exists(ProjectPath(icao));

    private string ProjectPath(string icao) => Path.Combine(_projectsDirectory, $"{icao}.json");

    // Schema v1 kept each taxi path's name as its own free-text
    // TaxiPathSegment.Name string; v2 replaced that with the shared,
    // user-editable AirportDetails.TaxiNames list referenced by
    // TaxiPathSegment.TaxiNameId (see TaxiName.cs), so renaming/deleting a
    // name doesn't silently break every other path that shared it. A v1
    // file's "Name" property is unknown to the current TaxiPathSegment shape
    // and would otherwise be silently discarded by System.Text.Json on
    // deserialize — reconstruct TaxiNames/TaxiNameId from it instead, per
    // CLAUDE.md's rule against silently dropping an existing value. Distinct
    // legacy name strings become one shared TaxiName each, which also
    // restores the "multiple paths share one taxiway name" relationship that
    // flattening to a per-segment string had broken in the first place.
    private static void MigrateLegacyTaxiNames(AirportDetails airport, string json)
    {
        if (airport.TaxiNames.Count > 0) return; // already v2-shaped

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("Airport", out var airportElement) ||
            !airportElement.TryGetProperty("TaxiPaths", out var taxiPathsElement) ||
            taxiPathsElement.ValueKind != JsonValueKind.Array)
            return;

        var legacyNames = taxiPathsElement.EnumerateArray()
            .Select(pathElement => pathElement.TryGetProperty("Name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString()
                : null)
            .ToList();

        if (legacyNames.All(string.IsNullOrEmpty)) return; // nothing to migrate

        var namesByValue = new Dictionary<string, TaxiName>();
        for (var i = 0; i < airport.TaxiPaths.Count && i < legacyNames.Count; i++)
        {
            var value = legacyNames[i];
            if (string.IsNullOrEmpty(value)) continue;

            if (!namesByValue.TryGetValue(value, out var taxiName))
            {
                taxiName = new TaxiName { Value = value };
                namesByValue[value] = taxiName;
                airport.TaxiNames.Add(taxiName);
            }
            airport.TaxiPaths[i].TaxiNameId = taxiName.Id;
        }
    }
}
