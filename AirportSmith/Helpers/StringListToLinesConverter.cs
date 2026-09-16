using System.Globalization;
using System.Windows.Data;

namespace AirportSmith.Helpers;

// Joins a warnings-style IReadOnlyList<string> (e.g. MainViewModel
// .LastXmlExportWarnings) into one newline-separated string for display in a
// single TextBlock.
public class StringListToLinesConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is IEnumerable<string> lines ? string.Join(Environment.NewLine, lines) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
