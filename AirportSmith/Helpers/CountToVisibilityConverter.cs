using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AirportSmith.Helpers;

// Visible when bound to a positive int (e.g. a list's .Count via a Binding
// Path), Collapsed for 0 or anything else — used to show/hide the XML
// export warnings block only when there are warnings.
public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
