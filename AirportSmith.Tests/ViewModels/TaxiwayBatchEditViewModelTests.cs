using AirportSmith.ViewModels;

namespace AirportSmith.Tests.ViewModels;

public class TaxiwayBatchEditViewModelTests
{
    [Fact]
    public void NewInstance_StartsUntouched()
    {
        var vm = new TaxiwayBatchEditViewModel();

        Assert.False(vm.IsTaxiNameIdTouched);
        Assert.Null(vm.TaxiNameId);
        Assert.Null(vm.LeftEdgeLighted);
        Assert.Null(vm.RightEdgeLighted);
    }

    [Fact]
    public void SettingTaxiNameId_MarksItTouched_EvenWhenSetToNull()
    {
        var vm = new TaxiwayBatchEditViewModel();

        vm.TaxiNameId = null; // e.g. the user explicitly picks "(none)"

        Assert.True(vm.IsTaxiNameIdTouched);
    }

    [Fact]
    public void ResetToUntouched_ClearsValuesAndTouchedFlag()
    {
        var vm = new TaxiwayBatchEditViewModel
        {
            TaxiNameId = Guid.NewGuid(),
            LeftEdgeLighted = true,
            RightEdgeLighted = false,
        };

        vm.ResetToUntouched();

        Assert.False(vm.IsTaxiNameIdTouched);
        Assert.Null(vm.TaxiNameId);
        Assert.Null(vm.LeftEdgeLighted);
        Assert.Null(vm.RightEdgeLighted);
    }
}
