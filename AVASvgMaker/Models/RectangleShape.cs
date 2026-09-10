using Avalonia;
using Avalonia.Media;

namespace AVASvgMaker.Models;

public class RectangleShape : DiagramShape
{
    public override ShapeKind Kind => ShapeKind.Rectangle;

    public RectangleShape(Rect bounds) : base(bounds)
    {
    }

    public override Geometry CreateGeometry() => new RectangleGeometry(Bounds);

    protected override string SvgBody() =>
        $"<rect x=\"{Num(Bounds.X)}\" y=\"{Num(Bounds.Y)}\" " +
        $"width=\"{Num(Bounds.Width)}\" height=\"{Num(Bounds.Height)}\" {SvgStyle()} />";
}
