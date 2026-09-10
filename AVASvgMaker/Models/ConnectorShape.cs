using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using AVASvgMaker.Engine;
using Avalonia.Media;

namespace AVASvgMaker.Models;

/// <summary>
/// A straight line between two points, each of which may be glued to a shape. A glued end
/// tracks the shape's centre and is clipped to its edge, so the line follows the shape as
/// it is moved or resized.
/// </summary>
public class ConnectorShape : DiagramShape
{
    private const double CapLength = 12;
    private const double CapHalfWidth = 5;
    private const double DotRadius = 4;

    /// <summary>How close a click has to be, in page units, to count as hitting the line.</summary>
    private const double HitTolerance = 6;

    public override ShapeKind Kind => ShapeKind.Connector;

    public override bool CanRotate => false;

    public override bool IsBoxResizable => false;

    public Point Start { get; set; }
    public Point End { get; set; }

    public DiagramShape? StartShape { get; set; }
    public DiagramShape? EndShape { get; set; }

    public EndCapStyle StartCap { get; set; } = EndCapStyle.None;
    public EndCapStyle EndCap { get; set; } = EndCapStyle.Arrow;

    /// <summary>
    /// Which of the glued shape's connection points this end is attached to, or -1 to let the
    /// line meet the shape wherever the two centres line up.
    /// </summary>
    public int StartPort { get; set; } = -1;

    public int EndPort { get; set; } = -1;

    /// <summary>Straight by default, so documents written before routing existed look the same.</summary>
    public ConnectorRouting Routing { get; set; } = ConnectorRouting.Straight;

    /// <summary>The routed polyline, refreshed by <see cref="UpdateRoute"/>.</summary>
    private IReadOnlyList<Point> _route = [];

    /// <summary>Identifies the geometry the current route was computed for.</summary>
    private int _routeKey;

    /// <summary>
    /// Bends the user has placed by hand, as interior points between the two ends. While these
    /// are set the connector keeps them instead of routing itself, so a route that has been
    /// tidied up by hand is not undone the next time something moves.
    /// </summary>
    public IReadOnlyList<Point> Waypoints { get; set; } = [];

    public bool HasManualRoute => Waypoints.Count > 0;

    #region Editing the route by hand

    /// <summary>
    /// Moves one bend, keeping the right angles either side of it. A neighbouring bend is
    /// simply brought along; a neighbouring *end* cannot move, because it is pinned to a
    /// shape, so a new bend is inserted next to it to take up the change instead.
    /// </summary>
    public void MoveBend(int index, Point target, bool keepRightAngles)
    {
        var path = Path.ToList();

        if (index <= 0 || index >= path.Count - 1)
            return;

        var previous = path[index - 1];
        var next = path[index + 1];
        var horizontalBefore = Math.Abs(previous.Y - path[index].Y) < 0.01;
        var horizontalAfter = Math.Abs(path[index].Y - next.Y) < 0.01;

        path[index] = target;

        if (!keepRightAngles)
        {
            Adopt(path);
            return;
        }

        // The far side first, so inserting there does not shift the index of the near side.
        if (index + 1 == path.Count - 1)
        {
            path.Insert(index + 1, horizontalAfter
                ? new Point(target.X, next.Y)
                : new Point(next.X, target.Y));
        }
        else
        {
            path[index + 1] = horizontalAfter
                ? new Point(next.X, target.Y)
                : new Point(target.X, next.Y);
        }

        if (index - 1 == 0)
        {
            path.Insert(index, horizontalBefore
                ? new Point(target.X, previous.Y)
                : new Point(previous.X, target.Y));
        }
        else
        {
            path[index - 1] = horizontalBefore
                ? new Point(previous.X, target.Y)
                : new Point(target.X, previous.Y);
        }

        Adopt(path);
    }

    /// <summary>
    /// Slides a whole segment sideways, keeping it parallel to where it was. A segment that
    /// meets a shape cannot move its end, so a new bend is inserted there instead - which is
    /// what turns one segment into three, each with a grab point of its own.
    /// </summary>
    public void SlideSegment(int index, Point target)
    {
        var path = Path.ToList();

        if (index < 0 || index + 1 >= path.Count)
            return;

        var a = path[index];
        var b = path[index + 1];
        var horizontal = Math.Abs(a.Y - b.Y) < 0.01;

        var movedA = horizontal ? new Point(a.X, target.Y) : new Point(target.X, a.Y);
        var movedB = horizontal ? new Point(b.X, target.Y) : new Point(target.X, b.Y);

        var moved = new List<Point>();

        for (var i = 0; i < path.Count; i++)
        {
            if (i == index)
            {
                // The first point is pinned to a shape: leave it and add a bend beside it.
                if (i == 0)
                    moved.Add(path[0]);

                moved.Add(movedA);
            }
            else if (i == index + 1)
            {
                moved.Add(movedB);

                if (i == path.Count - 1)
                    moved.Add(path[^1]);
            }
            else
            {
                moved.Add(path[i]);
            }
        }

        Adopt(moved);
    }

    /// <summary>Takes a bend out again, letting the two segments either side become one.</summary>
    public void RemoveBend(int index)
    {
        var path = Path.ToList();

        if (index <= 0 || index >= path.Count - 1)
            return;

        path.RemoveAt(index);
        Adopt(path);
    }

    /// <summary>Keeps the interior points of an edited path as the hand-placed route.</summary>
    private void Adopt(IReadOnlyList<Point> path)
    {
        var tidy = ConnectorRouter.Simplify(path).ToList();

        Waypoints = tidy.Count > 2
            ? tidy.GetRange(1, tidy.Count - 2)
            : [];
    }

    #endregion

    /// <summary>Throws away hand-placed bends and lets the connector route itself again.</summary>
    public void ResetRoute()
    {
        LabelOffset = default;
        Waypoints = [];
        _route = [];
        _routeKey = 0;
    }

    /// <summary>The line as drawn: hand-placed bends, else the route, else end to end.</summary>
    public IReadOnlyList<Point> Path
    {
        get
        {
            if (HasManualRoute)
            {
                var path = new List<Point> { ResolvedStart };
                path.AddRange(Waypoints);
                path.Add(ResolvedEnd);
                return path;
            }

            return _route.Count >= 2 ? _route : [ResolvedStart, ResolvedEnd];
        }
    }

    /// <summary>
    /// Recomputes the route if anything it depends on has moved. The key covers both ends and
    /// every obstacle, so a drag only pays for the search when the geometry actually changed.
    /// </summary>
    public void UpdateRoute(
        IReadOnlyList<DiagramShape> obstacles, double clearance,
        IReadOnlyList<(Point A, Point B)>? taken = null)
    {
        if (HasManualRoute)
            return;

        if (Routing == ConnectorRouting.Straight)
        {
            _route = [];
            _routeKey = 0;
            return;
        }

        var start = ResolvedStart;
        var end = ResolvedEnd;

        var key = new HashCode();
        key.Add(start);
        key.Add(end);
        key.Add(StartPort);
        key.Add(EndPort);

        foreach (var obstacle in obstacles)
            key.Add(obstacle.Bounds);

        // The routes already laid down are part of what this one depends on, so they belong in
        // the key: move the connector above and this one has to think again.
        if (taken is not null)
        {
            foreach (var (a, b) in taken)
            {
                key.Add(a);
                key.Add(b);
            }
        }

        var hash = key.ToHashCode();

        if (hash == _routeKey && _route.Count >= 2)
            return;

        _routeKey = hash;

        var rects = obstacles
            .Where(shape => !ReferenceEquals(shape, StartShape) && !ReferenceEquals(shape, EndShape))
            .Select(shape => shape.Bounds)
            .ToList();

        _route = ConnectorRouter.Route(
            start, StartDirection, end, EndDirection, rects, clearance, taken);
    }

    public ConnectorShape(Point start, Point end) : base(new Rect(start, end))
    {
        Start = start;
        End = end;
        Fill = Colors.Transparent;
        Stroke = DefaultStroke;
    }

    /// <summary>Where the line actually starts once gluing and edge clipping are applied.</summary>
    public Point ResolvedStart =>
        Resolve(StartShape, EffectiveStartPort, Start, AnchorOf(EndShape, EndPort, End));

    public Point ResolvedEnd =>
        Resolve(EndShape, EffectiveEndPort, End, AnchorOf(StartShape, StartPort, Start));

    /// <summary>The direction the line leaves each end, used when routing around obstacles.</summary>
    public Vector StartDirection => Direction(StartShape, EffectiveStartPort, ResolvedStart);

    public Vector EndDirection => Direction(EndShape, EffectiveEndPort, ResolvedEnd);

    /// <summary>
    /// The connection point actually used, which is the opposite of the chosen one when the
    /// chosen one faces away from the other end.
    ///
    /// A connector pinned to the bottom of one shape and the top of another looks right until
    /// the shapes swap places - reordering a lane will do it - and then each line has to leave
    /// its shape, doubling back across it, to reach the other. Flipping to the opposite point
    /// keeps the line outside both shapes. Nothing is written back: the chosen point is still
    /// the chosen one, so putting the shapes back the way they were restores the original.
    /// </summary>
    private int EffectiveStartPort => FacingPort(StartShape, StartPort, AnchorOf(EndShape, EndPort, End));

    private int EffectiveEndPort => FacingPort(EndShape, EndPort, AnchorOf(StartShape, StartPort, Start));

    private static int FacingPort(DiagramShape? glued, int port, Point toward)
    {
        if (glued is null || port < 0)
            return port;

        var points = glued.ConnectionPoints;

        // Only the plain four-point layout has a meaningful opposite.
        if (port >= points.Count || points.Count != DiagramShape.ConnectionDirections.Length)
            return port;

        var outward = glued.ConnectionDirection(port);
        var away = new Vector(toward.X - points[port].X, toward.Y - points[port].Y);

        // Facing the other end, or square on to it: leave it alone.
        if (outward.X * away.X + outward.Y * away.Y >= 0)
            return port;

        var opposite = (port + points.Count / 2) % points.Count;
        return opposite;
    }

    private static Point AnchorOf(DiagramShape? glued, int port, Point free)
    {
        if (glued is null)
            return free;

        return Port(glued, port) ?? glued.Bounds.Center;
    }

    private static Point Resolve(DiagramShape? glued, int port, Point free, Point toward)
    {
        if (glued is null)
            return free;

        return Port(glued, port) ?? ClipToRect(glued.Bounds, toward);
    }

    /// <summary>The connection point at that index, or null when the end is not pinned to one.</summary>
    private static Point? Port(DiagramShape shape, int port)
    {
        var points = shape.ConnectionPoints;
        return port >= 0 && port < points.Count ? points[port] : null;
    }

    private static Vector Direction(DiagramShape? glued, int port, Point resolved)
    {
        if (glued is null)
            return default;

        if (port >= 0)
            return glued.ConnectionDirection(port);

        // Not pinned: leave the shape the way the line already points.
        var centre = glued.Bounds.Center;
        var dx = resolved.X - centre.X;
        var dy = resolved.Y - centre.Y;

        return Math.Abs(dx) > Math.Abs(dy)
            ? new Vector(Math.Sign(dx), 0)
            : new Vector(0, Math.Sign(dy));
    }

    /// <summary>Walks from the centre of the rectangle toward a point and stops at the edge.</summary>
    private static Point ClipToRect(Rect rect, Point toward)
    {
        var centre = rect.Center;
        var dx = toward.X - centre.X;
        var dy = toward.Y - centre.Y;

        if (Math.Abs(dx) < 1e-6 && Math.Abs(dy) < 1e-6)
            return centre;

        var scaleX = Math.Abs(dx) > 1e-6 ? rect.Width / 2 / Math.Abs(dx) : double.PositiveInfinity;
        var scaleY = Math.Abs(dy) > 1e-6 ? rect.Height / 2 / Math.Abs(dy) : double.PositiveInfinity;
        var scale = Math.Min(Math.Min(scaleX, scaleY), 1);

        return new Point(centre.X + dx * scale, centre.Y + dy * scale);
    }

    /// <summary>
    /// Derived from the end points, so it moves on its own whenever a shape this connector
    /// is glued to moves. Assigning it translates the connector by the difference.
    /// </summary>
    public override Rect Bounds
    {
        get
        {
            var path = Path;
            var bounds = new Rect(path[0], path[^1]);

            for (var i = 1; i < path.Count - 1; i++)
                bounds = bounds.Union(new Rect(path[i], path[i]));

            return bounds;
        }
        set
        {
            var current = new Rect(ResolvedStart, ResolvedEnd);
            Translate(new Vector(value.X - current.X, value.Y - current.Y));
        }
    }

    /// <summary>
    /// Only free ends move. An end glued to a shape is positioned by that shape, so
    /// translating it as well would move it twice when the shape is moving too, and would
    /// tear it off the shape when it is not.
    /// </summary>
    public override void Translate(Vector delta)
    {
        if (StartShape is null)
            Start += delta;

        if (EndShape is null)
            End += delta;
    }

    public override Geometry CreateGeometry()
    {
        var path = Path;
        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            context.BeginFigure(path[0], false);

            for (var i = 1; i < path.Count; i++)
                context.LineTo(path[i]);

            context.EndFigure(false);
        }

        return geometry;
    }

    protected override bool HitTestUpright(Point point) => HitTestUpright(point, HitTolerance);

    protected override bool HitTestUpright(Point point, double slack)
    {
        var tolerance = Math.Max(slack, HitTolerance);
        var path = Path;

        for (var i = 0; i < path.Count - 1; i++)
        {
            if (DistanceToSegment(point, path[i], path[i + 1]) <= tolerance)
                return true;
        }

        return false;
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;

        if (lengthSquared < 1e-9)
            return Distance(p, a);

        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0, 1);
        return Distance(p, new Point(a.X + t * dx, a.Y + t * dy));
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    #region Rendering

    protected override void Draw(DrawingContext context, bool withText)
    {
        var path = Path.ToList();

        // Each cap points along the segment it terminates, and the shaft is pulled back so it
        // does not show through a filled one.
        var startAxis = Direction(path[1], path[0]);
        var endAxis = Direction(path[^1], path[^2]);

        if (startAxis is null || endAxis is null)
            return;

        var (sx, sy) = startAxis.Value;
        var (ex, ey) = endAxis.Value;

        path[0] = Offset(path[0], sx, sy, Inset(StartCap));
        path[^1] = Offset(path[^1], -ex, -ey, Inset(EndCap));

        var pen = CreatePen();

        if (pen is Pen concrete)
            concrete.LineJoin = PenLineJoin.Round;

        foreach (var (from, to) in VisibleSegments(path))
            context.DrawLine(pen, from, to);

        RenderCap(context, StartCap, ResolvedStart, -sx, -sy, pen);
        RenderCap(context, EndCap, ResolvedEnd, ex, ey, pen);

        if (withText)
            RenderText(context);
    }

    /// <summary>
    /// Where the label has been dragged to, from where it would otherwise sit. Kept as an
    /// offset rather than a position so the label follows the line as it re-routes: move
    /// either shape and the words stay the same distance from the run they belong to.
    /// </summary>
    public Vector LabelOffset { get; set; }

    /// <summary>How much clear space is left round the words where the line is broken.</summary>
    private const double LabelPadding = 5;

    /// <summary>
    /// The label sits in the middle of the longest straight run rather than halfway along the
    /// whole line. Halfway along is very often a corner - on a route that goes out, across and
    /// down, the middle of the journey is the bend - and a label wrapped round a corner reads
    /// as belonging to neither part.
    /// </summary>
    protected override Rect TextArea
    {
        get
        {
            var anchor = LabelAnchor;
            var size = LabelSize;

            return new Rect(
                anchor.X - size.Width / 2,
                anchor.Y - size.Height / 2,
                size.Width,
                size.Height);
        }
    }

    public Point LabelAnchor
    {
        get
        {
            var middle = LongestRun();
            return new Point(middle.X + LabelOffset.X, middle.Y + LabelOffset.Y);
        }
    }

    /// <summary>Just big enough for the words, so the gap in the line is no wider than it need be.</summary>
    private Size LabelSize
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Text))
                return default;

            var lines = Text.Replace("\r\n", "\n").Split('\n');
            var width = lines.Max(line => Format(line).Width);

            // Room for the padding the text renderer keeps inside a shape as well as for the
            // gap, or the words would be wrapped again inside the box measured to hold them.
            return new Size(
                width + (TextPadding + LabelPadding) * 2 + 1,
                lines.Length * LineHeight + LabelPadding);
        }
    }

    /// <summary>The middle of the longest straight run of the route.</summary>
    private Point LongestRun()
    {
        var path = Path;
        var best = -1.0;
        var middle = path[^1];

        for (var i = 0; i < path.Count - 1; i++)
        {
            var length = Distance(path[i], path[i + 1]);

            if (length <= best)
                continue;

            best = length;
            middle = new Point((path[i].X + path[i + 1].X) / 2, (path[i].Y + path[i + 1].Y) / 2);
        }

        return middle;
    }

    /// <summary>
    /// The line as drawn: the route, with a gap cut out of it where the label sits. Cutting a
    /// gap rather than painting a panel behind the words means the label reads over whatever
    /// is behind it - a coloured lane, another shape - instead of punching a white hole in it.
    /// </summary>
    private IEnumerable<(Point From, Point To)> VisibleSegments(IReadOnlyList<Point> path)
    {
        var gap = string.IsNullOrWhiteSpace(Text) ? default : TextArea;

        for (var i = 0; i < path.Count - 1; i++)
        {
            foreach (var piece in Outside(path[i], path[i + 1], gap))
                yield return piece;
        }
    }

    /// <summary>The parts of a segment that lie outside the rectangle, in order along it.</summary>
    private static IEnumerable<(Point From, Point To)> Outside(Point from, Point to, Rect gap)
    {
        if (gap.Width <= 0 || gap.Height <= 0)
        {
            yield return (from, to);
            yield break;
        }

        // Where the segment enters and leaves the box, as a fraction of its length.
        var enter = 0.0;
        var leave = 1.0;

        foreach (var (delta, from1, low, high) in new[]
                 {
                     (to.X - from.X, from.X, gap.Left, gap.Right),
                     (to.Y - from.Y, from.Y, gap.Top, gap.Bottom)
                 })
        {
            if (Math.Abs(delta) < 1e-9)
            {
                // Parallel to this pair of edges: either always between them or never.
                if (from1 < low || from1 > high)
                {
                    yield return (from, to);
                    yield break;
                }

                continue;
            }

            var a = (low - from1) / delta;
            var b = (high - from1) / delta;

            enter = Math.Max(enter, Math.Min(a, b));
            leave = Math.Min(leave, Math.Max(a, b));
        }

        if (enter >= leave)
        {
            yield return (from, to);
            yield break;
        }

        Point At(double t) => new(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);

        if (enter > 0)
            yield return (from, At(enter));

        if (leave < 1)
            yield return (At(leave), to);
    }

    /// <summary>Unit vector from <paramref name="from"/> to <paramref name="to"/>, or null for a zero-length line.</summary>
    private static (double X, double Y)? Direction(Point to, Point from)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);

        return length < 1e-6 ? null : (dx / length, dy / length);
    }

    private static Point Offset(Point point, double ux, double uy, double distance) =>
        new(point.X + ux * distance, point.Y + uy * distance);

    private static double Inset(EndCapStyle cap) => cap switch
    {
        EndCapStyle.Arrow or EndCapStyle.Diamond => CapLength,
        EndCapStyle.HollowArrow or EndCapStyle.HollowDiamond => CapLength,

        // The foot's toes touch the shape; the shaft stops at its ankle.
        EndCapStyle.CrowsFoot or EndCapStyle.CrowsFootOneOrMany or EndCapStyle.CrowsFootZeroOrMany
            => CapLength,

        _ => 0
    };

    /// <summary>Draws one cap at <paramref name="tip"/>, pointing along the outward vector.</summary>
    private void RenderCap(DrawingContext context, EndCapStyle cap, Point tip, double ux, double uy, IPen pen)
    {
        if (cap == EndCapStyle.None)
            return;

        var brush = new SolidColorBrush(Stroke);

        switch (cap)
        {
            case EndCapStyle.Dot:
                context.DrawEllipse(brush, null, tip, DotRadius, DotRadius);
                return;

            case EndCapStyle.Arrow:
                context.DrawGeometry(brush, null, Polygon(ArrowPoints(tip, ux, uy)));
                return;

            case EndCapStyle.Diamond:
                context.DrawGeometry(brush, null, Polygon(DiamondPoints(tip, ux, uy)));
                return;

            case EndCapStyle.OpenArrow:
                var (left, right) = OpenArrowArms(tip, ux, uy);
                context.DrawLine(pen, tip, left);
                context.DrawLine(pen, tip, right);
                return;

            // Hollow caps are closed but unfilled. The shaft already stops at the base, so
            // nothing shows through, and they read correctly on any background.
            case EndCapStyle.HollowArrow:
                context.DrawGeometry(null, pen, Polygon(ArrowPoints(tip, ux, uy)));
                return;

            case EndCapStyle.HollowDiamond:
                context.DrawGeometry(null, pen, Polygon(DiamondPoints(tip, ux, uy)));
                return;

            case EndCapStyle.CrowsFoot:
                DrawFoot(context, pen, tip, ux, uy);
                return;

            case EndCapStyle.CrowsFootOne:
                DrawBar(context, pen, tip, ux, uy, CapLength);
                return;

            case EndCapStyle.CrowsFootZeroOrOne:
                DrawBar(context, pen, tip, ux, uy, CapLength);
                DrawRing(context, pen, tip, ux, uy, CapLength * 2);
                return;

            case EndCapStyle.CrowsFootOneOrMany:
                DrawFoot(context, pen, tip, ux, uy);
                DrawBar(context, pen, tip, ux, uy, CapLength * 1.7);
                return;

            case EndCapStyle.CrowsFootZeroOrMany:
                DrawFoot(context, pen, tip, ux, uy);
                DrawRing(context, pen, tip, ux, uy, CapLength * 1.9);
                return;
        }
    }

    /// <summary>Three prongs spreading from the ankle out to the shape.</summary>
    private static void DrawFoot(DrawingContext context, IPen pen, Point tip, double ux, double uy)
    {
        var (ankle, toes) = FootPoints(tip, ux, uy);

        foreach (var toe in toes)
            context.DrawLine(pen, ankle, toe);
    }

    /// <summary>A bar across the line, the "one" of an entity-relationship end.</summary>
    private static void DrawBar(DrawingContext context, IPen pen, Point tip, double ux, double uy, double back)
    {
        var (from, to) = BarPoints(tip, ux, uy, back);
        context.DrawLine(pen, from, to);
    }

    /// <summary>An unfilled ring, the "zero" of an entity-relationship end.</summary>
    private static void DrawRing(DrawingContext context, IPen pen, Point tip, double ux, double uy, double back)
    {
        var centre = Offset(tip, -ux, -uy, back);
        context.DrawEllipse(null, pen, centre, DotRadius, DotRadius);
    }

    private static (Point Ankle, Point[] Toes) FootPoints(Point tip, double ux, double uy)
    {
        var ankle = new Point(tip.X - ux * CapLength, tip.Y - uy * CapLength);
        var spread = CapHalfWidth * 1.3;

        return (ankle,
        [
            tip,
            new Point(tip.X - uy * spread, tip.Y + ux * spread),
            new Point(tip.X + uy * spread, tip.Y - ux * spread)
        ]);
    }

    private static (Point From, Point To) BarPoints(Point tip, double ux, double uy, double back)
    {
        var centre = new Point(tip.X - ux * back, tip.Y - uy * back);

        return (
            new Point(centre.X - uy * CapHalfWidth, centre.Y + ux * CapHalfWidth),
            new Point(centre.X + uy * CapHalfWidth, centre.Y - ux * CapHalfWidth));
    }

    private static Geometry Polygon(Point[] points)
    {
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

    private static Point[] ArrowPoints(Point tip, double ux, double uy)
    {
        var baseCentre = new Point(tip.X - ux * CapLength, tip.Y - uy * CapLength);

        return
        [
            tip,
            new Point(baseCentre.X - uy * CapHalfWidth, baseCentre.Y + ux * CapHalfWidth),
            new Point(baseCentre.X + uy * CapHalfWidth, baseCentre.Y - ux * CapHalfWidth)
        ];
    }

    private static Point[] DiamondPoints(Point tip, double ux, double uy)
    {
        var middle = new Point(tip.X - ux * CapLength / 2, tip.Y - uy * CapLength / 2);
        var tail = new Point(tip.X - ux * CapLength, tip.Y - uy * CapLength);

        return
        [
            tip,
            new Point(middle.X - uy * CapHalfWidth, middle.Y + ux * CapHalfWidth),
            tail,
            new Point(middle.X + uy * CapHalfWidth, middle.Y - ux * CapHalfWidth)
        ];
    }

    private static (Point Left, Point Right) OpenArrowArms(Point tip, double ux, double uy)
    {
        var baseCentre = new Point(tip.X - ux * CapLength, tip.Y - uy * CapLength);

        return (
            new Point(baseCentre.X - uy * CapHalfWidth, baseCentre.Y + ux * CapHalfWidth),
            new Point(baseCentre.X + uy * CapHalfWidth, baseCentre.Y - ux * CapHalfWidth));
    }

    #endregion

    protected override string SvgBody()
    {
        var path = Path.ToList();

        var startAxis = Direction(path[1], path[0]);
        var endAxis = Direction(path[^1], path[^2]);

        if (startAxis is null || endAxis is null)
            return string.Empty;

        var (sx, sy) = startAxis.Value;
        var (ex, ey) = endAxis.Value;

        path[0] = Offset(path[0], sx, sy, Inset(StartCap));
        path[^1] = Offset(path[^1], -ex, -ey, Inset(EndCap));

        var stroke = $"stroke=\"{SvgPaint(Stroke)}\" stroke-width=\"{Num(StrokeThickness)}\"{SvgDash()}";

        var sb = new System.Text.StringBuilder();
        sb.Append("<g>");

        // Written as the same pieces the editor draws, so the gap the label sits in is in the
        // exported file too rather than only on screen.
        foreach (var (from, to) in VisibleSegments(path))
            sb.Append($"<line x1=\"{Num(from.X)}\" y1=\"{Num(from.Y)}\" " +
                      $"x2=\"{Num(to.X)}\" y2=\"{Num(to.Y)}\" {stroke} stroke-linecap=\"butt\" />");

        sb.Append(SvgCap(StartCap, ResolvedStart, -sx, -sy, stroke));
        sb.Append(SvgCap(EndCap, ResolvedEnd, ex, ey, stroke));
        sb.Append("</g>");

        return sb.ToString();
    }

    private string SvgCap(EndCapStyle cap, Point tip, double ux, double uy, string stroke)
    {
        switch (cap)
        {
            case EndCapStyle.None:
                return string.Empty;

            case EndCapStyle.Dot:
                return $"<circle cx=\"{Num(tip.X)}\" cy=\"{Num(tip.Y)}\" r=\"{Num(DotRadius)}\" fill=\"{ToHex(Stroke)}\" />";

            case EndCapStyle.Arrow:
                return $"<polygon points=\"{PointList(ArrowPoints(tip, ux, uy))}\" fill=\"{ToHex(Stroke)}\" />";

            case EndCapStyle.Diamond:
                return $"<polygon points=\"{PointList(DiamondPoints(tip, ux, uy))}\" fill=\"{ToHex(Stroke)}\" />";

            case EndCapStyle.OpenArrow:
                var (left, right) = OpenArrowArms(tip, ux, uy);
                return $"<path d=\"M {Num(left.X)},{Num(left.Y)} L {Num(tip.X)},{Num(tip.Y)} " +
                       $"L {Num(right.X)},{Num(right.Y)}\" {stroke} fill=\"none\" />";

            case EndCapStyle.HollowArrow:
                return $"<polygon points=\"{PointList(ArrowPoints(tip, ux, uy))}\" fill=\"none\" {stroke} />";

            case EndCapStyle.HollowDiamond:
                return $"<polygon points=\"{PointList(DiamondPoints(tip, ux, uy))}\" fill=\"none\" {stroke} />";

            case EndCapStyle.CrowsFoot:
                return SvgFoot(tip, ux, uy, stroke);

            case EndCapStyle.CrowsFootOne:
                return SvgBar(tip, ux, uy, CapLength, stroke);

            case EndCapStyle.CrowsFootZeroOrOne:
                return SvgBar(tip, ux, uy, CapLength, stroke) +
                       SvgRing(tip, ux, uy, CapLength * 2, stroke);

            case EndCapStyle.CrowsFootOneOrMany:
                return SvgFoot(tip, ux, uy, stroke) +
                       SvgBar(tip, ux, uy, CapLength * 1.7, stroke);

            case EndCapStyle.CrowsFootZeroOrMany:
                return SvgFoot(tip, ux, uy, stroke) +
                       SvgRing(tip, ux, uy, CapLength * 1.9, stroke);

            default:
                return string.Empty;
        }
    }

    private static string SvgFoot(Point tip, double ux, double uy, string stroke)
    {
        var (ankle, toes) = FootPoints(tip, ux, uy);
        var data = string.Join(" ", toes.Select(toe =>
            $"M {Num(ankle.X)},{Num(ankle.Y)} L {Num(toe.X)},{Num(toe.Y)}"));

        return $"<path d=\"{data}\" fill=\"none\" {stroke} />";
    }

    private static string SvgBar(Point tip, double ux, double uy, double back, string stroke)
    {
        var (from, to) = BarPoints(tip, ux, uy, back);

        return $"<line x1=\"{Num(from.X)}\" y1=\"{Num(from.Y)}\" " +
               $"x2=\"{Num(to.X)}\" y2=\"{Num(to.Y)}\" {stroke} />";
    }

    private static string SvgRing(Point tip, double ux, double uy, double back, string stroke)
    {
        var centre = Offset(tip, -ux, -uy, back);

        return $"<circle cx=\"{Num(centre.X)}\" cy=\"{Num(centre.Y)}\" " +
               $"r=\"{Num(DotRadius)}\" fill=\"none\" {stroke} />";
    }

    private static string PointList(Point[] points) =>
        string.Join(" ", Array.ConvertAll(points, p => $"{Num(p.X)},{Num(p.Y)}"));
}
