using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AVASvgMaker.Views;

/// <summary>
/// The scale along the top or down the left of the drawing area, marked in page units.
///
/// It takes its position from the canvas rather than from the scroll offset: the page is
/// centred in the workspace when it is smaller than the window, so where a page coordinate
/// lands on screen depends on the layout as much as on the scrolling. Asking the canvas where
/// it is gets both at once, and cannot drift out of step with it.
/// </summary>
public class RulerStrip : Control
{
    /// <summary>How thick the strip is, and so how much room the numbers have.</summary>
    public const double Thickness = 20;

    private static readonly double[] Steps = [5, 10, 20, 25, 50, 100, 200, 250, 500, 1000];

    private bool _vertical;
    private DrawingCanvas? _canvas;
    private double _pointer = double.NaN;

    /// <summary>Set from the layout: a strip down the side rather than across the top.</summary>
    public bool Vertical
    {
        get => _vertical;
        set
        {
            _vertical = value;
            Shape();
        }
    }

    public RulerStrip() => Shape();

    private void Shape()
    {
        Width = _vertical ? Thickness : double.NaN;
        Height = _vertical ? double.NaN : Thickness;
    }

    public void Attach(DrawingCanvas canvas, ScrollViewer scroll)
    {
        _canvas = canvas;

        canvas.ZoomChanged += _ => InvalidateVisual();
        canvas.PointerOnPage += At;
        canvas.LayoutUpdated += (_, _) => InvalidateVisual();
        scroll.ScrollChanged += (_, _) => InvalidateVisual();
    }

    private void At(Point? page)
    {
        var value = page is { } point ? (_vertical ? point.Y : point.X) : double.NaN;

        if (double.IsNaN(value) && double.IsNaN(_pointer))
            return;

        _pointer = value;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;

        context.DrawRectangle(AppTheme.Panel, null, new Rect(size));

        if (_canvas is null || !IsVisible)
            return;

        // Where page zero sits along this strip, and how far a page unit reaches.
        var origin = _canvas.TranslatePoint(default, this);

        if (origin is not { } corner)
            return;

        var zoom = _canvas.Zoom;
        var start = (_vertical ? corner.Y : corner.X) + DrawingCanvas.PageMargin * zoom;
        var length = _vertical ? size.Height : size.Width;

        var pen = new Pen(AppTheme.Border, 1);
        var text = AppTheme.Text;

        context.DrawLine(pen, Edge(0), Edge(length));

        // The first step whose marks are far enough apart to put a number between them.
        var step = Steps[^1];

        foreach (var candidate in Steps)
        {
            if (candidate * zoom >= 44)
            {
                step = candidate;
                break;
            }
        }

        var first = Math.Floor((0 - start) / zoom / step) * step;
        var last = (length - start) / zoom;

        for (var value = first; value <= last; value += step)
        {
            var at = start + value * zoom;

            if (at < -step * zoom || at > length)
                continue;

            context.DrawLine(pen, Across(at, Thickness * 0.45), Across(at, Thickness));

            // A minor mark halfway between the numbered ones.
            var half = at + step * zoom / 2;

            if (half <= length)
                context.DrawLine(pen, Across(half, Thickness * 0.75), Across(half, Thickness));

            if (value < 0)
                continue;

            var label = new FormattedText(
                value.ToString("0", CultureInfo.InvariantCulture),
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                Typeface.Default, 9, text);

            context.DrawText(label, _vertical
                ? new Point(2, at + 2)
                : new Point(at + 2, 3));
        }

        if (double.IsNaN(_pointer))
            return;

        var marker = start + _pointer * zoom;

        if (marker >= 0 && marker <= length)
            context.DrawLine(new Pen(AppTheme.Accent, 1), Across(marker, 0), Across(marker, Thickness));
    }

    /// <summary>A point along the strip, at the given depth into it.</summary>
    private Point Across(double along, double depth) =>
        _vertical ? new Point(depth, along) : new Point(along, depth);

    /// <summary>A point on the strip's inner edge, where it meets the drawing.</summary>
    private Point Edge(double along) =>
        _vertical ? new Point(Thickness, along) : new Point(along, Thickness);
}
