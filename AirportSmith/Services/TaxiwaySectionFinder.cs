namespace AirportSmith.Services;

// One taxi path as an edge of the taxiway graph: Id is the caller's own
// identifier for it (MainViewModel uses the TaxiPathSegment's list index),
// A/B are the sim TAXI_POINT indices at either end.
public readonly record struct TaxiwaySectionEdge(int Id, int A, int B);

// Finds the taxi paths making up the "section" between a set of selected
// taxiway points (Edit tab: selecting 2+ points auto-selects the paths
// between them). Rules, as agreed with the user:
//   - points[0] and points[1] (the first two points selected) are the
//     section's ends; every later point is a waypoint the route must pass
//     through, in whichever order gives the shortest route. If no route
//     fits that, the ends are whichever two selected points give the
//     shortest route through all the others.
//   - The route is the SHORTEST one, measured in taxiway points (hops), not
//     meters. Only when several routes are exactly as short are all of them
//     selected — e.g. two opposite points on a circular taxiway select both
//     halves, and a third point on one half narrows it to that half.
//   - Between two consecutive points, the route never passes through
//     another selected point.
// The caller decides which paths are edges (runway and parking paths are
// left out — a runway isn't a section, and a parking path's end is a
// parking spot, not a taxi point).
//
// Everything here is breadth-first searches plus a Held-Karp style DP over
// the visiting order of the selected points, so it's fast even on a large
// airport: no enumeration of routes.
public static class TaxiwaySectionFinder
{
    // Above this many selected points the exact visiting-order DP (2^n * n^2)
    // gets too slow for a click handler; a greedy nearest-next order is used
    // instead. Real section selections are a handful of points.
    private const int MaxPointsForExactOrder = 12;

    private const int Unreachable = int.MaxValue / 4;

    public static HashSet<int> FindSection(IReadOnlyList<TaxiwaySectionEdge> edges, IReadOnlyList<int> points)
    {
        var result = new HashSet<int>();
        var distinctPoints = points.Distinct().ToList();
        if (distinctPoints.Count < 2) return result;

        // Compact node numbering, so the searches can use plain arrays.
        var nodeOf = new Dictionary<int, int>();
        int Node(int pointIndex)
        {
            if (!nodeOf.TryGetValue(pointIndex, out var n))
                nodeOf[pointIndex] = n = nodeOf.Count;
            return n;
        }
        var edgeEnds = edges.Select(e => (A: Node(e.A), B: Node(e.B))).ToArray();
        if (distinctPoints.Any(p => !nodeOf.ContainsKey(p))) return result; // a point with no usable paths

        var adjacency = new List<int>[nodeOf.Count];
        for (var i = 0; i < adjacency.Length; i++) adjacency[i] = [];
        for (var i = 0; i < edgeEnds.Length; i++)
        {
            var (a, b) = edgeEnds[i];
            if (a == b) continue;
            adjacency[a].Add(b);
            adjacency[b].Add(a);
        }

        var selected = distinctPoints.Select(p => nodeOf[p]).ToArray();
        var isSelected = new bool[adjacency.Length];
        foreach (var s in selected) isSelected[s] = true;

        // Hop distances from each selected point, never passing THROUGH
        // another selected point (it can be reached, just not crossed).
        var dist = selected.Select(s => Bfs(s, adjacency, isSelected)).ToArray();
        var n = selected.Length;
        var leg = new int[n, n];
        for (var i = 0; i < n; i++)
        for (var j = 0; j < n; j++)
            leg[i, j] = dist[i][selected[j]];

        // Which consecutive pairs (legs) appear in some optimal visiting order.
        var legs = n <= MaxPointsForExactOrder
            ? OptimalLegs(leg, n, 0, 1) ?? OptimalLegs(leg, n, null, null)
            : GreedyLegs(leg, n);
        if (legs is null) return result;

        // Every edge on some shortest route of each leg.
        foreach (var (i, j) in legs)
        {
            var total = leg[i, j];
            for (var e = 0; e < edgeEnds.Length; e++)
            {
                var (a, b) = edgeEnds[e];
                if (a == b) continue;
                // Only the leg's own ends may be selected points.
                if ((isSelected[a] && a != selected[i] && a != selected[j]) ||
                    (isSelected[b] && b != selected[i] && b != selected[j])) continue;
                if (dist[i][a] + 1 + dist[j][b] == total || dist[i][b] + 1 + dist[j][a] == total)
                    result.Add(edges[e].Id);
            }
        }
        return result;
    }

    private static int[] Bfs(int source, List<int>[] adjacency, bool[] isSelected)
    {
        var dist = Enumerable.Repeat(Unreachable, adjacency.Length).ToArray();
        dist[source] = 0;
        var queue = new Queue<int>();
        queue.Enqueue(source);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node != source && isSelected[node]) continue; // reached, not crossed
            foreach (var to in adjacency[node])
            {
                if (dist[to] != Unreachable) continue;
                dist[to] = dist[node] + 1;
                queue.Enqueue(to);
            }
        }
        return dist;
    }

    // Held-Karp over visiting orders of all n points, starting at `start` and
    // ending at `end` (either null = free). Returns every leg (i, j) used by
    // at least one minimum-total order, or null if no order connects them.
    private static HashSet<(int, int)>? OptimalLegs(int[,] leg, int n, int? start, int? end)
    {
        var full = (1 << n) - 1;
        var cost = new int[1 << n, n];
        for (var m = 0; m <= full; m++)
        for (var i = 0; i < n; i++)
            cost[m, i] = Unreachable;
        for (var i = 0; i < n; i++)
            if (start is null || start == i)
                cost[1 << i, i] = 0;

        for (var m = 1; m <= full; m++)
        for (var last = 0; last < n; last++)
        {
            var c = cost[m, last];
            if (c >= Unreachable) continue;
            if (end == last && m != full) continue; // the end must come last
            for (var next = 0; next < n; next++)
            {
                if ((m & (1 << next)) != 0 || leg[last, next] >= Unreachable) continue;
                var nm = m | (1 << next);
                var nc = c + leg[last, next];
                if (nc < cost[nm, next]) cost[nm, next] = nc;
            }
        }

        var best = Unreachable;
        for (var i = 0; i < n; i++)
            if (end is null || end == i)
                best = Math.Min(best, cost[full, i]);
        if (best >= Unreachable) return null;

        // Walk back from every optimal final state through every optimal
        // predecessor, collecting the legs used.
        var legs = new HashSet<(int, int)>();
        var seen = new HashSet<(int, int)>();
        var stack = new Stack<(int Mask, int Last)>();
        for (var i = 0; i < n; i++)
            if ((end is null || end == i) && cost[full, i] == best)
                stack.Push((full, i));
        while (stack.Count > 0)
        {
            var (m, last) = stack.Pop();
            if (!seen.Add((m, last))) continue;
            var pm = m & ~(1 << last);
            if (pm == 0) continue;
            for (var prev = 0; prev < n; prev++)
            {
                if ((pm & (1 << prev)) == 0 || cost[pm, prev] >= Unreachable) continue;
                if (end == prev) continue;
                if (cost[pm, prev] + leg[prev, last] != cost[m, last]) continue;
                legs.Add((prev, last));
                stack.Push((pm, prev));
            }
        }
        return legs;
    }

    // Fallback for many points: from the first end, always go to the nearest
    // unvisited point, finishing at the second end.
    private static HashSet<(int, int)>? GreedyLegs(int[,] leg, int n)
    {
        var legs = new HashSet<(int, int)>();
        var visited = new bool[n];
        visited[0] = visited[1] = true;
        var current = 0;
        for (var step = 0; step < n - 2; step++)
        {
            var next = -1;
            for (var j = 2; j < n; j++)
                if (!visited[j] && leg[current, j] < Unreachable && (next < 0 || leg[current, j] < leg[current, next]))
                    next = j;
            if (next < 0) return null;
            legs.Add((current, next));
            visited[next] = true;
            current = next;
        }
        if (leg[current, 1] >= Unreachable) return null;
        legs.Add((current, 1));
        return legs;
    }
}
