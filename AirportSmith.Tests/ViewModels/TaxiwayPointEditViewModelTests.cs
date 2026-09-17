using AirportSmith.Models;
using AirportSmith.ViewModels;

namespace AirportSmith.Tests.ViewModels;

public class TaxiwayPointEditViewModelTests
{
    [Fact]
    public void BuildAll_DistinctStartAndEndIndices_OneRowPerIndex_WithResolvedCoordinates()
    {
        var airport = new AirportDetails();
        airport.TaxiPaths.Add(new TaxiPathSegment
        {
            Type = TaxiPathType.Taxi,
            StartIndex = 0,
            EndIndex = 1,
            StartXMeters = 10,
            StartZMeters = 20,
            EndXMeters = 30,
            EndZMeters = 40,
        });

        var points = TaxiwayPointEditViewModel.BuildAll(airport);

        Assert.Equal(2, points.Count);
        Assert.Equal(0, points[0].Index);
        Assert.Equal(10, points[0].XMeters);
        Assert.Equal(20, points[0].ZMeters);
        Assert.Equal(1, points[1].Index);
        Assert.Equal(30, points[1].XMeters);
        Assert.Equal(40, points[1].ZMeters);
    }

    [Fact]
    public void BuildAll_SharedIndexAcrossSegments_CollapsesToOneRow()
    {
        var airport = new AirportDetails();
        airport.TaxiPaths.Add(new TaxiPathSegment { Type = TaxiPathType.Taxi, StartIndex = 5, EndIndex = 6, StartXMeters = 0, StartZMeters = 0, EndXMeters = 10, EndZMeters = 0 });
        airport.TaxiPaths.Add(new TaxiPathSegment { Type = TaxiPathType.Taxi, StartIndex = 6, EndIndex = 7, StartXMeters = 10, StartZMeters = 0, EndXMeters = 20, EndZMeters = 0 });

        var points = TaxiwayPointEditViewModel.BuildAll(airport);

        // Index 6 is shared (first segment's End, second segment's Start) —
        // must collapse to one row, not two.
        Assert.Equal(3, points.Count);
        Assert.Equal([5, 6, 7], points.Select(p => p.Index));
    }

    [Fact]
    public void BuildAll_ParkingTypePathEnd_IsExcluded()
    {
        var airport = new AirportDetails();
        airport.TaxiPaths.Add(new TaxiPathSegment
        {
            Type = TaxiPathType.Parking,
            StartIndex = 0,
            EndIndex = 1,
            StartXMeters = 0,
            StartZMeters = 0,
            EndXMeters = 10,
            EndZMeters = 0,
        });

        var points = TaxiwayPointEditViewModel.BuildAll(airport);

        // Only the Start resolves to a taxi point — the End references a
        // TaxiwayParking item instead (see BuildAll's own doc comment).
        Assert.Single(points);
        Assert.Equal(0, points[0].Index);
    }

    [Fact]
    public void BuildAll_UnresolvedCoordinates_IsExcluded_NoCrash()
    {
        var airport = new AirportDetails();
        airport.TaxiPaths.Add(new TaxiPathSegment
        {
            Type = TaxiPathType.Taxi,
            StartIndex = 0,
            EndIndex = 1,
            StartXMeters = null,
            StartZMeters = null,
            EndXMeters = 10,
            EndZMeters = 0,
        });

        var points = TaxiwayPointEditViewModel.BuildAll(airport);

        Assert.Single(points);
        Assert.Equal(1, points[0].Index);
    }

    [Fact]
    public void SettingType_WritesThroughToEverySegmentSharingTheIndex()
    {
        var airport = new AirportDetails();
        var first = new TaxiPathSegment { Type = TaxiPathType.Taxi, StartIndex = 0, EndIndex = 1, StartXMeters = 0, StartZMeters = 0, EndXMeters = 10, EndZMeters = 0 };
        var second = new TaxiPathSegment { Type = TaxiPathType.Taxi, StartIndex = 1, EndIndex = 2, StartXMeters = 10, StartZMeters = 0, EndXMeters = 20, EndZMeters = 0 };
        airport.TaxiPaths.Add(first);
        airport.TaxiPaths.Add(second);

        var points = TaxiwayPointEditViewModel.BuildAll(airport);
        var sharedPoint = points.Single(p => p.Index == 1);

        sharedPoint.Type = TaxiPointType.HoldShort;
        sharedPoint.Orientation = TaxiPointOrientation.Reverse;

        Assert.Equal(TaxiPointType.HoldShort, first.EndPointType);
        Assert.Equal(TaxiPointType.HoldShort, second.StartPointType);
        Assert.Equal(TaxiPointOrientation.Reverse, first.EndPointOrientation);
        Assert.Equal(TaxiPointOrientation.Reverse, second.StartPointOrientation);
        Assert.Equal(TaxiPointType.HoldShort, sharedPoint.Type);
        Assert.Equal(TaxiPointOrientation.Reverse, sharedPoint.Orientation);
    }

    [Fact]
    public void SettingType_RaisesPropertyChanged()
    {
        var airport = new AirportDetails();
        airport.TaxiPaths.Add(new TaxiPathSegment { Type = TaxiPathType.Taxi, StartIndex = 0, EndIndex = 1, StartXMeters = 0, StartZMeters = 0, EndXMeters = 10, EndZMeters = 0 });
        var point = TaxiwayPointEditViewModel.BuildAll(airport)[0];
        var raised = new List<string?>();
        point.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        point.Type = TaxiPointType.Normal;
        point.Orientation = TaxiPointOrientation.Forward;

        Assert.Contains(nameof(TaxiwayPointEditViewModel.Type), raised);
        Assert.Contains(nameof(TaxiwayPointEditViewModel.Orientation), raised);
    }
}
