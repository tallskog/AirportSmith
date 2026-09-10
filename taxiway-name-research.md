# Taxiway name research (open-data sources)

**Status: research only — not a committed requirement.** Nothing here has been
turned into a `requirements.md` epic yet. Captured for later reference when the
"verify/correct taxiway names" feature gets prioritized.

**Goal being investigated:** the project's longer-term aim is to produce
airports with correct taxiway names, or at minimum automatically verify
whether an MSFS 2024 default airport's taxiway names are right or wrong. This
doc tracks open-source data options for that comparison, and one concrete
case study.

## Open-data sources considered

| Source | Coverage | Taxiway names? | Verdict |
|---|---|---|---|
| **X-Plane Scenery Gateway (`apt.dat`)** | Global, ~35,000 airports, community-contributed | Yes — pavement polygon names + a separate ATC taxi-route graph with named edges | **Best candidate** — only option with both global coverage and a real taxiway-name concept |
| OpenStreetMap | Global but wildly inconsistent per-airport | `aeroway=taxiway` ways carry a `ref=*` tag | Usable as an opportunistic secondary check only; most airports aren't tagged in detail |
| FAA NASR / Aeronautical open data (ArcGIS) | US only | Yes, official "AM Taxiway" GIS feature class | High quality but scoped to US airports only — doesn't fit a worldwide MSFS 2024 target |
| OurAirports open CSVs | Global | **No** — runways/frequencies/navaids only, no taxiway dataset at all | Ruled out |

## X-Plane Scenery Gateway licensing

No dedicated Terms-of-Service page was found at `gateway.x-plane.com` (the
usual URL patterns 404). What's confirmed instead, from the API docs page and
Laminar Research's own community statements:

- Airport data submitted to the Gateway is released under **GPL v2**,
  described explicitly by the Gateway's maintainer as "Free software
  available for your own use for whatever purpose... even including
  inclusion in other simulators." **No X-Plane-only restriction.**
- The only account-ban provision in the docs applies to people *uploading*
  scenery who bypass upload validation — irrelevant to AirportSmith, which
  would only ever be a data *consumer*.
- No formal rate limit; the docs ask only that callers "be considerate of
  server load."

**What GPL2 means in practice for AirportSmith:**
- Read-only, in-memory comparison (fetch `apt.dat` for an ICAO at runtime,
  parse taxiway names, diff against SimConnect's output, show a
  match/mismatch) — low friction, no redistribution involved.
- Caching/bundling Gateway data long-term, or shipping any of it inside a
  generated Community package, is where GPL2's copyleft actually engages —
  would need attribution/license notices and to stay under GPL2 terms.
- The safest long-term pattern for the "produce corrected airports" goal:
  use Gateway data to *inform* what a corrected name should be, but only
  ever write AirportSmith's own decided value into generated output — never
  copy Gateway geometry/text verbatim into a shipped package.

## `apt.dat` structure relevant to taxiway names

Pulled via the Gateway REST API: `GET /apiv1/airport/{ICAO}` →
`recommendedSceneryId` → `GET /apiv1/scenery/{id}` → `masterZipBlob` is a
base64 ZIP containing `{ICAO}.dat` (the actual apt.dat text).

Two places carry taxiway names:
- **Row code `110`** (pavement polygon start) — a single-letter identifier
  per taxiway/ramp surface, e.g. `110 1 0.00 270.0000 B surface`.
- **Row code `1200`-series** — the separate ATC taxi-route graph.
  `1201` defines named nodes (hold-short points like `A_stop`, `B_stop`).
  `1202` defines edges as `node1 node2 direction name group`, e.g.
  `1202 24 25 twoway taxiway_A A`.

The `1202` edge model maps conceptually 1:1 onto SimConnect's
`TAXI_PATH`/`TAXI_NAME` model (a node/edge graph with a name per segment),
so a comparison tool is structurally straightforward to build — modulo the
data-quality caveat below.

## Case study: OIBK (Kish Island)

Chosen because the user already knows from manual inspection that MSFS's
default OIBK is missing taxiways on the right side of the field.

### Gateway `apt.dat` (recommended submission, sceneryId 60384)
- Pavement (`110`) names found: **A, B, C, F, G, H, J** (7 distinct
  taxiway/ramp letters).
- ATC route-network (`1202`) edge names actually used: only **`taxiway_A`**
  and **`taxiway_F`** — every other edge in the graph is unlabeled, and
  several `1201` hold points are literally named `unnamed entity(split)`.
  So even this open-data source's *routing* metadata is incomplete for this
  airport, despite the pavement layer naming 7 sections. Only one of two
  accepted Gateway submissions for OIBK was checked — the other was not
  pulled (out of scope for this round).

### MSFS 2024 live pull (via SimConnect, using AirportSmith's new
Debug-only "Export Debug Data" feature — see `requirements.md`)
- **479 taxi path segments** returned in total.
- **170 segments named `"A"`.**
- **309 segments (65%) have an empty name** — no letter at all.
- No `B`, `C`, `F`, `G`, `H`, or `J` appear anywhere in the MSFS data.
- Runways cross-check clean against Gateway: `09L/27R` (3645.6 m) and
  `09R/27L` (3656.3 m) — same designations, near-identical lengths.
- 35 parking spots returned.

### Finding
MSFS's default OIBK genuinely under-names its taxiway network compared to
the open-data reference: only one letter (`A`) is ever assigned, covering
35% of segments, with the rest unnamed. This is consistent with the user's
manual observation that the right side of the airport is "missing" its
taxiways — though this data alone doesn't prove *spatially* which unnamed
MSFS segments correspond to which Gateway letters, since SimConnect's
`TAXI_PATH.StartIndex`/`EndIndex` are internal node indices, not
coordinates (`TAXI_POINT` resolution to lat/lon is explicitly out of scope
for v0.1 per `requirements.md`).

## Open questions / possible next steps (not started)

- Check OIBK's second (non-recommended) Gateway submission for a more
  complete route-network naming pass.
- Resolve `TAXI_POINT` coordinates for MSFS's unnamed segments so they can
  be spatially matched against Gateway's lettered pavement polygons —
  needed to actually confirm *which* real-world taxiway each unnamed MSFS
  segment corresponds to, not just that naming coverage is thin. See
  [`airport-diagram-rendering-research.md`](airport-diagram-rendering-research.md)
  for what `TAXI_POINT`/`TAXI_PARKING` actually expose (local `BIAS_X`/
  `BIAS_Z` offsets, not lat/lon) — the same data this needs would also
  unlock a rendered airport diagram, including an overlay comparing MSFS's
  taxiway layout against Gateway's.
- Decide whether verification-only (diff + report) or corrected-output
  generation (writing a name into generated `<Airport>` XML) is the actual
  v0.1+ target, and turn whichever is chosen into a `requirements.md` epic
  with acceptance criteria before implementation starts.
- Re-run the same Gateway-vs-SimConnect comparison against 2-3 more
  airports before generalizing "MSFS under-names taxiways" beyond this one
  data point.
