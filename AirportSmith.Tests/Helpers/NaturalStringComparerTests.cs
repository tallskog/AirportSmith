using AirportSmith.Helpers;

namespace AirportSmith.Tests.Helpers;

public class NaturalStringComparerTests
{
    [Fact]
    public void Sorts_NumbersByValue_LettersCaseInsensitively_AndBlankFirst()
    {
        string[] names = ["B", "a10", "A2", "", "A", "C1", "A1", "b2"];

        var sorted = names.Order(NaturalStringComparer.Instance);

        Assert.Equal(["", "A", "A1", "A2", "a10", "B", "b2", "C1"], sorted);
    }

    [Theory]
    [InlineData("A02", "A3", -1)]
    [InlineData("A99999999999999999999", "A100000000000000000000", -1)] // longer than a long
    [InlineData("A1B", "A1A", 1)]
    [InlineData(null, "A", -1)]
    public void Compare_HandlesLeadingZerosLongRunsAndNull(string? x, string? y, int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(NaturalStringComparer.Instance.Compare(x, y)));
        Assert.Equal(-expectedSign, Math.Sign(NaturalStringComparer.Instance.Compare(y, x)));
    }

    [Fact]
    public void Compare_NamesDifferingOnlyInCase_IsStillDeterministic()
    {
        Assert.NotEqual(0, NaturalStringComparer.Instance.Compare("a", "A"));
        Assert.Equal(0, NaturalStringComparer.Instance.Compare("A1", "A1"));
    }
}
