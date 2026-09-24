# AirportSmith — Claude Instructions

## Project overview
C# .NET 8 WPF application. A Windows desktop editor for producing customized/override versions of default MSFS 2024 airports — edit a stock airport's runways, taxiways, and parking, then package it as a Community-folder add-on that overrides the sim's built-in version at that ICAO.
Solution: `AirportSmith.slnx`, project: `AirportSmith/AirportSmith.csproj`, test project: `AirportSmith.Tests/AirportSmith.Tests.csproj`.

**Status: early planning/research phase.** No airport-reading, editing, or packaging functionality exists yet — see `requirements.md` for the researched constraints and proposed (not yet committed) v0.1 epics.

## Domain research (read before designing extraction/editing features)
- MSFS 2024's default airports are largely **streamed**, not present as local `.bgl` files. The established community tool for this job, Airport Design Editor (ADE), does not yet decompile native 2024 airport BGLs — its decompiler hasn't been updated for 2024's extra fields. The documented workaround is editing the *2020* version of an airport in ADE and bringing the exported XML into the 2024 SDK Dev Mode Scenery Editor.
- MSFS SDK Dev Mode's "Airport Archetype Overrides" only cover cosmetic ground-detail rendering (tire marks, cracks, stains) — not layout/runways/taxiways/parking. Not useful for this project's core feature.
- **SimConnect's Facility Data API** (`AddToFacilityDefinition` / `RequestFacilityData(_EX1)`) can pull live airport data — runways, taxi points/parking, jetways, tower position, frequencies — directly from a running sim session, including streamed airports, without touching BGL files. This is the **recommended primary extraction strategy**: it works today, doesn't depend on a broken decompiler, and follows the same pattern as `DestinationPlanner`'s `SimConnectService` (a sibling project by the same author). Known gap: Facility Data definitions have not been confirmed updated for some MSFS-2024-only additions (`TaxiwayServiceStand` objects, some new `TaxiwayParking` properties).
- **Out of scope for v0.1**: ground/apron visual polygons, static scenery objects/buildings, and any 2024-only taxiway/parking fields not exposed via Facility Data. Revisit only once the SimConnect Facility Data extraction path has been validated against a real airport.

## AppData path convention
- **Release builds**: `%LocalAppData%\AirportSmith-data\`. Not plain `AirportSmith\`: that is the Velopack install folder, which an uninstall deletes wholesale.
- **Debug builds**: `%LocalAppData%\AirportSmith-dev\`  (`#if DEBUG` in `Helpers/AppDataHelper.cs`)

This keeps dev and installed-release data completely separate.

## Backwards compatibility & data safety
Before marking any task done, check whether it touches persisted user data (project files, settings, cached extraction data under the AppData folder). If it does, verify explicitly (not just "should be fine") that a file written by a previous version still loads correctly and that no existing value is silently reset, dropped, or overwritten with a default.
- New fields added to any JSON/XML-serialized settings or project format must deserialize safely from an older file that lacks them (missing → type default, never an exception).
- Never let test code or throwaway tooling write to the real AppData path (`AppDataHelper.AppDataPath`) — Debug builds and the test project resolve to the same `AirportSmith-dev` folder a real dev build uses. Use an isolated/override path whenever a test exercises a code path that persists data. (This is the same guardrail DestinationPlanner needed after an incident there — see that project's `requirements.md` BUG-06 — so it's built in from day one here via the `AppDataHelperTests` test.)

## Requirements tracking
- When a plan is finalized and approved, update `requirements.md` with the user stories and acceptance criteria from the plan before starting implementation.
- Plan must not break existing requirements. If a new requirement conflicts with an older one, it must be checked which one to follow or modify existing accordingly.
- `requirements.md` should also reflect what automated test coverage each requirement needs (or already has).

## README.md
- `README.md` (repo root) must be checked and updated if needed every time when implementing a change.

## Committing
- When the user explicitly asks to commit (e.g. "commit the code", "commit this"), just create the commit directly — no need to ask for confirmation first. This still follows the general git safety rules (new commit, not amend; no `--no-verify`; only files relevant to the change get staged, never a blanket `git add -A`).
- Remote: `origin` = https://github.com/tallskog/AirportSmith (public). CI (`.github/workflows/ci.yml`) builds and tests every push/PR to `main`.

## Versioning
- `AirportSmith/AirportSmith.csproj`'s `<Version>` must match the latest git tag (tag `vX.Y.Z` → `<Version>X.Y.Z</Version>`, no leading `v`).
- When asked to tag a release (e.g. "tag this as vX.Y.Z"), update `<Version>` and commit that change *before* creating the tag, so the tagged commit already carries the matching version — don't tag first and fix the csproj after.
- **Whenever the user says "push"**, this means the whole sequence end-to-end: classify the change(s) since the last tag under semver, bump `<Version>` accordingly, commit that bump, create/move the git tag to match, then push both the commits and the tag — all without waiting for a separate confirmation. Pushing the tag triggers `.github/workflows/release.yml`, which publishes the GitHub Release that installed copies auto-detect as an update. That workflow fails if the tag and `<Version>` disagree.
  - **Patch** (`X.Y.Z+1`) — bug fix only, no new capability.
  - **Minor** (`X.Y+1.0`) — a new feature or capability added, backward-compatible.
  - **Major** (`X+1.0.0`) — a breaking/incompatible change: old project/settings files would no longer load correctly, or a documented behavior a user could depend on is removed/changed incompatibly.
  - Bumping a higher component resets the lower ones to `0`.
  - If a batch of unpushed commits mixes categories, use the highest-precedence one.
  - If it's genuinely ambiguous whether something is a fix vs. a feature vs. breaking, ask rather than guessing.

## Testing
- Test project: `AirportSmith.Tests` (xUnit), run with `dotnet test AirportSmith.slnx`.
- Whenever you make a code change, run `dotnet test` automatically and verify all tests pass before considering the task done — do not wait to be asked.
- **If any unit test fails, or a new requirement conflicts with an existing one in `requirements.md`, stop and consult the user before proceeding.** Do not silently weaken/delete a test or unilaterally pick which requirement "wins" — surface the conflict and let the user decide.
- Prefer testing pure logic and ViewModels via fakes over real I/O, SimConnect, or network calls. UI rendering and live SimConnect/MSFS behavior are verified manually, not by automated tests.

## Build
`dotnet build AirportSmith/AirportSmith.csproj` (or `dotnet build AirportSmith.slnx` to include the test project) — must pass with zero errors before marking any task done.
