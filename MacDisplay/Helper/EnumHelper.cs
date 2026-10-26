#nullable enable
using System;
using System.Collections.Generic;
using IFSEngine.Animation;

namespace MacDisplay.Helper;

public static class EnumHelper
{
    public static IEnumerable<InterpolationMode> InterpolationModes => Enum.GetValues<InterpolationMode>();
    public static IEnumerable<EasingDirection> EasingDirections => Enum.GetValues<EasingDirection>();
}