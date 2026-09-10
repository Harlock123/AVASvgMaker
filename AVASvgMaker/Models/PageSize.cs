using System;
using System.Collections.Generic;

namespace AVASvgMaker.Models;

/// <summary>
/// A named page size, in page units - which are pixels at 96 DPI, the same units everything
/// else on the page is measured in, and what SVG calls a user unit.
/// </summary>
public record PageSize(string Name, double Width, double Height)
{
    /// <summary>Portrait dimensions; landscape is the same size with the sides swapped.</summary>
    public static readonly IReadOnlyList<PageSize> Presets =
    [
        new("Letter", 816, 1056),
        new("Legal", 816, 1344),
        new("Tabloid", 1056, 1632),
        new("A5", 559, 794),
        new("A4", 794, 1123),
        new("A3", 1123, 1587)
    ];

    /// <summary>The preset matching these dimensions in either orientation, or null.</summary>
    public static PageSize? Match(double width, double height)
    {
        foreach (var preset in Presets)
        {
            if (Same(preset.Width, width) && Same(preset.Height, height))
                return preset;

            if (Same(preset.Width, height) && Same(preset.Height, width))
                return preset;
        }

        return null;
    }

    private static bool Same(double a, double b) => Math.Abs(a - b) < 1;
}
