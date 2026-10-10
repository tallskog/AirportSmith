using AirportSmith.Models;
using AirportSmith.Services;

namespace AirportSmith.Tests.Services;

public class TaxiPathNameRulesTests
{
    private static readonly TaxiName Alpha = new() { Value = "A" };
    private static readonly TaxiName Blank = new() { Value = "" };

    private static TaxiPathSegment Normalize(TaxiPathType type, Guid? nameId)
    {
        var path = new TaxiPathSegment { Type = type, TaxiNameId = nameId };
        TaxiPathNameRules.NormalizeOnRead(new AirportDetails { TaxiNames = [Alpha, Blank], TaxiPaths = [path] });
        return path;
    }

    [Theory]
    [InlineData(TaxiPathType.Path)]
    [InlineData(TaxiPathType.Taxi)]
    [InlineData(TaxiPathType.Unknown)]
    public void NamedPath_BecomesTaxi(TaxiPathType type)
    {
        var path = Normalize(type, Alpha.Id);

        Assert.Equal(TaxiPathType.Taxi, path.Type);
        Assert.Equal(Alpha.Id, path.TaxiNameId);
    }

    [Theory]
    [InlineData(TaxiPathType.Path)]
    [InlineData(TaxiPathType.Taxi)]
    [InlineData(TaxiPathType.Unknown)]
    public void UnnamedPath_BecomesPath(TaxiPathType type)
    {
        Assert.Equal(TaxiPathType.Path, Normalize(type, null).Type);
    }

    [Fact]
    public void PathPointingAtBlankName_CountsAsUnnamed()
    {
        var path = Normalize(TaxiPathType.Taxi, Blank.Id);

        Assert.Equal(TaxiPathType.Path, path.Type);
        Assert.Equal(Blank.Id, path.TaxiNameId);
    }

    [Fact]
    public void RunwayPath_KeepsTypeAndLosesName()
    {
        var path = Normalize(TaxiPathType.Runway, Alpha.Id);

        Assert.Equal(TaxiPathType.Runway, path.Type);
        Assert.Null(path.TaxiNameId);
    }

    [Fact]
    public void ParkingPath_IsUnchanged()
    {
        var path = Normalize(TaxiPathType.Parking, Alpha.Id);

        Assert.Equal(TaxiPathType.Parking, path.Type);
        Assert.Equal(Alpha.Id, path.TaxiNameId);
    }

    [Theory]
    [InlineData(TaxiPathType.Closed)]
    [InlineData(TaxiPathType.Vehicle)]
    [InlineData(TaxiPathType.Road)]
    [InlineData(TaxiPathType.PaintedLine)]
    public void OtherTypes_AreUnchanged(TaxiPathType type)
    {
        Assert.Equal(type, Normalize(type, Alpha.Id).Type);
        Assert.Equal(type, Normalize(type, null).Type);
    }
}
