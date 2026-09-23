# AirportSmith — Requirements

## Reference material

- **FAA AIM 2-3, Airport Marking Aids and Signs** —
  https://www.faa.gov/air_traffic/publications/atpubs/aim/aim0203.html —
  authoritative source for runway/taxiway marking colors, shapes, and
  placement (threshold bars, displaced-threshold arrows, demarcation bars,
  chevrons, etc.). Used to ground the Diagram tab's runway pavement-marking
  rendering (see the v0.1+ Diagram epic below) — consult this before changing
  how any airport marking is drawn.
- **FAA AIM 2-1, Airport Lighting Aids** —
  https://www.faa.gov/air_traffic/publications/atpubs/aim/aim0201.html —
  covers approach light system (ALS) types and their standard lengths/
  layouts (AIM 2-1-3). Used to ground the Diagram tab's approach-lights
  schematic (see the v0.1+ Diagram epic below) — consult this before changing
  how approach lights are categorized or drawn.

## Background / research (2026-09-09)

The core idea — take a default MSFS 2024 airport and produce an edited "new version" as an override add-on — is not fully solved end-to-end by existing tools today:

- MSFS 2024's default airports are largely **streamed**, not present as local `.bgl` files. The established community tool, Airport Design Editor (ADE), does not yet decompile native 2024 airport BGLs (its decompiler hasn't been updated for 2024's extra fields). The documented community workaround is editing the *2020* version of an airport in ADE, then bringing the exported XML into the 2024 SDK Dev Mode Scenery Editor.
- A community **"MSFS Airport Extractor"** tool (.NET 6, C#, console) already does ICAO-driven extraction of an `<Airport>` XML block for use as an SDK Dev Mode project starting point — confirming that pattern works, and that no one has wrapped it in a full GUI editor yet.
- MSFS SDK Dev Mode's "Airport Archetype Overrides" only affect cosmetic ground-detail rendering (tire marks, cracks, stains) — not layout/runways/taxiways/parking. Not relevant to this project's core feature.
- **SimConnect's Facility Data API** (`AddToFacilityDefinition` / `RequestFacilityData(_EX1)`) can pull live airport data (runways, taxi points/parking, jetways, tower position, frequencies) directly from a running sim session, including streamed airports, without touching BGL files. This is the recommended primary extraction strategy for AirportSmith: it works today, avoids the broken-decompiler problem, and reuses a pattern already proven in the sibling `DestinationPlanner` project's `SimConnectService`.
  - Known gap: Facility Data definitions have not been confirmed updated for some MSFS-2024-only additions (`TaxiwayServiceStand` objects, some new `TaxiwayParking` properties like passenger access type).
  - **Confirmed hard gap (2026-09-10): taxiway signs cannot be read via SimConnect at all.** The `SIMCONNECT_FACILITY_DATA_TYPE` enum (the full set of sub-types requestable via `AddToFacilityDefinition`/`RequestFacilityData(_EX1)`) has no sign-related member — its 26 values run `AIRPORT`, `RUNWAY`, `START`, `FREQUENCY`, `HELIPAD`, `APPROACH` (+ transition/leg sub-types), `DEPARTURE`, `ARRIVAL`, `RUNWAY_TRANSITION`, `ENROUTE_TRANSITION`, `TAXI_POINT`, `TAXI_PARKING`, `TAXI_PATH`, `TAXI_NAME`, `JETWAY`, `VOR`, `NDB`, `WAYPOINT`, `ROUTE`, `PAVEMENT`, `APPROACH_LIGHTS`, `VASI` — confirmed against both the static SimConnect API reference and the current MSFS 2024 SDK site. The 2024 SDK site's own table of contents lists "TaxiwaySign Objects" only under **Scenery Editor Objects** (BGL/XML authoring), never under the SimConnect API Reference section — confirming taxiway signs are an authoring-only concept, unlike `TaxiwayServiceStand`/newer `TaxiwayParking` fields (which at least plausibly exist in Facility Data but are unconfirmed). Reading taxiway sign placement/text would require the BGL-decompilation path, which is itself currently broken for native 2024 airports (see above) — so taxiway signs are unreachable by either strategy for the foreseeable future.

**Two possible extraction strategies and their coverage:**
1. **SimConnect Facility Data (live, from a running sim)** — runways, taxiway/parking network, frequencies, navaids, tower. Works for streamed airports. Misses ground-poly/apron visuals, buildings/scenery objects, newest 2024-only parking fields, and taxiway signs entirely (no Facility Data type exists for them).
2. **BGL/XML decompilation (ADE-style, offline)** — currently broken for native 2024 airport BGLs; only reliable via the 2020-data workaround.

**Non-goals for v0.1** (explicitly out of scope until re-evaluated): ground/apron visual polygons, static scenery objects/buildings, taxiway signs (no extraction path exists via either strategy), and any 2024-only taxiway/parking fields not exposed via Facility Data.

## v0.1 — Extract & display airport data from a running sim (committed)

**User story:** As a user, I can type an ICAO code, click a button, and see everything
AirportSmith can read about that airport from a running MSFS 2024 session via
SimConnect's Facility Data API — airport reference info, runways (including
VASI/PAPI approach lights and approach light systems), frequencies, taxi parking
spots, the taxiway path network, and jetways — so I can evaluate what's available
before deciding what to edit in a future version.

**Acceptance criteria:**
- Given MSFS 2024 is running and the ICAO exists, clicking "Load Airport" displays:
  name, lat/lon, elevation, magnetic variation; a runway list (designation, heading,
  length, width, surface, VASI/PAPI type+angle for each of the four
  primary/secondary × left/right slots when present, and the approach light system
  type for the primary/secondary ends when present); a frequency list (type,
  frequency, name); a taxi parking list (number, type, name/suffix codes, heading,
  radius); a taxi path list (type, width, start/end node indices, resolved name,
  associated runway number/designator, left/right edge type, and whether it has a
  (lighted) center line — see the "taxi path type/runway-association/edge-type/
  center-line editing" note under the Edit tab epic below for when these were
  added); and a jetway list when present at the airport. **Confirmed against a live MSFS
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
- Approach light system data (`Runway.PrimaryApproachLights`/`SecondaryApproachLights`,
  each an `ApproachLightSystem(SystemType)`) was added to support showing approach
  lights on the diagram per user request, sourced from RUNWAY's two nested
  `PRIMARY_APPROACH_LIGHTS`/`SECONDARY_APPROACH_LIGHTS` sub-structures (the SDK's
  `APPROACH_LIGHTS` facility data type). Only `SYSTEM` is requested (not
  `STROBE_COUNT`/`HAS_END_LIGHTS`/`HAS_REIL_LIGHTS`/`HAS_TOUCHDOWN_LIGHTS`/
  `ON_GROUND`/`ENABLE`/`OFFSET`/`SPACING`/`SLOPE`, which this project doesn't use
  yet). `SimConnectService` correlates each row back to primary/secondary the same
  way it does for the VASI/PAVEMENT sub-structures — counting rows since the last
  RUNWAY row arrived (`PendingLookup.ApproachLightsSlotIndex`).
  - **Bug found and fixed (2026-09-11, live sim, user report):** the first
    revision requested `SYSTEM` and `ENABLE`, gating presence on `ENABLE!=0` by
    analogy with `PAVEMENT.ENABLE`. The user checked several airports/runways and
    none showed approach light data at all. Re-checked directly against the local
    MSFS 2024 SDK's own bundled docs (`...\Documentation\public\retail\
    programming-apis\simconnect\api-reference\facilities\
    simconnect_addtofacilitydefinition\index.md`, found via the user's own IDE
    tab into `SimConnect.h`) rather than the earlier online mirror: `PAVEMENT`'s
    `ENABLE` is documented as "whether the requested pavement area is actually...
    present" (a structural existence flag), but `APPROACHLIGHTS`' own `ENABLE` is
    documented as "whether the approach lights are enabled" — an
    operational/runtime flag, not an install flag — so gating on it was silently
    dropping every real `SYSTEM` value. Fixed: `ENABLE` is no longer requested at
    all for this sub-structure; presence is now `SYSTEM!=0`, the same convention
    already confirmed working for `VASI.TYPE==0` (which also has no `ENABLE`
    field). Field names/order for both `RUNWAY.PRIMARY_APPROACH_LIGHTS`/
    `SECONDARY_APPROACH_LIGHTS` and `APPROACHLIGHTS.SYSTEM`'s 15-value enum are
    now confirmed directly against the local SDK docs, not just the earlier
    online mirror. **Re-verified against a live sim (2026-09-11, EFHK):** the
    fix works — the export shows real, non-`NONE` `SystemType` values (e.g.
    `SystemType=9`/CALVERT on 15, `SystemType=10`/CALVERT2 on 22L/04R and
    22R/04L), confirming `SYSTEM!=0` is the right presence signal.
  - **Second bug found and fixed (2026-09-11, same EFHK data):** even with
    real approach light data now populating `Runway.PrimaryApproachLights`/
    `SecondaryApproachLights`, the Diagram tab still showed nothing visible.
    Root cause was in the *rendering*, not the extraction: EFHK's own taxi/
    parking data alone already spans roughly 4900m x 3100m (measured
    directly from the export), and `AirportDiagramProjector`'s fit-to-view
    scale is `min(ActualWidth/CanvasWidth, ActualHeight/CanvasHeight)` — at
    that real-world scale (roughly 0.15x-0.2x for a typical window size), a
    lone few-meter-wide dot shrinks below a device pixel and is invisible
    regardless of its fill color, unlike a synthetic single-runway test
    fixture where the canvas stays small enough for a several-meter dot to
    still render at a few pixels. Fixed in `AirportDiagramView.xaml`'s
    `ApproachLightsTemplate`: a connecting `Polyline` is now drawn through
    the rail lights first (the same reason the thin taxiway centerlines stay
    visible at any zoom — a line's length keeps it rendering even when its
    stroke alone would be sub-pixel), with the individual dot markers (now
    16m, up from 6m) drawn on top for detail once zoomed in on a specific
    runway end; the red crossbar's stroke was also thickened (3 to 8) for
    the same reason. XAML-only change — the underlying `RailLights`/
    `CrossBar` coordinate data from `AirportDiagramProjector` was already
    correct (confirmed by `AirportDiagramProjectorTests`), so no projector
    or test changes were needed for this fix.
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
- Given a runway end has an approach light system (`RUNWAY.PRIMARY_APPROACH_LIGHTS`/
  `SECONDARY_APPROACH_LIGHTS`), it renders as a schematic per FAA AIM 2-1-3 (see the
  Reference material link above) — not a literal light-by-light reproduction: a row
  of evenly-spaced white dots extending outward from the threshold along the
  extended centerline, whose length and spacing are bucketed by the system's
  category (`AirportDiagramProjector.Categorize`, keyed off the SDK's raw `SYSTEM`
  value) rather than each of the 14 SDK system types' exact real-world layout —
  Sparse (ODALS: ~465m, ~92m spacing), Short (MALSF/SSALF/MALS/SALS/SALSF/SSALS:
  ~430m, ~30m spacing), and Full (MALSR/SSALR/RAIL/CALVERT/CALVERT2 and
  ALSF-1/ALSF-2: ~730m, ~30m spacing). ALSF-1/ALSF-2 additionally render a red
  crossbar ~300m (~1000ft) out from the threshold, for their defining red side-row
  barrettes / decision bar — every other category has none. `SYSTEM` value `0`
  (`NONE`), an unrecognized value, or no `ApproachLightSystem` at all (`ENABLE` was
  `0`) renders nothing, same as the other optional runway-end features.
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
- The approach-lights schematic's length/spacing buckets and the ALSF-1/ALSF-2
  red-crossbar distance are round-number approximations of the real ICAO/FAA
  standard lengths (per FAA AIM 2-1-3), not exact conversions or a literal
  reproduction of each of the SDK's 14 system types' real-world layout — a
  diagram-level simplification in the same spirit as the fixed-count
  chevron/single-arrow simplifications below. The underlying
  `Runway.PrimaryApproachLights`/`SecondaryApproachLights` data itself is
  unconfirmed against a live sim — see the v0.1 Known gaps above.
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

## v0.1+ — OpenStreetMap tile layer behind the diagram (phase 0)

**User story:** As a user, I can optionally show an OpenStreetMap layer behind
the Diagram tab's and Edit tab's airport rendering — off by default, one click
to turn on — so I can visually cross-check the airport's runway/taxiway layout
against real-world imagery (and, as a side effect, sanity-check the
still-UNCONFIRMED `BIAS_X`/`BIAS_Z` axis convention against real terrain, see
the diagram-rendering section above). This is phase 0 of the map/satellite
underlay researched in [`background-map-research.md`](background-map-research.md);
phases 1 (satellite imagery) and 2 (per-airport offset calibration + map-mode
styling) remain draft, below.

**Acceptance criteria:**
- A "Show map" checkbox is visible on both the Diagram tab and the Edit tab
  whenever an airport is loaded, unchecked by default every time the app
  starts (not persisted across restarts or in the project file — a
  session-only view preference for this phase). Both checkboxes reflect the
  same shared toggle.
- Checking it starts loading OpenStreetMap standard tiles behind the existing
  runway/taxiway/parking rendering, sized and positioned from the airport's
  own reference lat/lon (`AirportDiagram.ReferenceLatitude`/`ReferenceLongitude`)
  so real-world features line up with the diagram's own shapes; unchecking it
  removes the tile layer immediately (any in-flight tile requests are
  discarded on arrival, not cancelled).
- Only tiles currently visible in the viewport are ever requested — panning or
  zooming loads newly-visible tiles and never fetches tiles for the whole
  airport up front (no bulk/prefetch download, per OSM's usage policy).
- Every outbound tile request sends a unique `User-Agent` identifying
  AirportSmith (never a default HttpClient/library User-Agent). The tile
  endpoint (`tile.openstreetmap.org`) and User-Agent string are spike-only
  choices flagged in code as needing revisiting (a real contact string, and
  possibly a different endpoint) before the app is distributed beyond the
  author's own manual testing — OSMF's usage policy discourages third-party
  desktop apps hitting their tile server at scale without prior arrangement.
- "© OpenStreetMap contributors" is shown visibly over the map whenever the
  layer is on.
- A previously-fetched tile is served from a local disk cache
  (`%LocalAppData%\AirportSmith[-dev]\MapTiles`) for at least 7 days before
  being eligible for re-fetch, honoring a longer `Cache-Control: max-age` from
  the server when present; a cached tile renders with no network access
  needed.
- A tile that fails to load (network error, non-2xx response) is simply not
  drawn — no error dialog, no crash, the rest of the diagram renders
  normally.
- Turning the map on/off, panning, and zooming while it's on never freezes or
  visibly stutters the UI (tile fetches are asynchronous).
- Given no airport is loaded, the checkbox has no effect / is not shown (same
  "empty, no exception" rule as the rest of the Diagram tab).
- `GeoProjection`'s local-meters projection is corrected to use WGS84
  meridian/prime-vertical radii of curvature at the reference latitude
  (replacing the previous flat equatorial constant), so a map tile several
  kilometers from the airport's reference point lines up correctly — this
  also very slightly improves runway placement accuracy at low-latitude
  airports. `ProjectLatLon`/`UnprojectLocalPoint` remain exact inverses of
  each other after the fix (pinned by test, not just informally true).

**Explicitly out of scope for this phase** (see the draft phase 1/2 entry
below): satellite imagery, any settings/API-key store, a per-airport manual
offset/opacity control, and persisting the "Show map" toggle.

**Test coverage:** `GeoProjectionTests` — the existing round-trip/
reference-point cases stay valid (the fix preserves exact-inverse behavior),
plus new absolute-accuracy cases asserting a 1° lat/lon offset matches known
WGS84 meridian/prime-vertical meters-per-degree values at several latitudes
(the previous suite only pinned round-trip consistency, not absolute
accuracy). `MapTileMathTests` — hand-calculated Web Mercator tile math
(lat/lon↔tile, zoom selection, tile screen-rect placement, visible-tile-set
computation). `MapTileDiskCacheTests` — round-trip, the 7-day retention floor
is honored even with no/a shorter server `max-age`, a longer server `max-age`
is honored, expiry returns a cache miss; always via an explicit temp base
directory, never `AppDataHelper.AppDataPath` (per CLAUDE.md's guardrail).
`MapTileServiceTests` — cache-then-fetch composition via fakes (cache hit
skips the source; cache miss fetches and stores; a source failure returns
null without storing). `MainViewModelTests` — `IsMapAvailable` reflects
whether a tile service was injected; `ShowMap` defaults to false and raises
`PropertyChanged`. `AirportDiagramProjectorTests` — a new fact confirms
`ReferenceLatitude`/`ReferenceLongitude` are set from the airport. Real
network behavior, the on-disk tile cache's real file I/O, and the tile
layer's on-screen rendering/pan/zoom responsiveness are verified manually per
CLAUDE.md's testing policy — not covered by `dotnet test`, consistent with
this diagram's existing zoom/pan handling above.

## v0.1+ — Raw airport data inspector (Airport Data tab)

**User story:** As a user, after loading an airport, I can open an "Airport Data"
tab and see an expandable tree of literally everything AirportSmith extracted for
it — every top-level field, every runway/frequency/parking spot/taxi
path/jetway, and every nested sub-structure (a runway's VASI/PAPI, pavement
extras, and approach light system) — so I can check exactly what data is (and
isn't) present for a given airport without being limited to whatever columns a
given tab's `DataGrid` happens to show, or wondering whether a field the Diagram
tab depends on (e.g. an approach light system) is actually absent for that
airport versus present but not rendering. Added per user request after an
approach light system added nothing visible to a loaded airport's diagram and it
wasn't obvious whether that airport simply had none.

**Acceptance criteria:**
- Given an airport is loaded (live from MSFS or via the Debug "Load Debug Data
  File" feature), the Airport Data tab shows a tree with one root row per
  top-level `AirportDetails` field/collection — scalar fields (`Icao`, `Name`,
  `Latitude`, `Longitude`, `ElevationMeters`, `MagneticVariationDeg`) render as a
  single non-expandable "Name: value" row; each collection (`Runways`,
  `Frequencies`, `ParkingSpots`, `TaxiPaths`, `Jetways`) renders as an expandable
  "Name (count)" row whose children are one row per item, labelled with a short,
  type-specific summary (e.g. a runway's `"[1] 09L/27R"`, a taxi path's name or
  `"(unnamed, type N)"` when blank) rather than a bare index.
- Given an item has a nested optional sub-structure (e.g. a `Runway`'s
  `PrimaryLeftVasiType`+`AngleDeg`, `PrimaryThreshold`/`BlastPad`/`Overrun`, or
  `PrimaryApproachLights`/`SecondaryApproachLights`), it renders as its own
  expandable row when present (its own properties as children, recursively) or a
  single "Name: (not present)" row when the value is `null` — clicking the arrow
  on any expandable row reveals its children, same interaction as expanding a
  node in a browser's XML/JSON tree view.
- The tree is generic/reflection-based (`AirportDataTreeBuilder`), not a
  hand-maintained per-field dump — a new field added anywhere in
  `AirportDetails`'s object graph appears in the tree automatically, without the
  tab needing to be updated by hand (unlike the per-tab `DataGrid`s, which
  already show most of this but only via `AutoGenerateColumns`' flat, one-row-
  per-item view with no way to inspect a nested sub-structure).
- Given a raw enum-backed `int` field that the SDK's own Facility Data
  reference documents with a name-per-value list, its leaf row shows that name
  alongside the number (e.g. `"SystemType: 9 (CALVERT)"`, `"Type: 6 (TOWER)"`)
  rather than the bare number — covers `ApproachLightSystem.SystemType`,
  `Runway`'s four VASI `*Type` fields, `Frequency.Type`, `TaxiParkingSpot.Type`/
  `NameCode`/`SuffixCode`, and `TaxiPathSegment.Type`. Labels are transcribed
  directly from the local MSFS 2024 SDK's own bundled docs (see the
  `AirportDataTreeBuilder.EnumLabelsByField` comment for the exact path), not
  guessed — a value with no matching entry (unrecognized, or a field with no
  lookup at all) shows the plain number, never a fabricated label.
  `Runway.SurfaceType` is deliberately excluded: the SDK's own docs promise a
  value list for `RUNWAY.SURFACE`/`HELIPAD.SURFACE` and then give none (a real
  gap in the SDK's own documentation, confirmed by reading both the rendered
  HTML and the raw markdown source) — showing a value with no textual mapping
  is honest; inventing one from unrelated enums (e.g. the aircraft's own
  `SURFACE TYPE` SimVar, which uses different numeric codes) would not be.
- Given no airport is loaded, the tab is empty (no exception).
- `AirportDataTreeBuilder` is pure and unit-tested directly (see Test coverage
  below) — no SimConnect/WPF dependency, matching `AirportDiagramProjector`'s
  approach.

**Known gaps carried forward (not blockers, monitor during manual testing):**
- Number formatting for `double`/`float` leaves uses `CultureInfo.InvariantCulture`
  (`"0.######"`) rather than the OS locale — deliberate, so the tab's output is
  consistent and pasteable regardless of the machine it runs on, and so tests
  don't depend on the runner's culture (this surfaced as a real test failure
  during development on a comma-decimal locale before the fix).
- The TreeView itself (`MainWindow.xaml`'s "Airport Data" tab) is view-layer WPF
  rendering, so it isn't covered by automated tests — verified manually (the app
  was smoke-tested to launch and stay responsive with the tab present, catching a
  XAML parse/binding crash, which builds/`dotnet test` cannot) per CLAUDE.md's
  testing policy. `AirportDataTreeBuilder`'s output itself is fully unit-tested.

## v0.1+ — Edit taxiway naming/lighting and runway lighting (Edit tab)

**User story:** As a user, after loading an airport, I can open an "Edit" tab
and change a taxi path's name and left/right edge lighting, and a runway's
edge light intensity, VASI/PAPI type+angle (per end/side), and approach light
system (per end) — then save those edits as a named project so they're still
there next time I load that airport, without needing MSFS running.

This is a narrower, now-committed slice of the "Edit runway/taxiway/parking
data" idea first drafted below — it covers naming/lighting only, not runway
geometry, taxiway routing, or parking spot editing, which remain future,
still-uncommitted work. **Out of scope, still not done:** none of this makes
edits take effect in MSFS — `<Airport>` XML generation and the package/build
flow (see the proposed epics below) are separate, still-unapproved epics; an
edit made here only changes AirportSmith's own project file.

Checked directly against the local MSFS 2024 SDK docs
(`Documentation/public/retail/programming-apis/simconnect/api-reference/
facilities/simconnect_addtofacilitydefinition`) before starting: `TAXI_PATH`
has `LEFT_EDGE_LIGHTED`/`RIGHT_EDGE_LIGHTED` (INT32 bools) and `RUNWAY` has
`EDGE_LIGHTS` (documented INT8: 0 NONE/1 LOW/2 MEDIUM/3 HIGH) — neither was
previously requested from SimConnect. VASI (`TYPE`+`ANGLE`, 4 slots) and
`APPROACH_LIGHTS.SYSTEM` were already extracted for the Airport Data tab, just
stored as raw `int`s — promoted to real enums (`VasiType`,
`ApproachLightSystemType`) as part of this epic since they're now user-facing
picker values, not just inspector text.

**Revised after first-round user feedback:** taxi path naming was originally a
free-text `TaxiPathSegment.Name` per row — flagged even at the time as a
known limitation, since the sim's real `TAXI_NAME` array is shared across
multiple `TAXI_PATH` rows, and flattening it broke that relationship (renaming
one row didn't rename the taxiway it was actually part of). Two things fixed
this:
1. Taxi names are now a first-class, user-managed list
   (`AirportDetails.TaxiNames`, a `List<TaxiName>` keyed by a stable `Guid`,
   not array position — see `TaxiName.cs`) with its own Add/Rename/Delete UI
   in the Edit tab, and `TaxiPathSegment.TaxiNameId` references it by that
   `Guid` instead of owning a string. Renaming or deleting an entry is
   instantly reflected everywhere it's referenced (the picker, the diagram),
   since every reference resolves the shared entry live rather than holding a
   copy.
2. The Edit tab's diagram (a second, independent `AirportDiagramView`
   instance, reusing the same control as the read-only Diagram tab) is now
   shown alongside the grids, giving spatial context for which taxi path is
   which — click one or more taxiways there (Ctrl+click to extend the
   selection) to batch-edit their name/lighting together via a popover,
   rather than hunting through grid rows one at a time.

**Revised after second-round user feedback:**
1. Editing a taxi path's name (via the grid picker or the batch popover) or
   renaming/deleting a Taxi Name now updates the diagram's label/styling for
   every affected taxiway immediately, without reloading — `TaxiwaySegmentShape`
   changed from an immutable record to a mutable, `INotifyPropertyChanged`
   class (`Name`/`HasName` alongside the already-mutable `IsSelected`), and
   `MainViewModel` recomputes them from `AirportDetails.TaxiNames` whenever a
   `TaxiPathEditViewModel.TaxiNameId` or `TaxiNameEditViewModel.Value` changes.
   Re-running `AirportDiagramProjector.Project` on every edit was deliberately
   avoided — it would also reset the diagram's zoom/pan/selection state, which
   nothing else in the Edit tab does on every keystroke.
2. Selecting one or more rows in the Taxi Paths grid now highlights the
   matching taxiway(s) in the diagram too (same `IsSelected` mechanism as a
   diagram click), for the reverse spatial lookup — "where is this row?" —
   but deliberately does **not** open the batch-edit popover, since a grid
   selection isn't a request to batch-edit. `MainViewModel` tracks which of
   the two selection-changing paths fired most recently
   (`ShowTaxiwayBatchEditPopover` vs. the older `HasTaxiwaySelection`, which
   now only means "some shape is highlighted, from either source").
3. Each taxi path row also has a "Hide from Diagram" checkbox
   (`TaxiPathEditViewModel.IsHiddenFromDiagram`) that hides its shape from the
   diagram (`TaxiwaySegmentShape.IsVisible`) — a decluttering convenience for
   busy airports, not an edited property of the airport itself, so it's
   **not** written to `TaxiPathSegment`/persisted by Save Project.

**Revised after third-round user feedback — bug fix and two refinements:**
1. **Bug fix:** batch-editing a selection of taxi paths was silently changing
   fields on selected paths beyond what the user actually touched in the
   popover. Root cause: the popover seeded its Name/lighting fields from
   whichever path happened to be first in the selection, and Apply then wrote
   all three fields to every selected path unconditionally — so selecting two
   paths that didn't already agree on a field (e.g. different names), then
   touching only one control in the popover (e.g. just a lighting checkbox),
   silently stamped the *other*, untouched field(s) from the first path onto
   every selected path too. Fixed by tracking which fields the user actually
   interacted with (`TaxiwayBatchEditViewModel.IsTaxiNameIdTouched`, and
   `LeftEdgeLighted`/`RightEdgeLighted` becoming tri-state `bool?` where
   `null` means "leave unchanged," bound to `IsThreeState="True"` checkboxes)
   instead of seeding from a path's current values and overwriting
   unconditionally. Apply now only writes a field to the selected paths if
   the user touched it, leaving everything else exactly as it was per-path.
   Covered by
   `MainViewModelTests.ApplyTaxiwayBatchEditCommand_UntouchedFields_LeaveEachSelectedPathsOwnValueUnchanged`
   and `TaxiwayBatchEditViewModelTests`. (When the popover's staged values
   actually get reset back to untouched was revised again below — see
   fourth-round feedback.)
2. The "Hide from Diagram" column is now the first column in the Taxi Paths
   grid (was last).
3. Hiding from the diagram is now also available for runways: each Runway
   row has its own "Hide from Diagram" checkbox (first column,
   `RunwayEditViewModel.IsHiddenFromDiagram`), which hides that runway's
   shape — including its nested pavement-extension/threshold-marking/
   approach-light overlays, since they render inside the same per-runway
   `Canvas` whose `Visibility` this now controls — the same
   display-only-convenience, not-persisted contract as the taxi path version
   (`RunwayShape` gained the same `SourceIndex`/`IsVisible` pair as
   `TaxiwaySegmentShape`, becoming a mutable class for the same reason).

**Revised after fourth-round user feedback — two more bugs in the same
batch-edit flow, both now fixed:**
1. **Bug:** extending a diagram selection (Ctrl+clicking another taxiway
   while the batch-edit popover was already open) silently reset the
   popover's staged values back to "nothing touched" — so anything the user
   had already picked before extending the selection was lost, without any
   visible sign of it (the popover's `ComboBox` can keep displaying a
   previously picked name even after its bound value resets to `null`, since
   WPF doesn't necessarily clear a `ComboBox`'s own display just because
   `SelectedValue` no longer matches any item). **Fix:** `MainViewModel` now
   only resets `TaxiwayBatchEdit` to untouched when a batch-edit *session*
   actually ends (the popover closes via Apply/Cancel, or the selection
   becomes empty/grid-driven) — not on every incremental selection change
   while it's open, so a whole session's staged values now survive however
   the selection is built up.
2. **Bug (the more serious one — this is what the user actually observed as
   "changed paths that weren't selected"):** WPF's `DataGrid` marks a row
   "selected" as a side effect of clicking *any* cell in it — including just
   toggling an unrelated row's Left/Right Edge Lighted or Hide checkbox.
   `SyncTaxiwaySelectionFromRows` (added for grid-row-selection-highlights-
   diagram, see second-round feedback above) had no guard against this, so
   an incidental grid click while a diagram-driven batch-edit session was
   active would silently replace the diagram's whole selection with whatever
   row was just clicked — meaning Apply would then hit that row instead of
   the ones actually selected on the diagram, matching the report exactly
   ("not applied to selected paths but most of the not selected"). **Fix:**
   `SyncTaxiwaySelectionFromRows` is now a no-op while
   `ShowTaxiwayBatchEditPopover` is true — grid clicks can't drive the
   diagram highlight again until the active popover session is closed
   (Apply or Cancel). Both bugs are covered by dedicated regression tests:
   `MainViewModelTests.ExtendingDiagramSelection_DoesNotResetAlreadyStagedBatchEditValues`,
   `SyncTaxiwaySelectionFromRows_WhilePopoverOpen_IsIgnored`, and
   `SyncTaxiwaySelectionFromRows_AfterPopoverCloses_WorksAgain`.

**Revised after fifth-round user feedback — a blank taxi name looked like an
empty/junk row:** after the fourth-round fixes, the user correctly renamed a
taxi name via the Taxi Names panel and was surprised it applied to "numerous
paths" — not a bug this time: the sim's own `TAXI_NAME` array can (and
typically does) include a blank entry, and many taxi paths that genuinely
have no name (e.g. ones leading to parking) legitimately share `TaxiNameId`
pointing at it, sometimes dozens at once — confirmed by
`taxiway-name-research.md`'s ~65%-unnamed finding for a real airport. The
panel showed that entry as an indistinguishable blank row, giving no hint it
was meaningful or heavily shared before editing it. Fixed by adding
`TaxiNameEditViewModel.DisplayValue` (shows `"(no name)"` instead of blank,
styled gray/italic in the Taxi Names panel) and `IsUnnamed`, used everywhere
a `TaxiName` is displayed — the panel's Name column (now a
`DataGridTemplateColumn`: `CellTemplate` shows the styled `DisplayValue`,
`CellEditingTemplate` still edits the real `Value` directly, so it stays a
name like any other if that's genuinely wanted) and both Name pickers'
`DisplayMemberPath` (the Taxi Paths grid's row picker and the batch
popover's). Covered by `TaxiNameEditViewModelTests`.

**Revised after sixth-round user feedback — checkboxes needed an extra
click:** every checkbox in the Edit tab's grids (Hide from Diagram on both
taxi paths and runways, Left/Right Edge Lighted) needed two clicks to toggle
— a well-known WPF `DataGridCheckBoxColumn` quirk: the checkbox it renders
isn't actually interactive until its cell becomes "current" (selected), so
the first click just selects the cell and only the second one toggles it.
Fixed by replacing all four with `DataGridTemplateColumn`s containing a real
`CheckBox` directly in the `CellTemplate` — an always-interactive control
that toggles on the first click, since it isn't gated behind the grid's
edit-mode/cell-selection state the way `DataGridCheckBoxColumn`'s checkbox
is. Pure XAML/rendering change, nothing in `MainViewModel` or below moved —
not covered by automated tests, verified manually, same as the rest of this
tab's WPF interaction.

**Revised (2026-09-14) — added taxi path type/runway-association/edge-type/
center-line editing:** the Edit tab's Taxi Paths grid now also exposes, per
row: `Type` (the sim's TAXI_PATH.TYPE, e.g. Taxi/Runway/Parking/Path/Closed/
Vehicle/Road/PaintedLine), `RunwayNumber`+`RunwayDesignator` (which runway
this path is associated with, e.g. an entrance/exit taxiway near a specific
runway end), `LeftEdge`/`RightEdge` (the edge paint marking — None/Solid/
Dashed/SolidDashed — distinct from the already-existing
`LeftEdgeLighted`/`RightEdgeLighted`), and `CenterLine`/`CenterLineLighted`.
All six were newly requested from SimConnect for this change — confirmed
against `docs.flightsimulator.com`'s `SimConnect_AddToFacilityDefinition`
reference (`TYPE`, `WIDTH`, `RUNWAY_NUMBER`, `RUNWAY_DESIGNATOR`,
`LEFT_EDGE`, `LEFT_EDGE_LIGHTED`, `RIGHT_EDGE`, `RIGHT_EDGE_LIGHTED`,
`CENTER_LINE`, `CENTER_LINE_LIGHTED`, `START`, `END`, `NAME_INDEX`, all
INT32 except `WIDTH`/FLOAT32) before adding the `SimConnectService`
`AddToFacilityDefinition` calls/struct fields.

**Confirmed against a live sim (2026-09-14) — `LEFT_EDGE`/`RIGHT_EDGE`
reading almost always NONE is real sim data, not a bug:** the user
reported `LeftEdge`/`RightEdge` never showing anything but `NONE`, despite
MSFS visibly rendering taxiway edge lines, and asked for the read path to
be checked. Re-verified field names/types/order directly against the
**local** SDK docs shipped with the sim (`C:\MSFS 2024 SDK\Documentation\
public\retail\programming-apis\simconnect\api-reference\facilities\
simconnect_addtofacilitydefinition\index.md`) rather than the web mirror —
exact match, including the `LEFT_EDGE`/`RIGHT_EDGE` enum (0 NONE/1 SOLID/2
DASHED/3 SOLID_DASHED) the user independently guessed. `SimConnectService`'s
struct field order, `AddToFacilityDefinition` request order, and the
`OnFacilityData` → `TaxiPathSegment` mapping were all re-audited line by
line against that doc and found correct — no swap, no off-by-one, no wire
misalignment. Live-tested against a running MSFS session at EFHK (2804 taxi
paths, via **Export Debug Data**): `LeftEdge` is `0` on all 2804 rows,
`RightEdge` is `0` on 2803 and `1` (SOLID) on exactly one — proving the
field genuinely round-trips non-default values when the sim sends them,
not stuck at a hardcoded default. In the same export, `RunwayNumber`/
`RunwayDesignator`/`CenterLine`/`CenterLineLighted` all show rich,
plausible variance on the identical rows (e.g. `RunwayNumber: 15` on
`Type: RUNWAY` rows, correctly matching EFHK's real runway 15/33) — if the
struct were misaligned, these neighboring fields would be corrupted too,
and they aren't. Conclusion: the code is correct; EFHK's own taxi path data
just doesn't set `LEFT_EDGE`/`RIGHT_EDGE` for almost any path. The most
likely explanation (not itself confirmed) is that a heavily hand-modeled
airport like EFHK renders the edge striping the user sees via baked ground-
polygon/texture art rather than the legacy procedural taxiway-edge system
`LEFT_EDGE`/`RIGHT_EDGE` feeds — ground polygon visuals are already a
documented Facility Data gap for this project (see the "Non-goals for
v0.1" note near the top of this file). Worth re-checking against a smaller,
less heavily custom-art default airport if this needs firmer confirmation.
`TaxiPathSegment.Type` was promoted from a raw `int` to a real enum
(`TaxiPathType`, now covering the SDK's full documented 0-8 range —
previously stopped at 6, missing `Road`/`PaintedLine`) since it's now a
user-facing Edit tab picker value, the same promotion `VasiType`/
`ApproachLightSystemType` went through earlier in this epic;
`AirportDataTreeBuilder`'s separate `TaxiPathTypeLabels` lookup dictionary
was removed accordingly (the enum's own `ToString()` now renders the member
name). `RunwayDesignator`/`LeftEdge`/`RightEdge` are new real enums
(`TaxiPathRunwayDesignator`, `TaxiEdgeType`) for the same reason.
`RunwayNumber` is kept as a raw `int` (the SDK's documented 1-36 are literal
runway numbers, self-explanatory without a name; 37-44/45 are a small
compass-heading-plus-sentinel tail, given a friendly label in
`AirportDataTreeBuilder` instead of a dedicated enum, the same treatment as
`TaxiParkingSpot.NameCode`'s `GATE_A`..`GATE_Z` tail). All six new fields
also show up in the read-only "Airport Data" tab automatically, since that
tab's tree is built reflectively over whatever fields `TaxiPathSegment` has
(see `AirportDataTreeBuilder`) — no separate work was needed there beyond
the `RunwayNumber` label dictionary above. Covered by
`TaxiPathEditViewModelTests.SettingTypeRunwayAssociationAndEdgeFields_WritesThroughToWrappedSegment`,
new `AirportDataTreeBuilderTests` cases for the enum-rendered and
label-dictionary-rendered fields, and an extended
`AirportProjectStoreTests.Load_OldFileMissingNewFields_...` asserting all
six default cleanly from an old project file that predates them, per
`CLAUDE.md`'s back-compat rule.

**Revised (2026-09-14) — diagram click now filters the grid instead of
opening the popover directly:** per user feedback, clicking (or Ctrl+
clicking) a taxiway in the Edit tab's diagram used to select it AND
immediately pop the batch-edit popover open. That's now split into two
separate gestures:
1. **Left-click (or Ctrl+click) selects and filters, nothing more.**
   `ToggleTaxiwaySelectionCommand` only builds the selection now — it no
   longer touches whether the popover shows. `MainViewModel.VisibleTaxiPathEdits`
   is a new property (the Taxi Paths grid's `ItemsSource`, replacing
   `TaxiPathEdits` there) that `RefreshTaxiwayFilter` recomputes on every
   diagram-driven selection change: all of `TaxiPathEdits` when nothing's
   selected, or just the row(s) whose `SourceIndex` matches a selected shape
   otherwise. `TaxiPathEdits` itself is untouched and still the one every
   other index-by-`SourceIndex` consumer (`ApplyTaxiwayBatchEdit`,
   `RefreshAllTaxiwayLabels`/`Visibility`) uses, so nothing about how an edit
   actually gets applied changed. Deliberately **not** wired into
   `SyncTaxiwaySelectionFromRows` (the existing grid-row-selection-
   highlights-diagram path) — filtering the grid down to whatever rows the
   user just selected *in that same grid* would collapse the rest of it out
   from under a normal multi-row browsing/selection gesture, which isn't
   what was asked for; only a diagram-originated selection change filters.
2. **A plain click (no drag) on empty diagram space clears the selection**
   (`ClearTaxiwaySelectionCommand`, new), which drops the grid's filter back
   to "show everything." `AirportDiagramView.EndPan` now distinguishes a
   click from a pan-drag by movement distance (≤3 device-independent pixels)
   between mouse-down and mouse-up, and only fires the command for a
   below-threshold release that started on empty space (a click that started
   on a taxiway shape never sets `_isPanning` in the first place — see
   `TaxiwayShape_MouseLeftButtonDown` — so it can't spuriously clear the
   selection it just made).
3. **Right-click opens the popover** for whatever's currently selected
   (`OpenTaxiwayBatchEditCommand`/`TaxiwayContextMenuCommand`, new) —
   regardless of what's directly under the cursor, since right-click never
   changes the selection itself, only whether the popover shows.
   `ShowTaxiwayBatchEditPopover` is now `HasTaxiwaySelection &&` an explicit
   "was the popover requested via right-click" flag
   (`_batchEditPopoverRequested`), replacing the old
   `_selectionCameFromDiagram` flag entirely (that flag's only job was
   gating this same popover-visibility check, which is now handled directly
   by the right-click flag instead — nothing else needed it). The flag
   resets to false whenever the popover actually closes (selection cleared,
   Apply, or Cancel), so a freshly built selection always needs its own
   right-click rather than the popover reopening on its own.

Only taxiways are click-selectable in this round — runways currently have
no click handling in the diagram at all (no `IsSelected` on `RunwayShape`,
no click-command wiring), so extending this same filter/select/right-click
model to the Runways grid is explicitly out of scope here and would need
its own follow-up epic.

Covered by new `MainViewModelTests`:
`ToggleTaxiwaySelectionCommand_AloneDoesNotOpenPopover`,
`OpenTaxiwayBatchEditCommand_WithADiagramSelection_OpensPopover`,
`OpenTaxiwayBatchEditCommand_WithNoSelection_CanExecuteIsFalse`,
`ClickingDiagramTaxiway_FiltersVisibleTaxiPathEditsToSelection`,
`ClosingBatchEditPopover_ShowsEveryTaxiPathAgain`, plus updates to the
existing `SyncTaxiwaySelectionFromRows_WhilePopoverOpen_IsIgnored`/
`SyncTaxiwaySelectionFromRows_AfterPopoverCloses_WorksAgain`/
`ExtendingDiagramSelection_DoesNotResetAlreadyStagedBatchEditValues` to open
the popover via `OpenTaxiwayBatchEditCommand` explicitly, matching the new
flow, rather than relying on selection alone. The click-vs-drag distance
threshold and right-click wiring in `AirportDiagramView` itself are real WPF
mouse-input behavior, not covered by automated tests — verified manually,
same as the rest of this tab's mouse interaction.

**Bug fix (2026-09-14, same day) — clicking a taxiway filtered the grid but
never actually highlighted it, Ctrl+click multi-select didn't stick, and
right-click had nothing to open a popover for:** reported immediately after
the above landed. Root cause, found by bisecting against the last-committed
build and instrumenting the live app (see below) rather than by inspection
alone — reasoning about the code didn't surface it: reassigning
`VisibleTaxiPathEdits` swaps the Taxi Paths grid's `ItemsSource`, and WPF's
`DataGrid` clears its own selection and raises `SelectionChanged` as a side
effect of that — not once, but as a short burst of several such events,
**asynchronously**, on a later Dispatcher pass (observed ~60ms+ after the
`ItemsSource` assignment returned, all within about 2ms of each other).
`MainWindow.xaml.cs`'s `TaxiPathsGrid_SelectionChanged` wires that straight
into `SyncTaxiwaySelectionFromRows`, so each of those events arrived with
the grid's now-empty selection and stamped every diagram shape's
`IsSelected` back to `false` — silently undoing the very selection that
triggered the filter change in the first place. `VisibleTaxiPathEdits`
itself stayed correct (it isn't recomputed by that second call), which is
exactly what made the grid filter correctly while nothing ever highlighted.
Diagnosed by: (1) bisecting file-by-file against a build restored to the
last git commit (via `git stash`/`git show HEAD:<file>` — never touching
the user's own already-running instance, which held the default
`bin\Debug` output locked for the whole investigation, so every test build
in this bisection used `dotnet build -o <isolated temp dir>`) to confirm
the regression was real and narrow it down to `MainWindow.xaml`; (2)
temporary `File.AppendAllText` instrumentation in
`TaxiPathsGrid_SelectionChanged`/`ToggleTaxiwaySelection` (removed once the
fix was confirmed) to see the actual event timing, which is what revealed
the async, multi-event burst — a same-call-stack guard flag in
`MainViewModel` (the first fix attempted) couldn't work against that timing
and was replaced. **Fix:** `TaxiPathsGrid_SelectionChanged` now tracks the
grid's `ItemsSource` reference and debounces for a 300ms settle window
after it changes (`_lastTaxiPathsItemsSource`/
`_lastTaxiPathsItemsSourceChangedAt`/`TaxiPathsItemsSourceSettleWindow` in
`MainWindow.xaml.cs`) before forwarding to
`SyncTaxiwaySelectionFromRows` — long enough to absorb the whole burst
(observed within ~2ms) but short enough that a genuine user row click,
which is never sub-300ms after an unrelated diagram click, still goes
through normally. Verified against a live MSFS session (EFHK) after the
fix: click highlights and filters to one path, Ctrl+click extends to two
with both highlighted and both grid rows shown, right-click opens the
popover scoped to exactly that two-path selection. Not (and cannot easily
be) covered by an automated test — it's WPF's own internal `DataGrid`
event-timing behavior, not application logic; verified manually, same as
the rest of this tab's mouse interaction.

**Bug fix (2026-09-17), reported as "crash to desktop" while adding a taxi
name:** clicking **Add Name** then clicking the new blank row to start
typing its name crashed the app. Root-caused via a real repro plus the
Windows Application/`.NET Runtime` event log entries it left behind
(`System.InvalidOperationException: 'DeferRefresh' is not allowed during an
AddNew or Edit Item transaction`, thrown from
`ItemsControl.OnItemsSourceChanged` while a `BindingExpression` was
reattaching): both the Taxi Names grid itself and every Taxi Paths row's
Name-column `ComboBox` (one per row — hundreds for a real airport) bound
`ItemsSource` directly to the same `TaxiNames` `ObservableCollection`
instance. WPF's `CollectionViewSource` caches one shared default
`ICollectionView` per source collection instance, so all of those controls
shared the Taxi Names grid's own view — and the grid puts that view into an
"edit item" transaction the moment a row starts editing. If WPF
independently re-attached even one Name `ComboBox`'s binding to that same
shared view during that window (ordinary row-container virtualization as
the Taxi Paths grid scrolls, or `VisibleTaxiPathEdits` being reassigned by
`RefreshTaxiwayFilter` — both unrelated to Taxi Names on their face), the
conflicting `DeferRefresh()` call crashed the process. The new per-column
Taxi Paths filtering feature made `RefreshTaxiwayFilter` fire far more
often, which is almost certainly why this pre-existing landmine became easy
to hit only now. **Fix:** `MainViewModel.TaxiNamesPicker` is a
`ReadOnlyObservableCollection<TaxiNameEditViewModel>` wrapping the same
`TaxiNames` list — a genuinely separate collection instance, so it gets its
own independent default view, while still mirroring every add/rename/delete
live (`CollectionChanged` forwards through the wrapper; each item's own
`INotifyPropertyChanged` is unaffected either way). Every Name picker
*except* the Taxi Names grid itself now binds to `TaxiNamesPicker` instead
of `TaxiNames` directly. Regression-tested
(`TaxiNamesPicker_IsASeparateInstanceFromTaxiNames_ButMirrorsItsContentsLive`)
and confirmed against a real repro (the exact crash sequence — Add Name,
click the new row repeatedly, type — no longer terminates the process).

**UX fix, same report:** the Taxi Names grid's Name column previously needed
three clicks after Add Name before typing actually worked — a
`DataGridTemplateColumn`'s `CellEditingTemplate` only activates once a cell
is already "current," the same well-known WPF quirk this file's checkbox
columns already work around (see their own comment) by using a real,
always-interactive control directly in `CellTemplate` instead of a
template-switching pair. Fixed the same way: a single always-editable
`TextBox` (no separate `CellEditingTemplate`) — a single click now positions
the caret and typing works immediately. The blank-entry "(no name)" warning
(added after a previous incident — see below) is preserved as a watermark
`TextBlock` behind the (transparent-background) `TextBox`, shown only while
`IsUnnamed` and marked `IsHitTestVisible="False"` so a click on the
watermark itself still reaches the `TextBox` underneath.

**Known limitations:**
- While a diagram-driven batch-edit session is open (the popover is
  showing), the Taxi Paths grid's own row selection can't drive the diagram
  highlight (see fourth-round fix #2 above) — the user must Apply or Cancel
  first. A deliberate trade-off for correctness (an incidental grid click
  can no longer silently hijack an in-progress batch edit), not something
  planned to be relaxed further without a more robust way to distinguish
  "deliberate grid multi-select" from "incidental single-cell click."
- `RUNWAY.EDGE_LIGHTS` is marshaled as `sbyte` (INT8) per the SDK docs — the
  only INT8-sized field requested anywhere in this codebase, and this project
  has previously hit a doc-declared-size-vs-actual-wire-size bug (see the
  FLOAT32/FLOAT64 note in `SimConnectService.cs`). **Unconfirmed against a
  live sim** — if `SURFACE`/`PRIMARY_NUMBER`/etc. come back corrupted on a
  real airport, widen `FacilityRunwayData.EdgeLights` to `int` and re-verify.
- The read-only "Taxi Paths" tab (distinct from the Edit tab) still
  auto-generates its columns reflectively, so it now shows a raw
  `TaxiNameId` GUID instead of a friendly name — a cosmetic regression versus
  the old flat `Name` string column, deliberately not fixed here (would need
  an `AutoGeneratingColumn` handler or hand-written columns) since the tab is
  already a raw/unlabelled dump for every other enum-backed field (see the
  "Airport Data" tab for the friendly, resolved view).

**Acceptance criteria:**
- Given an airport is loaded, the Edit tab shows: a Taxi Names panel (one
  editable row per name, with Add/Delete); one editable row per taxi path
  (a Name picker drawing only from that list — not free text — a Type
  picker, an associated runway number field + designator picker, left/right
  edge type pickers, left/right edge lighting checkboxes, and center
  line/center line lighted checkboxes); one editable row per runway (edge
  light intensity, VASI/PAPI type+angle for all four end/side slots, and
  approach light system for both ends, each as a picker); and the airport's
  diagram. Edits write immediately to the loaded `AirportDetails`
  (`TaxiPathEditViewModel`/`RunwayEditViewModel`/`TaxiNameEditViewModel`), no
  separate "apply" step for single-row edits.
- Deleting a Taxi Name clears `TaxiNameId` on every taxi path that referenced
  it (no row is left pointing at a name that no longer exists).
- Setting a runway's VASI/PAPI or approach light picker to "(none)" clears
  that field back to "not installed" (`null`); picking a value (re)creates it.
- Clicking a taxiway in the Edit tab's diagram selects it (replacing any
  existing selection); Ctrl+click adds/removes it from a multi-selection —
  only taxi paths can be part of a selection at once (runways/parking spots
  aren't click-selectable in this epic). Selected taxiways are visually
  highlighted, and the Taxi Paths grid filters down to show only the
  selected path(s) (see the "diagram click filters the grid" note below).
  A plain click (no drag) on empty diagram space clears the selection and
  the grid's filter. With one or more selected, right-clicking the diagram
  opens a popover that lets the user set a name and/or left/right lighting
  and apply only the field(s) actually touched to every selected path at
  once — an untouched field is left exactly as it was on each individual
  path, never overwritten with another selected path's value (see the
  third-round feedback above); Cancel or Apply both clear the selection (and
  the grid's filter) afterward. The read-only Diagram tab's existing
  pan-by-dragging-anywhere behavior (including over a taxiway) is unchanged,
  since it never wires up the click-to-select/clear-selection/context-menu
  commands.
- Renaming a taxi path (via its grid picker or the batch popover), or
  renaming/deleting an entry in the Taxi Names panel, updates the diagram's
  taxiway label(s) and named/unnamed styling for every affected path
  immediately — no reload, and without disturbing the diagram's current
  zoom/pan or selection.
- Selecting one or more rows in the Taxi Paths grid highlights the matching
  taxiway(s) in the diagram (same highlight style as a diagram click) but
  does not open the batch-edit popover; selecting via the diagram itself
  still does.
- Checking a taxi path or runway row's "Hide from Diagram" box (the first
  column in each grid) hides its shape from the diagram (checking it for a
  taxi path also clears its selection/highlight if it was selected);
  unchecking it restores visibility. This is a display-only convenience for
  decluttering a busy airport — it is not saved by Save Project and does not
  affect `TaxiPathSegment`/`Runway`.
- The Taxi Paths grid also shows read-only **Start**/**End** columns —
  `TaxiPathEditViewModel.StartIndex`/`EndIndex`, the sim's own `TAXI_POINT`
  indices this path connects, matching the Taxiway Points grid's own rows and
  the exported `<TaxiwayPath start="..."/end="...">` — so a path can be
  cross-referenced against the specific points it connects without switching
  tabs or guessing from the diagram.
- Every column in the Taxi Paths grid has its own filter, entered directly in
  that column's header (a text box under the label for free-text/numeric
  columns — Name, Start, End, Rwy # — matched as a case-insensitive substring
  against the field's displayed text; a dropdown for enum/bool columns —
  Type, Rwy Designator, Left/Right Edge, Left/Right Edge Lighted, Center
  Line, Center Line Lighted, Hide from Diagram — with a leading "(any)"
  meaning no filter on that column). All active filters combine with (AND);
  a **Clear Filters** button next to the grid's header resets every one at
  once and is only enabled while at least one is set
  (`MainViewModel.TaxiPathFilter`/`ClearTaxiPathFilterCommand`). Filtering
  combines with (AND), rather than replaces, the diagram's existing
  click-to-select filter — selecting taxiways on the diagram narrows the
  candidate rows first, then each column filter narrows that further
  (`MatchesTaxiPathFilter`/`RefreshTaxiwayFilter`). A filter re-applies live
  as a row's own field is edited (e.g. changing a path's Type away from an
  active Type filter's value drops it out of the grid immediately) and every
  filter resets to cleared when a new airport is loaded.
  - **Known WPF quirk hit building this (2026-09-17):** binding a header's
    filter control the same way every other cross-tab binding in this
    codebase does (`RelativeSource AncestorType=Window` +
    `DataContext.PropertyName`) silently failed to push edits back to the
    source — the header `TextBox` itself displayed typed text fine (so the
    control and its local value were working), but the bound
    `TaxiPathFilter` property it was supposed to update never actually
    changed, confirmed directly with a temporary on-screen debug binding.
    Routing through the `DataGrid`'s own `Tag` (`RelativeSource
    AncestorType=DataGrid`) — the usual documented workaround for this exact
    "controls inside a `DataGridColumn.Header` can't reach page-level data"
    class of problem — didn't fix it either. `DataGridColumn.Header` content
    apparently doesn't reliably participate in the normal visual-tree
    `RelativeSource` walk the way an ordinary sibling control does (e.g. the
    Taxiway Points grid's "Hide All from Diagram" checkbox, confirmed
    working). What did work: each filter header's own root `StackPanel` sets
    its `DataContext` explicitly, once, in a `Loaded` handler
    (`MainWindow.xaml.cs`'s `TaxiPathFilterHeader_Loaded`), so its filter
    controls can then use a plain one-level `{Binding PropertyName}` — same
    as a normal `DataGrid` cell template binds directly to its row item.
    Root cause not otherwise identified; noted here so a future header-hosted
    control in this app doesn't rediscover the same dead end.
- The Edit tab also shows a **Taxiway Points** grid: one row per distinct sim
  `TAXI_POINT` index resolved from the loaded airport's taxi paths (every
  path's Start, plus its End unless the path is `Type==Parking`, whose End
  references a `TaxiwayParking` item instead — same distinct-index synthesis
  `AirportXmlExporter.BuildTaxiwayPoints` already used at export time, see
  `TaxiwayPointEditViewModel.BuildAll`), with read-only Index/X/Z columns and
  editable Type/Orientation pickers that write through to every taxi path
  segment sharing that point (keeping them consistent, unlike the raw
  per-segment fields which could otherwise disagree). The same points are
  drawn as small dots on both diagrams (`AirportDiagramProjector`'s
  `TaxiwayPoints`, rendered by `AirportDiagramView`'s dedicated
  `ItemsControl`, positioned via a `Path`+`EllipseGeometry` with an absolute
  `Center` rather than `Canvas.Left`/`Top` on the template root like every
  other marker in this view — that more usual approach was tried first and
  confirmed broken specifically for this `ItemsControl` against a real
  airport, with every generated point stacking at the same spot instead of
  its own position; root cause not identified, worked around by switching to
  the `EllipseGeometry` mechanism `ParkingSpots` already used successfully)
  — deliberately not filtered to Taxi/Path-typed paths like `TaxiwaySegments`
  is, so a `Runway`-type path's points are visible too (see this epic's
  earlier note on `Runway`-typed rows having no other way to be visually
  sanity-checked on the diagram). Points resolve to one of two colors: **red**
  for `TaxiPointType.Normal`/unresolved, **yellow** for any of the four
  hold-short variants (`HoldShort`/`IlsHoldShort`/`HoldShortNoDraw`/
  `IlsHoldShortNoDraw`) — `TaxiwayPointShape.IsHoldShort`, set by
  `AirportDiagramProjector.Project` from whichever segment's
  `StartPointType`/`EndPointType` first resolved that index (same first-wins
  dictionary as the point's own screen position). Selection's orange
  highlight still overrides both colors (`AirportDiagramView`'s `IsSelected`
  `DataTrigger` is evaluated after `IsHoldShort`'s). Covered by
  `AirportDiagramProjectorTests`
  (`Project_TaxiwayPoints_HoldShortVariant_SetsIsHoldShort`,
  `Project_TaxiwayPoints_NullOrNormalPointType_IsNotHoldShort`); the color
  mapping itself is WPF `DataTrigger` styling, verified manually per this
  file's testing conventions, not by an automated test.
  - A single **Hide All from Diagram** checkbox next to the grid header (not
    a per-row checkbox like taxiways/runways — there can be hundreds of
    points, so per-row would be impractical) toggles every point's
    visibility at once (`MainViewModel.HideAllTaxiwayPoints`); hiding also
    clears any current point selection. Display-only, like every other
    hide-from-diagram toggle — never persisted, never touches
    `TaxiPathSegment`. Resets to unchecked (and every point back to visible)
    whenever a new airport is loaded.
  - Clicking one or more red dots on either diagram (Ctrl+click to extend
    the selection, same interaction as a taxiway path — see
    `TaxiwayPointShape_MouseLeftButtonDown`/
    `MainViewModel.ToggleTaxiwayPointSelection`) filters the Taxiway Points
    grid down to just the selected point(s)
    (`MainViewModel.VisibleTaxiwayPointEdits`/`RefreshTaxiwayPointFilter`,
    matched to grid rows by the shared sim `TAXI_POINT` index rather than
    list position, since `TaxiwayPointEdits` isn't a 1:1 same-order wrapper
    over a raw `AirportDetails` list the way `TaxiPathEdits` is). This is an
    independent selection from the taxiway-path one (selecting a point
    doesn't affect path selection or vice versa) with no batch-edit popover
    of its own; a plain click on empty diagram space clears both selections
    at once (`ClearTaxiwaySelectionCommand`, shared with the taxiway-path
    clear).
- Given edits have been made, clicking **Save Project** writes an
  `AirportProjectFile` (schema v2: `SchemaVersion`, `SavedAtUtc`, `Airport`)
  to `AppDataHelper.AppDataPath\Projects\{ICAO}.json` via
  `IAirportProjectStore`, and shows the path saved to.
- Given a project was previously saved for the currently-typed ICAO, clicking
  **Load Project** loads it (bypassing SimConnect entirely — no sim needs to
  be running) and populates Airport/Diagram/Airport Data tab/Edit tab exactly
  as a live load would; given no project exists for that ICAO, it sets an
  error message instead and leaves the current state untouched.
- Given a project file from schema v1 (this epic's first, since-superseded
  shape — free-text `TaxiPathSegment.Name`, no `TaxiNames` list) or missing
  any field a later schema version added, loading it does not throw: fields
  simply missing from the file come back as their type default
  (`false`/`None`/`null`) per `CLAUDE.md`'s back-compat rule, and a v1 file's
  legacy names are actively migrated into the v2 `TaxiNames`/`TaxiNameId`
  shape rather than silently discarded — including restoring the "multiple
  paths share one taxiway name" relationship the v1 flat-string shape had
  broken, by deduplicating identical legacy name strings into one shared
  `TaxiName`. Verified by `AirportProjectStoreTests.Load_OldFileMissingNewFields_...`
  and `Load_V1FileWithLegacyNames_MigratesToTaxiNamesAndTaxiNameId` — this is
  the concrete baseline future schema changes must keep satisfying.
- The Edit tab (including its embedded diagram, the click-to-select
  interaction, and the batch-edit popover's placement/rendering), its
  Save/Load Project buttons, and the underlying `AirportProjectStore`/
  `SimConnectService` I/O are not covered by automated tests (real WPF
  rendering/mouse input and real file/SimConnect I/O) — verified manually,
  including the `EDGE_LIGHTS` INT8 risk above, which needs a live sim with a
  real airport of known edge-light intensity to confirm.

### Amendment: VASI/PAPI position editing, diagram rendering, and click-to-place

**User story:** As a user, after enabling a VASI/PAPI on a runway that
didn't have one, I can see where it will sit relative to the runway on the
diagram (rather than guessing meaningless Bias X/Z numbers blind), and
either type numbers or click a spot on the diagram to place it there.

Before this, `Runway.*VasiBiasXMeters`/`BiasZMeters`/`SpacingMeters` (added
in the XML-export epic below, for `<Vasi>`'s required `biasX`/`biasZ`/
`spacing` attributes) were extracted from SimConnect but had no Edit tab UI
at all — only Type/AngleDeg were editable, so a VASI/PAPI enabled from
"(none)" had no way to get a real position and exported at `0,0,0` with a
warning (see `AirportXmlExporter.AddVasi`).

- The Runways grid gains, per VASI/PAPI slot (`Pri L`/`Pri R`/`Sec L`/`Sec R`
  × 4): an editable **X**/**Z**/**Spacing** column (`RunwayEditViewModel`'s
  new `*VasiBiasXMeters`/`BiasZMeters`/`SpacingMeters` properties, writing
  straight through to the existing `Runway` fields) and a **Place** button.
- Picking a Type for a slot that was previously "(none)" auto-suggests a
  starting position — 300m inward from that end's threshold
  (`BiasZMeters`), 0m lateral offset (`BiasXMeters`), 15m `SpacingMeters` —
  rather than leaving it at the `0,0,0`-plus-warning default. Re-picking a
  *different* Type on an already-positioned slot (e.g. real sim-extracted
  data) never overwrites that position — the suggestion only fires when
  Bias X/Z/Spacing are all still completely unset
  (`RunwayEditViewModelTests.EnablingVasiFromNone_SuggestsDefaultPosition300MetersFromThreshold`/
  `ChangingVasiTypeOnAlreadyPositionedSlot_DoesNotOverwritePosition`/
  `ClearingVasiTypeToNull_DoesNotApplyDefaultPosition`, plus one fact per
  remaining slot).
- `AirportDiagram` gains `VasiLights` — unlike `TaxiwaySegments`/
  `TaxiwayPoints` (which only exist for what the airport actually has),
  `AirportDiagramProjector.Project` always creates exactly one `VasiShape`
  per slot per runway, and `IsInstalled` (that slot's Type != null) drives
  whether it's drawn — so enabling a VASI purely through the Type picker (no
  reload) just flips an existing shape visible. Position is a small cyan
  marker plus a short perpendicular "wing bar" line sized from
  `SpacingMeters` — a diagram-level schematic, not a literal light-by-light
  reproduction of PAPI's 2/4 lights or VASI's near/far bars, same
  simplification precedent as the approach-light rail. `BiasZMeters` is
  measured inward from that slot's own end's threshold and `BiasXMeters`
  perpendicular to the centerline — like `TaxiPathSegment`'s own BIAS_X/
  BIAS_Z, this axis/sign convention is **UNCONFIRMED against a live sim**,
  it's the best-documented assumption pending verification.
  (`AirportDiagramProjectorTests.Project_VasiLights_AlwaysFourSlotsPerRunway_OnlyInstalledOneMarkedInstalled`).
- Editing a slot's Type/BiasX/BiasZ/Spacing on the Edit tab immediately moves
  the matching `VasiShape` on the diagram — `MainViewModel.RefreshVasiShape`
  recomputes just that one shape's Position/WingBarStart/WingBarEnd/
  IsInstalled (`AirportDiagramProjector.ComputeVasiPlacement`) and mutates it
  in place, the same re-projecting-would-reset-zoom/pan-and-selection
  rationale every other mutable diagram shape in this app already follows.
- Clicking a slot's **Place** button arms click-to-place for it
  (`MainViewModel.ArmPrimaryLeftVasiPlacementCommand` and its three
  siblings); a status banner appears over the Edit tab's diagram naming the
  runway and slot. The next plain click on empty diagram space (not on an
  existing taxiway/point/runway) then writes that slot's Bias X/Z from the
  clicked position (`AirportDiagramProjector.ComputeVasiBias`, the inverse of
  the placement math above) instead of clearing the taxiway selection, and
  disarms — `AirportDiagramView`'s new `VasiPlacementCommand` is tried first
  in `EndPan`, falling through to the pre-existing
  `TaxiwayClearSelectionCommand` whenever nothing is armed
  (`MainViewModelTests.ArmVasiPlacementCommand_ArmsPlacementAndSetsStatusText`/
  `PlaceVasiCommand_CanExecute_OnlyTrueWhilePlacementArmed`/
  `PlaceVasiCommand_WritesBiasIntoArmedSlot_UpdatesDiagramShape_AndDisarms`).
- The Place button/diagram click interaction itself (real WPF mouse input)
  is not covered by automated tests, same as the rest of this epic's mouse
  handling — verified manually; the underlying geometry (position math and
  its exact inverse) is fully unit-tested per the bullets above.

### Amendment: parking spot diagram rendering, selection, and editing

**User story:** As a user, I can clearly see every parking spot on the
diagram, click one to find its data, and change it — its number, type,
name/suffix, heading, radius, and position (by typing numbers or by
clicking a new spot on the diagram) — with the diagram updating as I edit.

Before this, parking spots were drawn on the diagram but with a 1-unit
outline and a 20%-alpha fill, which fell below a device pixel at fit-to-view
zoom on any real airport (the same problem the approach-light rail and
taxiway points already had to solve) — so they were effectively invisible —
and the Edit tab had no way to change them at all (the read-only **Parking**
tab was the only place they appeared).

- `ParkingSpotShape` is now a mutable, observable class (was an immutable
  record) with `SourceIndex` (its index into `AirportDetails.ParkingSpots`),
  `Label` (the spot's `Number`), `IsSelected` and `IsVisible` — mutable for
  the same "update live without re-projecting (which would reset
  zoom/pan/selection)" reason as `VasiShape`/`TaxiwayPointShape`. Rendering:
  a fairly opaque orange circle sized from the spot's real `RadiusMeters`, a
  thick heading line, and the number as a label; a selected spot turns blue
  (distinct from the orange taxiway selection). Applies to both the read-only
  Diagram tab and the Edit tab's diagram.
- New **Parking Spots** grid in the Edit tab (`ParkingSpotEditViewModel`,
  one row per spot, same order as `Airport.ParkingSpots`): read-only `Index`
  (the sim's `TAXI_PARKING` ItemIndex, which Parking-type taxi paths
  reference), and editable `Number`, `Type`/`Name`/`Suffix` pickers (the
  SDK's enumerated codes, labelled from the same tables the Airport Data tab
  and XML exporter use — `Suffix` offers only NONE and GATE_A..GATE_Z since
  those are the only values `AirportXmlExporter.MapParkingSuffix` can
  export), `Heading`, `Radius`, and `X`/`Z` (`BiasXMeters`/`BiasZMeters`).
  Edits write straight into the loaded `Airport` (persisted by Save Project
  and carried into Export Airport XML with no further changes — the model
  and its JSON shape are unchanged).
- Editing `Number`/`Heading`/`Radius`/`X`/`Z` immediately updates just the
  matching `ParkingSpotShape` (`MainViewModel.RefreshParkingShape` →
  `AirportDiagramProjector.ComputeParkingPlacement`); `Type`/`Name`/`Suffix`
  don't affect what's drawn.
- Moving a spot (`X`/`Z`) also updates `EndXMeters`/`EndZMeters` on every
  `Type == Parking` taxi path whose `EndIndex` references it, matching what
  the extractor originally copied there — so the airport data never
  disagrees with itself. Only done when the spot's `ItemIndex` is unique
  among the airport's spots: a project saved before `ItemIndex` existed has
  every spot at `0`, where matching would wrongly drag unrelated paths
  along. Non-Parking-type paths with the same `EndIndex` value (a taxi
  point, not a spot) are never touched.
- Clicking a spot on the Edit tab's diagram selects it (Ctrl+click extends)
  and filters the Parking Spots grid to just the selected row(s) — an
  independent selection from taxiways/taxiway points; a click on empty
  diagram space clears all three. A **Hide All from Diagram** checkbox
  (display-only, never saved, resets on airport load) hides every spot at
  once, like the Taxiway Points grid's — there can be hundreds.
- Each row's **Place** button arms click-to-place, same idea as the VASI/PAPI
  Place buttons: a banner names the spot, and the next click on empty
  diagram space (or on another spot — while armed, spot clicks fall through
  as placement clicks) sets its X/Z there via
  `AirportDiagramProjector.ComputeParkingBias`, the inverse of the
  placement math. Only one of VASI/PAPI placement and parking placement can
  be armed at once (arming either disarms the other), and loading another
  airport disarms both.
- **Deliberately out of scope:** adding parking spots (a new spot would need
  a fresh sim-unique `ItemIndex` and taxi-path linkage — a separate feature;
  **deleting** one is covered by the follow-up amendment directly below),
  and dragging a spot with the mouse (Place-by-click is used instead,
  consistent with VASI/PAPI, and avoids competing with pan-drag). `BIAS_X`/
  `BIAS_Z`'s axis convention remains **UNCONFIRMED against a live sim** (same
  caveat as everywhere else in the Diagram epic) — this feature inherits it
  rather than resolving it.
- **Backwards compatibility:** no persisted field was added, removed, or
  reinterpreted (`TaxiParkingSpot` is unchanged), so a project file written
  by any earlier version loads exactly as before.
- Test coverage: `AirportDiagramProjectorTests.Project_ParkingSpots_SourceIndexMatchesListPosition_AndLabelIsNumber`/
  `ComputeParkingPlacement_AfterEditingSpot_MatchesWhatProjectWouldHaveProduced`/
  `ComputeParkingBias_InvertsComputeParkingPlacementsCenter`;
  `ParkingSpotEditViewModelTests` (write-through, change notification only on
  real changes, taxi-path End coordinate sync incl. the non-unique-ItemIndex
  and non-Parking-path guards, picker contents); `MainViewModelTests`
  (`ParkingSpotEdits_*`, `ToggleParkingSpotSelection_*`,
  `ClearTaxiwaySelection_AlsoClearsParkingSelection_*`,
  `EditingParkingSpot*`, `HideAllParkingSpots_*`, `ArmParkingPlacementCommand_*`,
  `PlaceParkingCommand_*`, `ArmingParkingAndVasiPlacement_AreMutuallyExclusive`,
  `LoadingAnotherAirport_DisarmsParkingPlacement_AndResetsHideAll`). The
  visual rendering, real mouse input (click-select, Place), and the grid's
  ComboBox/edit behavior are not covered by automated tests — verified
  manually.

### Amendment: delete parking spot(s) via selection + Delete key

**User story:** As a user, after selecting one or more parking spots on the
Edit tab's diagram, I can press the Delete key to remove them, with a
confirmation prompt first since there's no undo.

- The app's first keyboard shortcut: `MainWindow.xaml` gets a `Window`-level
  `KeyBinding` (`Key="Delete"` → `DeleteSelectedParkingSpotsCommand`) — fires
  regardless of which control has focus unless that control already consumes
  the Delete key itself (e.g. a grid text cell mid-edit, which keeps deleting
  the selected text as before). Enabled only while at least one parking spot
  is selected (`Diagram.ParkingSpots.Any(p => p.IsSelected)`); does nothing
  otherwise.
- Pressing Delete shows a confirmation dialog ("Delete N parking spot(s)?
  Any taxi path connecting to them will be deleted too.", singular/plural)
  via a new `IConfirmationService`/`ConfirmationService` (mirrors
  `IFileDialogService`'s interface-plus-fake pattern so `MainViewModel` stays
  unit-testable with no `System.Windows` dependency). Declining leaves
  everything unchanged.
- Confirming removes the selected spot(s) from `Airport.ParkingSpots`, and
  also removes any `Type == Parking` taxi path whose `EndIndex` references
  a deleted spot's `ItemIndex` — leaving that path referencing a deleted
  spot would be genuinely invalid data, not just an unnamed/default one (the
  precedent `DeleteTaxiNameCommand` set of detaching-rather-than-cascading
  doesn't apply here, since taxi names have a valid "no name" state and a
  parking path's endpoint doesn't). Same `ItemIndex`-uniqueness guard as the
  move-a-spot sync above: a spot whose `ItemIndex` collides with another
  spot's (pre-`ItemIndex`-migration data) has its paths left untouched
  rather than risk cascading the wrong one's.
- Implemented by mutating `Airport.ParkingSpots`/`Airport.TaxiPaths` directly
  and then calling `SetAirport(Airport)` again — the same rebuild path every
  airport load/reload already uses, guaranteeing `Diagram`, `ParkingSpotEdits`,
  `TaxiPathEdits`, and every filter/subscription stay consistent. **Known
  side effect, accepted as a tradeoff:** this also resets other session-only
  state (taxiway/taxiway-point selection, the Hide All checkboxes, the Taxi
  Path filter) rather than surgically patching just the parking-spot-related
  collections — judged lower-risk than a hand-rolled partial refresh for a
  first pass; worth revisiting if it proves annoying in practice.
- Defensive fix bundled in: the Parking Spots grid gets `CanUserDeleteRows=
  "False"` — previously unset, so WPF's own default row-delete-on-Delete-key
  gesture was latent (a visually selected grid row could let the `DataGrid`
  silently remove an item from `VisibleParkingSpotEdits`, a plain filtered
  list never wired back to `Airport.ParkingSpots`, producing a confusing
  transient state). Now Delete's only effect on this grid's data is via
  diagram selection.
- **Deliberately still out of scope:** adding parking spots (see the
  amendment above).
- **Backwards compatibility:** no persisted field was added, removed, or
  reinterpreted.
- Test coverage: `MainViewModelTests`
  (`DeleteSelectedParkingSpotsCommand_CanExecute_TrueOnlyWhenASpotIsSelected`,
  deleting a selected spot removes it from `Airport.ParkingSpots`/
  `Diagram.ParkingSpots` and renumbers remaining `SourceIndex`es, deleting
  cascades a matching `Type == Parking` path's removal while leaving other
  paths untouched, a colliding non-unique `ItemIndex` does not cascade,
  declining the confirmation via `FakeConfirmationService { ConfirmResult =
  false }` leaves everything unchanged, deleting multiple selected spots at
  once). The confirmation dialog's real WPF `MessageBox` and the Delete key
  itself (real keyboard input) are not covered by automated tests — verified
  manually, same category as the rest of this epic's mouse-input exclusions.
- **Bug found and fixed via live manual testing:** the first live test showed
  the diagram's zoom/pan resetting to fit-to-view on every delete, even
  though every other live edit (moving a spot, renaming a taxiway, etc.)
  leaves the current view untouched. Root cause: unlike those other edits,
  which mutate shapes in place with no `DataContext` change at all, deletion
  goes through `SetAirport(Airport)` (see above), which re-runs
  `AirportDiagramProjector.Project` and hands `AirportDiagramView` a brand
  new `AirportDiagram` instance — and
  `AirportDiagramView`'s `DataContextChanged` handler unconditionally called
  `FitToView()` on any such change, a fine assumption when it only ever fired
  for a genuine new-airport load, but wrong once the same handler also fires
  after an in-place structural edit to the airport currently being viewed.
  **Fixed:** the handler now only re-fits when the old and new
  `AirportDiagram`'s `ReferenceLatitude`/`ReferenceLongitude` differ (or the
  old one was absent, i.e. a first load) — those are copied straight from
  `AirportDetails.Latitude`/`Longitude` and never change for the same loaded
  airport, so an exact match reliably distinguishes "still the same airport,
  just refreshed" from "a different airport was loaded". **Known minor
  residual limitation, accepted:** deleting a spot that was defining an edge
  of the airport's bounding box can still shift the canvas origin slightly
  (`AirportDiagramProjector.Project` recomputes `minX`/`minZ` from the
  remaining shapes), nudging the preserved view a little even though the
  zoom level itself is kept — far less disruptive than a full reset, and not
  fixed here. Not covered by automated tests (real WPF `DataContextChanged`/
  zoom state) — verified manually, same exclusion category as this view's
  other mouse/zoom-input behavior.
- **Bug found and fixed via live manual testing — real data corruption, not
  just a UI glitch:** re-exporting OIBK after deleting a single, unrelated
  parking spot and diffing the XML against an unedited baseline export
  showed several TAXI-type `<TaxiwayPath>` elements — nowhere near the
  deleted spot's own connecting path — silently losing their `name`
  attribute, which the Scenery Editor reported as "Point not linked to main
  graph" / "Not linked to a hold-short" on the taxi points those paths led
  to. Root-caused (not guessed) by diffing an unedited `OIBK.xml` export
  against a post-delete `OIBK_del.xml` export line-by-line: every affected
  path had `name="1"` before the delete and no `name` attribute at all
  after, even though `Airport.TaxiNames` is never touched by parking-spot
  deletion. Cause: `MainViewModel.SetAirport` unconditionally
  `TaxiNames.Clear()`+rebuilt the shared `TaxiNames`
  `ObservableCollection` on every call — harmless on every other caller (a
  genuine new-airport load, where the row seeing the transient clear belongs
  to the previous airport's now-discarded `TaxiPathSegment` objects) but not
  when `SetAirport` is reused after an in-place edit to the *same* loaded
  airport (`DeleteSelectedParkingSpotsCommand`, see its own amendment
  above): a `Clear()` fires a `CollectionChanged` Reset on `TaxiNamesPicker`,
  which the Taxi Paths grid's Name `ComboBox` (two-way bound,
  `SelectedValue="{Binding TaxiNameId, UpdateSourceTrigger=PropertyChanged}"`,
  `MainWindow.xaml`) reacts to by clearing its own selection — and with that
  trigger, immediately writes that `null` back into whatever
  `TaxiPathSegment` a still-live (on-screen/virtualized) grid row's
  `DataContext` currently wraps, which for a reused `AirportDetails` is the
  *same, surviving* segment, not a discarded one. **Fixed:** extracted a
  `SyncTaxiNames` helper that skips the rebuild entirely when
  `airport.TaxiNames`' `Id` set/order already matches what's currently in
  `TaxiNames` — grepped the rest of `MainViewModel` to confirm this is the
  *only* place that `.Clear()`s a long-lived `ObservableCollection` exposed
  to a two-way-bound `Selector`, so no other picker shares this exposure.
  Regression-tested
  (`MainViewModelTests.DeleteSelectedParkingSpots_DoesNotRebuildUnchangedTaxiNames`
  — asserts the *same* `TaxiNameEditViewModel` instance survives a delete,
  not merely an equal one, since instance identity is what determines
  whether a `CollectionChanged` Reset fires; confirmed this test fails
  against the pre-fix code before confirming it passes against the fix, per
  this project's standard of proving a regression test actually catches the
  bug it's named for). The real-WPF `ComboBox`-clears-selection mechanism
  itself isn't covered by automated tests (requires live WPF binding/
  virtualization) — verified against the actual reported bug via the XML
  diff above, not merely reasoned about.

## v0.1+ — Generate SDK-compatible `<Airport>` XML

**User story:** As a user, after loading and editing an airport, I can click
an "Export Airport XML" button and get a `bglcomp.xsd`-conformant
`<FSData><Airport>...</Airport></FSData>` file that overrides the stock
airport's runways and taxiways/parking with my edits — ready to compile with
the MSFS 2024 SDK's `bglcomp` or import into the Dev Mode Scenery Editor.
This is the missing link that lets every edit already committed above
(runway lighting/VASI/PAPI/approach lights/pavement features, taxi path
naming/type/edges/lighting) actually take effect in MSFS. **Out of scope,
still not done:** this only produces the XML file — shelling out to
`fspackagetool`/Dev Mode to build a Community-folder package from it is a
separate, still-unapproved epic (see the proposed epics below).

Checked directly against the locally installed MSFS 2024 SDK before
starting: `Tools/bin/bglcomp.xsd` is the schema `bglcomp` validates against,
and `Documentation/public/flighting/content-configuration/environment/
airports-and-facilities/airport-xml-properties` (and its `runway-xml-
properties`/`taxiway-xml-properties` siblings) documents the element/
attribute reference. Overriding the stock airport reuses the same `ident`
plus a `<DeleteAirport>` element (per the docs' explicit callout that data is
otherwise *added* to the existing airport, not replaced) — no
`<ExclusionRectangle>`/`<Polygon>` needed, since those exclude 3D buildings/
vegetation/TIN, which stays out of scope. `<TaxiwayPoint>`/`<TaxiwayParking>`/
`<TaxiwayServiceStand>`/`<TaxiName>`/`<TaxiwayPath>` must appear in exactly
that order inside `<Airport>` — enforced by the schema.

Three scope decisions made with the user before implementation:
1. **Jetways are excluded from the export.** The XML schema's `<Jetway>`
   element requires an embedded `<SceneryObject>` referencing a 3D model
   GUID, which the `Jetway` model (from the legacy `RequestJetwayData` API)
   never captured — there's nothing correct to put there.
2. **VASI/PAPI position data was added to extraction** rather than
   approximated: `Runway` gained `*VasiBiasXMeters`/`BiasZMeters`/
   `SpacingMeters` per slot (purely additive, no schema/migration concerns),
   and `SimConnectService` now requests `BIAS_X`/`BIAS_Z`/`SPACING` alongside
   the already-requested `TYPE`/`ANGLE` for all four VASI slots.
3. **Frequencies are left out of the export entirely** (no `<Com>` elements,
   `deleteAllFrequencies` not set) — there's no frequency-editing feature, so
   the stock airport's COM frequencies are left completely untouched.
   **Parking spots ARE exported**, even though spot type/heading/radius
   editing isn't built yet (still proposed epic 1 below) — `deleteAllTaxiways`
   removes taxi points/parking/paths as one group, so replacing taxiways
   without re-adding the existing parking spots would silently delete every
   gate at the airport.

Known, documented limitations (not fixed by this epic):
- A taxi path with `TaxiPathType.Unknown` or `.PaintedLine` has no
  equivalent in the schema's `stTaxiwayPathType` and is skipped from the
  export (with a warning surfaced to the user), not defaulted to some other
  type.
- `TaxiName.Value` is truncated to the schema's 8-character `stString8`
  limit if longer (with a warning), since taxi names are free text in the
  Edit tab but the XML format caps them.
- `Runway.SurfaceType` and `TaxiParkingSpot.Type`/`NameCode`/`SuffixCode`
  are mapped from their raw SimConnect int values to the schema's string
  enums via explicit lookup tables (sourced from the SDK's Facility Data
  reference, cross-checked against `bglcomp.xsd`); an unmapped/out-of-range
  value falls back to a safe default (`"ASPHALT"` for surface, `"NONE"` for
  parking name/suffix/type) with a warning, rather than emitting a value
  `bglcomp` would reject.

Acceptance criteria:
- Given an airport is loaded, clicking **Export Airport XML** prompts for a
  save location (via `IFileDialogService.ShowSaveXmlFileDialog`) and writes a
  well-formed `<?xml version="1.0" encoding="utf-8"?><FSData version="9.0">
  <Airport>...</Airport></FSData>` document via `IAirportXmlExporter`, and
  shows the path written to (`LastXmlExportPath`) plus any warnings
  (`LastXmlExportWarnings`) for skipped/defaulted/truncated data as described
  above.
- The `<Airport>` element's `ident`/`lat`/`lon`/`alt`/`name`/`magvar` match
  the loaded `AirportDetails`, and it contains a `<DeleteAirport
  deleteAllRunways="true" deleteAllTaxiways="true" />` with no other flags
  set.
- Every `AirportDetails.Runways` entry becomes a `<Runway>` with matching
  geometry/surface/lighting, a `<Vasi>` element only for each non-null VASI
  slot (not an empty/default one for "not installed" — `biasX`/`biasZ`
  converted from AirportSmith's own threshold-relative/signed storage into
  the SDK's documented center-relative/unsigned meaning, see the amendment
  at this epic's end), an `<ApproachLights>`
  element only for each non-null approach-light system, an
  `<OffsetThreshold>`/`<BlastPad>`/`<Overrun>` only for each non-null
  pavement feature, and exactly two `<RunwayStart>` elements computed from
  the runway's own center/heading/length via the new
  `GeoProjection.UnprojectLocalPoint` (the documented inverse of
  `AirportDiagramProjector`'s existing `ProjectLatLon`, extracted into a
  shared `Services/GeoProjection.cs` so both stay in lockstep).
- `AirportDetails.TaxiPaths`/`ParkingSpots`/`TaxiNames` become
  `<TaxiwayPoint>`/`<TaxiwayParking>`/`<TaxiName>`/`<TaxiwayPath>` elements in
  the schema-required order, with `TaxiwayPoint` indices reused directly from
  `TaxiPathSegment.StartIndex`/`EndIndex` (already stable/shared across
  segments) and `TaxiwayParking`/`TaxiName` indices synthesized fresh per
  export. **A `<TaxiwayPoint>` is only emitted when at least one exported
  `<TaxiwayPath>` actually connects to it**, and its `type`/`orientation`
  reflect the real `TAXI_POINT.TYPE`/`ORIENTATION` the point resolved to
  (`NORMAL` only as a fallback for a point that never resolved a type) — see
  the revisions below for why both are called out explicitly as their own
  criteria, not just implied by the general "reused directly" wording.
- No `<Jetway>` or `<Com>` element is ever emitted, regardless of what's in
  `AirportDetails.Jetways`/`Frequencies`.

**Revised after first-round user feedback (real import into the MSFS 2024 SDK
Dev Mode Scenery Editor):** the first version built the `<TaxiwayPoint>` set
from *every* taxi path's individually-resolved endpoint, independent of
whether that path itself ended up exported. A path with one resolved end and
one unresolved end (or an unmappable type) got dropped from the output
entirely, but its resolved end had already been added to the point set —
orphaning it: emitted with a valid position, but connected by zero exported
`<TaxiwayPath>` elements. The Scenery Editor's own graph validation caught
every one of these on import, flagging each as "Point not linked to the main
graph". Fixed by reordering `AirportXmlExporter.Build`: it now decides which
taxi paths are exportable (both endpoints resolved, mappable type) *before*
building any `<TaxiwayPoint>`, and only builds points from that already-
filtered set — so a point can never exist in the output without at least one
path connecting to it. Regression-tested
(`Build_PathWithOneUnresolvedEnd_DoesNotEmitOrphanedTaxiwayPointForTheResolvedEnd`,
`Build_MixOfExportableAndUnresolvedPaths_OnlyEmitsPointsUsedByExportedPaths`).

**Revised after second-round user feedback (same real import):** the first
round's fix didn't fully resolve the Scenery Editor's errors — the remaining
ones turned out to be a *different* problem with the same "not linked to the
main graph" wording, plus a companion "no hold short within 200m of runway"
warning on the runway itself, and "point not linked to a hold short" on the
point's own properties. Root cause: every exported `<TaxiwayPoint>` was
hardcoded to `type="NORMAL"`, originally documented above as a cosmetic,
out-of-scope limitation ("hold-short markings are lost on export") — it
turned out not to be cosmetic at all. MSFS's taxiway network validation
structurally requires hold-short points near runway entrances; without any,
the network doesn't validate even though it's fully connected by paths.
Investigating further found the real bug: `SimConnectService` was already
requesting and marshaling `TAXI_POINT.TYPE`/`ORIENTATION` (confirmed against
the SDK's Facility Data reference) into `FacilityTaxiPointData`, then
discarding both fields immediately rather than storing them anywhere —
`TaxiPointNode` only kept X/Z. Fixed by:
1. Two new enums, `TaxiPointType` (`Normal`/`HoldShort`/`IlsHoldShort`/
   `HoldShortNoDraw`/`IlsHoldShortNoDraw`, matching `TAXI_POINT.TYPE`
   1/2/4/5/6 — no `None`/0 member, same "0 means not present" convention as
   `VasiType`/`ApproachLightSystemType`) and `TaxiPointOrientation`
   (`Forward`/`Reverse`, matching `TAXI_POINT.ORIENTATION` 0/1).
2. `TaxiPathSegment` gained `Start`/`EndPointType` and `Start`/
   `EndPointOrientation` (purely additive, no migration needed — same as the
   VASI bias/spacing fields added earlier in this epic), populated in
   `SimConnectService.ResolveTaxiPathPoints` from the now-retained
   `TaxiPointNode.Type`/`Orientation`.
3. `AirportXmlExporter.BuildTaxiwayPoints` now emits the real mapped
   `type` (falling back to `NORMAL` only when a point's type never
   resolved — e.g. an older saved project) and an `orientation` attribute
   for hold-short-family points only, per the SDK docs' "orientation is
   only meaningful when type is hold short". Regression-tested
   (`Build_TaxiwayPointWithHoldShortType_MapsTypeAndOrientation`).

**Revised after third-round user feedback (same real import, still failing):**
the hold-short fix above was correct but incomplete — the Scenery Editor
still reported "point not linked to the main graph" for many points.
Investigated directly against the actual exported XML (not just theory) by
running the graph's connected-components analysis by hand: every one of the
381 `<TaxiwayPoint>`s and 479 `<TaxiwayPath>`s formed a single connected
component, ruling out a fragmented-network explanation. Measuring each
`<TaxiwayPath>`'s length instead (distance between its `start`/`end`
points) found the real signature: 29 of the airport's 36 `TYPE="PARKING"`
paths (the short stub connecting a taxiway to a specific gate) were
hundreds to *thousands* of meters long — physically implausible for what
should be a ~20-60m stub, and invisible in the Diagram tab only because
it's a small fraction of ~480 lines rendered among a dense network.
Root cause, confirmed against the SDK's own `TAXI_PATH` reference: `START`/
`END` is documented as "the index number of taxiway point **or parking
space**" — for a `PARKING`-type path, `END` is a `TAXI_PARKING` ItemIndex,
not a `TAXI_POINT` one. `SimConnectService.ResolveTaxiPathPoints` was
resolving it against `TAXI_POINT` unconditionally regardless of type; since
both index spaces start at 0, that lookup almost always "succeeded" against
the wrong dictionary — silently returning an unrelated taxi point elsewhere
on the airport instead of failing loudly. Recomputing those 29 paths'
lengths against their actual `TaxiwayParking` spot (by matching `END` to
that spot's own ItemIndex, not a sequential position) brought every one down
to 18-64m — a real airport's parking-stub scale. Fixed by:
1. `TaxiParkingSpot` gained `ItemIndex` (the TAXI_PARKING row's own index,
   distinct from `Number`, the user-facing gate number) — purely additive,
   no migration needed, populated in `SimConnectService`'s existing
   `TAXI_PARKING` case.
2. `SimConnectService.ResolveTaxiPathPoints` now branches on
   `segment.Type == TaxiPathType.Parking`: only then is `EndIndex` resolved
   against the extracted parking spots (by `ItemIndex`) instead of
   `TAXI_POINT` rows. `StartIndex` is unaffected (every sampled case had a
   genuine taxiway point there).
3. `AirportXmlExporter` now exports each `<TaxiwayParking>` with its own
   `ItemIndex` as its `index` (via the new `ComputeParkingIndices`, which
   falls back to the old sequential numbering with a warning if `ItemIndex`
   values collide — the signature of a project saved before this field
   existed) instead of an arbitrary loop position, and `BuildTaxiwayPoints`
   no longer synthesizes a `<TaxiwayPoint>` for a `Parking`-type path's
   `EndIndex` at all — that index belongs to `<TaxiwayParking>`, and a
   same-indexed `<TaxiwayPoint>` would be redundant with it (and was, in
   fact, the original source of these particular "not linked to the main
   graph" errors before this was understood). Regression-tested
   (`Build_ParkingTypePathEnd_ResolvesToTaxiwayParkingNotASynthesizedTaxiwayPoint`,
   `Build_ParkingSpotItemIndicesCollide_FallsBackToSequentialNumberingAndWarns`).

This does not rule out every possible remaining cause (a handful of `RUNWAY`-
type paths were also found longer than expected during this investigation,
without as clean an explanation — see the Known limitations note above on
`RunwayNumber`/`RunwayDesignator` data quality, which may be related), but
it is expected to eliminate the large majority of the reported errors.

**OPEN — investigation paused mid-session (2026-09-15), not resolved:** after
the fix above, `<TaxiwayPath>` elements stopped being flagged as broken, but
a new, more fundamental symptom appeared: **the MSFS 2024 SDK Scenery Editor
does not import any `<TaxiwayPath>` elements at all** — the Content List
shows `Runway`/`TaxiwayPoint`/`TaxiwayParking` objects but zero taxiway path
objects, confirmed two ways: (1) manually selecting two imported
`TaxiwayPoint`s in the Editor and adding a `TaxiwayPath` between them via the
Editor's own UI immediately clears that point's "not linked to the main
graph" error — proving the Editor understands and can use `TaxiwayPath`
objects fine, it's specifically not importing ours; (2) tried both import
routes available — the dedicated "Airport XML Importer" tool (with "Import
taxiways" checked) and replacing the project's own
`PackageSources\Scenery\<group>\scenery\<ICAO>.xml` source file directly and
reloading via "Load In Editor" — both fail identically, ruling out one
specific importer tool as the cause.

Ruled out during this session, with evidence, so a future investigation
doesn't need to re-check them:
- **Not element ordering or schema validity** — the exported XML was
  directly inspected and confirmed well-formed, with `<TaxiwayPoint>` →
  `<TaxiwayParking>` → `<TaxiName>` → `<TaxiwayPath>` in the exact order
  `bglcomp.xsd` requires, every attribute matching its documented type.
- **Not graph fragmentation** — a full connected-components check against
  the actual exported file (479 `<TaxiwayPath>` edges, 381
  `<TaxiwayPoint>`s) found exactly one component containing every point.
- **Not gaps in the `TaxiwayPoint` index range** — checked directly: 381
  points span index 0-380 with zero missing values.
- **Not specific to scale/complexity or to `PARKING`/`RUNWAY`-type paths'
  data-quality issues** — a minimal hand-written test file (3
  `TaxiwayPoint`s, 2 plain `TYPE="TAXI"` `TaxiwayPath`s, no parking, no
  names, no hold-short points, touching only `deleteAllTaxiways`) was tried
  in isolation and **failed identically**. This is the most important
  finding to preserve: it means the parking-index-confusion fix earlier in
  this section, while real and worth keeping, is *not* sufficient to explain
  "zero `TaxiwayPath` elements import" — something more basic than any
  per-type coordinate bug is going on.

New lead surfaced by the user, not yet investigated: in the Edit tab's Taxi
Paths grid, rows with `Type == Runway` can't be located on the Diagram when
clicked. Partly expected behavior, not necessarily a bug by itself —
`AirportDiagramProjector.Project` (`Services/AirportDiagramProjector.cs`
~line 319) deliberately only draws `TaxiPathType.Taxi`/`.Path` segments, so
`Runway`-typed ones were never going to appear there regardless of whether
their coordinates are correct. But this also means there's currently no way
to visually sanity-check a `Runway`-type path's position, which is exactly
how the `RUNWAY`-type length anomalies found earlier in this section (3
paths, `Start=2/End=1`, `Start=14/End=13`, `Start=286/End=8`, all
suspiciously long) went unnoticed. Worth checking whether `Runway`-type
paths have their own version of the same "START/END index space is
overloaded depending on context" issue the `Parking`-type fix uncovered —
the SDK's own `TAXI_PATH.START`/`END` docs only mention "taxiway point or
parking space" explicitly, not a third runway-related interpretation, but
that doc sentence may simply be incomplete (the way the SDK's own
`RUNWAY.SURFACE` doc is separately known to be incomplete already).

**RESOLVED (2026-09-17)** — see the "Follow-up" entry near the end of this
epic: `Runway`-typed paths are now drawn on the diagram (as thick red
lines), which is what let the length anomalies noted above actually be
seen directly instead of staying a theory.

**RESOLVED (2026-09-16), root cause found and fixed:** step 1 above (diff
against the Editor's own real output) was carried out — the user imported
AirportSmith's XML into a scratch project, manually added a handful of
`<TaxiwayPath>`s between the imported points using the Editor's own UI, then
saved. Every `<TaxiwayPath>` the Editor itself wrote carried a `surface`
attribute (a material GUID, e.g.
`surface="{85D02B2B-08A1-452E-AB07-6D5AE7F52884}"`) plus
`drawSurface="FALSE" drawDetail="TRUE" groundMerging="TRUE"
excludeVegetationAround="TRUE" excludeVegetationInside="TRUE"` —
**AirportXmlExporter.BuildTaxiwayPath emitted none of these**, confirming
step 3's theory. `surface` is schema-optional (`bglcomp.xsd`'s `stSurface`)
so this was never caught by schema validation, but it's apparently
functionally required for the Editor to construct a taxiway path *object* at
all — without it, the element is silently dropped rather than erroring,
which also explains why the minimal 3-point/2-path test in the "ruled out"
list above failed identically (it also omitted `surface`, for the same
underlying reason).

Confirmed why the exporter never emitted `surface` in the first place:
SimConnect's `TAXI_PATH` facility data has no `SURFACE` field at all
(checked directly against the SDK's `AddToFacilityDefinition` reference —
`TAXI_PATH` exposes `TYPE`/`WIDTH`/`LEFT_HALF_WIDTH`/`RIGHT_HALF_WIDTH`/
`WEIGHT`/`RUNWAY_NUMBER`/`RUNWAY_DESIGNATOR`/`LEFT_EDGE(_LIGHTED)`/
`RIGHT_EDGE(_LIGHTED)`/`CENTER_LINE(_LIGHTED)`/`START`/`END`/`NAME_INDEX`
only), so there was never per-path surface data available to extract in the
first place — unlike `Runway.SurfaceType`, which SimConnect *does* expose via
`RUNWAY.SURFACE`.

**Fix:** every exported `<TaxiwayPath>` now also carries
`surface="ASPHALT"` (the same plain-string fallback `MapSurface` already
uses for an unrecognized Runway surface code — proven to work, since Runways
already import fine with plain surface names, not just GUIDs) and the same
`drawSurface`/`drawDetail`/`groundMerging`/`excludeVegetationAround`/
`excludeVegetationInside` values seen on every path in the Editor's own
output, applied as fixed defaults (not sourced from SimConnect, since
`TAXI_PATH` has none of these fields either). One export-level warning
(not one per path) notes that taxiway path surface is a synthesized default.
Regression-tested
(`Build_MappableTaxiPath_HasSurfaceAndSceneryEditorDefaultAttributes`).

**Follow-up (2026-09-16, same day): the surface/drawSurface fix above was not
sufficient on its own** — the user re-exported and re-imported and paths
still didn't show up. Second root cause found by re-comparing against the
same Editor-authored sample file: **every ordinary taxi route the Editor
wrote used `type="TAXI"`; none used `type="PATH"`** — but AirportSmith's
export had been emitting `type="PATH"` for effectively every real taxi path,
because that's what SimConnect's `TAXI_PATH.TYPE` reports for them (`TYPE ==
4`, mapped by `TaxiPathType.Path` — see that enum's own comment: the OIBK
sample had `Type == Path` on all 479 rows, zero `TAXI` rows observed at all).
`"PATH"` is a valid `stTaxiwayPathType` enumeration value and passes schema
validation, but it apparently isn't treated as part of the drivable taxi
network graph by the Scenery Editor's importer the way `"TAXI"` is — whereas
`"TAXI"` is exactly the type the Editor itself always chooses when a user
draws an ordinary taxi route by hand.

**Fix:** `AirportXmlExporter.MapTaxiPathType` now maps `TaxiPathType.Path` to
XML `type="TAXI"` instead of the schema's own literal `"PATH"` string — since
SimConnect's `Path` value is what virtually every real taxi route comes back
as, this affects nearly all exported `<TaxiwayPath>` elements. The other
mappings (`Taxi`→`TAXI`, `Runway`→`RUNWAY`, `Parking`→`PARKING`,
`Closed`→`CLOSED`, `Vehicle`→`VEHICLE`, `Road`→`ROAD`) are unchanged.
Regression-tested (`Build_TaxiPathWithPathType_MapsToTaxiXmlType`).

**CONFIRMED against a live import (2026-09-16):** with both fixes applied
(the `surface`/`drawSurface`/etc. attributes, and `TaxiPathType.Path` mapping
to `type="TAXI"`), the user re-exported and re-imported and `<TaxiwayPath>`
elements now import successfully. This closes the investigation — both
fixes were required together; neither alone was sufficient.

Minor same-day follow-up: the user inspected a confirmed-working exported
`<TaxiwayPath>` element directly and asked for the exported attribute order
to match it exactly (attribute order has no schema/parsing meaning, but
keeps AirportSmith's output diffable against future Scenery-Editor-authored
samples). While matching that order, one more gap surfaced: the sample had
`weightLimit="0"`, which AirportSmith's export wasn't emitting at all —
`TAXI_PATH.WEIGHT` isn't requested/stored anywhere (`TaxiPathSegment` has no
`WeightLimit` property), so `weightLimit="0"` (the SDK-documented "no limit"
default) is now emitted as a fixed default, same treatment as
`drawSurface`/`drawDetail`/etc. above. Not believed to be load-bearing for
the import (the confirmed-working live-import test above didn't have this
attribute), but included for consistency. Actually capturing real per-path
`WEIGHT` data from SimConnect remains unimplemented — out of scope unless a
future editing feature needs it.

Remaining loose end, not blocking (still not investigated): the `RUNWAY`-type
path length anomalies noted earlier in this section (`Start=2/End=1`,
`Start=14/End=13`, `Start=286/End=8`) and the "overloaded index space" theory
for `RUNWAY`-type paths specifically.

**Correctness fix, same day, flagged by the user against a real exported
file:** `number`/`designator` are documented as valid *only* when
`type="RUNWAY"` — the export was emitting `number` on any path whose
`RunwayNumber` was in the 0-36 range regardless of path type, which put an
invalid `number` attribute on every `TAXI`-type path SimConnect reports a
runway association for (a common, real case — e.g. an entrance/exit taxiway
near a specific runway — not an edge case). Fixed: `number`/`designator` are
now only emitted when the path's mapped XML type is `"RUNWAY"`; a non-RUNWAY
path with a nonzero `RunwayNumber` instead gets a warning and the attributes
are left off entirely (that association currently has no valid place in the
XML format for a non-RUNWAY path). Regression-tested
(`Build_TaxiTypePathWithRunwayAssociation_OmitsNumberAndDesignatorAndWarns`,
`Build_RunwayTypePathWithRunwayAssociation_IncludesNumberAndDesignator`).

**Follow-up (2026-09-17), the "RUNWAY-type path" loose end above picked back
up — reported against OIBK:** the user noticed taxi points 0 and 12 render
as apparently-disconnected red dots in the Edit/Diagram tabs even though the
Taxi Paths grid clearly shows a row connecting each of them (`start="79"
end="0"`, `start="13" end="12"`, both `type="RUNWAY"`), and separately that
the Scenery Editor flags a "point not linked anywhere" error on import.
Investigated directly against the real OIBK debug JSON and exported XML:

- **Diagram symptom, root-caused and fixed:** `AirportDiagramProjector`
  only ever drew `Taxi`/`Path`-typed segments as lines (`TaxiwaySegments`) —
  `Runway`-typed ones were filtered out entirely, even though
  `TaxiwayPoints` (the red dots) were never filtered by type. A point only
  reachable via a `Runway`-type path therefore always looked orphaned,
  regardless of whether the underlying data was fine. Fixed in two parts:
  1. `AirportDiagramProjector` now includes `Runway`-typed segments in
     `TaxiwaySegments` (`TaxiwaySegmentShape.IsRunwayType`), rendered as a
     thick red line in `AirportDiagramView` (a `DataTrigger`, same mechanism
     as the existing named/unnamed blue/gray styling) — `Parking` stays
     excluded, since its End references a `TaxiwayParking` item, not a taxi
     point (see `BuildTaxiwayPoints`'s own comment).
  2. **A second, non-obvious bug found while verifying the first fix
     visually:** the new red lines were computing correct positions (checked
     directly with a temporary on-canvas debug marker at the exact
     coordinates) but stayed completely invisible — because `Runway`-type
     paths run along/across the runway pavement itself by definition, and
     `AirportDiagramView`'s `Runways` `ItemsControl` (the `DarkSlateGray`
     pavement polygon) was drawn *after* `TaxiwaySegments` in the visual
     tree, painting over them. Fixed by moving the whole `TaxiwaySegments`
     `ItemsControl` to after `Runways` in the XAML. Also had to move
     `StrokeThickness` out of a local XAML attribute on the `Line` element
     into the `Style`'s own `Setter` — a local value takes precedence over
     every `Style.Triggers` `Setter` for the same property regardless of
     which trigger is active, which was silently keeping every segment
     (including the new red ones) at the same thin `1.5` regardless of the
     `IsRunwayType` trigger's own (higher) value.
  3. This also surfaced the real extent of the previously-flagged length
     anomalies: several `RUNWAY`-type paths are genuinely very long (995m,
     838.5m, 542m, and others in the 300-500m range) — now directly visible
     and inspectable as prominent red lines on the diagram instead of a
     theory that needed guessing at. Not yet judged whether these lengths
     are real airport geometry or a further data-quality issue; visible for
     the first time is the point of this fix, not a claim that they're wrong.
- **Export symptom, root-caused and fixed:** confirmed directly against the
  real exported OIBK XML that `AirportXmlExporter.BuildTaxiwayPath` was
  emitting a `name` attribute on `RUNWAY`-type `<TaxiwayPath>` elements
  (SimConnect's `TAXI_NAME` association isn't itself gated by path type, so
  a `RUNWAY`-type path can and did resolve one) — the same class of "valid
  only for one path type" mistake as the `number`/`designator` fix above,
  just the opposite direction: `name` is documented as valid only when type
  is *not* `"RUNWAY"`. Suspected (not independently confirmed against a live
  Scenery Editor import at time of writing) to be the actual cause of the
  "point not linked anywhere" error: an invalid attribute rejecting the
  whole `<TaxiwayPath>` element would silently orphan any `<TaxiwayPoint>`
  only reachable through it — which matches points 0/12 exactly, since both
  are only linked via a `RUNWAY`-type path once you exclude the coincidental
  `PARKING`-type path that numerically collides with the same index (see
  `BuildTaxiwayPoints`'s established "Parking path End isn't a taxi point"
  rule — that part of the export was already correct). Fixed:
  `BuildTaxiwayPath` now omits `name` when the mapped XML type is
  `"RUNWAY"`, with a warning when a resolvable name would otherwise have
  been dropped. Regression-tested
  (`Build_RunwayTypePathWithResolvableName_OmitsNameAndWarns`,
  `Build_NonRunwayTypePathWithResolvableName_IncludesName`).
- **Next step for the user:** re-export OIBK and re-import into the Scenery
  Editor to confirm whether the `name`-attribute fix actually clears the
  "not linked" error for points 0/12 — this wasn't independently verified
  against a live Editor import (this project's tooling can inspect the
  generated XML directly but can't run the Editor itself).

## Proposed v0.1+ epics (not yet committed — for prioritization with the user)

These are draft candidates surfaced by the research above, not approved user stories. Each needs to be broken into concrete acceptance criteria once prioritized.

1. **Edit runway geometry/taxiway routing/parking data.** UI to modify runway surface/length, taxiway routing, and parking spot type/heading/radius. Taxi path naming/lighting and runway lighting (edge lights, VASI/PAPI, approach lights) are already committed above — this covers the rest of the original "Edit runway/taxiway/parking data" idea.
   - **Follow-up: add parking spots.** Deliberately left out of the parking spot editing amendment above (**deleting** one is now committed — see its own amendment). Adding needs new sim-unique `ItemIndex` values and taxi-path linking (a Parking-type path's `EndIndex` references a spot's `ItemIndex`), so it's a separate feature.
   - **Follow-up: drag a parking spot with the mouse.** Also left out — Place-by-click is used instead, consistent with VASI/PAPI, and avoids competing with pan-drag.
2. **Background map, phases 1–2 (satellite imagery + calibration).** Phase 0
   (OpenStreetMap tile layer, projection fix) is now committed above — see
   [`background-map-research.md`](background-map-research.md) for the full
   findings. Still draft/open:
   - **Phase 1 — satellite.** User supplies their own Esri (ArcGIS Location
     Platform, 2M free tiles/month) or Mapbox (750k free/month) key; needs a
     new app-level settings store (`%LocalAppData%\AirportSmith[-dev]\settings.json`,
     never committed) and a provider abstraction behind `IMapTileSource` so
     OSM/Esri/Mapbox are interchangeable. Not verified: whether each
     provider's terms allow deriving positions from imagery.
   - **Phase 2 — calibration.** Per-airport manual offset nudge + opacity
     control, persisted as additive `AirportProjectFile`/`AirportDetails`
     fields (back-compat rules apply, needs a load test); existing diagram
     styling (opaque runway fill, `#33808080` taxiway bands, thin
     centerlines) assumes a white background and needs a legibility review
     over imagery.
   - **Open decision carried forward:** does the user already have an Esri or
     Mapbox account, or should phase 1 default to Esri?
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
  clears `ErrorMessage`/`LastExportPath`); a successful load and a valid
  `LoadFromFileCommand` both populate `AirportDataTree` (non-empty); a
  not-connected load and an invalid `LoadFromFileCommand` both leave it empty.
  All via `Fakes/FakeSimConnectService`,
  `Fakes/FakeDebugDataStore`, and `Fakes/FakeFileDialogService` — no live
  SimConnect/MSFS dependency, no real file I/O, no WPF dialog.
- `AirportDataTreeBuilderTests` (`AirportSmith.Tests`) covers: scalar fields
  render as non-expandable `"Name: value"` leaves; a `null` nested record (e.g.
  `Runway.PrimaryApproachLights` unset) renders as a `"Name: (not present)"` leaf
  with no children; a present nested record expands into its own properties as
  children; a list of runways renders as an expandable `"Runways (N)"` row whose
  children are labelled with each runway's designations (`"[1] 09L/27R"`); an
  empty list renders `"Name (0)"` with no children; a taxi path with a blank
  `Name` falls back to `"[i] (unnamed, type N)"` rather than a blank label; a
  present vs. absent nullable `double` field on the same object both render
  correctly (`"Field: 12.5"` vs. `"Field: (not present)"`); `ApproachLightSystem
  .SystemType` appends its documented label for a known value (`0`/NONE,
  `9`/CALVERT) and falls back to the plain number for an unrecognized one
  (`999`); `Runway.PrimaryLeftVasiType`, `Frequency.Type`,
  `TaxiParkingSpot.Type`/`NameCode`/`SuffixCode` (including a `GATE_A`..`GATE_Z`
  value), and `TaxiPathSegment.Type` each append their own documented label the
  same way; `Runway.SurfaceType` deliberately shows the plain number with no
  label (the SDK's own docs don't enumerate it). All pure
  computation via reflection, no fakes needed — same testing approach as
  `AirportDiagramProjectorTests`.
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
  coverage rather than truncation; a runway with a `PrimaryApproachLights` of
  `SystemType` `ApproachLightSystemType.Alsf2` renders the Full category's
  rail-light count and first/last positions plus the red crossbar's two
  endpoints, all matching hand-calculated coordinates, with
  `SecondaryFeatures.ApproachLights` null; `Malsr` renders the same
  Full-category rail-light count with an empty `CrossBar`; `Mals` renders the
  Short category's (fewer) rail-light count; `Odals` renders the Sparse
  category's widely-spaced rail-light count; a runway with no
  `PrimaryApproachLights` set and one with `SystemType` `0` (unnamed — no
  `None` member on this enum, see `ApproachLightSystemType.cs`) both produce
  a null `ApproachLights` shape; multi-runway canvas bounds cover
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
- `AirportProjectStoreTests` (`AirportSmith.Tests`) covers: save-then-load
  round-trips every field this epic added (`TaxiPathSegment.LeftEdgeLighted`/
  `RightEdgeLighted`/`TaxiNameId`, `AirportDetails.TaxiNames`,
  `Runway.EdgeLightIntensity`, a `VasiType`-typed field,
  `ApproachLightSystem`'s enum-typed `SystemType`); `Load` returns `null` for
  an ICAO with no saved project; `HasProject` reflects whether `Save` was
  called; a hand-written "old" (schema v1) JSON file missing every field this
  epic added deserializes without exception, with those fields coming back as
  their type default (proves the back-compat contract in the Edit-tab epic
  above rather than assuming it); a corrupt (non-JSON) file makes `Load`
  return `null` rather than throw; a v1 file whose `TaxiPaths` use the old
  free-text `Name` field (including two paths sharing one name, and one
  blank) migrates into v2's `TaxiNames`/`TaxiNameId` shape with duplicate
  names deduplicated into one shared `TaxiName` and the blank one left
  unnamed (`Load_V1FileWithLegacyNames_MigratesToTaxiNamesAndTaxiNameId`); a
  genuine v2 file's `TaxiNames` isn't re-migrated or duplicated on load
  (`Load_V2File_DoesNotReMigrateOrDuplicateTaxiNames`). Every test uses its
  own temp directory, never `AppDataHelper.AppDataPath`, per `CLAUDE.md`'s
  guardrail.
- `TaxiPathEditViewModelTests`/`RunwayEditViewModelTests`/
  `TaxiNameEditViewModelTests` (`AirportSmith.Tests`) cover: setting an
  editable property writes through to the wrapped `TaxiPathSegment`/`Runway`/
  `TaxiName` and raises `PropertyChanged`; setting the same value again does
  not re-raise it; setting a runway's approach-light system type to a value
  (re)creates the underlying `ApproachLightSystem` record, and setting it to
  `null` clears it back to "not installed"; `TaxiPathEditViewModel
  .ClearTaxiNameIfReferencing` clears `TaxiNameId` only when it matches the
  given id, leaving a non-matching one untouched; `TaxiNameEditViewModel
  .DisplayValue`/`IsUnnamed` correctly show `"(no name)"`/`true` for a blank
  `Value` and the real string/`false` otherwise, in both directions (starting
  blank, and clearing a non-blank value back to blank), with `PropertyChanged`
  raised for both derived properties whenever `Value` changes. Pure
  computation, no fakes needed.
- `MainViewModelTests` additionally covers: a successful load populates
  `TaxiPathEdits`/`RunwayEdits`/`TaxiNames` (one wrapper per item, matching
  the loaded data); a not-connected load leaves them empty;
  `IsProjectStoreAvailable` and `SaveProjectCommand`/`LoadProjectCommand`'s
  `CanExecute` correctly depend on whether an `IAirportProjectStore` was
  injected (and, for Save, whether an airport is loaded); executing
  `SaveProjectCommand` delegates to the store and sets `LastProjectSavePath`;
  `LoadProjectCommand` against an ICAO with no saved project sets an error
  message and leaves `Airport` untouched; against one that does, it populates
  `Airport`/`Diagram`/`AirportDataTree`/`RunwayEdits` exactly like a live
  load, with no live `SimConnectService` call needed — via the new
  `Fakes/FakeAirportProjectStore`; `AddTaxiNameCommand`'s `CanExecute` depends
  on an airport being loaded and its execution adds a blank `TaxiName` to
  both `Airport.TaxiNames` and the `TaxiNames` collection;
  `DeleteTaxiNameCommand`'s execution removes it from both and clears
  `TaxiNameId` on every `TaxiPathEdits` entry that referenced it;
  `ToggleTaxiwaySelectionCommand` replaces the selection on a plain click and
  extends/toggles it on a Ctrl+click (`ExtendSelection: true`), and — unlike
  `SyncTaxiwaySelectionFromRows` — sets `ShowTaxiwayBatchEditPopover`;
  `ApplyTaxiwayBatchEditCommand` writes only the `TaxiwayBatchEdit` field(s)
  actually touched onto every currently selected taxi path — a dedicated
  regression test
  (`ApplyTaxiwayBatchEditCommand_UntouchedFields_LeaveEachSelectedPathsOwnValueUnchanged`)
  selects two paths that don't already agree on name/lighting, touches only
  one field, and asserts every other field on both paths is unchanged
  afterward, per the third-round bug-fix above — and updates the
  corresponding diagram shapes' `Name`/`HasName` immediately before clearing
  the selection; editing `TaxiPathEditViewModel.TaxiNameId` directly (as the
  grid's picker does) has the same immediate-diagram-update effect; renaming
  a `TaxiNameEditViewModel.Value` updates every diagram shape whose path
  references that name, not just one; `DeleteTaxiNameCommand` also clears the
  affected shape's label back to unnamed; `SyncTaxiwaySelectionFromRows`
  highlights exactly the shapes matching the given rows and leaves
  `ShowTaxiwayBatchEditPopover` false, including when it runs right after a
  diagram click had set it true (grid selection always wins); toggling
  `TaxiPathEditViewModel.IsHiddenFromDiagram` flips the matching shape's
  `IsVisible` and, when hiding, also clears its `IsSelected` (and thus
  `HasTaxiwaySelection`) so a hidden shape can't linger as a phantom
  selection; toggling `RunwayEditViewModel.IsHiddenFromDiagram` flips the
  matching `RunwayShape.IsVisible` the same way (no selection concept for
  runways, so nothing else to clear).
- `TaxiwayBatchEditViewModelTests` (`AirportSmith.Tests`) covers: a new
  instance starts fully untouched (`IsTaxiNameIdTouched` false,
  `TaxiNameId`/`LeftEdgeLighted`/`RightEdgeLighted` all `null`); setting
  `TaxiNameId` marks it touched even when set to `null` (an explicit "(none)"
  choice, not "untouched"); `ResetToUntouched` clears both the values and the
  touched flag.
- `MainViewModelTests` additionally covers the fourth-round bug fixes above:
  extending a diagram selection (`ToggleTaxiwaySelectionCommand` with
  `ExtendSelection: true`) after already staging a value in
  `TaxiwayBatchEdit` leaves that staged value in place, and Apply still
  writes it to every selected path
  (`ExtendingDiagramSelection_DoesNotResetAlreadyStagedBatchEditValues`);
  `SyncTaxiwaySelectionFromRows` called while `ShowTaxiwayBatchEditPopover`
  is true leaves the diagram's existing selection completely untouched
  (`SyncTaxiwaySelectionFromRows_WhilePopoverOpen_IsIgnored`) and works
  normally again once the popover is closed
  (`SyncTaxiwaySelectionFromRows_AfterPopoverCloses_WorksAgain`).
- `AirportDataTreeBuilderTests` was updated for `ApproachLightSystem
  .SystemType`/`Runway.*VasiType` becoming real enums (`ApproachLightSystemType`/
  `VasiType`): they now render via the builder's existing enum-`ToString()`
  fallback (e.g. `"SystemType: Alsf2"`) instead of the old raw-number-plus-
  label format, so their now-redundant label dictionaries were deleted from
  `AirportDataTreeBuilder` rather than kept alongside real enum types. Also
  updated for `TaxiPathSegment.Name` becoming `TaxiNameId`: the tree's
  per-item summary label now shows `(named, id <guid>)` or
  `(unnamed, type N)` rather than a resolved name string, since this raw
  inspector has no `AirportDetails.TaxiNames` in scope at that call site.
- `AirportDiagramProjectorTests` was updated for `TaxiwaySegmentShape`
  gaining `SourceIndex` (verified to track a segment's real position in
  `AirportDetails.TaxiPaths`, not its position in the filtered
  `TaxiwaySegments` list, when an earlier path is excluded) and for taxi
  names resolving via `AirportDetails.TaxiNames`/`TaxiNameId` instead of a
  flat string (including a `TaxiNameId` that doesn't match any `TaxiNames`
  entry rendering the same as unnamed, not a crash or stale label).
- The Edit tab itself (`MainWindow.xaml`'s new tab, including its embedded
  diagram, the taxiway click-to-select interaction, the batch-edit popover's
  placement/rendering, and its `DataGrid`/`ComboBox` editing controls) and the
  real `AirportProjectStore`/`SimConnectService` I/O are not covered by
  automated tests (real WPF
  rendering/editing and real file/SimConnect I/O) — verified manually per
  `CLAUDE.md`'s testing policy, including confirming `RUNWAY.EDGE_LIGHTS`'s
  INT8 marshaling against a live sim (see the Known limitations above).
- `GeoProjectionTests` (`AirportSmith.Tests`) covers: `UnprojectLocalPoint` is
  the exact inverse of `ProjectLatLon` for representative reference points/
  offsets (round-trip within a small epsilon); `AirportDiagramProjectorTests`
  was updated only to call through the extracted `GeoProjection` helper
  (behavior-preserving refactor, no new/changed assertions).
- `AirportXmlExporterTests` (`AirportSmith.Tests`, pure via `Build`, no fakes
  needed) covers: the root `<FSData version="9.0">` and `<Airport>`
  attributes match the input `AirportDetails`; `<DeleteAirport>` has exactly
  `deleteAllRunways`/`deleteAllTaxiways` set and no frequency/jetway flags; a
  fully-populated runway (edge lights, all 4 VASI slots including the new
  bias/spacing fields, both approach light systems, all 6 pavement features)
  round-trips into the correct attributes/child elements with correct
  enum-to-string mapping; a runway with all-null optional slots omits those
  elements entirely rather than emitting empty/default ones; the two
  synthesized `<RunwayStart>` positions, re-projected through
  `GeoProjection.ProjectLatLon`, land within a small epsilon of the runway's
  own computed threshold points; `<TaxiwayPoint>`/`<TaxiwayParking>`/
  `<TaxiName>`/`<TaxiwayPath>` appear in that exact schema-required order; a
  taxi path with `TaxiPathType.Unknown`/`.PaintedLine` is skipped and adds a
  warning; a taxi name over 8 characters is truncated and warns; an
  unmapped/out-of-range surface or parking type/name/suffix code falls back
  to its documented safe default and warns; no `<Jetway>` or `<Com>` element
  is ever emitted regardless of input; `Export` (real file write, to a temp
  directory — never `AppDataHelper.AppDataPath`, per `CLAUDE.md`'s guardrail)
  produces a file `XDocument.Load` reads back with the same structure `Build`
  produced; per the first-round revision above, a taxi path with one
  resolved end and one unresolved end emits no `<TaxiwayPoint>` for its
  resolved end when nothing else references it
  (`Build_PathWithOneUnresolvedEnd_DoesNotEmitOrphanedTaxiwayPointForTheResolvedEnd`),
  and a mix of one exportable and one partially-unresolved path emits
  `<TaxiwayPoint>`s only for the indices the exported path(s) actually use
  (`Build_MixOfExportableAndUnresolvedPaths_OnlyEmitsPointsUsedByExportedPaths`);
  per the second-round revision above, a point with `TaxiPointType.HoldShort`
  and `TaxiPointOrientation.Reverse` emits `type="HOLD_SHORT"
  orientation="REVERSE"`, while an unrelated point with no resolved type on
  the same path still falls back to `type="NORMAL"` with no `orientation`
  attribute at all (`Build_TaxiwayPointWithHoldShortType_MapsTypeAndOrientation`);
  per the third-round revision above, a `Type == Parking` path's `EndIndex`
  matching a `TaxiParkingSpot.ItemIndex` produces a `<TaxiwayParking>` with
  that same `index`, a `<TaxiwayPath end="...">` matching it, and no
  redundant `<TaxiwayPoint>` at that index at all — only the `Start` end
  still gets one
  (`Build_ParkingTypePathEnd_ResolvesToTaxiwayParkingNotASynthesizedTaxiwayPoint`);
  colliding/default `ItemIndex` values across parking spots (an older saved
  project) fall back to sequential numbering with a warning rather than
  exporting duplicate `<TaxiwayParking>` indices
  (`Build_ParkingSpotItemIndicesCollide_FallsBackToSequentialNumberingAndWarns`);
  per the "OPEN, resolved" investigation above, every exported `<TaxiwayPath>`
  carries `surface="ASPHALT"` plus the Editor's own observed
  `drawSurface`/`drawDetail`/`groundMerging`/`excludeVegetationAround`/
  `excludeVegetationInside` defaults, and the export emits one warning (not
  one per path) noting the surface value is synthesized, not real SimConnect
  data
  (`Build_MappableTaxiPath_HasSurfaceAndSceneryEditorDefaultAttributes`); per
  that investigation's same-day follow-up, `TaxiPathType.Path` (what
  SimConnect reports for virtually every real taxi path) maps to XML
  `type="TAXI"`, not the schema's own literal `"PATH"` value
  (`Build_TaxiPathWithPathType_MapsToTaxiXmlType`) — both fixes together are
  now confirmed against a live Scenery Editor import.
  `Build_MappableTaxiPath_HasSurfaceAndSceneryEditorDefaultAttributes` also
  covers `weightLimit="0"`, added alongside the attribute-order cleanup that
  matched a confirmed-working sample the user inspected directly;
  `number`/`designator` are only emitted when a path's XML type is
  `"RUNWAY"` — a non-RUNWAY path with a runway association gets neither
  attribute and a warning instead
  (`Build_TaxiTypePathWithRunwayAssociation_OmitsNumberAndDesignatorAndWarns`,
  `Build_RunwayTypePathWithRunwayAssociation_IncludesNumberAndDesignator`).
- `MainViewModelTests` additionally covers: `ExportXmlCommand.CanExecute`
  depends on whether an `IAirportXmlExporter` and `IFileDialogService` were
  injected and whether an airport is loaded; executing it delegates to the
  exporter and sets `LastXmlExportPath`/`LastXmlExportWarnings`; a cancelled
  save-file dialog leaves both untouched — via the new
  `Fakes/FakeAirportXmlExporter`.

### Amendment: fixed `<Vasi>` biasX/biasZ export — confirmed wrong against a live Scenery Editor import

**Root cause, confirmed 2026-09-20 via a real round trip:** a LEFT PAPI was
added to OIBK runway 09L (`LengthMeters=3645.635986328125`) in AirportSmith
via the diagram's click-to-place (added the previous session), exporting
`biasX="-54.75245734797872"` `biasZ="267.35724458909516"`. On import into the
MSFS 2024 SDK Dev Mode Scenery Editor, `biasX` silently reset to `0` (no
error) — and manually re-placing the PAPI at roughly the intended real-world
spot and reading its properties back gave `biasX=38`, and a Z-axis value of
`1439` (labelled `biasY` in the Editor's own property panel UI). Checked
directly against the local MSFS 2024 SDK docs
(`Documentation/public/retail/content-configuration/environment/
airports-and-facilities/runway-xml-properties`): `<Vasi>`'s `biasZ` is
documented as "distance along the runway **from the runway center point** to
the VASI reference point" — not from the threshold, which is what
AirportSmith's own `Runway.*VasiBiasZMeters` (and the Edit tab's Z column,
diagram rendering, and click-to-place — all unchanged, still "distance
inward from that end's threshold", the more intuitive authoring convention)
actually store. `biasX` is documented only as "distance ... across the
runway width" with no sign — `side` (LEFT/RIGHT) already carries which
physical side, and exporting AirportSmith's own signed drawing-convention
value produced the observed `0`-reset.

Both confirmed by the numbers themselves:
`halfLength (1822.818) - ourBiasZ (267.357) = 1555.461`, matching the
manually-read `1439` closely enough (same order of magnitude and sign, an
eyeballed drag-placement in a different editor/zoom level) to confirm the
"from center" hypothesis over the old "from threshold" one; and
`Math.Abs(-54.75) = 54.75` vs. the manually-read `38` similarly confirms
"unsigned magnitude" over "signed."

**Fix — `AirportXmlExporter.AddVasi`** (the only code changed; the Edit
tab/diagram/click-to-place all keep their existing, more intuitive
threshold-relative/signed internal meaning — this was purely an
export-mapping bug):
- `biasZ` exported as `halfLength - Runway.*VasiBiasZMeters` (symmetric for
  both PRIMARY and SECONDARY, since each stores its own "distance inward
  from ITS OWN end's threshold").
- `biasX` exported as `Math.Abs(Runway.*VasiBiasXMeters)`.
- Covered by
  `AirportXmlExporterTests.Build_Vasi_ConvertsThresholdRelativeBiasToSdkDocumentedCenterRelativeAndUnsignedX`
  (asserts against the exact real OIBK numbers above) and updated
  `Build_FullyPopulatedRunway_MapsAttributesAndSubElementsWithCorrectEnumStrings`/
  `Build_MissingVasiPosition_DefaultsToZeroAndWarns` (a missing/defaulted
  `BiasZMeters` of `0` now correctly converts to "at the threshold", i.e.
  `halfLength`, rather than the old accidental "at the runway center").
- **Not yet re-confirmed against a live import** (the round trip above used
  the pre-fix export) — worth re-testing the same OIBK 09L PAPI once this
  fix ships, to close the loop.
- Actually importing the generated XML into the MSFS 2024 SDK Dev Mode
  Scenery Editor (or compiling it with `bglcomp`) is not covered by automated
  tests (requires MSFS/the SDK) — verified manually.

### Amendment: OpenStreetMap tile layer behind the diagram (phase 0)

Implements the phase-0 committed epic above (`v0.1+ — OpenStreetMap tile
layer behind the diagram (phase 0)`) — see that section for the user
story/acceptance criteria and `background-map-research.md` for the full
research behind it.

- `GeoProjectionTests` — the previous entry above only pinned round-trip
  consistency (`ProjectLatLon`/`UnprojectLocalPoint` invert each other),
  which the new WGS84-based formula still satisfies unchanged (same 4
  `InlineData` cases and 2 reference-point facts pass with no modification).
  Added `ProjectLatLon_OneDegreeOffset_MatchesKnownWgs84MetersPerDegree`
  (asserts a 1° lat/lon offset at 4 representative latitudes against
  independently-computed WGS84 meridian/prime-vertical meters-per-degree
  values, pinning *absolute* accuracy for the first time) and
  `ProjectLatLon_AtNonEquatorialLatitude_DiffersFromOldFlatEquatorialConstant`
  (regression guard against reverting to the old flat `111_320` constant).
  Ripple-checked: the full suite (`AirportDiagramProjectorTests`,
  `AirportXmlExporterTests`) still passes unmodified after the fix — their
  hand-calculated fixtures use `Latitude = 0`, where the new formula's delta
  from the old constant is sub-millimeter, within existing tolerances.
- `AirportDiagramProjectorTests.Project_SetsReferenceLatitudeLongitude_FromAirport`
  confirms the new `AirportDiagram.ReferenceLatitude`/`ReferenceLongitude`
  fields come through from `AirportDetails.Latitude`/`Longitude`.
- `MapTileMathTests` (`AirportSmith.Tests`, pure/no fakes) covers:
  `LatLonToTileFraction`/`LatLonToTile`/`TileToLatLon`/`MetersPerPixel` each
  against hand-computed values (from the published Web Mercator slippy-map
  formulas, not by running the code under test); `SelectZoom` picks an exact
  match and clamps to `MinZoom`/`MaxZoom` at extreme scales; `GetVisibleTiles`
  returns exactly one tile for a zero-area viewport inside it, exactly the
  expected four for a viewport spanning four tile centers, and is capped at
  `MaxVisibleTiles` for a viewport covering the whole world.
- `MapTileDiskCacheTests` (`AirportSmith.Tests`, real file I/O against an
  explicit temp directory — never `AppDataHelper.AppDataPath`, per
  `CLAUDE.md`'s guardrail, same pattern as `AirportProjectStoreTests`) covers:
  an uncached tile returns null; store-then-get round-trips the same bytes;
  a tile with no `Cache-Control` header is still served within the 7-day
  retention floor; a short server `max-age` doesn't shorten that floor; a
  longer server `max-age` is honored past the floor; a tile past both its
  `max-age` and the floor returns null (re-fetch expected).
- `MapTileServiceTests` (`AirportSmith.Tests`, via `Fakes/FakeMapTileSource`/
  `Fakes/FakeMapTileCache`) covers the cache-then-fetch composition: a cache
  hit never calls the source; a cache miss fetches from the source and stores
  the result; a source failure returns null without storing anything.
- `MainViewModelTests` — `IsMapAvailable_ReflectsWhetherMapTileServiceWasInjected`
  and `ShowMap_DefaultsFalse_AndRaisesPropertyChangedWhenSet` (via
  `Fakes/FakeMapTileService`), same pattern as the existing
  `IsXmlExportAvailable`/`IsDevModeExportAvailable` availability tests.
- Not covered by automated tests, verified manually per `CLAUDE.md`'s testing
  policy (same category as the existing zoom/pan-interaction exclusion
  above): `AirportDiagramView`'s actual tile rendering and the debounced
  refresh timer (real WPF), and `OsmMapTileSource`'s real HTTP behavior
  (requires live network access and cannot assert on a third party's actual
  tile content/response codes from a unit test).
- **Bugs found via live manual testing — two, both fixed:** the first live
  test (a real loaded airport, "Show map" checked) showed only one small,
  wrongly-placed tile, and a different single tile each time the diagram was
  zoomed, rather than a coherent map filling the viewport. `MapTileMath`'s own
  tile placement/sizing math checked out correctly against hand-computed and
  realistic-viewport values (see `MapTileMathTests` above), so two separate
  causes were pursued:
  1. A single pan/zoom makes many tiles newly visible at once (a realistic
     800×600 fit-to-view viewport needs ~16), and
     `AirportDiagramView.RefreshVisibleTilesAsync` requested every one of them
     from `MapTileService` **concurrently, with no cap** — plausible to leave
     only one or two tiles rendering if OSM's real tile server throttled the
     burst (indistinguishable from an ordinary failed fetch, since
     `IMapTileSource`'s contract already treats any failure as "just don't
     draw it"). **Fixed:** `MapTileService.GetTileAsync` now serializes its
     own work through a `SemaphoreSlim` capped at 2 concurrent operations —
     the classic "per-host connection" convention most polite tile-consuming
     desktop clients use. Regression-tested
     (`MapTileServiceTests.GetTileAsync_ManyConcurrentRequests_NeverExceedsConcurrencyCap`).
     Good practice and moves the app closer to OSM's own tile usage policy
     regardless, but **re-testing against the same live session showed the
     exact same symptom, unchanged** — this alone wasn't the (or the whole)
     cause.
  2. The actual cause: `MapTiles`'s `DataTemplate` positioned each tile
     `<Image>` via `Canvas.Left`/`Canvas.Top` attached properties on the
     template root — the exact same pattern already documented as **confirmed
     broken** for the `TaxiwayPoints` template earlier in this section (every
     one of 381 items rendering stacked at the same (0,0)-ish spot instead of
     its own position, root cause never identified, worked around with an
     absolute-coordinate `Path`/`EllipseGeometry` instead). `MapTiles` hit the
     identical symptom: every fetched tile image rendering stacked near
     canvas-origin regardless of its real position, which is why only one
     (whichever loaded last) was ever visible, in the wrong place, changing
     with every zoom (a different real-world tile, still misplaced). **Fixed**
     the same way `TaxiwayPoints` was: each tile is now a `Path` with an
     absolute-coordinate `RectangleGeometry` filled with an `ImageBrush`,
     instead of `Canvas.Left`/`Top` + `Width`/`Height` on an `<Image>` —
     `MapTileViewModel` now exposes `Fill`/`Rect` instead of
     `Image`/`Left`/`Top`/`Width`/`Height`. **Not yet re-confirmed against a
     live session** — this is the fix expected to actually resolve the
     originally-reported symptom; the concurrency cap above should still make
     the fill-in visibly progressive (tiles appearing a couple at a time)
     rather than all at once.

## Bug fix: Parking-type taxi paths invisible on diagram (2026-09-23)

**User-reported symptom:** taxiway paths of `Type == Parking` (the short stub
connecting a taxiway point to a parking stand) never rendered as a line on
the diagram, even though the parking spot itself (`ParkingSpotShape`, the
orange dot) rendered fine.

**Root cause:** `AirportDiagramProjector.Project`'s `TaxiwaySegments` filter
only ever included `Taxi`/`Path`/`Runway`-typed paths. The exclusion of
`Parking` was deliberate when `Runway` was added (see the OIBK anomaly
writeup above), reasoned as "its `End` doesn't reference a taxi point at all
... so there's no sensible line endpoint to draw for it." That reasoning was
stale: `SimConnectService.ResolveTaxiPathPoints` already resolves a `Parking`
path's `EndXMeters`/`EndZMeters` to its `TaxiParkingSpot`'s own
`BiasXMeters`/`BiasZMeters` (confirmed in code, not just theory), so a real
line endpoint has been available all along — it just wasn't being drawn.

**Fix:** `Parking` is now included in the `TaxiwaySegments` filter alongside
`Taxi`/`Path`/`Runway`. `TaxiwaySegmentShape` gained `IsParkingType` (same
pattern as the existing `IsRunwayType`), and `AirportDiagramView` renders a
`Parking`-typed segment as a solid `DarkOrange` line (matching the parking
spot dots' own color) instead of the ordinary named/unnamed blue/gray
styling, so it reads as "this stand's own lead-in," not an ordinary taxiway.

Unaffected by this fix (still correct, unrelated code path): a `Parking`
path's `End` still does NOT get a synthesized `TaxiwayPoint` red-dot marker
(`AirportDiagramProjector.Project`'s `taxiwayPointsByIndex` loop and
`AirportXmlExporter.BuildTaxiwayPoints` both still special-case
`Type == Parking` for that, since that index is a `TaxiwayParking` reference,
not a `TAXI_POINT` one) — only the line itself was the gap.

- Test coverage: `AirportDiagramProjectorTests.Project_TaxiwaySegment_ParkingType_IsIncludedWithIsParkingTypeTrue`
  (replaces the old `..._ParkingType_IsExcluded`);
  `Project_TaxiwaySegments_SourceIndexSkipsExcludedSegments` now uses a
  `Closed`-typed path (a type still genuinely excluded) to exercise the
  index-skip behavior, since `Parking` is no longer excluded.
- No persisted-data/AppData impact — this only changes in-memory diagram
  projection and WPF styling, not any serialized project/settings format.

### Follow-up: moving a parking spot didn't move its linked taxiway line (2026-09-23)

**User-reported symptom:** once Parking-type paths were drawn (the fix
above), moving a parking spot's position (grid Bias X/Z edit, or diagram
click-to-place) moved the spot's own orange dot but left its lead-in stub
line stranded at the old position — a newly-visible desync that didn't exist
while the line wasn't drawn at all.

**Root cause:** `ParkingSpotEditViewModel.BiasXMeters`/`BiasZMeters` already
wrote the new position through onto every linked `Type == Parking` path's
`EndXMeters`/`EndZMeters` (this write-through predates the fix above — it
was already needed for the Airport Data tab / a hypothetical future
consumer). But `TaxiwaySegmentShape.End`/`MidPoint`/`WidthCorners` were
`init`-only, computed once by `AirportDiagramProjector.Project` at load time
and never refreshed — unlike `ParkingSpotShape.Center`/`HeadingTip`, which
`MainViewModel.RefreshParkingShape` already recomputes live on every edit.

**Fix:**
- `TaxiwaySegmentShape.End`/`MidPoint`/`WidthCorners` are now mutable
  (settable, `INotifyPropertyChanged`), same pattern as `ParkingSpotShape`'s
  own live-updatable fields. `Start` stays `init`-only — it's always a taxi
  point, and the Edit tab has no way to move one.
- New `AirportDiagramProjector.ComputeTaxiwaySegmentPlacement(diagram, segment)`
  recomputes just one segment's screen-space `End`/`MidPoint`/`WidthCorners`
  from its current `StartXMeters`/`StartZMeters`/`EndXMeters`/`EndZMeters`/
  `WidthMeters`, reusing the diagram's existing origin (so the rest of the
  diagram doesn't shift) — same "recompute just this shape" approach
  `ComputeParkingPlacement` already used for the spot's own dot. Returns
  `null` if Start/End aren't both resolved. The rectangle-corner math itself
  was factored out of `Project`'s taxiway loop into a shared
  `BuildTaxiwayWidthCorners` helper so both stay in agreement.
- `ParkingSpotEditViewModel` now exposes `LinkedParkingPaths` (the same list
  its Bias setters already write through to). `MainViewModel` gained
  `RefreshParkingLeadingTaxiways`, called from `OnParkingSpotEditChanged`
  only on a Bias X/Z change (Number/Heading/Radius don't affect a path's
  line): for each linked path, finds its `TaxiwaySegmentShape` by
  `SourceIndex` (via `Airport.TaxiPaths.IndexOf`, matching by reference) and
  pushes the recomputed geometry onto it. Covers both the Parking Spots
  grid's Bias fields and diagram click-to-place (`PlaceParking`), since both
  go through the same `ParkingSpotEditViewModel.BiasXMeters`/`BiasZMeters`
  setters.

- Test coverage: `AirportDiagramProjectorTests.ComputeTaxiwaySegmentPlacement_AfterEndMoves_MatchesHandCalculatedCoordinates`/
  `ComputeTaxiwaySegmentPlacement_UnresolvedStartOrEnd_ReturnsNull`;
  `MainViewModelTests.EditingParkingSpotPosition_AlsoMovesItsLinkedParkingTaxiwayShape`
  (also confirms an unrelated `Taxi`-type path sharing the same `EndIndex`
  by coincidence is left untouched — only `Parking`-type paths are linked).
- No persisted-data/AppData impact — same as the fix above.
