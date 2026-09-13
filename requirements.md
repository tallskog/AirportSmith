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
  (a Name picker drawing only from that list — not free text — plus
  left/right edge lighting checkboxes); one editable row per runway (edge
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
  existing selection); Ctrl+click adds/removes it from a multi-selection.
  Selected taxiways are visually highlighted. With one or more selected, a
  popover lets the user set a name and/or left/right lighting and apply
  only the field(s) actually touched to every selected path at once — an
  untouched field is left exactly as it was on each individual path, never
  overwritten with another selected path's value (see the third-round
  feedback above); Cancel or Apply both clear the selection afterward. The
  read-only Diagram tab's existing pan-by-dragging-anywhere behavior
  (including over a taxiway) is unchanged, since it never wires up the
  click-to-select command.
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

## Proposed v0.1+ epics (not yet committed — for prioritization with the user)

These are draft candidates surfaced by the research above, not approved user stories. Each needs to be broken into concrete acceptance criteria once prioritized.

1. **Edit runway geometry/taxiway routing/parking data.** UI to modify runway surface/length, taxiway routing, and parking spot type/heading/radius. Taxi path naming/lighting and runway lighting (edge lights, VASI/PAPI, approach lights) are already committed above — this covers the rest of the original "Edit runway/taxiway/parking data" idea.
2. **Generate SDK-compatible `<Airport>` XML.** Produce Dev-Mode/PackageTool-compatible XML for the edited airport, including the `<Exclude>` entries needed to properly override the stock version. Still needed before any edit (including the ones committed above) can take effect in MSFS.
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
