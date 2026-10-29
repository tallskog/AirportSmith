using AirportSmith.Models;

namespace AirportSmith.Services;

public sealed record TaxiwaySignPlan(IReadOnlyList<TaxiwaySign> Signs, IReadOnlyList<string> Warnings);

// Generates taxiway signs from the taxi network and its taxi names — the
// Edit tab's "Auto-place Signs" (whole airport) and "Generate for Selected
// Points" (onlyPoints). Rules follow FAA AIM 2-3 (see requirements.md's
// reference list and its "author taxiway signs" section):
//   - Hold-short: at each hold-short point, on the left of every leg that
//     approaches the runway, the taxiway's location sign outboard of the
//     runway's mandatory sign, e.g. l[A]m[09-27] (reader's-left runway end
//     first); ILS hold-shorts get m[ILS].
//   - Intersection: before a junction of differently named taxiways, on the
//     left of each approach leg, a direction array — left turns, the
//     location sign, then straight/right turns, clockwise — e.g.
//     d[B<]l[A]d[C>].
//   - Location after a junction: l[B] a short way down each named branch.
//   - Runway exit: a direction sign on the exit's side of the runway, before
//     the exit, for each landing direction it can be taken from.
// Geometry is in the local-meters plane (X east, Z north, heading 0 = +Z),
// the same as TaxiPathSegment's resolved coordinates. Every generated sign
// is IsAutoPlaced with AnchorPointIndex set to the taxi point it belongs to.
// Heading/Justification follow requirements.md's UNCONFIRMED working
// assumption — see SignFacing, the one place to change if a Scenery Editor
// import shows otherwise.
public static class TaxiwaySignPlanner
{
    // Sign distance beyond the pavement edge. FAA AC 150/5340-18 places
    // signs a few meters to tens of feet off the edge; one value for all.
    private const double EdgeClearanceMeters = 6;

    // How far before a junction its direction array stands, and how far past
    // it a confirming location sign stands, measured along the taxiway.
    private const double IntersectionSetbackMeters = 30;
    private const double LocationSetbackMeters = 40;

    // A branch shorter than this before the next junction gets no location
    // sign — it'd sit right at the next junction's own signs.
    private const double MinLocationLegMeters = 20;

    // How far before an exit (along the runway) its exit sign stands.
    private const double ExitSetbackMeters = 60;

    // A hold-short point further than this from every runway isn't treated
    // as a runway hold.
    private const double MaxHoldShortRunwayDistanceMeters = 400;

    // Fallback when a path has no width stored.
    private const double DefaultTaxiwayWidthMeters = 20;

    private readonly record struct Vec(double X, double Z)
    {
        public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Z + b.Z);
        public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Z - b.Z);
        public static Vec operator *(Vec a, double k) => new(a.X * k, a.Z * k);
        public double Length => Math.Sqrt(X * X + Z * Z);
        public Vec Unit => Length > 0 ? this * (1 / Length) : new Vec(0, 1);
        // Rotated 90° counter-clockwise: the left-hand side of travel along this vector.
        public Vec Left => new(-Z, X);
        public static double Dot(Vec a, Vec b) => a.X * b.X + a.Z * b.Z;
        public static double Cross(Vec a, Vec b) => a.X * b.Z - a.Z * b.X;
    }

    // One drivable taxi path as a graph edge.
    private sealed record Leg(int Id, int A, int B, string? Name, double Width);

    private sealed record RunwayGeometry(Runway Runway, Vec Center, Vec Forward, double HalfLength, double HalfWidth)
    {
        public Vec Threshold1 => Center - Forward * HalfLength;
        public Vec Threshold2 => Center + Forward * HalfLength;

        public bool Contains(Vec p, double margin = 1)
        {
            var d = p - Center;
            return Math.Abs(Vec.Dot(d, Forward)) <= HalfLength + margin && Math.Abs(Vec.Dot(d, Forward.Left)) <= HalfWidth + margin;
        }

        public double DistanceTo(Vec p)
        {
            var d = p - Center;
            var along = Math.Clamp(Vec.Dot(d, Forward), -HalfLength, HalfLength);
            return (p - (Center + Forward * along)).Length;
        }
    }

    private sealed class Network
    {
        public required Dictionary<int, Vec> Points { get; init; }
        public required Dictionary<int, TaxiPointType?> PointTypes { get; init; }
        public required Dictionary<int, List<Leg>> LegsAt { get; init; }
        public required List<RunwayGeometry> Runways { get; init; }
        public required HashSet<int> RunwayPathPoints { get; init; }

        public int Other(Leg leg, int point) => leg.A == point ? leg.B : leg.A;

        // Unit direction leaving `from` along `leg`.
        public Vec Dir(Leg leg, int from) => (Points[Other(leg, from)] - Points[from]).Unit;

        public bool IsOnRunway(int point) =>
            RunwayPathPoints.Contains(point) || Runways.Any(r => r.Contains(Points[point]));
    }

    public static TaxiwaySignPlan Plan(AirportDetails airport, IReadOnlySet<int>? onlyPoints = null)
    {
        var warnings = new List<string>();
        var signs = new List<TaxiwaySign>();
        var network = BuildNetwork(airport);
        bool Wanted(int point) => onlyPoints is null || onlyPoints.Contains(point);

        foreach (var (point, type) in network.PointTypes)
        {
            if (!Wanted(point) || !IsHoldShort(type)) continue;
            PlanHoldShort(network, point, type!.Value, signs, warnings);
        }

        foreach (var point in network.LegsAt.Keys.Order())
        {
            if (!Wanted(point) || !IsJunction(network, point)) continue;
            PlanIntersection(network, point, signs, warnings);
            PlanLocationsAfterJunction(network, point, signs);
        }

        foreach (var point in network.LegsAt.Keys.Order())
        {
            if (!Wanted(point) || !network.IsOnRunway(point)) continue;
            PlanRunwayExits(network, point, signs);
        }

        return new TaxiwaySignPlan(signs, warnings);
    }

    // The new sign list after an auto-placement run: every manual (or
    // edited, which clears IsAutoPlaced) sign is kept; the Auto signs the run
    // covers — all of them, or only those anchored to onlyPoints — are
    // replaced by `generated`.
    public static List<TaxiwaySign> Merge(IEnumerable<TaxiwaySign> existing, IEnumerable<TaxiwaySign> generated, IReadOnlySet<int>? onlyPoints = null) =>
        existing.Where(s => !IsReplacedBy(s, onlyPoints)).Concat(generated).ToList();

    // How many existing signs a run would replace — the confirmation prompt's count.
    public static int CountReplaced(IEnumerable<TaxiwaySign> existing, IReadOnlySet<int>? onlyPoints = null) =>
        existing.Count(s => IsReplacedBy(s, onlyPoints));

    private static bool IsReplacedBy(TaxiwaySign sign, IReadOnlySet<int>? onlyPoints) =>
        sign.IsAutoPlaced && (onlyPoints is null || sign.AnchorPointIndex is { } anchor && onlyPoints.Contains(anchor));

    // A manual sign's starting label/heading (Edit tab "Add Sign"): a location
    // sign for the nearest named Taxi/Path path within maxDistanceMeters,
    // heading along it. Null when no named path is that close.
    public static (string Label, double HeadingDeg)? SuggestLocationSign(AirportDetails airport, double xMeters, double zMeters, double maxDistanceMeters = 60)
    {
        var names = airport.TaxiNames.ToDictionary(n => n.Id, n => TaxiwaySignLabel.SanitizeName(n.Value));
        var p = new Vec(xMeters, zMeters);
        (string Name, Vec Dir, double Distance)? best = null;

        foreach (var path in airport.TaxiPaths)
        {
            if (path.Type is not (TaxiPathType.Taxi or TaxiPathType.Path)) continue;
            if (path.TaxiNameId is not { } id || names.GetValueOrDefault(id) is not { } name) continue;
            if (path.StartXMeters is not { } sx || path.StartZMeters is not { } sz || path.EndXMeters is not { } ex || path.EndZMeters is not { } ez) continue;

            var a = new Vec(sx, sz);
            var ab = new Vec(ex, ez) - a;
            var t = ab.Length > 0 ? Math.Clamp(Vec.Dot(p - a, ab) / Vec.Dot(ab, ab), 0, 1) : 0;
            var distance = (p - (a + ab * t)).Length;
            if (distance <= maxDistanceMeters && (best is null || distance < best.Value.Distance))
                best = (name, ab.Unit, distance);
        }

        if (best is not { } b) return null;
        return ($"l[{b.Name}]", Math.Round(SignFacing(b.Dir, TaxiwaySignJustification.Left).HeadingDeg, 1));
    }

    // ---- Hold-short ---------------------------------------------------

    private static void PlanHoldShort(Network network, int point, TaxiPointType type, List<TaxiwaySign> signs, List<string> warnings)
    {
        if (!network.LegsAt.TryGetValue(point, out var legs)) return;
        var here = network.Points[point];
        var runway = network.Runways.MinBy(r => r.DistanceTo(here));
        if (runway is null || runway.DistanceTo(here) > MaxHoldShortRunwayDistanceMeters)
        {
            warnings.Add($"Hold-short point {point} isn't near any runway — no hold-short sign placed.");
            return;
        }

        var isIls = type is TaxiPointType.IlsHoldShort or TaxiPointType.IlsHoldShortNoDraw;
        foreach (var leg in legs)
        {
            // Only legs coming from further away than the hold itself
            // approach the runway; traffic leaving the runway gets no sign.
            var from = network.Points[network.Other(leg, point)];
            if (runway.DistanceTo(from) <= runway.DistanceTo(here)) continue;

            var travel = (here - from).Unit;
            var name = leg.Name ?? legs.Select(l => l.Name).FirstOrDefault(n => n != null);
            var mandatory = isIls ? "ILS" : RunwayDesignator(runway, here, travel);
            var label = (name != null ? $"l[{name}]" : "") + $"m[{mandatory}]";

            signs.Add(Sign(here + travel.Left * (leg.Width / 2 + EdgeClearanceMeters), travel, label, TaxiwaySignJustification.Left, point));
        }
    }

    // "09-27" with the runway end on the reader's left first, per FAA AIM
    // 2-3-8's holding position sign.
    private static string RunwayDesignator(RunwayGeometry runway, Vec here, Vec travel)
    {
        var primary = TaxiwaySignLabel.SanitizeName(runway.Runway.PrimaryDesignation) ?? "";
        var secondary = TaxiwaySignLabel.SanitizeName(runway.Runway.SecondaryDesignation);
        if (secondary is null) return primary;

        var primaryOnLeft = Vec.Dot(runway.Threshold1 - here, travel.Left) > 0;
        return primaryOnLeft ? $"{primary}-{secondary}" : $"{secondary}-{primary}";
    }

    // ---- Intersections ------------------------------------------------

    // A point where 3+ taxiway legs meet and at least two different taxi
    // names are involved. Holds and runway points have their own signs.
    private static bool IsJunction(Network network, int point) =>
        network.LegsAt.TryGetValue(point, out var legs) && legs.Count >= 3 &&
        legs.Select(l => l.Name).Where(n => n != null).Distinct().Count() >= 2 &&
        !IsHoldShort(network.PointTypes.GetValueOrDefault(point)) && !network.IsOnRunway(point);

    private static void PlanIntersection(Network network, int point, List<TaxiwaySign> signs, List<string> warnings)
    {
        var legs = network.LegsAt[point];
        foreach (var approach in legs)
        {
            var travel = network.Dir(approach, point) * -1;
            var branches = legs
                .Where(b => b != approach && b.Name != null && b.Name != approach.Name)
                .Select(b => (b.Name!, Turn: SignedAngleDeg(travel, network.Dir(b, point))))
                .Distinct()
                // Clockwise from the leftmost turn (largest left angle).
                .OrderByDescending(b => b.Turn)
                .ToList();
            if (branches.Count == 0) continue;

            var parts = branches.Select(b => (Text: DirectionSegment(b.Item1, b.Turn), IsLeft: b.Turn >= LeftTurnMinDeg)).ToList();
            var location = approach.Name != null ? $"l[{approach.Name}]" : null;

            string Build() =>
                string.Concat(parts.Where(p => p.IsLeft).Select(p => p.Text)) +
                location +
                string.Concat(parts.Where(p => !p.IsLeft).Select(p => p.Text));

            var label = Build();
            while (TaxiwaySignLabel.CountSymbols(label) > TaxiwaySignLabel.MaxSymbols && parts.Count > 1)
            {
                parts.RemoveAt(parts.Count - 1);
                label = Build();
            }
            if (TaxiwaySignLabel.CountSymbols(label) > TaxiwaySignLabel.MaxSymbols) continue;
            if (parts.Count < branches.Count)
                warnings.Add($"The direction sign before point {point} had too many taxiways for one sign; " +
                    $"{branches.Count - parts.Count} were left off.");

            var (position, at) = WalkAlong(network, point, approach, IntersectionSetbackMeters);
            var signTravel = at * -1;
            signs.Add(Sign(position + signTravel.Left * (approach.Width / 2 + EdgeClearanceMeters), signTravel, label, TaxiwaySignJustification.Left, point));
        }
    }

    private static void PlanLocationsAfterJunction(Network network, int point, List<TaxiwaySign> signs)
    {
        var legs = network.LegsAt[point];
        foreach (var branch in legs)
        {
            if (branch.Name is null || legs.All(l => l.Name == branch.Name || l.Name is null)) continue;

            var (position, at) = WalkAlong(network, point, branch, LocationSetbackMeters);
            if ((position - network.Points[point]).Length < MinLocationLegMeters) continue;
            signs.Add(Sign(position + at.Left * (branch.Width / 2 + EdgeClearanceMeters), at, $"l[{branch.Name}]", TaxiwaySignJustification.Left, point));
        }
    }

    // ---- Runway exits -------------------------------------------------

    private static void PlanRunwayExits(Network network, int point, List<TaxiwaySign> signs)
    {
        var here = network.Points[point];
        var runway = network.Runways.Where(r => r.Contains(here, margin: 5)).MinBy(r => r.DistanceTo(here));
        if (runway is null) return;

        foreach (var exit in network.LegsAt[point])
        {
            if (exit.Name is null || network.IsOnRunway(network.Other(exit, point))) continue;
            var exitDir = network.Dir(exit, point);

            // Each landing direction along the runway the exit can be taken from.
            foreach (var landing in new[] { runway.Forward, runway.Forward * -1 })
            {
                var turn = SignedAngleDeg(landing, exitDir);
                if (Math.Abs(turn) > 95) continue;

                var along = Vec.Dot(here - runway.Center, runway.Forward) - Vec.Dot(landing, runway.Forward) * ExitSetbackMeters;
                if (Math.Abs(along) > runway.HalfLength) continue;

                var isLeft = turn > 0;
                var side = isLeft ? landing.Left : landing.Left * -1;
                var position = runway.Center + runway.Forward * along + side * (runway.HalfWidth + EdgeClearanceMeters);
                var label = DirectionSegment(exit.Name, turn);
                signs.Add(Sign(position, landing, label, isLeft ? TaxiwaySignJustification.Left : TaxiwaySignJustification.Right, point));
            }
        }
    }

    // ---- Shared -------------------------------------------------------

    private const double LeftTurnMinDeg = 22.5;

    // d[B<], d[C>], d[D'] ... — the arrow on the side it points to (left
    // arrows before the name, right and straight-ahead arrows after it), as
    // the SDK page's own d[F\] example does. turnDeg > 0 is a left turn.
    private static string DirectionSegment(string name, double turnDeg)
    {
        var (arrow, before) = turnDeg switch
        {
            >= 112.5 => ('/', true),
            >= 67.5 => ('<', true),
            >= LeftTurnMinDeg => ('`', true),
            > -LeftTurnMinDeg => ('^', false),
            > -67.5 => ('\'', false),
            > -112.5 => ('>', false),
            _ => ('\\', false),
        };
        return before ? $"d[{arrow}{name}]" : $"d[{name}{arrow}]";
    }

    // Signed angle from a to b in degrees, positive counter-clockwise (a left turn).
    private static double SignedAngleDeg(Vec a, Vec b) =>
        Math.Atan2(Vec.Cross(a, b), Vec.Dot(a, b)) * 180 / Math.PI;

    // Walks `distance` meters from `start` along `leg`, continuing through
    // points where the taxiway just continues (exactly two legs) and stopping
    // early at the next junction/end. Returns where it stopped and the
    // direction of travel there (pointing away from `start`).
    private static (Vec Position, Vec Direction) WalkAlong(Network network, int start, Leg leg, double distance)
    {
        var from = start;
        var current = leg;
        var remaining = distance;
        for (var guard = 0; guard < 1000; guard++)
        {
            var to = network.Other(current, from);
            var a = network.Points[from];
            var b = network.Points[to];
            var length = (b - a).Length;
            var dir = (b - a).Unit;
            if (length >= remaining) return (a + dir * remaining, dir);

            remaining -= length;
            var next = network.LegsAt[to];
            if (next.Count != 2 || IsHoldShort(network.PointTypes.GetValueOrDefault(to))) return (b, dir);
            current = next[0] == current ? next[1] : next[0];
            from = to;
        }
        return (network.Points[network.Other(leg, start)], network.Dir(leg, start));
    }

    private static TaxiwaySign Sign(Vec position, Vec travel, string label, TaxiwaySignJustification side, int anchor)
    {
        var (heading, justification) = SignFacing(travel, side);
        return new TaxiwaySign
        {
            XMeters = Math.Round(position.X, 2),
            ZMeters = Math.Round(position.Z, 2),
            HeadingDeg = Math.Round(heading, 1),
            Label = label,
            Justification = justification,
            IsAutoPlaced = true,
            AnchorPointIndex = anchor,
        };
    }

    // The one place mapping "read by traffic travelling `travel`, standing on
    // `side` of it" to the <TaxiwaySign> heading/justification attributes.
    // Working assumption (requirements.md, UNCONFIRMED): heading is the
    // reader's direction of travel, justification the side.
    private static (double HeadingDeg, TaxiwaySignJustification Justification) SignFacing(Vec travel, TaxiwaySignJustification side) =>
        (NormalizeHeading(Math.Atan2(travel.X, travel.Z) * 180 / Math.PI), side);

    public static double NormalizeHeading(double deg) => ((deg % 360) + 360) % 360;

    private static bool IsHoldShort(TaxiPointType? type) => type is
        TaxiPointType.HoldShort or TaxiPointType.IlsHoldShort or TaxiPointType.HoldShortNoDraw or TaxiPointType.IlsHoldShortNoDraw;

    private static Network BuildNetwork(AirportDetails airport)
    {
        var names = airport.TaxiNames.ToDictionary(n => n.Id, n => TaxiwaySignLabel.SanitizeName(n.Value));
        var points = new Dictionary<int, Vec>();
        var pointTypes = new Dictionary<int, TaxiPointType?>();
        var legsAt = new Dictionary<int, List<Leg>>();
        var runwayPathPoints = new HashSet<int>();

        void AddPoint(int index, double? x, double? z, TaxiPointType? type)
        {
            if (x is null || z is null) return;
            points.TryAdd(index, new Vec(x.Value, z.Value));
            // First non-null type any path reports for this point wins.
            pointTypes[index] = pointTypes.GetValueOrDefault(index) ?? type;
        }

        for (var i = 0; i < airport.TaxiPaths.Count; i++)
        {
            var path = airport.TaxiPaths[i];
            if (path.Type == TaxiPathType.Parking) continue; // End is a parking spot, not a taxi point
            AddPoint(path.StartIndex, path.StartXMeters, path.StartZMeters, path.StartPointType);
            AddPoint(path.EndIndex, path.EndXMeters, path.EndZMeters, path.EndPointType);
            if (path.StartXMeters is null || path.StartZMeters is null || path.EndXMeters is null || path.EndZMeters is null) continue;
            if (path.StartIndex == path.EndIndex) continue;

            if (path.Type == TaxiPathType.Runway)
            {
                runwayPathPoints.Add(path.StartIndex);
                runwayPathPoints.Add(path.EndIndex);
                continue;
            }
            if (path.Type is not (TaxiPathType.Taxi or TaxiPathType.Path)) continue;

            var name = path.TaxiNameId is { } id ? names.GetValueOrDefault(id) : null;
            var leg = new Leg(i, path.StartIndex, path.EndIndex, name, path.WidthMeters > 0 ? path.WidthMeters : DefaultTaxiwayWidthMeters);
            foreach (var end in new[] { leg.A, leg.B })
            {
                if (!legsAt.TryGetValue(end, out var list)) legsAt[end] = list = [];
                list.Add(leg);
            }
        }

        var runways = airport.Runways.Select(r =>
        {
            var (cx, cz) = GeoProjection.ProjectLatLon(airport.Latitude, airport.Longitude, r.Latitude, r.Longitude);
            var rad = r.HeadingDeg * Math.PI / 180;
            return new RunwayGeometry(r, new Vec(cx, cz), new Vec(Math.Sin(rad), Math.Cos(rad)), r.LengthMeters / 2, r.WidthMeters / 2);
        }).ToList();

        return new Network
        {
            Points = points,
            PointTypes = pointTypes,
            LegsAt = legsAt,
            Runways = runways,
            RunwayPathPoints = runwayPathPoints,
        };
    }
}
