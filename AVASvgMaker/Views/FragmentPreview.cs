using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;

namespace AVASvgMaker.Views;

/// <summary>
/// A thumbnail of a saved fragment: the shapes themselves, scaled down to fit the box.
///
/// The shapes are drawn rather than an outline of them, because a stencil of your own is a
/// piece of drawing - its colours and its arrangement are most of what makes it recognisable
/// in a list of other people's rectangles.
/// </summary>
public class FragmentPreview : Control
{
    private const double Padding = 2;

    private readonly List<DiagramShape> _shapes = [];
    private Rect _extent;

    public FragmentPreview(string fragment)
    {
        IsHitTestVisible = false;

        try
        {
            _shapes.AddRange(DiagramFile.FromJson(fragment).Shapes);
        }
        catch
        {
            // A fragment that will not parse simply draws nothing.
            return;
        }

        if (ShapeClipboard.Extent(fragment) is { } extent)
            _extent = extent;
    }

    public override void Render(DrawingContext context)
    {
        if (_shapes.Count == 0 || _extent.Width <= 0 || _extent.Height <= 0)
            return;

        var box = new Rect(Padding, Padding, Bounds.Width - Padding * 2, Bounds.Height - Padding * 2);

        if (box.Width <= 0 || box.Height <= 0)
            return;

        // Fitted rather than stretched, so a tall fragment is not squashed into a wide box.
        var scale = System.Math.Min(box.Width / _extent.Width, box.Height / _extent.Height);

        var offset = new Point(
            box.Center.X - _extent.Center.X * scale,
            box.Center.Y - _extent.Center.Y * scale);

        using var placed = context.PushTransform(
            Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset.X, offset.Y));

        foreach (var shape in _shapes)
            shape.Render(context);
    }
}
