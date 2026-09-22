using System.Net.Http;

namespace AirportSmith.Services;

// Fetches standard OpenStreetMap raster tiles over HTTP - the app's first
// outbound network call (see MainViewModel.ShowMap: off by default, opt-in).
//
// SPIKE-ONLY CHOICE, NOT FOR WIDE DISTRIBUTION: tile.openstreetmap.org is
// OSMF's own tile server, whose usage policy
// (https://operations.osmfoundation.org/policies/tiles/) discourages
// third-party desktop apps hitting it at scale without prior arrangement.
// This default is fine for the author's own manual testing of this phase-0
// spike; revisit the endpoint (and the placeholder User-Agent contact string
// below) before this app is distributed to other users - see
// background-map-research.md and requirements.md's phase-0 entry.
//
// OSM's tile usage policy requires: a unique User-Agent identifying the app
// (never a default HttpClient/library User-Agent - enforced by setting it
// once here, not per-request), and callers must cache what they fetch for at
// least 7 days (enforced by MapTileDiskCache, not here - this class is pure
// fetch).
public class OsmMapTileSource : IMapTileSource
{
    private const string DefaultUrlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    private readonly HttpClient _httpClient;
    private readonly string _urlTemplate;

    public OsmMapTileSource(string userAgent, string urlTemplate = DefaultUrlTemplate)
    {
        _urlTemplate = urlTemplate;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
    }

    public async Task<MapTileFetchResult?> FetchTileAsync(int x, int y, int zoom, CancellationToken ct)
    {
        var url = _urlTemplate
            .Replace("{z}", zoom.ToString())
            .Replace("{x}", x.ToString())
            .Replace("{y}", y.ToString());

        try
        {
            using var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            var maxAge = response.Headers.CacheControl?.MaxAge;
            return new MapTileFetchResult(bytes, maxAge);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // A missing tile is drawn as "nothing," never surfaced as an
            // error - see IMapTileSource's doc comment. TaskCanceledException
            // also covers HttpClient's own request-timeout case (it throws
            // this, not TimeoutException, when no explicit CancellationToken
            // fired).
            return null;
        }
    }
}
