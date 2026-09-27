using AirportSmith.Models;

namespace AirportSmith.Services;

// AirportSmith never exports jetways: the XML <Jetway> needs the jetway's
// SimObject model plus its separate tunnel ("link") scenery object, and the
// tunnel isn't readable from the sim at all. The stock jetways stay in place
// instead (no deleteAllJetways), and the sim attaches each one to its gate
// by name + number + suffix. That works until a gate is renamed,
// renumbered or deleted (the jetway has nothing to attach to), or moved (the
// jetway stays where it was and may not reach). This records each jetway's
// original gate at load time and, at export, warns about exactly those
// cases. Pure — no I/O.
public static class JetwayGateCheck
{
    // Moving a gate less than this is within what a jetway can reach anyway.
    public const double MaxGateMoveMeters = 2.0;

    // Records, on each jetway, the gate (parking spot with the matching
    // ItemIndex) it serves right now. Called once after loading from the sim,
    // before any edits. Jetways whose spot isn't found are left unrecorded.
    public static void CaptureGates(AirportDetails airport)
    {
        foreach (var jetway in airport.Jetways)
        {
            var spot = airport.ParkingSpots.FirstOrDefault(s => s.ItemIndex == jetway.ParkingIndex);
            if (spot is null) continue;
            jetway.GateNameCode = spot.NameCode;
            jetway.GateNumber = spot.Number;
            jetway.GateSuffixCode = spot.SuffixCode;
            jetway.GateBiasXMeters = spot.BiasXMeters;
            jetway.GateBiasZMeters = spot.BiasZMeters;
        }
    }

    public static List<string> FindWarnings(AirportDetails airport)
    {
        var warnings = new List<string>();
        var unrecorded = 0;
        var checkedGates = new HashSet<(int, int, int)>();

        foreach (var jetway in airport.Jetways)
        {
            if (jetway is not { GateNameCode: { } name, GateNumber: { } number, GateSuffixCode: { } suffix,
                               GateBiasXMeters: { } x, GateBiasZMeters: { } z })
            {
                unrecorded++;
                continue;
            }
            if (!checkedGates.Add((name, number, suffix))) continue; // one warning per gate

            var label = GateLabel(name, number, suffix);
            var spot = airport.ParkingSpots.FirstOrDefault(s => s.NameCode == name && s.Number == number && s.SuffixCode == suffix);
            if (spot is null)
            {
                warnings.Add($"Gate {label} had a jetway, but no parking spot is named {label} any more (renamed, " +
                    "renumbered or deleted). The stock jetway will have no gate to attach to.");
                continue;
            }

            var moved = Math.Sqrt(Math.Pow(spot.BiasXMeters - x, 2) + Math.Pow(spot.BiasZMeters - z, 2));
            if (moved > MaxGateMoveMeters)
                warnings.Add($"Gate {label} has a jetway but was moved {moved:F0} m. The stock jetway stays where it " +
                    "was and may not reach the aircraft.");
        }

        if (unrecorded > 0)
            warnings.Add($"{unrecorded} jetway(s) can't be checked against their gates: their original gate wasn't " +
                "recorded (project saved by an older AirportSmith version). Renamed, renumbered, deleted or moved " +
                "gates may lose their stock jetway.");
        return warnings;
    }

    // E.g. "GATE_A 12" or "GATE 5 GATE_B", matching the XML's name/number/suffix.
    private static string GateLabel(int nameCode, int number, int suffixCode)
    {
        var name = AirportDataTreeBuilder.TaxiParkingNameLabels.TryGetValue(nameCode, out var n) ? n : $"name {nameCode}";
        var suffix = suffixCode is >= 12 and <= 37 && AirportDataTreeBuilder.TaxiParkingNameLabels.TryGetValue(suffixCode, out var s)
            ? $" {s}"
            : string.Empty;
        return $"{name} {number}{suffix}";
    }
}
