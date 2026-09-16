using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Media;
using AVASvgMaker.Models;

namespace AVASvgMaker.Engine;

/// <summary>
/// Reads a Mermaid flowchart back into a drawing.
///
/// The other way round from every other import here. An SVG or a Visio file says where things
/// are and the reading is a matter of believing it; Mermaid does not take positions at all, so
/// there is nothing to believe. What arrives is what connects to what, and where it all goes
/// is worked out afterwards by <see cref="GraphLayout"/> - which is the same thing Mermaid's
/// own renderer does, and the reason this could not be written until there was a layout pass
/// to hand.
///
/// What it is for: the flowchart in a README, a wiki or a pull request, which you would rather
/// edit as a drawing than as text. Paste it in, move things about, and send it back out as a
/// picture - or as Mermaid again, which is what <see cref="MermaidExporter"/> is for.
/// </summary>
public static class MermaidImporter
{
    public sealed record Result(DiagramPage Page, IReadOnlyList<string> Skipped)
    {
        public int Count => Page.Shapes.Count(shape => shape is not ConnectorShape);

        public string Summary => Count == 0
            ? "Found no flowchart to read"
            : $"Read {Count} shape{(Count == 1 ? "" : "s")}" +
              (Skipped.Count == 0 ? string.Empty : " - left out " + string.Join(", ", Skipped));
    }

    /// <summary>The arrows, longest first, so "-->" is not read as "--" with a stray ">".</summary>
    private static readonly string[] Arrows =
    [
        "<==>", "<-.->", "<-->", "==>", "-.->", "-->", "===", "-.-", "---", "--"
    ];

    private static readonly Regex Header =
        new(@"^(?:flowchart|graph)\s+(?<way>TD|TB|BT|LR|RL)?\s*$", RegexOptions.IgnoreCase);

    private static readonly Regex Subgraph =
        new(@"^subgraph\s+(?<rest>.+)$", RegexOptions.IgnoreCase);

    private static readonly Regex Styling =
        new(@"^style\s+(?<name>\S+)\s+(?<how>.+)$", RegexOptions.IgnoreCase);

    private static readonly Regex Named = new(@"^(?<id>[A-Za-z0-9_\-\.]+)(?<body>.*)$");

    public static Result Read(string text, double width = 816, double height = 1056)
    {
        var page = new DiagramPage("Mermaid") { Width = width, Height = height };
        var notes = new Dictionary<string, int>();

        void Leave(string what) => notes[what] = notes.GetValueOrDefault(what) + 1;

        var lines = Clean(text);
        var flow = LayoutFlow.Down;
        var started = false;

        var shapes = new Dictionary<string, DiagramShape>();
        var order = new List<DiagramShape>();
        var groups = new List<(string Label, List<DiagramShape> Inside)>();
        var open = new Stack<int>();

        foreach (var line in lines)
        {
            if (!started)
            {
                if (Header.Match(line) is { Success: true } head)
                {
                    var way = head.Groups["way"].Value.ToUpperInvariant();
                    flow = way is "LR" or "RL" ? LayoutFlow.Right : LayoutFlow.Down;
                    started = true;
                    continue;
                }

                // Anything before the header is somebody else's business - front matter, a
                // heading, the prose the diagram was pasted out of.
                continue;
            }

            if (line.Equals("end", StringComparison.OrdinalIgnoreCase))
            {
                if (open.Count > 0)
                    open.Pop();

                continue;
            }

            if (Subgraph.Match(line) is { Success: true } group)
            {
                groups.Add((Title(group.Groups["rest"].Value), []));
                open.Push(groups.Count - 1);
                continue;
            }

            if (Styling.Match(line) is { Success: true } paint)
            {
                if (shapes.TryGetValue(paint.Groups["name"].Value, out var shape))
                    Paint(shape, paint.Groups["how"].Value);

                continue;
            }

            if (line.StartsWith("classDef", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("class ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("linkStyle", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("click ", StringComparison.OrdinalIgnoreCase))
            {
                Leave("a styling or click line");
                continue;
            }

            Wire(line, page, shapes, order, groups, open, Leave);
        }

        if (!started)
            Leave("everything - no \"flowchart\" or \"graph\" line was found");

        // Nothing has a position yet, which is the whole difficulty with Mermaid, so the
        // layout pass decides. Containers are sized round what they hold once that is done.
        GraphLayout.Apply(
            order, page.Shapes.OfType<ConnectorShape>().ToList(), flow,
            new Rect(0, 0, width, height));

        Enclose(page, groups);

        return new Result(page, notes
            .OrderByDescending(entry => entry.Value)
            .Select(entry => entry.Value == 1 ? entry.Key : $"{entry.Value} × {entry.Key}")
            .ToList());
    }

    #region Reading the text

    /// <summary>Drops the fences a block is pasted in, the comments, and the blank lines.</summary>
    private static List<string> Clean(string text) => text
        .Replace("\r\n", "\n")
        .Split('\n')
        .Select(line => line.Trim())
        .Where(line => line.Length > 0)
        .Where(line => !line.StartsWith("```") && !line.StartsWith("%%"))
        .ToList();

    /// <summary>
    /// One line joining nodes, or one node on its own. A line may be a chain - "a --> b --> c"
    /// - so it is taken an arrow at a time, the far end of one becoming the near end of the
    /// next.
    /// </summary>
    private static void Wire(
        string line, DiagramPage page,
        Dictionary<string, DiagramShape> shapes, List<DiagramShape> order,
        List<(string Label, List<DiagramShape> Inside)> groups, Stack<int> open,
        Action<string> leave)
    {
        var rest = line;
        DiagramShape? previous = null;

        while (true)
        {
            var (arrow, at) = Find(rest);

            if (at < 0)
            {
                var last = Shape(rest, page, shapes, order, groups, open, leave);

                if (previous is not null && last is not null)
                    Join(page, previous, last, string.Empty, "--");

                return;
            }

            var near = rest[..at].Trim();
            var after = rest[(at + arrow.Length)..];
            var label = string.Empty;

            // "a -->|yes| b": the words ride between bars just after the arrow.
            if (after.TrimStart().StartsWith('|'))
            {
                var open2 = after.IndexOf('|');
                var close = after.IndexOf('|', open2 + 1);

                if (close > open2)
                {
                    label = Words(after[(open2 + 1)..close]);
                    after = after[(close + 1)..];
                }
            }

            var from = previous ?? Shape(near, page, shapes, order, groups, open, leave);

            // "a -- yes --> b": the words sit inside the arrow, so the near side of what looks
            // like a second arrow is really the label of the first.
            var (next, to) = Find(after);

            if (from is null)
                return;

            if (to >= 0 && label.Length == 0 && Inline(arrow, next))
            {
                label = Words(after[..to].Trim());
                after = after[(to + next.Length)..];
            }

            var (far, onward) = Split(after);
            var shape = Shape(far, page, shapes, order, groups, open, leave);

            if (shape is null)
                return;

            Join(page, from, shape, label, arrow);

            if (onward.Length == 0)
                return;

            previous = shape;
            rest = onward;
        }
    }

    /// <summary>The first arrow in a line, and where it starts.</summary>
    private static (string Arrow, int At) Find(string text)
    {
        var best = -1;
        var token = string.Empty;

        foreach (var arrow in Arrows)
        {
            var at = text.IndexOf(arrow, StringComparison.Ordinal);

            if (at < 0 || (best >= 0 && at > best))
                continue;

            if (at == best && arrow.Length <= token.Length)
                continue;

            best = at;
            token = arrow;
        }

        return (token, best);
    }

    /// <summary>
    /// Whether what follows is the far half of one arrow with words in it, rather than the
    /// next arrow of a chain. "--" then "-->" is one labelled arrow; "-->" then "-->" is two.
    /// </summary>
    private static bool Inline(string opening, string closing) =>
        opening is "--" or "===" or "-.-" && closing.Length >= 2 && closing.EndsWith('>');

    /// <summary>Where a node ends and the next arrow of a chain begins.</summary>
    private static (string Node, string Onward) Split(string text)
    {
        var (_, at) = Find(text);

        return at < 0
            ? (text.Trim(), string.Empty)
            : (text[..at].Trim(), text[at..]);
    }

    #endregion

    #region Nodes

    /// <summary>
    /// The shape a name stands for, making it the first time the name is seen. A name used
    /// again with no brackets is the same shape; used again with brackets it keeps the first
    /// description, because Mermaid does the same and a drawing should not change under you
    /// depending on which line is read last.
    /// </summary>
    private static DiagramShape? Shape(
        string text, DiagramPage page,
        Dictionary<string, DiagramShape> shapes, List<DiagramShape> order,
        List<(string Label, List<DiagramShape> Inside)> groups, Stack<int> open,
        Action<string> leave)
    {
        text = text.Trim();

        if (text.Length == 0)
            return null;

        var named = Named.Match(text);

        if (!named.Success)
        {
            leave("a line that could not be read");
            return null;
        }

        var id = named.Groups["id"].Value;
        var body = named.Groups["body"].Value.Trim();

        if (shapes.TryGetValue(id, out var known))
            return known;

        var (kind, words) = Describe(body);
        var shape = ShapeFactory.Create(kind, Room(words.Length == 0 ? id : words));
        shape.Text = words.Length == 0 ? id : words;

        shapes[id] = shape;
        order.Add(shape);
        page.Shapes.Add(shape);

        if (open.Count > 0)
            groups[open.Peek()].Inside.Add(shape);

        return shape;
    }

    /// <summary>Room enough for the words, within reason.</summary>
    private static Rect Room(string text)
    {
        var longest = text.Split('\n').Max(line => line.Length);
        var lines = text.Count(letter => letter == '\n') + 1;

        return new Rect(0, 0,
            Math.Clamp(28 + longest * 8.5, 110, 280),
            Math.Clamp(34 + lines * 20, 56, 200));
    }

    /// <summary>
    /// Which shape the brackets round a label ask for. The same table the exporter writes by,
    /// read the other way, with the few forms Mermaid has that nothing here writes.
    /// </summary>
    private static (ShapeKind Kind, string Text) Describe(string body)
    {
        (string Open, string Close, ShapeKind Kind)[] forms =
        [
            ("(((", ")))", ShapeKind.Ellipse),
            ("((", "))", ShapeKind.Ellipse),
            ("([", "])", ShapeKind.RoundedRectangle),
            ("[[", "]]", ShapeKind.PredefinedProcess),
            ("[(", ")]", ShapeKind.Cylinder),
            ("[/", "/]", ShapeKind.Parallelogram),
            ("[/", "\\]", ShapeKind.Trapezoid),
            ("[\\", "/]", ShapeKind.Trapezoid),
            ("[\\", "\\]", ShapeKind.Parallelogram),
            ("{{", "}}", ShapeKind.Hexagon),
            ("{", "}", ShapeKind.Diamond),
            (">", "]", ShapeKind.Document),
            ("(", ")", ShapeKind.RoundedRectangle),
            ("[", "]", ShapeKind.Rectangle)
        ];

        foreach (var (start, end, kind) in forms)
        {
            if (body.Length >= start.Length + end.Length &&
                body.StartsWith(start, StringComparison.Ordinal) &&
                body.EndsWith(end, StringComparison.Ordinal))
                return (kind, Words(body[start.Length..^end.Length]));
        }

        return (ShapeKind.Rectangle, string.Empty);
    }

    /// <summary>
    /// A label as it was written: unquoted, and with the escapes the exporter puts in put back.
    /// </summary>
    private static string Words(string text)
    {
        text = text.Trim();

        if (text.Length >= 2 && text.StartsWith('"') && text.EndsWith('"'))
            text = text[1..^1];

        return text
            .Replace("<br/>", "\n")
            .Replace("<br>", "\n")
            .Replace("&quot;", "\"")
            .Replace("&#124;", "|")
            .Replace("&amp;", "&")
            .Trim();
    }

    #endregion

    #region Lines and paint

    private static void Join(
        DiagramPage page, DiagramShape from, DiagramShape to, string label, string arrow)
    {
        var line = new ConnectorShape(default, default)
        {
            StartShape = from,
            EndShape = to,
            StartPort = -1,
            EndPort = -1,
            Routing = ConnectorRouting.Orthogonal,
            Text = label,

            // A connector is drawn with an arrow on it by default. Here the arrows are what
            // the text says they are, so both ends start bare and only what is written is put
            // back on - "a --- b" is a line with no ends, and has to come out as one.
            StartCap = EndCapStyle.None,
            EndCap = EndCapStyle.None
        };

        // Read back off the arrow the way the exporter wrote it on.
        if (arrow.Contains('.'))
            line.StrokeStyle = StrokeStyle.Dashed;

        if (arrow.Contains('='))
            line.StrokeThickness = 4;

        if (arrow.EndsWith('>'))
            line.EndCap = EndCapStyle.Arrow;

        if (arrow.StartsWith('<'))
            line.StartCap = EndCapStyle.Arrow;

        page.Shapes.Add(line);
    }

    private static void Paint(DiagramShape shape, string how)
    {
        foreach (var part in how.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split(':', 2);

            if (pair.Length != 2)
                continue;

            var name = pair[0].Trim().ToLowerInvariant();
            var value = pair[1].Trim();

            switch (name)
            {
                case "fill" when value.Equals("none", StringComparison.OrdinalIgnoreCase):
                    shape.Fill = Colors.Transparent;
                    break;

                case "fill" when Colour(value) is { } fill:
                    shape.Fill = fill;
                    break;

                case "stroke" when Colour(value) is { } stroke:
                    shape.Stroke = stroke;
                    break;

                case "stroke-width" when double.TryParse(
                    value.TrimEnd('p', 'x', 'P', 'X'), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var weight):
                    shape.StrokeThickness = Math.Clamp(weight, 1, 12);
                    break;
            }
        }
    }

    private static Color? Colour(string text) =>
        Color.TryParse(text, out var colour) ? colour : null;

    #endregion

    #region Containers

    /// <summary>
    /// Draws a box round each subgraph's members, once they have been given places. A subgraph
    /// says which shapes belong together and nothing about where they are, so there is nothing
    /// to draw until the layout has spoken.
    /// </summary>
    private static void Enclose(
        DiagramPage page, List<(string Label, List<DiagramShape> Inside)> groups)
    {
        const double padding = 26;
        const double title = 22;

        foreach (var (label, inside) in groups)
        {
            if (inside.Count == 0)
                continue;

            var bounds = inside[0].Bounds;

            foreach (var shape in inside)
                bounds = bounds.Union(shape.Bounds);

            var box = ShapeFactory.Create(ShapeKind.ContainerBox, new Rect(
                bounds.X - padding,
                bounds.Y - padding - title,
                bounds.Width + padding * 2,
                bounds.Height + padding * 2 + title));

            box.Text = label;

            foreach (var shape in inside)
                shape.Container = box;

            // Behind what it holds, or it would be drawn over the top of it.
            page.Shapes.Insert(0, box);
        }
    }

    /// <summary>The name of a subgraph, which may be an id, a label, or an id with one.</summary>
    private static string Title(string rest)
    {
        rest = rest.Trim();

        var named = Named.Match(rest);

        if (named.Success && named.Groups["body"].Value.Trim().Length > 0)
            return Describe(named.Groups["body"].Value.Trim()).Text;

        return Words(rest);
    }

    #endregion
}
