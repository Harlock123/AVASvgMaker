using System.Net.Http;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AVASvgMaker.Agents.Mcp;

/// <summary>A shape to make (add_shapes).</summary>
public sealed class ShapeSpec
{
    [JsonPropertyName("kind"), Description("A stencil's name or kind (list_stencils): \"Process\", \"Decision\", \"Terminator\", \"Ellipse\", \"Text box\", \"Server\"…")]
    public string Kind { get; set; } = "Process";

    [JsonPropertyName("text"), Description("Its label. Optional.")]
    public string? Text { get; set; }

    [JsonPropertyName("x"), Description("Left edge, in page units (96 to the inch).")]
    public double X { get; set; }

    [JsonPropertyName("y"), Description("Top edge.")]
    public double Y { get; set; }

    [JsonPropertyName("w"), Description("Width. Default 120.")]
    public double? W { get; set; }

    [JsonPropertyName("h"), Description("Height. Default 60.")]
    public double? H { get; set; }

    [JsonPropertyName("fill"), Description("Fill colour, \"#RRGGBB\". Optional.")]
    public string? Fill { get; set; }

    [JsonPropertyName("stroke"), Description("Outline colour, \"#RRGGBB\". Optional.")]
    public string? Stroke { get; set; }

    [JsonPropertyName("text_color"), Description("Label colour, \"#RRGGBB\". Optional.")]
    public string? TextColor { get; set; }
}

/// <summary>A connector to make (connect).</summary>
public sealed class LinkSpec
{
    [JsonPropertyName("from"), Description("The shape it starts at, by id (\"s3\").")]
    public string From { get; set; } = "";

    [JsonPropertyName("to"), Description("The shape it ends at, by id.")]
    public string To { get; set; } = "";

    [JsonPropertyName("label"), Description("Its label (\"yes\", \"no\", \"HTTPS\"…). Optional.")]
    public string? Label { get; set; }

    [JsonPropertyName("routing"), Description("orthogonal (right angles round obstacles; the default), straight or curved.")]
    public string? Routing { get; set; }

    [JsonPropertyName("start_cap"), Description("none (the default), arrow, open_arrow, dot, diamond, hollow_arrow or hollow_diamond.")]
    public string? StartCap { get; set; }

    [JsonPropertyName("end_cap"), Description("arrow (the default), none, open_arrow, dot, diamond, hollow_arrow or hollow_diamond.")]
    public string? EndCap { get; set; }

    [JsonPropertyName("dashed"), Description("A dashed line. Default false.")]
    public bool? Dashed { get; set; }
}

/// <summary>
/// The MCP tools: what an agent can do with AVASvgMaker. Each handler reaches the drawing only through
/// <see cref="IDiagramHost.RunAsync{T}"/>, so the same code serves headless and live. Every change is one undo step
/// (the document's batch), a call that fails leaves the drawing as it was, and failures come back as MCP tool errors
/// with a message that says what to do instead.
/// </summary>
/// <remarks>The parameter names are the JSON names agents see, hence snake_case.</remarks>
public sealed partial class DiagramTools(IDiagramHost host)
{
    private readonly Workspace _files = new(host.Root);

    private const string PageHelp = "The page, numbered from 1. Default: the page being shown.";

    // ───────────────────────────── the drawing ─────────────────────────────

    [McpServerTool(Name = "get_document", ReadOnly = true, Idempotent = true)]
    [Description("The drawing as a whole: its file, whether it has unsaved changes, and its pages (each one's name, size, margin and how many shapes and connectors it has), with the page being shown. Start here; get_page lists a page's shapes.")]
    public Task<string> GetDocument(CancellationToken cancel = default) =>
        Read("read the drawing", s =>
        {
            var d = s.Document;
            return new JsonObject
            {
                ["file"] = s.FilePath is { } f ? Path.GetRelativePath(host.Root, f) : null,
                ["changed"] = d.IsModified,
                ["page_shown"] = d.PageIndex + 1,
                ["pages"] = new JsonArray(d.Pages.Select((p, i) => (JsonNode)new JsonObject
                {
                    ["page"] = i + 1,
                    ["name"] = p.Name,
                    ["size"] = $"{R(p.Width)} x {R(p.Height)}",
                    ["paper"] = PaperName(p.Width, p.Height),
                    ["shapes"] = p.Shapes.Count(x => x is not ConnectorShape),
                    ["connectors"] = p.Shapes.Count(x => x is ConnectorShape),
                }).ToArray()),
            };
        }, cancel);

    [McpServerTool(Name = "new_drawing")]
    [Description("Starts a new drawing, replacing the open one: one empty page of a paper size. Fails if the open drawing has unsaved changes, unless discard_changes is true.")]
    public Task<string> NewDrawing(
        [Description("Letter (the default), Legal, Tabloid, A3, A4 or A5.")] string? paper = null,
        [Description("Turned sideways. Default false.")] bool landscape = false,
        [Description("The page's name. Default \"Page 1\".")] string? name = null,
        [Description("Throw away unsaved changes to the open drawing. Default false.")] bool discard_changes = false,
        CancellationToken cancel = default) =>
        Read("started a new drawing", s =>
        {
            Discardable(s, discard_changes);
            var (w, h) = Paper(paper, landscape);
            var doc = new DiagramDocument();
            doc.SetPages([new DiagramPage(string.IsNullOrWhiteSpace(name) ? "Page 1" : name.Trim()) { Width = w, Height = h }]);
            s.Replace(doc, null);
            s.Refresh();
            return new JsonObject { ["pages"] = 1, ["size"] = $"{R(w)} x {R(h)}" };
        }, cancel);

    [McpServerTool(Name = "open_drawing")]
    [Description("Opens a drawing from the working folder, replacing the open one: an .avadiag file, or a Visio drawing (.vsdx), an SVG or a Mermaid file (.mmd, .mermaid), each read as AVASvgMaker reads them. Fails if the open drawing has unsaved changes, unless discard_changes is true.")]
    public Task<string> OpenDrawing(
        [Description("The file, relative to the working folder.")] string path,
        [Description("Throw away unsaved changes to the open drawing. Default false.")] bool discard_changes = false,
        CancellationToken cancel = default) =>
        Read("opened a drawing", s =>
        {
            Discardable(s, discard_changes);
            var full = _files.Resolve(path, "A drawing", "." + DiagramFile.Extension, ".vsdx", ".svg", ".mmd", ".mermaid");
            if (!File.Exists(full)) throw new FileNotFoundException($"There's no {path} in the working folder.");
            DiagramDocument doc;
            var ext = Path.GetExtension(full).ToLowerInvariant();
            if (ext == "." + DiagramFile.Extension)
            {
                using var stream = File.OpenRead(full);
                doc = DiagramFile.Read(stream);
            }
            else if (ext == ".mmd" || ext == ".mermaid")
            {
                doc = new DiagramDocument();
                doc.SetPages([MermaidImporter.Read(File.ReadAllText(full)).Page]);
            }
            else
            {
                using var stream = File.OpenRead(full);
                doc = ext == ".vsdx" ? VisioImporter.Read(stream).Document : SvgImporter.Read(stream).Document;
            }
            s.Replace(doc, ext == "." + DiagramFile.Extension ? full : null);
            ShapeIds.Ensure(s.Document);
            s.Refresh();
            return new JsonObject
            {
                ["opened"] = Path.GetRelativePath(host.Root, full),
                ["pages"] = s.Document.Pages.Count,
                ["shapes"] = s.Document.Pages.Sum(p => p.Shapes.Count),
            };
        }, cancel);

    [McpServerTool(Name = "save_drawing")]
    [Description("Saves the drawing as .avadiag (AVASvgMaker's own file, every page). Without a path it saves to the file it came from. export writes SVG, PDF, pictures, Mermaid or Visio instead.")]
    public Task<string> SaveDrawing(
        [Description("Where to save, relative to the working folder; .avadiag is added if missing. Default: the drawing's own file.")] string? path = null,
        [Description("Write over an existing file. Default false (its own file always may be).")] bool overwrite = false,
        CancellationToken cancel = default) =>
        Read("saved the drawing", s =>
        {
            string full;
            if (path is null)
            {
                full = s.FilePath ?? throw new ArgumentException("The drawing hasn't been saved before: give a path, such as \"flowchart.avadiag\".");
                _files.Resolve(full);
                overwrite = true;
            }
            else
            {
                var p = path.EndsWith("." + DiagramFile.Extension, StringComparison.OrdinalIgnoreCase) ? path : path + "." + DiagramFile.Extension;
                full = _files.Resolve(p);
                if (s.FilePath is { } own && string.Equals(Path.GetFullPath(own), full, StringComparison.Ordinal)) overwrite = true;
            }
            _files.CheckWritable(full, overwrite);
            using (var stream = File.Create(full)) DiagramFile.Write(s.Document, stream);
            _files.Wrote(full);
            s.Saved(full);
            return new JsonObject { ["saved"] = Path.GetRelativePath(host.Root, full), ["bytes"] = new FileInfo(full).Length };
        }, cancel);

    // ───────────────────────────── pages ─────────────────────────────

    [McpServerTool(Name = "get_page", ReadOnly = true, Idempotent = true)]
    [Description("Everything on a page, bottom first: each shape's id, kind, label, box (x, y, w, h) and colours, with its container and group; each connector's id, the shapes it joins, its label and routing. Also the page's size and margin.")]
    public Task<string> GetPage(
        [Description(PageHelp)] int? page = null,
        CancellationToken cancel = default) =>
        Read("read a page", s =>
        {
            var d = s.Document;
            var p = d.Pages[PageIndex(d, page)];
            var shapes = p.Shapes.Where(x => x is not ConnectorShape).ToList();
            return new JsonObject
            {
                ["page"] = d.Pages.ToList().IndexOf(p) + 1,
                ["name"] = p.Name,
                ["size"] = $"{R(p.Width)} x {R(p.Height)}",
                ["margin"] = R(p.Margin),
                ["shapes"] = new JsonArray(shapes.Select(x => (JsonNode)Describe(x)).ToArray()),
                ["connectors"] = new JsonArray(p.Shapes.OfType<ConnectorShape>().Select(c => (JsonNode)Describe(c)).ToArray()),
            };
        }, cancel);

    [McpServerTool(Name = "add_page")]
    [Description("Adds a page after the others (or after a given one) and shows it. New shapes then go on it.")]
    public Task<string> AddPage(
        [Description("Its name. Default \"Page N\".")] string? name = null,
        [Description("Put it after this page (from 1); 0 puts it first. Default: at the end.")] int? after = null,
        [Description("Letter, Legal, Tabloid, A3, A4 or A5. Default: the size of the page shown.")] string? paper = null,
        [Description("With paper: turned sideways.")] bool landscape = false,
        CancellationToken cancel = default) =>
        Edit("added a page", s =>
        {
            var d = s.Document;
            var at = after ?? d.Pages.Count;
            if (at < 0 || at > d.Pages.Count) throw new ArgumentException($"after is from 0 to {d.Pages.Count}.");
            var p = d.InsertPage(at);
            if (!string.IsNullOrWhiteSpace(name)) d.RenamePage(d.PageIndex, name.Trim());
            if (paper is not null)
            {
                var (w, h) = Paper(paper, landscape);
                d.SetPageSize(w, h);
            }
            return new JsonObject { ["page"] = d.PageIndex + 1, ["name"] = p.Name, ["pages"] = d.Pages.Count };
        }, cancel);

    [McpServerTool(Name = "set_page")]
    [Description("Changes a page: its name, size (a paper or a width and height), margin guide, or background colour; or shows it (show true). Give only what changes.")]
    public Task<string> SetPage(
        [Description(PageHelp)] int? page = null,
        [Description("A new name.")] string? name = null,
        [Description("Letter, Legal, Tabloid, A3, A4 or A5.")] string? paper = null,
        [Description("With paper: turned sideways.")] bool landscape = false,
        [Description("Width in page units (96 to the inch).")] double? width = null,
        [Description("Height in page units.")] double? height = null,
        [Description("The margin guide's inset in page units (0: none).")] double? margin = null,
        [Description("The paper's colour, \"#RRGGBB\".")] string? background = null,
        [Description("Show this page in the window.")] bool show = false,
        CancellationToken cancel = default) =>
        Edit("changed a page", s =>
        {
            var d = s.Document;
            d.PageIndex = PageIndex(d, page);
            if (!string.IsNullOrWhiteSpace(name)) d.RenamePage(d.PageIndex, name.Trim());
            if (paper is not null || width is not null || height is not null)
            {
                var (w, h) = paper is not null ? Paper(paper, landscape) : (width ?? d.PageWidth, height ?? d.PageHeight);
                if (w is < 96 or > 20000 || h is < 96 or > 20000) throw new ArgumentException("A page is from 96 (1 inch) to 20,000 units each way.");
                d.SetPageSize(w, h);
            }
            if (margin is { } m) d.SetMargin(Math.Clamp(m, 0, Math.Min(d.PageWidth, d.PageHeight) / 3));
            if (background is not null) d.SetPaper(ColourOf(background, "background"), null, 90);
            return new JsonObject { ["page"] = d.PageIndex + 1, ["name"] = d.CurrentPage.Name, ["size"] = $"{R(d.PageWidth)} x {R(d.PageHeight)}" };
        }, cancel);

    [McpServerTool(Name = "delete_page", Destructive = true)]
    [Description("Deletes a page and everything on it. The last page can't be deleted.")]
    public Task<string> DeletePage(
        [Description("The page, from 1.")] int page,
        CancellationToken cancel = default) =>
        Edit("deleted a page", s =>
        {
            var d = s.Document;
            if (d.Pages.Count <= 1) throw new InvalidOperationException("A drawing needs at least one page.");
            if (!d.RemovePage(PageIndex(d, page))) throw new InvalidOperationException("That page couldn't be deleted.");
            return new JsonObject { ["deleted"] = page, ["pages"] = d.Pages.Count };
        }, cancel);

    // ───────────────────────────── shapes ─────────────────────────────

    [McpServerTool(Name = "list_stencils", ReadOnly = true, Idempotent = true)]
    [Description("The shapes the stencil catalogue offers, by category (Basic shapes, Arrows, Callouts, Flowchart, BPMN, UML, Network, Electrical, Logic gates, Electronic): each one's name, which add_shapes takes as kind. Narrow it with search (matched against names and keywords) or category.")]
    public Task<string> ListStencils(
        [Description("Words to look for in a stencil's name or keywords, e.g. \"database\", \"server\", \"gate\". Optional.")] string? search = null,
        [Description("Only this category, e.g. \"Flowchart\", \"Network\", \"UML\". Optional.")] string? category = null,
        CancellationToken cancel = default) =>
        Read("listed the stencils", _ =>
        {
            var all = StencilCatalogue.All.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(category))
            {
                var c = Enum.GetValues<StencilCategory>().FirstOrDefault(x => x.ToString().Equals(category.Trim(), StringComparison.OrdinalIgnoreCase)
                    || StencilCatalogue.CategoryName(x).Equals(category.Trim(), StringComparison.OrdinalIgnoreCase), (StencilCategory)(-1));
                if ((int)c < 0) throw new ArgumentException($"There's no category \"{category}\". The categories: {string.Join(", ", Enum.GetValues<StencilCategory>().Select(StencilCatalogue.CategoryName))}.");
                all = all.Where(x => x.Category == c);
            }
            if (!string.IsNullOrWhiteSpace(search)) all = all.Where(x => StencilCatalogue.Matches(x, search.Trim()));
            var list = all.ToList();
            return new JsonObject
            {
                ["count"] = list.Count,
                ["categories"] = new JsonArray(list.GroupBy(x => x.Category).Select(g => (JsonNode)new JsonObject
                {
                    ["category"] = StencilCatalogue.CategoryName(g.Key),
                    ["stencils"] = new JsonArray(g.Select(x => (JsonNode)x.Name).Distinct().ToArray()),
                }).ToArray()),
                ["also"] = "Text box, Pool, Lane and Container box are made by these names too.",
            };
        }, cancel);

    [McpServerTool(Name = "add_shapes")]
    [Description("Places shapes on a page (one or many in one step), each a stencil kind at a box, with an optional label and colours. Returns their ids, in order, for connect and set_shape. For a flowchart, add_flowchart does the shapes, connectors and layout in one go.")]
    public Task<string> AddShapes(
        [Description("The shapes: each with kind, text, x, y, and optionally w, h, fill, stroke, text_color.")] List<ShapeSpec> shapes,
        [Description(PageHelp)] int? page = null,
        CancellationToken cancel = default) =>
        Edit("added shapes", s =>
        {
            var d = s.Document;
            d.PageIndex = PageIndex(d, page);
            if (shapes.Count == 0) throw new ArgumentException("Give at least one shape.");
            var made = new List<DiagramShape>();
            foreach (var spec in shapes)
            {
                var shape = Make(spec.Kind, new Rect(spec.X, spec.Y, Size(spec.W ?? 120, "w"), Size(spec.H ?? 60, "h")));
                shape.Text = spec.Text ?? shape.Text;
                if (spec.Fill is not null) shape.Fill = ColourOf(spec.Fill, "fill");
                if (spec.Stroke is not null) shape.Stroke = ColourOf(spec.Stroke, "stroke");
                if (spec.TextColor is not null) shape.TextColor = ColourOf(spec.TextColor, "text_color");
                d.Shapes.Add(shape);
                made.Add(shape);
            }
            d.NormaliseOrder();
            ShapeIds.Ensure(d);
            d.SetSelection(made);
            return new JsonObject
            {
                ["page"] = d.PageIndex + 1,
                ["ids"] = new JsonArray(made.Select(x => (JsonNode)x.Uid).ToArray()),
                ["off_page"] = OffPage(d, made),
            };
        }, cancel);

    [McpServerTool(Name = "connect")]
    [Description("Joins shapes with connectors (one or many in one step), glued to the shapes so they follow when the shapes move. Both shapes must be on the same page. Returns the connectors' ids.")]
    public Task<string> Connect(
        [Description("The connectors: each with from and to (shape ids), and optionally label, routing, start_cap, end_cap, dashed.")] List<LinkSpec> links,
        CancellationToken cancel = default) =>
        Edit("connected shapes", s =>
        {
            var d = s.Document;
            if (links.Count == 0) throw new ArgumentException("Give at least one connector.");
            var made = new List<ConnectorShape>();
            foreach (var link in links)
            {
                var (from, pf) = Shape(d, link.From);
                var (to, pt) = Shape(d, link.To);
                if (pf != pt) throw new ArgumentException($"{link.From} is on page {pf + 1} and {link.To} on page {pt + 1}: a connector joins shapes on one page.");
                if (from is ConnectorShape || to is ConnectorShape) throw new ArgumentException("A connector joins shapes, not other connectors.");
                d.PageIndex = pf;
                var line = Join(from, to, link.Label ?? "", link.Routing, link.StartCap, link.EndCap ?? "arrow");
                if (link.Dashed == true) line.StrokeStyle = StrokeStyle.Dashed;
                d.Shapes.Add(line);
                made.Add(line);
            }
            d.NormaliseOrder();
            d.Refresh();
            ShapeIds.Ensure(d);
            return new JsonObject { ["ids"] = new JsonArray(made.Select(x => (JsonNode)x.Uid).ToArray()) };
        }, cancel);

    [McpServerTool(Name = "set_shape")]
    [Description("Changes shapes or connectors: label, box (x, y, w, h), colours, outline width and style, font size, bold, italic, alignment, rotation; a connector's routing and end caps. Give only what changes; several at once with ids.")]
    public Task<string> SetShape(
        [Description("The shape's or connector's id. Or give ids.")] string? id = null,
        [Description("Several ids, all changed the same way.")] List<string>? ids = null,
        [Description("The label (\\n for a new line).")] string? text = null,
        [Description("Left edge.")] double? x = null,
        [Description("Top edge.")] double? y = null,
        [Description("Width.")] double? w = null,
        [Description("Height.")] double? h = null,
        [Description("Fill colour, \"#RRGGBB\".")] string? fill = null,
        [Description("Outline colour.")] string? stroke = null,
        [Description("Label colour.")] string? text_color = null,
        [Description("Outline width.")] double? stroke_width = null,
        [Description("solid, dashed or dotted.")] string? line_style = null,
        [Description("The label's size in points.")] double? font_size = null,
        [Description("Bold label.")] bool? bold = null,
        [Description("Italic label.")] bool? italic = null,
        [Description("Rotation in degrees.")] double? rotation = null,
        [Description("A connector's routing: orthogonal, straight or curved.")] string? routing = null,
        [Description("A connector's start cap: none, arrow, open_arrow, dot, diamond, hollow_arrow, hollow_diamond.")] string? start_cap = null,
        [Description("A connector's end cap.")] string? end_cap = null,
        CancellationToken cancel = default) =>
        Edit("changed shapes", s =>
        {
            var d = s.Document;
            var targets = Targets(d, id, ids);
            foreach (var (shape, p) in targets)
            {
                d.PageIndex = p;
                if (text is not null) shape.Text = text.Replace("\\n", "\n");
                if (shape is not ConnectorShape && (x is not null || y is not null || w is not null || h is not null))
                {
                    var b = shape.Bounds;
                    shape.Bounds = new Rect(x ?? b.X, y ?? b.Y, Size(w ?? b.Width, "w"), Size(h ?? b.Height, "h"));
                }
                if (fill is not null) shape.Fill = ColourOf(fill, "fill");
                if (stroke is not null) shape.Stroke = ColourOf(stroke, "stroke");
                if (text_color is not null) shape.TextColor = ColourOf(text_color, "text_color");
                if (stroke_width is { } sw) shape.StrokeThickness = Math.Clamp(sw, 0.25, 40);
                if (line_style is not null) shape.StrokeStyle = Choice<StrokeStyle>(line_style, "line_style");
                if (font_size is { } fs) shape.FontSize = Math.Clamp(fs, 4, 200);
                if (bold is { } bo) shape.Bold = bo;
                if (italic is { } it) shape.Italic = it;
                if (rotation is { } r && shape.CanRotate) d.Rotate([shape], r, absolute: true);
                if (shape is ConnectorShape c)
                {
                    if (routing is not null) c.Routing = Choice<ConnectorRouting>(routing, "routing");
                    if (start_cap is not null) c.StartCap = Cap(start_cap);
                    if (end_cap is not null) c.EndCap = Cap(end_cap);
                }
            }
            d.Refresh();
            return new JsonObject { ["changed"] = new JsonArray(targets.Select(t => (JsonNode)Describe(t.Shape)).ToArray()) };
        }, cancel);

    [McpServerTool(Name = "delete_shapes", Destructive = true)]
    [Description("Deletes shapes or connectors by id; the connectors glued to a deleted shape go with it. In the open program, deleting most of a page's shapes first asks the person.")]
    public async Task<string> DeleteShapes(
        McpServer server,
        [Description("The ids to delete.")] List<string> ids,
        CancellationToken cancel = default)
    {
        var (count, ofPage) = await Run("counted shapes", s =>
        {
            var targets = Targets(s.Document, null, ids);
            var page = targets[0].Page;
            return (targets.Count(t => t.Shape is not ConnectorShape), s.Document.Pages[page].Shapes.Count(x => x is not ConnectorShape));
        }, cancel).ConfigureAwait(false);
        if (count >= 4 && count * 2 >= ofPage && host is DiagramHost { Live: true } && server.ClientCapabilities?.Elicitation != null)
            await Confirm(server, $"This would delete {count} of the {ofPage} shapes on the page. Go ahead?", cancel).ConfigureAwait(false);
        return await Edit("deleted shapes", s =>
        {
            var d = s.Document;
            var targets = Targets(d, null, ids);
            var gone = targets.Select(t => t.Shape).ToHashSet();
            var removed = 0;
            foreach (var (shape, p) in targets)
            {
                d.PageIndex = p;
                foreach (var glued in d.Shapes.OfType<ConnectorShape>().Where(c => gone.Contains(c.StartShape!) || gone.Contains(c.EndShape!)).ToList())
                {
                    d.Remove(glued);
                    removed++;
                }
                if (d.Shapes.Contains(shape))
                {
                    d.Remove(shape);
                    removed++;
                }
            }
            return new JsonObject { ["deleted"] = removed };
        }, cancel).ConfigureAwait(false);
    }

    [McpServerTool(Name = "arrange")]
    [Description("Arranges shapes: align two or more by an edge or centre, distribute three or more evenly, stack them (front, forward, backward, back), make them the same size as the first, or group and ungroup them. Give one of align, distribute, order, same_size, group.")]
    public Task<string> Arrange(
        [Description("The shapes' ids (on one page).")] List<string> ids,
        [Description("left, centre, right, top, middle or bottom.")] string? align = null,
        [Description("across or down.")] string? distribute = null,
        [Description("front, forward, backward or back.")] string? order = null,
        [Description("width, height or both: match the first shape's.")] string? same_size = null,
        [Description("true groups them; false takes their groups apart.")] bool? group = null,
        CancellationToken cancel = default) =>
        Edit("arranged shapes", s =>
        {
            var d = s.Document;
            var targets = Targets(d, null, ids);
            if (targets.Select(t => t.Page).Distinct().Count() > 1) throw new ArgumentException("Arrange shapes on one page at a time.");
            d.PageIndex = targets[0].Page;
            var shapes = targets.Select(t => t.Shape).ToList();
            var did = new List<string>();
            if (align is not null)
            {
                if (!ShapeArranger.Align(shapes, align.Trim().ToLowerInvariant() switch
                {
                    "left" => AlignEdge.Left, "centre" or "center" => AlignEdge.CenterX, "right" => AlignEdge.Right,
                    "top" => AlignEdge.Top, "middle" => AlignEdge.Middle, "bottom" => AlignEdge.Bottom,
                    _ => throw new ArgumentException($"align is left, centre, right, top, middle or bottom, not \"{align}\"."),
                })) throw new ArgumentException("align needs two or more shapes.");
                did.Add("aligned");
            }
            if (distribute is not null)
            {
                var across = distribute.Trim().ToLowerInvariant() switch
                {
                    "across" or "horizontal" => true, "down" or "vertical" => false,
                    _ => throw new ArgumentException($"distribute is across or down, not \"{distribute}\"."),
                };
                if (!ShapeArranger.Distribute(shapes, across)) throw new ArgumentException("distribute needs three or more shapes.");
                did.Add("distributed");
            }
            if (order is not null)
            {
                d.SetSelection(shapes);
                ShapeArranger.Reorder(d.Shapes, shapes, Choice<ZOrder>(order, "order"));
                did.Add("reordered");
            }
            if (same_size is not null)
            {
                if (!ShapeArranger.MatchSize(shapes, shapes[0], Choice<SizeMatch>(same_size, "same_size"))) throw new ArgumentException("same_size needs two or more shapes.");
                did.Add("sized");
            }
            if (group is { } g)
            {
                if (g ? !d.Group(shapes) : !d.Ungroup(shapes)) throw new ArgumentException(g ? "Group two or more shapes." : "None of those is in a group.");
                did.Add(g ? "grouped" : "ungrouped");
            }
            if (did.Count == 0) throw new ArgumentException("Give align, distribute, order, same_size or group.");
            d.Refresh();
            return new JsonObject { ["did"] = string.Join(", ", did), ["shapes"] = new JsonArray(shapes.Select(x => (JsonNode)Describe(x)).ToArray()) };
        }, cancel);

    [McpServerTool(Name = "lay_out")]
    [Description("Lays a page's shapes out in layers by the connectors between them (down the page as a flowchart is read, or across), untangling crossing lines; the connectors route themselves after. Shapes in pools and lanes stay put. Optionally only some shapes.")]
    public Task<string> LayOut(
        [Description(PageHelp)] int? page = null,
        [Description("down (the default) or across.")] string flow = "down",
        [Description("Only these shapes (ids). Default: the whole page.")] List<string>? ids = null,
        CancellationToken cancel = default) =>
        Edit("laid out the page", s =>
        {
            var d = s.Document;
            d.PageIndex = ids is { Count: > 0 } ? Targets(d, null, ids)[0].Page : PageIndex(d, page);
            var chosen = ids is { Count: > 0 } ? Targets(d, null, ids).Select(t => t.Shape).ToList() : d.Shapes.ToList();
            var shapes = chosen.Where(x => x is not ConnectorShape and not ContainerShape && d.ContainerFor(x) is null).ToList();
            var ok = GraphLayout.Apply(shapes, d.Shapes.OfType<ConnectorShape>().ToList(), Flow(flow), new Rect(0, 0, d.PageWidth, d.PageHeight));
            if (!ok) throw new InvalidOperationException("Nothing to lay out: join two or more shapes with connectors first.");
            FitPage(d);
            d.Refresh();
            return new JsonObject { ["page"] = d.PageIndex + 1, ["laid_out"] = shapes.Count, ["size"] = $"{R(d.PageWidth)} x {R(d.PageHeight)}" };
        }, cancel);

    [McpServerTool(Name = "undo")]
    [Description("Takes back the last change (an agent's call, or the person's own edit), as Ctrl+Z does.")]
    public Task<string> Undo(CancellationToken cancel = default) =>
        Read("undid", s =>
        {
            if (!s.History.CanUndo) throw new InvalidOperationException("There's nothing to undo.");
            s.History.Undo();
            s.Refresh();
            return new JsonObject { ["undone"] = true, ["can_undo"] = s.History.CanUndo };
        }, cancel);

    [McpServerTool(Name = "redo")]
    [Description("Puts back the change last undone.")]
    public Task<string> Redo(CancellationToken cancel = default) =>
        Read("redid", s =>
        {
            if (!s.History.CanRedo) throw new InvalidOperationException("There's nothing to redo.");
            s.History.Redo();
            s.Refresh();
            return new JsonObject { ["redone"] = true };
        }, cancel);

    // ───────────────────────────── helpers ─────────────────────────────

    private static void Discardable(IDiagramSession s, bool discard)
    {
        if (s.Document.IsModified && !discard)
            throw new InvalidOperationException("The open drawing has changes that haven't been saved. Save it first (save_drawing), or call again with discard_changes true to throw them away.");
    }

    internal static int PageIndex(DiagramDocument d, int? page)
    {
        if (page is null) return d.PageIndex;
        if (page < 1 || page > d.Pages.Count) throw new ArgumentException($"There's no page {page}: the drawing has {d.Pages.Count} page{(d.Pages.Count == 1 ? "" : "s")}. add_page adds one.");
        return page.Value - 1;
    }

    private static (DiagramShape Shape, int Page) Shape(DiagramDocument d, string id) =>
        ShapeIds.Find(d, id) ?? throw new ArgumentException($"There's no shape \"{id}\". get_page lists a page's shapes and their ids.");

    private static List<(DiagramShape Shape, int Page)> Targets(DiagramDocument d, string? id, List<string>? ids)
    {
        var list = new List<string>();
        if (id is not null) list.Add(id);
        if (ids is not null) list.AddRange(ids);
        if (list.Count == 0) throw new ArgumentException("Give the shape's id (or ids for several). get_page lists them.");
        return list.Distinct(StringComparer.OrdinalIgnoreCase).Select(x => Shape(d, x)).ToList();
    }

    /// <summary>A shape of a kind named by its stencil's name or its kind.</summary>
    internal static DiagramShape Make(string kind, Rect bounds)
    {
        var k = KindOf(kind);
        var shape = ShapeFactory.Create(k, bounds);
        StencilCatalogue.Find(k)?.ApplyDefaults(shape);
        return shape;
    }

    internal static ShapeKind KindOf(string kind)
    {
        var name = kind.Trim();
        var squashed = new string(name.Where(char.IsLetterOrDigit).ToArray());
        if (StencilCatalogue.All.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } byName) return byName.Kind;
        if (System.Enum.TryParse<ShapeKind>(squashed, true, out var k) && k != ShapeKind.Connector) return k;
        if (StencilCatalogue.All.FirstOrDefault(x => new string(x.Name.Where(char.IsLetterOrDigit).ToArray()).Equals(squashed, StringComparison.OrdinalIgnoreCase)) is { } loose) return loose.Kind;
        var near = StencilCatalogue.All.Where(x => StencilCatalogue.Matches(x, name.Split(' ')[0])).Select(x => x.Name).Distinct().Take(6).ToList();
        throw new ArgumentException($"There's no stencil \"{kind}\"." + (near.Count > 0 ? $" Perhaps: {string.Join(", ", near)}." : "") + " list_stencils lists them.");
    }

    internal static ConnectorShape Join(DiagramShape from, DiagramShape to, string label, string? routing, string? startCap, string? endCap) => new(default, default)
    {
        StartShape = from,
        EndShape = to,
        StartPort = -1,
        EndPort = -1,
        Routing = routing is null ? ConnectorRouting.Orthogonal : Choice<ConnectorRouting>(routing, "routing"),
        Text = label,
        StartCap = startCap is null ? EndCapStyle.None : Cap(startCap),
        EndCap = endCap is null ? EndCapStyle.Arrow : Cap(endCap),
    };

    private static EndCapStyle Cap(string text) => Choice<EndCapStyle>(text, "cap");

    internal static T Choice<T>(string text, string what) where T : struct, System.Enum =>
        System.Enum.TryParse<T>(new string(text.Trim().Where(c => c != '_' && c != ' ' && c != '-').ToArray()), true, out var v) && System.Enum.IsDefined(v) ? v
        : throw new ArgumentException($"{what} is one of {string.Join(", ", System.Enum.GetNames<T>().Select(Snake))}, not \"{text}\".");

    private static string Snake(string name) => string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    internal static LayoutFlow Flow(string flow) => flow.Trim().ToLowerInvariant() switch
    {
        "down" or "td" or "tb" or "vertical" => LayoutFlow.Down,
        "across" or "right" or "lr" or "horizontal" => LayoutFlow.Right,
        _ => throw new ArgumentException($"flow is down or across, not \"{flow}\"."),
    };

    private static double Size(double v, string what) =>
        double.IsFinite(v) && v is >= 4 and <= 20000 ? v : throw new ArgumentException($"{what} is from 4 to 20,000 page units.");

    internal static Color ColourOf(string text, string what) =>
        Color.TryParse(text.Trim(), out var c) ? c : throw new ArgumentException($"{what} is a colour such as \"#2D6CDF\", not \"{text}\".");

    internal static string Hex(Color c) => c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    internal static double R(double v) => Math.Round(v, 1);

    private static (double W, double H) Paper(string? paper, bool landscape)
    {
        var (w, h) = (paper?.Trim().ToLowerInvariant() ?? "letter") switch
        {
            "letter" => (816.0, 1056.0), "legal" => (816.0, 1344.0), "tabloid" => (1056.0, 1632.0),
            "a3" => (1123.0, 1587.0), "a4" => (794.0, 1123.0), "a5" => (559.0, 794.0),
            _ => throw new ArgumentException($"paper is Letter, Legal, Tabloid, A3, A4 or A5, not \"{paper}\"."),
        };
        return landscape ? (h, w) : (w, h);
    }

    private static string PaperName(double w, double h) =>
        (Math.Round(Math.Min(w, h)), Math.Round(Math.Max(w, h))) switch
        {
            (816, 1056) => "Letter", (816, 1344) => "Legal", (1056, 1632) => "Tabloid", (1123, 1587) => "A3", (794, 1123) => "A4", (559, 794) => "A5",
            _ => "custom",
        } + (w > h ? ", landscape" : "");

    /// <summary>Makes the page big enough for its drawing (with a margin), never smaller.</summary>
    internal static void FitPage(DiagramDocument d)
    {
        if (d.DrawingBounds is not { } b) return;
        double w = d.PageWidth, h = d.PageHeight;
        var needW = b.Right + 48;
        var needH = b.Bottom + 48;
        if (needW > w || needH > h) d.SetPageSize(Math.Max(w, Math.Ceiling(needW)), Math.Max(h, Math.Ceiling(needH)));
    }

    private static JsonArray? OffPage(DiagramDocument d, IEnumerable<DiagramShape> shapes)
    {
        var off = shapes.Where(x => x.Bounds.X < 0 || x.Bounds.Y < 0 || x.Bounds.Right > d.PageWidth || x.Bounds.Bottom > d.PageHeight).Select(x => (JsonNode)x.Uid).ToArray();
        return off.Length == 0 ? null : new JsonArray(off);
    }

    /// <summary>A shape or connector as an agent sees it.</summary>
    internal static JsonObject Describe(DiagramShape x)
    {
        var o = new JsonObject { ["id"] = x.Uid };
        if (x is ConnectorShape c)
        {
            o["from"] = c.StartShape?.Uid;
            o["to"] = c.EndShape?.Uid;
            if (c.Text.Length > 0) o["label"] = c.Text;
            o["routing"] = Snake(c.Routing.ToString());
            if (c.StartCap != EndCapStyle.None) o["start_cap"] = Snake(c.StartCap.ToString());
            o["end_cap"] = Snake(c.EndCap.ToString());
            if (c.StrokeStyle != StrokeStyle.Solid) o["line_style"] = Snake(c.StrokeStyle.ToString());
            return o;
        }
        o["kind"] = ShapeFactory.DisplayName(x.Kind);
        if (x.Text.Length > 0) o["text"] = x.Text;
        var b = x.Bounds;
        o["box"] = new JsonObject { ["x"] = R(b.X), ["y"] = R(b.Y), ["w"] = R(b.Width), ["h"] = R(b.Height) };
        o["fill"] = Hex(x.Fill);
        o["stroke"] = Hex(x.Stroke);
        if (x.Rotation != 0) o["rotation"] = R(x.Rotation);
        if (x.Container is { } container) o["in"] = container.Uid;
        if (x.GroupId != 0) o["group"] = x.GroupId;
        return o;
    }

    /// <summary>Runs a call that changes nothing (or replaces the drawing wholesale) and answers in JSON.</summary>
    private Task<string> Read(string action, Func<IDiagramSession, JsonObject> work, CancellationToken cancel) =>
        Run(action, s =>
        {
            ShapeIds.Ensure(s.Document);
            return work(s).ToJsonString(Json.Options);
        }, cancel);

    /// <summary>
    /// Runs a change as one undo step (the document's batch). If it fails, the drawing goes back to how it was, and no
    /// step is left behind.
    /// </summary>
    private Task<string> Edit(string label, Func<IDiagramSession, JsonObject> work, CancellationToken cancel) =>
        Run(label, s =>
        {
            var d = s.Document;
            ShapeIds.Ensure(d);
            var before = DiagramFile.ToJson(d);
            var page = d.PageIndex;
            var wasModified = d.IsModified;
            JsonObject result;
            using (d.BeginBatch())
            {
                try
                {
                    result = work(s);
                    ShapeIds.Ensure(d);
                    d.MarkModified();
                }
                catch
                {
                    d.ReplaceWith(DiagramFile.FromJson(before));
                    d.PageIndex = Math.Min(page, d.Pages.Count - 1);
                    throw;
                }
            }
            if (!wasModified && DiagramFile.ToJson(d) == before) d.MarkSaved();
            s.Refresh();
            return result.ToJsonString(Json.Options);
        }, cancel);

    private async Task<T> Run<T>(string action, Func<IDiagramSession, T> work, CancellationToken cancel)
    {
        try
        {
            return await host.RunAsync(action, work, cancel).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not McpException and not OperationCanceledException)
        {
            throw new McpException(Explain(e), e);
        }
    }

    private async Task<T> RunAsync<T>(string action, Func<IDiagramSession, Task<T>> work, CancellationToken cancel)
    {
        try
        {
            return await host.RunAsync(action, work, cancel).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not McpException and not OperationCanceledException)
        {
            throw new McpException(Explain(e), e);
        }
    }

    /// <summary>Asks the person (MCP elicitation; the in-app chat shows buttons). Throws if they say no or can't be asked.</summary>
    private static async Task Confirm(McpServer server, string question, CancellationToken cancel)
    {
        ElicitResult answer;
        try
        {
            answer = await server.ElicitAsync(new ElicitRequestParams
            {
                Message = question,
                RequestedSchema = new ElicitRequestParams.RequestSchema { Properties = { } },
            }, cancel).ConfigureAwait(false);
        }
        catch (Exception e) when (e is McpException or InvalidOperationException or NotSupportedException)
        {
            throw new McpException($"{question} The person couldn't be asked ({e.Message}), so nothing was changed.");
        }
        if (answer.Action != "accept")
            throw new McpException($"The person said no: {question.Replace(" Go ahead?", "", StringComparison.Ordinal)}. Nothing was changed. Ask them what they want, or do less at a time.");
    }

    internal static string Explain(Exception e) => e switch
    {
        ArgumentException or InvalidOperationException or FormatException or NotSupportedException or IOException
            or UnauthorizedAccessException or DiagramBusyException or JsonException or InvalidDataException or KeyNotFoundException => e.Message,
        _ => $"Something went wrong ({e.GetType().Name}): {e.Message}",
    };
}
