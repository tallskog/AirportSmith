using AirportSmith.Models;

namespace AirportSmith.Services;

// A suggested name for the taxi path at AirportDetails.TaxiPaths[PathIndex].
public record TaxiwayNameSuggestion(int PathIndex, string Name);

// Matches an airport's taxi paths to OSM taxiway ways (see
// OverpassTaxiwaySource) and suggests the way's name for each path that runs
// along one. Pure — no I/O — so it's unit tested with fake ways.
//
// A path matches the NEAREST OSM way that runs alongside it: close on
// average and at worst (sampled along the path) and in roughly the same
// direction. The direction check keeps a crossing taxiway, which passes
// right by a path's end point, from matching. If that nearest way is
// unnamed, there's no suggestion — an unnamed way right next to the path
// must not let a farther named one win.
public static class TaxiwayNameSuggester
{
    public const double MaxMeanDistanceMeters = 20;
    public const double MaxDistanceMeters = 30;
    public const double MaxAngleDegrees = 30;

    // Samples along a path: both ends plus one about every 10 m in between.
    private const double SampleSpacingMeters = 10;

    // Paths shorter than this have too little direction to check reliably.
    private const double MinLengthForAngleCheckMeters = 5;

    public static List<TaxiwayNameSuggestion> Suggest(
        AirportDetails airport, IReadOnlyList<OsmTaxiway> taxiways, Func<Guid?, string> resolveName)
    {
        var ways = taxiways
            .Select(t => (t.Name, Points: t.Points
                .Select(p => GeoProjection.ProjectLatLon(airport.Latitude, airport.Longitude, p.Lat, p.Lon))
                .ToArray()))
            .ToList();

        var suggestions = new List<TaxiwayNameSuggestion>();
        for (var i = 0; i < airport.TaxiPaths.Count; i++)
        {
            var path = airport.TaxiPaths[i];
            if (path.Type is not (TaxiPathType.Taxi or TaxiPathType.Path)) continue;
            if (path.StartXMeters is not { } sx || path.StartZMeters is not { } sz ||
                path.EndXMeters is not { } ex || path.EndZMeters is not { } ez) continue;

            string? bestName = null;
            var bestMean = double.PositiveInfinity;
            foreach (var (name, points) in ways)
            {
                if (MeanDistanceIfAlongside((sx, sz), (ex, ez), points) is { } mean && mean < bestMean)
                    (bestMean, bestName) = (mean, name);
            }

            if (bestName is null) continue;
            if (string.Equals(resolveName(path.TaxiNameId), bestName, StringComparison.OrdinalIgnoreCase)) continue;
            suggestions.Add(new TaxiwayNameSuggestion(i, bestName));
        }
        return suggestions;
    }

    // The path's mean distance to the way, or null if the way doesn't run
    // alongside it (too far anywhere, or at too steep an angle).
    private static double? MeanDistanceIfAlongside((double X, double Z) start, (double X, double Z) end, (double X, double Z)[] way)
    {
        var dx = end.X - start.X;
        var dz = end.Z - start.Z;
        var length = Math.Sqrt(dx * dx + dz * dz);
        var samples = Math.Max(2, (int)Math.Ceiling(length / SampleSpacingMeters) + 1);

        var total = 0.0;
        for (var s = 0; s < samples; s++)
        {
            var t = (double)s / (samples - 1);
            var (distance, _) = NearestSegment((start.X + dx * t, start.Z + dz * t), way);
            if (distance > MaxDistanceMeters) return null;
            total += distance;
        }
        var mean = total / samples;
        if (mean > MaxMeanDistanceMeters) return null;

        if (length >= MinLengthForAngleCheckMeters)
        {
            var (_, segment) = NearestSegment((start.X + dx / 2, start.Z + dz / 2), way);
            var wx = way[segment + 1].X - way[segment].X;
            var wz = way[segment + 1].Z - way[segment].Z;
            var wayLength = Math.Sqrt(wx * wx + wz * wz);
            if (wayLength > 0)
            {
                // Undirected: a way drawn in the opposite direction is still alongside.
                var cos = Math.Abs(dx * wx + dz * wz) / (length * wayLength);
                if (cos < Math.Cos(MaxAngleDegrees * Math.PI / 180)) return null;
            }
        }
        return mean;
    }

    private static (double Distance, int Segment) NearestSegment((double X, double Z) p, (double X, double Z)[] way)
    {
        var best = (Distance: double.PositiveInfinity, Segment: 0);
        for (var i = 0; i < way.Length - 1; i++)
        {
            var d = DistanceToSegment(p, way[i], way[i + 1]);
            if (d < best.Distance) best = (d, i);
        }
        return best;
    }

    private static double DistanceToSegment((double X, double Z) p, (double X, double Z) a, (double X, double Z) b)
    {
        var abx = b.X - a.X;
        var abz = b.Z - a.Z;
        var lengthSquared = abx * abx + abz * abz;
        var t = lengthSquared == 0 ? 0 : Math.Clamp(((p.X - a.X) * abx + (p.Z - a.Z) * abz) / lengthSquared, 0, 1);
        var cx = a.X + abx * t - p.X;
        var cz = a.Z + abz * t - p.Z;
        return Math.Sqrt(cx * cx + cz * cz);
    }
}
