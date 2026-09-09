using AirportSmith.Helpers;

namespace AirportSmith.Tests;

public class AppDataHelperTests
{
    [Fact]
    public void AppDataPath_UsesDevSuffix_InDebugBuilds()
    {
        // The test project always builds Debug-equivalent (#if DEBUG in AppDataHelper),
        // so this pins the guardrail that keeps tests off the real AppData folder.
        Assert.EndsWith("AirportSmith-dev", AppDataHelper.AppDataPath);
    }
}
