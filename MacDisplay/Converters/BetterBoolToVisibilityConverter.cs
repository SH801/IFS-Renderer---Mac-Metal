#nullable enable
using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MacDisplay.Converters;

public class BetterBooleanToVisibilityConverter : IValueConverter
{
    // Accepts strings like "Visible", "Collapsed", or booleans from XAML
    public object? True { get; set; } = true;
    public object? False { get; set; } = false;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool boolVal = value is bool b && b;
        object? targetObj = boolVal ? True : False;

        return ConvertToBool(targetObj);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool boolVal = value is bool b && b;
        return boolVal;
    }

    private static bool ConvertToBool(object? obj)
    {
        if (obj is bool b) return b;
        if (obj is string s)
        {
            if (s.Equals("Visible", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.Equals("Collapsed", StringComparison.OrdinalIgnoreCase)) return false;
            if (s.Equals("Hidden", StringComparison.OrdinalIgnoreCase)) return false;
            if (bool.TryParse(s, out var parsed)) return parsed;
        }
        return obj != null;
    }
}