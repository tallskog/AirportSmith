using AirportSmith.Models;
using AirportSmith.Models.Inspector;
using AirportSmith.Services;

namespace AirportSmith.Tests.Services;

public class AirportDataTreeBuilderTests
{
    private static DataNode Find(IEnumerable<DataNode> nodes, string prefix) =>
        Assert.Single(nodes, n => n.Text.StartsWith(prefix));

    // Same idea as Find above, but boundary-aware: a plain StartsWith(prefix)
    // would also match sibling properties that happen to share the prefix
    // as their own leading substring (e.g. "PrimaryApproachLights" is a
    // prefix of "PrimaryApproachLightsStrobeCount" too, once that field
    // existed alongside the nested ApproachLightSystem record of the same
    // base name) — this only matches the node that IS that property (a bare
    // "{name}" header for a present nested object/record, or "{name}: ..."
    // for a leaf/absent one), never a same-prefixed sibling.
    private static DataNode FindExact(IEnumerable<DataNode> nodes, string name) =>
        Assert.Single(nodes, n => n.Text == name || n.Text.StartsWith(name + ":") || n.Text.StartsWith(name + " ("));

    [Fact]
    public void Build_ScalarFields_RenderAsNameColonValueLeavesWithNoChildren()
    {
        var airport = new AirportDetails { Icao = "OIBK", Name = "Kermanshah", Latitude = 34.348, Longitude = 47.158 };

        var tree = AirportDataTreeBuilder.Build(airport);

        var icao = Find(tree, "Icao");
        Assert.Equal("Icao: OIBK", icao.Text);
        Assert.Empty(icao.Children);

        var lat = Find(tree, "Latitude");
        Assert.Equal("Latitude: 34.348", lat.Text);
    }

    [Fact]
    public void Build_NullNestedRecord_RendersAsNotPresentLeaf()
    {
        var airport = new AirportDetails();
        airport.Runways.Add(new Runway { PrimaryDesignation = "09L", SecondaryDesignation = "27R" });

        var tree = AirportDataTreeBuilder.Build(airport);

        var runway = Find(Find(tree, "Runways").Children, "[1]");
        var approachLights = FindExact(runway.Children, "PrimaryApproachLights");
        Assert.Equal("PrimaryApproachLights: (not present)", approachLights.Text);
        Assert.Empty(approachLights.Children);
    }

    [Fact]
    public void Build_PresentNestedRecord_ExpandsIntoItsOwnProperties()
    {
        var airport = new AirportDetails();
        airport.Runways.Add(new Runway
        {
            PrimaryDesignation = "09L",
            SecondaryDesignation = "27R",
            PrimaryApproachLights = new ApproachLightSystem(ApproachLightSystemType.Alsf2),
        });

        var tree = AirportDataTreeBuilder.Build(airport);

        var runway = Find(Find(tree, "Runways").Children, "[1]");
        var approachLights = FindExact(runway.Children, "PrimaryApproachLights");
        Assert.Equal("PrimaryApproachLights", approachLights.Text);
        var systemType = Assert.Single(approachLights.Children);
        Assert.Equal("SystemType: Alsf2", systemType.Text);
    }

    [Theory]
    [InlineData(ApproachLightSystemType.Calvert, "SystemType: Calvert")]
    [InlineData((ApproachLightSystemType)999, "SystemType: 999")]
    public void Build_ApproachLightSystemType_RendersEnumName_OrPlainNumberIfUnrecognized(ApproachLightSystemType systemType, string expectedText)
    {
        var airport = new AirportDetails();
        airport.Runways.Add(new Runway { PrimaryApproachLights = new ApproachLightSystem(systemType) });

        var tree = AirportDataTreeBuilder.Build(airport);

        var runway = Find(Find(tree, "Runways").Children, "[1]");
        var node = Assert.Single(FindExact(runway.Children, "PrimaryApproachLights").Children);
        Assert.Equal(expectedText, node.Text);
    }

    [Fact]
    public void Build_VasiType_RendersEnumName()
    {
        var airport = new AirportDetails();
        airport.Runways.Add(new Runway { PrimaryLeftVasiType = VasiType.Papi2, PrimaryLeftVasiAngleDeg = 3.0 });

        var tree = AirportDataTreeBuilder.Build(airport);

        var runway = Find(Find(tree, "Runways").Children, "[1]");
        Assert.Equal("PrimaryLeftVasiType: Papi2", Find(runway.Children, "PrimaryLeftVasiType").Text);
    }

    [Fact]
    public void Build_FrequencyType_AppendsDocumentedLabel()
    {
        var airport = new AirportDetails();
        airport.Frequencies.Add(new Frequency { Type = 6, FrequencyHz = 118_100_000, Name = "TOWER" });

        var tree = AirportDataTreeBuilder.Build(airport);

        var frequency = Find(Find(tree, "Frequencies").Children, "[1]");
        Assert.Equal("Type: 6 (TOWER)", Find(frequency.Children, "Type").Text);
    }

    [Fact]
    public void Build_TaxiParkingSpotTypeAndNameCode_AppendDocumentedLabels()
    {
        var airport = new AirportDetails();
        airport.ParkingSpots.Add(new TaxiParkingSpot { Type = 9, NameCode = 14, SuffixCode = 0 });

        var tree = AirportDataTreeBuilder.Build(airport);

        var spot = Find(Find(tree, "ParkingSpots").Children, "[1]");
        Assert.Equal("Type: 9 (GATE_MEDIUM)", Find(spot.Children, "Type").Text);
        // NameCode/SuffixCode share the same GATE_A..GATE_Z (12-37) labels.
        Assert.Equal("NameCode: 14 (GATE_C)", Find(spot.Children, "NameCode").Text);
        Assert.Equal("SuffixCode: 0 (NONE)", Find(spot.Children, "SuffixCode").Text);
    }

    [Fact]
    public void Build_TaxiPathSegmentType_RendersEnumName()
    {
        // TaxiPathSegment.Type is a real enum (TaxiPathType), not a raw int
        // with a label dictionary — same promotion VasiType/
        // ApproachLightSystemType went through, so no lookup is needed here;
        // see Build_ApproachLightSystemType_RendersEnumName_OrPlainNumberIfUnrecognized.
        var airport = new AirportDetails();
        airport.TaxiPaths.Add(new TaxiPathSegment { Type = TaxiPathType.Path });

        var tree = AirportDataTreeBuilder.Build(airport);

        var segment = Find(Find(tree, "TaxiPaths").Children, "[1]");
        Assert.Equal("Type: Path", Find(segment.Children, "Type").Text);
    }

    [Fact]
    public void Build_TaxiPathSegmentRunwayNumber_AppendsDocumentedLabel()
    {
        var airport = new AirportDetails();
        airport.TaxiPaths.Add(new TaxiPathSegment { RunwayNumber = 39 });

        var tree = AirportDataTreeBuilder.Build(airport);

        var segment = Find(Find(tree, "TaxiPaths").Children, "[1]");
        Assert.Equal("RunwayNumber: 39 (EAST)", Find(segment.Children, "RunwayNumber").Text);
    }

    [Fact]
    public void Build_TaxiPathSegmentRunwayDesignatorLeftEdgeRightEdge_RenderEnumNames()
    {
        var airport = new AirportDetails();
        airport.TaxiPaths.Add(new TaxiPathSegment
        {
            RunwayDesignator = TaxiPathRunwayDesignator.Left,
            LeftEdge = TaxiEdgeType.Solid,
            RightEdge = TaxiEdgeType.Dashed,
        });

        var tree = AirportDataTreeBuilder.Build(airport);

        var segment = Find(Find(tree, "TaxiPaths").Children, "[1]");
        Assert.Equal("RunwayDesignator: Left", Find(segment.Children, "RunwayDesignator").Text);
        // "LeftEdge"/"RightEdge" prefixes would also match LeftEdgeLighted/
        // RightEdgeLighted — include the colon to disambiguate.
        Assert.Equal("LeftEdge: Solid", Find(segment.Children, "LeftEdge:").Text);
        Assert.Equal("RightEdge: Dashed", Find(segment.Children, "RightEdge:").Text);
    }

    [Fact]
    public void Build_SurfaceType_HasNoLabel_SdkDocsDoNotEnumerateIt()
    {
        // Runway.SurfaceType is deliberately NOT in the label lookup — the
        // SDK's own docs promise a list for RUNWAY.SURFACE and then give
        // none, so a raw number is shown rather than a guessed label.
        var airport = new AirportDetails();
        airport.Runways.Add(new Runway { SurfaceType = 2 });

        var tree = AirportDataTreeBuilder.Build(airport);

        var runway = Find(Find(tree, "Runways").Children, "[1]");
        Assert.Equal("SurfaceType: 2", Find(runway.Children, "SurfaceType").Text);
    }

    [Fact]
    public void Build_RunwayList_ItemLabelUsesDesignationsAndCount()
    {
        var airport = new AirportDetails();
        airport.Runways.Add(new Runway { PrimaryDesignation = "09L", SecondaryDesignation = "27R" });
        airport.Runways.Add(new Runway { PrimaryDesignation = "18", SecondaryDesignation = "36" });

        var tree = AirportDataTreeBuilder.Build(airport);

        var runways = Find(tree, "Runways");
        Assert.Equal("Runways (2)", runways.Text);
        Assert.Equal(2, runways.Children.Count);
        Assert.Equal("[1] 09L/27R", runways.Children[0].Text);
        Assert.Equal("[2] 18/36", runways.Children[1].Text);
    }

    [Fact]
    public void Build_EmptyList_RendersZeroCountWithNoChildren()
    {
        var airport = new AirportDetails();

        var tree = AirportDataTreeBuilder.Build(airport);

        var jetways = Find(tree, "Jetways");
        Assert.Equal("Jetways (0)", jetways.Text);
        Assert.Empty(jetways.Children);
    }

    [Fact]
    public void Build_TaxiPathSegment_UnnamedSegment_LabelFallsBackToTypeNotBlankName()
    {
        var airport = new AirportDetails();
        airport.TaxiPaths.Add(new TaxiPathSegment { Type = TaxiPathType.Path, TaxiNameId = null });

        var tree = AirportDataTreeBuilder.Build(airport);

        var segment = Find(Find(tree, "TaxiPaths").Children, "[1]");
        Assert.Equal("[1] (unnamed, type Path)", segment.Text);
    }

    [Fact]
    public void Build_TaxiPathSegment_NamedSegment_LabelShowsTaxiNameId()
    {
        var nameId = Guid.NewGuid();
        var airport = new AirportDetails();
        airport.TaxiPaths.Add(new TaxiPathSegment { Type = TaxiPathType.Path, TaxiNameId = nameId });

        var tree = AirportDataTreeBuilder.Build(airport);

        var segment = Find(Find(tree, "TaxiPaths").Children, "[1]");
        Assert.Equal($"[1] (named, id {nameId:N})", segment.Text);
    }

    [Fact]
    public void Build_NullableDoubleField_PresentAndAbsent_BothRenderCorrectly()
    {
        var airport = new AirportDetails();
        airport.TaxiPaths.Add(new TaxiPathSegment { StartXMeters = 12.5, StartZMeters = null });

        var tree = AirportDataTreeBuilder.Build(airport);

        var segment = Find(Find(tree, "TaxiPaths").Children, "[1]");
        Assert.Equal("StartXMeters: 12.5", Find(segment.Children, "StartXMeters").Text);
        Assert.Equal("StartZMeters: (not present)", Find(segment.Children, "StartZMeters").Text);
    }
}
