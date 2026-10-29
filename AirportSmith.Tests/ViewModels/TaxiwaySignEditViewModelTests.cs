using AirportSmith.Models;
using AirportSmith.ViewModels;

namespace AirportSmith.Tests.ViewModels;

public class TaxiwaySignEditViewModelTests
{
    private static TaxiwaySign AutoSign() => new() { Label = "l[A]", IsAutoPlaced = true, XMeters = 1, ZMeters = 2, HeadingDeg = 90 };

    public static TheoryData<Action<TaxiwaySignEditViewModel>> Edits => new()
    {
        vm => vm.Label = "l[B]",
        vm => vm.HeadingDeg = 180,
        vm => vm.Size = TaxiwaySignSize.Size1,
        vm => vm.Justification = TaxiwaySignJustification.Right,
        vm => vm.XMeters = 10,
        vm => vm.ZMeters = 10,
        vm => vm.MoveTo(5, 6),
    };

    [Theory]
    [MemberData(nameof(Edits))]
    public void AnyEdit_WritesThroughAndClearsAutoPlaced(Action<TaxiwaySignEditViewModel> edit)
    {
        var sign = AutoSign();
        var vm = new TaxiwaySignEditViewModel(sign);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        edit(vm);

        Assert.False(sign.IsAutoPlaced);
        Assert.False(vm.IsAutoPlaced);
        Assert.Contains(nameof(TaxiwaySignEditViewModel.IsAutoPlaced), raised);
    }

    [Fact]
    public void SettingTheSameValue_KeepsAutoPlaced()
    {
        var sign = AutoSign();
        var vm = new TaxiwaySignEditViewModel(sign);

        vm.Label = "l[A]";
        vm.HeadingDeg = 90;
        vm.MoveTo(1, 2);

        Assert.True(sign.IsAutoPlaced);
    }

    [Fact]
    public void Label_UpdatesErrorAndPreview()
    {
        var vm = new TaxiwaySignEditViewModel(new TaxiwaySign { Label = "l[A]" });
        Assert.False(vm.HasLabelError);
        Assert.Single(vm.Segments);

        vm.Label = "l[A]m";

        Assert.True(vm.HasLabelError);
        Assert.NotNull(vm.LabelError);
    }
}
