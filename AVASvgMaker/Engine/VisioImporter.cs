using System;
using System.Collections.Generic;
using System.Globalization;
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

    /// <summary>
    /// Everything that stays the same while one page is read: what to look things up in, what
    /// is being filled in, and what has been left out along the way. Gathered here so that
    /// reading a shape takes the shape and its frame, rather than eight things and a shape.
    /// </summary>
    private sealed record Reading(
        IReadOnlyDictionary<string, Master> Masters,
        Palette Palette,
        Styles Styles,
        DiagramPage Page,
        double PageHeight,
        Dictionary<string, DiagramShape> ById,
        List<(ConnectorShape Line, string Id)> Connectors,
        Dictionary<Tally, int> Skipped);

    /// <summary>Something left out, counted, and able to say itself for any number of it.</summary>
    private sealed record Tally(string One, string Many)
    {
        public string Say(int count) => count == 1 ? $"1 {One}" : $"{count} {Many}";
    }

    /// <summary>
    /// The drawing's style sheets: named sets of formatting that a shape can take its line,
    /// its fill or its text from rather than stating them itself.
    ///
    /// A shape points at up to three of them at once - one for each of those - and a style
    /// sheet may point at another in turn, so a drawing's ordinary formatting is often several
    /// links away from the shape that shows it. Which chain a cell follows is decided by what
    /// the cell is: LineWeight is a line matter, FillPattern a fill one, and a cell that is
    /// none of the three is not a style's business and is not looked for in one.
    /// </summary>
    private sealed class Styles
    {
        public static readonly Styles Empty = new(new Dictionary<string, XElement>());

        private readonly IReadOnlyDictionary<string, XElement> _sheets;

        private Styles(IReadOnlyDictionary<string, XElement> sheets) => _sheets = sheets;

        public static Styles Read(ZipArchive package)
        {
            var sheets = new Dictionary<string, XElement>();
            var document = Part(package, "visio/document.xml");

            foreach (var sheet in document?.Root?.Element(V + "StyleSheets")?.Elements(V + "StyleSheet") ?? [])
                if ((string?)sheet.Attribute("ID") is { } id)
                    sheets[id] = sheet;

            return sheets.Count == 0 ? Empty : new Styles(sheets);
        }

        /// <summary>Which of a shape's three styles a cell is answered by, if any.</summary>
        private static string? Chain(string name)
        {
            // The variation is one number the whole shape is coloured out of rather than a
            // line matter, but a style that states it states it for all three, so the line's
            // chain will do to find it by.
            if (name.StartsWith("Line", StringComparison.Ordinal) ||
                name.StartsWith("QuickStyleLine", StringComparison.Ordinal) ||
                name is "BeginArrow" or "EndArrow" or "BeginArrowSize" or "EndArrowSize"
                     or "Rounding" or "QuickStyleVariation")
                return "LineStyle";

            if (name.StartsWith("Fill", StringComparison.Ordinal) ||
                name.StartsWith("Shdw", StringComparison.Ordinal) ||
                name.StartsWith("QuickStyleFill", StringComparison.Ordinal))
                return "FillStyle";

            if (name.StartsWith("Txt", StringComparison.Ordinal) ||
                name.StartsWith("QuickStyleFont", StringComparison.Ordinal) ||
                name.EndsWith("Margin", StringComparison.Ordinal) ||
                name is "VerticalAlign" or "TextBkgnd")
                return "TextStyle";

            return null;
        }

        /// <summary>A cell, from whichever style the sheet in hand hands it on to.</summary>
        public string? Cell(XElement owner, string name) =>
            Chain(name) is { } chain ? Walk(owner, chain, sheet => Value(sheet, name)) : null;

        /// <summary>A cell from the first row of a named section, down the text style's chain.</summary>
        public string? Cell(XElement owner, string section, string name) =>
            Walk(owner, "TextStyle", sheet => Value(sheet, section, name));

        /// <summary>
        /// Follows one of the three chains from the shape outwards, stopping at the first
        /// style that has something to say. A style that points at itself, or a pair that
        /// point at each other, would otherwise go round for ever - so the walk is bounded by
        /// the number of sheets there are.
        /// </summary>
        private string? Walk(XElement owner, string chain, Func<XElement, string?> read)
        {
            var at = (string?)owner.Attribute(chain);

            for (var step = 0; step <= _sheets.Count && at is not null; step++)
            {
                if (!_sheets.TryGetValue(at, out var sheet))
                    return null;

                if (read(sheet) is { } value)
                    return value;

                at = (string?)sheet.Attribute(chain);
            }

            return null;
        }

        private static string? Value(XElement sheet, string name) => sheet
            .Elements(V + "Cell")
            .FirstOrDefault(cell => (string?)cell.Attribute("N") == name)
            ?.Attribute("V")?.Value;

        private static string? Value(XElement sheet, string section, string name) => sheet
            .Elements(V + "Section")
            .FirstOrDefault(part => (string?)part.Attribute("N") == section)
            ?.Elements(V + "Row").FirstOrDefault()
            ?.Elements(V + "Cell")
            .FirstOrDefault(cell => (string?)cell.Attribute("N") == name)
            ?.Attribute("V")?.Value;
    }

    /// <summary>
    /// One number in a geometry row, and the sheet whose inches it is stated in. A cell also
    /// keeps the formula behind its value, because the rows that draw a spline put the whole
    /// run of control points in there and leave the value to name only where it ends.
    /// </summary>
    private sealed record Slot(double Value, Sheet Owner, string? Formula = null);

    /// <summary>A geometry row once it has inherited what it did not restate.</summary>
    private sealed record Step(string? Index, string Kind, IReadOnlyDictionary<string, Slot> Cells);

    /// <summary>A geometry section once it has inherited what it did not restate.</summary>
    private sealed record Figure(string? Index, bool Shown, bool Filled, IReadOnlyList<Step> Steps);

    private sealed record Sheet(XElement Element, Sheet? Master, Styles Styles)
    {
        /// <summary>
        /// What the shape says, then what its master says, then what the styles either of them
        /// points at say. A style is asked last: it is where a drawing keeps the formatting it
        /// has not troubled to state, so anything actually stated outranks it.
        /// </summary>
        public string? Cell(string name)
        {
            var own = Element.Elements(V + "Cell")
                .FirstOrDefault(cell => (string?)cell.Attribute("N") == name);

            if (own is not null)
                return (string?)own.Attribute("V");

            return Master?.Cell(name) ?? Styles.Cell(Element, name);
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

            if (own is not null)
                return (string?)own.Attribute("V");

            return Master?.Cell(section, name) ?? Styles.Cell(Element, section, name);
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
                        cells[name] = new Slot(
                            VisioFormat.Number((string?)cell.Attribute("V")),
                            this,
                            (string?)cell.Attribute("F"));

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

        var styles = Styles.Read(package);
        var masters = ReadMasters(package, styles);
        var palette = Palette.Read(package);
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
                total += ReadPage(contents.Root, page, height, masters, palette, styles, skipped);
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
        IReadOnlyDictionary<string, Master> masters, Palette palette, Styles styles,
        Dictionary<Tally, int> skipped)
    {
        var reading = new Reading(masters, palette, styles, page, pageHeight,
            new Dictionary<string, DiagramShape>(), [], skipped);

        var count = 0;

        foreach (var element in root.Element(V + "Shapes")?.Elements(V + "Shape") ?? [])
            count += ReadShape(element, reading, null, Matrix.Identity);

        // Glue is a page-level list of which end of which connector meets which shape.
        foreach (var connect in root.Element(V + "Connects")?.Elements(V + "Connect") ?? [])
        {
            var from = (string?)connect.Attribute("FromSheet");
            var to = (string?)connect.Attribute("ToSheet");
            var cell = (string?)connect.Attribute("FromCell") ?? string.Empty;

            if (from is null || to is null ||
                reading.Connectors.FirstOrDefault(entry => entry.Id == from).Line is not { } line ||
                !reading.ById.TryGetValue(to, out var target))
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

    private static int ReadShape(XElement element, Reading reading, Master? inherited, Matrix parent)
    {
        // A shape either stamps a whole master, or - inside a stamped group - answers to one
        // particular shape within the master its parent stamped, by that shape's own ID.
        if ((string?)element.Attribute("Master") is { } stamped &&
            reading.Masters.TryGetValue(stamped, out var master))
            inherited = master;

        var stencil = (string?)element.Attribute("Master") is not null
            ? inherited?.Stamp
            : (string?)element.Attribute("MasterShape") is { } part &&
              inherited?.Parts.TryGetValue(part, out var piece) == true
                ? new Sheet(piece, null, reading.Styles)
                : null;

        var sheet = new Sheet(element, stencil, reading.Styles);

        var id = (string?)element.Attribute("ID") ?? string.Empty;
        var width = sheet.Number("Width");
        var height = sheet.Number("Height");

        var pin = new Point(sheet.Number("PinX"), sheet.Number("PinY"));
        var local = new Point(sheet.Number("LocPinX", width / 2), sheet.Number("LocPinY", height / 2));

        // Where this shape's own coordinates land in the page's. A group hands the very same
        // frame to everything inside it, which is why a group that has been turned turns what
        // it holds rather than sliding it sideways.
        var mine = Placement(sheet, pin, local) * parent;

        // A connector is a shape with two ends rather than a box, and it stands for the whole
        // of itself: what a dynamic connector holds inside is its own label and arrowhead.
        if (sheet.Cell("BeginX") is not null && sheet.Cell("EndX") is not null)
        {
            var from = new Point(sheet.Number("BeginX"), sheet.Number("BeginY")).Transform(parent);
            var to = new Point(sheet.Number("EndX"), sheet.Number("EndY")).Transform(parent);

            var line = new ConnectorShape(
                VisioFormat.ToPage(from.X, from.Y, reading.PageHeight),
                VisioFormat.ToPage(to.X, to.Y, reading.PageHeight))
            {
                Routing = ConnectorRouting.Straight,
                StartCap = Cap(sheet, "BeginArrow"),
                EndCap = Cap(sheet, "EndArrow"),
                Text = Words(element)
            };

            Paint(line, sheet, reading.Palette, stroke: true);
            reading.Page.Shapes.Add(line);
            reading.Connectors.Add((line, id));

            return 1;
        }

        var count = 0;

        // A group holds its children in its own frame; the frame is carried down rather than
        // each child being given a position it does not have.
        var children = element.Element(V + "Shapes");

        if (children is not null)
            foreach (var child in children.Elements(V + "Shape"))
                count += ReadShape(child, reading, inherited, mine);

        if (width <= 0 || height <= 0)
            return count;

        // The box the shape ends up occupying, and how it ends up sitting in it. Anything
        // above the shape may have turned it, flipped it or stretched it, so the answer comes
        // from the frame itself rather than from the shape's own cells.
        var (bounds, turn, mirrored) = Sits(mine, width, height, reading.PageHeight);

        var outline = Outline(sheet, width, height, mirrored);

        if (outline is not { } drawing)
        {
            // A group is a holder rather than something drawn; its children are already in.
            if (children is null)
            {
                var tally = new Tally("shape with no outline", "shapes with no outline");
                reading.Skipped[tally] = reading.Skipped.GetValueOrDefault(tally) + 1;
            }

            return count;
        }

        var shape = Build(drawing.Body, drawing.Detail, bounds);

        shape.Text = Words(element);
        shape.Rotation = turn;

        Lettering(shape, sheet, reading.Palette);
        Block(shape, sheet, width, height, mirrored);
        Paint(shape, sheet, reading.Palette, stroke: false);

        reading.Page.Shapes.Add(shape);
        reading.ById[id] = shape;

        return count + 1;
    }

    /// <summary>
    /// A shape's own coordinates as its parent sees them. Visio turns and flips a shape about
    /// its local pin, and then puts that pin where PinX and PinY say. A group hands the result
    /// on to everything inside it unchanged, so this is also the frame a group's contents are
    /// drawn in.
    /// </summary>
    private static Matrix Placement(Sheet sheet, Point pin, Point local)
    {
        var flipX = Math.Abs(sheet.Number("FlipX")) > 0.5;
        var flipY = Math.Abs(sheet.Number("FlipY")) > 0.5;
        var angle = sheet.Number("Angle");

        var turn = Matrix.Identity;

        if (flipX || flipY)
            turn *= Matrix.CreateScale(flipX ? -1 : 1, flipY ? -1 : 1);

        if (Math.Abs(angle) > 1e-9)
            turn *= Matrix.CreateRotation(angle);

        // Both happen about the local pin, so the shape is carried to the origin and back.
        return Matrix.CreateTranslation(-local.X, -local.Y) *
               turn *
               Matrix.CreateTranslation(pin.X, pin.Y);
    }

    /// <summary>
    /// Where a shape of the given size ends up once its frame is applied: the box it occupies,
    /// the angle it sits at, and whether it has been turned over.
    ///
    /// The model here holds an upright box and an angle, and has no room for a mirror - but
    /// every mirrored frame is some turn of a shape flipped once, so the flip is handed back
    /// to be folded into the outline and what is left is an angle like any other.
    /// </summary>
    private static (Rect Bounds, double Turn, bool Mirrored) Sits(
        Matrix frame, double width, double height, double pageHeight)
    {
        Point At(double x, double y) => new Point(x, y).Transform(frame);

        var centre = At(width / 2, height / 2);
        var across = At(width, height / 2) - At(0, height / 2);
        var down = At(width / 2, height) - At(width / 2, 0);

        var wide = Math.Sqrt(across.X * across.X + across.Y * across.Y);
        var tall = Math.Sqrt(down.X * down.X + down.Y * down.Y);

        // A frame that has been turned over reverses which way round its two axes go.
        var mirrored = across.X * down.Y - across.Y * down.X < 0;

        var middle = VisioFormat.ToPage(centre.X, centre.Y, pageHeight);

        var bounds = new Rect(
            middle.X - VisioFormat.ToPixels(wide) / 2,
            middle.Y - VisioFormat.ToPixels(tall) / 2,
            VisioFormat.ToPixels(wide),
            VisioFormat.ToPixels(tall));

        return (bounds, VisioFormat.ToDegrees(Math.Atan2(across.Y, across.X)), mirrored);
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

    /// <summary>
    /// What one end of a connector is drawn with. Visio numbers its line ends out of a gallery
    /// of some forty-odd; the twelve here are a different set, chosen for UML and
    /// entity-relationship notation, and the two do not map onto one another. Nothing in this
    /// file, nor anything to hand, says which number is which shape - so only the part that
    /// can be read for certain is read: whether there is an end at all.
    ///
    /// It matters more than it sounds. Every imported connector used to be given an arrow
    /// whatever the drawing said, which put arrowheads on the plain associations of a use-case
    /// diagram that never had any.
    /// </summary>
    private static EndCapStyle Cap(Sheet sheet, string cell) =>
        Math.Abs(sheet.Number(cell)) < 0.5 ? EndCapStyle.None : EndCapStyle.Arrow;

    /// <summary>How the shape's words are set: size, weight, colour, and which edge they hug.</summary>
    private static void Lettering(DiagramShape shape, Sheet sheet, Palette palette)
    {
        var size = VisioFormat.Number(sheet.Cell("Character", "Size"), 0.16667);
        shape.FontSize = Math.Max(6, VisioFormat.ToPixels(size));

        // Visio keeps the whole of a character's styling in one number, a bit to a trait.
        var style = (int)VisioFormat.Number(sheet.Cell("Character", "Style"));
        shape.Bold = (style & 1) != 0;
        shape.Italic = (style & 2) != 0;

        if (Colour(sheet.Cell("Character", "Color"), sheet, palette, "QuickStyleFontColor") is { } ink)
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

    /// <summary>
    /// The block the shape's words go in. Visio gives it a size and a pin of its own, in the
    /// shape's coordinates, and it need not sit on the shape at all - the name under a stick
    /// figure hangs below it. Where the block is the shape, nothing is recorded, which keeps
    /// it out of the way of every shape that does not need it.
    /// </summary>
    private static void Block(DiagramShape shape, Sheet sheet, double width, double height, bool mirrored)
    {
        shape.TextVerticalAlign = sheet.Number("VerticalAlign", 1) switch
        {
            0 => TextVerticalAlign.Top,
            2 => TextVerticalAlign.Bottom,
            _ => TextVerticalAlign.Middle
        };

        if (width <= 0 || height <= 0)
            return;

        var blockWidth = sheet.Number("TxtWidth", width);
        var blockHeight = sheet.Number("TxtHeight", height);

        var pin = new Point(sheet.Number("TxtPinX", width / 2), sheet.Number("TxtPinY", height / 2));
        var local = new Point(
            sheet.Number("TxtLocPinX", blockWidth / 2),
            sheet.Number("TxtLocPinY", blockHeight / 2));

        // Visio measures up from the bottom of the shape and we measure down from its top.
        var left = (pin.X - local.X) / width;
        var top = 1 - (pin.Y - local.Y + blockHeight) / height;

        var frame = new Rect(left, mirrored ? 1 - top - blockHeight / height : top,
            blockWidth / width, blockHeight / height);

        // A block that is simply the shape is not worth recording.
        if (Math.Abs(frame.X) > 1e-6 || Math.Abs(frame.Y) > 1e-6 ||
            Math.Abs(frame.Width - 1) > 1e-6 || Math.Abs(frame.Height - 1) > 1e-6)
            shape.TextFrame = frame;
    }

    private static void Paint(DiagramShape shape, Sheet sheet, Palette palette, bool stroke)
    {
        shape.Stroke = Colour(sheet.Cell("LineColor"), sheet, palette, "QuickStyleLineColor")
                       ?? DiagramShape.DefaultStroke;

        if (!stroke)
        {
            // A fill pattern of 0 means the shape is not filled at all, whatever colour it
            // may name. Anything else is taken as solid: the hatchings and gradients Visio
            // can fill with have nothing here to become.
            var filled = Math.Abs(VisioFormat.Number(sheet.Cell("FillPattern"), 1)) >= 0.5;

            // Only a colour the shape states outright is used to fill it. A themed fill is
            // tinted through a quick-style matrix that is not read here, and a shape painted
            // solid in its own line colour would be further from the truth than an empty one.
            shape.Fill = filled ? Stated(sheet.Cell("FillForegnd"), palette) ?? Colors.Transparent
                                : Colors.Transparent;
        }

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
    /// A colour cell, in whichever of its forms it is written: a hex triplet, a number into
    /// the drawing's table of colours, or the word "Themed".
    ///
    /// A shape that has been given a quick style very often states no colour at all - the cell
    /// is simply absent, and the colour it is drawn in comes from the theme by way of the
    /// shape's quick-style cells. So saying nothing and saying "Themed" mean the same thing
    /// here, and both end up asking the theme.
    /// </summary>
    private static Color? Colour(string? value, Sheet sheet, Palette palette, string quickStyle) =>
        Stated(value, palette)
        ?? palette.Themed(sheet.Number("QuickStyleVariation"), sheet.Number(quickStyle, -1));

    /// <summary>
    /// A colour the cell states for itself - a hex triplet, or a number into the drawing's
    /// table of colours. Nothing, when the cell is absent or defers to the theme.
    /// </summary>
    private static Color? Stated(string? value, Palette palette)
    {
        value = value?.Trim();

        if (string.IsNullOrEmpty(value) ||
            string.Equals(value, "Themed", StringComparison.OrdinalIgnoreCase))
            return null;

        if (value.StartsWith('#'))
            return Color.TryParse(value, out var written) ? written : null;

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var index)
            ? palette.Numbered((int)index)
            : null;
    }


    #endregion

    #region Colour

    /// <summary>
    /// The colours a drawing points at rather than states.
    ///
    /// A colour cell holds one of three things: a hex triplet, a number into a table of
    /// colours, or the word "Themed" - which means the shape takes its colour from the theme,
    /// and says which one it wants through its quick-style cells. All three are here.
    /// </summary>
    private sealed record Palette(
        IReadOnlyDictionary<int, Color> Table,
        IReadOnlyList<IReadOnlyList<Color>> Variations)
    {
        /// <summary>
        /// Visio's own two dozen colours, which a drawing numbers without writing down. A
        /// document may add its own past the end of these, and does when it has resolved a
        /// theme colour and wants to name it again.
        /// </summary>
        private static readonly string[] Standard =
        [
            "#000000", "#FFFFFF", "#FF0000", "#00FF00", "#0000FF", "#FFFF00",
            "#FF00FF", "#00FFFF", "#800000", "#008000", "#000080", "#808000",
            "#800080", "#008080", "#C0C0C0", "#808080", "#9999FF", "#993366",
            "#FFFFCC", "#CCFFFF", "#660066", "#FF8080", "#0066CC", "#CCCCFF"
        ];

        public static readonly Palette Empty = new(new Dictionary<int, Color>(), []);

        public static Palette Read(ZipArchive package)
        {
            var table = new Dictionary<int, Color>();

            for (var i = 0; i < Standard.Length; i++)
                if (Color.TryParse(Standard[i], out var colour))
                    table[i] = colour;

            var document = Part(package, "visio/document.xml");

            foreach (var entry in document?.Root?.Element(V + "Colors")?.Elements(V + "ColorEntry") ?? [])
                if (int.TryParse((string?)entry.Attribute("IX"), out var index) &&
                    Color.TryParse((string?)entry.Attribute("RGB") ?? string.Empty, out var colour))
                    table[index] = colour;

            return new Palette(table, Theme(package));
        }

        /// <summary>
        /// The theme's variations. Visio keeps them in the theme part beside the Office colour
        /// scheme, in an extension of its own: a list of variations, each seven colours, which
        /// is what a shape's quick-style cells count along.
        /// </summary>
        private static IReadOnlyList<IReadOnlyList<Color>> Theme(ZipArchive package)
        {
            XNamespace drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
            XNamespace themed = "http://schemas.microsoft.com/office/visio/2012/theme";

            // A relationship's target is relative to the folder its own part sits in.
            var part = Part(package, "visio/" + (Target(package, "visio/_rels/document.xml.rels", "theme")
                                                 ?? "theme/theme1.xml"));

            var variations = new List<IReadOnlyList<Color>>();

            foreach (var scheme in part?.Root?.Descendants(themed + "variationClrScheme") ?? [])
            {
                var colours = new List<Color>();

                // The seven are named rather than listed, so they are asked for by name.
                for (var i = 1; i <= 7; i++)
                {
                    var value = (string?)scheme.Element(themed + $"varColor{i}")
                        ?.Element(drawing + "srgbClr")?.Attribute("val");

                    if (value is not null && Color.TryParse("#" + value.TrimStart('#'), out var colour))
                        colours.Add(colour);
                }

                if (colours.Count > 0)
                    variations.Add(colours);
            }

            return variations;
        }

        /// <summary>A numbered colour. 255 is Visio's way of saying it has none.</summary>
        public Color? Numbered(int index) =>
            index != 255 && Table.TryGetValue(index, out var colour) ? colour : null;

        /// <summary>
        /// The colour a quick-style cell asks for. Values from 100 up count along the chosen
        /// variation's seven; below that they name a slot in a matrix this does not read, and
        /// so are left to the default rather than guessed at.
        /// </summary>
        public Color? Themed(double variation, double wanted)
        {
            if (wanted < 100 || Variations.Count == 0)
                return null;

            var scheme = Variations[Math.Clamp((int)variation, 0, Variations.Count - 1)];
            var slot = (int)wanted - 100;

            return slot >= 0 && slot < scheme.Count ? scheme[slot] : null;
        }
    }

    /// <summary>The first relationship of a kind, by the tail of its type.</summary>
    private static string? Target(ZipArchive package, string rels, string kind)
    {
        var part = Part(package, rels);

        return part?.Root?.Elements()
            .FirstOrDefault(link => ((string?)link.Attribute("Type"))?.EndsWith("/" + kind) == true)
            ?.Attribute("Target")?.Value;
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
    private static (string Body, string? Detail)? Outline(
        Sheet sheet, double width, double height, bool mirrored)
    {
        var figures = sheet.Geometry;

        if (figures.Count == 0 || width <= 0 || height <= 0)
            return null;

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

            return $"{VisioFormat.Number(u)},{VisioFormat.Number(mirrored ? 1 - v : v)}";
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

                // Numbers written into a formula are in the inches of the sheet that wrote the
                // row they sit in, the same as the row's own cells.
                var frame = step.Cells.Values
                    .Select(slot => slot.Owner.Number("Width", width))
                    .FirstOrDefault(width);

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

                    case "PolylineTo":
                    {
                        // A run of straight edges kept in a formula, with the row's own cells
                        // naming only the last of them.
                        var to = At("X", "Y");

                        foreach (var corner in Corners(step, "A", "POLYLINE", 2, frame, aspect))
                            Curve('L', corner);

                        Curve('L', to);
                        cursor = to;
                        break;
                    }

                    case "NURBSTo":
                    {
                        // A curve through control points kept in a formula: four numbers each,
                        // being the point, its knot and its weight, after a leading knot,
                        // degree and the pair of flags the coordinates are measured by.
                        var to = At("X", "Y");
                        var numbers = Arguments(step.Cells.GetValueOrDefault("E")?.Formula, "NURBS");

                        if (numbers is null || numbers.Count < 8)
                        {
                            Curve('L', to);
                            cursor = to;
                            break;
                        }

                        var degree = (int)numbers[1];
                        var control = new List<Point> { cursor };
                        var weights = new List<double> { 1 };

                        for (var i = 4; i + 3 < numbers.Count; i += 4)
                        {
                            control.Add(Measured(numbers[i], numbers[i + 1], numbers[2], numbers[3], frame, aspect));
                            weights.Add(numbers[i + 3]);
                        }

                        control.Add(to);
                        weights.Add(Plain("B", 1));

                        foreach (var along in Spline(control, weights, degree))
                            Curve('L', along);

                        cursor = to;
                        break;
                    }

                    case "SplineStart" or "SplineKnot":
                    {
                        // A spline states one control point per row, and how its knots run
                        // cannot be settled from the file alone - so the run of points itself
                        // is drawn, which is the shape the curve is pulled towards.
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

    /// <summary>
    /// A point from a formula's own pair of numbers, measured the way its flags say: a flag of
    /// 0 means a fraction of the shape, anything else means the sheet's own inches.
    /// </summary>
    private static Point Measured(double x, double y, double xKind, double yKind, double frame, double aspect) =>
        new(Math.Abs(xKind) < 0.5 ? x : x / frame,
            Math.Abs(yKind) < 0.5 ? y / aspect : y / frame);

    /// <summary>The corners a POLYLINE formula lists, in the same measure as everything else.</summary>
    private static IEnumerable<Point> Corners(
        Step step, string cell, string function, int lead, double frame, double aspect)
    {
        var numbers = Arguments(step.Cells.GetValueOrDefault(cell)?.Formula, function);

        if (numbers is null || numbers.Count < lead + 2)
            yield break;

        for (var i = lead; i + 1 < numbers.Count; i += 2)
            yield return Measured(numbers[i], numbers[i + 1], numbers[0], numbers[1], frame, aspect);
    }

    /// <summary>
    /// The numbers inside a function written in a formula - POLYLINE(...) or NURBS(...) - or
    /// nothing when the formula is not that function. Visio keeps the whole run of points a
    /// spline is drawn through in there, leaving the row's own cells to name only where it
    /// ends up.
    /// </summary>
    private static IReadOnlyList<double>? Arguments(string? formula, string function)
    {
        if (formula is null)
            return null;

        var start = formula.IndexOf(function + "(", StringComparison.OrdinalIgnoreCase);

        if (start < 0)
            return null;

        start += function.Length + 1;
        var end = formula.LastIndexOf(')');

        if (end <= start)
            return null;

        var numbers = new List<double>();

        foreach (var part in formula[start..end].Split(','))
        {
            if (!double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return null;

            numbers.Add(value);
        }

        return numbers;
    }

    /// <summary>
    /// Points along a B-spline of the given degree through its control points, at a handful of
    /// samples per span.
    ///
    /// The knots are taken to be clamped and evenly spaced. Visio states one knot per control
    /// point rather than the whole vector, and what it leaves out cannot be settled from the
    /// file alone - so the reading here is the ordinary one, which pins the curve's two ends to
    /// the first and last control points. Should the drawing have meant something else, the
    /// curve still begins and ends where it should and still lies inside the run of points that
    /// shapes it, which is a bounded sort of wrong.
    /// </summary>
    private static IEnumerable<Point> Spline(IReadOnlyList<Point> control, IReadOnlyList<double> weights, int degree)
    {
        degree = Math.Clamp(degree, 1, Math.Max(1, control.Count - 1));

        if (control.Count <= degree)
        {
            // Too few points to curve through; the run of them is the best there is.
            foreach (var point in control.Skip(1))
                yield return point;

            yield break;
        }

        var count = control.Count;
        var knots = new double[count + degree + 1];

        for (var i = 0; i < knots.Length; i++)
            knots[i] = Math.Clamp(i - degree, 0, count - degree) / (double)(count - degree);

        // Enough samples that a span reads as a curve rather than as a run of corners.
        var steps = Math.Max(16, 12 * (count - degree));

        for (var step = 1; step <= steps; step++)
        {
            var t = step / (double)steps;

            // The last sample sits exactly on the final control point rather than a hair short.
            yield return t >= 1 ? control[^1] : DeBoor(control, weights, knots, degree, t);
        }
    }

    /// <summary>
    /// One point on the curve, by de Boor's algorithm. The control points are carried with
    /// their weights and divided back out at the end, which is what makes it rational - Visio
    /// weights a control point to pull the curve towards it, and a circle drawn as a spline
    /// is not a circle without that.
    /// </summary>
    private static Point DeBoor(
        IReadOnlyList<Point> control, IReadOnlyList<double> weights, double[] knots, int degree, double t)
    {
        // The span t falls in.
        var span = degree;

        while (span < control.Count - 1 && t >= knots[span + 1])
            span++;

        var x = new double[degree + 1];
        var y = new double[degree + 1];
        var w = new double[degree + 1];

        for (var i = 0; i <= degree; i++)
        {
            var at = span - degree + i;
            var weight = at < weights.Count ? weights[at] : 1;

            if (Math.Abs(weight) < 1e-9)
                weight = 1;

            x[i] = control[at].X * weight;
            y[i] = control[at].Y * weight;
            w[i] = weight;
        }

        for (var round = 1; round <= degree; round++)
            for (var i = degree; i >= round; i--)
            {
                var at = span - degree + i;
                var low = knots[at];
                var high = knots[at + degree - round + 1];
                var share = high - low < 1e-12 ? 0 : (t - low) / (high - low);

                x[i] = (1 - share) * x[i - 1] + share * x[i];
                y[i] = (1 - share) * y[i - 1] + share * y[i];
                w[i] = (1 - share) * w[i - 1] + share * w[i];
            }

        return Math.Abs(w[degree]) < 1e-12
            ? new Point(x[degree], y[degree])
            : new Point(x[degree] / w[degree], y[degree] / w[degree]);
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

    private static IReadOnlyDictionary<string, Master> ReadMasters(ZipArchive package, Styles styles)
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
            masters[id] = new Master(new Sheet(shape, null, styles), parts);
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
