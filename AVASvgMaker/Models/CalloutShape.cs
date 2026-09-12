using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Media;

namespace AVASvgMaker.Models;

/// <summary>
/// A bubble with a tail, and the tail is the point of it: a callout that cannot be aimed is
/// only a box with a spike on it.
///
/// The bubble is the shape's box and the tail hangs outside it, which is what lets the tail
/// reach something. Where it points is held as fractions of the box, so it travels with the
/// shape and stretches with it - or it is pinned to a connection point on another shape, and
/// then it follows that shape about instead.
///
/// Body and tail are one outline rather than two overlaid, because two would be stroked along
/// the seam where they meet and the join would show.
/// </summary>
public class CalloutShape : DiagramShape
{
    private readonly ShapeKind _kind;

    public override ShapeKind Kind => _kind;

    /// <summary>
    /// Where the tail points, as fractions of the bubble's box. Beyond 0 and 1 is the usual
    /// case - a tail inside the bubble would have nothing to point at.
    /// </summary>
    public Point Tail { get; set; } = new(0.1, 1.45);

    /// <summary>The shape the tail is pinned to, and which of its connection points.</summary>
    public DiagramShape? TailShape { get; set; }

    public int TailPort { get; set; } = -1;

    public CalloutShape(ShapeKind kind, Rect bounds) : base(bounds) => _kind = kind;

    /// <summary>
    /// Where the tip actually is, in the shape's own upright frame - the frame the outline is
    /// written in, so that a turned callout still reaches what it is aimed at.
    ///
    /// A pinned tail asks the shape it is pinned to every time, so that moving that shape
    /// moves the tail without anything having to be told.
    /// </summary>
    public Point TailTip
    {
        get
        {
            if (TailShape is { } target)
            {
                var points = target.ConnectionPoints;

                if (TailPort >= 0 && TailPort < points.Count)
                    return Unrotate(points[TailPort]);
            }

            return new Point(
                Bounds.X + Tail.X * Bounds.Width,
                Bounds.Y + Tail.Y * Bounds.Height);
        }
    }

    /// <summary>
    /// Lets go of whatever the tail was pinned to, keeping it where it currently points. Used
    /// when that shape is deleted: the callout should stay aimed at the spot, not jump.
    /// </summary>
    public void Unpin()
    {
        if (TailShape is null)
            return;

        var tip = TailTip;

        TailShape = null;
        TailPort = -1;

        if (Bounds.Width > 0 && Bounds.Height > 0)
            Tail = new Point(
                (tip.X - Bounds.X) / Bounds.Width,
                (tip.Y - Bounds.Y) / Bounds.Height);
    }

    /// <summary>
    /// Whether the tail is a spike cut out of the bubble. A thought trails bubbles instead,
    /// so nothing on the shape itself shows where the tip has got to.
    /// </summary>
    public bool Spiked => _kind != ShapeKind.ThoughtBubble;

    /// <summary>Whether this kind of callout has a tail to aim at all.</summary>
    public static bool HasTail(ShapeKind kind) =>
        kind is ShapeKind.SpeechBubble or ShapeKind.OvalCallout
            or ShapeKind.RectangularCallout or ShapeKind.ThoughtBubble;

    public override Geometry CreateGeometry() => StencilPath.ToGeometry(UnitOutline, Bounds);

    public override string UnitOutline => Outline();

    /// <summary>
    /// A thought bubble trails smaller bubbles rather than a spike, so they are drawn over the
    /// body rather than being part of it.
    /// </summary>
    public override string? UnitDetail => _kind == ShapeKind.ThoughtBubble ? Trail() : null;

    protected override void Draw(DrawingContext context, bool withText)
    {
        context.DrawGeometry(FillBrush(), CreatePen(), CreateGeometry());

        if (UnitDetail is { } trail)
            context.DrawGeometry(FillBrush(), CreatePen(), StencilPath.ToGeometry(trail, Bounds));

        if (withText)
            RenderText(context);
    }

    protected override string SvgBody()
    {
        var body = $"<path d=\"{StencilPath.ToSvgData(UnitOutline, Bounds)}\" {SvgStyle()} />";

        return UnitDetail is { } trail
            ? $"<g>{body}<path d=\"{StencilPath.ToSvgData(trail, Bounds)}\" {SvgStyle()} /></g>"
            : body;
    }

    /// <summary>The words go in the bubble, never out along the tail.</summary>
    protected override Rect TextArea => Framed(Bounds);

    #region The outline

    /// <summary>How wide the tail is where it leaves the bubble, as a fraction of the box.</summary>
    private const double Root = 0.22;

    /// <summary>
    /// The bubble with the tail spliced into it: the body is walked as a ring of points, and
    /// where the ring passes nearest the tip the tail is let into it.
    ///
    /// A ring rather than arcs and curves because splicing into a bezier is a good deal of
    /// work for a join nobody will see - at this many points a circle is round.
    /// </summary>
    private string Outline()
    {
        var ring = Body().ToList();

        // A thought is not spoken, so it trails bubbles instead of a spike - and those are
        // drawn over the body rather than cut into it.
        if (!HasTail(_kind) || _kind == ShapeKind.ThoughtBubble || ring.Count < 3)
            return Ring(ring);

        var tip = TailInUnits();

        // A tail that points at the middle of its own bubble points at nothing.
        if (Apart(tip, new Point(0.5, 0.5)) < 1e-6)
            return Ring(ring);

        // How far round the outline each vertex sits, so the root can be measured as a
        // distance rather than as a count of vertices. The vertices are not evenly spaced - a
        // rounded corner packs several into a very short arc - so counting them would make the
        // root a sliver at the corners and half a side along the straight edges.
        var mark = new double[ring.Count];
        var total = 0.0;

        for (var i = 0; i < ring.Count; i++)
        {
            mark[i] = total;
            total += Apart(ring[i], ring[(i + 1) % ring.Count]);
        }

        if (total < 1e-9)
            return Ring(ring);

        var nearest = 0;
        var closest = double.MaxValue;

        for (var i = 0; i < ring.Count; i++)
        {
            var gap = Apart(ring[i], tip);

            if (gap >= closest)
                continue;

            closest = gap;
            nearest = i;
        }

        // Half the root either side of the nearest vertex - but never so much of a small
        // bubble that the tail eats the thing it belongs to.
        var half = Math.Min(Root / 2, total / 6);
        var opens = Along(mark[nearest] + half, total);
        var closes = Along(mark[nearest] - half, total);
        var body = total - 2 * half;

        // From one root, the long way round the bubble, to the other - and then out to the
        // tip, which closes the figure. What is left out is the short arc between the roots,
        // the part of the bubble nearest whatever is being pointed at.
        var spliced = new List<Point> { On(ring, mark, total, opens) };

        for (var i = 1; i <= ring.Count; i++)
        {
            var at = (nearest + i) % ring.Count;

            if (Along(mark[at] - opens, total) < body)
                spliced.Add(ring[at]);
        }

        spliced.Add(On(ring, mark, total, closes));
        spliced.Add(tip);

        return Ring(spliced);
    }

    /// <summary>A distance round a closed outline, brought back into range.</summary>
    private static double Along(double distance, double total) =>
        ((distance % total) + total) % total;

    /// <summary>The point that far round the outline, between vertices where it falls between them.</summary>
    private static Point On(IReadOnlyList<Point> ring, double[] mark, double total, double at)
    {
        for (var i = ring.Count - 1; i >= 0; i--)
        {
            if (at < mark[i])
                continue;

            var next = (i + 1) % ring.Count;
            var run = (i + 1 == ring.Count ? total : mark[next]) - mark[i];
            var t = run > 1e-9 ? (at - mark[i]) / run : 0;

            return new Point(
                ring[i].X + (ring[next].X - ring[i].X) * t,
                ring[i].Y + (ring[next].Y - ring[i].Y) * t);
        }

        return ring[0];
    }

    /// <summary>The tail's tip in the same fractions the outline is written in.</summary>
    private Point TailInUnits()
    {
        if (TailShape is null || Bounds.Width <= 0 || Bounds.Height <= 0)
            return Tail;

        var tip = TailTip;

        return new Point(
            (tip.X - Bounds.X) / Bounds.Width,
            (tip.Y - Bounds.Y) / Bounds.Height);
    }

    /// <summary>The bubble itself, as a ring of points running clockwise.</summary>
    private IEnumerable<Point> Body()
    {
        const int Steps = 64;

        switch (_kind)
        {
            case ShapeKind.OvalCallout or ShapeKind.ThoughtBubble:
                for (var i = 0; i < Steps; i++)
                {
                    var angle = Math.Tau * i / Steps;
                    yield return new Point(0.5 + 0.5 * Math.Cos(angle), 0.5 + 0.5 * Math.Sin(angle));
                }

                break;

            case ShapeKind.SpeechBubble:
            {
                // A rounded box: the corners are quarter circles walked a few steps each.
                const double R = 0.18;

                foreach (var point in Corner(1 - R, R, -90, 0, R)) yield return point;
                foreach (var point in Corner(1 - R, 1 - R, 0, 90, R)) yield return point;
                foreach (var point in Corner(R, 1 - R, 90, 180, R)) yield return point;
                foreach (var point in Corner(R, R, 180, 270, R)) yield return point;

                break;
            }

            default:
                yield return new Point(0, 0);
                yield return new Point(0.5, 0);
                yield return new Point(1, 0);
                yield return new Point(1, 0.5);
                yield return new Point(1, 1);
                yield return new Point(0.5, 1);
                yield return new Point(0, 1);
                yield return new Point(0, 0.5);

                break;
        }
    }

    private static IEnumerable<Point> Corner(double cx, double cy, double from, double to, double r)
    {
        const int Steps = 6;

        for (var i = 0; i <= Steps; i++)
        {
            var angle = (from + (to - from) * i / Steps) * Math.PI / 180;

            yield return new Point(cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
        }
    }

    /// <summary>The two small bubbles a thought trails towards whatever it is about.</summary>
    private string Trail()
    {
        var tip = TailInUnits();
        var away = new Point(tip.X - 0.5, tip.Y - 0.5);
        var length = Math.Sqrt(away.X * away.X + away.Y * away.Y);

        if (length < 1e-6)
            return string.Empty;

        var sb = new StringBuilder();

        foreach (var (along, size) in new[] { (0.62, 0.075), (0.85, 0.045) })
        {
            var at = new Point(0.5 + away.X * along, 0.5 + away.Y * along);

            sb.Append(Ring(Enumerable.Range(0, 16).Select(i =>
            {
                var angle = Math.Tau * i / 16;
                return new Point(at.X + size * Math.Cos(angle), at.Y + size * Math.Sin(angle));
            })));

            sb.Append(' ');
        }

        return sb.ToString().Trim();
    }

    private static double Apart(Point a, Point b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private static string Ring(IEnumerable<Point> points)
    {
        var sb = new StringBuilder();
        var first = true;

        foreach (var point in points)
        {
            sb.Append(first ? "M " : "L ");
            sb.Append($"{Num(point.X)},{Num(point.Y)} ");
            first = false;
        }

        return sb.Append('Z').ToString();
    }

    #endregion
}
