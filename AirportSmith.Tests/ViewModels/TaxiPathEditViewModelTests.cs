using AirportSmith.Models;
using AirportSmith.ViewModels;

namespace AirportSmith.Tests.ViewModels;

public class TaxiPathEditViewModelTests
{
    [Fact]
    public void SettingProperties_WritesThroughToWrappedSegment()
    {
        var nameId = Guid.NewGuid();
        var segment = new TaxiPathSegment { TaxiNameId = null, LeftEdgeLighted = false, RightEdgeLighted = false };
        var vm = new TaxiPathEditViewModel(segment);

        vm.TaxiNameId = nameId;
        vm.LeftEdgeLighted = true;
        vm.RightEdgeLighted = true;

        Assert.Equal(nameId, segment.TaxiNameId);
        Assert.True(segment.LeftEdgeLighted);
        Assert.True(segment.RightEdgeLighted);
    }

    [Fact]
    public void SettingProperty_RaisesPropertyChanged()
    {
        var vm = new TaxiPathEditViewModel(new TaxiPathSegment());
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.TaxiNameId = Guid.NewGuid();
        vm.LeftEdgeLighted = true;

        Assert.Contains(nameof(TaxiPathEditViewModel.TaxiNameId), raised);
        Assert.Contains(nameof(TaxiPathEditViewModel.LeftEdgeLighted), raised);
    }

    [Fact]
    public void SettingSameValue_DoesNotRaisePropertyChanged()
    {
        var nameId = Guid.NewGuid();
        var vm = new TaxiPathEditViewModel(new TaxiPathSegment { TaxiNameId = nameId });
        var raised = false;
        vm.PropertyChanged += (_, _) => raised = true;

        vm.TaxiNameId = nameId;

        Assert.False(raised);
    }

    [Fact]
    public void ClearTaxiNameIfReferencing_MatchingId_ClearsTaxiNameId()
    {
        var nameId = Guid.NewGuid();
        var vm = new TaxiPathEditViewModel(new TaxiPathSegment { TaxiNameId = nameId });

        vm.ClearTaxiNameIfReferencing(nameId);

        Assert.Null(vm.TaxiNameId);
    }

    [Fact]
    public void ClearTaxiNameIfReferencing_DifferentId_LeavesTaxiNameIdUnchanged()
    {
        var nameId = Guid.NewGuid();
        var vm = new TaxiPathEditViewModel(new TaxiPathSegment { TaxiNameId = nameId });

        vm.ClearTaxiNameIfReferencing(Guid.NewGuid());

        Assert.Equal(nameId, vm.TaxiNameId);
    }
}
