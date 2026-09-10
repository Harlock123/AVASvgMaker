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

    public override string UnitOutline
    {
        get
        {
            // The corner is the same length of edge either way round, so in the unit square
            // it becomes two different fractions.
            var x = Bounds.Width <= 0 ? 0 : CornerRadius / Bounds.Width;
            var y = Bounds.Height <= 0 ? 0 : CornerRadius / Bounds.Height;

            // The handle that puts a cubic on a quarter circle, along each axis.
            var hx = x * (1 - 0.5522847498307933);
            var hy = y * (1 - 0.5522847498307933);

            return $"M {Num(x)},0 L {Num(1 - x)},0 C {Num(1 - hx)},0 1,{Num(hy)} 1,{Num(y)} " +
                   $"L 1,{Num(1 - y)} C 1,{Num(1 - hy)} {Num(1 - hx)},1 {Num(1 - x)},1 " +
                   $"L {Num(x)},1 C {Num(hx)},1 0,{Num(1 - hy)} 0,{Num(1 - y)} " +
                   $"L 0,{Num(y)} C 0,{Num(hy)} {Num(hx)},0 {Num(x)},0 Z";
        }
    }

    protected override string SvgBody() =>
        $"<rect x=\"{Num(Bounds.X)}\" y=\"{Num(Bounds.Y)}\" " +
        $"width=\"{Num(Bounds.Width)}\" height=\"{Num(Bounds.Height)}\" " +
        $"rx=\"{Num(CornerRadius)}\" ry=\"{Num(CornerRadius)}\" {SvgStyle()} />";
}
