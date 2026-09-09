using System.IO;

namespace AirportSmith.Helpers;

public static class AppDataHelper
{
    private static readonly string _appDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
#if DEBUG
        "AirportSmith-dev");
#else
        "AirportSmith");
#endif

    public static string AppDataPath => _appDataPath;

    public static void EnsureCreated()
        => Directory.CreateDirectory(_appDataPath);
}
