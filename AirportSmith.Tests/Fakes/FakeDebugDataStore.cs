using AirportSmith.Models;
using AirportSmith.Services;

namespace AirportSmith.Tests.Fakes;

public class FakeDebugDataStore : IDebugDataStore
{
    public AirportDetails? LastExported { get; private set; }
    public string PathToReturn { get; set; } = "fake-export-path.json";
    public AirportDetails? AirportToImport { get; set; }
    public string? LastImportedPath { get; private set; }

    public string Export(AirportDetails airport)
    {
        LastExported = airport;
        return PathToReturn;
    }

    public AirportDetails? Import(string filePath)
    {
        LastImportedPath = filePath;
        return AirportToImport;
    }
}
