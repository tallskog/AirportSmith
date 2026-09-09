# AirportSmith — Requirements

## Background / research (2026-09-09)

The core idea — take a default MSFS 2024 airport and produce an edited "new version" as an override add-on — is not fully solved end-to-end by existing tools today:

- MSFS 2024's default airports are largely **streamed**, not present as local `.bgl` files. The established community tool, Airport Design Editor (ADE), does not yet decompile native 2024 airport BGLs (its decompiler hasn't been updated for 2024's extra fields). The documented community workaround is editing the *2020* version of an airport in ADE, then bringing the exported XML into the 2024 SDK Dev Mode Scenery Editor.
- A community **"MSFS Airport Extractor"** tool (.NET 6, C#, console) already does ICAO-driven extraction of an `<Airport>` XML block for use as an SDK Dev Mode project starting point — confirming that pattern works, and that no one has wrapped it in a full GUI editor yet.
- MSFS SDK Dev Mode's "Airport Archetype Overrides" only affect cosmetic ground-detail rendering (tire marks, cracks, stains) — not layout/runways/taxiways/parking. Not relevant to this project's core feature.
- **SimConnect's Facility Data API** (`AddToFacilityDefinition` / `RequestFacilityData(_EX1)`) can pull live airport data (runways, taxi points/parking, jetways, tower position, frequencies) directly from a running sim session, including streamed airports, without touching BGL files. This is the recommended primary extraction strategy for AirportSmith: it works today, avoids the broken-decompiler problem, and reuses a pattern already proven in the sibling `DestinationPlanner` project's `SimConnectService`.
  - Known gap: Facility Data definitions have not been confirmed updated for some MSFS-2024-only additions (`TaxiwayServiceStand` objects, some new `TaxiwayParking` properties like passenger access type).

**Two possible extraction strategies and their coverage:**
1. **SimConnect Facility Data (live, from a running sim)** — runways, taxiway/parking network, frequencies, navaids, tower. Works for streamed airports. Misses ground-poly/apron visuals, buildings/scenery objects, newest 2024-only parking fields.
2. **BGL/XML decompilation (ADE-style, offline)** — currently broken for native 2024 airport BGLs; only reliable via the 2020-data workaround.

**Non-goals for v0.1** (explicitly out of scope until re-evaluated): ground/apron visual polygons, static scenery objects/buildings, and any 2024-only taxiway/parking fields not exposed via Facility Data.

## Proposed v0.1 epics (not yet committed — for prioritization with the user)

These are draft candidates surfaced by the research above, not approved user stories. Each needs to be broken into concrete acceptance criteria once prioritized.

1. **Spike: validate SimConnect Facility Data coverage.** Connect to a running MSFS 2024 session, request facility data for a real ICAO, and confirm in practice which runway/taxiway/parking/frequency fields actually come back — this determines the real editing scope and de-risks everything below before UI work starts.
2. **Extract & display a default airport.** Given an ICAO (with a sim running), pull and show its runway/taxiway/parking/frequency data read-only.
3. **Edit runway/taxiway/parking data.** UI to modify the extracted data (e.g. runway surface/length, taxiway routing, parking spot type/heading/radius).
4. **Generate SDK-compatible `<Airport>` XML.** Produce Dev-Mode/PackageTool-compatible XML for the edited airport, including the `<Exclude>` entries needed to properly override the stock version.
5. **Package/build flow.** Either hand off the generated project to the SDK's Dev Mode / PackageTool for the user to build, or shell out to `fspackagetool` directly to produce a Community-folder package.

## Test coverage
- No functional requirements are approved yet, so no functional test coverage is owed yet.
- `AppDataHelperTests.AppDataPath_UsesDevSuffix_InDebugBuilds` (`AirportSmith.Tests`) pins the dev/release AppData split guardrail from day one, matching the data-safety discipline established in `DestinationPlanner`.
