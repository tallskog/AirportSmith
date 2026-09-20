using AirportSmith.Models;

namespace AirportSmith.ViewModels;

// Editable wrapper around one TaxiParkingSpot for the Edit tab's Parking
// grid. Properties read/write straight through to the wrapped spot, same
// no-separate-apply pattern as TaxiPathEditViewModel/RunwayEditViewModel.
//
// BiasXMeters/BiasZMeters additionally write through to every Type == Parking
// taxi path whose EndIndex references this spot: the extractor
// (SimConnectService.ResolveTaxiPathPoints) copies the spot's position into
// such a path's EndXMeters/EndZMeters, so leaving those behind when the spot
// moves would leave the airport's own data internally inconsistent (nothing
// draws or exports those particular coordinates today, but the Airport Data
// tab shows them, and a future feature could rely on them).
public class ParkingSpotEditViewModel : ViewModelBase
{
    private readonly TaxiParkingSpot _spot;
    private readonly IReadOnlyList<TaxiPathSegment> _parkingPaths;

    public ParkingSpotEditViewModel(TaxiParkingSpot spot, IReadOnlyList<TaxiPathSegment>? parkingPaths = null)
    {
        _spot = spot;
        _parkingPaths = parkingPaths ?? [];
    }

    // The sim's own TAXI_PARKING row index — what a Parking-type taxi path's
    // EndIndex references. Read-only: changing it would silently re-point (or
    // orphan) those paths.
    public int ItemIndex => _spot.ItemIndex;

    public int Number
    {
        get => _spot.Number;
        set { if (_spot.Number == value) return; _spot.Number = value; OnPropertyChanged(); }
    }

    // Type/NameCode/SuffixCode are the SDK's enumerated codes (see
    // AirportDataTreeBuilder.TaxiParkingTypeLabels/TaxiParkingNameLabels and
    // EnumOptions.ParkingTypes/ParkingNames/ParkingSuffixes for the pickers).
    public int Type
    {
        get => _spot.Type;
        set { if (_spot.Type == value) return; _spot.Type = value; OnPropertyChanged(); }
    }

    public int NameCode
    {
        get => _spot.NameCode;
        set { if (_spot.NameCode == value) return; _spot.NameCode = value; OnPropertyChanged(); }
    }

    public int SuffixCode
    {
        get => _spot.SuffixCode;
        set { if (_spot.SuffixCode == value) return; _spot.SuffixCode = value; OnPropertyChanged(); }
    }

    public double HeadingDeg
    {
        get => _spot.HeadingDeg;
        set { if (_spot.HeadingDeg == value) return; _spot.HeadingDeg = value; OnPropertyChanged(); }
    }

    public double RadiusMeters
    {
        get => _spot.RadiusMeters;
        set { if (_spot.RadiusMeters == value) return; _spot.RadiusMeters = value; OnPropertyChanged(); }
    }

    public double BiasXMeters
    {
        get => _spot.BiasXMeters;
        set
        {
            if (_spot.BiasXMeters == value) return;
            _spot.BiasXMeters = value;
            foreach (var path in _parkingPaths) path.EndXMeters = value;
            OnPropertyChanged();
        }
    }

    public double BiasZMeters
    {
        get => _spot.BiasZMeters;
        set
        {
            if (_spot.BiasZMeters == value) return;
            _spot.BiasZMeters = value;
            foreach (var path in _parkingPaths) path.EndZMeters = value;
            OnPropertyChanged();
        }
    }

    // One edit view model per airport.ParkingSpots entry, in the same order —
    // MainViewModel relies on that 1:1 order (and ParkingSpotShape.SourceIndex
    // matching it) rather than searching. A Parking-type path is only linked
    // to a spot when that spot's ItemIndex is unique among the airport's
    // spots: a project saved before ItemIndex was tracked has every spot at
    // 0 (see TaxiParkingSpot.ItemIndex), where matching by EndIndex would
    // wrongly drag unrelated paths along with whichever spot is edited.
    public static IReadOnlyList<ParkingSpotEditViewModel> BuildAll(AirportDetails airport)
    {
        var indexIsUnique = airport.ParkingSpots
            .GroupBy(s => s.ItemIndex)
            .ToDictionary(g => g.Key, g => g.Count() == 1);
        var pathsByEndIndex = airport.TaxiPaths
            .Where(p => p.Type == TaxiPathType.Parking)
            .ToLookup(p => p.EndIndex);

        return airport.ParkingSpots
            .Select(spot => new ParkingSpotEditViewModel(
                spot,
                indexIsUnique[spot.ItemIndex] ? pathsByEndIndex[spot.ItemIndex].ToList() : []))
            .ToList();
    }
}
