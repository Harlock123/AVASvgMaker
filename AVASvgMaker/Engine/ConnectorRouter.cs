using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;

namespace AVASvgMaker.Engine;

/// <summary>
/// Right-angled connector routing that keeps clear of the shapes already on the page.
///
/// Obstacles are inflated by the clearance, and a lattice is built from their edges, the
/// terminals, and the midpoints between those - which puts a candidate lane down every gap.
/// A* then walks the lattice, paying a penalty per corner so it prefers the straightest
/// route rather than merely the shortest one. Comparisons are strict, so a route is allowed
/// to run along an inflated edge: exactly the clearance away from the shape itself.
/// </summary>
public static class ConnectorRouter
{
    private const double Epsilon = 0.01;

    /// <summary>What a corner costs, in page units, relative to distance travelled.</summary>
    private const double BendPenalty = 40;

    /// <summary>
    /// What running alongside a connector that is already there costs, per page unit of
    /// company kept. A penalty rather than a prohibition: a corridor with no room for two
    /// lines is still better used than not reached at all.
    /// </summary>
    private const double CrowdingPenalty = 3;

    /// <summary>Beyond this many segments already on the page, crowding is not weighed at all.</summary>
    private const int CrowdedEnough = 400;

    /// <summary>Beyond this many obstacles the lattice is thinned to keep routing interactive.</summary>
    private const int DenseObstacleCount = 24;

    /// <summary>
    /// Routes from <paramref name="start"/> to <paramref name="end"/>. The directions are the
    /// way the line should leave each end - the outward normal of a connection point - and may
    /// be zero for an end that is not attached to anything.
    /// </summary>
    public static IReadOnlyList<Point> Route(
        Point start,
        Vector startDirection,
        Point end,
        Vector endDirection,
        IReadOnlyList<Rect> obstacles,
        double clearance,
        IReadOnlyList<(Point A, Point B)>? taken = null,
        IReadOnlyList<Rect>? terminals = null)
    {
        var blocked = Inflate(obstacles, clearance);

        // The shapes this connector is attached to. They cannot be obstacles like the rest -
        // the line has to begin and end on them - but everything between the two stubs must
        // still keep out, or the line turns straight back across the shape it just left.
        var ends = Inflate(terminals, clearance);
        var bare = Inflate(terminals, 0);

        // Shortened rather than left to land inside something. Two shapes closer together
        // than the clearance cannot both be left square on, and a stub that starts inside a
        // shape leaves the search nowhere to begin - which used to mean a route that ignored
        // every shape on the page, including the two it was attached to.
        var stub = Math.Max(clearance, 1);
        var from = Stub(start, startDirection, stub, blocked, bare);
        var to = Stub(end, endDirection, stub, blocked, bare);

        var lanes = taken is { Count: > 0 and <= CrowdedEnough } ? taken : null;

        // Three sets, in order of how well the result reads. Keeping the clearance from its
        // own shapes is best; touching them is acceptable and still never crosses them; and
        // only when neither can be routed at all is the old free-for-all better than nothing.
        List<List<Rect>> attempts =
        [
            Join(blocked, ends),
            Join(blocked, bare),
            blocked
        ];

        List<Point>? middle = null;
        var guard = blocked;

        foreach (var set in attempts)
        {
            middle = Search(from, startDirection, to, set, lanes, clearance);

            if (middle is null)
                continue;

            guard = set;
            break;
        }

        middle ??= Elbow(from, to, startDirection);

        var path = new List<Point> { start };

        foreach (var point in middle)
            path.Add(point);

        path.Add(end);

        var simplified = Simplify(path).ToList();
        Centre(simplified, blocked, guard);

        return Simplify(simplified);
    }

    private static List<Rect> Inflate(IReadOnlyList<Rect>? rects, double clearance) =>
        rects is null
            ? []
            : rects
                .Select(rect => clearance > 0 ? rect.Inflate(clearance) : rect)
                .Where(rect => rect.Width > 0 && rect.Height > 0)
                .ToList();

    private static List<Rect> Join(List<Rect> first, List<Rect> second) =>
        second.Count == 0 ? first : first.Concat(second).ToList();

    /// <summary>
    /// Steps out from a connection point by the stub, or by as much of it as lands clear of
    /// everything. Falls back to the point itself, which at least sits on its own outline.
    /// </summary>
    private static Point Stub(
        Point point, Vector direction, double distance, List<Rect> blocked, List<Rect> bare)
    {
        if (IsZero(direction))
            return point;

        const int steps = 8;

        for (var step = steps; step > 0; step--)
        {
            var candidate = Step(point, direction, distance * step / steps);

            if (!Inside(candidate, blocked) && !Inside(candidate, bare))
                return candidate;
        }

        return point;
    }

    private static Point Step(Point point, Vector direction, double distance) =>
        IsZero(direction) ? point : new Point(point.X + direction.X * distance, point.Y + direction.Y * distance);

    private static bool IsZero(Vector v) => Math.Abs(v.X) < Epsilon && Math.Abs(v.Y) < Epsilon;

    #region Lattice

    /// <summary>Candidate lines: the terminals, each obstacle edge, and a lane down every gap.</summary>
    private static double[] Axis(double a, double b, List<Rect> blocked, bool horizontal)
    {
        var values = new List<double> { a, b };

        foreach (var rect in blocked)
        {
            values.Add(horizontal ? rect.Left : rect.Top);
            values.Add(horizontal ? rect.Right : rect.Bottom);
        }

        values.Sort();

        var unique = new List<double>();
        foreach (var value in values)
        {
            if (unique.Count == 0 || value - unique[^1] > Epsilon)
                unique.Add(value);
        }

        // A lane between each pair of neighbours, so gaps between shapes are reachable.
        if (blocked.Count > DenseObstacleCount)
            return unique.ToArray();

        var withLanes = new List<double>();
        for (var i = 0; i < unique.Count; i++)
        {
            withLanes.Add(unique[i]);

            if (i + 1 < unique.Count && unique[i + 1] - unique[i] > Epsilon * 2)
                withLanes.Add((unique[i] + unique[i + 1]) / 2);
        }

        return withLanes.ToArray();
    }

    private static bool Inside(Point point, List<Rect> blocked) => blocked.Any(rect =>
        point.X > rect.Left + Epsilon && point.X < rect.Right - Epsilon &&
        point.Y > rect.Top + Epsilon && point.Y < rect.Bottom - Epsilon);

    /// <summary>True when an axis-aligned segment passes through any obstacle's interior.</summary>
    private static bool Crosses(Point a, Point b, List<Rect> blocked)
    {
        var minX = Math.Min(a.X, b.X);
        var maxX = Math.Max(a.X, b.X);
        var minY = Math.Min(a.Y, b.Y);
        var maxY = Math.Max(a.Y, b.Y);

        return blocked.Any(rect =>
            minX < rect.Right - Epsilon && maxX > rect.Left + Epsilon &&
            minY < rect.Bottom - Epsilon && maxY > rect.Top + Epsilon);
    }

    #endregion

    #region Search

    private static List<Point>? Search(
        Point from, Vector startDirection, Point to, List<Rect> blocked,
        IReadOnlyList<(Point A, Point B)>? taken, double spacing)
    {
        var xs = Axis(from.X, to.X, blocked, horizontal: true);
        var ys = Axis(from.Y, to.Y, blocked, horizontal: false);

        var startX = IndexOf(xs, from.X);
        var startY = IndexOf(ys, from.Y);
        var goalX = IndexOf(xs, to.X);
        var goalY = IndexOf(ys, to.Y);

        if (startX < 0 || startY < 0 || goalX < 0 || goalY < 0)
            return null;

        var width = xs.Length;
        var height = ys.Length;

        Point At(int x, int y) => new(xs[x], ys[y]);

        // State is a node plus the direction it was entered from, so corners can be charged.
        var best = new Dictionary<int, double>();
        var cameFrom = new Dictionary<int, int>();
        var queue = new PriorityQueue<int, double>();

        int Key(int x, int y, int axis) => (y * width + x) * 2 + axis;

        double Heuristic(int x, int y) => Math.Abs(xs[x] - to.X) + Math.Abs(ys[y] - to.Y);

        // Leaving along the stub costs nothing; turning immediately costs a corner.
        var horizontalStart = Math.Abs(startDirection.X) > Math.Abs(startDirection.Y);

        for (var axis = 0; axis < 2; axis++)
        {
            var cost = IsZero(startDirection) || (axis == 0) == horizontalStart ? 0 : BendPenalty;
            var key = Key(startX, startY, axis);

            best[key] = cost;
            queue.Enqueue(key, cost + Heuristic(startX, startY));
        }

        var goal = -1;

        while (queue.TryDequeue(out var current, out _))
        {
            var axisOf = current % 2;
            var node = current / 2;
            var x = node % width;
            var y = node / width;

            if (x == goalX && y == goalY)
            {
                goal = current;
                break;
            }

            var cost = best[current];

            for (var axis = 0; axis < 2; axis++)
            {
                for (var step = -1; step <= 1; step += 2)
                {
                    var nx = axis == 0 ? x + step : x;
                    var ny = axis == 1 ? y + step : y;

                    if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                        continue;

                    var a = At(x, y);
                    var b = At(nx, ny);

                    if (Inside(b, blocked) || Crosses(a, b, blocked))
                        continue;

                    var length = Math.Abs(b.X - a.X) + Math.Abs(b.Y - a.Y);
                    var next = cost + length + (axis == axisOf ? 0 : BendPenalty)
                               + Shared(a, b, taken, spacing) * CrowdingPenalty;
                    var key = Key(nx, ny, axis);

                    if (best.TryGetValue(key, out var known) && known <= next + Epsilon)
                        continue;

                    best[key] = next;
                    cameFrom[key] = current;
                    queue.Enqueue(key, next + Heuristic(nx, ny));
                }
            }
        }

        if (goal < 0)
            return null;

        var path = new List<Point>();

        for (var key = goal; ; )
        {
            var node = key / 2;
            path.Add(At(node % width, node / width));

            if (!cameFrom.TryGetValue(key, out var previous))
                break;

            key = previous;
        }

        path.Reverse();
        return path;
    }

    private static int IndexOf(double[] values, double value)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (Math.Abs(values[i] - value) < Epsilon)
                return i;
        }

        return -1;
    }

    #endregion

    #region Centring

    /// <summary>
    /// A* is free to pick any of several equally cheap routes, and tends to return one whose
    /// bends hug an end. This slides each interior segment sideways toward the midpoint
    /// between its neighbours - as far as the obstacles allow - so the jog sits in the middle
    /// of the gap it crosses, which is what the eye expects.
    /// </summary>
    /// <param name="blocked">What the segments joining this one to its neighbours must clear.</param>
    /// <param name="all">
    /// The same, plus the shapes at either end. The segment being moved is held to the
    /// stricter set: it is free to slide, so it is the one that can slide into a shape. The
    /// joining segments are not, and are the stubs themselves at the two ends of the path -
    /// they touch the shape by design, and holding them to it would stop any centring at all.
    /// </param>
    private static void Centre(List<Point> path, List<Rect> blocked, List<Rect> all)
    {
        for (var i = 1; i + 2 < path.Count; i++)
        {
            var a = path[i];
            var b = path[i + 1];
            var before = path[i - 1];
            var after = path[i + 2];

            if (Math.Abs(a.X - b.X) < Epsilon)
            {
                var ideal = (before.X + after.X) / 2;
                var value = Slide(ideal, a.X, candidate =>
                    Clear(new Point(candidate, a.Y), new Point(candidate, b.Y), all) &&
                    Clear(before, new Point(candidate, a.Y), blocked) &&
                    Clear(new Point(candidate, b.Y), after, blocked));

                path[i] = new Point(value, a.Y);
                path[i + 1] = new Point(value, b.Y);
            }
            else if (Math.Abs(a.Y - b.Y) < Epsilon)
            {
                var ideal = (before.Y + after.Y) / 2;
                var value = Slide(ideal, a.Y, candidate =>
                    Clear(new Point(a.X, candidate), new Point(b.X, candidate), all) &&
                    Clear(before, new Point(a.X, candidate), blocked) &&
                    Clear(new Point(b.X, candidate), after, blocked));

                path[i] = new Point(a.X, value);
                path[i + 1] = new Point(b.X, value);
            }
        }
    }

    /// <summary>Takes the ideal position if it is clear, otherwise the closest one that is.</summary>
    private static double Slide(double ideal, double current, Func<double, bool> isClear)
    {
        if (Math.Abs(ideal - current) < Epsilon)
            return current;

        if (isClear(ideal))
            return ideal;

        const int steps = 12;

        for (var step = 1; step <= steps; step++)
        {
            var candidate = ideal + (current - ideal) * step / steps;

            if (isClear(candidate))
                return candidate;
        }

        return current;
    }

    private static bool Clear(Point a, Point b, List<Rect> blocked) => !Crosses(a, b, blocked);

    /// <summary>
    /// How far a candidate run keeps company with the connectors already on the page: the
    /// length it spends beside one of them, near enough and parallel enough to read as the
    /// same line. Crossings are not counted - two lines at right angles are only a crossing,
    /// and unavoidable - so only lines along the same axis are measured.
    /// </summary>
    private static double Shared(Point a, Point b, IReadOnlyList<(Point A, Point B)>? taken, double spacing)
    {
        if (taken is null)
            return 0;

        var horizontal = Math.Abs(a.Y - b.Y) < Epsilon;
        var total = 0.0;

        foreach (var (c, d) in taken)
        {
            if (horizontal)
            {
                if (Math.Abs(c.Y - d.Y) >= Epsilon || Math.Abs(c.Y - a.Y) > spacing)
                    continue;

                total += Overlap(a.X, b.X, c.X, d.X);
            }
            else
            {
                if (Math.Abs(c.X - d.X) >= Epsilon || Math.Abs(c.X - a.X) > spacing)
                    continue;

                total += Overlap(a.Y, b.Y, c.Y, d.Y);
            }
        }

        return total;
    }

    private static double Overlap(double a1, double a2, double b1, double b2) => Math.Max(0,
        Math.Min(Math.Max(a1, a2), Math.Max(b1, b2)) - Math.Max(Math.Min(a1, a2), Math.Min(b1, b2)));

    #endregion

    /// <summary>The fallback when no route exists: a plain elbow, ignoring what is in the way.</summary>
    private static List<Point> Elbow(Point from, Point to, Vector startDirection)
    {
        if (Math.Abs(from.X - to.X) < Epsilon || Math.Abs(from.Y - to.Y) < Epsilon)
            return [from, to];

        // Turn away from the end the line leaves first.
        return Math.Abs(startDirection.Y) > Math.Abs(startDirection.X)
            ? [from, new Point(from.X, to.Y), to]
            : [from, new Point(to.X, from.Y), to];
    }

    /// <summary>Drops duplicate points and any that sit on the line between their neighbours.</summary>
    public static IReadOnlyList<Point> Simplify(IReadOnlyList<Point> points) => Simplify(points.ToList());

    private static IReadOnlyList<Point> Simplify(List<Point> path)
    {
        var result = new List<Point>();

        foreach (var point in path)
        {
            if (result.Count > 0 && Math.Abs(point.X - result[^1].X) < Epsilon &&
                Math.Abs(point.Y - result[^1].Y) < Epsilon)
                continue;

            result.Add(point);
        }

        for (var i = result.Count - 2; i > 0; i--)
        {
            var before = result[i - 1];
            var here = result[i];
            var after = result[i + 1];

            var collinearX = Math.Abs(before.X - here.X) < Epsilon && Math.Abs(here.X - after.X) < Epsilon;
            var collinearY = Math.Abs(before.Y - here.Y) < Epsilon && Math.Abs(here.Y - after.Y) < Epsilon;

            if (collinearX || collinearY)
                result.RemoveAt(i);
        }

        return result;
    }
}
