#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MacDisplay.Converters;

public class ConnectionGeometryConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 3 && 
            values[0] is PathGeometry g1 && 
            values[1] is EllipseGeometry g2 && 
            values[2] is bool isLoopbackConnection)
        {
            return isLoopbackConnection ? g2 : g1;
        }
        
        return null;
    }
}