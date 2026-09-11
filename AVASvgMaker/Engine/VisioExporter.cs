using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Media;
using AVASvgMaker.Models;

namespace AVASvgMaker.Engine;

/// <summary>
/// Writes a drawing as a Visio .vsdx package.
///
/// A .vsdx is a zip of XML parts wired together by relationship files, and a shape in it is a
/// list of named cells rather than an element with attributes. Only the cells that carry this
/// drawing's meaning are written: where the shape sits, how big it is, what colour it is, and
/// the run of lines and curves that is its outline. Everything Visio would otherwise inherit
/// from a master is stated outright instead, so the file stands on its own and needs no
/// stencil to open.
///
/// Curves are written as RelCubBezTo and positions as fractions of the shape, which is how
/// Visio's own files put them, so a shape resized in Visio keeps its proportions.
/// </summary>
public static class VisioExporter
{
    private const string Package = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string Visio2010 = "http://schemas.microsoft.com/visio/2010/relationships";
    private const string Office = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static void Write(DiagramDocument document, Stream stream, string title = "Drawing")
    {
        var pages = document.Pages.ToList();

        using var package = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

        Put(package, "[Content_Types].xml", ContentTypes(pages.Count));
        Put(package, "_rels/.rels", RootRelationships());
        Put(package, "docProps/core.xml", Core(title));
        Put(package, "docProps/app.xml", App());
        Put(package, "visio/document.xml", Document());
        Put(package, "visio/_rels/document.xml.rels", DocumentRelationships());
        Put(package, "visio/windows.xml", Windows());
        Put(package, "visio/pages/pages.xml", Pages(pages));
        Put(package, "visio/pages/_rels/pages.xml.rels", PageRelationships(pages.Count));

        for (var i = 0; i < pages.Count; i++)
            Put(package, $"visio/pages/page{i + 1}.xml", Contents(pages[i]));
    }

    private static void Put(ZipArchive package, string path, string xml)
    {
        using var writer = new StreamWriter(package.CreateEntry(path, CompressionLevel.Optimal).Open(), Utf8);
        writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n");
        writer.Write(xml);
    }

    private static readonly UTF8Encoding Utf8 = new(false);

    #region The package

    private static string ContentTypes(int pages)
    {
        var overrides = new StringBuilder();

        for (var i = 1; i <= pages; i++)
            overrides.Append($"<Override PartName=\"/visio/pages/page{i}.xml\" " +
                             "ContentType=\"application/vnd.ms-visio.page+xml\"/>");

        return "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
               "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
               "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
               "<Override PartName=\"/visio/document.xml\" ContentType=\"application/vnd.ms-visio.drawing.main+xml\"/>" +
               "<Override PartName=\"/visio/pages/pages.xml\" ContentType=\"application/vnd.ms-visio.pages+xml\"/>" +
               overrides +
               "<Override PartName=\"/visio/windows.xml\" ContentType=\"application/vnd.ms-visio.windows+xml\"/>" +
               "<Override PartName=\"/docProps/core.xml\" " +
               "ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>" +
               "<Override PartName=\"/docProps/app.xml\" " +
               "ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/>" +
               "</Types>";
    }

    private static string RootRelationships() =>
        $"<Relationships xmlns=\"{Package}\">" +
        $"<Relationship Id=\"rId1\" Type=\"{Visio2010}/document\" Target=\"visio/document.xml\"/>" +
        $"<Relationship Id=\"rId2\" Type=\"{Package}/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
        $"<Relationship Id=\"rId3\" Type=\"{Office}/extended-properties\" Target=\"docProps/app.xml\"/>" +
        "</Relationships>";

    private static string DocumentRelationships() =>
        $"<Relationships xmlns=\"{Package}\">" +
        $"<Relationship Id=\"rId1\" Type=\"{Visio2010}/pages\" Target=\"pages/pages.xml\"/>" +
        $"<Relationship Id=\"rId2\" Type=\"{Visio2010}/windows\" Target=\"windows.xml\"/>" +
        "</Relationships>";

    private static string PageRelationships(int pages)
    {
        var links = new StringBuilder();

        for (var i = 1; i <= pages; i++)
            links.Append($"<Relationship Id=\"rId{i}\" Type=\"{Visio2010}/page\" Target=\"page{i}.xml\"/>");

        return $"<Relationships xmlns=\"{Package}\">{links}</Relationships>";
    }

    private static string Core(string title) =>
        "<cp:coreProperties " +
        "xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
        "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" " +
        "xmlns:dcterms=\"http://purl.org/dc/terms/\" " +
        "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
        $"<dc:title>{Escape(title)}</dc:title>" +
        "<dc:creator>AVASvgMaker</dc:creator>" +
        $"<dcterms:created xsi:type=\"dcterms:W3CDTF\">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</dcterms:created>" +
        "</cp:coreProperties>";

    private static string App() =>
        "<Properties " +
        "xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\" " +
        "xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\">" +
        "<Application>AVASvgMaker</Application></Properties>";

    private static string Document() =>
        $"<VisioDocument xmlns=\"{VisioFormat.Main}\" xmlns:r=\"{VisioFormat.Relationships}\" xml:space=\"preserve\">" +
        "<DocumentSettings TopPage=\"0\" DefaultTextStyle=\"0\" DefaultLineStyle=\"0\" DefaultFillStyle=\"0\">" +
        "<GlueSettings>9</GlueSettings><SnapSettings>65847</SnapSettings>" +
        "<SnapExtensions>34</SnapExtensions><DynamicGridEnabled>1</DynamicGridEnabled>" +
        "<ProtectStyles>0</ProtectStyles><ProtectShapes>0</ProtectShapes>" +
        "<ProtectMasters>0</ProtectMasters><ProtectBkgnds>0</ProtectBkgnds>" +
        "</DocumentSettings></VisioDocument>";

    private static string Windows() =>
        $"<Windows xmlns=\"{VisioFormat.Main}\" xmlns:r=\"{VisioFormat.Relationships}\" ClientWidth=\"1024\" " +
        "ClientHeight=\"768\"><Window ID=\"0\" WindowType=\"Drawing\" WindowState=\"1073741824\" " +
        "Document=\"visio/document.xml\" Page=\"0\" ViewScale=\"1\" ViewCenterX=\"5.5\" ViewCenterY=\"4.25\"/></Windows>";

    #endregion

    #region Pages

    private static string Pages(IReadOnlyList<DiagramPage> pages)
    {
        var body = new StringBuilder();

        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            var width = VisioFormat.ToInches(page.Width);
            var height = VisioFormat.ToInches(page.Height);
            var name = Escape(page.Name);

            body.Append($"<Page ID=\"{i}\" NameU=\"{name}\" Name=\"{name}\" ViewScale=\"1\" " +
                        $"ViewCenterX=\"{Number(width / 2)}\" ViewCenterY=\"{Number(height / 2)}\">" +
                        "<PageSheet LineStyle=\"0\" FillStyle=\"0\" TextStyle=\"0\">" +
                        Cell("PageWidth", width) + Cell("PageHeight", height) +
                        Cell("PageScale", 1, "IN_F") + Cell("DrawingScale", 1, "IN_F") +
                        Cell("DrawingSizeType", 3) + Cell("DrawingScaleType", 0) +
                        Cell("InhibitSnap", 0) + Cell("UIVisibility", 0) + Cell("ShdwType", 0) +
                        "</PageSheet>" +
                        $"<Rel r:id=\"rId{i + 1}\"/></Page>");
        }

        return $"<Pages xmlns=\"{VisioFormat.Main}\" xmlns:r=\"{VisioFormat.Relationships}\" " +
               $"xml:space=\"preserve\">{body}</Pages>";
    }

    private static string Contents(DiagramPage page)
    {
        var height = VisioFormat.ToInches(page.Height);
        var shapes = new StringBuilder();
        var glue = new StringBuilder();

        // Every shape is numbered once, so that glue can name the shape it is stuck to.
        var ids = new Dictionary<DiagramShape, int>();
        var id = 1;

        foreach (var shape in page.Shapes)
            ids[shape] = id++;

        foreach (var shape in page.Shapes)
        {
            shapes.Append(shape is ConnectorShape line
                ? Line(line, ids[line], height)
                : Box(shape, ids[shape], height));

            if (shape is ConnectorShape connector)
                glue.Append(Glue(connector, ids));
        }

        return $"<PageContents xmlns=\"{VisioFormat.Main}\" xmlns:r=\"{VisioFormat.Relationships}\" " +
               $"xml:space=\"preserve\"><Shapes>{shapes}</Shapes>" +
               (glue.Length > 0 ? $"<Connects>{glue}</Connects>" : string.Empty) +
               "</PageContents>";
    }

    /// <summary>Which end of which connector is stuck to which shape.</summary>
    private static string Glue(ConnectorShape line, IReadOnlyDictionary<DiagramShape, int> ids)
    {
        var glue = new StringBuilder();

        // Part 9 is a connector's begin point and part 12 its end. At the other end, part 3
        // is the shape itself - glue that lets the line leave from wherever suits - and 100
        // and up name one connection point, which is glue that holds it there.
        void Stick(DiagramShape? target, string cell, int part, int port)
        {
            if (target is null || !ids.TryGetValue(target, out var to))
                return;

            var (held, at) = port >= 0
                ? (100 + port, $"Connections.X{port + 1}")
                : (3, "PinX");

            glue.Append($"<Connect FromSheet=\"{ids[line]}\" FromCell=\"{cell}\" FromPart=\"{part}\" " +
                        $"ToSheet=\"{to}\" ToCell=\"{at}\" ToPart=\"{held}\"/>");
        }

        Stick(line.StartShape, "BeginX", 9, line.StartPort);
        Stick(line.EndShape, "EndX", 12, line.EndPort);

        return glue.ToString();
    }

    #endregion

    #region Shapes

    private static string Box(DiagramShape shape, int id, double pageHeight)
    {
        var bounds = shape.Bounds;
        var width = VisioFormat.ToInches(bounds.Width);
        var height = VisioFormat.ToInches(bounds.Height);

        var centre = VisioFormat.FromPage(bounds.Center, pageHeight);

        var cells =
            Cell("PinX", centre.X) + Cell("PinY", centre.Y) +
            Cell("Width", width) + Cell("Height", height) +
            Cell("LocPinX", width / 2) + Cell("LocPinY", height / 2) +
            Cell("Angle", VisioFormat.ToRadians(shape.Rotation)) +
            Cell("FlipX", 0) + Cell("FlipY", 0) +
            Paint(shape) + Lettering(shape);

        return $"<Shape ID=\"{id}\" NameU=\"Sheet.{id}\" Type=\"Shape\" LineStyle=\"0\" FillStyle=\"0\" " +
               $"TextStyle=\"0\">{cells}{Outline(shape)}{Words(shape)}</Shape>";
    }

    /// <summary>
    /// The shape's filled body, then whatever it draws over the top and does not fill. Visio
    /// marks a run it will not fill on the section itself, which is exactly the distinction.
    /// </summary>
    private static string Outline(DiagramShape shape)
    {
        var body = Geometry(shape.UnitOutline, filled: true, from: 0);

        return shape.UnitDetail is { } detail
            ? body + Geometry(detail, filled: false, from: Sections(shape.UnitOutline))
            : body;
    }

    /// <summary>How many sections an outline takes up, so the next one carries on from there.</summary>
    private static int Sections(string outline) =>
        StencilPath.Steps(outline, new Rect(0, 0, 1, 1)).Count(step => step.Command == 'M');

    private static string Line(ConnectorShape line, int id, double pageHeight)
    {
        var route = line.Path;
        var first = route[0];
        var last = route[^1];

        // A connector's box is the corner-to-corner run of its route; Visio wants its geometry
        // as fractions of that box, and a run that is level or plumb has no thickness at all.
        var box = new Rect(
            new Point(route.Min(point => point.X), route.Min(point => point.Y)),
            new Point(route.Max(point => point.X), route.Max(point => point.Y)));

        var width = VisioFormat.ToInches(box.Width);
        var height = VisioFormat.ToInches(box.Height);

        var begin = VisioFormat.FromPage(first, pageHeight);
        var end = VisioFormat.FromPage(last, pageHeight);
        var origin = VisioFormat.FromPage(new Point(box.X, box.Bottom), pageHeight);

        var outline = new StringBuilder();

        for (var i = 0; i < route.Count; i++)
        {
            var u = box.Width <= 0 ? 0 : (route[i].X - box.X) / box.Width;
            var v = box.Height <= 0 ? 0 : (route[i].Y - box.Y) / box.Height;

            outline.Append($"{(i == 0 ? 'M' : 'L')} {Number(u)},{Number(v)} ");
        }

        var cells =
            Cell("PinX", origin.X + width / 2) + Cell("PinY", origin.Y + height / 2) +
            Cell("Width", width) + Cell("Height", height) +
            Cell("LocPinX", width / 2) + Cell("LocPinY", height / 2) +
            Cell("Angle", 0) + Cell("BeginX", begin.X) + Cell("BeginY", begin.Y) +
            Cell("EndX", end.X) + Cell("EndY", end.Y) +
            Cell("ObjType", 2) +
            Cell("BeginArrow", Arrow(line.StartCap)) + Cell("EndArrow", Arrow(line.EndCap)) +
            Paint(line) + Lettering(line);

        return $"<Shape ID=\"{id}\" NameU=\"Connector.{id}\" Type=\"Shape\" LineStyle=\"0\" FillStyle=\"0\" " +
               $"TextStyle=\"0\">{cells}{Geometry(outline.ToString(), filled: false, from: 0)}{Words(line)}</Shape>";
    }

    /// <summary>
    /// Visio's number for a line end. Its gallery and the twelve here are different sets, and
    /// nothing to hand says which of its numbers is which shape, so every end that is drawn at
    /// all goes out as a plain filled arrow. That loses a hollow arrow's meaning - but it used
    /// to lose the whole end, since anything that was not the plain arrow was written as no
    /// end at all.
    /// </summary>
    private static int Arrow(EndCapStyle cap) => cap == EndCapStyle.None ? 0 : 4;

    private static string Words(DiagramShape shape) =>
        string.IsNullOrEmpty(shape.Text) ? string.Empty : $"<Text>{Escape(shape.Text)}</Text>";

    private static string Paint(DiagramShape shape)
    {
        var solid = shape.Fill.A > 0 && shape is not ConnectorShape;

        var pattern = shape.Stroke.A == 0
            ? 0
            : shape.StrokeStyle switch
            {
                StrokeStyle.Dashed => 2,
                StrokeStyle.Dotted => 3,
                _ => 1
            };

        return Cell("FillForegnd", Hex(shape.Fill)) +
               Cell("FillPattern", solid ? 1 : 0) +
               Cell("FillForegndTrans", 1 - shape.Fill.A / 255.0) +
               Cell("LineColor", Hex(shape.Stroke)) +
               Cell("LinePattern", pattern) +
               Cell("LineWeight", VisioFormat.ToInches(shape.StrokeThickness)) +
               Cell("LineCap", 0) + Cell("Rounding", 0);
    }

    /// <summary>The text's own cells, and the sections that carry the font and the alignment.</summary>
    private static string Lettering(DiagramShape shape)
    {
        var align = shape.TextAlign switch
        {
            TextAlign.Left => 0,
            TextAlign.Right => 2,
            _ => 1
        };

        var style = (shape.Bold ? 1 : 0) + (shape.Italic ? 2 : 0);

        var down = shape.TextVerticalAlign switch
        {
            TextVerticalAlign.Top => 0,
            TextVerticalAlign.Bottom => 2,
            _ => 1
        };

        return Cell("VerticalAlign", down) + Cell("TextBkgnd", 0) + Block(shape) +
               "<Section N=\"Character\"><Row IX=\"0\">" +
               Cell("Size", VisioFormat.ToInches(shape.FontSize), "PT") +
               Cell("Style", style) +
               Cell("Color", Hex(shape.TextColor)) +
               (string.IsNullOrWhiteSpace(shape.FontName)
                   ? string.Empty
                   : Cell("Font", Escape(shape.FontName))) +
               "</Row></Section>" +
               $"<Section N=\"Paragraph\"><Row IX=\"0\">{Cell("HorzAlign", align)}</Row></Section>";
    }

    /// <summary>
    /// Where the words go, when that is not simply the shape. Visio states the block's size
    /// and pins it in the shape's own coordinates, measuring up from the bottom.
    /// </summary>
    private static string Block(DiagramShape shape)
    {
        if (shape.TextFrame is not { } frame)
            return string.Empty;

        var width = VisioFormat.ToInches(shape.Bounds.Width);
        var height = VisioFormat.ToInches(shape.Bounds.Height);

        var blockWidth = frame.Width * width;
        var blockHeight = frame.Height * height;

        return Cell("TxtWidth", blockWidth) + Cell("TxtHeight", blockHeight) +
               Cell("TxtLocPinX", blockWidth / 2) + Cell("TxtLocPinY", blockHeight / 2) +
               Cell("TxtPinX", frame.X * width + blockWidth / 2) +
               Cell("TxtPinY", height - frame.Y * height - blockHeight / 2) +
               Cell("TxtAngle", 0);
    }

    #endregion

    #region Geometry

    /// <summary>
    /// The outline as Visio geometry sections. Each separate run of the pen becomes its own
    /// section, because Visio has no way of lifting the pen inside one; the rows within are
    /// all relative, which is to say fractions of the shape, so the outline is stated once and
    /// holds at any size. Visio's y runs up the page and ours runs down it, hence the flip.
    /// </summary>
    private static string Geometry(string outline, bool filled, int from)
    {
        var sections = new StringBuilder();
        var rows = new StringBuilder();
        var index = from;
        var row = 1;

        var start = new Point();
        var cursor = new Point();

        string Place(Point point) =>
            $"{Cell("X", point.X)}{Cell("Y", 1 - point.Y)}";

        void Close()
        {
            if (rows.Length == 0)
                return;

            sections.Append($"<Section N=\"Geometry\" IX=\"{index++}\">" +
                            Cell("NoFill", filled ? 0 : 1) + Cell("NoLine", 0) +
                            Cell("NoShow", 0) + Cell("NoSnap", 0) +
                            rows + "</Section>");
            rows.Clear();
            row = 1;
        }

        foreach (var (command, points) in StencilPath.Steps(outline, new Rect(0, 0, 1, 1)))
        {
            switch (command)
            {
                case 'M':
                    Close();
                    start = cursor = points[0];
                    rows.Append($"<Row T=\"RelMoveTo\" IX=\"{row++}\">{Place(cursor)}</Row>");
                    break;

                case 'L':
                    cursor = points[0];
                    rows.Append($"<Row T=\"RelLineTo\" IX=\"{row++}\">{Place(cursor)}</Row>");
                    break;

                case 'C':
                    rows.Append(Bezier(points[0], points[1], points[2], ref row));
                    cursor = points[2];
                    break;

                case 'Q':
                {
                    // Visio has no quadratic row, and a quadratic is a cubic with both of its
                    // handles two thirds of the way to the one control point.
                    var control = points[0];
                    var to = points[1];

                    rows.Append(Bezier(
                        cursor + (control - cursor) * (2.0 / 3),
                        to + (control - to) * (2.0 / 3),
                        to, ref row));

                    cursor = to;
                    break;
                }

                case 'Z':
                    // Visio closes a run by arriving back where it started rather than by
                    // being told to.
                    if (Point.Distance(cursor, start) > 1e-9)
                        rows.Append($"<Row T=\"RelLineTo\" IX=\"{row++}\">{Place(start)}</Row>");

                    cursor = start;
                    break;
            }
        }

        Close();

        return sections.ToString();

        string Bezier(Point one, Point two, Point to, ref int at) =>
            $"<Row T=\"RelCubBezTo\" IX=\"{at++}\">{Place(to)}" +
            $"{Cell("A", one.X)}{Cell("B", 1 - one.Y)}{Cell("C", two.X)}{Cell("D", 1 - two.Y)}</Row>";
    }

    #endregion

    #region Words and numbers

    private static string Cell(string name, double value, string? unit = null) =>
        $"<Cell N=\"{name}\" V=\"{Number(value)}\"{(unit is null ? string.Empty : $" U=\"{unit}\"")}/>";

    private static string Cell(string name, string value) =>
        $"<Cell N=\"{name}\" V=\"{value}\"/>";

    private static string Number(double value) => VisioFormat.Number(value);

    private static string Hex(Color colour) =>
        $"#{colour.R:x2}{colour.G:x2}{colour.B:x2}";

    private static string Escape(string text) => text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");

    #endregion
}
