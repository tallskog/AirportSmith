# Airport diagram rendering — research

**Status: research only — not a committed requirement.** Prompted by
comparing a real FAA-style airport diagram (runways, lettered/numbered
taxiway segments, aprons, terminal buildings, control tower, VOR/DME) against
what AirportSmith currently pulls, to see what it would take to render
something similar from SimConnect data. Related: [`taxiway-name-research.md`](taxiway-name-research.md).

## What's drawable today vs. what's missing

| Element | Model | Position data today | Drawable now? |
|---|---|---|---|
| Runway | `Runway` | Center `Latitude`/`Longitude` + `HeadingDeg` + `LengthMeters` + `WidthMeters` | **Yes** — enough to compute both thresholds and draw a rotated rectangle |
| Taxiway segment | `TaxiPathSegment` | Only `StartIndex`/`EndIndex` — abstract node indices, no coordinates at all | **No** — needs `TAXI_POINT` data, not currently requested (see below) |
| Parking spot | `TaxiParkingSpot` | Only `HeadingDeg`/`RadiusMeters` — no position at all (`Latitude`/`Longitude` were removed earlier after confirming `TAXI_PARKING` has no such field) | **No** — needs a different field than was tried before (see below) |
| Terminal buildings / control tower / aprons | — | Not pulled at all | Out of scope — MSFS Facility Data doesn't expose static scenery/building footprints; this was already flagged as out-of-v0.1-scope in `CLAUDE.md`'s domain research |

## New finding: `TAXI_POINT` and `TAXI_PARKING` use local offsets, not lat/lon

Checked the SimConnect Facility Data reference
(`SimConnect_AddToFacilityDefinition`, `docs.flightsimulator.com`) for the
exact field lists of the two structs that would supply position:

- **`TAXI_POINT`**: `TYPE`, `ORIENTATION`, `BIAS_X`, `BIAS_Z`
- **`TAXI_PARKING`**: `TYPE`, `TAXI_POINT_TYPE`, `NAME`, `SUFFIX`, `NUMBER`, `ORIENTATION`, `HEADING`, `RADIUS`, `BIAS_X`, `BIAS_Z`

Neither has a `LATITUDE`/`LONGITUDE` field — position is a **local Cartesian
offset** (`BIAS_X`/`BIAS_Z`, presumably meters relative to the airport
reference point) instead. This is good news for the diagram feature:

- **Taxiway nodes**: adding `TAXI_POINT` to the facility definition and
  joining `TaxiPathSegment.StartIndex`/`EndIndex` against it (the same
  pattern `TAXI_NAME` resolution already uses for taxi path *names*, per
  `SimConnectService.ResolveTaxiPathNames`) would give every taxiway
  segment's endpoints directly, no lat/lon math needed for that part.
- **Parking spots**: `TAXI_PARKING` already carries its own `BIAS_X`/`BIAS_Z`
  — no join needed at all. The earlier removal of
  `TaxiParkingSpot.Latitude`/`Longitude` was correct (that field genuinely
  doesn't exist), but `BIAS_X`/`BIAS_Z` is a different, real field that
  hasn't been tried yet.

**Caveat, consistent with this project's own established discipline:** field
*order* and *type* (e.g. `FLOAT32` vs `FLOAT64`, as `HEADING`/`LENGTH`/
`WIDTH`/`RADIUS`/`FREQUENCY` all turned out to be) must be empirically
re-verified against a live sim before being trusted — guessing wrongly here
previously corrupted every field after the bad one (`requirements.md`'s
"Known gaps" section covers that history). `BIAS_X`/`BIAS_Z`'s units and
axis convention (which is "X", which is "Z", positive direction, meters vs.
feet) are documented only at a high level and should be confirmed the same
way, not assumed.

## `TAXI_POINT` ↔ `TaxiPathSegment` data model (confirmed)

`TAXI_POINT` rows form an indexed list of node positions around the airport.
`TaxiPathSegment.StartIndex`/`EndIndex` are indices into that same list —
draw a taxiway as the line between `points[StartIndex]` and
`points[EndIndex]`. Two nuances the implementation needs to account for,
both grounded in patterns/data already seen elsewhere in this project:

- **Ordering isn't guaranteed.** Exactly the same issue already solved for
  `TAXI_NAME` (`SimConnectService.ResolveTaxiPathNames` resolves
  `NAME_INDEX` in a deferred pass in `OnFacilityDataEnd`, because "order
  between TAXI_PATH and TAXI_NAME rows isn't guaranteed" —
  `requirements.md`'s known-gaps section). `TAXI_POINT` resolution needs the
  identical pattern: collect all points first, resolve indices after
  everything for the airport has arrived.
- **Not every path segment is a taxiway.** `TAXI_PATH.TYPE`
  (`TaxiPathSegment.Type`, currently stored raw/unmapped) distinguishes path
  kinds — taxiway, runway, parking, closed, etc. The OIBK `apt.dat` sample
  pulled earlier showed the equivalent split directly: most `1202` route
  edges were labeled with a taxiway letter, but some were labeled
  `"runway"` instead. Drawing every `StartIndex`/`EndIndex` pair
  identically would double-draw runway centerlines (already drawn from the
  `Runway` model) — the diagram needs to filter/style by `Type`, not treat
  `TaxiPaths` as one undifferentiated taxiway network.

## Coordinate system

- Anchor everything on the airport reference point (`AirportDetails.Latitude`/`Longitude`), which is already pulled.
- Project runway lat/lon into the same local-meters plane `BIAS_X`/`BIAS_Z`
  already use, via a flat-earth/equirectangular approximation (meters-per-degree
  latitude is constant; meters-per-degree longitude scaled by `cos(latitude)`).
  Accurate enough at airport scale (a few km) — the same approach tools like
  ADE or SkyVector use for a single-airport diagram; no need for a full
  geodesic/great-circle projection.
- Once runways and taxi points/parking are all in one local-meters coordinate
  space, drawing is just an affine transform (scale + flip Y for screen
  space) into pixels, with optional pan/zoom.

## Rendering approach

Recommend plain **WPF vector drawing** (`Canvas` + `Line`/`Polygon`/`Path`,
or a `DrawingVisual`-based custom control if segment counts get large enough
that per-element `UIElement` overhead matters — OIBK alone had ~480 taxi path
segments, so this is worth watching but not a blocker to start with the
simpler `Canvas`-based approach). No new NuGet dependency: WPF's vector
rendering is resolution/DPI-independent already, which is all a fixed 2D
top-down diagram needs. An external charting/mapping/game-engine library
(SkiaSharp, a map SDK, etc.) would be unwarranted complexity and dependency
weight for this data scale and a fixed, non-interactive-basemap use case.

Keep the projection math (lat/lon + local offsets → screen-space shapes) as
plain, WPF-independent C# — a pure function/service producing something like
a list of `RunwayShape`/`TaxiwaySegmentShape`/`ParkingSpotShape` records from
an `AirportDetails` — so it's unit-testable via the project's existing
"test pure logic via fakes" policy, with only the final data-binding-to-XAML
step needing manual/visual verification.

## Phased plan (not started — for later prioritization)

0. **Prerequisite (backend):** add `TAXI_POINT` to the facility definition;
   empirically verify its field layout/units/axis convention against a live
   sim with the same rigor as the earlier `SimConnectService` debugging
   rounds; resolve `TaxiPathSegment` start/end to coordinates the way
   `TAXI_NAME` resolution already works for names. Do the same verification
   pass for `TAXI_PARKING.BIAS_X`/`BIAS_Z`.
1. **Runway-only diagram** — fully buildable with data that already exists
   today. Good incremental milestone: proves the projection/rendering
   pipeline end-to-end before taking on the taxi-point work.
2. **Taxiway centerlines**, labeled by name, with unnamed segments drawn in a
   visually distinct style (e.g. a different color/dash). This is close to
   free once the geometry exists, and turns directly into a visual QA tool
   for the taxiway-naming-gap problem from `taxiway-name-research.md` —
   OIBK's 65%-unnamed segments would be immediately visible rather than
   needing a manual count.
3. **Parking spots** as circles/rectangles, oriented by heading, positioned
   via their own `BIAS_X`/`BIAS_Z`.
4. **Stretch:** overlay the X-Plane Gateway `apt.dat` layout for the same
   airport as a second, differently-styled layer on the same diagram —
   directly visualizes MSFS-vs-open-data mismatches in one picture instead
   of a text diff.

None of this is in `requirements.md` yet. Needs a decision on scope (how much
of the phased plan is v0.1+ vs. later) and acceptance criteria written up
before implementation starts, per `CLAUDE.md`'s process.
