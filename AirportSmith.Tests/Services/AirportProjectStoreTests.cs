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
                PrimaryApproachLightsStrobeCount = 5,
                PrimaryApproachLightsHasEndLights = true,
                PrimaryApproachLightsHasReilLights = true,
                PrimaryApproachLightsHasTouchdownLights = true,
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
            Assert.Equal(5, runway.PrimaryApproachLightsStrobeCount);
            Assert.True(runway.PrimaryApproachLightsHasEndLights);
            Assert.True(runway.PrimaryApproachLightsHasReilLights);
            Assert.True(runway.PrimaryApproachLightsHasTouchdownLights);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    // Per CLAUDE.md's data-safety guardrail: a project file written before
    // Runway.PrimaryApproachLightsStrobeCount/HasEndLights/HasReilLights/
    // HasTouchdownLights (and their Secondary counterparts) existed must
    // still load without throwing, with those new fields defaulting to
    // false/0 — never an exception, and never silently resetting anything
    // ELSE already in the file (PrimaryApproachLights' own SystemType is
    // still asserted here to confirm the rest of the runway survives
    // untouched). Writes the raw JSON directly (bypassing Save, which would
    // always write the new fields) to genuinely simulate an older file.
    [Fact]
    public void Load_OlderFileWithoutApproachLightExtraFields_DefaultsThemFalseZero_AndLoadsRestUnchanged()
    {
        var baseDir = CreateTempDirectory();
        try
        {
            var projectsDir = Path.Combine(baseDir, "Projects");
            Directory.CreateDirectory(projectsDir);
            File.WriteAllText(Path.Combine(projectsDir, "EFHK.json"), """
                {
                    "SchemaVersion": 2,
                    "Airport": {
                        "Icao": "EFHK",
                        "Runways": [
                            {
                                "PrimaryDesignation": "04L",
                                "PrimaryApproachLights": { "SystemType": 7 }
                            }
                        ]
                    }
                }
                """);

            var store = new AirportProjectStore(baseDir);
            var loaded = store.Load("EFHK");

            var runway = Assert.Single(loaded!.Runways);
            Assert.Equal(ApproachLightSystemType.Alsf2, runway.PrimaryApproachLights?.SystemType);
            Assert.Equal(0, runway.PrimaryApproachLightsStrobeCount);
            Assert.False(runway.PrimaryApproachLightsHasEndLights);
            Assert.False(runway.PrimaryApproachLightsHasReilLights);
            Assert.False(runway.PrimaryApproachLightsHasTouchdownLights);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    [Fact]
    public void SaveThenLoad_RoundTripsDeleteAllTaxiwaySigns()
    {
        var baseDir = CreateTempDirectory();
        try
        {
            var store = new AirportProjectStore(baseDir);
            store.Save(new AirportDetails { Icao = "EFHK", DeleteAllTaxiwaySigns = true, ApplyFlatten = true });

            var loaded = store.Load("EFHK")!;
            Assert.True(loaded.DeleteAllTaxiwaySigns);
            Assert.True(loaded.ApplyFlatten);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    // A project saved before the option existed has no DeleteAllTaxiwaySigns
    // field at all — must load as false (the previous export behavior), with
    // everything else intact.
    [Fact]
    public void Load_OlderFileWithoutDeleteAllTaxiwaySigns_DefaultsFalse()
    {
        var baseDir = CreateTempDirectory();
        try
        {
            var projectsDir = Path.Combine(baseDir, "Projects");
            Directory.CreateDirectory(projectsDir);
            File.WriteAllText(Path.Combine(projectsDir, "EFHK.json"), """
                {
                    "SchemaVersion": 2,
                    "Airport": { "Icao": "EFHK", "Name": "Helsinki-Vantaa" }
                }
                """);

            var loaded = new AirportProjectStore(baseDir).Load("EFHK");

            Assert.False(loaded!.DeleteAllTaxiwaySigns);
            Assert.False(loaded.ApplyFlatten);
            Assert.Equal("Helsinki-Vantaa", loaded.Name);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    // Jetways saved before gate recording existed: the new Gate* fields load
    // as null (reported by JetwayGateCheck as "can't be checked"), and the
    // existing jetway fields are kept.
    [Fact]
    public void Load_OlderFileWithJetwaysWithoutGateFields_LoadsThemAsNull()
    {
        var baseDir = CreateTempDirectory();
        try
        {
            var projectsDir = Path.Combine(baseDir, "Projects");
            Directory.CreateDirectory(projectsDir);
            File.WriteAllText(Path.Combine(projectsDir, "OEDF.json"), """
                {
                    "SchemaVersion": 2,
                    "Airport": {
                        "Icao": "OEDF",
                        "Jetways": [ { "ParkingIndex": 7, "JetwayObjectId": 42, "Status": 0, "Latitude": 26.4, "Longitude": 49.8, "HeadingDeg": 90 } ]
                    }
                }
                """);

            var jetway = Assert.Single(new AirportProjectStore(baseDir).Load("OEDF")!.Jetways);

            Assert.Equal(7, jetway.ParkingIndex);
            Assert.Equal(42, jetway.JetwayObjectId);
            Assert.Equal(26.4, jetway.Latitude);
            Assert.Null(jetway.GateNameCode);
            Assert.Null(jetway.GateNumber);
            Assert.Null(jetway.GateBiasXMeters);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    [Fact]
    public void SaveThenLoad_RoundTripsJetwayGateFields()
    {
        var baseDir = CreateTempDirectory();
        try
        {
            var store = new AirportProjectStore(baseDir);
            var airport = new AirportDetails { Icao = "OEDF" };
            airport.Jetways.Add(new Jetway { ParkingIndex = 7, GateNameCode = 12, GateNumber = 5, GateSuffixCode = 0, GateBiasXMeters = 10, GateBiasZMeters = 20 });
            store.Save(airport);

            var jetway = Assert.Single(store.Load("OEDF")!.Jetways);

            Assert.Equal((12, 5, 0, 10.0, 20.0), (jetway.GateNameCode, jetway.GateNumber, jetway.GateSuffixCode, jetway.GateBiasXMeters, jetway.GateBiasZMeters));
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

    [Fact]
    public void SaveThenLoad_RoundTripsTaxiwaySigns()
    {
        var baseDir = CreateTempDirectory();
        try
        {
            var store = new AirportProjectStore(baseDir);
            var sign = new TaxiwaySign
            {
                XMeters = 12.5, ZMeters = -40, HeadingDeg = 270, Label = "l[A]m[09-27]",
                Justification = TaxiwaySignJustification.Right, Size = TaxiwaySignSize.Size5,
                IsAutoPlaced = true, AnchorPointIndex = 17,
            };
            var airport = new AirportDetails { Icao = "EFHK" };
            airport.TaxiwaySigns.Add(sign);
            store.Save(airport);

            var loaded = Assert.Single(store.Load("EFHK")!.TaxiwaySigns);
            Assert.Equal(sign.Id, loaded.Id);
            Assert.Equal(12.5, loaded.XMeters);
            Assert.Equal(-40, loaded.ZMeters);
            Assert.Equal(270, loaded.HeadingDeg);
            Assert.Equal("l[A]m[09-27]", loaded.Label);
            Assert.Equal(TaxiwaySignJustification.Right, loaded.Justification);
            Assert.Equal(TaxiwaySignSize.Size5, loaded.Size);
            Assert.True(loaded.IsAutoPlaced);
            Assert.Equal(17, loaded.AnchorPointIndex);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    // A project saved before taxiway signs existed has no TaxiwaySigns list:
    // it must load as an empty list with every other value intact.
    [Fact]
    public void Load_OlderFileWithoutTaxiwaySigns_LoadsEmptyListAndKeepsEverythingElse()
    {
        var baseDir = CreateTempDirectory();
        try
        {
            var projectsDir = Path.Combine(baseDir, "Projects");
            Directory.CreateDirectory(projectsDir);
            File.WriteAllText(Path.Combine(projectsDir, "EFHK.json"), """
                {
                    "SchemaVersion": 2,
                    "Airport": {
                        "Icao": "EFHK", "Name": "Helsinki-Vantaa", "DeleteAllTaxiwaySigns": true,
                        "TaxiNames": [ { "Id": "8c1e1f3e-4d55-4f7a-9a43-0d6c2f3a1b11", "Value": "A" } ],
                        "TaxiPaths": [ { "TaxiNameId": "8c1e1f3e-4d55-4f7a-9a43-0d6c2f3a1b11", "Type": 1 } ]
                    }
                }
                """);

            var loaded = new AirportProjectStore(baseDir).Load("EFHK")!;

            Assert.NotNull(loaded.TaxiwaySigns);
            Assert.Empty(loaded.TaxiwaySigns);
            Assert.Equal("Helsinki-Vantaa", loaded.Name);
            Assert.True(loaded.DeleteAllTaxiwaySigns);
            Assert.Equal("A", Assert.Single(loaded.TaxiNames).Value);
            Assert.Equal(loaded.TaxiNames[0].Id, Assert.Single(loaded.TaxiPaths).TaxiNameId);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    // A sign written without Size/Justification (hand-edited file) still
    // loads as a valid sign: the Scenery Editor's default size, left side.
    [Fact]
    public void Load_TaxiwaySignMissingSizeAndSide_DefaultsToSize3Left()
    {
        var baseDir = CreateTempDirectory();
        try
        {
            var projectsDir = Path.Combine(baseDir, "Projects");
            Directory.CreateDirectory(projectsDir);
            File.WriteAllText(Path.Combine(projectsDir, "EFHK.json"), """
                {
                    "SchemaVersion": 2,
                    "Airport": { "Icao": "EFHK", "TaxiwaySigns": [ { "Label": "l[A]", "XMeters": 5 } ] }
                }
                """);

            var sign = Assert.Single(new AirportProjectStore(baseDir).Load("EFHK")!.TaxiwaySigns);

            Assert.Equal(TaxiwaySignSize.Size3, sign.Size);
            Assert.Equal(TaxiwaySignJustification.Left, sign.Justification);
            Assert.False(sign.IsAutoPlaced);
            Assert.Equal(5, sign.XMeters);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    // v0.3.0 (schema v2) stored the reader's travel direction; the SDK heading
    // is that turned 90° toward the taxiway (+90 Left, −90 Right).
    [Fact]
    public void Load_SchemaV2TaxiwaySigns_TurnsHeadingsBySide_AndKeepsTheRest()
    {
        var baseDir = CreateTempDirectory();
        try
        {
            var projectsDir = Path.Combine(baseDir, "Projects");
            Directory.CreateDirectory(projectsDir);
            File.WriteAllText(Path.Combine(projectsDir, "EFHK.json"), """
                {
                    "SchemaVersion": 2,
                    "Airport": { "Icao": "EFHK", "Name": "Helsinki-Vantaa", "TaxiwaySigns": [
                        { "Label": "l[A]", "HeadingDeg": 348.1, "Justification": 0, "XMeters": 5, "IsAutoPlaced": true, "AnchorPointIndex": 3 },
                        { "Label": "d[B>]", "HeadingDeg": 77.9, "Justification": 1, "Size": 2 }
                    ] }
                }
                """);

            var loaded = new AirportProjectStore(baseDir).Load("EFHK")!;

            Assert.Equal(78.1, loaded.TaxiwaySigns[0].HeadingDeg, 6);
            Assert.Equal(347.9, loaded.TaxiwaySigns[1].HeadingDeg, 6);
            Assert.Equal("l[A]", loaded.TaxiwaySigns[0].Label);
            Assert.Equal(5, loaded.TaxiwaySigns[0].XMeters);
            Assert.True(loaded.TaxiwaySigns[0].IsAutoPlaced);
            Assert.Equal(3, loaded.TaxiwaySigns[0].AnchorPointIndex);
            Assert.Equal(TaxiwaySignSize.Size2, loaded.TaxiwaySigns[1].Size);
            Assert.Equal("Helsinki-Vantaa", loaded.Name);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    [Fact]
    public void SaveThenLoad_CurrentSchema_DoesNotTurnSignHeadingsAgain()
    {
        var baseDir = CreateTempDirectory();
        try
        {
            var store = new AirportProjectStore(baseDir);
            var airport = new AirportDetails { Icao = "EFHK" };
            airport.TaxiwaySigns.Add(new TaxiwaySign { Label = "l[A]", HeadingDeg = 78.1 });
            store.Save(airport);
            store.Save(store.Load("EFHK")!);

            Assert.Equal(78.1, Assert.Single(store.Load("EFHK")!.TaxiwaySigns).HeadingDeg);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }
}
