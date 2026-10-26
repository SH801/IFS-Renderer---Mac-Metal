#nullable enable
using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MacDisplay.Converters;

internal class ParameterEqualityToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => 
        parameter?.Equals(value) ?? value is null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b)
        {
            return parameter;
        }
        return null;
    }
}