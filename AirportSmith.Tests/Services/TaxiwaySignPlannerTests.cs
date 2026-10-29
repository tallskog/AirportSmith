using AirportSmith.Models;
using AirportSmith.Services;

namespace AirportSmith.Tests.Services;

// Small hand-built airports in the local-meters plane (X east, Z north). The
// runway, when present, is "09/27": heading 90, 2000 m x 45 m, centered on
// the airport reference point, so it runs along the X axis from x=-1000
// (09's threshold) to x=+1000 (27's threshold).
public class TaxiwaySignPlannerTests
{
    private const double Width = 20; // taxi path width; signs stand 6 m beyond its edge => 16 m from centerline

    private sealed class Builder
    {
        public AirportDetails Airport { get; } = new() { Icao = "TEST", Latitude = 60, Longitude = 25 };
        private readonly Dictionary<int, (double X, double Z, TaxiPointType? Type)> _points = [];

        public Builder WithRunway()
        {
            Airport.Runways.Add(new Runway
            {
                PrimaryDesignation = "09", SecondaryDesignation = "27",
                Latitude = 60, Longitude = 25, HeadingDeg = 90, LengthMeters = 2000, WidthMeters = 45,
            });
            return this;
        }

        public Builder Point(int index, double x, double z, TaxiPointType? type = TaxiPointType.Normal)
        {
            _points[index] = (x, z, type);
            return this;
        }

        public Builder Path(int from, int to, string? name, TaxiPathType type = TaxiPathType.Taxi)
        {
            Guid? nameId = null;
            if (name != null)
            {
                var existing = Airport.TaxiNames.FirstOrDefault(n => n.Value == name);
                if (existing is null) Airport.TaxiNames.Add(existing = new TaxiName { Value = name });
                nameId = existing.Id;
            }
            var a = _points[from];
            var b = _points[to];
            Airport.TaxiPaths.Add(new TaxiPathSegment
            {
                Type = type, StartIndex = from, EndIndex = to, WidthMeters = Width, TaxiNameId = nameId,
                StartXMeters = a.X, StartZMeters = a.Z, StartPointType = a.Type,
                EndXMeters = b.X, EndZMeters = b.Z, EndPointType = b.Type,
            });
            return this;
        }
    }

    private static void AssertAt(TaxiwaySign sign, double x, double z, double heading)
    {
        Assert.Equal(x, sign.XMeters, 1);
        Assert.Equal(z, sign.ZMeters, 1);
        Assert.Equal(heading, sign.HeadingDeg, 1);
    }

    // ---- Hold-short ---------------------------------------------------

    // Taxiway A comes from the north (point 1) to a hold at (0, 80), then on
    // to the runway; taxiway B comes from the south to a hold at (300, -80).
    private static AirportDetails HoldShortAirport(TaxiPointType holdType = TaxiPointType.HoldShort) => new Builder()
        .WithRunway()
        .Point(1, 0, 200).Point(2, 0, 80, holdType).Point(3, 0, 0)
        .Point(4, 300, -200).Point(5, 300, -80, holdType).Point(6, 300, 0)
        .Path(1, 2, "A").Path(2, 3, "A")
        .Path(4, 5, "B").Path(5, 6, "B")
        .Airport;

    [Fact]
    public void Plan_HoldShort_LocationOutboardOfRunwaySign_ReadersLeftRunwayEndFirst()
    {
        var signs = TaxiwaySignPlanner.Plan(HoldShortAirport()).Signs.Where(s => s.Label.Contains("m[")).ToList();

        // Southbound on A: its left is east, where the 27 end is.
        var a = Assert.Single(signs, s => s.AnchorPointIndex == 2);
        Assert.Equal("l[A]m[27-09]", a.Label);
        AssertAt(a, 16, 80, 180);
        Assert.Equal(TaxiwaySignJustification.Left, a.Justification);

        // Northbound on B: its left is west, where the 09 end is.
        var b = Assert.Single(signs, s => s.AnchorPointIndex == 5);
        Assert.Equal("l[B]m[09-27]", b.Label);
        AssertAt(b, 284, -80, 0);
    }

    [Fact]
    public void Plan_HoldShort_NoSignForTrafficLeavingTheRunway()
    {
        var signs = TaxiwaySignPlanner.Plan(HoldShortAirport()).Signs.Where(s => s.AnchorPointIndex == 2 && s.Label.Contains("m[")).ToList();
        Assert.Single(signs);
    }

    [Theory]
    [InlineData(TaxiPointType.IlsHoldShort)]
    [InlineData(TaxiPointType.IlsHoldShortNoDraw)]
    public void Plan_IlsHoldShort_GetsIlsSign(TaxiPointType type)
    {
        var sign = Assert.Single(TaxiwaySignPlanner.Plan(HoldShortAirport(type)).Signs, s => s.AnchorPointIndex == 2 && s.Label.Contains("m["));
        Assert.Equal("l[A]m[ILS]", sign.Label);
    }

    [Fact]
    public void Plan_HoldShortOnUnnamedTaxiway_OnlyRunwaySign()
    {
        var airport = new Builder().WithRunway()
            .Point(1, 0, 200).Point(2, 0, 80, TaxiPointType.HoldShortNoDraw).Point(3, 0, 0)
            .Path(1, 2, null, TaxiPathType.Path).Path(2, 3, null, TaxiPathType.Path)
            .Airport;

        var sign = Assert.Single(TaxiwaySignPlanner.Plan(airport).Signs);
        Assert.Equal("m[27-09]", sign.Label);
    }

    [Fact]
    public void Plan_HoldShortFarFromAnyRunway_WarnsAndPlacesNothing()
    {
        var airport = new Builder().WithRunway()
            .Point(1, 0, 2000).Point(2, 0, 1500, TaxiPointType.HoldShort)
            .Path(1, 2, "A")
            .Airport;

        var plan = TaxiwaySignPlanner.Plan(airport);
        Assert.Empty(plan.Signs);
        Assert.Contains(plan.Warnings, w => w.Contains("point 2"));
    }

    // ---- Intersections ------------------------------------------------

    // Taxiway A runs west (1) - junction (2) - east (3); B leaves the
    // junction northwards (4). No runway.
    private static AirportDetails TJunctionAirport() => new Builder()
        .Point(1, -200, 0).Point(2, 0, 0).Point(3, 200, 0).Point(4, 0, 200)
        .Path(1, 2, "A").Path(2, 3, "A").Path(2, 4, "B")
        .Airport;

    [Fact]
    public void Plan_TJunction_DirectionArrayPerApproach()
    {
        var arrays = TaxiwaySignPlanner.Plan(TJunctionAirport()).Signs.Where(s => s.Label.Contains("d[")).ToList();
        Assert.Equal(3, arrays.Count);
        Assert.All(arrays, s => Assert.Equal(2, s.AnchorPointIndex));

        // Eastbound on A: B is a left turn, so it goes left of the location sign.
        var eastbound = Assert.Single(arrays, s => s.HeadingDeg == 90);
        Assert.Equal("d[<B]l[A]", eastbound.Label);
        AssertAt(eastbound, -30, 16, 90);

        // Westbound on A: B is a right turn.
        var westbound = Assert.Single(arrays, s => s.HeadingDeg == 270);
        Assert.Equal("l[A]d[B>]", westbound.Label);
        AssertAt(westbound, 30, -16, 270);

        // Southbound on B: A both ways, left turn first (clockwise order).
        var southbound = Assert.Single(arrays, s => s.HeadingDeg == 180);
        Assert.Equal("d[<A]l[B]d[A>]", southbound.Label);
        AssertAt(southbound, 16, 30, 180);
    }

    [Fact]
    public void Plan_TJunction_LocationSignsDownEachBranch()
    {
        var locations = TaxiwaySignPlanner.Plan(TJunctionAirport()).Signs.Where(s => !s.Label.Contains("d[")).ToList();

        Assert.Equal(3, locations.Count);
        AssertAt(Assert.Single(locations, s => s.HeadingDeg == 270), -40, -16, 270); // westbound on A, left = south
        AssertAt(Assert.Single(locations, s => s.HeadingDeg == 90), 40, 16, 90);
        var north = Assert.Single(locations, s => s.HeadingDeg == 0);
        Assert.Equal("l[B]", north.Label);
        AssertAt(north, -16, 40, 0);
    }

    [Fact]
    public void Plan_JunctionOfOneTaxiwayName_IsNotSigned()
    {
        var airport = new Builder()
            .Point(1, -200, 0).Point(2, 0, 0).Point(3, 200, 0).Point(4, 0, 200)
            .Path(1, 2, "A").Path(2, 3, "A").Path(2, 4, "A")
            .Airport;

        Assert.Empty(TaxiwaySignPlanner.Plan(airport).Signs);
    }

    [Fact]
    public void Plan_SetbackWalksThroughIntermediatePoints()
    {
        // A's western leg is split into 10 m pieces; the array still stands 30 m back.
        var airport = new Builder()
            .Point(10, -200, 0).Point(11, -20, 0).Point(12, -10, 0).Point(2, 0, 0).Point(3, 200, 0).Point(4, 0, 200)
            .Path(10, 11, "A").Path(11, 12, "A").Path(12, 2, "A").Path(2, 3, "A").Path(2, 4, "B")
            .Airport;

        var eastbound = Assert.Single(TaxiwaySignPlanner.Plan(airport).Signs, s => s.Label == "d[<B]l[A]");
        AssertAt(eastbound, -30, 16, 90);
    }

    [Fact]
    public void Plan_TooManyBranchesForOneSign_TrimsAndWarns()
    {
        var b = new Builder().Point(0, 0, 0).Point(1, 0, -200);
        b.Path(1, 0, "A");
        // Seven long-named branches fanning out north of the junction.
        for (var i = 0; i < 7; i++)
        {
            var angle = (i + 1) * Math.PI / 8;
            b.Point(100 + i, Math.Cos(angle) * 200, Math.Sin(angle) * 200).Path(0, 100 + i, $"T{i}X");
        }

        var plan = TaxiwaySignPlanner.Plan(b.Airport);
        var northbound = Assert.Single(plan.Signs, s => s.Label.Contains("l[A]") && s.Label.Contains("d["));
        Assert.True(TaxiwaySignLabel.Parse(northbound.Label).IsValid);
        Assert.Contains(plan.Warnings, w => w.Contains("too many taxiways"));
    }

    // ---- Runway exits -------------------------------------------------

    // Taxiway C leaves the runway at (200, 0) at 45°, towards the south-east.
    private static AirportDetails ExitAirport() => new Builder().WithRunway()
        .Point(1, -500, 0).Point(2, 200, 0).Point(3, 306.07, -106.07)
        .Path(1, 2, null, TaxiPathType.Runway).Path(2, 3, "C")
        .Airport;

    [Fact]
    public void Plan_AcuteRunwayExit_SignedForTheLandingDirectionThatCanTakeIt()
    {
        var sign = Assert.Single(TaxiwaySignPlanner.Plan(ExitAirport()).Signs);

        Assert.Equal("d[C']", sign.Label);
        // Eastbound landing, exit on the right: 60 m before the exit, 6 m off the runway edge.
        AssertAt(sign, 140, -28.5, 90);
        Assert.Equal(TaxiwaySignJustification.Right, sign.Justification);
        Assert.Equal(2, sign.AnchorPointIndex);
    }

    [Fact]
    public void Plan_RightAngleExit_SignedFromBothDirections()
    {
        var airport = new Builder().WithRunway()
            .Point(1, -500, 0).Point(2, 200, 0).Point(3, 200, 200)
            .Path(1, 2, null, TaxiPathType.Runway).Path(2, 3, "D")
            .Airport;

        var signs = TaxiwaySignPlanner.Plan(airport).Signs;

        var eastbound = Assert.Single(signs, s => s.HeadingDeg == 90);
        Assert.Equal("d[<D]", eastbound.Label);
        AssertAt(eastbound, 140, 28.5, 90);
        var westbound = Assert.Single(signs, s => s.HeadingDeg == 270);
        Assert.Equal("d[D>]", westbound.Label);
        AssertAt(westbound, 260, 28.5, 270);
    }

    // ---- Restricting and merging ----------------------------------------

    [Fact]
    public void Plan_OnlyPoints_SignsJustThosePoints()
    {
        var signs = TaxiwaySignPlanner.Plan(HoldShortAirport(), new HashSet<int> { 5 }).Signs;
        Assert.All(signs, s => Assert.Equal(5, s.AnchorPointIndex));
        Assert.Single(signs);
    }

    [Fact]
    public void Plan_EverySignIsAutoPlacedAndValid()
    {
        var signs = TaxiwaySignPlanner.Plan(HoldShortAirport()).Signs
            .Concat(TaxiwaySignPlanner.Plan(TJunctionAirport()).Signs)
            .Concat(TaxiwaySignPlanner.Plan(ExitAirport()).Signs)
            .ToList();

        Assert.NotEmpty(signs);
        Assert.All(signs, s =>
        {
            Assert.True(s.IsAutoPlaced);
            Assert.True(TaxiwaySignLabel.Parse(s.Label).IsValid, s.Label);
        });
    }

    [Fact]
    public void Merge_KeepsManualSigns_ReplacesAutoSigns()
    {
        var manual = new TaxiwaySign { Label = "l[M]", AnchorPointIndex = 1 };
        var oldAuto = new TaxiwaySign { Label = "l[OLD]", IsAutoPlaced = true, AnchorPointIndex = 1 };
        var fresh = new TaxiwaySign { Label = "l[NEW]", IsAutoPlaced = true, AnchorPointIndex = 1 };

        Assert.Equal(1, TaxiwaySignPlanner.CountReplaced([manual, oldAuto]));
        Assert.Equal([manual, fresh], TaxiwaySignPlanner.Merge([manual, oldAuto], [fresh]));
    }

    [Fact]
    public void Merge_OnlyPoints_ReplacesJustThosePointsAutoSigns()
    {
        var atOne = new TaxiwaySign { Label = "l[A]", IsAutoPlaced = true, AnchorPointIndex = 1 };
        var atTwo = new TaxiwaySign { Label = "l[B]", IsAutoPlaced = true, AnchorPointIndex = 2 };
        var unanchored = new TaxiwaySign { Label = "l[C]", IsAutoPlaced = true };
        var fresh = new TaxiwaySign { Label = "l[A2]", IsAutoPlaced = true, AnchorPointIndex = 1 };
        var only = new HashSet<int> { 1 };

        Assert.Equal(1, TaxiwaySignPlanner.CountReplaced([atOne, atTwo, unanchored], only));
        Assert.Equal([atTwo, unanchored, fresh], TaxiwaySignPlanner.Merge([atOne, atTwo, unanchored], [fresh], only));
    }

    // ---- Manual sign suggestion -----------------------------------------

    [Fact]
    public void SuggestLocationSign_NearestNamedPath_HeadingAlongIt()
    {
        var suggestion = TaxiwaySignPlanner.SuggestLocationSign(TJunctionAirport(), 5, 150);

        Assert.Equal("l[B]", suggestion?.Label);
        Assert.Equal(0, suggestion!.Value.HeadingDeg, 1);
    }

    [Fact]
    public void SuggestLocationSign_NothingNearby_IsNull()
    {
        Assert.Null(TaxiwaySignPlanner.SuggestLocationSign(TJunctionAirport(), 1000, 1000));
    }

    [Theory]
    [InlineData(-90, 270)]
    [InlineData(360, 0)]
    [InlineData(725, 5)]
    public void NormalizeHeading_WrapsInto0To360(double input, double expected)
    {
        Assert.Equal(expected, TaxiwaySignPlanner.NormalizeHeading(input), 6);
    }
}
