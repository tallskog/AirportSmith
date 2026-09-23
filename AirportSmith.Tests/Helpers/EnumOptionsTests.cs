using AirportSmith.Helpers;
using AirportSmith.Models;

namespace AirportSmith.Tests.Helpers;

public class EnumOptionsTests
{
    // Confirms the user-requested informal-name suffixes for CALVERT/
    // CALVERT2 (the two systems whose plain enum name reads ambiguously on
    // its own — see EnumOptions.ApproachLightSystemLabel's own doc comment)
    // without disturbing every other system's plain name, and that the
    // leading null entry (for clearing a runway's approach light system
    // back to "not installed") still displays blank, same as the old
    // plain-enum ComboBox's ToString()-based null display.
    [Fact]
    public void ApproachLightSystemTypeOptions_CalvertAndCalvert2_HaveInformalNameSuffix()
    {
        var options = EnumOptions.ApproachLightSystemTypeOptions;

        Assert.Equal("Calvert (PALS)", options.Single(o => o.Value == ApproachLightSystemType.Calvert).Label);
        Assert.Equal("Calvert2 (PALS CAT II)", options.Single(o => o.Value == ApproachLightSystemType.Calvert2).Label);
        Assert.Equal(string.Empty, options.Single(o => o.Value == null).Label);
    }

    [Fact]
    public void ApproachLightSystemTypeOptions_EveryOtherSystem_LabelIsPlainEnumName()
    {
        var options = EnumOptions.ApproachLightSystemTypeOptions;

        foreach (var type in Enum.GetValues<ApproachLightSystemType>())
        {
            if (type is ApproachLightSystemType.Calvert or ApproachLightSystemType.Calvert2) continue;
            Assert.Equal(type.ToString(), options.Single(o => o.Value == type).Label);
        }
    }

    [Fact]
    public void ApproachLightSystemTypeOptions_OneEntryPerEnumValue_PlusLeadingNull()
    {
        Assert.Equal(Enum.GetValues<ApproachLightSystemType>().Length + 1, EnumOptions.ApproachLightSystemTypeOptions.Count);
    }
}
