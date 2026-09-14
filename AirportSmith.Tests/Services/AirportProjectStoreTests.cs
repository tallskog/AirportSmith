using System.IO;
using AirportSmith.Models;
using AirportSmith.Services;

namespace AirportSmith.Tests.Services;

// Every test uses its own temp directory (never AppDataHelper.AppDataPath,
// which even in this test project resolves to the real AirportSmith-dev
// folder a real dev build uses) — see CLAUDE.md's data-safety guardrail and
// AppDataHelperTests.
public class AirportProjectStoreTests
{
    private static string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "AirportSmith-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAirportDetails()
    {
        var baseDir = CreateTempDirectory();
        try
        {
            var store = new AirportProjectStore(baseDir);
            var taxiName = new TaxiName { Value = "A" };
            var airport = new AirportDetails { Icao = "EFHK", Name = "Helsinki-Vantaa" };
            airport.TaxiNames.Add(taxiName);
            airport.TaxiPaths.Add(new TaxiPathSegment
            {
                TaxiNameId = taxiName.Id, LeftEdgeLighted = true, RightEdgeLighted = false,
                Type = TaxiPathType.Runway, RunwayNumber = 4, RunwayDesignator = TaxiPathRunwayDesignator.Left,
                LeftEdge = TaxiEdgeType.Solid, RightEdge = TaxiEdgeType.Dashed, CenterLine = true, CenterLineLighted = true,
            });
            airport.Runways.Add(new Runway
            {
                PrimaryDesignation = "04L",
                EdgeLightIntensity = RunwayLightIntensity.High,
                PrimaryLeftVasiType = VasiType.Papi4,
                PrimaryLeftVasiAngleDeg = 3.0,
                PrimaryApproachLights = new ApproachLightSystem(ApproachLightSystemType.Alsf2),
            });

            var path = store.Save(airport);
            var loaded = store.Load("EFHK");

            Assert.True(File.Exists(path));
            Assert.NotNull(loaded);
            Assert.Equal("EFHK", loaded!.Icao);
            Assert.Equal("Helsinki-Vantaa", loaded.Name);
            var loadedName = Assert.Single(loaded.TaxiNames);
            Assert.Equal("A", loadedName.Value);
            var taxiPath = Assert.Single(loaded.TaxiPaths);
            Assert.Equal(loadedName.Id, taxiPath.TaxiNameId);
            Assert.True(taxiPath.LeftEdgeLighted);
            Assert.False(taxiPath.RightEdgeLighted);
            Assert.Equal(TaxiPathType.Runway, taxiPath.Type);
            Assert.Equal(4, taxiPath.RunwayNumber);
            Assert.Equal(TaxiPathRunwayDesignator.Left, taxiPath.RunwayDesignator);
            Assert.Equal(TaxiEdgeType.Solid, taxiPath.LeftEdge);
            Assert.Equal(TaxiEdgeType.Dashed, taxiPath.RightEdge);
            Assert.True(taxiPath.CenterLine);
            Assert.True(taxiPath.CenterLineLighted);
            var runway = Assert.Single(loaded.Runways);
            Assert.Equal(RunwayLightIntensity.High, runway.EdgeLightIntensity);
            Assert.Equal(VasiType.Papi4, runway.PrimaryLeftVasiType);
            Assert.Equal(3.0, runway.PrimaryLeftVasiAngleDeg);
            Assert.Equal(ApproachLightSystemType.Alsf2, runway.PrimaryApproachLights?.SystemType);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    [Fact]
    public void Load_NoProjectSaved_ReturnsNull()
    {
        var baseDir = CreateTempDirectory();
        try
        {
            var store = new AirportProjectStore(baseDir);
            Assert.Null(store.Load("ZZZZ"));
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    [Fact]
    public void HasProject_ReflectsWhetherAProjectWasSaved()
    {
        var baseDir = CreateTempDirectory();
        try
        {
            var store = new AirportProjectStore(baseDir);
            Assert.False(store.HasProject("EFHK"));

            store.Save(new AirportDetails { Icao = "EFHK" });

            Assert.True(store.HasProject("EFHK"));
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    // Proves the CLAUDE.md back-compat contract ("missing -> type default,
    // never an exception") for this schema rather than assuming
    // System.Text.Json's default-value behavior holds — a hand-written "old"
    // (schema v1) file missing every field this feature added
    // (LeftEdgeLighted/RightEdgeLighted, EdgeLightIntensity, the
    // VasiType-typed fields, ApproachLightSystem's enum-typed SystemType, and
    // TaxiPathSegment's later Type/RunwayNumber/RunwayDesignator/LeftEdge/
    // RightEdge/CenterLine/CenterLineLighted fields)
    // must still load cleanly. Its TaxiPaths[].Name (v1's free-text field,
    // unknown to the current TaxiPathSegment shape) also exercises the v1->v2
    // taxi-name migration — see MigrateLegacyTaxiNames_ tests below for that
    // in isolation.
    [Fact]
    public void Load_OldFileMissingNewFields_DeserializesWithDefaults_NoException()
    {
        var baseDir = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(baseDir, "Projects"));
            var oldJson = """
            {
              "SchemaVersion": 1,
              "SavedAtUtc": "2026-01-01T00:00:00Z",
              "Airport": {
                "Icao": "EFHK",
                "Name": "Helsinki-Vantaa",
                "Runways": [ { "PrimaryDesignation": "04L" } ],
                "TaxiPaths": [ { "Name": "A" } ]
              }
            }
            """;
            File.WriteAllText(Path.Combine(baseDir, "Projects", "EFHK.json"), oldJson);
            var store = new AirportProjectStore(baseDir);

            var loaded = store.Load("EFHK");

            Assert.NotNull(loaded);
            var taxiPath = Assert.Single(loaded!.TaxiPaths);
            Assert.False(taxiPath.LeftEdgeLighted);
            Assert.False(taxiPath.RightEdgeLighted);
            // Type/RunwayNumber/RunwayDesignator/LeftEdge/RightEdge/CenterLine/
            // CenterLineLighted are all newer than this old file too — missing
            // from the JSON entirely, so must come back as their type default
            // rather than throwing, same CLAUDE.md contract as the fields above.
            Assert.Equal(TaxiPathType.Unknown, taxiPath.Type);
            Assert.Equal(0, taxiPath.RunwayNumber);
            Assert.Equal(TaxiPathRunwayDesignator.None, taxiPath.RunwayDesignator);
            Assert.Equal(TaxiEdgeType.None, taxiPath.LeftEdge);
            Assert.Equal(TaxiEdgeType.None, taxiPath.RightEdge);
            Assert.False(taxiPath.CenterLine);
            Assert.False(taxiPath.CenterLineLighted);
            var runway = Assert.Single(loaded.Runways);
            Assert.Equal(RunwayLightIntensity.None, runway.EdgeLightIntensity);
            Assert.Null(runway.PrimaryLeftVasiType);
            Assert.Null(runway.PrimaryApproachLights);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    // v1's per-segment free-text Name was replaced by v2's shared
    // AirportDetails.TaxiNames list + TaxiPathSegment.TaxiNameId (see
    // TaxiName.cs) — Load must reconstruct the list from a v1 file's legacy
    // names, per CLAUDE.md's rule against silently dropping an existing
    // value, rather than leaving every path unnamed.
    [Fact]
    public void Load_V1FileWithLegacyNames_MigratesToTaxiNamesAndTaxiNameId()
    {
        var baseDir = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(baseDir, "Projects"));
            // Two paths share "A" (the sim's real "one taxiway, many path
            // segments" relationship the v1 flat-string shape had broken),
            // one is "B", and one is unnamed (blank Name).
            var oldJson = """
            {
              "SchemaVersion": 1,
              "SavedAtUtc": "2026-01-01T00:00:00Z",
              "Airport": {
                "Icao": "EFHK",
                "TaxiPaths": [
                  { "Name": "A" },
                  { "Name": "B" },
                  { "Name": "A" },
                  { "Name": "" }
                ]
              }
            }
            """;
            File.WriteAllText(Path.Combine(baseDir, "Projects", "EFHK.json"), oldJson);
            var store = new AirportProjectStore(baseDir);

            var loaded = store.Load("EFHK");

            Assert.NotNull(loaded);
            Assert.Equal(4, loaded!.TaxiPaths.Count);
            Assert.Equal(2, loaded.TaxiNames.Count); // "A" and "B", deduplicated
            var nameA = Assert.Single(loaded.TaxiNames, n => n.Value == "A");
            var nameB = Assert.Single(loaded.TaxiNames, n => n.Value == "B");
            Assert.Equal(nameA.Id, loaded.TaxiPaths[0].TaxiNameId);
            Assert.Equal(nameB.Id, loaded.TaxiPaths[1].TaxiNameId);
            // The two paths that shared "A" in v1 now correctly share one
            // TaxiNameId, restoring the relationship flattening had broken.
            Assert.Equal(nameA.Id, loaded.TaxiPaths[2].TaxiNameId);
            Assert.Null(loaded.TaxiPaths[3].TaxiNameId);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    [Fact]
    public void Load_V2File_DoesNotReMigrateOrDuplicateTaxiNames()
    {
        var baseDir = CreateTempDirectory();
        try
        {
            var store = new AirportProjectStore(baseDir);
            var taxiName = new TaxiName { Value = "A" };
            var airport = new AirportDetails { Icao = "EFHK" };
            airport.TaxiNames.Add(taxiName);
            airport.TaxiPaths.Add(new TaxiPathSegment { TaxiNameId = taxiName.Id });
            store.Save(airport);

            var loaded = store.Load("EFHK");

            Assert.NotNull(loaded);
            var name = Assert.Single(loaded!.TaxiNames);
            Assert.Equal(taxiName.Id, name.Id);
            Assert.Equal(taxiName.Id, loaded.TaxiPaths[0].TaxiNameId);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    [Fact]
    public void Load_CorruptFile_ReturnsNull_DoesNotThrow()
    {
        var baseDir = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(baseDir, "Projects"));
            File.WriteAllText(Path.Combine(baseDir, "Projects", "EFHK.json"), "{ not valid json");
            var store = new AirportProjectStore(baseDir);

            Assert.Null(store.Load("EFHK"));
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }
}
