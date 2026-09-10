using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media;
using AVASvgMaker.Models;

namespace AVASvgMaker.Engine;

/// <summary>
/// Reads an SVG file into a page of shapes.
///
/// Writing SVG is a translation; reading it is an interpretation, and a lossy one. SVG can say
/// far more than this editor can hold - gradients, patterns, filters, clipping, symbols,
/// stylesheets - so this reads the part of the language that maps onto shapes with a fill, an
/// outline and a label, and says plainly what it could not take. What comes out is a drawing
/// you can edit, not a facsimile of the file.
///
/// Handled: svg, g, rect, circle, ellipse, line, polyline, polygon, path and text, with
/// transforms, and fill, stroke, width, dashes and font given either as attributes or in a
/// style attribute. Anything else is counted and reported.
/// </summary>
public static class SvgImporter
{
    /// <summary>What an import produced, and what it had to leave behind.</summary>
    public sealed record Result(
        DiagramDocument Document,
        int Shapes,
        IReadOnlyList<string> Skipped)
    {
        public string Summary => Shapes == 0
            ? "Nothing in that file could be read as shapes"
            : $"Imported {Shapes} shape{(Shapes == 1 ? string.Empty : "s")}" +
              (Skipped.Count == 0
                  ? string.Empty
                  : $" - left out {string.Join(", ", Skipped)}");
    }

    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    public static Result Read(Stream stream)
    {
        var document = XDocument.Load(stream);
        var root = document.Root ?? throw new InvalidDataException("That file is empty.");

        if (root.Name.LocalName != "svg")
            throw new InvalidDataException("That is not an SVG file.");

        var page = new DiagramDocument();
        var skipped = new Dictionary<string, int>();

        // The page takes the file's size, and the viewBox becomes the transform onto it.
        var width = Length(root.Attribute("width")?.Value, 0);
        var height = Length(root.Attribute("height")?.Value, 0);
        var view = Numbers(root.Attribute("viewBox")?.Value);

        var toPage = Matrix.Identity;

        if (view.Length == 4 && view[2] > 0 && view[3] > 0)
        {
            if (width <= 0) width = view[2];
            if (height <= 0) height = view[3];

            toPage = Matrix.CreateTranslation(-view[0], -view[1]) *
                     Matrix.CreateScale(width / view[2], height / view[3]);
        }

        if (width > 0 && height > 0)
            page.SetPageSize(Math.Min(width, 20000), Math.Min(height, 20000));

        var shapes = new List<DiagramShape>();
        Walk(root, toPage, Style.Root, shapes, skipped);

        foreach (var shape in shapes)
            page.Shapes.Add(shape);

        page.ClearSelection();
        page.MarkSaved();

        var left = skipped
            .OrderByDescending(entry => entry.Value)
            .Select(entry => entry.Value == 1 ? $"1 {entry.Key}" : $"{entry.Value} {entry.Key}s")
            .ToList();

        return new Result(page, shapes.Count, left);
    }

    /// <summary>Style inherited down the tree, as SVG has it.</summary>
    private readonly record struct Style(
        Color? Fill, Color? Stroke, double Width, string? Font, double FontSize,
        bool Bold, bool Italic, TextAlign Align, StrokeStyle Dash)
    {
        public static readonly Style Root =
            new(Colors.Black, null, 1, null, 13, false, false, TextAlign.Center, StrokeStyle.Solid);
    }

    private static void Walk(
        XElement element, Matrix transform, Style inherited,
        List<DiagramShape> shapes, Dictionary<string, int> skipped)
    {
        foreach (var child in element.Elements())
        {
            if (child.Name.Namespace != Svg && child.Name.Namespace != XNamespace.None)
                continue;

            var name = child.Name.LocalName;
            var at = Transform(child.Attribute("transform")?.Value) * transform;
            var style = Restyle(child, inherited);

            switch (name)
            {
                case "g" or "a" or "switch":
                    Walk(child, at, style, shapes, skipped);
                    continue;

                case "svg":
                    Walk(child, at, style, shapes, skipped);
                    continue;

                case "title" or "desc" or "metadata" or "defs" or "style":
                    continue;
            }

            var shape = Shape(child, name, at, style);

            if (shape is not null)
            {
                shapes.Add(shape);
                continue;
            }

            // Counted so the user is told what was in the file and is not on the page.
            skipped[name] = skipped.GetValueOrDefault(name) + 1;
        }
    }

    private static DiagramShape? Shape(XElement element, string name, Matrix at, Style style)
    {
        switch (name)
        {
            case "rect":
            {
                var box = Box(element, "x", "y", "width", "height");

                if (box.Width <= 0 || box.Height <= 0)
                    return null;

                var round = Length(element.Attribute("rx")?.Value, 0) > 0;
                var kind = round ? ShapeKind.RoundedRectangle : ShapeKind.Rectangle;

                return Placed(ShapeFactory.Create(kind, box), box, at, style);
            }

            case "circle":
            {
                var r = Length(element.Attribute("r")?.Value, 0);

                if (r <= 0)
                    return null;

                var centre = new Point(
                    Length(element.Attribute("cx")?.Value, 0),
                    Length(element.Attribute("cy")?.Value, 0));

                var box = new Rect(centre.X - r, centre.Y - r, r * 2, r * 2);
                return Placed(ShapeFactory.Create(ShapeKind.Ellipse, box), box, at, style);
            }

            case "ellipse":
            {
                var rx = Length(element.Attribute("rx")?.Value, 0);
                var ry = Length(element.Attribute("ry")?.Value, 0);

                if (rx <= 0 || ry <= 0)
                    return null;

                var centre = new Point(
                    Length(element.Attribute("cx")?.Value, 0),
                    Length(element.Attribute("cy")?.Value, 0));

                var box = new Rect(centre.X - rx, centre.Y - ry, rx * 2, ry * 2);
                return Placed(ShapeFactory.Create(ShapeKind.Ellipse, box), box, at, style);
            }

            case "line":
            {
                var from = new Point(
                    Length(element.Attribute("x1")?.Value, 0),
                    Length(element.Attribute("y1")?.Value, 0)).Transform(at);

                var to = new Point(
                    Length(element.Attribute("x2")?.Value, 0),
                    Length(element.Attribute("y2")?.Value, 0)).Transform(at);

                var line = new ConnectorShape(from, to)
                {
                    Routing = ConnectorRouting.Straight,
                    StartCap = EndCapStyle.None,
                    EndCap = EndCapStyle.None,
                    Stroke = style.Stroke ?? Colors.Black,
                    StrokeThickness = Weight(style, at),
                    StrokeStyle = style.Dash
                };

                return line;
            }

            case "polyline" or "polygon":
            {
                var points = Numbers(element.Attribute("points")?.Value);

                if (points.Length < 4)
                    return null;

                var data = new System.Text.StringBuilder($"M {N(points[0])},{N(points[1])}");

                for (var i = 2; i + 1 < points.Length; i += 2)
                    data.Append($" L {N(points[i])},{N(points[i + 1])}");

                if (name == "polygon")
                    data.Append(" Z");

                return FromPath(data.ToString(), at, style);
            }

            case "path":
                return FromPath(element.Attribute("d")?.Value ?? string.Empty, at, style);

            case "text":
                return Text(element, at, style);
        }

        return null;
    }

    /// <summary>A path, normalised into its own box so it scales like any other shape.</summary>
    private static DiagramShape? FromPath(string data, Matrix at, Style style)
    {
        if (SvgPathData.Normalise(data, at) is not var (outline, extent, _))
            return null;

        return new PathShape(extent)
        {
            Outline = outline,
            Fill = style.Fill ?? Colors.Transparent,
            Stroke = style.Stroke ?? Colors.Transparent,
            StrokeThickness = Weight(style, at),
            StrokeStyle = style.Dash
        };
    }

    private static DiagramShape? Text(XElement element, Matrix at, Style style)
    {
        var content = string.Concat(element.DescendantNodes().OfType<XText>().Select(node => node.Value))
            .Replace("\n", " ")
            .Trim();

        if (content.Length == 0)
            return null;

        var x = Length(element.Attribute("x")?.Value ?? First(element, "x"), 0);
        var y = Length(element.Attribute("y")?.Value ?? First(element, "y"), 0);
        var anchor = new Point(x, y).Transform(at);

        // A rough box round the words: SVG places text by its baseline and gives no extent, so
        // one is invented that comfortably holds it and can be resized afterwards.
        var size = style.FontSize * Magnification(at);
        var width = Math.Max(20, content.Length * size * 0.62);
        var height = size * 1.6;

        var left = style.Align switch
        {
            TextAlign.Left => anchor.X,
            TextAlign.Right => anchor.X - width,
            _ => anchor.X - width / 2
        };

        var shape = ShapeFactory.Create(ShapeKind.TextBox, new Rect(left, anchor.Y - height * 0.75, width, height));

        shape.Text = content;
        shape.TextColor = style.Fill ?? Colors.Black;
        shape.FontSize = Math.Max(4, size);
        shape.FontName = style.Font ?? string.Empty;
        shape.Bold = style.Bold;
        shape.Italic = style.Italic;
        shape.TextAlign = style.Align;

        return shape;
    }

    private static string? First(XElement element, string name) =>
        element.Descendants().Select(child => child.Attribute(name)?.Value).FirstOrDefault(v => v is not null);

    /// <summary>
    /// Puts a box-shaped element where the transform says. A turn is kept as the shape's own
    /// angle, which it now has; anything more - a skew, or different scales along each axis
    /// after a turn - is beyond a box and its corners are simply taken as they land.
    /// </summary>
    private static DiagramShape Placed(DiagramShape shape, Rect box, Matrix at, Style style)
    {
        var corners = new[]
        {
            box.TopLeft.Transform(at), box.TopRight.Transform(at),
            box.BottomRight.Transform(at), box.BottomLeft.Transform(at)
        };

        var minX = corners.Min(point => point.X);
        var minY = corners.Min(point => point.Y);
        var maxX = corners.Max(point => point.X);
        var maxY = corners.Max(point => point.Y);

        // The angle the transform turns by, taken from where the top edge ended up.
        var edge = corners[1] - corners[0];
        var angle = DiagramDocument.Normalise(Math.Atan2(edge.Y, edge.X) * 180 / Math.PI);

        if (Math.Abs(angle) > 0.01 && shape.CanRotate)
        {
            var width = Length(corners[0], corners[1]);
            var height = Length(corners[0], corners[3]);
            var centre = new Point((minX + maxX) / 2, (minY + maxY) / 2);

            shape.Bounds = new Rect(centre.X - width / 2, centre.Y - height / 2, width, height);
            shape.Rotation = angle;
        }
        else
        {
            shape.Bounds = new Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
        }

        shape.Fill = style.Fill ?? Colors.Transparent;
        shape.Stroke = style.Stroke ?? Colors.Transparent;
        shape.StrokeThickness = Weight(style, at);
        shape.StrokeStyle = style.Dash;

        return shape;
    }

    private static double Length(Point a, Point b) => Math.Sqrt(
        (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>
    /// How much a transform magnifies. A stroke is scaled along with what it outlines, so a
    /// line inside a doubled group is drawn twice as thick - and a drawing whose viewBox does
    /// the scaling, which is most of them, comes in with the weights it was drawn at.
    /// </summary>
    private static double Magnification(Matrix at)
    {
        var area = Math.Abs(at.M11 * at.M22 - at.M12 * at.M21);
        return area <= 0 ? 1 : Math.Sqrt(area);
    }

    private static double Weight(Style style, Matrix at) =>
        Math.Max(0.1, style.Width * Magnification(at));

    #region Reading attributes

    private static Style Restyle(XElement element, Style inherited)
    {
        var style = inherited;

        // A style attribute wins over the presentation attributes beside it, as CSS does.
        var declarations = (element.Attribute("style")?.Value ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split(':', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].Trim().ToLowerInvariant(), parts => parts[1].Trim());

        string? Read(string name) =>
            declarations.TryGetValue(name, out var value) ? value : element.Attribute(name)?.Value;

        if (Read("fill") is { } fill)
            style = style with { Fill = Paint(fill) };

        if (Read("stroke") is { } stroke)
            style = style with { Stroke = Paint(stroke) };

        if (Read("stroke-width") is { } width)
            style = style with { Width = Length(width, style.Width) };

        if (Read("stroke-dasharray") is { } dashes)
            style = style with { Dash = Dash(dashes) };

        if (Read("font-size") is { } size)
            style = style with { FontSize = Length(size, style.FontSize) };

        if (Read("font-family") is { } font)
            style = style with { Font = font.Split(',')[0].Trim().Trim('\'', '"') };

        if (Read("font-weight") is { } weight)
            style = style with { Bold = weight is "bold" or "bolder" || (int.TryParse(weight, out var n) && n >= 600) };

        if (Read("font-style") is { } slant)
            style = style with { Italic = slant is "italic" or "oblique" };

        if (Read("text-anchor") is { } anchor)
            style = style with
            {
                Align = anchor switch
                {
                    "start" => TextAlign.Left,
                    "end" => TextAlign.Right,
                    _ => TextAlign.Center
                }
            };

        return style;
    }

    /// <summary>A colour, or null for "none" and for anything referring to something else.</summary>
    private static Color? Paint(string value)
    {
        value = value.Trim();

        if (value.Length == 0 || value.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            return null;

        if (value.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
            return Colors.Black;

        return Color.TryParse(value, out var colour) ? colour : null;
    }

    private static StrokeStyle Dash(string value)
    {
        var lengths = Numbers(value);

        if (lengths.Length == 0 || lengths.All(length => length <= 0))
            return StrokeStyle.Solid;

        // Short marks read as dotted, longer ones as dashed - the two this editor has.
        return lengths[0] <= 2 ? StrokeStyle.Dotted : StrokeStyle.Dashed;
    }

    private static Rect Box(XElement element, string x, string y, string width, string height) => new(
        Length(element.Attribute(x)?.Value, 0),
        Length(element.Attribute(y)?.Value, 0),
        Length(element.Attribute(width)?.Value, 0),
        Length(element.Attribute(height)?.Value, 0));

    /// <summary>
    /// A length in user units. The absolute units are converted; percentages and the relative
    /// ones need a context this importer does not carry, and fall back to the default.
    /// </summary>
    private static double Length(string? value, double fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        value = value.Trim();

        var scale = 1.0;

        foreach (var (suffix, factor) in new[]
                 {
                     ("px", 1.0), ("pt", 96.0 / 72), ("pc", 16.0),
                     ("mm", 96.0 / 25.4), ("cm", 96.0 / 2.54), ("in", 96.0)
                 })
        {
            if (!value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;

            scale = factor;
            value = value[..^suffix.Length];
            break;
        }

        if (value.EndsWith('%'))
            return fallback;

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number * scale
            : fallback;
    }

    private static double[] Numbers(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return value
            .Split([' ', ',', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
                ? n
                : double.NaN)
            .Where(n => !double.IsNaN(n))
            .ToArray();
    }

    private static Matrix Transform(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Matrix.Identity;

        var result = Matrix.Identity;
        var at = 0;

        while (at < value.Length)
        {
            var open = value.IndexOf('(', at);

            if (open < 0)
                break;

            var close = value.IndexOf(')', open);

            if (close < 0)
                break;

            var name = value[at..open].Trim(' ', ',', '\t', '\n', '\r').ToLowerInvariant();
            var args = Numbers(value[(open + 1)..close]);

            // Applied in the order written, each one before those already gathered.
            result = Single(name, args) * result;
            at = close + 1;
        }

        return result;
    }

    private static Matrix Single(string name, double[] args) => name switch
    {
        "translate" when args.Length >= 1 =>
            Matrix.CreateTranslation(args[0], args.Length > 1 ? args[1] : 0),

        "scale" when args.Length >= 1 =>
            Matrix.CreateScale(args[0], args.Length > 1 ? args[1] : args[0]),

        "rotate" when args.Length >= 3 =>
            Matrix.CreateTranslation(-args[1], -args[2]) *
            Matrix.CreateRotation(args[0] * Math.PI / 180) *
            Matrix.CreateTranslation(args[1], args[2]),

        "rotate" when args.Length >= 1 => Matrix.CreateRotation(args[0] * Math.PI / 180),

        "matrix" when args.Length >= 6 => new Matrix(args[0], args[1], args[2], args[3], args[4], args[5]),

        "skewx" when args.Length >= 1 => new Matrix(1, 0, Math.Tan(args[0] * Math.PI / 180), 1, 0, 0),
        "skewy" when args.Length >= 1 => new Matrix(1, Math.Tan(args[0] * Math.PI / 180), 0, 1, 0, 0),

        _ => Matrix.Identity
    };

    private static string N(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    #endregion
}
