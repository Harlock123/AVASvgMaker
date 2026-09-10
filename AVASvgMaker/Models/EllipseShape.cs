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

    protected override string SvgBody() =>
        $"<ellipse cx=\"{Num(Bounds.Center.X)}\" cy=\"{Num(Bounds.Center.Y)}\" " +
        $"rx=\"{Num(Bounds.Width / 2)}\" ry=\"{Num(Bounds.Height / 2)}\" {SvgStyle()} />";
}
