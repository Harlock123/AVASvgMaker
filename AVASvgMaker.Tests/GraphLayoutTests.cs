using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using Xunit;

namespace AVASvgMaker.Tests;

/// <summary>
/// Laying shapes out in layers by the connectors between them. The lines are the router's
/// business and are not placed here - these are about where the shapes end up.
/// </summary>
public class GraphLayoutTests
{
    private static DiagramShape Box(DiagramDocument document, double x, double y, string name) =>
        Harness.Box(document, new Rect(x, y, 120, 50), name);

    private static bool Lay(DiagramDocument document, LayoutFlow flow = LayoutFlow.Down) =>
        GraphLayout.Apply(
            document.Shapes.Where(s => s is not ConnectorShape).ToList(),
            document.Shapes.OfType<ConnectorShape>().ToList(),
            flow);

    /// <summary>Everything a shape points at sits further along the flow than the shape does.</summary>
    private static void AssertFlows(DiagramDocument document, LayoutFlow flow)
    {
        foreach (var line in document.Shapes.OfType<ConnectorShape>())
        {
            if (line.StartShape is not { } from || line.EndShape is not { } to || ReferenceEquals(from, to))
                continue;

            var a = flow == LayoutFlow.Down ? from.Bounds.Center.Y : from.Bounds.Center.X;
            var b = flow == LayoutFlow.Down ? to.Bounds.Center.Y : to.Bounds.Center.X;

            Assert.True(a < b, $"{from.Text} should come before {to.Text}");
        }
    }

    private static void AssertNoOverlaps(DiagramDocument document)
    {
        var shapes = document.Shapes.Where(s => s is not ConnectorShape).ToList();

        for (var i = 0; i < shapes.Count; i++)
        for (var j = i + 1; j < shapes.Count; j++)
        {
            Assert.False(shapes[i].Bounds.Intersects(shapes[j].Bounds),
                $"{shapes[i].Text} overlaps {shapes[j].Text}");
        }
    }

    [AvaloniaFact]
    public void AChainComesOutInOrder()
    {
        var document = Harness.Page(1200, 900);

        // Deliberately scattered, and not in reading order.
        var c = Box(document, 700, 120, "C");
        var a = Box(document, 200, 600, "A");
        var b = Box(document, 900, 400, "B");

        Harness.Join(document, a, 2, b, 0);
        Harness.Join(document, b, 2, c, 0);

        Assert.True(Lay(document));

        AssertFlows(document, LayoutFlow.Down);
        AssertNoOverlaps(document);

        // A chain is one shape per layer, so they line up.
        Assert.Equal(a.Bounds.Center.X, b.Bounds.Center.X, 1);
        Assert.Equal(b.Bounds.Center.X, c.Bounds.Center.X, 1);
    }

    [AvaloniaFact]
    public void ASplitPutsTheTwoSidesInOneLayer()
    {
        var document = Harness.Page(1200, 900);
        var a = Box(document, 100, 100, "A");
        var left = Box(document, 100, 300, "left");
        var right = Box(document, 100, 500, "right");
        var join = Box(document, 100, 700, "join");

        Harness.Join(document, a, 2, left, 0);
        Harness.Join(document, a, 2, right, 0);
        Harness.Join(document, left, 2, join, 0);
        Harness.Join(document, right, 2, join, 0);

        Assert.True(Lay(document));

        AssertFlows(document, LayoutFlow.Down);
        AssertNoOverlaps(document);

        // The two sides share a layer, so they sit level with one another.
        Assert.Equal(left.Bounds.Center.Y, right.Bounds.Center.Y, 1);
        Assert.NotEqual(left.Bounds.Center.X, right.Bounds.Center.X);

        // And the shape they both reach sits between them.
        var middle = (left.Bounds.Center.X + right.Bounds.Center.X) / 2;
        Assert.Equal(middle, join.Bounds.Center.X, 1);
    }

    /// <summary>
    /// A drawing with a loop in it has no first layer until the edge that closes the loop is
    /// turned round. This is here because getting it wrong is an app that hangs, not one that
    /// lays out badly.
    /// </summary>
    [AvaloniaFact]
    public void ALoopDoesNotHangOrCollapse()
    {
        var document = Harness.Page(1200, 900);
        var a = Box(document, 100, 100, "A");
        var b = Box(document, 100, 300, "B");
        var c = Box(document, 100, 500, "C");

        Harness.Join(document, a, 2, b, 0);
        Harness.Join(document, b, 2, c, 0);
        Harness.Join(document, c, 0, a, 2);

        Assert.True(Lay(document));

        AssertNoOverlaps(document);

        // Three layers still, with the back edge the only one running against the flow.
        var levels = new[] { a, b, c }.Select(s => s.Bounds.Center.Y).Distinct().Count();
        Assert.Equal(3, levels);
    }

    [AvaloniaFact]
    public void LinesThatWouldCrossArePulledApart()
    {
        var document = Harness.Page(1200, 900);

        // Two pairs, wired across each other: a1 reaches b2 and a2 reaches b1.
        var a1 = Box(document, 100, 100, "a1");
        var a2 = Box(document, 400, 100, "a2");
        var b1 = Box(document, 100, 400, "b1");
        var b2 = Box(document, 400, 400, "b2");

        Harness.Join(document, a1, 2, b2, 0);
        Harness.Join(document, a2, 2, b1, 0);

        Assert.True(Lay(document));

        // Whichever way round they end up, the pairs should now be above one another rather
        // than wired across: a1 over b2 and a2 over b1, or the mirror of that.
        var straight =
            Math.Abs(a1.Bounds.Center.X - b2.Bounds.Center.X) < 1 &&
            Math.Abs(a2.Bounds.Center.X - b1.Bounds.Center.X) < 1;

        Assert.True(straight, "the two lines still cross");
    }

    [AvaloniaFact]
    public void AcrossInsteadOfDown()
    {
        var document = Harness.Page(1200, 900);
        var a = Box(document, 100, 100, "A");
        var b = Box(document, 100, 300, "B");
        var c = Box(document, 100, 500, "C");

        Harness.Join(document, a, 1, b, 3);
        Harness.Join(document, b, 1, c, 3);

        Assert.True(Lay(document, LayoutFlow.Right));

        AssertFlows(document, LayoutFlow.Right);
        AssertNoOverlaps(document);
        Assert.Equal(a.Bounds.Center.Y, c.Bounds.Center.Y, 1);
    }

    /// <summary>Tidying a drawing should not also move it.</summary>
    [AvaloniaFact]
    public void TheBlockStaysWhereItWas()
    {
        var document = Harness.Page(1200, 900);
        var a = Box(document, 300, 200, "A");
        var b = Box(document, 500, 400, "B");
        var c = Box(document, 200, 600, "C");

        Harness.Join(document, a, 2, b, 0);
        Harness.Join(document, a, 2, c, 0);

        var before = a.Bounds.Union(b.Bounds).Union(c.Bounds);

        Assert.True(Lay(document));

        var after = a.Bounds.Union(b.Bounds).Union(c.Bounds);

        Assert.Equal(before.X, after.X, 1);
        Assert.Equal(before.Y, after.Y, 1);
    }

    /// <summary>Laying out twice running changes nothing the second time.</summary>
    [AvaloniaFact]
    public void ItSettles()
    {
        var document = Harness.Page(1200, 900);
        var a = Box(document, 100, 500, "A");
        var b = Box(document, 600, 100, "B");
        var c = Box(document, 300, 300, "C");
        var d = Box(document, 900, 700, "D");

        Harness.Join(document, a, 2, b, 0);
        Harness.Join(document, a, 2, c, 0);
        Harness.Join(document, b, 2, d, 0);
        Harness.Join(document, c, 2, d, 0);

        Lay(document);
        var settled = new[] { a, b, c, d }.Select(s => s.Bounds).ToArray();

        Assert.False(Lay(document), "a second pass should find nothing to do");
        Assert.Equal(settled, new[] { a, b, c, d }.Select(s => s.Bounds).ToArray());
    }

    /// <summary>
    /// Tidied, a drawing is usually taller than the sprawl it came from, so putting it back
    /// exactly where it started can hang it off the paper. Given the page, it slides back on.
    /// </summary>
    [AvaloniaFact]
    public void ItSlidesBackOntoThePage()
    {
        var document = Harness.Page(800, 600);

        // Started low down, so a five-layer result would run off the bottom.
        var a = Box(document, 300, 430, "A");
        var b = Box(document, 300, 470, "B");
        var c = Box(document, 300, 510, "C");
        var d = Box(document, 300, 550, "D");

        Harness.Join(document, a, 2, b, 0);
        Harness.Join(document, b, 2, c, 0);
        Harness.Join(document, c, 2, d, 0);

        var page = new Rect(0, 0, 800, 600);

        Assert.True(GraphLayout.Apply(
            document.Shapes.Where(s => s is not ConnectorShape).ToList(),
            document.Shapes.OfType<ConnectorShape>().ToList(),
            LayoutFlow.Down,
            page));

        var block = a.Bounds.Union(b.Bounds).Union(c.Bounds).Union(d.Bounds);

        Assert.True(block.Y >= -0.01, $"the top of the drawing is off the page at {block.Y:0.0}");
        Assert.True(block.Bottom <= 600.01, $"the bottom runs to {block.Bottom:0.0}");
    }

    /// <summary>
    /// Shapes in one container come out side by side, even when what they are joined to would
    /// have interleaved them with another container's. A box has to be drawn round each
    /// afterwards, and members scattered along the layer would need one reaching across
    /// everything in between.
    /// </summary>
    [AvaloniaFact]
    public void ContainersAreKeptTogether()
    {
        var document = Harness.Page(1400, 900);

        var hub = Box(document, 600, 60, "hub");

        var one = Harness.Box(document, new Rect(0, 0, 200, 80), "one", ShapeKind.ContainerBox);
        var two = Harness.Box(document, new Rect(0, 0, 200, 80), "two", ShapeKind.ContainerBox);

        // Laid out interleaved, so the starting order alone would mix them up.
        var a1 = Box(document, 100, 300, "a1");
        var b1 = Box(document, 300, 300, "b1");
        var a2 = Box(document, 500, 300, "a2");
        var b2 = Box(document, 700, 300, "b2");

        a1.Container = one;
        a2.Container = one;
        b1.Container = two;
        b2.Container = two;

        foreach (var shape in new[] { a1, b1, a2, b2 })
            Harness.Join(document, hub, 2, shape, 0);

        Assert.True(Lay(document));

        var ones = new[] { a1, a2 }.Select(s => s.Bounds.Center.X).ToList();
        var twos = new[] { b1, b2 }.Select(s => s.Bounds.Center.X).ToList();

        // The two runs do not interleave: one is wholly to one side of the other.
        var apart = ones.Max() < twos.Min() || twos.Max() < ones.Min();

        Assert.True(apart,
            $"one at [{string.Join(", ", ones.Select(x => x.ToString("0")))}] " +
            $"and two at [{string.Join(", ", twos.Select(x => x.ToString("0")))}] are mixed up");

        // And there is more room between the groups than within either, so a box will fit.
        var within = Math.Abs(ones[0] - ones[1]);
        var between = Math.Min(
            Math.Abs(ones.Max() - twos.Min()),
            Math.Abs(twos.Max() - ones.Min()));

        Assert.True(between > within, $"between {between:0} is no more than within {within:0}");
    }

    /// <summary>The containers themselves are drawn round the result, not laid out as shapes.</summary>
    [AvaloniaFact]
    public void AContainerIsNotItselfLaidOut()
    {
        var document = Harness.Page(1000, 800);

        var box = Harness.Box(document, new Rect(11, 13, 200, 80), "box", ShapeKind.ContainerBox);
        var a = Box(document, 100, 300, "a");
        var b = Box(document, 300, 300, "b");

        a.Container = box;
        b.Container = box;

        Harness.Join(document, a, 2, b, 0);

        Assert.True(Lay(document));
        Assert.Equal(new Rect(11, 13, 200, 80), box.Bounds);
    }

    [AvaloniaFact]
    public void OneShapeIsNothingToArrange()
    {
        var document = Harness.Page(1200, 900);
        Box(document, 100, 100, "only");

        Assert.False(Lay(document));
    }

    /// <summary>
    /// The whole point of laying the shapes out is that the router can then do its half. This
    /// checks the two together: nothing crosses a shape once the dust settles.
    /// </summary>
    [AvaloniaFact]
    public void TheLinesRouteCleanlyOverTheResult()
    {
        var document = Harness.Page(1400, 1000);
        var shapes = new List<DiagramShape>();

        for (var i = 0; i < 7; i++)
            shapes.Add(Box(document, 100 + i * 40, 100 + i * 30, $"S{i}"));

        Harness.Join(document, shapes[0], 2, shapes[1], 0);
        Harness.Join(document, shapes[0], 2, shapes[2], 0);
        Harness.Join(document, shapes[1], 2, shapes[3], 0);
        Harness.Join(document, shapes[2], 2, shapes[3], 0);
        Harness.Join(document, shapes[3], 2, shapes[4], 0);
        Harness.Join(document, shapes[4], 2, shapes[5], 0);
        Harness.Join(document, shapes[0], 2, shapes[6], 0);

        Assert.True(Lay(document));
        AssertNoOverlaps(document);

        document.RouteConnectors();

        foreach (var line in document.Shapes.OfType<ConnectorShape>())
        foreach (var (p, q) in Harness.Segments(line))
        foreach (var shape in shapes)
        {
            var length = Math.Sqrt(Math.Pow(q.X - p.X, 2) + Math.Pow(q.Y - p.Y, 2));

            if (length < 2)
                continue;

            var across = new Vector(-(q.Y - p.Y) / length, (q.X - p.X) / length) * 0.75;
            var middle = new Point((p.X + q.X) / 2, (p.Y + q.Y) / 2);

            Assert.False(shape.HitTest(middle + across) && shape.HitTest(middle - across),
                $"a line runs through {shape.Text}");
        }
    }
}
