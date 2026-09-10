using Avalonia;

namespace AVASvgMaker.Models;

public class DiamondShape : PolygonShape
{
    public override ShapeKind Kind => ShapeKind.Diamond;

    public DiamondShape(Rect bounds) : base(bounds)
    {
    }

    public override Point[] GetPolygonPoints() =>
    [
        new Point(Bounds.Center.X, Bounds.Top),
        new Point(Bounds.Right, Bounds.Center.Y),
        new Point(Bounds.Center.X, Bounds.Bottom),
        new Point(Bounds.Left, Bounds.Center.Y)
    ];
}
