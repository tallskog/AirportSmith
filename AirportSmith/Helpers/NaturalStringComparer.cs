namespace AirportSmith.Helpers;

// Case-insensitive alphabetical order that compares runs of digits by their
// numeric value, so taxiway names sort the way a chart lists them — "A2"
// before "A10", not after it as a plain string comparison would put it.
// Used for the Taxi Names list (MainViewModel.SortTaxiNames).
public sealed class NaturalStringComparer : IComparer<string?>
{
    public static NaturalStringComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        x ??= string.Empty;
        y ??= string.Empty;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                var xStart = i;
                var yStart = j;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                while (j < y.Length && char.IsDigit(y[j])) j++;

                // Compare by value without parsing (no overflow on long runs):
                // ignore leading zeros, then the longer run is the bigger number,
                // then digit by digit.
                var xDigits = x.AsSpan(xStart, i - xStart).TrimStart('0');
                var yDigits = y.AsSpan(yStart, j - yStart).TrimStart('0');
                if (xDigits.Length != yDigits.Length) return xDigits.Length.CompareTo(yDigits.Length);
                var byValue = xDigits.SequenceCompareTo(yDigits);
                if (byValue != 0) return byValue;
                continue;
            }

            var byChar = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
            if (byChar != 0) return byChar;
            i++;
            j++;
        }

        var byLength = (x.Length - i).CompareTo(y.Length - j);
        // Equal ignoring case (e.g. "a" and "A"): fall back to an ordinal
        // comparison so the order is still deterministic.
        return byLength != 0 ? byLength : string.CompareOrdinal(x, y);
    }
}
