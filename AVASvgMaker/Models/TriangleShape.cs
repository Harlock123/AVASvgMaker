using Avalonia;

namespace AVASvgMaker.Models;

public class TriangleShape : PolygonShape
{
    public override ShapeKind Kind => ShapeKind.Triangle;

    public TriangleShape(Rect bounds) : base(bounds)
    {
    }

    public override Point[] GetPolygonPoints() =>
    [
        new Point(Bounds.Center.X, Bounds.Top),
        new Point(Bounds.Right, Bounds.Bottom),
        new Point(Bounds.Left, Bounds.Bottom)
    ];
}
