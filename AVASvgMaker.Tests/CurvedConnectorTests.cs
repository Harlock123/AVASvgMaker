using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using Xunit;

namespace AVASvgMaker.Tests;

/// <summary>
/// Connectors drawn with their corners rounded off. The route is the router's business and is
/// not changed by this - these are about what is drawn over it, and about the drawn line still
/// keeping the promises the route made.
/// </summary>
public class CurvedConnectorTests
{
    private static (DiagramDocument Document, ConnectorShape Line) Bent(ConnectorRouting routing)
    {
        var document = Harness.Page(900, 700);
        var a = Harness.Box(document, new Rect(60, 100, 110, 40), "A");
        var b = Harness.Box(document, new Rect(600, 400, 110, 40), "B");

        var connector = Harness.Join(document, a, 1, b, 3);
        connector.Routing = routing;

        document.RouteConnectors();
        return (document, connector);
    }

    /// <summary>Points along a piece: the run itself, or the curve through the corner.</summary>
    private static IEnumerable<Point> Walk(ConnectorPiece piece, int steps = 24)
    {
        for (var i = 0; i <= steps; i++)
        {
            var t = (double)i / steps;

            if (piece.Control is not { } c)
            {
                yield return new Point(
                    piece.From.X + (piece.To.X - piece.From.X) * t,
                    piece.From.Y + (piece.To.Y - piece.From.Y) * t);

                continue;
            }

            var u = 1 - t;

            yield return new Point(
                u * u * piece.From.X + 2 * u * t * c.X + t * t * piece.To.X,
                u * u * piece.From.Y + 2 * u * t * c.Y + t * t * piece.To.Y);
        }
    }

    /// <summary>
    /// Rounding is a way of drawing a route, not a way of finding one. Both modes must come out
    /// of the router with the same line; only what is drawn over it differs.
    /// </summary>
    [AvaloniaFact]
    public void ACurvedConnectorTakesTheSameRouteAsARightAngledOne()
    {
        var (_, square) = Bent(ConnectorRouting.Orthogonal);
        var (_, curved) = Bent(ConnectorRouting.Curved);

        Assert.Equal(Harness.Path(square), Harness.Path(curved));
        Assert.True(curved.IsRightAngled, "a curved connector is still routed in runs");
    }

    /// <summary>
    /// The pieces join up: each begins where the last ended, the first begins at the start of
    /// the route and the last ends at its end. A corner cut too deeply would break this.
    /// </summary>
    [AvaloniaFact]
    public void ThePiecesJoinUpEndToEnd()
    {
        var (_, curved) = Bent(ConnectorRouting.Curved);
        var pieces = curved.Pieces().ToList();
        var route = curved.Path;

        Assert.Equal(route[0], pieces[0].From);
        Assert.Equal(route[^1], pieces[^1].To);

        for (var i = 1; i < pieces.Count; i++)
            Assert.Equal(pieces[i - 1].To, pieces[i].From);

        // Every turn is cut from a corner the route actually has.
        foreach (var turn in pieces.Where(piece => piece.Control is not null))
            Assert.Contains(turn.Control!.Value, route);
    }

    /// <summary>A right-angled connector has no turns at all - it is all runs, as it always was.</summary>
    [AvaloniaFact]
    public void ARightAngledConnectorHasNoTurns()
    {
        var (_, square) = Bent(ConnectorRouting.Orthogonal);

        Assert.All(square.Pieces(), piece => Assert.Null(piece.Control));
    }

    /// <summary>
    /// Corners on short runs take what room there is rather than the full radius. Two corners a
    /// few units apart would otherwise each eat further than the run between them is long, and
    /// the line would doubled back on itself.
    /// </summary>
    [AvaloniaFact]
    public void ShortRunsClampTheCornerRatherThanOvershoot()
    {
        var (document, curved) = Bent(ConnectorRouting.Curved);

        // A staircase of six-unit treads, arriving square so the bends are not dropped for
        // slanting - that rule and this one are different things and would mask each other.
        curved.Waypoints =
        [
            new Point(300, 120), new Point(300, 126), new Point(306, 126), new Point(306, 420)
        ];

        document.RouteConnectors();

        Assert.True(curved.HasManualRoute, "the bends should have survived to be rounded");

        var route = curved.Path;
        var pieces = curved.Pieces().ToList();

        for (var i = 1; i < pieces.Count; i++)
            Assert.Equal(pieces[i - 1].To, pieces[i].From);

        // A corner may take half of the shorter of the two runs it joins, and no more than the
        // radius. On a six-unit tread that is three units, not ten.
        foreach (var turn in pieces.Where(piece => piece.Control is not null))
        {
            var corner = turn.Control!.Value;
            var at = route.ToList().IndexOf(corner);

            var room = Math.Min(
                Distance(route[at - 1], corner),
                Distance(corner, route[at + 1])) / 2;

            var allowed = Math.Min(10, room) + 0.01;

            Assert.True(Distance(turn.From, corner) <= allowed,
                $"a corner on a {room * 2:0.0}-unit run reached {Distance(turn.From, corner):0.00} back");

            Assert.True(Distance(corner, turn.To) <= allowed,
                $"a corner on a {room * 2:0.0}-unit run reached {Distance(corner, turn.To):0.00} on");
        }
    }

    /// <summary>
    /// The promise the route made has to survive being drawn. Cutting a corner moves the line
    /// towards the inside of the turn, which is the side the shape being rounded is on - so the
    /// drawn curve, not merely the route beneath it, is what is checked here.
    /// </summary>
    [AvaloniaFact]
    public void ACurvedLineStillKeepsOutOfTheShapes()
    {
        var random = new Random(31415);

        for (var trial = 0; trial < 60; trial++)
        {
            var document = Harness.Page(1200, 900);
            var boxes = new List<DiagramShape>();

            for (var i = 0; i < 6; i++)
            for (var attempt = 0; attempt < 200; attempt++)
            {
                var at = new Rect(random.Next(60, 1000), random.Next(60, 760), 110, 44);

                if (boxes.Any(other => other.Bounds.Inflate(24).Intersects(at)))
                    continue;

                boxes.Add(Harness.Box(document, at, $"S{boxes.Count}"));
                break;
            }

            var joins = new List<ConnectorShape>();

            for (var i = 0; i < 4; i++)
            {
                var from = boxes[random.Next(boxes.Count)];
                var to = boxes[random.Next(boxes.Count)];

                if (ReferenceEquals(from, to))
                    continue;

                var connector = Harness.Join(document, from, random.Next(4), to, random.Next(4));
                connector.Routing = ConnectorRouting.Curved;
                joins.Add(connector);
            }

            document.RouteConnectors();

            foreach (var connector in joins)
            {
                var drawn = connector.Pieces().SelectMany(piece => Walk(piece)).ToList();

                for (var i = 0; i + 1 < drawn.Count; i++)
                foreach (var shape in boxes)
                    Assert.False(Pierces(drawn[i], drawn[i + 1], shape),
                        "the drawn curve reaches into a shape");
            }
        }
    }

    /// <summary>
    /// A curved connector is drawn round its corners but routed square, so the rule that drops
    /// bends which have stopped turning square corners must not mistake one for the other.
    /// </summary>
    [AvaloniaFact]
    public void ACurvedConnectorIsNotMistakenForAStrandedRoute()
    {
        var (document, curved) = Bent(ConnectorRouting.Curved);

        curved.Waypoints = [new Point(385, 120), new Point(385, 420)];
        document.RouteConnectors();

        Assert.True(curved.HasManualRoute, "square bends on a curved connector are still square");
    }

    [AvaloniaFact]
    public void TheCurvesReachTheExports()
    {
        var (document, _) = Bent(ConnectorRouting.Curved);

        var svg = SvgExporter.Export(document);
        Assert.Contains(" Q ", svg);
        Assert.Contains("fill=\"none\"", svg);

        using var stream = new MemoryStream();
        VisioExporter.Write(document, stream, "Test");
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.Entries.First(e => e.FullName.Contains("page1.xml")).Open());
        var xml = reader.ReadToEnd();

        // Visio has no quadratic row of its own, so each turn goes out as the cubic it becomes.
        Assert.Contains("RelCubBezTo", xml);
    }

    [AvaloniaFact]
    public void TheModeSurvivesSavingAndLoading()
    {
        var (document, _) = Bent(ConnectorRouting.Curved);

        var reloaded = DiagramFile.FromJson(DiagramFile.ToJson(document));

        Assert.Equal(
            ConnectorRouting.Curved,
            reloaded.Shapes.OfType<ConnectorShape>().Single().Routing);
    }

    private static bool Pierces(Point a, Point b, DiagramShape shape)
    {
        var length = Distance(a, b);

        if (length < 1e-6)
            return false;

        var across = new Vector(-(b.Y - a.Y) / length, (b.X - a.X) / length) * 0.75;
        var middle = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);

        return shape.HitTest(middle + across) && shape.HitTest(middle - across);
    }

    private static double Distance(Point a, Point b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}
