# Background map behind the diagram — research

**Status: research only — not a committed requirement.** Question: can the
Diagram/Edit tab show a satellite image or a traditional map behind the
airport, and what would it take? Related:
[`airport-diagram-rendering-research.md`](airport-diagram-rendering-research.md).
Provider terms below were checked on 2026-09-20 and change often — re-check
before building anything against one.

## Short answer

**Feasible, moderate effort, no new NuGet dependency needed.** The diagram
already lives in a flat local-meters plane around the airport reference
point, north-up — the same orientation as standard Web Mercator ("slippy
map") tiles, so tiles only need to be scaled/placed, never rotated. The hard
part isn't code, it's **licensing**: there is no free, keyless, global,
high-resolution *satellite* source. A traditional map (OpenStreetMap) is easy
and allowed; satellite needs the user's own API key with one of a few
providers.

Bonus: a map underlay is also the best available way to **confirm the
`BIAS_X`/`BIAS_Z` axis convention** that `requirements.md` still lists as
UNCONFIRMED everywhere — the runway is placed from real lat/lon, the taxi
network from bias meters, so if both line up with the runway on real imagery
the convention is right (a rotation, e.g. from magnetic variation — OIBK has
`magvar=357` — or a flipped axis would be visible at a glance).

## Coordinate alignment

- `AirportDiagram` maps local meters to screen with
  `screenX = localX + OriginXMeters`, `screenY = OriginZMeters - localZ`
  (see its doc comment), so a tile placed by projecting its NW/SE corner
  lat/lon into local meters and then through that same mapping lands in the
  right place with a plain axis-aligned rectangle. Within one tile
  (~100–300 m at airport zoom levels) Mercator's non-linearity is negligible.
- **Finding — `GeoProjection` is not accurate enough for imagery.**
  `MetersPerDegLat = 111_320` is the *equatorial longitude* figure; true
  meters per degree of latitude is ~110,574 at the equator to ~111,694 at
  the poles (WGS84). At OIBK (26.53°N) the constant is **+0.47 %** too large
  north–south (true ≈ 110,796 m/°) — ~9 m per 2 km, ~24 m at 5 km. Today
  that's harmless: runway centers sit only ~70–300 m from the reference
  point, so the runway is off by ≤ ~1.4 m, and east–west is within ~0.07 %.
  But tiles extend km from the reference point, where 10–25 m of drift
  against the true-meter `BIAS_X`/`BIAS_Z` taxi data would be visible. So the
  map work should either fix `GeoProjection` (WGS84 meridian/prime-vertical
  radii; `AirportXmlExporter` uses the inverse, and both directions would
  change together, so they stay consistent with each other) or use a separate
  accurate projection for tiles only. Fixing it also slightly corrects
  runway placement at low-latitude airports. Would need tests updated
  (`GeoProjectionTests`, exporter/projector hand-calculated coordinates).
- Web Mercator ground resolution: `156543.03 · cos(lat) / 2^zoom` m/pixel.
  At OIBK: z15 ≈ 4.3 m/px, z17 ≈ 1.1, z18 ≈ 0.54, z19 ≈ 0.27. The diagram's
  zoom range is 0.25×–40× of fit-to-view (a ~4 km airport is ~0.25 px/m at
  fit, ~10 px/m at max), so the tile zoom must follow the current view scale,
  capped where imagery runs out (z19–20 at best; beyond that, upscale and
  accept blur).
- Tile volume is small: a whole 4×3 km airport at z18 is ~640 tiles; a
  screenful is ~30–60. Load only what's visible.
- `AirportDiagram` doesn't carry the reference lat/lon today — it would need
  `ReferenceLatitude`/`ReferenceLongitude` (trivial, set in `Project`).

## Where imagery comes from

| Source | Type | Access | Verdict |
|---|---|---|---|
| **OpenStreetMap** standard tiles | Map | No key. Desktop apps allowed. Must send a unique `User-Agent` naming the app (library defaults are blocked), show "© OpenStreetMap contributors" visibly, cache ≥ 7 days, honor cache headers. **No bulk download / prefetch / offline use.** | **Good default.** Fine for "load visible tiles as the user pans". Prefetching an airport's tiles or shipping them is not allowed. |
| **Esri World Imagery** (ArcGIS Location Platform) | Satellite | API key from a free ArcGIS Location Platform account; 2 M basemap tiles/month free, then $0.15/1000. ~0.3–0.5 m/px in much of the US/W. Europe, 1 m elsewhere. Older keyless tile URL is licence-restricted (not for commercial use) — don't rely on it. | **Best satellite option** with a user-supplied key. |
| **Mapbox** satellite raster tiles | Satellite | Access token; 750 k raster tile requests/month free, then $0.25/1000. Attribution required. | Good alternative, user-supplied token. |
| **MapTiler** satellite | Satellite | Key; free plan is **non-commercial only**, 100 k requests/month. | Usable for a hobby tool; restrictive if the app is ever sold. |
| **USGS National Map** imagery | Satellite (US only) | No key; public domain; 0.15–1 m. | Free and clean, but only covers the US. |
| **Bing Maps** | Satellite | Free/Basic accounts were retired 2025-06-30; Enterprise only until 2028; replaced by Azure Maps. | Not viable for a hobby project. |
| **Google Map Tiles API** | Satellite | Their terms forbid pre-fetching/caching beyond cache headers, offline use, and machine analysis of imagery; requires a Google agreement. | **Avoid.** |
| Sentinel-2 cloudless (EOX etc.) | Satellite | Free, but ~10 m/px and non-commercial | Too coarse to place a parking spot. |

Consequences for the design:
- **Bring-your-own-key** for satellite (Esri or Mapbox): the app author never
  holds a quota or a billing relationship, and the user accepts the
  provider's terms themselves. The key must be stored locally (user-scoped,
  ideally DPAPI-protected), never committed or shipped.
- The provider should be a configurable **URL template** (`{z}/{x}/{y}` plus
  key/attribution string) behind a small interface, so adding/removing
  providers doesn't touch rendering code.
- **Tracing from imagery**: the exported XML contains no imagery, so tile
  licences don't propagate to the add-on. Whether a provider's terms permit
  *deriving positions* from its imagery differs per provider and wasn't
  verified — worth reading before advertising "trace parking from
  satellite".
- **Sim vs. reality**: MSFS 2024's default airports are Asobo-authored, not
  guaranteed to match any given imagery, and different imagery vintages/
  orthorectification can be off by several meters. Expect a per-airport
  **manual offset nudge** to be needed, plus an opacity control.

## Implementation options (WPF)

1. **Custom tile layer inside `AirportDiagramView` — recommended.** A
   `Canvas` of `Image` elements under the existing shapes, in the same
   `DiagramCanvas` so it shares the existing scale/translate and pan/zoom
   for free. Pure math (tile ↔ lat/lon ↔ canvas rect, zoom-for-scale,
   visible-tile set) goes in a static, unit-testable class in the style of
   `AirportDiagramProjector`; I/O behind `IMapTileSource` /
   `IMapTileCache` interfaces with fakes for tests. Uses `HttpClient` (the app
   has none today) + disk cache under `AppDataHelper.AppDataPath\MapTiles`
   (tests must use an override path — the AppData guardrail in `CLAUDE.md`).
   Tile images `IsHitTestVisible=false` so click-select/pan are unaffected;
   re-evaluate visible tiles (debounced) on zoom/pan. Tiles outside the
   airport's canvas rectangle still show when zoomed out, which is desirable.
2. **XAML-Map-Control (WPF `MapTileLayer`).** Mature library, handles tiles,
   caching and attribution — but it *owns* pan/zoom/rotation in Web Mercator
   space, so the meter-space diagram would have to be re-hosted as map items
   and the existing view/click logic reworked. More change than option 1 for
   little gain.
3. **WebView2 + Leaflet/MapLibre.** Heavy runtime dependency and hard to keep
   pixel-aligned with a WPF overlay. Not recommended.
4. **One static image per airport** (fetched once, stored with the project):
   simplest, but no detail when zooming in, and most providers' terms on
   storing static images are stricter than for live tiles.

## UI/behavior changes it implies

- Toggle "Show map" + provider picker + opacity slider; **off by default**.
  A new outbound network call sends the airport's coordinates and the user's
  IP to a third party — disclose it, make it opt-in.
- Offline/failed tiles: show nothing (or last cached), never block the
  diagram; small status line for errors ("map unavailable").
- Existing styling assumes a white background: runway polygons are opaque
  DarkSlateGray, taxiway bands are `#33808080`, the taxi centerlines are
  thin — legibility over imagery needs a review (maybe a "map mode" style
  with outlines/halos, or diagram-opacity control).
- Attribution text must be visible over the map (OSM requires it; Esri/
  Mapbox too).
- **Persisted data:** the per-airport offset (and possibly provider id, never
  the key) would be new fields in the project file — additive with type
  defaults (0 / null) so older files load unchanged, with a load test, per
  the back-compat rules in `CLAUDE.md`. No `SchemaVersion` bump needed for
  purely additive fields. The key/provider selection are app-level settings,
  and there's no settings store yet — that's a small new piece
  (`%LocalAppData%\AirportSmith[-dev]\settings.json`).

## Suggested phasing (if pursued)

0. **Spike** — OSM tiles only, no settings, no persistence: fixes/adds the
   accurate projection, tile math + tests, tile layer in `AirportDiagramView`
   behind a checkbox. Answers the axis-convention question immediately.
1. **Satellite** — settings store, provider abstraction, user-supplied
   Esri/Mapbox key, attribution, disk cache.
2. **Calibration** — per-airport offset nudge + opacity persisted in the
   project file; map-mode diagram styling.

Rough size: phase 0 is a few hundred lines plus tests; phases 1–2 are similar
each. None require SimConnect or a live sim.

## Sources

- [OSM tile usage policy](https://operations.osmfoundation.org/policies/tiles/)
- [ArcGIS Location Platform pricing](https://location.arcgis.com/pricing/),
  [Static Basemap Tiles service](https://developers.arcgis.com/rest/static-basemap-tiles/)
- [Mapbox pricing](https://www.mapbox.com/pricing)
- [MapTiler pricing](https://www.maptiler.com/cloud/pricing/),
  [terms](https://www.maptiler.com/terms/)
- [Bing Maps for Enterprise retirement](https://blogs.bing.com/maps/2025-01/What-are-my-options-regarding-Bing-Maps-for-Enterprise-Retirement)
- [Google Map Tiles API policies](https://developers.google.com/maps/documentation/tile/policies)
- [USGS National Map base map services](https://www.usgs.gov/faqs/what-are-base-map-services-or-urls-used-national-map)
- [XAML-Map-Control](https://github.com/ClemensFischer/XAML-Map-Control)
