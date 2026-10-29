using System.Text;
using AirportSmith.Models;

namespace AirportSmith.Services;

public sealed record TaxiwaySignLabelParseResult(IReadOnlyList<TaxiwaySignSegment> Segments, string? Error)
{
    public bool IsValid => Error is null;
}

// Parser/validator for a <TaxiwaySign label="...">, per the SDK's Scenery
// Editor "TaxiwaySign Objects" page: one or more segments, each a lower-case
// sign-type letter (l d m i r u) followed by content characters — A-Z, 0-9,
// '_' or space, '-', the arrows > < ^ v ' ` / \, the borders [ ], and '|'.
// At most 17 content symbols per sign (the type letters don't count). The sim
// silently doesn't render a malformed label, so the grid shows Error inline
// and the exporter skips (and warns about) an invalid one rather than
// writing a sign that would just be missing in the sim.
public static class TaxiwaySignLabel
{
    public const int MaxSymbols = 17;

    private const string SpecialContentChars = "_ ->< ^v'`/\\[]|";

    public static TaxiwaySignLabelParseResult Parse(string? label)
    {
        if (string.IsNullOrEmpty(label))
            return new([], "Label is empty.");

        var segments = new List<TaxiwaySignSegment>();
        TaxiwaySignSegmentKind? kind = null;
        var content = new StringBuilder();
        var symbols = 0;

        void Close()
        {
            if (kind is { } k) segments.Add(new TaxiwaySignSegment(k, content.ToString()));
            content.Clear();
        }

        foreach (var c in label)
        {
            if (KindOf(c) is { } nextKind)
            {
                if (kind != null && content.Length == 0)
                    return new(segments, $"Sign type '{TypeLetter(kind.Value)}' has no text after it.");
                Close();
                kind = nextKind;
                continue;
            }

            if (!IsContentChar(c))
                return new(segments, $"'{c}' can't be shown on a taxiway sign.");
            if (kind is null)
                return new(segments, "A label must start with a sign type letter (l, d, m, i, r or u).");

            content.Append(c);
            symbols++;
        }

        if (kind != null && content.Length == 0)
            return new(segments, $"Sign type '{TypeLetter(kind.Value)}' has no text after it.");
        Close();

        if (symbols > MaxSymbols)
            return new(segments, $"{symbols} symbols; a sign holds at most {MaxSymbols}.");

        return new(segments, null);
    }

    public static string? Validate(string? label) => Parse(label).Error;

    // Content symbol count, the unit of MaxSymbols (type letters excluded).
    public static int CountSymbols(string label) => label.Count(c => KindOf(c) is null);

    // A taxi name as sign content: upper-cased, spaces as '_', anything else
    // the grammar can't draw dropped. Null when nothing drawable is left
    // (e.g. a blank "(no name)" entry), meaning "don't sign this taxiway".
    public static string? SanitizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var sb = new StringBuilder();
        foreach (var c in name.Trim().ToUpperInvariant())
        {
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-') sb.Append(c);
            else if (c == ' ') sb.Append('_');
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    public static char TypeLetter(TaxiwaySignSegmentKind kind) => kind switch
    {
        TaxiwaySignSegmentKind.Location => 'l',
        TaxiwaySignSegmentKind.Direction => 'd',
        TaxiwaySignSegmentKind.Mandatory => 'm',
        TaxiwaySignSegmentKind.Information => 'i',
        TaxiwaySignSegmentKind.Runway => 'r',
        _ => 'u',
    };

    private static TaxiwaySignSegmentKind? KindOf(char c) => c switch
    {
        'l' => TaxiwaySignSegmentKind.Location,
        'd' => TaxiwaySignSegmentKind.Direction,
        'm' => TaxiwaySignSegmentKind.Mandatory,
        'i' => TaxiwaySignSegmentKind.Information,
        'r' => TaxiwaySignSegmentKind.Runway,
        'u' => TaxiwaySignSegmentKind.Unknown,
        _ => null,
    };

    private static bool IsContentChar(char c) =>
        c is >= 'A' and <= 'Z' or >= '0' and <= '9' || SpecialContentChars.Contains(c);
}
