using AirportSmith.Models;

namespace AirportSmith.Helpers;

// Combo-box item sources for the Edit tab's enum pickers (MainWindow.xaml).
// VasiType/ApproachLightSystemType include a leading null entry so the user
// can clear a runway's VASI/approach light system back to "not installed" —
// RunwayLightIntensity doesn't need one since it already has a real None
// member (see RunwayLightIntensity.cs). Same reasoning applies to
// TaxiPathType/TaxiPathRunwayDesignator/TaxiEdgeType below — each has a real
// None/Unknown member (0), so no leading null entry is needed either.
public static class EnumOptions
{
    public static readonly IReadOnlyList<RunwayLightIntensity> RunwayLightIntensities = Enum.GetValues<RunwayLightIntensity>();

    public static readonly IReadOnlyList<VasiType?> VasiTypes =
        new VasiType?[] { null }.Concat(Enum.GetValues<VasiType>().Cast<VasiType?>()).ToList();

    public static readonly IReadOnlyList<ApproachLightSystemType?> ApproachLightSystemTypes =
        new ApproachLightSystemType?[] { null }.Concat(Enum.GetValues<ApproachLightSystemType>().Cast<ApproachLightSystemType?>()).ToList();

    public static readonly IReadOnlyList<TaxiPathType> TaxiPathTypes = Enum.GetValues<TaxiPathType>();

    public static readonly IReadOnlyList<TaxiPathRunwayDesignator> TaxiPathRunwayDesignators = Enum.GetValues<TaxiPathRunwayDesignator>();

    public static readonly IReadOnlyList<TaxiEdgeType> TaxiEdgeTypes = Enum.GetValues<TaxiEdgeType>();

    // TaxiPointType has no None/0 member (see its own doc comment) — a
    // leading null entry lets the Taxiway Points grid clear a point back to
    // "never resolved a type", same reasoning as VasiType/ApproachLightSystemType
    // above.
    public static readonly IReadOnlyList<TaxiPointType?> TaxiPointTypes =
        new TaxiPointType?[] { null }.Concat(Enum.GetValues<TaxiPointType>().Cast<TaxiPointType?>()).ToList();

    // Orientation is only meaningful for a hold-short-family Type — null
    // means "not applicable/not set", same leading-null convention as above.
    public static readonly IReadOnlyList<TaxiPointOrientation?> TaxiPointOrientations =
        new TaxiPointOrientation?[] { null }.Concat(Enum.GetValues<TaxiPointOrientation>().Cast<TaxiPointOrientation?>()).ToList();

    // Nullable variants of TaxiPathTypes/TaxiPathRunwayDesignators/TaxiEdgeTypes
    // above, for the Taxi Paths grid's per-column filters (TaxiPathFilterViewModel)
    // — kept separate from the non-nullable lists used by the actual per-row Edit
    // tab pickers, since those enums already have a real 0 member (Unknown/None)
    // that's a valid value to PICK for a path, but must stay distinct from "(any)"
    // / no filter applied when FILTERING.
    public static readonly IReadOnlyList<TaxiPathType?> TaxiPathTypesFilter =
        new TaxiPathType?[] { null }.Concat(Enum.GetValues<TaxiPathType>().Cast<TaxiPathType?>()).ToList();

    public static readonly IReadOnlyList<TaxiPathRunwayDesignator?> TaxiPathRunwayDesignatorsFilter =
        new TaxiPathRunwayDesignator?[] { null }.Concat(Enum.GetValues<TaxiPathRunwayDesignator>().Cast<TaxiPathRunwayDesignator?>()).ToList();

    public static readonly IReadOnlyList<TaxiEdgeType?> TaxiEdgeTypesFilter =
        new TaxiEdgeType?[] { null }.Concat(Enum.GetValues<TaxiEdgeType>().Cast<TaxiEdgeType?>()).ToList();

    // A labeled 3-way (any/yes/no) option list for the Taxi Paths grid's
    // boolean-column filters (left/right edge lighted, center line/lighted,
    // hide from diagram) — a plain IReadOnlyList<bool?> would display via
    // bool?'s own ToString() ("True"/"False", blank for null), which isn't as
    // clear as an explicit label in a filter dropdown.
    public static readonly IReadOnlyList<BoolFilterOption> BoolFilterOptions =
    [
        new("(any)", null),
        new("Yes", true),
        new("No", false),
    ];
}

// See EnumOptions.BoolFilterOptions.
public sealed record BoolFilterOption(string Label, bool? Value);
