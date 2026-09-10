using System;
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
        {
            if (ReferenceEquals(shape, connector.StartShape) || ReferenceEquals(shape, connector.EndShape))
                continue;

            Assert.False(Cuts(p, q, shape.Bounds), $"a route crosses {shape.Text}");
        }
    }

    [AvaloniaFact]
    public void AHandPlacedRouteIsLeftAlone()
    {
        var (document, _, two) = Crowded();

        two.Waypoints = [new Point(400, 430)];
        var placed = Harness.Path(two);

        document.RouteConnectors();

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
