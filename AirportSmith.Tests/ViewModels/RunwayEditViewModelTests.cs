using AirportSmith.Models;
using AirportSmith.ViewModels;

namespace AirportSmith.Tests.ViewModels;

public class RunwayEditViewModelTests
{
    [Fact]
    public void SettingProperties_WritesThroughToWrappedRunway()
    {
        var runway = new Runway { PrimaryDesignation = "04L", SecondaryDesignation = "22R" };
        var vm = new RunwayEditViewModel(runway);

        vm.EdgeLightIntensity = RunwayLightIntensity.Medium;
        vm.PrimaryLeftVasiType = VasiType.Papi4;
        vm.PrimaryLeftVasiAngleDeg = 3.2;

        Assert.Equal(RunwayLightIntensity.Medium, runway.EdgeLightIntensity);
        Assert.Equal(VasiType.Papi4, runway.PrimaryLeftVasiType);
        Assert.Equal(3.2, runway.PrimaryLeftVasiAngleDeg);
        Assert.Equal("04L", vm.PrimaryDesignation);
        Assert.Equal("22R", vm.SecondaryDesignation);
    }

    [Fact]
    public void SettingSystemType_CreatesApproachLightSystemRecord()
    {
        var runway = new Runway();
        var vm = new RunwayEditViewModel(runway);

        vm.PrimarySystemType = ApproachLightSystemType.Alsf2;

        Assert.NotNull(runway.PrimaryApproachLights);
        Assert.Equal(ApproachLightSystemType.Alsf2, runway.PrimaryApproachLights!.SystemType);
        Assert.Equal(ApproachLightSystemType.Alsf2, vm.PrimarySystemType);
    }

    [Fact]
    public void SettingSystemTypeToNull_ClearsApproachLightSystemRecord()
    {
        var runway = new Runway { PrimaryApproachLights = new ApproachLightSystem(ApproachLightSystemType.Mals) };
        var vm = new RunwayEditViewModel(runway);

        vm.PrimarySystemType = null;

        Assert.Null(runway.PrimaryApproachLights);
        Assert.Null(vm.PrimarySystemType);
    }

    [Fact]
    public void SettingProperty_RaisesPropertyChanged()
    {
        var vm = new RunwayEditViewModel(new Runway());
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.EdgeLightIntensity = RunwayLightIntensity.Low;
        vm.SecondarySystemType = ApproachLightSystemType.Odals;

        Assert.Contains(nameof(RunwayEditViewModel.EdgeLightIntensity), raised);
        Assert.Contains(nameof(RunwayEditViewModel.SecondarySystemType), raised);
    }
}
