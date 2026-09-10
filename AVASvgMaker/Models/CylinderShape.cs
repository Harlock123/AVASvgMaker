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
