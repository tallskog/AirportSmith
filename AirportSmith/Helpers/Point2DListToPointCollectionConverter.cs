using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AirportSmith.Models.Diagram;

namespace AirportSmith.Helpers;

public class Point2DListToPointCollectionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not IReadOnlyList<Point2D> points)
            return new PointCollection();

        return new PointCollection(points.Select(p => new Point(p.X, p.Y)));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
