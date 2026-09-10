using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media;
using AVASvgMaker.Models;

namespace AVASvgMaker.Engine;

/// <summary>
/// Reads a modern Visio drawing - the .vsdx package, a zip of XML parts - into pages of shapes.
///
/// Like the SVG importer, this is an interpretation and not a copy. Visio holds a great deal
/// this editor has no place to put: layers, themes, shape data, master inheritance chains,
/// scaled drawings. What it reads is the part that maps onto shapes with a fill, an outline,
/// a label and lines between them, and it says what it had to leave.
///
/// The geometry of a shape is often not on the shape at all but on the master it was stamped
/// from, so masters are read first and consulted whenever a shape carries none of its own.
/// Without that, a drawing made the ordinary way - by dragging stencils onto a page - comes in
/// as a page of empty boxes.
/// </summary>
public static class VisioImporter
{
    public sealed record Result(DiagramDocument Document, int Shapes, IReadOnlyList<string> Skipped)
    {
        public string Summary => Shapes == 0
            ? "Nothing in that drawing could be read as shapes"
            : $"Imported {Shapes} shape{(Shapes == 1 ? string.Empty : "s")}" +
              (Skipped.Count == 0 ? string.Empty : $" - left out {string.Join(", ", Skipped)}");
    }

    private static readonly XNamespace V = VisioFormat.Main;
    private static readonly XNamespace R = VisioFormat.Relationships;

    /// <summary>One shape's cells and sections, with its master standing behind it.</summary>
    /// <summary>
    /// A master as an instance sees it: the stamp a whole shape takes, and every shape inside
    /// it by ID, for the children of a stamped group to answer to one by one.
    /// </summary>
    private sealed record Master(Sheet Stamp, IReadOnlyDictionary<string, XElement> Parts);

    /// <summary>Something left out, counted, and able to say itself for any number of it.</summary>
    private sealed record Tally(string One, string Many)
    {
        public string Say(int count) => count == 1 ? $"1 {One}" : $"{count} {Many}";
    }

    /// <summary>One number in a geometry row, and the sheet whose inches it is stated in.</summary>
    private sealed record Slot(double Value, Sheet Owner);

    /// <summary>A geometry row once it has inherited what it did not restate.</summary>
    private sealed record Step(string? Index, string Kind, IReadOnlyDictionary<string, Slot> Cells);

    /// <summary>A geometry section once it has inherited what it did not restate.</summary>
    private sealed record Figure(string? Index, bool Shown, bool Filled, IReadOnlyList<Step> Steps);

    private sealed record Sheet(XElement Element, Sheet? Master)
    {
        public string? Cell(string name)
        {
            var own = Element.Elements(V + "Cell")
                .FirstOrDefault(cell => (string?)cell.Attribute("N") == name);

            return own is not null ? (string?)own.Attribute("V") : Master?.Cell(name);
        }

        public double Number(string name, double fallback = 0) =>
            VisioFormat.Number(Cell(name), fallback);

        /// <summary>
        /// A cell from the first row of a named section - how a shape states its font and its
        /// alignment. Only the first row is read: a shape here has one look, not a different
        /// one per run of characters.
        /// </summary>
        public string? Cell(string section, string name)
        {
            var row = Element.Elements(V + "Section")
                .FirstOrDefault(part => (string?)part.Attribute("N") == section)
                ?.Elements(V + "Row").FirstOrDefault();

            var own = row?.Elements(V + "Cell")
                .FirstOrDefault(cell => (string?)cell.Attribute("N") == name);

            return own is not null ? (string?)own.Attribute("V") : Master?.Cell(section, name);
        }

        /// <summary>
        /// The shape's outline, inherited all the way down. Visio writes only what differs
        /// from the master - a section, a row within it, or a single number within that row -
        /// so a shape's geometry has to be built by laying its own words over its master's
        /// rather than by choosing between the two. A shape whose file says nothing but
        /// "the second corner moved" really does mean the rest of the master's outline.
        /// </summary>
        public IReadOnlyList<Figure> Geometry
        {
            get
            {
                var figures = new List<Figure>(Master?.Geometry ?? []);

                var own = Element.Elements(V + "Section")
                    .Where(section => (string?)section.Attribute("N") == "Geometry");

                foreach (var section in own)
                {
                    var index = (string?)section.Attribute("IX");
                    var at = index is null ? -1 : figures.FindIndex(figure => figure.Index == index);

                    if ((string?)section.Attribute("Del") == "1")
                    {
                        if (at >= 0)
                            figures.RemoveAt(at);

                        continue;
                    }

                    var figure = Lay(section, index, at >= 0 ? figures[at] : null);

                    if (at >= 0)
                        figures[at] = figure;
                    else
                        figures.Add(figure);
                }

                return figures;
            }
        }

        /// <summary>This sheet's words for one section, laid over what it inherits.</summary>
        private Figure Lay(XElement section, string? index, Figure? under)
        {
            bool? Says(string name)
            {
                var cell = section.Elements(V + "Cell")
                    .FirstOrDefault(entry => (string?)entry.Attribute("N") == name);

                return cell is null ? null : VisioFormat.Number((string?)cell.Attribute("V")) == 0;
            }

            var shown = Says("NoShow") ?? under?.Shown ?? true;
            var filled = Says("NoFill") ?? under?.Filled ?? true;

            var steps = new List<Step>(under?.Steps ?? []);

            foreach (var row in section.Elements(V + "Row"))
            {
                var ix = (string?)row.Attribute("IX");
                var at = ix is null ? -1 : steps.FindIndex(step => step.Index == ix);

                if ((string?)row.Attribute("Del") == "1")
                {
                    if (at >= 0)
                        steps.RemoveAt(at);

                    continue;
                }

                var basis = at >= 0 ? steps[at] : null;
                var cells = basis is null
                    ? new Dictionary<string, Slot>()
                    : new Dictionary<string, Slot>(basis.Cells);

                foreach (var cell in row.Elements(V + "Cell"))
                    if ((string?)cell.Attribute("N") is { } name)
                        cells[name] = new Slot(VisioFormat.Number((string?)cell.Attribute("V")), this);

                var step = new Step(ix, (string?)row.Attribute("T") ?? basis?.Kind ?? string.Empty, cells);

                if (at >= 0)
                    steps[at] = step;
                else
                    steps.Add(step);
            }

            return new Figure(index, shown, filled, steps);
        }
    }

    public static Result Read(Stream stream)
    {
        using var package = new ZipArchive(stream, ZipArchiveMode.Read);

        var pagesPart = Part(package, "visio/pages/pages.xml")
                        ?? throw new InvalidDataException("That is not a Visio drawing.");

        var masters = ReadMasters(package);
        var document = new DiagramDocument();
        var pages = new List<DiagramPage>();
        var skipped = new Dictionary<Tally, int>();
        var total = 0;

        // Which file holds which page, by the relationship its entry names.
        var links = Links(package, "visio/pages/_rels/pages.xml.rels");

        foreach (var element in pagesPart.Root?.Elements(V + "Page") ?? [])
        {
            // A background page is scenery for another one and has no place of its own here.
            if ((string?)element.Attribute("Background") == "1")
            {
                var tally = new Tally("background page", "background pages");
                skipped[tally] = skipped.GetValueOrDefault(tally) + 1;
                continue;
            }

            var sheet = element.Element(V + "PageSheet");
            var width = Cell(sheet, "PageWidth", 11);
            var height = Cell(sheet, "PageHeight", 8.5);

            var page = new DiagramPage((string?)element.Attribute("NameU") ?? $"Page {pages.Count + 1}")
            {
                Width = VisioFormat.ToPixels(width),
                Height = VisioFormat.ToPixels(height)
            };

            var relationship = element.Element(V + "Rel")?.Attribute(R + "id")?.Value;

            if (relationship is not null && links.TryGetValue(relationship, out var path) &&
                Part(package, "visio/pages/" + path) is { Root: not null } contents)
            {
                total += ReadPage(contents.Root, page, height, masters, skipped);
            }

            pages.Add(page);
        }

        if (pages.Count == 0)
            throw new InvalidDataException("That drawing has no pages.");

        document.SetPages(pages);
        document.MarkSaved();

        var left = skipped
            .OrderByDescending(entry => entry.Value)
            .Select(entry => entry.Key.Say(entry.Value))
            .ToList();

        return new Result(document, total, left);
    }

    #region Pages

    private static int ReadPage(
        XElement root, DiagramPage page, double pageHeight,
        IReadOnlyDictionary<string, Master> masters, Dictionary<Tally, int> skipped)
    {
        var byId = new Dictionary<string, DiagramShape>();
        var connectors = new List<(ConnectorShape Line, string Id)>();
        var count = 0;

        foreach (var element in root.Element(V + "Shapes")?.Elements(V + "Shape") ?? [])
            count += ReadShape(element, page, pageHeight, masters, null, Matrix.Identity, byId, connectors, skipped);

        // Glue is a page-level list of which end of which connector meets which shape.
        foreach (var connect in root.Element(V + "Connects")?.Elements(V + "Connect") ?? [])
        {
            var from = (string?)connect.Attribute("FromSheet");
            var to = (string?)connect.Attribute("ToSheet");
            var cell = (string?)connect.Attribute("FromCell") ?? string.Empty;

            if (from is null || to is null ||
                connectors.FirstOrDefault(entry => entry.Id == from).Line is not { } line ||
                !byId.TryGetValue(to, out var target))
                continue;

            // Visio says which part of the shape the line is stuck to: 3 means the shape
            // itself, and the line is free to leave from wherever suits, while 100 and up name
            // one connection point. Free glue is kept free rather than pinned to whatever
            // point happens to be nearest, because that is what it means.
            var part = (int)VisioFormat.Number((string?)connect.Attribute("ToPart"), 3);
            var port = part >= 100 ? part - 100 : -1;

            var end = cell.StartsWith("Begin", StringComparison.Ordinal);

            if (end)
            {
                line.StartShape = target;
                line.StartPort = port;
            }
            else
            {
                line.EndShape = target;
                line.EndPort = port;
            }
        }

        return count;
    }

    #endregion

    #region Shapes

    private static int ReadShape(
        XElement element, DiagramPage page, double pageHeight,
        IReadOnlyDictionary<string, Master> masters, Master? inherited, Matrix parent,
        Dictionary<string, DiagramShape> byId,
        List<(ConnectorShape Line, string Id)> connectors,
        Dictionary<Tally, int> skipped)
    {
        // A shape either stamps a whole master, or - inside a stamped group - answers to one
        // particular shape within the master its parent stamped, by that shape's own ID.
        if ((string?)element.Attribute("Master") is { } stamped && masters.TryGetValue(stamped, out var master))
            inherited = master;

        var stencil = (string?)element.Attribute("Master") is not null
            ? inherited?.Stamp
            : (string?)element.Attribute("MasterShape") is { } part &&
              inherited?.Parts.TryGetValue(part, out var piece) == true
                ? new Sheet(piece, null)
                : null;

        var sheet = new Sheet(element, stencil);

        var id = (string?)element.Attribute("ID") ?? string.Empty;
        var width = sheet.Number("Width");
        var height = sheet.Number("Height");

        var pin = new Point(sheet.Number("PinX"), sheet.Number("PinY"));
        var local = new Point(sheet.Number("LocPinX", width / 2), sheet.Number("LocPinY", height / 2));

        // Where the shape's own bottom-left corner sits on the page, before any group above it.
        var origin = new Point(pin.X - local.X, pin.Y - local.Y);
        var placed = origin.Transform(parent);

        // A connector is a shape with two ends rather than a box, and it stands for the whole
        // of itself: what a dynamic connector holds inside is its own label and arrowhead.
        if (sheet.Cell("BeginX") is not null && sheet.Cell("EndX") is not null)
        {
            var from = new Point(sheet.Number("BeginX"), sheet.Number("BeginY")).Transform(parent);
            var to = new Point(sheet.Number("EndX"), sheet.Number("EndY")).Transform(parent);

            var line = new ConnectorShape(
                VisioFormat.ToPage(from.X, from.Y, pageHeight),
                VisioFormat.ToPage(to.X, to.Y, pageHeight))
            {
                Routing = ConnectorRouting.Straight,
                EndCap = EndCapStyle.Arrow,
                Text = Words(element)
            };

            Paint(line, sheet, stroke: true);
            page.Shapes.Add(line);
            connectors.Add((line, id));

            return 1;
        }

        var count = 0;

        // A group holds its children in its own frame; the frame is carried down rather than
        // each child being given a position it does not have.
        var children = element.Element(V + "Shapes");

        if (children is not null)
        {
            var inside = Matrix.CreateTranslation(placed.X, placed.Y);

            foreach (var child in children.Elements(V + "Shape"))
                count += ReadShape(child, page, pageHeight, masters, inherited, inside, byId, connectors, skipped);
        }

        if (width <= 0 || height <= 0)
            return count;

        var outline = Outline(sheet, width, height);

        if (outline is not { } drawing)
        {
            // A group is a holder rather than something drawn; its children are already in.
            if (children is null)
            {
                var tally = new Tally("shape with no outline", "shapes with no outline");
                skipped[tally] = skipped.GetValueOrDefault(tally) + 1;
            }

            return count;
        }

        var bounds = new Rect(
            VisioFormat.ToPixels(placed.X),
            VisioFormat.ToPixels(pageHeight - placed.Y - height),
            VisioFormat.ToPixels(width),
            VisioFormat.ToPixels(height));

        var shape = Build(drawing.Body, drawing.Detail, bounds);

        shape.Text = Words(element);
        shape.Rotation = VisioFormat.ToDegrees(sheet.Number("Angle"));
        Lettering(shape, sheet);

        Paint(shape, sheet, stroke: false);

        page.Shapes.Add(shape);
        byId[id] = shape;

        return count + 1;
    }

    /// <summary>A rectangle stays a rectangle, so it can be resized and recognised.</summary>
    private static DiagramShape Build(string outline, string? detail, Rect bounds) =>
        outline == PathShape.Fallback && detail is null
            ? ShapeFactory.Create(ShapeKind.Rectangle, bounds)
            : new PathShape(bounds) { Outline = outline, Detail = detail };

    private static string Words(XElement element)
    {
        var text = element.Element(V + "Text");

        return text is null
            ? string.Empty
            : string.Concat(text.Nodes().OfType<System.Xml.Linq.XText>().Select(node => node.Value)).Trim();
    }

    /// <summary>How the shape's words are set: size, weight, colour, and which edge they hug.</summary>
    private static void Lettering(DiagramShape shape, Sheet sheet)
    {
        var size = VisioFormat.Number(sheet.Cell("Character", "Size"), 0.16667);
        shape.FontSize = Math.Max(6, VisioFormat.ToPixels(size));

        // Visio keeps the whole of a character's styling in one number, a bit to a trait.
        var style = (int)VisioFormat.Number(sheet.Cell("Character", "Style"));
        shape.Bold = (style & 1) != 0;
        shape.Italic = (style & 2) != 0;

        if (Colour(sheet.Cell("Character", "Color")) is { } ink)
            shape.TextColor = ink;

        if (sheet.Cell("Character", "Font") is { Length: > 0 } face)
            shape.FontName = face;

        shape.TextAlign = VisioFormat.Number(sheet.Cell("Paragraph", "HorzAlign"), 1) switch
        {
            0 => TextAlign.Left,
            2 => TextAlign.Right,
            _ => TextAlign.Center
        };
    }

    private static void Paint(DiagramShape shape, Sheet sheet, bool stroke)
    {
        shape.Stroke = Colour(sheet.Cell("LineColor")) ?? DiagramShape.DefaultStroke;

        if (!stroke)
            shape.Fill = Colour(sheet.Cell("FillForegnd")) ?? Colors.Transparent;

        // A line pattern of 0 is no line at all; anything past 1 is some sort of dash.
        var pattern = sheet.Number("LinePattern", 1);

        if (Math.Abs(pattern) < 0.5)
            shape.Stroke = Colors.Transparent;
        else if (pattern > 1.5)
            shape.StrokeStyle = pattern > 3.5 ? StrokeStyle.Dotted : StrokeStyle.Dashed;

        var weight = sheet.Number("LineWeight", 0.01);
        shape.StrokeThickness = Math.Clamp(VisioFormat.ToPixels(weight), 0.5, 20);
    }

    /// <summary>
    /// A colour cell is either a hex triplet or an index into a theme's palette. The palette
    /// is not read - it lives in another part and depends on the theme - so an index falls
    /// back to nothing rather than to a colour picked at random.
    /// </summary>
    private static Color? Colour(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();

        return value.StartsWith('#') && Color.TryParse(value, out var colour) ? colour : null;
    }

    #endregion

    #region Geometry

    /// <summary>
    /// A shape's outline in the unit square, from its geometry rows. Visio states them in
    /// inches from the shape's bottom-left corner with y running up, so they are turned into
    /// fractions of the shape and flipped, which is what the stencil language wants.
    ///
    /// Every number is first divided by the width of whichever sheet stated it - an inherited
    /// row is in the master's inches, an overridden one in the shape's own - so that the two
    /// meet on one scale. That leaves both axes measured in widths, which keeps circles round
    /// and angles honest while the rows are being read; the y axis is put back on the shape's
    /// own proportions only as each point is written out.
    /// </summary>
    private static (string Body, string? Detail)? Outline(Sheet sheet, double width, double height)
    {
        var figures = sheet.Geometry;

        if (figures.Count == 0 || width <= 0 || height <= 0)
            return null;

        var flipX = Math.Abs(sheet.Number("FlipX")) > 0.5;
        var flipY = Math.Abs(sheet.Number("FlipY")) > 0.5;
        var aspect = width / height;

        // A run Visio will not fill is kept apart, so the fill cannot swallow a line ruled
        // across the shape's own face.
        var path = new StringBuilder();
        var marks = new StringBuilder();
        var into = path;
        var drawn = false;

        string Unit(Point point)
        {
            var u = point.X;
            var v = 1 - point.Y * aspect;

            return $"{VisioFormat.Number(flipX ? 1 - u : u)},{VisioFormat.Number(flipY ? 1 - v : v)}";
        }

        void Curve(char command, params Point[] points) =>
            into.Append($"{command} {string.Join(" ", points.Select(Unit))} ");

        foreach (var figure in figures)
        {
            // A section can be marked as not drawn - a guide, or a hidden alternative.
            if (!figure.Shown)
                continue;

            into = figure.Filled ? path : marks;

            var cursor = new Point();
            var start = new Point();
            var open = false;

            void Begin(Point point)
            {
                if (open)
                    into.Append("Z ");

                cursor = start = point;
                Curve('M', point);
                open = true;
                drawn = true;
            }

            foreach (var step in figure.Steps)
            {
                var relative = step.Kind.StartsWith("Rel", StringComparison.Ordinal);

                // A relative row is already a fraction of the shape; an absolute one is in the
                // inches of whichever sheet wrote it.
                double Across(string name, double fallback = 0) =>
                    step.Cells.TryGetValue(name, out var slot)
                        ? relative ? slot.Value : slot.Value / slot.Owner.Number("Width", width)
                        : fallback;

                double Up(string name, double fallback = 0) =>
                    step.Cells.TryGetValue(name, out var slot)
                        ? relative ? slot.Value / aspect : slot.Value / slot.Owner.Number("Width", width)
                        : fallback;

                // An angle or a ratio is a number in its own right and is not a measurement.
                double Plain(string name, double fallback = 0) =>
                    step.Cells.TryGetValue(name, out var slot) ? slot.Value : fallback;

                Point At(string x, string y) => new(Across(x), Up(y));

                // A section whose opening move was inherited and then lost still has to start
                // somewhere rather than trail on from the section before it.
                if (!open && step.Kind is not ("MoveTo" or "RelMoveTo" or "Ellipse"))
                    Begin(cursor);

                switch (step.Kind.Replace("Rel", string.Empty))
                {
                    case "MoveTo":
                        Begin(At("X", "Y"));
                        break;

                    case "LineTo":
                    {
                        cursor = At("X", "Y");
                        Curve('L', cursor);
                        break;
                    }

                    case "ArcTo":
                    {
                        // A circular arc given by where it ends and how far it bows out from
                        // the straight line between the two ends.
                        var to = At("X", "Y");
                        Arc(Curve, cursor, Bulge(cursor, to, Across("A")), to, Matrix.Identity);
                        cursor = to;
                        break;
                    }

                    case "EllipticalArcTo":
                    {
                        // An elliptical arc through a given point, on an ellipse whose major
                        // axis lies at C radians and is D times its minor one. Squashing the
                        // plane by D along that axis turns the ellipse into a circle, so the
                        // arc is solved there and carried back.
                        var to = At("X", "Y");
                        var through = At("A", "B");
                        var ratio = Plain("D", 1);

                        var round = Matrix.CreateRotation(-Plain("C")) *
                                    Matrix.CreateScale(1, ratio == 0 ? 1 : ratio);

                        Arc(Curve, cursor, through, to, round);
                        cursor = to;
                        break;
                    }

                    case "Ellipse":
                    {
                        // A whole ellipse in one row: its centre, and a point on each axis.
                        var centre = At("X", "Y");
                        var major = At("A", "B") - centre;
                        var minor = At("C", "D") - centre;

                        Begin(centre + major);
                        Ellipse(Curve, centre, major, minor);
                        into.Append("Z ");
                        open = false;
                        break;
                    }

                    case "CubBezTo":
                    {
                        var to = At("X", "Y");
                        Curve('C', At("A", "B"), At("C", "D"), to);
                        cursor = to;
                        break;
                    }

                    case "QuadBezTo":
                    {
                        var to = At("X", "Y");
                        Curve('Q', At("A", "B"), to);
                        cursor = to;
                        break;
                    }

                    case "NURBSTo" or "PolylineTo" or "SplineStart" or "SplineKnot":
                    {
                        // Not read as curves; taken as a straight run to where they end up, so
                        // the outline still closes and the shape still has a body.
                        cursor = At("X", "Y");
                        Curve('L', cursor);
                        break;
                    }
                }
            }

            // Closed only when it comes back to where it started. An outline that stays open -
            // an arrow's flight, a bracket - should not be sewn shut behind the file's back.
            if (open && Point.Distance(cursor, start) < 1e-6)
                into.Append("Z ");
        }

        if (!drawn)
            return null;

        // A shape that is nothing but unfilled runs is a line drawing, and that is its body.
        return path.Length > 0
            ? (path.ToString().Trim(), marks.Length > 0 ? marks.ToString().Trim() : null)
            : (marks.ToString().Trim(), null);
    }

    /// <summary>The point an arc passes through, from its bow height.</summary>
    private static Point Bulge(Point from, Point to, double bow)
    {
        var middle = new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2);
        var run = to - from;
        var length = Math.Sqrt(run.X * run.X + run.Y * run.Y);

        if (length < 1e-9)
            return middle;

        return new Point(middle.X + run.Y / length * bow, middle.Y - run.X / length * bow);
    }

    /// <summary>
    /// An arc from one point to another through a third, written as cubics. <paramref
    /// name="round"/> is a transform that turns the arc's ellipse into a circle - the identity
    /// for one that is already circular - so a single circular solution serves both kinds.
    /// </summary>
    private static void Arc(Action<char, Point[]> curve, Point from, Point through, Point to, Matrix round)
    {
        if (!round.TryInvert(out var back))
            back = Matrix.Identity;

        var a = from.Transform(round);
        var b = through.Transform(round);
        var c = to.Transform(round);

        if (Centre(a, b, c) is not { } centre)
        {
            // Three points in a line have no arc through them.
            curve('L', [to]);
            return;
        }

        var radius = Point.Distance(centre, a);

        var startAngle = Math.Atan2(a.Y - centre.Y, a.X - centre.X);
        var midAngle = Math.Atan2(b.Y - centre.Y, b.X - centre.X);
        var endAngle = Math.Atan2(c.Y - centre.Y, c.X - centre.X);

        // The sweep is whichever way round gets from one end to the other by way of the
        // point the arc is known to pass through.
        var sweep = endAngle - startAngle;

        while (sweep <= 0)
            sweep += Math.Tau;

        var toMid = midAngle - startAngle;

        while (toMid <= 0)
            toMid += Math.Tau;

        if (toMid > sweep)
            sweep -= Math.Tau;

        // A cubic tracks a circle well up to a quarter turn and poorly past it.
        var steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 2) - 1e-9));
        var step = sweep / steps;
        var handle = 4.0 / 3 * Math.Tan(step / 4);

        for (var i = 0; i < steps; i++)
        {
            var one = startAngle + step * i;
            var two = one + step;

            Point On(double angle) => new(
                centre.X + radius * Math.Cos(angle),
                centre.Y + radius * Math.Sin(angle));

            var head = On(one);
            var tail = On(two);

            var first = new Point(head.X - radius * handle * Math.Sin(one), head.Y + radius * handle * Math.Cos(one));
            var second = new Point(tail.X + radius * handle * Math.Sin(two), tail.Y - radius * handle * Math.Cos(two));

            curve('C', [first.Transform(back), second.Transform(back), tail.Transform(back)]);
        }
    }

    /// <summary>A whole ellipse from its centre and its two axes, as four cubics.</summary>
    private static void Ellipse(Action<char, Point[]> curve, Point centre, Point major, Point minor)
    {
        // The handle length that makes a cubic sit on a quarter of a circle.
        const double Handle = 0.5522847498307933;

        var axes = new[] { major, minor, -major, -minor };

        for (var i = 0; i < 4; i++)
        {
            var from = axes[i];
            var to = axes[(i + 1) % 4];

            curve('C', [
                centre + from + to * Handle,
                centre + to + from * Handle,
                centre + to
            ]);
        }
    }

    /// <summary>The centre of the circle through three points, or nothing if they are in a line.</summary>
    private static Point? Centre(Point a, Point b, Point c)
    {
        var d = 2 * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));

        if (Math.Abs(d) < 1e-12)
            return null;

        var a2 = a.X * a.X + a.Y * a.Y;
        var b2 = b.X * b.X + b.Y * b.Y;
        var c2 = c.X * c.X + c.Y * c.Y;

        return new Point(
            (a2 * (b.Y - c.Y) + b2 * (c.Y - a.Y) + c2 * (a.Y - b.Y)) / d,
            (a2 * (c.X - b.X) + b2 * (a.X - c.X) + c2 * (b.X - a.X)) / d);
    }

    #endregion

    #region The package

    private static IReadOnlyDictionary<string, Master> ReadMasters(ZipArchive package)
    {
        var masters = new Dictionary<string, Master>();
        var index = Part(package, "visio/masters/masters.xml");

        if (index?.Root is null)
            return masters;

        var links = Links(package, "visio/masters/_rels/masters.xml.rels");

        foreach (var master in index.Root.Elements(V + "Master"))
        {
            var id = (string?)master.Attribute("ID");
            var relationship = master.Element(V + "Rel")?.Attribute(R + "id")?.Value;

            if (id is null || relationship is null || !links.TryGetValue(relationship, out var path))
                continue;

            // The master's own first shape is what a stamp of it looks like; the rest of the
            // shapes in it are what an instance's own children inherit from, one for one.
            var contents = Part(package, "visio/masters/" + path);
            var shape = contents?.Root?.Element(V + "Shapes")?.Elements(V + "Shape").FirstOrDefault();

            if (shape is null)
                continue;

            var parts = new Dictionary<string, XElement>();

            void Index(XElement element)
            {
                if ((string?)element.Attribute("ID") is { } key)
                    parts[key] = element;

                foreach (var child in element.Element(V + "Shapes")?.Elements(V + "Shape") ?? [])
                    Index(child);
            }

            Index(shape);
            masters[id] = new Master(new Sheet(shape, null), parts);
        }

        return masters;
    }

    private static double Cell(XElement? sheet, string name, double fallback)
    {
        var cell = sheet?.Elements(V + "Cell").FirstOrDefault(c => (string?)c.Attribute("N") == name);
        return cell is null ? fallback : VisioFormat.Number((string?)cell.Attribute("V"), fallback);
    }

    private static string? Cell(XElement section, string name) => section
        .Elements(V + "Cell")
        .FirstOrDefault(cell => (string?)cell.Attribute("N") == name)
        ?.Attribute("V")?.Value;

    private static string? RowCell(XElement row, string name) => row
        .Elements(V + "Cell")
        .FirstOrDefault(cell => (string?)cell.Attribute("N") == name)
        ?.Attribute("V")?.Value;

    private static XDocument? Part(ZipArchive package, string path)
    {
        var entry = package.GetEntry(path);

        if (entry is null)
            return null;

        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    /// <summary>A relationships part, as id to target.</summary>
    private static Dictionary<string, string> Links(ZipArchive package, string path)
    {
        var links = new Dictionary<string, string>();
        var part = Part(package, path);

        foreach (var relationship in part?.Root?.Elements() ?? [])
        {
            var id = (string?)relationship.Attribute("Id");
            var target = (string?)relationship.Attribute("Target");

            if (id is not null && target is not null)
                links[id] = target;
        }

        return links;
    }

    #endregion
}
