using AirportSmith.Services;

namespace AirportSmith.Tests.Services;

public class TaxiwaySectionFinderTests
{
    // A circular taxiway as 8 edges: point 0 = west, 2 = north, 4 = east,
    // 6 = south. Edge i runs from point i to point i+1, so edges 0-3 are the
    // north half and 4-7 the south half.
    private static List<TaxiwaySectionEdge> Circle() =>
        Enumerable.Range(0, 8).Select(i => new TaxiwaySectionEdge(i, i, (i + 1) % 8)).ToList();

    [Fact]
    public void FewerThanTwoPoints_SelectsNothing()
    {
        Assert.Empty(TaxiwaySectionFinder.FindSection(Circle(), []));
        Assert.Empty(TaxiwaySectionFinder.FindSection(Circle(), [0]));
    }

    // Both halves are exactly as short (4 points each), so both are
    // selected...
    [Fact]
    public void Circle_OppositePoints_SelectsBothEquallyShortHalves()
    {
        var section = TaxiwaySectionFinder.FindSection(Circle(), [0, 4]);

        Assert.Equal(Enumerable.Range(0, 8).ToHashSet(), section);
    }

    // ...and a north point narrows it to the north half only.
    [Fact]
    public void Circle_WaypointOnOneHalf_SelectsOnlyThatHalf()
    {
        var section = TaxiwaySectionFinder.FindSection(Circle(), [0, 4, 2]);

        Assert.Equal(new HashSet<int> { 0, 1, 2, 3 }, section);
    }

    // Not equally short (5 vs. 3 points) — only the shorter half.
    [Fact]
    public void Circle_NonOppositePoints_SelectsOnlyTheShorterRoute()
    {
        var section = TaxiwaySectionFinder.FindSection(Circle(), [0, 3]);

        Assert.Equal(new HashSet<int> { 0, 1, 2 }, section);
    }

    [Fact]
    public void LongerRoute_IsNotSelected_UnlessAWaypointForcesIt()
    {
        // Direct 0-1 (one hop) vs. 0-2-1 (two hops).
        List<TaxiwaySectionEdge> edges = [new(0, 0, 1), new(1, 0, 2), new(2, 2, 1)];

        Assert.Equal(new HashSet<int> { 0 }, TaxiwaySectionFinder.FindSection(edges, [0, 1]));
        Assert.Equal(new HashSet<int> { 1, 2 }, TaxiwaySectionFinder.FindSection(edges, [0, 1, 2]));
    }

    [Fact]
    public void DeadEndStubOffTheRoute_IsNotSelected()
    {
        List<TaxiwaySectionEdge> edges = [new(0, 0, 1), new(1, 1, 2), new(2, 1, 9)];

        Assert.Equal(new HashSet<int> { 0, 1 }, TaxiwaySectionFinder.FindSection(edges, [0, 2]));
    }

    [Fact]
    public void DuplicatePathsBetweenTheSamePoints_AreAllSelected()
    {
        List<TaxiwaySectionEdge> edges = [new(0, 0, 1), new(1, 1, 0)];

        Assert.Equal(new HashSet<int> { 0, 1 }, TaxiwaySectionFinder.FindSection(edges, [0, 1]));
    }

    [Fact]
    public void DisconnectedPoints_SelectNothing()
    {
        List<TaxiwaySectionEdge> edges = [new(0, 0, 1), new(1, 2, 3)];

        Assert.Empty(TaxiwaySectionFinder.FindSection(edges, [0, 3]));
        Assert.Empty(TaxiwaySectionFinder.FindSection(edges, [0, 42]));
    }

    // Points clicked in order along a straight taxiway: the third lies
    // beyond the second, so the first two can't be the ends — the ends fall
    // back to the outermost pair instead of selecting nothing.
    [Fact]
    public void PointsClickedInOrderAlongALine_FallBackToOutermostEnds()
    {
        List<TaxiwaySectionEdge> edges = [new(0, 0, 1), new(1, 1, 2), new(2, 2, 3)];

        Assert.Equal(new HashSet<int> { 0, 1 }, TaxiwaySectionFinder.FindSection(edges, [0, 1, 2]));
        Assert.Equal(new HashSet<int> { 0, 1, 2 }, TaxiwaySectionFinder.FindSection(edges, [0, 1, 2, 3]));
    }

    // The fallback only kicks in when the first two can't be the ends — on
    // the circle, west + east + south still keeps west/east as the ends.
    [Fact]
    public void Circle_FirstTwoPointsStayTheEnds_WhenARouteExists()
    {
        Assert.Equal(new HashSet<int> { 4, 5, 6, 7 }, TaxiwaySectionFinder.FindSection(Circle(), [0, 4, 6]));
    }

    // More points than the exact visiting-order search handles: the greedy
    // order still gives the whole line.
    [Fact]
    public void ManyPointsAlongALine_SelectsTheWholeLine()
    {
        var edges = Enumerable.Range(0, 20).Select(i => new TaxiwaySectionEdge(i, i, i + 1)).ToList();
        int[] points = [0, 20, .. Enumerable.Range(1, 14)];

        Assert.Equal(Enumerable.Range(0, 20).ToHashSet(), TaxiwaySectionFinder.FindSection(edges, points));
    }
}
