using AirportSmith.Models;

namespace AirportSmith.Services;

// How a taxi path's Type and its name relate:
// - Taxi/Path/Unknown follow the name. A named path is Taxi and an unnamed
//   one is Path, both when an airport is read in (NormalizeOnRead) and when
//   the name is edited (TaxiPathEditViewModel.TaxiNameId).
// - Runway/Parking keep their type, and their name can't be edited (the
//   Taxi Paths grid shows "---"). A Runway path's name is removed on read,
//   since the XML format doesn't allow `name` on RUNWAY paths anyway (see
//   AirportXmlExporter).
// - Closed/Vehicle/Road/PaintedLine keep their type and an editable name.
//   Turning an unnamed Vehicle road into Path would let AI aircraft taxi on it.
//
// "Named" means the TaxiNameId resolves to a non-blank TaxiName. Many sim
// paths point at the sim's own blank TAXI_NAME entry (see
// TaxiNameEditViewModel.IsUnnamed), and those count as unnamed.
public static class TaxiPathNameRules
{
    public static bool IsNameLocked(TaxiPathType type) => type is TaxiPathType.Runway or TaxiPathType.Parking;

    public static bool FollowsName(TaxiPathType type) => type is TaxiPathType.Taxi or TaxiPathType.Path or TaxiPathType.Unknown;

    public static TaxiPathType TypeForName(bool isNamed) => isNamed ? TaxiPathType.Taxi : TaxiPathType.Path;

    // Applied to data read from the sim (SimConnectService) or a debug dump
    // (MainViewModel.LoadFromFile), not to saved projects, which keep the
    // types the user left them with.
    public static void NormalizeOnRead(AirportDetails airport)
    {
        var namedIds = airport.TaxiNames.Where(n => !string.IsNullOrWhiteSpace(n.Value)).Select(n => n.Id).ToHashSet();

        foreach (var path in airport.TaxiPaths)
        {
            if (path.Type == TaxiPathType.Runway)
                path.TaxiNameId = null;
            else if (FollowsName(path.Type))
                path.Type = TypeForName(path.TaxiNameId is { } id && namedIds.Contains(id));
        }
    }
}
