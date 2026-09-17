using AirportSmith.Models;
using AirportSmith.ViewModels;

namespace AirportSmith.Tests.ViewModels;

public class TaxiPathFilterViewModelTests
{
    [Fact]
    public void HasAnyFilter_NoFiltersSet_IsFalse()
    {
        Assert.False(new TaxiPathFilterViewModel().HasAnyFilter);
    }

    [Theory]
    [InlineData(nameof(TaxiPathFilterViewModel.NameFilter))]
    [InlineData(nameof(TaxiPathFilterViewModel.StartIndexFilter))]
    [InlineData(nameof(TaxiPathFilterViewModel.EndIndexFilter))]
    [InlineData(nameof(TaxiPathFilterViewModel.RunwayNumberFilter))]
    public void HasAnyFilter_StringFilterSet_IsTrue(string propertyName)
    {
        var filter = new TaxiPathFilterViewModel();
        typeof(TaxiPathFilterViewModel).GetProperty(propertyName)!.SetValue(filter, "x");

        Assert.True(filter.HasAnyFilter);
    }

    [Fact]
    public void HasAnyFilter_EnumOrBoolFilterSet_IsTrue()
    {
        Assert.True(new TaxiPathFilterViewModel { TypeFilter = TaxiPathType.Taxi }.HasAnyFilter);
        Assert.True(new TaxiPathFilterViewModel { RunwayDesignatorFilter = TaxiPathRunwayDesignator.Left }.HasAnyFilter);
        Assert.True(new TaxiPathFilterViewModel { LeftEdgeFilter = TaxiEdgeType.Solid }.HasAnyFilter);
        Assert.True(new TaxiPathFilterViewModel { LeftEdgeLightedFilter = true }.HasAnyFilter);
        Assert.True(new TaxiPathFilterViewModel { RightEdgeFilter = TaxiEdgeType.Dashed }.HasAnyFilter);
        Assert.True(new TaxiPathFilterViewModel { RightEdgeLightedFilter = false }.HasAnyFilter);
        Assert.True(new TaxiPathFilterViewModel { CenterLineFilter = true }.HasAnyFilter);
        Assert.True(new TaxiPathFilterViewModel { CenterLineLightedFilter = false }.HasAnyFilter);
        Assert.True(new TaxiPathFilterViewModel { HiddenFromDiagramFilter = true }.HasAnyFilter);
    }

    [Fact]
    public void Reset_ClearsEveryFilterField()
    {
        var filter = new TaxiPathFilterViewModel
        {
            NameFilter = "A",
            TypeFilter = TaxiPathType.Taxi,
            StartIndexFilter = "1",
            EndIndexFilter = "2",
            RunwayNumberFilter = "9",
            RunwayDesignatorFilter = TaxiPathRunwayDesignator.Left,
            LeftEdgeFilter = TaxiEdgeType.Solid,
            LeftEdgeLightedFilter = true,
            RightEdgeFilter = TaxiEdgeType.Dashed,
            RightEdgeLightedFilter = false,
            CenterLineFilter = true,
            CenterLineLightedFilter = false,
            HiddenFromDiagramFilter = true,
        };

        filter.Reset();

        Assert.False(filter.HasAnyFilter);
        Assert.Null(filter.NameFilter);
        Assert.Null(filter.TypeFilter);
        Assert.Null(filter.StartIndexFilter);
        Assert.Null(filter.EndIndexFilter);
        Assert.Null(filter.RunwayNumberFilter);
        Assert.Null(filter.RunwayDesignatorFilter);
        Assert.Null(filter.LeftEdgeFilter);
        Assert.Null(filter.LeftEdgeLightedFilter);
        Assert.Null(filter.RightEdgeFilter);
        Assert.Null(filter.RightEdgeLightedFilter);
        Assert.Null(filter.CenterLineFilter);
        Assert.Null(filter.CenterLineLightedFilter);
        Assert.Null(filter.HiddenFromDiagramFilter);
    }

    [Fact]
    public void SettingProperty_RaisesPropertyChanged()
    {
        var filter = new TaxiPathFilterViewModel();
        var raised = new List<string?>();
        filter.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        filter.NameFilter = "A";
        filter.TypeFilter = TaxiPathType.Taxi;

        Assert.Contains(nameof(TaxiPathFilterViewModel.NameFilter), raised);
        Assert.Contains(nameof(TaxiPathFilterViewModel.TypeFilter), raised);
    }
}
