using AirportSmith.Helpers;
using AirportSmith.Models;
using AirportSmith.ViewModels;

namespace AirportSmith.Tests.ViewModels;

public class ParkingSpotEditViewModelTests
{
    private static AirportDetails AirportWithSpots(params TaxiParkingSpot[] spots)
    {
        var airport = new AirportDetails();
        airport.ParkingSpots.AddRange(spots);
        return airport;
    }

    [Fact]
    public void BuildAll_OneRowPerSpot_InSameOrder()
    {
        var airport = AirportWithSpots(
            new TaxiParkingSpot { ItemIndex = 0, Number = 7 },
            new TaxiParkingSpot { ItemIndex = 1, Number = 3 });

        var edits = ParkingSpotEditViewModel.BuildAll(airport);

        Assert.Equal(2, edits.Count);
        Assert.Equal(7, edits[0].Number);
        Assert.Equal(3, edits[1].Number);
        Assert.Equal(1, edits[1].ItemIndex);
    }

    [Fact]
    public void Setters_WriteStraightThroughToTheWrappedSpot()
    {
        var spot = new TaxiParkingSpot { ItemIndex = 0 };
        var edit = ParkingSpotEditViewModel.BuildAll(AirportWithSpots(spot))[0];

        edit.Number = 12;
        edit.Type = 4;
        edit.NameCode = 10;
        edit.SuffixCode = 12;
        edit.HeadingDeg = 90.5;
        edit.RadiusMeters = 18;
        edit.BiasXMeters = -25;
        edit.BiasZMeters = 40;

        Assert.Equal(12, spot.Number);
        Assert.Equal(4, spot.Type);
        Assert.Equal(10, spot.NameCode);
        Assert.Equal(12, spot.SuffixCode);
        Assert.Equal(90.5, spot.HeadingDeg);
        Assert.Equal(18, spot.RadiusMeters);
        Assert.Equal(-25, spot.BiasXMeters);
        Assert.Equal(40, spot.BiasZMeters);
    }

    [Fact]
    public void Setters_RaisePropertyChanged_OnlyWhenValueActuallyChanges()
    {
        var edit = ParkingSpotEditViewModel.BuildAll(AirportWithSpots(new TaxiParkingSpot { Number = 5 }))[0];
        var raised = new List<string?>();
        edit.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        edit.Number = 5;
        Assert.Empty(raised);

        edit.Number = 6;
        Assert.Equal([nameof(ParkingSpotEditViewModel.Number)], raised);
    }

    [Fact]
    public void MovingASpot_AlsoMovesEndOfParkingTypePathsReferencingIt()
    {
        var airport = AirportWithSpots(
            new TaxiParkingSpot { ItemIndex = 0, BiasXMeters = 1, BiasZMeters = 2 },
            new TaxiParkingSpot { ItemIndex = 1, BiasXMeters = 100, BiasZMeters = 200 });
        var toSpot1 = new TaxiPathSegment { Type = TaxiPathType.Parking, EndIndex = 1, EndXMeters = 100, EndZMeters = 200 };
        var toSpot0 = new TaxiPathSegment { Type = TaxiPathType.Parking, EndIndex = 0, EndXMeters = 1, EndZMeters = 2 };
        // Same EndIndex value, but a taxi point (not a parking spot) — must not follow the spot.
        var ordinaryPath = new TaxiPathSegment { Type = TaxiPathType.Taxi, EndIndex = 1, EndXMeters = 55, EndZMeters = 66 };
        airport.TaxiPaths.AddRange([toSpot1, toSpot0, ordinaryPath]);

        var edits = ParkingSpotEditViewModel.BuildAll(airport);
        edits[1].BiasXMeters = 300;
        edits[1].BiasZMeters = 400;

        Assert.Equal(300, toSpot1.EndXMeters);
        Assert.Equal(400, toSpot1.EndZMeters);
        Assert.Equal(1, toSpot0.EndXMeters);
        Assert.Equal(2, toSpot0.EndZMeters);
        Assert.Equal(55, ordinaryPath.EndXMeters);
        Assert.Equal(66, ordinaryPath.EndZMeters);
    }

    [Fact]
    public void MovingASpot_WhenItemIndicesAreNotUnique_LeavesTaxiPathsAlone()
    {
        // A project saved before ItemIndex was tracked has every spot at 0 —
        // matching by EndIndex would drag unrelated paths along.
        var airport = AirportWithSpots(new TaxiParkingSpot { ItemIndex = 0 }, new TaxiParkingSpot { ItemIndex = 0 });
        var path = new TaxiPathSegment { Type = TaxiPathType.Parking, EndIndex = 0, EndXMeters = 9, EndZMeters = 9 };
        airport.TaxiPaths.Add(path);

        var edits = ParkingSpotEditViewModel.BuildAll(airport);
        edits[0].BiasXMeters = 500;

        Assert.Equal(9, path.EndXMeters);
    }

    [Fact]
    public void ParkingPickers_CoverEveryLabelledCode_AndSuffixIsLimitedToWhatTheExporterCanWrite()
    {
        Assert.Equal(16, EnumOptions.ParkingTypes.Count);
        Assert.Equal("NONE", EnumOptions.ParkingTypes[0].Label);
        Assert.Equal(38, EnumOptions.ParkingNames.Count);
        Assert.Contains(EnumOptions.ParkingNames, o => o.Code == 12 && o.Label == "GATE_A");

        // NONE + GATE_A..GATE_Z only.
        Assert.Equal(27, EnumOptions.ParkingSuffixes.Count);
        Assert.Equal(0, EnumOptions.ParkingSuffixes[0].Code);
        Assert.All(EnumOptions.ParkingSuffixes.Skip(1), o => Assert.InRange(o.Code, 12, 37));
    }
}
