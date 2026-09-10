using System;
using Avalonia;
using Avalonia.Media;

namespace AVASvgMaker.Models;

/// <summary>The flowchart "database" shape: a tube with an elliptical lip.</summary>
public class CylinderShape : DiagramShape
{
    public override ShapeKind Kind => ShapeKind.Cylinder;

    public CylinderShape(Rect bounds) : base(bounds)
    {
    }

    private double LipRadius => Math.Min(Bounds.Height / 4, Bounds.Width / 4);

    public override Geometry CreateGeometry()
    {
        var ry = LipRadius;
        var rx = Bounds.Width / 2;
        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(Bounds.Left, Bounds.Top + ry), true);
            context.ArcTo(new Point(Bounds.Right, Bounds.Top + ry), new Size(rx, ry), 0, false, SweepDirection.Clockwise);
            context.LineTo(new Point(Bounds.Right, Bounds.Bottom - ry));
            context.ArcTo(new Point(Bounds.Left, Bounds.Bottom - ry), new Size(rx, ry), 0, false, SweepDirection.Clockwise);
            context.EndFigure(true);
        }

        return geometry;
    }

    /// <summary>
    /// The barrel, then the front edge of the lip as a second, open run - the same two parts
    /// the shape is drawn in. Each end of the barrel is half an ellipse, and the lip is the
    /// half of the top one that a real barrel would let you see.
    /// </summary>
    public override string UnitOutline
    {
        get
        {
            // How deep the ellipse at each end is, as a fraction of the shape.
            var r = Bounds.Height <= 0 ? 0.25 : LipRadius / Bounds.Height;

            // The handle that puts a cubic on a quarter ellipse, along each axis.
            const double Pull = 0.5522847498307933;
            var across = 0.5 * Pull;
            var deep = r * Pull;

            // Half an ellipse from one side of the shape to the other, by way of a point
            // between them: the flat side of a barrel end.
            string Half(double ends, double middle, double edge) =>
                $"C 0,{Num(edge)} {Num(0.5 - across)},{Num(middle)} 0.5,{Num(middle)} " +
                $"C {Num(0.5 + across)},{Num(middle)} 1,{Num(edge)} 1,{Num(ends)} ";

            var top = $"M 0,{Num(r)} " + Half(r, 0, r - deep);
            var side = $"L 1,{Num(1 - r)} ";

            var bottom = $"C 1,{Num(1 - r + deep)} {Num(0.5 + across)},1 0.5,1 " +
                         $"C {Num(0.5 - across)},1 0,{Num(1 - r + deep)} 0,{Num(1 - r)} ";

            return (top + side + bottom + "Z").TrimEnd();
        }
    }

    /// <summary>The half of the top ellipse a real barrel would let you see.</summary>
    public override string? UnitDetail
    {
        get
        {
            var r = Bounds.Height <= 0 ? 0.25 : LipRadius / Bounds.Height;
            const double Pull = 0.5522847498307933;
            var across = 0.5 * Pull;
            var deep = r * Pull;

            return $"M 0,{Num(r)} C 0,{Num(r + deep)} {Num(0.5 - across)},{Num(2 * r)} 0.5,{Num(2 * r)} " +
                   $"C {Num(0.5 + across)},{Num(2 * r)} 1,{Num(r + deep)} 1,{Num(r)}";
        }
    }

    /// <summary>The front edge of the top lip, drawn as an unfilled arc over the body.</summary>
    private Geometry CreateLipGeometry()
    {
        var ry = LipRadius;
        var rx = Bounds.Width / 2;
        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(Bounds.Left, Bounds.Top + ry), false);
            context.ArcTo(new Point(Bounds.Right, Bounds.Top + ry), new Size(rx, ry), 0, false, SweepDirection.CounterClockwise);
            context.EndFigure(false);
        }

        return geometry;
    }

    protected override void Draw(DrawingContext context, bool withText)
    {
        var brush = new SolidColorBrush(Fill);
        var pen = CreatePen();

        context.DrawGeometry(brush, pen, CreateGeometry());
        context.DrawGeometry(null, pen, CreateLipGeometry());

        if (withText)
            RenderText(context);
    }

    protected override string SvgBody()
    {
        var ry = LipRadius;
        var rx = Bounds.Width / 2;

        var body =
            $"M {Num(Bounds.Left)},{Num(Bounds.Top + ry)} " +
            $"A {Num(rx)},{Num(ry)} 0 0 1 {Num(Bounds.Right)},{Num(Bounds.Top + ry)} " +
            $"L {Num(Bounds.Right)},{Num(Bounds.Bottom - ry)} " +
            $"A {Num(rx)},{Num(ry)} 0 0 1 {Num(Bounds.Left)},{Num(Bounds.Bottom - ry)} Z";

        var lip =
            $"M {Num(Bounds.Left)},{Num(Bounds.Top + ry)} " +
            $"A {Num(rx)},{Num(ry)} 0 0 0 {Num(Bounds.Right)},{Num(Bounds.Top + ry)}";

        return $"<g><path d=\"{body}\" {SvgStyle()} />" +
               $"<path d=\"{lip}\" fill=\"none\" stroke=\"{ToHex(Stroke)}\" stroke-width=\"{Num(StrokeThickness)}\" /></g>";
    }
}
