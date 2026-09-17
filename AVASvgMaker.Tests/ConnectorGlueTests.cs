using System;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using AVASvgMaker.Views;
using Xunit;

namespace AVASvgMaker.Tests;

/// <summary>
/// A connector glued to another connector rather than to a shape - a loop feeding back into
/// the line that runs into a check, which is the usual reason to want it.
/// </summary>
public class ConnectorGlueTests
{
    /// <summary>How far a point is from the nearest part of a line as drawn.</summary>
    private static double Off(Point at, ConnectorShape line)
    {
        var best = double.MaxValue;

        foreach (var (p, q) in Harness.Segments(line))
        {
            var run = q - p;
            var length = run.X * run.X + run.Y * run.Y;

            var t = length < 1e-9
                ? 0
                : Math.Clamp(((at.X - p.X) * run.X + (at.Y - p.Y) * run.Y) / length, 0, 1);

            var on = new Point(p.X + run.X * t, p.Y + run.Y * t);
            best = Math.Min(best, Math.Sqrt(Math.Pow(at.X - on.X, 2) + Math.Pow(at.Y - on.Y, 2)));
        }

        return best;
    }

    private static (DiagramDocument Document, ConnectorShape Spine, ConnectorShape Loop) Feedback()
    {
        var document = Harness.Page(900, 700);

        var start = Harness.Box(document, new Rect(80, 60, 120, 50), "Start");
        var check = Harness.Box(document, new Rect(80, 300, 120, 50), "Check");
        var work = Harness.Box(document, new Rect(500, 300, 120, 50), "Work");

        var spine = Harness.Join(document, start, 2, check, 0);
        var loop = Harness.Join(document, work, 0, check, 1);

        // The loop comes back not to the check but to the line running into it.
        loop.EndShape = spine;
        loop.EndPort = -1;
        loop.EndAlong = 0.5;

        document.RouteConnectors();
        return (document, spine, loop);
    }

    [AvaloniaFact]
    public void ALineGluedToALineMeetsIt()
    {
        var (_, spine, loop) = Feedback();

        Assert.True(Off(loop.ResolvedEnd, spine) < 0.5,
            $"the loop ends {Off(loop.ResolvedEnd, spine):0.0} from the line it is glued to");
    }

    /// <summary>Halfway means halfway along the line as drawn, not halfway across its box.</summary>
    [AvaloniaFact]
    public void TheFractionIsMeasuredAlongTheLine()
    {
        var (_, spine, loop) = Feedback();

        foreach (var (along, expected) in new[] { (0.0, 0.0), (1.0, 1.0) })
        {
            loop.EndAlong = along;

            var path = spine.Path;
            var want = expected == 0 ? path[0] : path[^1];

            Assert.True(
                Math.Abs(loop.ResolvedEnd.X - want.X) < 0.5 &&
                Math.Abs(loop.ResolvedEnd.Y - want.Y) < 0.5,
                $"at {along} the end sat at {loop.ResolvedEnd} rather than {want}");
        }
    }

    /// <summary>The whole point: it stays on the line when the line moves.</summary>
    [AvaloniaFact]
    public void ItGoesWhereTheLineGoes()
    {
        var (document, spine, loop) = Feedback();

        var was = loop.ResolvedEnd;

        var start = document.Shapes.First(shape => shape.Text == "Start");
        start.Bounds = new Rect(600, 40, 120, 50);

        document.RouteConnectors();

        Assert.NotEqual(was, loop.ResolvedEnd);
        Assert.True(Off(loop.ResolvedEnd, spine) < 0.5, "it came off the line when the line moved");
    }

    /// <summary>It leaves the line square to it, on the side the rest of the connector is.</summary>
    [AvaloniaFact]
    public void ItLeavesTheLineSquareOn()
    {
        var (_, spine, loop) = Feedback();

        var out_ = loop.EndDirection;

        Assert.True(Math.Abs(out_.X) + Math.Abs(out_.Y) > 0.5, "it has no direction to leave by");

        // Square to the line it meets.
        var (p, q) = Harness.Segments(spine)
            .OrderBy(seg => Off(loop.ResolvedEnd, spine))
            .First();

        var run = q - p;
        var length = Math.Sqrt(run.X * run.X + run.Y * run.Y);

        if (length > 1e-9)
        {
            var dot = Math.Abs(out_.X * run.X / length + out_.Y * run.Y / length);
            Assert.True(dot < 0.01, $"it leaves at {dot:0.00} rather than square");
        }
    }

    /// <summary>The line being hung off is routed before the line hanging off it.</summary>
    [AvaloniaFact]
    public void TheLineItHangsOffIsRoutedFirst()
    {
        var document = Harness.Page(900, 700);

        var a = Harness.Box(document, new Rect(80, 60, 120, 50), "A");
        var b = Harness.Box(document, new Rect(80, 300, 120, 50), "B");
        var c = Harness.Box(document, new Rect(500, 300, 120, 50), "C");

        // The dependent one is added first, so drawing order alone would route it too early.
        var loop = Harness.Join(document, c, 0, b, 1);
        var spine = Harness.Join(document, a, 2, b, 0);

        loop.EndShape = spine;
        loop.EndPort = -1;

        document.RouteConnectors();

        Assert.True(Off(loop.ResolvedEnd, spine) < 0.5,
            "the loop was placed before the line it hangs off had one");
    }

    /// <summary>Two glued to each other settle instead of chasing one another for ever.</summary>
    [AvaloniaFact]
    public void ARingOfTwoDoesNotHang()
    {
        var document = Harness.Page(900, 700);

        var a = Harness.Box(document, new Rect(80, 60, 120, 50), "A");
        var b = Harness.Box(document, new Rect(80, 300, 120, 50), "B");
        var c = Harness.Box(document, new Rect(500, 60, 120, 50), "C");
        var d = Harness.Box(document, new Rect(500, 300, 120, 50), "D");

        var one = Harness.Join(document, a, 2, b, 0);
        var two = Harness.Join(document, c, 2, d, 0);

        one.EndShape = two;
        one.EndPort = -1;
        two.EndShape = one;
        two.EndPort = -1;

        // Routing at all is the assertion; a ring used to be a stack overflow.
        document.RouteConnectors();
        document.RouteConnectors();

        Assert.Equal(2, document.Shapes.OfType<ConnectorShape>().Count());
    }

    [AvaloniaFact]
    public void GluingToItselfIsIgnored()
    {
        var (document, spine, _) = Feedback();

        spine.EndShape = spine;
        spine.EndPort = -1;

        document.RouteConnectors();

        Assert.True(spine.Path.Count >= 2);
    }

    /// <summary>
    /// Drawn with the pointer, which is how anybody will actually do it: from a shape, and
    /// dropped on a line rather than on anything at either end of it.
    /// </summary>
    [AvaloniaFact]
    public void ALineCanBeDrawnOntoALine()
    {
        var (window, canvas) = Harness.Editor(900, 700);

        var start = Harness.Box(canvas.Document, new Rect(80, 60, 120, 50), "Start");
        var check = Harness.Box(canvas.Document, new Rect(80, 300, 120, 50), "Check");
        var work = Harness.Box(canvas.Document, new Rect(500, 300, 120, 50), "Work");

        var spine = Harness.Join(canvas.Document, start, 2, check, 0);
        canvas.Document.RouteConnectors();
        Harness.Settle(window);

        // Somewhere along the middle of the line, clear of both shapes.
        var path = spine.Path;
        var onTheLine = new Point(
            (path[0].X + path[^1].X) / 2,
            (path[0].Y + path[^1].Y) / 2);

        canvas.Tool = EditorTool.Connector;
        canvas.Document.SetSelection([]);

        Harness.Drag(canvas, work.Bounds.Center, onTheLine);
        Harness.Settle(window);

        var drawn = canvas.Document.Shapes.OfType<ConnectorShape>()
            .Single(line => !ReferenceEquals(line, spine));

        Assert.Same(work, drawn.StartShape);
        Assert.Same(spine, drawn.EndShape);

        // And it landed where it was dropped rather than at some default.
        Assert.True(Math.Abs(drawn.EndAlong - spine.Nearest(onTheLine)) < 0.05,
            $"glued at {drawn.EndAlong:0.00}, dropped at {spine.Nearest(onTheLine):0.00}");

        canvas.Document.RouteConnectors();
        Assert.True(Off(drawn.ResolvedEnd, spine) < 0.5);
    }

    /// <summary>Mermaid joins nodes to nodes, so it says what it had to leave out.</summary>
    [AvaloniaFact]
    public void MermaidSaysItCannotWriteOne()
    {
        var (document, _, _) = Feedback();

        var written = MermaidExporter.Export(document.CurrentPage);

        Assert.DoesNotContain("joined to nothing", written.Summary);
        Assert.Contains("another line", written.Summary);
    }

    [AvaloniaFact]
    public void ItSurvivesSavingAndLoading()
    {
        var (document, _, loop) = Feedback();

        loop.EndAlong = 0.25;

        var back = DiagramFile.FromJson(DiagramFile.ToJson(document));
        var lines = back.Shapes.OfType<ConnectorShape>().ToList();
        var reloaded = lines.Single(line => line.EndShape is ConnectorShape);

        Assert.Equal(0.25, reloaded.EndAlong, 3);
        Assert.IsType<ConnectorShape>(reloaded.EndShape);

        back.RouteConnectors();

        var spine = lines.Single(line => line.EndShape is not ConnectorShape);
        Assert.True(Off(reloaded.ResolvedEnd, spine) < 0.5);
    }
}
