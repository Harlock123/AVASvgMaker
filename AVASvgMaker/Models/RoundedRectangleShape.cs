using System;
using Avalonia;
using Avalonia.Media;

namespace AVASvgMaker.Models;

public class RoundedRectangleShape : DiagramShape
{
    public override ShapeKind Kind => ShapeKind.RoundedRectangle;

    public RoundedRectangleShape(Rect bounds) : base(bounds)
    {
    }

    private double CornerRadius => Math.Min(16, Math.Min(Bounds.Width, Bounds.Height) / 4);

    public override Geometry CreateGeometry() =>
        new RectangleGeometry(Bounds) { RadiusX = CornerRadius, RadiusY = CornerRadius };

    protected override string SvgBody() =>
        $"<rect x=\"{Num(Bounds.X)}\" y=\"{Num(Bounds.Y)}\" " +
        $"width=\"{Num(Bounds.Width)}\" height=\"{Num(Bounds.Height)}\" " +
        $"rx=\"{Num(CornerRadius)}\" ry=\"{Num(CornerRadius)}\" {SvgStyle()} />";
}
