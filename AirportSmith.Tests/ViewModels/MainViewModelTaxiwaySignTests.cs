using AirportSmith.Models;
using AirportSmith.Models.Diagram;
using AirportSmith.Tests.Fakes;
using AirportSmith.ViewModels;

namespace AirportSmith.Tests.ViewModels;

// The Edit tab's Taxiway Signs workflow (MainViewModel.TaxiwaySigns.cs).
// Airport: runway 09/27 along the X axis, taxiway A coming from the north
// (0,200) to a hold-short at (0,80) and on to the runway, and one parking spot.
public class MainViewModelTaxiwaySignTests
{
    private static AirportDetails Airport()
    {
        var a = new TaxiName { Value = "A" };
        var airport = new AirportDetails { Icao = "TEST", Latitude = 60, Longitude = 25 };
        airport.TaxiNames.Add(a);
        airport.Runways.Add(new Runway
        {
            PrimaryDesignation = "09", SecondaryDesignation = "27",
            Latitude = 60, Longitude = 25, HeadingDeg = 90, LengthMeters = 2000, WidthMeters = 45,
        });
        airport.TaxiPaths.Add(new TaxiPathSegment
        {
            Type = TaxiPathType.Taxi, StartIndex = 1, EndIndex = 2, WidthMeters = 20, TaxiNameId = a.Id,
            StartXMeters = 0, StartZMeters = 200, StartPointType = TaxiPointType.Normal,
            EndXMeters = 0, EndZMeters = 80, EndPointType = TaxiPointType.HoldShort,
        });
        airport.TaxiPaths.Add(new TaxiPathSegment
        {
            Type = TaxiPathType.Taxi, StartIndex = 2, EndIndex = 3, WidthMeters = 20, TaxiNameId = a.Id,
            StartXMeters = 0, StartZMeters = 80, StartPointType = TaxiPointType.HoldShort,
            EndXMeters = 0, EndZMeters = 0, EndPointType = TaxiPointType.Normal,
        });
        airport.ParkingSpots.Add(new TaxiParkingSpot { ItemIndex = 0, Number = 1, BiasXMeters = 300, BiasZMeters = 300, RadiusMeters = 15 });
        return airport;
    }

    private static (MainViewModel Vm, FakeConfirmationService Confirm) Create(AirportDetails? airport = null)
    {
        airport ??= Airport();
        var store = new FakeAirportProjectStore();
        store.Save(airport);
        var confirm = new FakeConfirmationService();
        var vm = new MainViewModel(new FakeSimConnectService(), projectStore: store, confirmationService: confirm) { IcaoInput = airport.Icao };
        vm.LoadProjectCommand.Execute(null);
        return (vm, confirm);
    }

    private static Point2D Screen(MainViewModel vm, double x, double z) =>
        new(x + vm.Diagram!.OriginXMeters, vm.Diagram.OriginZMeters - z);

    [Fact]
    public void AddSign_ThenDiagramClick_CreatesManualSignForTheNearestTaxiway()
    {
        var (vm, _) = Create();

        vm.AddTaxiwaySignCommand.Execute(null);
        Assert.True(vm.IsTaxiwaySignPlacementArmed);
        Assert.NotNull(vm.TaxiwaySignPlacementStatusText);
        vm.PlaceTaxiwaySignCommand.Execute(Screen(vm, 5, 150));

        var sign = Assert.Single(vm.Airport!.TaxiwaySigns);
        Assert.Equal("l[A]", sign.Label);
        Assert.Equal(270, sign.HeadingDeg, 1);
        Assert.Equal(5, sign.XMeters, 2);
        Assert.Equal(150, sign.ZMeters, 2);
        Assert.False(sign.IsAutoPlaced);
        Assert.Same(sign, Assert.Single(vm.TaxiwaySignEdits).Sign);
        Assert.Equal(sign.Id, Assert.Single(vm.Diagram!.TaxiwaySigns).SignId);
        Assert.False(vm.IsTaxiwaySignPlacementArmed);
        Assert.False(vm.PlaceTaxiwaySignCommand.CanExecute(Screen(vm, 0, 0)));
    }

    [Fact]
    public void AddSign_AwayFromTaxiways_StartsWithAnEmptyInvalidLabel()
    {
        var (vm, _) = Create();

        vm.AddTaxiwaySignCommand.Execute(null);
        vm.PlaceTaxiwaySignCommand.Execute(Screen(vm, 900, 900));

        Assert.Equal("", Assert.Single(vm.Airport!.TaxiwaySigns).Label);
        Assert.True(vm.TaxiwaySignEdits[0].HasLabelError);
        Assert.False(vm.Diagram!.TaxiwaySigns[0].IsValid);
    }

    [Fact]
    public void RowPlace_MovesTheSign_AndItsDiagramShape()
    {
        var (vm, _) = Create();
        vm.AutoPlaceTaxiwaySignsCommand.Execute(null);
        var edit = vm.TaxiwaySignEdits[0];
        var shape = vm.Diagram!.TaxiwaySigns.Single(s => s.SignId == edit.Sign.Id);

        vm.ArmTaxiwaySignPlacementCommand.Execute(edit);
        var target = Screen(vm, 50, 60);
        vm.PlaceTaxiwaySignCommand.Execute(target);

        Assert.Equal(50, edit.XMeters, 2);
        Assert.Equal(60, edit.ZMeters, 2);
        Assert.Equal(target.X, shape.Center.X, 2);
        Assert.Equal(target.Y, shape.Center.Y, 2);
        Assert.False(edit.IsAutoPlaced);
    }

    [Fact]
    public void EditingALabel_UpdatesTheDiagramShape()
    {
        var (vm, _) = Create();
        vm.AutoPlaceTaxiwaySignsCommand.Execute(null);
        var edit = vm.TaxiwaySignEdits[0];
        var shape = vm.Diagram!.TaxiwaySigns.Single(s => s.SignId == edit.Sign.Id);

        edit.Label = "l[A]d[B>]m";
        Assert.False(shape.IsValid);

        edit.Label = "l[A]d[B>]";
        Assert.True(shape.IsValid);
        Assert.Equal(2, shape.Segments.Count);
    }

    [Fact]
    public void AutoPlace_ReRun_ReplacesAutoSignsAndKeepsEditedOnes()
    {
        var (vm, confirm) = Create();

        vm.AutoPlaceTaxiwaySignsCommand.Execute(null);
        var firstRun = vm.Airport!.TaxiwaySigns.ToList();
        Assert.NotEmpty(firstRun);
        Assert.All(firstRun, s => Assert.True(s.IsAutoPlaced));
        Assert.Equal(0, confirm.CallCount); // nothing to replace yet
        Assert.NotNull(vm.TaxiwaySignStatus);

        var edited = vm.TaxiwaySignEdits[0];
        edited.Label = "l[A]";

        vm.AutoPlaceTaxiwaySignsCommand.Execute(null);

        Assert.Equal(1, confirm.CallCount);
        Assert.Contains($"{firstRun.Count - 1} auto-placed", confirm.LastMessage);
        Assert.Contains(edited.Sign, vm.Airport.TaxiwaySigns);
        Assert.Equal(firstRun.Count + 1, vm.Airport.TaxiwaySigns.Count);
        Assert.DoesNotContain(vm.Airport.TaxiwaySigns, s => s.IsAutoPlaced && firstRun.Contains(s));
        Assert.Equal(vm.Airport.TaxiwaySigns.Count, vm.Diagram!.TaxiwaySigns.Count);
    }

    [Fact]
    public void AutoPlace_ConfirmationDeclined_LeavesSignsUnchanged()
    {
        var (vm, confirm) = Create();
        vm.AutoPlaceTaxiwaySignsCommand.Execute(null);
        var before = vm.Airport!.TaxiwaySigns.ToList();
        confirm.ConfirmResult = false;

        vm.AutoPlaceTaxiwaySignsCommand.Execute(null);

        Assert.Equal(before, vm.Airport.TaxiwaySigns);
    }

    [Fact]
    public void GenerateForSelectedPoints_OnlySignsTheSelectedPoint()
    {
        var (vm, _) = Create();
        Assert.False(vm.GenerateTaxiwaySignsForSelectedPointsCommand.CanExecute(null));

        var hold = vm.Diagram!.TaxiwayPoints.Single(p => p.Index == 2);
        vm.ToggleTaxiwayPointSelectionCommand.Execute(new TaxiwayPointSelectionRequest(hold, false));
        Assert.True(vm.GenerateTaxiwaySignsForSelectedPointsCommand.CanExecute(null));
        vm.GenerateTaxiwaySignsForSelectedPointsCommand.Execute(null);

        var sign = Assert.Single(vm.Airport!.TaxiwaySigns);
        Assert.Equal(2, sign.AnchorPointIndex);
        Assert.Equal("l[A]m[27-09]", sign.Label);
    }

    [Fact]
    public void ClickingASign_FiltersTheGrid_AndDeleteRemovesIt()
    {
        var (vm, confirm) = Create();
        vm.AutoPlaceTaxiwaySignsCommand.Execute(null);
        var count = vm.Airport!.TaxiwaySigns.Count;
        var shape = vm.Diagram!.TaxiwaySigns[0];

        vm.ToggleTaxiwaySignSelectionCommand.Execute(new TaxiwaySignSelectionRequest(shape, false));
        Assert.Equal(shape.SignId, Assert.Single(vm.VisibleTaxiwaySignEdits).Sign.Id);

        vm.DeleteSelectedCommand.Execute(null);

        Assert.Equal(1, confirm.CallCount);
        Assert.Equal(count - 1, vm.Airport.TaxiwaySigns.Count);
        Assert.DoesNotContain(vm.Airport.TaxiwaySigns, s => s.Id == shape.SignId);
        Assert.DoesNotContain(vm.Diagram.TaxiwaySigns, s => s.SignId == shape.SignId);
        Assert.Equal(count - 1, vm.VisibleTaxiwaySignEdits.Count);
    }

    [Fact]
    public void DeleteSigns_Declined_KeepsThem()
    {
        var (vm, confirm) = Create();
        vm.AutoPlaceTaxiwaySignsCommand.Execute(null);
        var count = vm.Airport!.TaxiwaySigns.Count;
        vm.ToggleTaxiwaySignSelectionCommand.Execute(new TaxiwaySignSelectionRequest(vm.Diagram!.TaxiwaySigns[0], false));
        confirm.ConfirmResult = false;

        vm.DeleteSelectedCommand.Execute(null);

        Assert.Equal(count, vm.Airport.TaxiwaySigns.Count);
    }

    [Fact]
    public void HideAll_HidesAndDeselectsSigns()
    {
        var (vm, _) = Create();
        vm.AutoPlaceTaxiwaySignsCommand.Execute(null);
        vm.ToggleTaxiwaySignSelectionCommand.Execute(new TaxiwaySignSelectionRequest(vm.Diagram!.TaxiwaySigns[0], false));

        vm.HideAllTaxiwaySignsCommand.Execute(null);

        Assert.All(vm.Diagram.TaxiwaySigns, s => Assert.False(s.IsVisible || s.IsSelected));
        Assert.Equal(vm.TaxiwaySignEdits.Count, vm.VisibleTaxiwaySignEdits.Count);
        Assert.All(vm.TaxiwaySignEdits, e => Assert.True(e.IsHiddenFromDiagram));
        Assert.False(vm.HideAllTaxiwaySignsCommand.CanExecute(null));
        Assert.True(vm.ShowAllTaxiwaySignsCommand.CanExecute(null));

        vm.ShowAllTaxiwaySignsCommand.Execute(null);

        Assert.All(vm.Diagram.TaxiwaySigns, s => Assert.True(s.IsVisible));
        Assert.False(vm.ShowAllTaxiwaySignsCommand.CanExecute(null));
    }

    // Signs for this group: l[A] near the taxiway, l[B] and a runway-only
    // m[09-27] elsewhere, d[B<]l[A]d[C>] showing three names.
    private static (MainViewModel Vm, Guid A, Guid B, Guid Runway, Guid Array) CreateWithHandMadeSigns()
    {
        var airport = Airport();
        var b = new TaxiName { Value = "B" };
        airport.TaxiNames.Add(b);
        var signA = new TaxiwaySign { Label = "l[A]", XMeters = 10, ZMeters = 100 };
        var signB = new TaxiwaySign { Label = "l[B]", XMeters = 20, ZMeters = 100 };
        var signRunway = new TaxiwaySign { Label = "m[09-27]", XMeters = 30, ZMeters = 100 };
        var signArray = new TaxiwaySign { Label = "d[B<]l[A]d[C>]", XMeters = 40, ZMeters = 100 };
        airport.TaxiwaySigns.AddRange([signA, signB, signRunway, signArray]);
        var (vm, _) = Create(airport);
        return (vm, signA.Id, signB.Id, signRunway.Id, signArray.Id);
    }

    private static bool IsSignVisible(MainViewModel vm, Guid id) => vm.Diagram!.TaxiwaySigns.Single(s => s.SignId == id).IsVisible;

    [Fact]
    public void HidingOneSign_HidesOnlyThatSign_WithoutMakingItAManualEdit()
    {
        var (vm, _) = Create();
        vm.AutoPlaceTaxiwaySignsCommand.Execute(null);
        var edit = vm.TaxiwaySignEdits[0];

        edit.IsHiddenFromDiagram = true;

        Assert.False(IsSignVisible(vm, edit.Sign.Id));
        Assert.All(vm.TaxiwaySignEdits.Skip(1), e => Assert.True(IsSignVisible(vm, e.Sign.Id)));
        Assert.True(edit.Sign.IsAutoPlaced);

        edit.IsHiddenFromDiagram = false;

        Assert.True(IsSignVisible(vm, edit.Sign.Id));
    }

    [Fact]
    public void HidingATaxiName_HidesEverySignShowingIt_IncludingSignArrays()
    {
        var (vm, a, b, runway, array) = CreateWithHandMadeSigns();

        vm.TaxiNames.Single(n => n.Value == "A").IsHiddenFromDiagram = true;

        Assert.False(IsSignVisible(vm, a));
        Assert.False(IsSignVisible(vm, array)); // any of its names hidden
        Assert.True(IsSignVisible(vm, b));
        Assert.True(IsSignVisible(vm, runway));
        Assert.All(vm.TaxiwaySignEdits, e => Assert.False(e.IsHiddenFromDiagram)); // own Hide untouched

        vm.TaxiNames.Single(n => n.Value == "A").IsHiddenFromDiagram = false;

        Assert.True(IsSignVisible(vm, a));
        Assert.True(IsSignVisible(vm, array));
    }

    [Fact]
    public void TaxiNamesHideAll_HidesNamedSigns_ButNotRunwayOnlySigns()
    {
        var (vm, a, b, runway, array) = CreateWithHandMadeSigns();

        vm.HideAllTaxiNamesCommand.Execute(null);

        Assert.False(IsSignVisible(vm, a));
        Assert.False(IsSignVisible(vm, b));
        Assert.False(IsSignVisible(vm, array));
        Assert.True(IsSignVisible(vm, runway));
    }

    [Fact]
    public void ShowAllSigns_KeepsSignsOfHiddenTaxiNamesHidden()
    {
        var (vm, a, b, _, _) = CreateWithHandMadeSigns();
        vm.HideAllTaxiwaySignsCommand.Execute(null);
        vm.TaxiNames.Single(n => n.Value == "A").IsHiddenFromDiagram = true;

        vm.ShowAllTaxiwaySignsCommand.Execute(null);

        Assert.False(IsSignVisible(vm, a));
        Assert.True(IsSignVisible(vm, b));
    }

    [Fact]
    public void EditingALabel_OrRenamingAHiddenName_UpdatesWhichSignsAreHidden()
    {
        var (vm, a, b, _, _) = CreateWithHandMadeSigns();
        var nameA = vm.TaxiNames.Single(n => n.Value == "A");
        nameA.IsHiddenFromDiagram = true;

        vm.TaxiwaySignEdits.Single(e => e.Sign.Id == b).Label = "l[A]";
        Assert.False(IsSignVisible(vm, b));

        nameA.Value = "Z";
        Assert.True(IsSignVisible(vm, a));
        Assert.True(IsSignVisible(vm, b));
    }

    [Fact]
    public void AutoPlacement_KeepsAHiddenManualSignHidden()
    {
        var (vm, _, b, _, _) = CreateWithHandMadeSigns();
        vm.TaxiwaySignEdits.Single(e => e.Sign.Id == b).IsHiddenFromDiagram = true;

        vm.AutoPlaceTaxiwaySignsCommand.Execute(null);

        Assert.True(vm.TaxiwaySignEdits.Single(e => e.Sign.Id == b).IsHiddenFromDiagram);
        Assert.False(IsSignVisible(vm, b));
    }

    [Fact]
    public void DeletingAHiddenTaxiName_ShowsItsSignsAgain()
    {
        var (vm, a, _, _, _) = CreateWithHandMadeSigns();
        var nameA = vm.TaxiNames.Single(n => n.Value == "A");
        nameA.IsHiddenFromDiagram = true;

        vm.DeleteTaxiNameCommand.Execute(nameA);

        Assert.True(IsSignVisible(vm, a));
    }

    [Fact]
    public void AddingOrDeletingSigns_KeepsTheOtherSignsHidden()
    {
        var (vm, a, b, _, _) = CreateWithHandMadeSigns();
        vm.TaxiwaySignEdits.Single(e => e.Sign.Id == a).IsHiddenFromDiagram = true;
        vm.ToggleTaxiwaySignSelectionCommand.Execute(new TaxiwaySignSelectionRequest(vm.Diagram!.TaxiwaySigns.Single(s => s.SignId == b), false));

        vm.DeleteSelectedTaxiwaySignsCommand.Execute(null);
        vm.AddTaxiwaySignCommand.Execute(null);
        vm.PlaceTaxiwaySignCommand.Execute(Screen(vm, 5, 150));

        Assert.True(vm.TaxiwaySignEdits.Single(e => e.Sign.Id == a).IsHiddenFromDiagram);
        Assert.False(IsSignVisible(vm, a));
        Assert.True(IsSignVisible(vm, vm.TaxiwaySignEdits[^1].Sign.Id)); // the new one
    }

    [Fact]
    public void LoadingAProjectWithSigns_BuildsRowsAndShapes()
    {
        var airport = Airport();
        airport.TaxiwaySigns.Add(new TaxiwaySign { Label = "l[A]", XMeters = 10, ZMeters = 100 });
        airport.TaxiwaySigns.Add(new TaxiwaySign { Label = "m[09-27]", XMeters = 20, ZMeters = 100 });

        var (vm, _) = Create(airport);

        Assert.Equal(2, vm.TaxiwaySignEdits.Count);
        Assert.Equal(2, vm.VisibleTaxiwaySignEdits.Count);
        Assert.Equal(airport.TaxiwaySigns.Select(s => s.Id), vm.Diagram!.TaxiwaySigns.Select(s => s.SignId));
    }

    [Fact]
    public void ArmingASignPlacement_DisarmsParkingPlacement_AndViceVersa()
    {
        var (vm, _) = Create();

        vm.ArmParkingPlacementCommand.Execute(vm.ParkingSpotEdits[0]);
        vm.AddTaxiwaySignCommand.Execute(null);
        Assert.False(vm.IsParkingPlacementArmed);
        Assert.True(vm.IsTaxiwaySignPlacementArmed);

        vm.ArmParkingPlacementCommand.Execute(vm.ParkingSpotEdits[0]);
        Assert.True(vm.IsParkingPlacementArmed);
        Assert.False(vm.IsTaxiwaySignPlacementArmed);
    }

    [Fact]
    public void ClearingTheDiagramSelection_AlsoClearsSignSelection()
    {
        var (vm, _) = Create();
        vm.AutoPlaceTaxiwaySignsCommand.Execute(null);
        vm.ToggleTaxiwaySignSelectionCommand.Execute(new TaxiwaySignSelectionRequest(vm.Diagram!.TaxiwaySigns[0], false));

        vm.ClearTaxiwaySelectionCommand.Execute(null);

        Assert.DoesNotContain(vm.Diagram.TaxiwaySigns, s => s.IsSelected);
        Assert.Equal(vm.TaxiwaySignEdits.Count, vm.VisibleTaxiwaySignEdits.Count);
    }
}
