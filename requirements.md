# AirportSmith — Requirements

## Reference material

- **FAA AIM 2-3, Airport Marking Aids and Signs** —
  https://www.faa.gov/air_traffic/publications/atpubs/aim/aim0203.html —
  authoritative source for runway/taxiway marking colors, shapes, and
  placement (threshold bars, displaced-threshold arrows, demarcation bars,
  chevrons, etc.). Used to ground the Diagram tab's runway pavement-marking
  rendering (see the v0.1+ Diagram epic below) — consult this before changing
  how any airport marking is drawn.

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

## v0.1 — Extract & display airport data from a running sim (committed)

**User story:** As a user, I can type an ICAO code, click a button, and see everything
AirportSmith can read about that airport from a running MSFS 2024 session via
SimConnect's Facility Data API — airport reference info, runways (including
VASI/PAPI approach lights), frequencies, taxi parking spots, the taxiway path
network, and jetways — so I can evaluate what's available before deciding what to
edit in a future version.

**Acceptance criteria:**
- Given MSFS 2024 is running and the ICAO exists, clicking "Load Airport" displays:
  name, lat/lon, elevation, magnetic variation; a runway list (designation, heading,
  length, width, surface, and VASI/PAPI type+angle for each of the four
  primary/secondary × left/right slots when present); a frequency list (type,
  frequency, name); a taxi parking list (number, type, name/suffix codes, heading,
  radius); a taxi path list (type, width, start/end node indices, resolved name);
  and a jetway list when present at the airport. **Confirmed against a live MSFS
  2024 session on 2026-09-09** for Runways, Frequencies, Parking, Taxi Paths, and
  the VASI/PAPI-absent case (all returned sane, correct values) — the
  VASI/PAPI-*present* case and Jetways not yet confirmed (needs a runway with an
  actual PAPI/VASI, and an airport with an actual jetway, respectively).
- Given MSFS is not running or unreachable, clicking "Load Airport" shows a clear,
  specific error message and does not crash; no background auto-reconnect is
  attempted — the next click retries the connection.
- Given the ICAO does not exist / is not found by the sim, a clear "not found"
  message is shown (not an unhandled exception, not a blank/success-looking screen).
- Given the sim is slow/unresponsive, the request gives up after a bounded timeout
  with a clear message rather than hanging the UI indefinitely.
- Fields not populated by the sim/API for a given airport (e.g. a runway with no
  ILS, or other MSFS-2024-only fields not yet confirmed present) are shown as
  blank/omitted rather than causing a crash or a misleading zero/default value.
- Connecting to SimConnect only happens as a side effect of clicking "Load Airport"
  (or opportunistically once at window startup to capture the window handle) — there
  is no timer-based background reconnect loop.

**Dev-mode debugging aid:** In Debug builds only, an "Export Debug Data" button (visible/enabled only once an airport is loaded) serializes the currently loaded `AirportDetails` to JSON under `%Temp%\AirportSmith-dev`, so a pulled dataset can be inspected or handed off (e.g. cross-referenced against open-data sources like the X-Plane Scenery Gateway for taxiway names) without the app staying open. Deliberately writes to the OS temp directory, not `AppDataHelper.AppDataPath` — this is throwaway debug output, never a persisted project/settings file, so it's outside that guardrail entirely. A second "Load Debug Data File" button (also Debug-only) opens a file picker rooted at that same folder and deserializes a previously exported JSON file straight into `Airport`, bypassing SimConnect entirely — lets a dataset be iterated on (e.g. verified against open-data sources) without MSFS running. Exported files are **not** deleted on app exit (deliberately, so a previous run's export can be reloaded on the next run) — they're left for the OS's own temp-cleanup schedule. Not present at all in Release builds (`MainViewModel.IsDevModeExportAvailable`/`IsDevModeImportAvailable` are false when the dev-mode services aren't injected). Covered by `MainViewModelTests`: `IsDevModeExportAvailable`/`IsDevModeImportAvailable` reflect whether the debug store and file-dialog service were injected; `ExportDebugDataCommand.CanExecute` is false until an airport is loaded (and false with no store); executing export calls into the store and sets `LastExportPath`; a new `Load Airport` click clears the previous `LastExportPath`; `LoadFromFileCommand` handles the user cancelling the dialog, an invalid/unparsable file (sets an error message, leaves `Airport` untouched), and a valid file (populates `Airport`, clears `ErrorMessage`/`LastExportPath`). All via `Fakes/FakeDebugDataStore` and `Fakes/FakeFileDialogService`, no real file I/O or WPF dialog in tests. `DebugDataStore` and `FileDialogService` themselves (the real file-writing/dialog implementations) are not covered by automated tests, matching the project's policy of not testing real I/O or UI dialogs directly — verify manually per the acceptance criteria below.

**Known gaps carried forward (not blockers, monitor during manual testing):**
- Live-sim testing (2026-09-09) went through three rounds before finding the real
  root cause. The first two rounds guessed at field presence/order/size and got
  progressively closer but still had garbage Runway heading/length and a garbage
  Frequency value even after matching DestinationPlanner's exact proven field set —
  with no `SIMCONNECT_EXCEPTION` ever logged (`SimConnectService.OnException` logs
  these to the console), ruling out a rejected field name as the cause. The actual
  root cause, confirmed against the SDK's own Facility Data reference
  (`SimConnect_AddToFacilityDefinition`, via its 2020-era static doc mirror — the
  2024 doc site is a JS app that doesn't expose field tables to a plain fetch):
  - `HEADING`, `LENGTH`, `WIDTH`, `RADIUS`, and `FREQUENCY`'s Hz value are all
    `FLOAT32`, not `FLOAT64` — they were declared as C# `double` (8 bytes) instead
    of `float` (4 bytes), corrupting every field after them in each struct. Fixed.
  - `TAXI_PATH` has no `NAME` field at all (only `NAME_INDEX`, an index into the
    separate `TAXI_NAME` type) — a nonexistent field was being requested, which
    corrupted everything after it. `TaxiPathSegment.Name` has been removed.
  - `TAXI_PARKING` has no `LATITUDE`/`LONGITUDE` field, and its `NAME`/`SUFFIX`
    are enumerated `INT32` codes (0-37), not free text, despite the field names.
    `TaxiParkingSpot.Latitude`/`Longitude`/`Name`/`PushbackAngleDeg` (the last of
    which also doesn't exist in this API) have been replaced with
    `NameCode`/`SuffixCode` (raw ints, not yet mapped to friendly labels).
  - The docs also confirmed multiple child sub-types can be nested under one
    facility definition (`AddToFacilityDefinition`'s own example: "Runway is a
    child of Airport, so we can request them in Airport FacilityDataDefinition"),
    so `SimConnectService` reverted to one combined definition (simpler) after a
    prior round had split each sub-type into its own definition on a mistaken
    theory that combining them was the bug.
  - Runway `WidthMeters`/`SurfaceType`/`ElevationMeters` are back in the model
    now that their real types are known.
  - **Confirmed fixed against a live MSFS 2024 session (2026-09-09):** a retest
    after the above fixes showed fully sane values across Runways, Frequencies,
    Parking, and Taxi Paths (e.g. runway `09L`/`27R`, length 3645.6m, width
    44.77m; frequency 128.000 MHz named "OIBK"; parking radius 21m; taxi path
    width 25m) — no more garbage values, no `SIMCONNECT_EXCEPTION`s.
  - Taxi path names are now resolved: `TAXI_PATH.NAME_INDEX` is looked up
    against the airport's `TAXI_NAME` rows (also FLOAT32-clean, CHAR[32]) once
    all Facility Data for the request has arrived — order between TAXI_PATH and
    TAXI_NAME rows isn't guaranteed, so this can't happen inline in
    `OnFacilityData` and instead runs as a resolution pass in
    `OnFacilityDataEnd` (`SimConnectService.ResolveTaxiPathNames`). Not yet
    re-verified against a live sim (added after the confirmed-fixed retest above).
- VASI/PAPI light data (`Runway.PrimaryLeftVasiType`/`AngleDeg`, and the
  matching PrimaryRight/SecondaryLeft/SecondaryRight properties) was added per
  user request, sourced from RUNWAY's four nested VASI sub-blocks
  (`PRIMARY_LEFT_VASI`/`PRIMARY_RIGHT_VASI`/`SECONDARY_LEFT_VASI`/`SECONDARY_RIGHT_VASI`),
  each delivered as its own `SIMCONNECT_FACILITY_DATA_TYPE.VASI` row. Since
  these four are named single-instance children (not a `TAXI_PARKING`-style
  repeating list with an `ItemIndex`), `SimConnectService` correlates each VASI
  row back to one of the four slots by counting rows since the last RUNWAY row
  arrived (`PendingLookup.VasiSlotIndex`). **Confirmed against a live MSFS 2024
  session (2026-09-09)** on a runway known to have no PAPI/VASI at all: all
  four slots were sent, in request order, every time — with `TYPE 0` meaning
  "none installed" (and a meaningless fixed `ANGLE` of 3 alongside it), rather
  than the slot being omitted. `SimConnectService` now treats `TYPE 0` as
  "absent" and leaves the corresponding `Runway` properties `null`. Still
  unconfirmed: a runway that actually *has* a PAPI/VASI on at least one
  side/end, to verify the populated (non-zero) case looks sane too.
- Jetway data comes from a different, older SimConnect API
  (`RequestJetwayData`/`OnRecvJetwayData`) than the rest of this feature (Facility
  Data), requires parking indices from the TAXI_PARKING results first, and its
  pagination behavior (`dwEntryNumber`/`dwOutOf`) is unverified against a real
  multi-jetway airport — jetway extraction is allowed to fail independently (empty
  list, no crash) without blocking the rest of the airport data.
- `TAXI_PARKING_AIRLINE` (airline codes assigned to a gate) and resolving `TAXI_PATH`
  node indices to real coordinates via `TAXI_POINT` are explicitly out of scope for
  v0.1 (taxi path *names* via `TAXI_NAME` are now in scope and implemented, per above).
- This machine cannot run MSFS/SimConnect; live end-to-end verification against a
  real sim session is a manual step for the user, not something covered by
  `dotnet test`.

## v0.1+ — Airport diagram rendering (Diagram tab)

**User story:** As a user, after loading an airport (live from MSFS or via the
Debug "Load Debug Data File" feature), I can open a "Diagram" tab and see a
top-down rendering of its runways, taxiways, and parking spots — starting
fitted to the window, and zoomable/pannable with the mouse — so I can visually
spot-check the airport's layout and — directly building on
`taxiway-name-research.md`'s finding that OIBK's default MSFS taxiway network
is 65% unnamed — see at a glance which taxiway segments have a real name
versus none at all (named segments render solid blue with their name labelled,
unnamed ones dashed gray).

Background/design: see `airport-diagram-rendering-research.md` for the full
research (coordinate approach, rendering technology choice, phased plan).
This epic implements that research's Phase 0 (backend: `TAXI_POINT` added to
the facility definition and resolved against `TaxiPathSegment.StartIndex`/
`EndIndex`; `TAXI_PARKING.BIAS_X`/`BIAS_Z` added) + Phase 1 (runways) + Phase
2 (taxiway centerlines) + Phase 3 (parking spots) as one pass, plus mouse
zoom/pan added immediately after on user request (the research doc had
deferred it; it replaced the original `Viewbox Stretch="Uniform"` with an
explicit `ScaleTransform`/`TranslateTransform` pair, which the doc anticipated
as the later path). Explicitly out of scope, still deferred: Phase 4 (an
X-Plane Gateway `apt.dat` overlay on the same diagram).

**Acceptance criteria:**
- Given an airport with runways is loaded, the Diagram tab renders each
  runway as a filled rectangle, sized and rotated per its heading/length/width,
  in a coordinate system anchored on the airport's own reference point (flat-
  earth projection — accurate at airport scale, no geodesic library needed),
  with its primary/secondary designation (e.g. "09L"/"27R") labelled in white
  near each end, nudged inward from the exact threshold so it sits visibly on
  the pavement (`RunwayShape.PrimaryLabelPosition`/`SecondaryLabelPosition`).
  **Confirmed against a live sim (OIBK):** the primary threshold's physical
  position was initially placed at the wrong end — `HeadingDeg` is the
  heading of travel *from* the primary threshold (i.e. the direction you
  roll after touching down there), so the primary threshold itself sits at
  the *opposite* end from where that heading vector points, not the same
  end. Fixed in `AirportDiagramProjector` (threshold1/threshold2 use
  `-forward`/`+forward` respectively, not the reverse) after the user
  reported OIBK's ~270°-heading runway showing its "27" label at the wrong
  end.
- Given a runway end has a displaced threshold (`RUNWAY.PRIMARY_THRESHOLD`/
  `SECONDARY_THRESHOLD`), it renders per FAA AIM 2-3-3 (see the Reference
  material link above) — entirely in WHITE, since this pavement is still
  usable runway, just not for landing: a translucent white zone overlay
  spanning from that end inward by the feature's `LENGTH`, a solid white
  10ft-wide threshold bar at the displaced threshold itself, and a white
  arrow along the centerline pointing at that bar (one arrow drawn, not
  AIM's repeated arrowheads across the full width — a diagram-level
  simplification). Given a runway end has a blast pad and/or overrun/stopway
  (`PRIMARY_BLASTPAD`/`PRIMARY_OVERRUN` or their `SECONDARY_` equivalents),
  each renders extra pavement extending OUTWARD beyond that threshold, sized
  by the feature's own `LENGTH`/`WIDTH` (falling back to the runway's own
  width if `WIDTH` is unset/zero): a solid yellow 3ft-wide demarcation bar at
  the boundary with the runway, and yellow chevrons tiling it with a 90° tip
  at each vertex and zero gap between chevrons — each one's arm-ends sit
  exactly where the next chevron's tip begins, per the reference screenshot
  the user provided. Chevron size is `featureHalfWidth * 0.85`, capped at the
  feature's own `LENGTH` so a short extension gets a proportioned chevron
  that fits within its own footprint rather than one sized off the width
  alone and overflowing past the actual pavement. Chevrons repeat for the
  extension's full `LENGTH` (fixed chevron depth × however many fit, not an
  evenly-divided fixed count) — a bug where a low upper cap on chevron count
  left the back portion of a long extension blank (reported against OIBK's
  blast pads) is fixed by raising that cap to a 500-chevron safety ceiling
  that only guards against corrupt/garbage `LENGTH` data, never real
  pavement — both per AIM 2-3-3. The extension's
  *background* fill is `SlateGray` — close to the runway's own
  `DarkSlateGray` (same pavement family) but light enough to tell apart from
  it at a glance — rather than AIM's own (also-yellow) background, per user
  request, so the yellow demarcation bar/chevrons read as the "this pavement
  is unusable" signal against a background that still reads as pavement.
  A runway with none of these six pavement sub-structures present (`ENABLE`
  was 0) renders exactly as before — no overlay/extension drawn.
  **Not yet confirmed against a live sim** — see Known gaps below for the
  interpretation this relies on (both blast pad and overrun are attached to,
  and extend away from, the end they're named after — the standard apt.dat-
  style convention, not independently re-derived from an MSFS-specific
  diagram).
- Given taxiway data with resolved `TAXI_POINT` coordinates, each
  `TaxiPathSegment` whose `Type` is `TaxiPathType.Taxi` or `.Path` (excludes
  `Runway`/`Parking`-typed path rows, avoiding double-drawing runway
  centerlines already drawn from the `Runway` model) renders as: a filled
  pavement-footprint band sized from `WidthMeters` (`TaxiwaySegmentShape.WidthCorners`),
  with the original thin centerline drawn on top unchanged — solid blue if
  `Name` is non-empty and dashed gray if not — and, for named segments only,
  a text label at the segment's midpoint showing the name currently used in
  MSFS's scenery for that taxiway. A segment with an unresolved index
  (missing `TAXI_POINT` row) is skipped, not drawn as a garbage line to the
  origin.
- Given parking spot data with `BIAS_X`/`BIAS_Z`, each spot renders as a
  circle (sized by `RadiusMeters`) with a short heading tick.
- Given no airport is loaded, the Diagram tab is empty (no exception).
- The projection math (`AirportDiagramProjector`) is pure and unit-tested
  directly (see Test coverage below) — no SimConnect/WPF dependency.
- Given a diagram is shown, the mouse wheel zooms in/out anchored on the point
  under the cursor (that point stays put while the view scales), and holding
  the left mouse button drags the diagram around. Zoom is clamped to
  0.25×–40× the fit-to-view scale so the diagram can't be lost off-screen or
  shrunk to nothing.
- The diagram starts fitted to the window, re-fits on window resize until the
  user first zooms/pans (after which their view is preserved across resizes),
  and re-fits again whenever a new airport is loaded.

**Known gaps carried forward (not blockers, monitor during manual testing):**
- `TAXI_POINT.BIAS_X`/`BIAS_Z` (new struct `FacilityTaxiPointData` in
  `SimConnectService`) and the newly-added `TAXI_PARKING.BIAS_X`/`BIAS_Z`
  fields are **unconfirmed against a live sim** — field order/type (assumed
  `FLOAT32` per this project's established pattern) and axis convention
  (which axis is which, positive direction, meters vs. feet) are implemented
  per the SDK's documented field order only, the same category of risk as the
  earlier `HEADING`/`LENGTH`/`WIDTH`/`RADIUS`/`FREQUENCY` FLOAT32 bugs. If a
  live pull shows garbage taxiway/parking positions on the Diagram tab, that's
  the first thing to re-check, the same way the earlier bugs were found.
- `TaxiPathType`'s raw-int-to-label mapping is **unconfirmed** — the one real
  data point so far (OIBK, 479 segments) had `Type == 4` on every row, which
  this enum maps to `.Path` (included as drawable), but no other value has
  been observed to confirm the rest of the mapping.
- `AirportDiagramProjector`'s north-up/Z-flip screen-space convention is a
  documented assumption (isolated to one line in `ToScreen`) pending
  confirmation of `BIAS_X`/`BIAS_Z`'s real axis convention — a one-line fix
  if the real axis turns out flipped.
- Taxi points/parking bias coordinates were added to `SimConnectService` and
  build/test clean, but have not been pulled against a live sim yet — the
  already-saved `OIBK_20260910_185034.json` debug export predates this
  feature and has no resolved taxi point/bias data, so it can only be used to
  manually verify the *runway* rendering (via Load Debug Data File) until a
  fresh live pull is done.
- Explicitly out of scope: Phase 4 (X-Plane Gateway `apt.dat` overlay).
- The zoom/pan interaction (`Controls/AirportDiagramView.xaml.cs`) is
  view-layer mouse handling, so it lives in code-behind and is **not** covered
  by automated tests — verified manually per CLAUDE.md's testing policy. The
  diagram's shape data stays pure/testable in `AirportDiagramProjector`.
- `RUNWAY.PRIMARY_THRESHOLD`/`PRIMARY_BLASTPAD`/`PRIMARY_OVERRUN` and their
  `SECONDARY_` counterparts (`FacilityPavementData` in `SimConnectService`)
  are **unconfirmed against a live sim** — `LENGTH`/`WIDTH` field
  order/type (assumed `FLOAT32`) and `ENABLE`'s not-present convention
  (assumed `0`, matching VASI's `TYPE==0`) are implemented per the SDK's
  documented field list only, same risk category as the earlier FLOAT32
  bugs. Additionally, **which end each feature attaches to is an assumption**:
  blast pad and overrun are drawn extending outward from the specific end
  they're named after (the standard convention X-Plane's `apt.dat` uses for
  the equivalent per-end displaced-threshold/stopway/blastpad fields) — this
  has not been checked against a real MSFS Facility Data diagram or SDK
  illustration, only reasoned by analogy. If a live pull shows blast
  pad/overrun pointing the wrong direction (mirroring the earlier
  primary/secondary threshold bug), that's the first thing to re-check. The
  marking *colors and sub-shapes* (white threshold bar/arrow, yellow
  demarcation bar/chevrons) are grounded directly in FAA AIM 2-3-3, not a
  guess — see the Reference material link at the top of this file.

## Proposed v0.1+ epics (not yet committed — for prioritization with the user)

These are draft candidates surfaced by the research above, not approved user stories. Each needs to be broken into concrete acceptance criteria once prioritized.

1. **Edit runway/taxiway/parking data.** UI to modify the extracted data (e.g. runway surface/length, taxiway routing, parking spot type/heading/radius).
2. **Generate SDK-compatible `<Airport>` XML.** Produce Dev-Mode/PackageTool-compatible XML for the edited airport, including the `<Exclude>` entries needed to properly override the stock version.
3. **Package/build flow.** Either hand off the generated project to the SDK's Dev Mode / PackageTool for the user to build, or shell out to `fspackagetool` directly to produce a Community-folder package.

## Test coverage
- `MainViewModelTests` (`AirportSmith.Tests`) covers: successful load populates
  results; not-connected, not-found, and timeout paths set the correct error message
  and leave results empty; `LoadCommand.CanExecute` is false while a request is in
  flight and for invalid/empty ICAO input; ICAO input is uppercased; `IsConnected`
  reflects the underlying service via `ConnectionChanged`; `IsDevModeExportAvailable`/
  `IsDevModeImportAvailable` and `ExportDebugDataCommand`/`LoadFromFileCommand`'s
  `CanExecute` correctly depend on whether the debug store and/or file-dialog service
  were injected and whether an airport is loaded; executing export delegates to the
  store and sets `LastExportPath`; a new load clears the previous `LastExportPath`;
  `LoadFromFileCommand` correctly handles a cancelled dialog, an invalid file (sets
  an error, leaves `Airport` untouched), and a valid file (populates `Airport`,
  clears `ErrorMessage`/`LastExportPath`). All via `Fakes/FakeSimConnectService`,
  `Fakes/FakeDebugDataStore`, and `Fakes/FakeFileDialogService` — no live
  SimConnect/MSFS dependency, no real file I/O, no WPF dialog.
- `SimConnectService`, `DebugDataStore` (the real file-writing/reading
  implementation), and `FileDialogService` (the real WPF dialog) are not covered by
  automated tests (the first requires a live SimConnect session/window handle; the
  other two are real I/O/UI) — all three verified manually per CLAUDE.md's testing
  policy.
- `AppDataHelperTests.AppDataPath_UsesDevSuffix_InDebugBuilds` (`AirportSmith.Tests`) pins the dev/release AppData split guardrail from day one, matching the data-safety discipline established in `DestinationPlanner`.
- `AirportDiagramProjectorTests` (`AirportSmith.Tests`) covers: a single
  runway's thresholds, all 4 rotated-rectangle corners, and both designation
  label positions match hand-calculated coordinates, with all six pavement
  extras (`PrimaryFeatures`/`SecondaryFeatures`) null when the runway has
  none set; a runway with a `PrimaryThreshold` renders a
  `RunwayThresholdMarkingShape` (zone overlay, threshold bar, arrow shaft and
  head) matching hand-calculated coordinates; a runway with
  `PrimaryBlastPad`/`PrimaryOverrun` (one with an explicit `WidthMeters`, one
  at `0` to exercise the fallback-to-runway-width path, which also exercises
  the length-cap since the fallback width alone would overflow a 15m-long
  overrun) renders both as a `RunwayPavementExtensionShape` — footprint,
  demarcation bar, and chevrons whose 90°-tip/zero-gap tiling is verified by
  checking one chevron's arm-ends land exactly at the next chevron's vertex
  — matching hand-calculated coordinates; a 68m-long blast pad needing 16
  chevrons (past the old, buggy 10-chevron cap) has its last chevron's
  arm-ends land exactly on the extension's own far edge, confirming full
  coverage rather than truncation; multi-runway canvas bounds cover
  every runway; a `Taxi`-typed
  segment with resolved coordinates and a name is included with `HasName=true`,
  its `WidthCorners` (the pavement-footprint band) and `MidPoint` (label
  position) matching hand-calculated coordinates (and `HasName=false` for an
  empty name); a `Runway`-typed segment is excluded even
  with valid coordinates (the double-draw guard); a segment with an unresolved
  coordinate is excluded without throwing; a parking spot's center/heading-tip
  match hand-calculated coordinates; an empty `AirportDetails` yields empty
  collections and a non-degenerate canvas with no exception. All pure
  computation, no fakes needed. `MainViewModelTests` additionally covers:
  successful load and a valid `LoadFromFileCommand` both populate `Diagram`;
  not-connected load and an invalid `LoadFromFileCommand` both leave `Diagram`
  null.
- The `SimConnectService` additions for `TAXI_POINT`/`TAXI_PARKING.BIAS_X`/
  `BIAS_Z` and the Diagram tab's rendering and zoom/pan interaction
  (`Controls/AirportDiagramView.xaml` + `.xaml.cs`) are not covered by
  automated tests (real SimConnect I/O and real WPF rendering/mouse input,
  respectively) — verified manually, per CLAUDE.md's testing policy. The app
  was smoke-tested to launch and stay responsive with the new tab present
  (catches a XAML parse/binding crash, which builds/`dotnet test` cannot) —
  actually opening the Diagram tab and checking the rendering visually is a
  manual step for the user.
