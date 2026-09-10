using Avalonia;

namespace AVASvgMaker.Models;

/// <summary>The flowchart "data" shape.</summary>
public class ParallelogramShape : PolygonShape
{
    public override ShapeKind Kind => ShapeKind.Parallelogram;

    public ParallelogramShape(Rect bounds) : base(bounds)
    {
    }

    public override Point[] GetPolygonPoints()
    {
        var slant = Bounds.Width / 5;

        return
        [
            new Point(Bounds.Left + slant, Bounds.Top),
            new Point(Bounds.Right, Bounds.Top),
            new Point(Bounds.Right - slant, Bounds.Bottom),
            new Point(Bounds.Left, Bounds.Bottom)
        ];
    }
}
