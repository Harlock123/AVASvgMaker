using Avalonia;
using Avalonia.Media;

namespace AVASvgMaker.Models;

public class EllipseShape : DiagramShape
{
    public override ShapeKind Kind => ShapeKind.Ellipse;

    public EllipseShape(Rect bounds) : base(bounds)
    {
    }

    public override Geometry CreateGeometry() => new EllipseGeometry(Bounds);

    // The handle length that puts a cubic on a quarter of a circle.
    private const double Handle = 0.5522847498307933;

    public override string UnitOutline =>
        $"M 1,0.5 C 1,{Num(0.5 + Handle / 2)} {Num(0.5 + Handle / 2)},1 0.5,1 " +
        $"C {Num(0.5 - Handle / 2)},1 0,{Num(0.5 + Handle / 2)} 0,0.5 " +
        $"C 0,{Num(0.5 - Handle / 2)} {Num(0.5 - Handle / 2)},0 0.5,0 " +
        $"C {Num(0.5 + Handle / 2)},0 1,{Num(0.5 - Handle / 2)} 1,0.5 Z";

    protected override string SvgBody() =>
        $"<ellipse cx=\"{Num(Bounds.Center.X)}\" cy=\"{Num(Bounds.Center.Y)}\" " +
        $"rx=\"{Num(Bounds.Width / 2)}\" ry=\"{Num(Bounds.Height / 2)}\" {SvgStyle()} />";
}
