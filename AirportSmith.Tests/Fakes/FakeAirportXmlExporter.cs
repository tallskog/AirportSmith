using AirportSmith.Models;
using AirportSmith.Services;

namespace AirportSmith.Tests.Fakes;

public class FakeAirportXmlExporter : IAirportXmlExporter
{
    public AirportDetails? LastExported { get; private set; }
    public string? LastFilePath { get; private set; }
    public string PathToReturn { get; set; } = "fake-export-path.xml";
    public IReadOnlyList<string> WarningsToReturn { get; set; } = [];

    public AirportXmlExportOutcome Export(AirportDetails airport, string filePath)
    {
        LastExported = airport;
        LastFilePath = filePath;
        return new AirportXmlExportOutcome(PathToReturn, WarningsToReturn);
    }
}
