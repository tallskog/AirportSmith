using System.IO;

namespace AirportSmith.Helpers;

public static class AppDataHelper
{
    private static readonly string _appDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
#if DEBUG
        "AirportSmith-dev");
#else
        // Not plain "AirportSmith": that is the Velopack install folder
        // (%LocalAppData%\AirportSmith\), which an uninstall deletes wholesale.
        "AirportSmith-data");
#endif

    public static string AppDataPath => _appDataPath;

    public static void EnsureCreated()
        => Directory.CreateDirectory(_appDataPath);
}
