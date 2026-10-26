#nullable enable
using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MacDisplay.Converters;

public class NullVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool b = value != null;
        if (parameter != null)
        {
            b = !b; // Invert with parameter
        }
        return b;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}