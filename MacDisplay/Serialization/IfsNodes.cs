#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

using IFSEngine.Model;

using MacDisplay.Helper;

namespace MacDisplay.Serialization;

/// <summary>
/// Helper class for serializing <see cref="IFS"/> with node positions.
/// </summary>
public class IfsNodes(IFS? ifs)
{
    public IFS IFS { get; set; } = ifs ?? new IFS();

    public List<BindablePoint> Positions
    {
        get
        {
            if (IFS?.Iterators == null)
                return [];

            return IFS.Iterators
                .Where(i => i != null)
                .Select(i => i.GetPosition()!)
                .Where(p => p != null)
                .ToList();
        }
        set
        {
            if (IFS?.Iterators == null || value == null)
                return;

            var iteratorList = IFS.Iterators.ToList();
            for (int i = 0; i < Math.Min(value.Count, iteratorList.Count); i++)
            {
                iteratorList[i]?.SetPosition(value[i]);
            }
        }
    }
}