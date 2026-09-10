using AirportSmith.Services;

namespace AirportSmith.Tests.Fakes;

public class FakeFileDialogService : IFileDialogService
{
    public string? PathToReturn { get; set; }
    public string? LastInitialDirectory { get; private set; }

    public string? ShowOpenJsonFileDialog(string initialDirectory)
    {
        LastInitialDirectory = initialDirectory;
        return PathToReturn;
    }
}
