using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Media;
using AVASvgMaker.Models;

namespace AVASvgMaker.Engine;

/// <summary>
/// Writes a page as a Mermaid flowchart.
///
/// This is the one export that gives up the drawing rather than reproducing it. Every other
/// one here writes down where things are; Mermaid does not take positions at all - it is told
/// what connects to what and works out the arrangement itself. So what goes out is the
/// drawing's *meaning*: its boxes, their shapes, what they say, what joins them, and which
/// boxes are inside which container.
///
/// Which makes it good for exactly the things a picture is bad at - pasting a diagram into a
/// README, a wiki or a pull request, where it stays readable in a code review and can be
/// edited as text - and no good at all for a drawing whose point is its layout.
/// </summary>
public static class MermaidExporter
{
    public sealed record Result(string Code, IReadOnlyList<string> Skipped)
    {
        public string Summary => Skipped.Count == 0
            ? "Wrote the page as Mermaid"
            : "Wrote the page as Mermaid - left out " + string.Join(", ", Skipped);
    }

    public static Result Export(DiagramPage page)
    {
        var skipped = new Dictionary<string, int>();

        void Leave(string what) => skipped[what] = skipped.GetValueOrDefault(what) + 1;

        var shapes = page.Shapes.Where(shape => shape is not ConnectorShape).ToList();
        var lines = page.Shapes.OfType<ConnectorShape>().ToList();

        // Mermaid names a node once and refers to it by that name ever after, so every shape
        // that might be referred to needs one before anything is written.
        var names = new Dictionary<DiagramShape, string>();
        var next = 1;

        foreach (var shape in shapes.Where(shape => !shape.IsContainer))
            names[shape] = "n" + next++;

        var sb = new StringBuilder();

        sb.AppendLine($"flowchart {Direction(lines)}");

        // A container becomes a subgraph holding whatever sits inside it.
        var housed = new HashSet<DiagramShape>();

        foreach (var container in shapes.Where(shape => shape.IsContainer))
        {
            var inside = shapes
                .Where(shape => !shape.IsContainer && ReferenceEquals(shape.Container, container))
                .ToList();

            if (inside.Count == 0)
            {
                Leave("an empty container");
                continue;
            }

            sb.AppendLine($"    subgraph {"s" + next++}[{Label(container.DisplayText, container.Kind)}]");

            foreach (var shape in inside)
            {
                sb.AppendLine("        " + Node(shape, names[shape]));
                housed.Add(shape);
            }

            sb.AppendLine("    end");
        }

        foreach (var shape in shapes.Where(shape => !shape.IsContainer && !housed.Contains(shape)))
            sb.AppendLine("    " + Node(shape, names[shape]));

        // An edge joins two named nodes, so a line with a loose end has nothing to be.
        foreach (var line in lines)
        {
            if (line.StartShape is null || line.EndShape is null ||
                !names.TryGetValue(line.StartShape, out var from) ||
                !names.TryGetValue(line.EndShape, out var to))
            {
                Leave("a connector joined to nothing");
                continue;
            }

            sb.AppendLine($"    {from} {Link(line)} {to}");
        }

        var styled = shapes.Where(shape => !shape.IsContainer && Styled(shape)).ToList();

        if (styled.Count > 0)
        {
            sb.AppendLine();

            foreach (var shape in styled)
                sb.AppendLine($"    style {names[shape]} {Style(shape)}");
        }

        foreach (var shape in shapes)
        {
            if (shape.Kind == ShapeKind.TextBox)
                Leave("a text box, which Mermaid has nothing for");
            else if (shape.IsRotated)
                Leave("a shape's angle, Mermaid arranging the page itself");
            else if (shape.FillTo is not null)
                Leave("a fade, Mermaid filling flat");
        }

        if (page.Watermark.Length + page.Header.Length + page.Footer.Length > 0)
            Leave("the page's own furniture");

        return new Result(sb.ToString().TrimEnd() + Environment.NewLine, Left(skipped));
    }

    /// <summary>
    /// Which way the chart runs. Mermaid arranges the page itself, but it takes a direction,
    /// and the drawing already knows which way it reads: whichever way its connectors mostly
    /// go. A page with no connectors is left going down, which is Mermaid's own habit.
    /// </summary>
    private static string Direction(IReadOnlyList<ConnectorShape> lines)
    {
        var across = 0.0;
        var down = 0.0;

        foreach (var line in lines)
        {
            var run = line.ResolvedEnd - line.ResolvedStart;

            across += Math.Abs(run.X);
            down += Math.Abs(run.Y);
        }

        return across > down ? "LR" : "TD";
    }

    /// <summary>A node, in whichever of Mermaid's brackets comes nearest the shape.</summary>
    private static string Node(DiagramShape shape, string name)
    {
        var text = Label(shape.DisplayText, shape.Kind);

        return shape.Kind switch
        {
            ShapeKind.RoundedRectangle or ShapeKind.BpmnTask or ShapeKind.UmlState
                => $"{name}({text})",

            ShapeKind.Ellipse or ShapeKind.UmlUseCase or ShapeKind.BpmnStartEvent
                or ShapeKind.BpmnEndEvent or ShapeKind.BpmnIntermediateEvent
                => $"{name}(({text}))",

            ShapeKind.Diamond or ShapeKind.BpmnExclusiveGateway or ShapeKind.BpmnParallelGateway
                or ShapeKind.BpmnInclusiveGateway or ShapeKind.BpmnEventGateway
                => $"{name}{{{text}}}",

            ShapeKind.Hexagon => $"{name}{{{{{text}}}}}",

            ShapeKind.Parallelogram or ShapeKind.ManualInput => $"{name}[/{text}/]",

            ShapeKind.Trapezoid => $"{name}[/{text}\\]",

            ShapeKind.Cylinder or ShapeKind.StoredData or ShapeKind.StorageArray
                => $"{name}[({text})]",

            ShapeKind.PredefinedProcess or ShapeKind.BpmnSubprocess => $"{name}[[{text}]]",

            ShapeKind.Document or ShapeKind.MultiDocument or ShapeKind.UmlNote
                => $"{name}>{text}]",

            ShapeKind.OnPageConnector or ShapeKind.UmlInitialState or ShapeKind.UmlFinalState
                => $"{name}(({text}))",

            _ => $"{name}[{text}]"
        };
    }

    /// <summary>
    /// The words, quoted. Mermaid takes the brackets round a label as part of the drawing, so
    /// a label containing any of them has to be quoted or it ends the node early; quoting
    /// everything is simpler than working out which, and costs nothing.
    /// </summary>
    private static string Label(string text, ShapeKind kind)
    {
        text = text.Replace("\r\n", "\n").Trim();

        if (text.Length == 0)
            text = Spaced(kind.ToString());

        // A quotation mark inside a quoted label has to arrive as an entity, and so does a
        // bar: quoting settles what a bracket means, but a bar is what closes the label on a
        // line between two nodes, and nothing inside the quotes can be relied on to stop it.
        return "\"" + text
            .Replace("\"", "&quot;")
            .Replace("|", "&#124;")
            .Replace("\n", "<br/>") + "\"";
    }

    /// <summary>An enum name as words, for a shape that has nothing written on it.</summary>
    private static string Spaced(string name)
    {
        var sb = new StringBuilder();

        foreach (var letter in name)
        {
            if (char.IsUpper(letter) && sb.Length > 0)
                sb.Append(' ');

            sb.Append(sb.Length == 0 ? letter : char.ToLowerInvariant(letter));
        }

        return sb.ToString();
    }

    /// <summary>The line between two nodes: how it is drawn, and what it says.</summary>
    private static string Link(ConnectorShape line)
    {
        var both = line.StartCap != EndCapStyle.None && line.EndCap != EndCapStyle.None;
        var arrow = line.EndCap != EndCapStyle.None || both;

        var shaft = line.StrokeStyle switch
        {
            StrokeStyle.Dashed or StrokeStyle.Dotted => both ? "<-.->" : arrow ? "-.->" : "-.-",
            _ when line.StrokeThickness >= 4 => both ? "<==>" : arrow ? "==>" : "===",
            _ => both ? "<-->" : arrow ? "-->" : "---"
        };

        return string.IsNullOrWhiteSpace(line.Text)
            ? shaft
            : $"{shaft}|{Label(line.Text, ShapeKind.Connector)}|";
    }

    /// <summary>Whether a shape is painted differently enough from the default to say so.</summary>
    private static bool Styled(DiagramShape shape) =>
        shape.Fill != DiagramShape.DefaultFill || shape.Stroke != DiagramShape.DefaultStroke;

    private static string Style(DiagramShape shape)
    {
        var parts = new List<string>();

        if (shape.Fill.A == 0)
            parts.Add("fill:none");
        else
            parts.Add($"fill:{Hex(shape.Fill)}");

        if (shape.Stroke.A != 0)
        {
            parts.Add($"stroke:{Hex(shape.Stroke)}");
            parts.Add($"stroke-width:{Num(shape.StrokeThickness)}px");
        }

        return string.Join(",", parts);
    }

    private static string Hex(Color colour) => $"#{colour.R:x2}{colour.G:x2}{colour.B:x2}";

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static IReadOnlyList<string> Left(Dictionary<string, int> skipped) => skipped
        .OrderByDescending(entry => entry.Value)
        .Select(entry => entry.Value == 1 ? entry.Key : $"{entry.Value} × {entry.Key}")
        .ToList();
}
