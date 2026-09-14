using System.Collections;
using System.Globalization;
using System.Reflection;
using AirportSmith.Models;
using AirportSmith.Models.Inspector;

namespace AirportSmith.Services;

// Builds a generic, expandable tree of every field SimConnectService
// populated onto AirportDetails (and everything nested under it — a
// Runway's VASI/PAVEMENT/APPROACH_LIGHTS sub-structures included) for the
// "Airport Data" tab, so the user can inspect exactly what data is (and
// isn't) present for a loaded airport — e.g. to check whether a runway
// actually has an ApproachLightSystem before wondering why the Diagram tab
// isn't drawing one — without a hand-maintained DataGrid column needing to
// be added every time a field is added to the model. Reflection-based and
// generic on purpose (walks whatever public properties AirportDetails'
// object graph has, rather than a hand-written per-type dump), so new
// fields show up automatically. Pure computation, no WPF dependency — same
// testing approach as AirportDiagramProjector.
public static class AirportDataTreeBuilder
{
    // AirportDetails has no cycles (a strict tree of records/POCOs), so no
    // cycle guard is needed here.
    public static IReadOnlyList<DataNode> Build(AirportDetails airport) => BuildObjectChildren(airport);

    private static DataNode BuildNode(Type ownerType, string name, object? value)
    {
        if (value is null)
            return new DataNode { Text = $"{name}: (not present)" };

        var type = value.GetType();

        if (IsLeafType(type))
            return new DataNode { Text = $"{name}: {FormatLeaf(ownerType, name, value)}" };

        if (value is IEnumerable enumerable and not string)
        {
            var items = enumerable.Cast<object?>()
                .Select((item, i) => new DataNode
                {
                    Text = Summarize(i + 1, item),
                    Children = item is null ? [] : BuildObjectChildren(item),
                })
                .ToList();
            return new DataNode { Text = $"{name} ({items.Count})", Children = items };
        }

        return new DataNode { Text = name, Children = BuildObjectChildren(value) };
    }

    private static IReadOnlyList<DataNode> BuildObjectChildren(object obj)
    {
        var ownerType = obj.GetType();
        return ownerType
           .GetProperties(BindingFlags.Public | BindingFlags.Instance)
           .Where(p => p.GetIndexParameters().Length == 0)
           .Select(p => BuildNode(ownerType, p.Name, p.GetValue(obj)))
           .ToList();
    }

    private static bool IsLeafType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsPrimitive || underlying.IsEnum || underlying == typeof(string) || underlying == typeof(decimal);
    }

    // Appends the field's documented enum label in parentheses when one is
    // known (see EnumLabelsByField) — e.g. "SystemType: 9 (CALVERT)" instead
    // of a bare "SystemType: 9" the user has to cross-reference against the
    // SDK docs by hand. Falls back to the plain formatted value when the
    // field isn't a looked-up enum, or the value doesn't match a documented
    // entry (an unrecognized/out-of-range raw value is shown as-is, not
    // hidden or guessed at).
    private static string FormatLeaf(Type ownerType, string propertyName, object value)
    {
        var formatted = value switch
        {
            double d => d.ToString("0.######", CultureInfo.InvariantCulture),
            float f => f.ToString("0.######", CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

        if (value is int i
            && EnumLabelsByField.TryGetValue((ownerType, propertyName), out var labels)
            && labels.TryGetValue(i, out var label))
            return $"{formatted} ({label})";

        return formatted;
    }

    // Text labels for raw enum-backed int fields, transcribed directly from
    // the local MSFS 2024 SDK's own Facility Data reference
    // (Documentation/public/retail/programming-apis/simconnect/api-reference/
    // facilities/simconnect_addtofacilitydefinition/index.md) — the same
    // primary source that resolved the APPROACH_LIGHTS ENABLE-vs-SYSTEM bug
    // above. Runway.SurfaceType is deliberately NOT included here: the SDK's
    // own docs literally say "The return value will be one of the following:"
    // for both RUNWAY.SURFACE and HELIPAD.SURFACE and then give no list at
    // all (a real gap in the SDK's own documentation, not a lookup we missed)
    // — inventing a label list for it would risk showing a wrong or
    // misleading name, worse than showing the honest raw number.
    // ApproachLightSystem.SystemType, Runway.*VasiType, and
    // TaxiPathSegment.Type/RunwayDesignator/LeftEdge/RightEdge used to have
    // (or, for Type, would otherwise need) label dictionaries here — now
    // that they're real enums (ApproachLightSystemType/VasiType/
    // TaxiPathType/TaxiPathRunwayDesignator/TaxiEdgeType),
    // IsLeafType/FormatLeaf's default value.ToString() already renders the
    // member name (e.g. "Papi4") with no lookup needed.

    private static readonly IReadOnlyDictionary<int, string> FrequencyTypeLabels = new Dictionary<int, string>
    {
        [0] = "NONE", [1] = "ATIS", [2] = "MULTICOM", [3] = "UNICOM", [4] = "CTAF", [5] = "GROUND",
        [6] = "TOWER", [7] = "CLEARANCE", [8] = "APPROACH", [9] = "DEPARTURE", [10] = "CENTER",
        [11] = "FSS", [12] = "AWOS", [13] = "ASOS", [14] = "CPT", [15] = "GCO",
    };

    private static readonly IReadOnlyDictionary<int, string> TaxiParkingTypeLabels = new Dictionary<int, string>
    {
        [0] = "NONE", [1] = "RAMP_GA", [2] = "RAMP_GA_SMALL", [3] = "RAMP_GA_MEDIUM", [4] = "RAMP_GA_LARGE",
        [5] = "RAMP_CARGO", [6] = "RAMP_MIL_CARGO", [7] = "RAMP_MIL_COMBAT", [8] = "GATE_SMALL",
        [9] = "GATE_MEDIUM", [10] = "GATE_HEAVY", [11] = "DOCK_GA", [12] = "FUEL", [13] = "VEHICLE",
        [14] = "RAMP_GA_EXTRA", [15] = "GATE_EXTRA",
    };

    // TAXI_PARKING's NAME and SUFFIX fields share this same documented
    // enumeration (both are "the name of the parking spot" per the SDK
    // docs, just used for two different purposes) — 12-37 are GATE_A..GATE_Z.
    private static readonly IReadOnlyDictionary<int, string> TaxiParkingNameLabels = BuildTaxiParkingNameLabels();

    private static Dictionary<int, string> BuildTaxiParkingNameLabels()
    {
        var labels = new Dictionary<int, string>
        {
            [0] = "NONE", [1] = "PARKING", [2] = "N_PARKING", [3] = "NE_PARKING", [4] = "E_PARKING",
            [5] = "SE_PARKING", [6] = "S_PARKING", [7] = "SW_PARKING", [8] = "W_PARKING", [9] = "NW_PARKING",
            [10] = "GATE", [11] = "DOCK",
        };
        for (var letter = 0; letter < 26; letter++)
            labels[12 + letter] = $"GATE_{(char)('A' + letter)}";
        return labels;
    }

    // TAXI_PATH.RUNWAY_NUMBER's documented range is 0 (none), 1-36 (literal
    // runway numbers — self-explanatory as plain numbers, no label needed),
    // then a small non-numeric tail: compass headings for helipad-associated
    // paths, plus a 45 (LAST) bounds sentinel — labelled here the same way
    // TaxiParkingSpot.NameCode's GATE_A..GATE_Z tail is, rather than via a
    // dedicated enum type (see TaxiPathSegment.RunwayNumber's doc comment).
    private static readonly IReadOnlyDictionary<int, string> TaxiPathRunwayNumberLabels = new Dictionary<int, string>
    {
        [0] = "NONE", [37] = "NORTH", [38] = "NORTHEAST", [39] = "EAST", [40] = "SOUTHEAST",
        [41] = "SOUTH", [42] = "SOUTHWEST", [43] = "WEST", [44] = "NORTHWEST", [45] = "LAST",
    };

    private static readonly IReadOnlyDictionary<(Type OwnerType, string PropertyName), IReadOnlyDictionary<int, string>> EnumLabelsByField =
        new Dictionary<(Type, string), IReadOnlyDictionary<int, string>>
        {
            [(typeof(Frequency), nameof(Frequency.Type))] = FrequencyTypeLabels,
            [(typeof(TaxiParkingSpot), nameof(TaxiParkingSpot.Type))] = TaxiParkingTypeLabels,
            [(typeof(TaxiParkingSpot), nameof(TaxiParkingSpot.NameCode))] = TaxiParkingNameLabels,
            [(typeof(TaxiParkingSpot), nameof(TaxiParkingSpot.SuffixCode))] = TaxiParkingNameLabels,
            [(typeof(TaxiPathSegment), nameof(TaxiPathSegment.RunwayNumber))] = TaxiPathRunwayNumberLabels,
        };

    // A short label for a list item's own node, so e.g. Runways[1] reads as
    // "[1] 09L/27R" rather than a bare index — falls back to a plain index
    // for item types with no obvious short name.
    private static string Summarize(int index, object? item) => item switch
    {
        null => $"[{index}] (null)",
        Runway r => $"[{index}] {r.PrimaryDesignation}/{r.SecondaryDesignation}",
        Frequency f => $"[{index}] {f.Name}",
        TaxiParkingSpot p => $"[{index}] #{p.Number}",
        // Can't resolve TaxiNameId to its actual string here — this raw
        // inspector has no AirportDetails.TaxiNames in scope at this call
        // site, same as how StartIndex/EndIndex show as raw indices rather
        // than resolved coordinates elsewhere in this tree.
        TaxiPathSegment t => $"[{index}] {(t.TaxiNameId is { } id ? $"(named, id {id:N})" : $"(unnamed, type {t.Type})")}",
        Jetway j => $"[{index}] Jetway {j.JetwayObjectId}",
        _ => $"[{index}]",
    };
}
