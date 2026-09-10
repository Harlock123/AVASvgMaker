using System;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using Xunit;

namespace AVASvgMaker.Tests;

public class ConnectorLabelTests
{
    /// <summary>Two boxes with a long horizontal run between them, and a label on it.</summary>
    private static (DiagramDocument Document, ConnectorShape Line) Labelled(string text = "then dispatch")
    {
        var document = Harness.Page(800, 400);

        var a = Harness.Box(document, new Rect(60, 180, 120, 60), "A");
        var b = Harness.Box(document, new Rect(600, 180, 120, 60), "B");

        var line = Harness.Join(document, a, 1, b, 3);
        line.Text = text;

        document.RouteConnectors();
        return (document, line);
    }

    [AvaloniaFact]
    public void ALabelSitsOnTheLongestRunAndNotOnACorner()
    {
        // A route that goes across, down and across has its halfway point at a bend; the
        // longest run is where a label can be read.
        var document = Harness.Page(800, 500);

        var a = Harness.Box(document, new Rect(60, 60, 100, 50), "A");
        var b = Harness.Box(document, new Rect(600, 380, 100, 50), "B");

        var line = Harness.Join(document, a, 2, b, 0);
        line.Text = "label";
        document.RouteConnectors();

        var path = line.Path;
        var anchor = line.LabelAnchor;

        // It is on the line, and it is in the middle of a run rather than at either end of one.
        var longest = -1.0;
        var middle = default(Point);

        for (var i = 0; i < path.Count - 1; i++)
        {
            var length = Math.Abs(path[i].X - path[i + 1].X) + Math.Abs(path[i].Y - path[i + 1].Y);

            if (length <= longest)
                continue;

            longest = length;
            middle = new Point((path[i].X + path[i + 1].X) / 2, (path[i].Y + path[i + 1].Y) / 2);
        }

        Assert.Equal(middle.X, anchor.X, 1);
        Assert.Equal(middle.Y, anchor.Y, 1);

        foreach (var corner in path)
            Assert.True(Math.Abs(corner.X - anchor.X) + Math.Abs(corner.Y - anchor.Y) > 4,
                $"the label landed on the bend at {corner}");
    }

    [AvaloniaFact]
    public void TheLineIsBrokenWhereTheLabelSits()
    {
        var (_, line) = Labelled();
        var label = line.LabelArea;

        Assert.True(label.Width > 0, "the label has a box");

        // Nothing the connector draws may pass through the middle of its own label.
        foreach (var (from, to) in Segments(line))
            Assert.False(Crosses(from, to, label.Deflate(1)),
                $"the line runs through the label between {from} and {to}");
    }

    [AvaloniaFact]
    public void AConnectorWithNoLabelIsDrawnWhole()
    {
        var (_, line) = Labelled(string.Empty);

        // One run, unbroken, from end to end.
        var pieces = Segments(line).ToList();

        Assert.Equal(line.Path.Count - 1, pieces.Count);
    }

    [AvaloniaFact]
    public void ALabelIsJustBigEnoughForItsWords()
    {
        var (_, line) = Labelled("a");
        var narrow = line.LabelArea.Width;

        line.Text = "a much longer label than that one";
        var wide = line.LabelArea.Width;

        Assert.True(wide > narrow * 2, $"{wide} against {narrow}");

        // And not so tight that the words are wrapped again inside it.
        Assert.DoesNotContain("\n", line.Text);
        Assert.True(line.LabelArea.Height < line.FontSize * 2.2, "one line stayed one line");
    }

    [AvaloniaFact]
    public void ALabelStaysOnTheLineWhenAShapeMoves()
    {
        var (document, line) = Labelled();

        Assert.True(OnTheLine(line), "the label starts on the line");

        // Re-routed round a new shape position, the label is still on the run it belongs to -
        // which is the property that matters, and not that it necessarily moved.
        document.Shapes[1].Translate(new Vector(0, 120));
        document.RouteConnectors();

        Assert.True(OnTheLine(line), "the label is still on the line");

        // Moving the whole thing does move it.
        var before = line.LabelAnchor;

        foreach (var shape in document.Shapes)
            shape.Translate(new Vector(0, 60));

        document.RouteConnectors();

        Assert.Equal(before.Y + 60, line.LabelAnchor.Y, 1);
        Assert.True(OnTheLine(line));
    }

    /// <summary>True when the label's anchor lies on one of the connector's own segments.</summary>
    private static bool OnTheLine(ConnectorShape line)
    {
        var at = line.LabelAnchor;
        var path = line.Path;

        for (var i = 0; i < path.Count - 1; i++)
        {
            var from = path[i];
            var to = path[i + 1];

            var run = to - from;
            var length = run.X * run.X + run.Y * run.Y;

            if (length <= 0)
                continue;

            var along = Math.Clamp(((at.X - from.X) * run.X + (at.Y - from.Y) * run.Y) / length, 0, 1);
            var near = new Point(from.X + run.X * along, from.Y + run.Y * along);

            if (Math.Abs(near.X - at.X) + Math.Abs(near.Y - at.Y) < 0.5)
                return true;
        }

        return false;
    }

    [AvaloniaFact]
    public void ALabelCanBeMovedAndTheOffsetIsKeptRatherThanThePosition()
    {
        var (document, line) = Labelled();
        var anchor = line.LabelAnchor;

        line.LabelOffset = new Vector(0, -40);
        Assert.Equal(anchor.Y - 40, line.LabelAnchor.Y, 1);

        // The offset is from wherever the line is, so it survives a re-route rather than
        // leaving the words behind where the line used to be.
        document.Shapes[0].Translate(new Vector(0, 100));
        document.Shapes[1].Translate(new Vector(0, 100));
        document.RouteConnectors();

        Assert.Equal(line.Path[0].Y - 40, line.LabelAnchor.Y, 1);
    }

    [AvaloniaFact]
    public void DraggingTheLabelMovesIt()
    {
        var (window, canvas) = Harness.Editor(800, 400);
        var document = canvas.Document;

        var a = Harness.Box(document, new Rect(60, 180, 120, 60), "A");
        var b = Harness.Box(document, new Rect(600, 180, 120, 60), "B");
        var line = Harness.Join(document, a, 1, b, 3);
        line.Text = "label";

        document.RouteConnectors();
        document.SelectOnly(line);
        Harness.Settle(window);

        var from = line.LabelAnchor;
        Harness.Drag(canvas, from, new Point(from.X, from.Y - 50));

        Assert.Equal(-50, line.LabelOffset.Y, 1);
        Assert.Equal(from.Y - 50, line.LabelAnchor.Y, 1);
    }

    [AvaloniaFact]
    public void ResettingTheRoutePutsTheLabelBackToo()
    {
        var (document, line) = Labelled();

        line.LabelOffset = new Vector(30, -30);
        line.ResetRoute();

        Assert.Equal(default, line.LabelOffset);
        document.RouteConnectors();
    }

    [AvaloniaFact]
    public void TheLabelAndItsGapReachTheSvg()
    {
        var (document, line) = Labelled();
        var svg = SvgExporter.Export(document);

        Assert.Contains("then dispatch", svg);

        // The break is in the file as well as on the screen: the run is written as pieces
        // rather than one polyline from end to end.
        var body = line.ToSvg();
        Assert.Contains("<line ", body);
        Assert.True(body.Split("<line ").Length - 1 >= 2, "the line was written in pieces");
    }

    [AvaloniaFact]
    public void AMovedLabelSurvivesTheFile()
    {
        var (document, line) = Labelled();
        line.LabelOffset = new Vector(12, -34);

        var json = DiagramFile.ToJson(document);
        Assert.Contains($"\"version\": {DiagramFile.CurrentVersion}", json);

        var read = DiagramFile.FromJson(json).Shapes.OfType<ConnectorShape>().Single();

        Assert.Equal(12, read.LabelOffset.X, 3);
        Assert.Equal(-34, read.LabelOffset.Y, 3);
        Assert.Equal("then dispatch", read.Text);
    }

    [AvaloniaFact]
    public void ALabelLeftAloneWritesNothingExtra()
    {
        var (document, _) = Labelled();

        Assert.DoesNotContain("labelOffset", DiagramFile.ToJson(document));
    }

    [AvaloniaFact]
    public void AVersion13FileComesBackWithItsLabelWhereTheLineIs()
    {
        var (document, line) = Labelled();
        line.LabelOffset = new Vector(20, 20);

        var json = System.Text.RegularExpressions.Regex.Replace(
                DiagramFile.ToJson(document), ",?\\s*\"labelOffset\": \\[[^\\]]*\\]", string.Empty)
            .Replace($"\"version\": {DiagramFile.CurrentVersion}", "\"version\": 13");

        var read = DiagramFile.FromJson(json).Shapes.OfType<ConnectorShape>().Single();

        Assert.Equal(default, read.LabelOffset);
        Assert.Equal("then dispatch", read.Text);
    }

    private static (Point From, Point To)[] Segments(ConnectorShape line)
    {
        // What the connector actually draws, read back out of its own SVG.
        return System.Text.RegularExpressions.Regex
            .Matches(line.ToSvg(), @"<line x1=""([-\d.]+)"" y1=""([-\d.]+)"" x2=""([-\d.]+)"" y2=""([-\d.]+)""")
            .Select(m => (
                new Point(double.Parse(m.Groups[1].Value), double.Parse(m.Groups[2].Value)),
                new Point(double.Parse(m.Groups[3].Value), double.Parse(m.Groups[4].Value))))
            .ToArray();
    }

    /// <summary>True when a segment passes through a rectangle.</summary>
    private static bool Crosses(Point from, Point to, Rect box)
    {
        for (var i = 0; i <= 40; i++)
        {
            var t = i / 40.0;
            var at = new Point(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);

            if (box.Contains(at))
                return true;
        }

        return false;
    }
}
