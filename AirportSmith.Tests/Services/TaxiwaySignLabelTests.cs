using AirportSmith.Models;
using AirportSmith.Services;

namespace AirportSmith.Tests.Services;

public class TaxiwaySignLabelTests
{
    // The examples on the SDK's Scenery Editor "TaxiwaySign Objects" page.
    [Theory]
    [InlineData("l[G]")]
    [InlineData("l[GE]")]
    [InlineData("l[G]d[F\\]m[11R-29L]")]
    public void Parse_SdkExamples_AreValid(string label)
    {
        Assert.True(TaxiwaySignLabel.Parse(label).IsValid);
    }

    [Fact]
    public void Parse_MultiSegmentLabel_SplitsIntoKindsAndContent()
    {
        var result = TaxiwaySignLabel.Parse("l[G]d[F\\]m[11R-29L]");

        Assert.Equal(
            [
                new TaxiwaySignSegment(TaxiwaySignSegmentKind.Location, "[G]"),
                new TaxiwaySignSegment(TaxiwaySignSegmentKind.Direction, "[F\\]"),
                new TaxiwaySignSegment(TaxiwaySignSegmentKind.Mandatory, "[11R-29L]"),
            ],
            result.Segments);
    }

    [Fact]
    public void Parse_LowercaseVIsTheDownArrowNotASignType()
    {
        var segment = Assert.Single(TaxiwaySignLabel.Parse("d[Av]").Segments);
        Assert.Equal("[Av]", segment.Content);
        Assert.Equal("A↓", segment.DisplayText);
    }

    [Fact]
    public void Parse_EveryTypeLetter_IsRecognized()
    {
        var kinds = TaxiwaySignLabel.Parse("lAdBmCiDrEuF").Segments.Select(s => s.Kind);
        Assert.Equal(
            [
                TaxiwaySignSegmentKind.Location, TaxiwaySignSegmentKind.Direction, TaxiwaySignSegmentKind.Mandatory,
                TaxiwaySignSegmentKind.Information, TaxiwaySignSegmentKind.Runway, TaxiwaySignSegmentKind.Unknown,
            ],
            kinds);
    }

    [Fact]
    public void Parse_SeventeenSymbols_IsValid_EighteenIsNot()
    {
        Assert.True(TaxiwaySignLabel.Parse("l" + new string('A', 17)).IsValid);
        // Type letters don't count toward the limit.
        Assert.True(TaxiwaySignLabel.Parse("l" + new string('A', 8) + "d" + new string('B', 9)).IsValid);
        Assert.Contains("17", TaxiwaySignLabel.Parse("l" + new string('A', 18)).Error);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData(null, "empty")]
    [InlineData("[A]", "must start with a sign type")]
    [InlineData("l[a]", "'a'")]
    [InlineData("l[A]x", "'x'")]
    [InlineData("l[A]d", "no text")]
    [InlineData("ldA", "no text")]
    [InlineData("l[Ä]", "'Ä'")]
    public void Parse_InvalidLabels_ReportWhy(string? label, string expectedErrorPart)
    {
        var error = TaxiwaySignLabel.Validate(label);
        Assert.NotNull(error);
        Assert.Contains(expectedErrorPart, error);
    }

    [Fact]
    public void DisplayText_DropsBracketsAndDrawsArrows()
    {
        var segment = new TaxiwaySignSegment(TaxiwaySignSegmentKind.Direction, "[<B_C>^'`/\\]");
        Assert.Equal("←B C→↑↗↖↙↘", segment.DisplayText);
    }

    [Theory]
    [InlineData("a", "A")]
    [InlineData(" B2 ", "B2")]
    [InlineData("Alpha 1", "ALPHA_1")]
    [InlineData("A-1", "A-1")]
    [InlineData("Ä1", "1")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("ÄÖ", null)]
    public void SanitizeName_MakesSignContent(string name, string? expected)
    {
        Assert.Equal(expected, TaxiwaySignLabel.SanitizeName(name));
    }

    [Theory]
    [InlineData("l[A]", "A")]
    [InlineData("d[B<]l[A]d[C>]", "A,B,C")]
    [InlineData("l[A]m[09-27]", "A")]          // runway designator in the mandatory part isn't a taxi name
    [InlineData("m[ILS]", "")]
    [InlineData("r[09]", "")]
    [InlineData("d[A1^]|[B_2v]", "A1,B_2")]    // arrows/borders split names; '_' is a space in a name
    [InlineData("i[TWY_K']", "TWY_K")]
    [InlineData("l[A]d", "A")]                 // half-typed (invalid) label: as far as it parses
    [InlineData("", "")]
    public void TaxiNamesShown_ListsTheTaxiwayNamesOnASign(string label, string expected)
    {
        var names = TaxiwaySignLabel.TaxiNamesShown(label).Order(StringComparer.Ordinal);

        Assert.Equal(expected, string.Join(",", names));
    }
}
