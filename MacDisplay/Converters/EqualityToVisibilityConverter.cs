#nullable enable
using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MacDisplay.Converters;

internal class EqualityToVisibilityConverter : IValueConverter
{
    public virtual object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => 
        Equals(value, parameter);

    public virtual object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => 
        throw new NotImplementedException();
}