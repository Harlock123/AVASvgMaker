using Avalonia;

namespace AVASvgMaker.Models;

public class HexagonShape : PolygonShape
{
    public override ShapeKind Kind => ShapeKind.Hexagon;

    public HexagonShape(Rect bounds) : base(bounds)
    {
    }

    public override Point[] GetPolygonPoints()
    {
        var inset = Bounds.Width / 4;

        return
        [
            new Point(Bounds.Left + inset, Bounds.Top),
            new Point(Bounds.Right - inset, Bounds.Top),
            new Point(Bounds.Right, Bounds.Center.Y),
            new Point(Bounds.Right - inset, Bounds.Bottom),
            new Point(Bounds.Left + inset, Bounds.Bottom),
            new Point(Bounds.Left, Bounds.Center.Y)
        ];
    }
}
