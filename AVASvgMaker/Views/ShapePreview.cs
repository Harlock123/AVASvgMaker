using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AVASvgMaker.Models;

namespace AVASvgMaker.Views;

/// <summary>Draws a single shape scaled to fill the control - used for toolbox thumbnails.</summary>
public class ShapePreview : Control
{
    private const double Padding = 3;

    public ShapeKind Kind { get; }

    public ShapePreview(ShapeKind kind)
    {
        Kind = kind;
        IsHitTestVisible = false;
    }

    public override void Render(DrawingContext context)
    {
        var box = new Rect(
            Padding,
            Padding,
            Bounds.Width - Padding * 2,
            Bounds.Height - Padding * 2);

        if (box.Width <= 0 || box.Height <= 0)
            return;

        var shape = ShapeFactory.Create(Kind, box);
        shape.StrokeThickness = 1.5;
        shape.Render(context);
    }
}
