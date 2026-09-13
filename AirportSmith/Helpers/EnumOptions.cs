using AirportSmith.Models;

namespace AirportSmith.Helpers;

// Combo-box item sources for the Edit tab's enum pickers (MainWindow.xaml).
// VasiType/ApproachLightSystemType include a leading null entry so the user
// can clear a runway's VASI/approach light system back to "not installed" —
// RunwayLightIntensity doesn't need one since it already has a real None
// member (see RunwayLightIntensity.cs).
public static class EnumOptions
{
    public static readonly IReadOnlyList<RunwayLightIntensity> RunwayLightIntensities = Enum.GetValues<RunwayLightIntensity>();

    public static readonly IReadOnlyList<VasiType?> VasiTypes =
        new VasiType?[] { null }.Concat(Enum.GetValues<VasiType>().Cast<VasiType?>()).ToList();

    public static readonly IReadOnlyList<ApproachLightSystemType?> ApproachLightSystemTypes =
        new ApproachLightSystemType?[] { null }.Concat(Enum.GetValues<ApproachLightSystemType>().Cast<ApproachLightSystemType?>()).ToList();
}
