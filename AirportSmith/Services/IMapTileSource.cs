namespace AirportSmith.Services;

// A tile successfully fetched from a remote source, along with how long the
// server says it can be cached for (from the response's Cache-Control:
// max-age, if any) - MaxAge is null when the server sent no usable header.
public record MapTileFetchResult(byte[] Bytes, TimeSpan? MaxAge);

// Raw HTTP fetch of a single map tile image. Implementations never throw for
// an ordinary failure (network error, timeout, non-2xx response) - they
// return null so a caller treats a missing tile as "don't draw it," never a
// crash or a blocking error (the diagram must keep rendering normally with
// the map layer simply incomplete).
public interface IMapTileSource
{
    Task<MapTileFetchResult?> FetchTileAsync(int x, int y, int zoom, CancellationToken ct);
}
