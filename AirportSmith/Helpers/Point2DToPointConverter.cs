using System.Globalization;
using System.Windows.Data;
using AirportSmith.Models.Diagram;

namespace AirportSmith.Helpers;

public class Point2DToPointConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Point2D p ? new System.Windows.Point(p.X, p.Y) : new System.Windows.Point();

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
