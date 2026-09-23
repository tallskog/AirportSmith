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

    // Independent of PrimarySystemType/SecondarySystemType (see
    // Runway.PrimaryApproachLightsStrobeCount's own doc comment) — settable
    // and readable with no approach light system chosen at all, unlike
    // SettingSystemTypeToNull_ClearsApproachLightSystemRecord above (which
    // NEEDS a SystemType to exist as a host).
    [Fact]
    public void SettingApproachLightExtras_WritesThroughToWrappedRunway_WithNoSystemTypeChosen()
    {
        var runway = new Runway();
        var vm = new RunwayEditViewModel(runway);
        Assert.Null(vm.PrimarySystemType);

        vm.PrimaryApproachLightsStrobeCount = 5;
        vm.PrimaryApproachLightsHasEndLights = true;
        vm.PrimaryApproachLightsHasReilLights = true;
        vm.PrimaryApproachLightsHasTouchdownLights = true;
        vm.SecondaryApproachLightsStrobeCount = 3;
        vm.SecondaryApproachLightsHasEndLights = true;
        vm.SecondaryApproachLightsHasReilLights = true;
        vm.SecondaryApproachLightsHasTouchdownLights = true;

        Assert.Null(vm.PrimarySystemType); // still not set — these are independent
        Assert.Equal(5, runway.PrimaryApproachLightsStrobeCount);
        Assert.True(runway.PrimaryApproachLightsHasEndLights);
        Assert.True(runway.PrimaryApproachLightsHasReilLights);
        Assert.True(runway.PrimaryApproachLightsHasTouchdownLights);
        Assert.Equal(3, runway.SecondaryApproachLightsStrobeCount);
        Assert.True(runway.SecondaryApproachLightsHasEndLights);
        Assert.True(runway.SecondaryApproachLightsHasReilLights);
        Assert.True(runway.SecondaryApproachLightsHasTouchdownLights);
        Assert.Equal(5, vm.PrimaryApproachLightsStrobeCount);
        Assert.True(vm.PrimaryApproachLightsHasEndLights);
    }

    [Fact]
    public void SettingApproachLightExtras_RaisesPropertyChanged()
    {
        var vm = new RunwayEditViewModel(new Runway());
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.PrimaryApproachLightsStrobeCount = 5;
        vm.PrimaryApproachLightsHasEndLights = true;
        vm.PrimaryApproachLightsHasReilLights = true;
        vm.PrimaryApproachLightsHasTouchdownLights = true;

        Assert.Contains(nameof(RunwayEditViewModel.PrimaryApproachLightsStrobeCount), raised);
        Assert.Contains(nameof(RunwayEditViewModel.PrimaryApproachLightsHasEndLights), raised);
        Assert.Contains(nameof(RunwayEditViewModel.PrimaryApproachLightsHasReilLights), raised);
        Assert.Contains(nameof(RunwayEditViewModel.PrimaryApproachLightsHasTouchdownLights), raised);
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

    [Fact]
    public void EnablingVasiFromNone_SuggestsDefaultPosition300MetersFromThreshold()
    {
        var runway = new Runway();
        var vm = new RunwayEditViewModel(runway);

        vm.PrimaryLeftVasiType = VasiType.Papi4;

        Assert.Equal(0, runway.PrimaryLeftVasiBiasXMeters);
        Assert.Equal(300, runway.PrimaryLeftVasiBiasZMeters);
        Assert.Equal(15, runway.PrimaryLeftVasiSpacingMeters);
    }

    [Fact]
    public void ChangingVasiTypeOnAlreadyPositionedSlot_DoesNotOverwritePosition()
    {
        var runway = new Runway
        {
            PrimaryLeftVasiType = VasiType.Vasi21,
            PrimaryLeftVasiBiasXMeters = 42,
            PrimaryLeftVasiBiasZMeters = 123,
            PrimaryLeftVasiSpacingMeters = 7,
        };
        var vm = new RunwayEditViewModel(runway);

        // Re-picking a different type on an already-positioned slot (e.g.
        // sim-extracted data) must never clobber the real position with the
        // "new install" defaults.
        vm.PrimaryLeftVasiType = VasiType.Papi2;

        Assert.Equal(42, runway.PrimaryLeftVasiBiasXMeters);
        Assert.Equal(123, runway.PrimaryLeftVasiBiasZMeters);
        Assert.Equal(7, runway.PrimaryLeftVasiSpacingMeters);
    }

    [Fact]
    public void ClearingVasiTypeToNull_DoesNotApplyDefaultPosition()
    {
        var runway = new Runway { PrimaryLeftVasiType = VasiType.Papi4 };
        var vm = new RunwayEditViewModel(runway);

        vm.PrimaryLeftVasiType = null;

        Assert.Null(runway.PrimaryLeftVasiBiasXMeters);
        Assert.Null(runway.PrimaryLeftVasiBiasZMeters);
        Assert.Null(runway.PrimaryLeftVasiSpacingMeters);
    }

    [Fact]
    public void EnablingVasiFromNone_AppliesDefaultsForPrimaryRightSlot()
    {
        var runway = new Runway();
        var vm = new RunwayEditViewModel(runway);

        vm.PrimaryRightVasiType = VasiType.Papi2;

        Assert.Equal(0, runway.PrimaryRightVasiBiasXMeters);
        Assert.Equal(300, runway.PrimaryRightVasiBiasZMeters);
        Assert.Equal(15, runway.PrimaryRightVasiSpacingMeters);
    }

    [Fact]
    public void EnablingVasiFromNone_AppliesDefaultsForSecondaryLeftSlot()
    {
        var runway = new Runway();
        var vm = new RunwayEditViewModel(runway);

        vm.SecondaryLeftVasiType = VasiType.Papi2;

        Assert.Equal(0, runway.SecondaryLeftVasiBiasXMeters);
        Assert.Equal(300, runway.SecondaryLeftVasiBiasZMeters);
        Assert.Equal(15, runway.SecondaryLeftVasiSpacingMeters);
    }

    [Fact]
    public void EnablingVasiFromNone_AppliesDefaultsForSecondaryRightSlot()
    {
        var runway = new Runway();
        var vm = new RunwayEditViewModel(runway);

        vm.SecondaryRightVasiType = VasiType.Papi2;

        Assert.Equal(0, runway.SecondaryRightVasiBiasXMeters);
        Assert.Equal(300, runway.SecondaryRightVasiBiasZMeters);
        Assert.Equal(15, runway.SecondaryRightVasiSpacingMeters);
    }

    [Fact]
    public void SettingBiasAndSpacingProperties_WriteThroughToWrappedRunway()
    {
        var runway = new Runway();
        var vm = new RunwayEditViewModel(runway);

        vm.SecondaryRightVasiBiasXMeters = -5;
        vm.SecondaryRightVasiBiasZMeters = 250;
        vm.SecondaryRightVasiSpacingMeters = 9;

        Assert.Equal(-5, runway.SecondaryRightVasiBiasXMeters);
        Assert.Equal(250, runway.SecondaryRightVasiBiasZMeters);
        Assert.Equal(9, runway.SecondaryRightVasiSpacingMeters);
    }
}
