using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace AirportSmith.Services;

// One OSM aeroway=taxiway way: Name is its `ref` (what OSM uses for taxiway
// letters like "A" or "B2"), falling back to `name`, or null when it has
// neither. Unnamed ways are kept on purpose — see TaxiwayNameSuggester.
public record OsmTaxiway(string? Name, IReadOnlyList<(double Lat, double Lon)> Points);

// Either the ways found, or a user-facing error message.
public record OsmTaxiwayFetchResult(IReadOnlyList<OsmTaxiway>? Taxiways, string? Error);

public interface IOsmTaxiwaySource
{
    Task<OsmTaxiwayFetchResult> FetchTaxiwaysAsync(double lat, double lon, double radiusMeters, CancellationToken ct);
}

// Fetches OSM taxiway ways around a point through the public Overpass API —
// the app's second outbound network call after the map tiles, and like them
// only ever user-triggered (MainViewModel.SuggestTaxiNamesFromOsmCommand),
// one request per click. Never throws for an ordinary failure; returns an
// Error message instead.
public class OverpassTaxiwaySource : IOsmTaxiwaySource
{
    private const string DefaultEndpoint = "https://overpass-api.de/api/interpreter";

    private readonly HttpClient _httpClient;
    private readonly string _endpoint;

    public OverpassTaxiwaySource(string userAgent, string endpoint = DefaultEndpoint)
    {
        _endpoint = endpoint;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
    }

    public async Task<OsmTaxiwayFetchResult> FetchTaxiwaysAsync(double lat, double lon, double radiusMeters, CancellationToken ct)
    {
        var query = string.Format(CultureInfo.InvariantCulture,
            "[out:json][timeout:60];way[\"aeroway\"=\"taxiway\"](around:{0:F0},{1:F6},{2:F6});out tags geom;",
            radiusMeters, lat, lon);
        try
        {
            using var content = new FormUrlEncodedContent([new KeyValuePair<string, string>("data", query)]);
            using var response = await _httpClient.PostAsync(_endpoint, content, ct);
            if (!response.IsSuccessStatusCode)
                return new OsmTaxiwayFetchResult(null, $"OpenStreetMap (Overpass) request failed: HTTP {(int)response.StatusCode}. The server may be busy — try again in a minute.");
            var json = await response.Content.ReadAsStringAsync(ct);
            return new OsmTaxiwayFetchResult(Parse(json), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new OsmTaxiwayFetchResult(null, $"Could not reach OpenStreetMap (Overpass): {ex.Message}");
        }
        catch (JsonException)
        {
            return new OsmTaxiwayFetchResult(null, "OpenStreetMap (Overpass) returned an unreadable response. Try again in a minute.");
        }
    }

    // Parses an Overpass `out tags geom` JSON response. Ways without a usable
    // geometry (fewer than two points) are skipped.
    public static List<OsmTaxiway> Parse(string json)
    {
        var result = new List<OsmTaxiway>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("elements", out var elements)) return result;

        foreach (var element in elements.EnumerateArray())
        {
            if (!element.TryGetProperty("type", out var type) || type.GetString() != "way") continue;
            if (!element.TryGetProperty("geometry", out var geometry)) continue;

            var points = geometry.EnumerateArray()
                .Where(p => p.ValueKind == JsonValueKind.Object)
                .Select(p => (p.GetProperty("lat").GetDouble(), p.GetProperty("lon").GetDouble()))
                .ToList();
            if (points.Count < 2) continue;

            string? name = null;
            if (element.TryGetProperty("tags", out var tags))
                name = Tag(tags, "ref") ?? Tag(tags, "name");
            result.Add(new OsmTaxiway(name, points));
        }
        return result;
    }

    private static string? Tag(JsonElement tags, string key) =>
        tags.TryGetProperty(key, out var value) && value.GetString() is { } s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;
}
