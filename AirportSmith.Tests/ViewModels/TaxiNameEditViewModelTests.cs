using AirportSmith.Models;
using AirportSmith.ViewModels;

namespace AirportSmith.Tests.ViewModels;

public class TaxiNameEditViewModelTests
{
    [Fact]
    public void SettingValue_WritesThroughToWrappedTaxiName()
    {
        var model = new TaxiName { Value = "A" };
        var vm = new TaxiNameEditViewModel(model);

        vm.Value = "Alpha";

        Assert.Equal("Alpha", model.Value);
    }

    [Fact]
    public void SettingProperty_RaisesPropertyChanged()
    {
        var vm = new TaxiNameEditViewModel(new TaxiName { Value = "A" });
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Value = "Alpha";

        Assert.Contains(nameof(TaxiNameEditViewModel.Value), raised);
        Assert.Contains(nameof(TaxiNameEditViewModel.DisplayValue), raised);
        Assert.Contains(nameof(TaxiNameEditViewModel.IsUnnamed), raised);
    }

    [Fact]
    public void SettingSameValue_DoesNotRaisePropertyChanged()
    {
        var vm = new TaxiNameEditViewModel(new TaxiName { Value = "A" });
        var raised = false;
        vm.PropertyChanged += (_, _) => raised = true;

        vm.Value = "A";

        Assert.False(raised);
    }

    // A blank Value is a real, often heavily-shared sim entry (e.g. taxi
    // paths leading to parking legitimately have no name) — DisplayValue/
    // IsUnnamed exist so the Taxi Names panel and Name pickers can show it as
    // "(no name)" instead of an indistinguishable blank row, per user
    // feedback after a blank entry was renamed without realizing how many
    // paths pointed at it.
    [Fact]
    public void EmptyValue_DisplayValueShowsNoNamePlaceholder_AndIsUnnamedTrue()
    {
        var vm = new TaxiNameEditViewModel(new TaxiName { Value = "" });

        Assert.Equal("(no name)", vm.DisplayValue);
        Assert.True(vm.IsUnnamed);
    }

    [Fact]
    public void NonEmptyValue_DisplayValueEqualsValue_AndIsUnnamedFalse()
    {
        var vm = new TaxiNameEditViewModel(new TaxiName { Value = "Alpha" });

        Assert.Equal("Alpha", vm.DisplayValue);
        Assert.False(vm.IsUnnamed);
    }

    [Fact]
    public void ClearingValueToEmpty_UpdatesDisplayValueAndIsUnnamed()
    {
        var vm = new TaxiNameEditViewModel(new TaxiName { Value = "Alpha" });

        vm.Value = "";

        Assert.Equal("(no name)", vm.DisplayValue);
        Assert.True(vm.IsUnnamed);
    }
}
