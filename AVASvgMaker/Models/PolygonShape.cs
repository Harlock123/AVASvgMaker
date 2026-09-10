using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;

namespace AVASvgMaker.Models;

/// <summary>Shapes defined by a closed run of straight edges.</summary>
public abstract class PolygonShape : DiagramShape
{
    protected PolygonShape(Rect bounds) : base(bounds)
    {
    }

    public abstract Point[] GetPolygonPoints();

    public override Geometry CreateGeometry()
    {
        var points = GetPolygonPoints();
        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], true);
            for (var i = 1; i < points.Length; i++)
                context.LineTo(points[i]);
            context.EndFigure(true);
        }

        return geometry;
    }

    protected override string SvgBody()
    {
        var points = string.Join(" ", GetPolygonPoints()
            .Select(p => $"{Num(p.X)},{Num(p.Y)}"));

        return $"<polygon points=\"{points}\" {SvgStyle()} />";
    }
}
