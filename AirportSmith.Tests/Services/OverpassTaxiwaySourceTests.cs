using AirportSmith.Services;

namespace AirportSmith.Tests.Services;

public class OverpassTaxiwaySourceTests
{
    [Fact]
    public void Parse_ReadsWaysWithGeometry_PreferringRefOverName()
    {
        const string json = """
            {
              "elements": [
                { "type": "way", "id": 1, "tags": { "aeroway": "taxiway", "ref": "A1", "name": "Alpha One" },
                  "geometry": [ { "lat": 60.1, "lon": 24.9 }, { "lat": 60.2, "lon": 24.95 } ] },
                { "type": "way", "id": 2, "tags": { "aeroway": "taxiway", "name": " Bravo " },
                  "geometry": [ { "lat": 60.3, "lon": 24.9 }, { "lat": 60.4, "lon": 24.9 } ] },
                { "type": "way", "id": 3, "tags": { "aeroway": "taxiway" },
                  "geometry": [ { "lat": 60.5, "lon": 24.9 }, { "lat": 60.6, "lon": 24.9 } ] }
              ]
            }
            """;

        var ways = OverpassTaxiwaySource.Parse(json);

        Assert.Equal(["A1", "Bravo", null], ways.Select(w => w.Name));
        Assert.Equal((60.1, 24.9), ways[0].Points[0]);
        Assert.Equal(2, ways[0].Points.Count);
    }

    [Fact]
    public void Parse_SkipsWaysWithoutUsableGeometry_AndNonWays()
    {
        const string json = """
            {
              "elements": [
                { "type": "node", "id": 9, "lat": 60.0, "lon": 24.0 },
                { "type": "way", "id": 1, "tags": { "ref": "A" } },
                { "type": "way", "id": 2, "tags": { "ref": "B" }, "geometry": [ { "lat": 60.1, "lon": 24.9 } ] },
                { "type": "way", "id": 3, "tags": { "ref": "C" },
                  "geometry": [ { "lat": 60.1, "lon": 24.9 }, null, { "lat": 60.2, "lon": 24.9 } ] }
              ]
            }
            """;

        var way = Assert.Single(OverpassTaxiwaySource.Parse(json));
        Assert.Equal("C", way.Name);
        Assert.Equal(2, way.Points.Count);
    }

    [Fact]
    public void Parse_NoElements_ReturnsEmpty()
    {
        Assert.Empty(OverpassTaxiwaySource.Parse("{}"));
    }
}
