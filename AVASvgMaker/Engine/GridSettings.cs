using System;
using Avalonia;

namespace AVASvgMaker.Engine;

/// <summary>Grid display and snapping rules for the page.</summary>
public class GridSettings
{
    /// <summary>Every Nth line is drawn darker.</summary>
    public const int MajorEvery = 5;

    /// <summary>
    /// The sizes offered. Named here rather than in the toolbar's markup so that the settings
    /// dialog offers the same five - two lists of grid sizes would sooner or later disagree.
    /// </summary>
    public static readonly double[] Sizes = [5, 10, 20, 25, 50];

    public bool ShowGrid { get; set; } = true;
    public bool SnapToGrid { get; set; } = true;
    public double Size { get; set; } = 10;

    public double Snap(double value) => SnapToGrid ? Math.Round(value / Size) * Size : value;

    public Point Snap(Point point) => new(Snap(point.X), Snap(point.Y));

    /// <summary>Snaps the origin and the size, keeping the rectangle at least one cell across.</summary>
    public Rect Snap(Rect rect)
    {
        if (!SnapToGrid)
            return rect;

        return new Rect(
            Snap(rect.X),
            Snap(rect.Y),
            Math.Max(Size, Snap(rect.Width)),
            Math.Max(Size, Snap(rect.Height)));
    }
}
