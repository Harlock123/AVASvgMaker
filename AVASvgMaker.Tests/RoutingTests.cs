using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using Xunit;

namespace AVASvgMaker.Tests;

public class RoutingTests
{
    /// <summary>
    /// Two pairs close together with a wall between them. Both would rather go over the top
    /// than round the bottom, so both want the same lane above it.
    /// </summary>
    private static (DiagramDocument Document, ConnectorShape One, ConnectorShape Two) Crowded()
    {
        var document = Harness.Page(760, 460);

        var a1 = Harness.Box(document, new Rect(60, 100, 110, 40), "A1");
        var a2 = Harness.Box(document, new Rect(60, 170, 110, 40), "A2");
        var b1 = Harness.Box(document, new Rect(580, 100, 110, 40), "B1");
        var b2 = Harness.Box(document, new Rect(580, 170, 110, 40), "B2");
        Harness.Box(document, new Rect(340, 60, 70, 330), "wall");

        var one = Harness.Join(document, a1, 1, b1, 3);
        var two = Harness.Join(document, a2, 1, b2, 3);

        document.RouteConnectors();
        return (document, one, two);
    }

    [AvaloniaFact]
    public void TwoConnectorsDoNotShareOneCorridor()
    {
        var (document, one, two) = Crowded();

        Assert.True(Harness.Company(one, two, document.RouteClearance) < 40,
            "routed together they keep out of each other's way");

        // The control: the same page with each ignoring the other, which is what it used to do.
        var obstacles = document.Shapes.Where(shape => shape is not ConnectorShape).ToList();

        foreach (var connector in new[] { one, two })
        {
            connector.ResetRoute();
            connector.UpdateRoute(obstacles, document.RouteClearance);
        }

        Assert.True(Harness.Company(one, two, document.RouteClearance) > 150,
            "ignoring each other they would run along the same lane");
    }

    [AvaloniaFact]
    public void ThePageSettlesRatherThanChasingItself()
    {
        var (document, one, two) = Crowded();

        var first = Harness.Path(one);
        var second = Harness.Path(two);

        for (var i = 0; i < 5; i++)
            document.RouteConnectors();

        Assert.Equal(first, Harness.Path(one));
        Assert.Equal(second, Harness.Path(two));
    }

    [AvaloniaFact]
    public void AReloadedPageRoutesToTheSamePlace()
    {
        var (document, one, two) = Crowded();

        var expected = new[] { Harness.Path(one), Harness.Path(two) };

        var reloaded = DiagramFile.FromJson(DiagramFile.ToJson(document));
        reloaded.RouteConnectors();

        var actual = reloaded.Shapes.OfType<ConnectorShape>().Select(Harness.Path).ToArray();

        Assert.Equal(expected, actual);
    }

    [AvaloniaFact]
    public void ARouteStillKeepsOutOfTheShapes()
    {
        var (document, one, two) = Crowded();

        foreach (var connector in new[] { one, two })
        foreach (var (p, q) in Harness.Segments(connector))
        foreach (var shape in document.Shapes.Where(s => s is not ConnectorShape))
            Assert.False(Cuts(p, q, shape.Bounds), $"a route crosses {shape.Text}");
    }

    /// <summary>Bends that still clear everything are the user's, and are left alone.</summary>
    [AvaloniaFact]
    public void AHandPlacedRouteIsLeftAlone()
    {
        var (document, _, two) = Crowded();

        // Out to the side, down past the wall, across under it and back up: roundabout, which
        // is the point - it is a route someone chose, square throughout, crossing nothing.
        two.Waypoints =
        [
            new Point(240, 190), new Point(240, 430), new Point(500, 430), new Point(500, 190)
        ];
        var placed = Harness.Path(two);

        document.RouteConnectors();

        Assert.True(two.HasManualRoute);
        Assert.Equal(placed, Harness.Path(two));
    }

    /// <summary>
    /// Bends that have stopped clearing everything are not. A hand-placed route is a route
    /// through a page that has since changed, and once it runs through a shape it is no
    /// longer the route anybody asked for - so it goes, and the connector routes itself.
    /// </summary>
    [AvaloniaFact]
    public void BendsGoOnceTheyCutThroughSomething()
    {
        var (document, _, two) = Crowded();

        // Straight at the wall.
        two.Waypoints = [new Point(400, 430)];

        document.RouteConnectors();

        Assert.False(two.HasManualRoute, "the bends should have gone");

        foreach (var (p, q) in Harness.Segments(two))
        foreach (var shape in document.Shapes.Where(s => s is not ConnectorShape))
            Assert.False(Cuts(p, q, shape.Bounds), $"the route it found crosses {shape.Text}");
    }

    /// <summary>
    /// The other way bends stop making sense. A route can end up clear of every shape and
    /// still be wrong: an end moves, the bends behind it do not, and the segment joining them
    /// comes out slanted. Nobody can draw that on purpose, so it is not left standing.
    /// </summary>
    [AvaloniaFact]
    public void BendsGoOnceTheyStopTurningSquareCorners()
    {
        var document = Harness.Page(900, 700);
        var a = Harness.Box(document, new Rect(60, 100, 110, 40), "A");
        var b = Harness.Box(document, new Rect(600, 100, 110, 40), "B");

        var connector = Harness.Join(document, a, 1, b, 3);

        // Square corners, clear of both shapes, out to the side and over the top.
        connector.Waypoints =
        [
            new Point(300, 120), new Point(300, 40), new Point(600, 40)
        ];

        document.RouteConnectors();

        Assert.True(connector.HasManualRoute, "as placed the bends are square and clear");

        // A drops away. It leaves by its own right-hand side still, so nothing is crossed -
        // but the run from there to the first bend is now a slant.
        a.Bounds = new Rect(60, 300, 110, 40);
        document.RouteConnectors();

        Assert.False(connector.HasManualRoute, "the bends should have gone");

        foreach (var (p, q) in Harness.Segments(connector))
        {
            Assert.True(Math.Abs(p.X - q.X) < 0.01 || Math.Abs(p.Y - q.Y) < 0.01,
                "the route it found still slants");
        }
    }

    /// <summary>
    /// The shape has to actually move first. Bends are not re-examined on every repaint, both
    /// because it would cost something and because a route that was fine a moment ago and has
    /// not been disturbed is still fine.
    /// </summary>
    [AvaloniaFact]
    public void BendsSurviveARepaintThatChangesNothing()
    {
        var (document, _, two) = Crowded();

        two.Waypoints =
        [
            new Point(240, 190), new Point(240, 430), new Point(500, 430), new Point(500, 190)
        ];

        var placed = Harness.Path(two);

        for (var i = 0; i < 5; i++)
            document.RouteConnectors();

        Assert.True(two.HasManualRoute);
        Assert.Equal(placed, Harness.Path(two));
    }

    [AvaloniaFact]
    public void ACrowdOfConnectorsStillRoutesQuickly()
    {
        var document = Harness.Page(1600, 1200);

        var left = Enumerable.Range(0, 12)
            .Select(i => Harness.Box(document, new Rect(80, 60 + i * 90, 90, 44)))
            .ToArray();

        var right = Enumerable.Range(0, 12)
            .Select(i => Harness.Box(document, new Rect(1200, 60 + i * 90, 90, 44)))
            .ToArray();

        for (var i = 0; i < 12; i++)
            Harness.Join(document, left[i], 1, right[11 - i], 3);

        var first = System.Diagnostics.Stopwatch.StartNew();
        document.RouteConnectors();
        first.Stop();

        Assert.True(first.ElapsedMilliseconds < 8000, $"took {first.ElapsedMilliseconds} ms");

        // Nothing moved, so nothing should be searched again.
        var again = System.Diagnostics.Stopwatch.StartNew();
        document.RouteConnectors();
        again.Stop();

        Assert.True(again.ElapsedMilliseconds < 250, $"re-routing cost {again.ElapsedMilliseconds} ms");
    }


    /// <summary>
    /// The shapes at either end are shapes too. A route used to be free to cross them - they
    /// were left out of the obstacles altogether, on the grounds that the line has to start
    /// and finish on them - and the centring step would then slide a segment off the stub and
    /// straight back through the shape it had just left.
    ///
    /// Swept rather than staged, because the geometry that showed this up is not one anybody
    /// would have sat down and written: it takes a particular arrangement of a particular
    /// number of shapes before the centring has anywhere worth sliding to.
    /// </summary>
    [AvaloniaFact]
    public void ARouteNeverCrossesTheShapesItJoins()
    {
        var random = new Random(20260915);
        var crossings = 0;
        var checkedPages = 0;

        for (var trial = 0; trial < 120; trial++)
        {
            var document = Harness.Page(1200, 900);
            var boxes = new List<DiagramShape>();

            // Laid out clear of one another: two shapes closer together than the clearance
            // cannot both be left square on, and that is a different problem.
            void Place(DiagramShape? moving)
            {
                for (var attempt = 0; attempt < 200; attempt++)
                {
                    var at = new Rect(random.Next(60, 1000), random.Next(60, 760), 110, 44);

                    if (boxes.Any(other => !ReferenceEquals(other, moving) &&
                                           other.Bounds.Inflate(24).Intersects(at)))
                        continue;

                    if (moving is null)
                        boxes.Add(Harness.Box(document, at, $"S{boxes.Count}"));
                    else
                        moving.Bounds = at;

                    return;
                }
            }

            for (var i = 0; i < 6; i++)
                Place(null);

            var joins = new List<ConnectorShape>();

            for (var i = 0; i < 5; i++)
            {
                var from = boxes[random.Next(boxes.Count)];
                var to = boxes[random.Next(boxes.Count)];

                if (!ReferenceEquals(from, to))
                    joins.Add(Harness.Join(document, from, random.Next(4), to, random.Next(4)));
            }

            document.RouteConnectors();

            // The move is the point of the exercise: this is what a drag does.
            Place(boxes[random.Next(boxes.Count)]);
            document.RouteConnectors();
            checkedPages++;

            foreach (var connector in joins)
            foreach (var (p, q) in Harness.Segments(connector))
            foreach (var shape in boxes)
            {
                if (Cuts(p, q, shape.Bounds))
                    crossings++;
            }
        }

        Assert.Equal(120, checkedPages);
        Assert.Equal(0, crossings);
    }

    /// <summary>
    /// The smallest arrangement the sweep turned up, kept as itself so a failure says what
    /// broke rather than only that something did. Two shapes, one above the other, joined
    /// left to right - so both ends face away and both are re-faced.
    ///
    /// It used to come out as (759,471) (696,471) (696,234) (632,234): out of the right of
    /// the lower shape, back across the whole width of it, up, and then back across the upper
    /// one to reach its left side. Both shapes crossed end to end by the line attached to them.
    /// </summary>
    [AvaloniaFact]
    public void ALineDoesNotTurnBackThroughTheShapeItLeft()
    {
        var document = Harness.Page(1200, 900);
        var lower = Harness.Box(document, new Rect(649, 449, 110, 44), "lower");
        var upper = Harness.Box(document, new Rect(632, 212, 110, 44), "upper");

        var connector = Harness.Join(document, lower, 3, upper, 1);
        document.RouteConnectors();

        foreach (var (p, q) in Harness.Segments(connector))
        {
            Assert.False(Cuts(p, q, lower.Bounds), "the line goes back through the lower shape");
            Assert.False(Cuts(p, q, upper.Bounds), "the line goes back through the upper shape");
        }
    }

    /// <summary>
    /// A shape that has been turned is still a shape to keep out of. Obstacles are described
    /// by the upright rectangle a shape occupies, and used to be tested as though that were
    /// where the shape was - so a turned shape was avoided in the wrong place, and the line
    /// went through where it actually was.
    ///
    /// The one that showed it up, kept as itself: the line leaves a shape turned most of the
    /// way round and cuts straight back across it.
    /// </summary>
    [AvaloniaFact]
    public void ALineDoesNotCutTheTurnedShapeItLeaves()
    {
        var document = Harness.Page(1000, 800);
        var turned = Harness.Box(document, new Rect(473, 187, 120, 60), "turned");
        var upright = Harness.Box(document, new Rect(297, 543, 120, 60), "upright");

        turned.Rotation = 238;

        var connector = Harness.Join(document, turned, 3, upright, 0);
        document.RouteConnectors();

        foreach (var (p, q) in Harness.Segments(connector))
        {
            Assert.False(Pierces(p, q, turned), "the line cuts the shape it leaves");
            Assert.False(Pierces(p, q, upright), "the line cuts the shape it arrives at");
        }
    }

    /// <summary>
    /// The same, swept. Shapes are turned to every sort of angle, laid out so that what they
    /// actually cover does not overlap, and then one of them is turned again - which is the
    /// move that used to leave lines lying across them.
    /// </summary>
    [AvaloniaFact]
    public void ARouteKeepsOutOfShapesThatHaveBeenTurned()
    {
        var random = new Random(4242);
        var crossings = 0;

        for (var trial = 0; trial < 80; trial++)
        {
            var document = Harness.Page(1200, 900);
            var boxes = new List<DiagramShape>();

            for (var i = 0; i < 5; i++)
            for (var attempt = 0; attempt < 400; attempt++)
            {
                var at = new Rect(random.Next(80, 980), random.Next(80, 720), 120, 60);
                var turn = random.Next(0, 4) == 0 ? 0 : random.Next(1, 360);

                // Kept apart by what they cover once turned, not by the upright box: two shapes
                // that overlap have no clean route between them, which is a different question.
                if (boxes.Any(other => Covers(other.Bounds, other.Rotation).Inflate(30)
                                           .Intersects(Covers(at, turn))))
                    continue;

                var shape = Harness.Box(document, at, $"S{boxes.Count}");
                shape.Rotation = turn;
                boxes.Add(shape);
                break;
            }

            var joins = new List<ConnectorShape>();

            for (var i = 0; i < 4; i++)
            {
                var from = boxes[random.Next(boxes.Count)];
                var to = boxes[random.Next(boxes.Count)];

                if (!ReferenceEquals(from, to))
                    joins.Add(Harness.Join(document, from, random.Next(4), to, random.Next(4)));
            }

            document.RouteConnectors();

            var moved = boxes[random.Next(boxes.Count)];

            for (var attempt = 0; attempt < 400; attempt++)
            {
                var turn = random.Next(0, 360);

                if (boxes.Any(other => !ReferenceEquals(other, moved) &&
                                       Covers(other.Bounds, other.Rotation).Inflate(30)
                                           .Intersects(Covers(moved.Bounds, turn))))
                    continue;

                moved.Rotation = turn;
                break;
            }

            document.RouteConnectors();

            foreach (var connector in joins)
            foreach (var (p, q) in Harness.Segments(connector))
            foreach (var shape in boxes)
            {
                if (Pierces(p, q, shape))
                    crossings++;
            }
        }

        Assert.Equal(0, crossings);
    }

    /// <summary>What a shape covers once turned, as an upright box: for keeping fixtures apart.</summary>
    private static Rect Covers(Rect box, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        var across = Math.Abs(Math.Cos(radians));
        var down = Math.Abs(Math.Sin(radians));
        var width = box.Width * across + box.Height * down;
        var height = box.Width * down + box.Height * across;

        return new Rect(box.Center.X - width / 2, box.Center.Y - height / 2, width, height);
    }

    /// <summary>
    /// Through the shape's own outline, turn and all, rather than the box it is described by.
    /// Sampled a whisker to either side of the line, so one running along an edge - which is
    /// where a line leaving a shape belongs - is not counted as passing through it.
    /// </summary>
    private static bool Pierces(Point a, Point b, DiagramShape shape)
    {
        var length = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));

        if (length < 2)
            return false;

        var across = new Vector(-(b.Y - a.Y) / length, (b.X - a.X) / length) * 0.75;

        for (var step = 1; step < 40; step++)
        {
            var t = step / 40.0;
            var at = new Point(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

            if (shape.HitTest(at + across) && shape.HitTest(at - across))
                return true;
        }

        return false;
    }

    /// <summary>True when a segment passes through a rectangle rather than touching its edge.</summary>
    private static bool Cuts(Point a, Point b, Rect rect)
    {
        var inner = rect.Deflate(0.5);

        if (inner.Width <= 0 || inner.Height <= 0)
            return false;

        if (Math.Abs(a.Y - b.Y) < 0.01)
            return a.Y > inner.Top && a.Y < inner.Bottom &&
                   Harness.Overlap(a.X, b.X, inner.Left, inner.Right) > 0.5;

        return a.X > inner.Left && a.X < inner.Right &&
               Harness.Overlap(a.Y, b.Y, inner.Top, inner.Bottom) > 0.5;
    }
}
