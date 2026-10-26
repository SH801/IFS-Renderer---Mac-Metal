using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace MacDisplay.Views.Animation;

public class PositionToPointConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double x = 0;
        double y = 0;

        if (value is IConvertible convX)
        {
            x = System.Convert.ToDouble(convX, CultureInfo.InvariantCulture);
        }

        if (parameter is IConvertible convY)
        {
            y = System.Convert.ToDouble(convY, CultureInfo.InvariantCulture);
        }

        return new Point(x, y);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}