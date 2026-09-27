using AirportSmith.Models;
using AirportSmith.Services;

namespace AirportSmith.Tests.Services;

public class JetwayGateCheckTests
{
    private const int GateA = 12; // GATE_A name code
    private const int GateB = 13;

    // Gate GATE_A 5 (ItemIndex 7) with one jetway, captured as if just loaded.
    private static AirportDetails AirportWithJetwayAtGate()
    {
        var airport = new AirportDetails { Icao = "OEDF" };
        airport.ParkingSpots.Add(new TaxiParkingSpot { ItemIndex = 3, NameCode = GateB, Number = 1, BiasXMeters = 100, BiasZMeters = 0 });
        airport.ParkingSpots.Add(new TaxiParkingSpot { ItemIndex = 7, NameCode = GateA, Number = 5, BiasXMeters = 10, BiasZMeters = 20 });
        airport.Jetways.Add(new Jetway { ParkingIndex = 7 });
        JetwayGateCheck.CaptureGates(airport);
        return airport;
    }

    private static TaxiParkingSpot Gate(AirportDetails airport) => airport.ParkingSpots.Single(s => s.ItemIndex == 7);

    [Fact]
    public void CaptureGates_RecordsTheGateWithTheJetwaysParkingIndex()
    {
        var jetway = AirportWithJetwayAtGate().Jetways[0];

        Assert.Equal(GateA, jetway.GateNameCode);
        Assert.Equal(5, jetway.GateNumber);
        Assert.Equal(0, jetway.GateSuffixCode);
        Assert.Equal(10, jetway.GateBiasXMeters);
        Assert.Equal(20, jetway.GateBiasZMeters);
    }

    [Fact]
    public void UnchangedGate_NoWarnings()
    {
        Assert.Empty(JetwayGateCheck.FindWarnings(AirportWithJetwayAtGate()));
    }

    [Fact]
    public void EditingOtherFieldsOrASmallMove_NoWarnings()
    {
        var airport = AirportWithJetwayAtGate();
        Gate(airport).HeadingDeg = 90;
        Gate(airport).RadiusMeters = 30;
        Gate(airport).BiasXMeters += 1.5;

        Assert.Empty(JetwayGateCheck.FindWarnings(airport));
    }

    [Theory]
    [InlineData("rename")]
    [InlineData("renumber")]
    [InlineData("suffix")]
    [InlineData("delete")]
    public void GateNoLongerExists_Warns(string edit)
    {
        var airport = AirportWithJetwayAtGate();
        switch (edit)
        {
            case "rename": Gate(airport).NameCode = GateB; break;
            case "renumber": Gate(airport).Number = 6; break;
            case "suffix": Gate(airport).SuffixCode = GateB; break;
            case "delete": airport.ParkingSpots.Remove(Gate(airport)); break;
        }

        var warning = Assert.Single(JetwayGateCheck.FindWarnings(airport));
        Assert.Contains("GATE_A 5", warning);
        Assert.Contains("no gate to attach to", warning);
    }

    // What matters in the sim is the gate identity, not the spot record: if
    // another spot now carries GATE_A 5, the jetway attaches to that one, so
    // only its distance counts.
    [Fact]
    public void AnotherSpotTakingOverTheGateIdentity_IsCheckedByPosition()
    {
        var airport = AirportWithJetwayAtGate();
        airport.ParkingSpots.Remove(Gate(airport));
        var other = airport.ParkingSpots[0];
        other.NameCode = GateA;
        other.Number = 5;

        var warning = Assert.Single(JetwayGateCheck.FindWarnings(airport));
        Assert.Contains("was moved 92 m", warning);
    }

    [Fact]
    public void GateMovedBeyondTheLimit_Warns()
    {
        var airport = AirportWithJetwayAtGate();
        Gate(airport).BiasZMeters += 8;

        var warning = Assert.Single(JetwayGateCheck.FindWarnings(airport));
        Assert.Contains("GATE_A 5", warning);
        Assert.Contains("moved 8 m", warning);
    }

    [Fact]
    public void SeveralJetwaysAtOneGate_OneWarning()
    {
        var airport = AirportWithJetwayAtGate();
        airport.Jetways.Add(new Jetway { ParkingIndex = 7 });
        JetwayGateCheck.CaptureGates(airport);
        Gate(airport).Number = 6;

        Assert.Single(JetwayGateCheck.FindWarnings(airport));
    }

    // Project saved before gates were recorded: can't check, and says so once.
    [Fact]
    public void UnrecordedJetways_OneSummaryWarning()
    {
        var airport = new AirportDetails { Icao = "OEDF" };
        airport.Jetways.Add(new Jetway { ParkingIndex = 1 });
        airport.Jetways.Add(new Jetway { ParkingIndex = 2 });

        var warning = Assert.Single(JetwayGateCheck.FindWarnings(airport));
        Assert.StartsWith("2 jetway(s) can't be checked", warning);
    }

    [Fact]
    public void NoJetways_NoWarnings()
    {
        Assert.Empty(JetwayGateCheck.FindWarnings(new AirportDetails()));
    }
}
