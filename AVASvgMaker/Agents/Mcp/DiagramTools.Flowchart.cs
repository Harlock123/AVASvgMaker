using System.Net.Http;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using ModelContextProtocol.Server;

namespace AVASvgMaker.Agents.Mcp;

/// <summary>A step of a flowchart (add_flowchart).</summary>
public sealed class FlowNode
{
    [JsonPropertyName("id"), Description("An id of your own for the step, used by edges (\"start\", \"n1\", \"check_input\").")]
    public string Id { get; set; } = "";

    [JsonPropertyName("label"), Description("What the step says, in plain words.")]
    public string Label { get; set; } = "";

    [JsonPropertyName("kind"), Description("start, end, process (the default), decision, input, output, data, database, document, subroutine, preparation, manual, delay, display or note.")]
    public string? Kind { get; set; }

    [JsonPropertyName("fill"), Description("Fill colour, \"#RRGGBB\". Optional.")]
    public string? Fill { get; set; }
}

/// <summary>An arrow of a flowchart (add_flowchart).</summary>
public sealed class FlowEdge
{
    [JsonPropertyName("from"), Description("The step it leaves (a node id).")]
    public string From { get; set; } = "";

    [JsonPropertyName("to"), Description("The step it goes to (a node id).")]
    public string To { get; set; } = "";

    [JsonPropertyName("label"), Description("Its label: \"yes\", \"no\", a case, a condition. Optional.")]
    public string? Label { get; set; }
}

/// <summary>The tools for flowcharts and for reading the files they're made from.</summary>
public sealed partial class DiagramTools
{
    /// <summary>How many characters read_file returns at most in one call.</summary>
    private const int ReadLimit = 60_000;

    [McpServerTool(Name = "add_flowchart")]
    [Description("Draws a whole flowchart in one step: nodes (each an id of your own, a label and a kind) become shapes, edges become connectors (labelled \"yes\"/\"no\" and so on), and the lot is laid out in layers down the page (or across), with the page grown to fit. Goes on a new page by default. Returns each node's shape id. The quickest way to chart a process, an algorithm or a source file's control flow.")]
    public Task<string> AddFlowchart(
        [Description("The steps, in reading order: each with id, label and kind (start, end, process, decision, input, output, data, database, document, subroutine, preparation, manual, delay, display, note).")] List<FlowNode> nodes,
        [Description("The arrows: each with from and to (node ids) and an optional label.")] List<FlowEdge> edges,
        [Description("A title, drawn above the chart and used as the new page's name. Optional.")] string? title = null,
        [Description("down (the default) or across.")] string flow = "down",
        [Description("Put it on a new page. Default true; false draws it on the page shown, below anything there.")] bool new_page = true,
        CancellationToken cancel = default) =>
        Edit("drew a flowchart", s =>
        {
            if (nodes.Count == 0) throw new ArgumentException("Give at least one node.");
            if (nodes.Count > 400) throw new ArgumentException("A flowchart is up to 400 nodes; split a bigger one across pages.");
            var dupes = nodes.GroupBy(n => n.Id.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Key.Length == 0 || g.Count() > 1).Select(g => g.Key).ToList();
            if (dupes.Count > 0) throw new ArgumentException($"Every node needs an id of its own; {(dupes[0].Length == 0 ? "one has none" : $"\"{dupes[0]}\" is used twice")}.");
            var d = s.Document;
            var direction = Flow(flow);
            if (new_page)
            {
                d.AddPage();
                d.RenamePage(d.PageIndex, string.IsNullOrWhiteSpace(title) ? "Flowchart" : Clip(title.Trim(), 40));
            }
            var top = new_page ? 0 : (d.DrawingBounds?.Bottom ?? 0);
            var made = new Dictionary<string, DiagramShape>(StringComparer.OrdinalIgnoreCase);
            var x = 48.0;
            foreach (var n in nodes)
            {
                var (kind, w, h) = FlowShape(n.Kind, n.Label);
                var shape = ShapeFactory.Create(kind, new Rect(x, top + 120, w, h));
                StencilCatalogue.Find(kind)?.ApplyDefaults(shape);
                shape.Text = n.Label.Replace("\\n", "\n");
                if (n.Fill is not null) shape.Fill = ColourOf(n.Fill, "fill");
                d.Shapes.Add(shape);
                made[n.Id.Trim()] = shape;
                x += w + 20;
            }
            var lines = new List<ConnectorShape>();
            foreach (var e in edges)
            {
                if (!made.TryGetValue(e.From.Trim(), out var from)) throw new ArgumentException($"An edge leaves \"{e.From}\", which isn't a node id. The ids: {string.Join(", ", made.Keys.Take(20))}.");
                if (!made.TryGetValue(e.To.Trim(), out var to)) throw new ArgumentException($"An edge goes to \"{e.To}\", which isn't a node id. The ids: {string.Join(", ", made.Keys.Take(20))}.");
                var line = Join(from, to, e.Label ?? "", null, null, null);
                d.Shapes.Add(line);
                lines.Add(line);
            }
            var placed = made.Values.ToList();
            var area = new Rect(0, top, d.PageWidth, Math.Max(d.PageHeight - top, 200));
            if (placed.Count >= 2) GraphLayout.Apply(placed, lines, direction, area);
            // Down the page from below the title (and anything already there), from the left margin.
            if (placed.Count > 0)
            {
                var minX = placed.Min(p => p.Bounds.X);
                var minY = placed.Min(p => p.Bounds.Y);
                var shift = new Vector(48 - minX, top + (string.IsNullOrWhiteSpace(title) ? 48 : 96) - minY);
                foreach (var p in placed) p.Translate(shift);
            }
            if (!string.IsNullOrWhiteSpace(title))
            {
                var heading = ShapeFactory.Create(ShapeKind.TextBox, new Rect(48, top + 24, Math.Max(240, Math.Min(d.PageWidth - 96, title.Length * 13)), 40));
                heading.Text = title.Trim();
                heading.FontSize = 20;
                heading.Bold = true;
                heading.TextAlign = TextAlign.Left;
                d.Shapes.Add(heading);
            }
            d.NormaliseOrder();
            FitPage(d);
            d.Refresh();
            ShapeIds.Ensure(d);
            return new JsonObject
            {
                ["page"] = d.PageIndex + 1,
                ["ids"] = new JsonObject(made.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value.Uid))),
                ["connectors"] = lines.Count,
                ["size"] = $"{R(d.PageWidth)} x {R(d.PageHeight)}",
                ["next"] = "Look at it with render_page; set_shape, connect and lay_out tidy it.",
            };
        }, cancel);

    [McpServerTool(Name = "import_mermaid")]
    [Description("Draws a Mermaid flowchart (\"flowchart TD\" or \"graph LR\" text: nodes in [ ], ( ), { }, [( )], arrows -->, labels -->|yes|, subgraphs) as shapes and connectors, laid out, on a new page. Returns what it made with their ids, and anything it left out.")]
    public Task<string> ImportMermaid(
        [Description("The Mermaid flowchart text.")] string code,
        [Description("The new page's name. Default \"Mermaid\".")] string? name = null,
        CancellationToken cancel = default) =>
        Edit("drew a Mermaid flowchart", s =>
        {
            var d = s.Document;
            var result = MermaidImporter.Read(code, d.PageWidth, d.PageHeight);
            if (result.Count == 0) throw new ArgumentException($"{result.Summary}: start the text with \"flowchart TD\" (or LR), then a line per node or arrow, such as A[Start] --> B{{Ready?}}.");
            d.AddPage();
            d.RenamePage(d.PageIndex, string.IsNullOrWhiteSpace(name) ? "Mermaid" : Clip(name.Trim(), 40));
            d.SetPageSize(result.Page.Width, result.Page.Height);
            foreach (var shape in result.Page.Shapes) d.Shapes.Add(shape);
            d.NormaliseOrder();
            FitPage(d);
            d.Refresh();
            ShapeIds.Ensure(d);
            return new JsonObject
            {
                ["page"] = d.PageIndex + 1,
                ["summary"] = result.Summary,
                ["shapes"] = new JsonArray(d.Shapes.Where(x => x is not ConnectorShape).Select(x => (JsonNode)new JsonObject { ["id"] = x.Uid, ["text"] = x.Text }).ToArray()),
                ["connectors"] = d.Shapes.Count(x => x is ConnectorShape),
            };
        }, cancel);

    [McpServerTool(Name = "get_mermaid", ReadOnly = true, Idempotent = true)]
    [Description("A page as Mermaid flowchart text (for a README, a wiki or a pull request), with what couldn't be said in Mermaid listed.")]
    public Task<string> GetMermaid(
        [Description(PageHelp)] int? page = null,
        CancellationToken cancel = default) =>
        Read("wrote Mermaid", s =>
        {
            var result = MermaidExporter.Export(s.Document.Pages[PageIndex(s.Document, page)]);
            return new JsonObject { ["mermaid"] = result.Code, ["left_out"] = new JsonArray(result.Skipped.Select(x => (JsonNode)x).ToArray()) };
        }, cancel);

    [McpServerTool(Name = "list_files", ReadOnly = true, Idempotent = true)]
    [Description("Lists files in the working folder (or a folder inside it): names, sizes and sub-folders, to find a source file to read and chart. Optionally only those matching a pattern such as \"*.cs\", and through sub-folders.")]
    public Task<string> ListFiles(
        [Description("A folder inside the working folder. Default: the working folder.")] string folder = ".",
        [Description("A file name pattern, such as \"*.py\" or \"*Controller*\". Default: every file.")] string pattern = "*",
        [Description("Look through sub-folders too. Default false.")] bool recursive = false,
        CancellationToken cancel = default) =>
        Read("listed files", _ =>
        {
            var full = folder is "." or "" ? _files.Root : _files.Resolve(folder);
            if (!Directory.Exists(full)) throw new DirectoryNotFoundException($"There's no folder {folder} in the working folder.");
            var skip = new[] { "bin", "obj", "node_modules", ".git", ".vs", ".idea" };
            var files = Directory.EnumerateFiles(full, pattern, new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true })
                .Where(f => !Path.GetRelativePath(full, f).Split(Path.DirectorySeparatorChar).Any(part => skip.Contains(part, StringComparer.OrdinalIgnoreCase)))
                .Take(501).ToList();
            var folders = recursive ? [] : Directory.EnumerateDirectories(full).Select(Path.GetFileName).Where(n => n is not null && !skip.Contains(n, StringComparer.OrdinalIgnoreCase)).Take(200).ToList();
            return new JsonObject
            {
                ["folder"] = Path.GetRelativePath(_files.Root, full),
                ["folders"] = new JsonArray(folders.Select(f => (JsonNode)f!).ToArray()),
                ["files"] = new JsonArray(files.Take(500).Select(f => (JsonNode)new JsonObject { ["path"] = Path.GetRelativePath(_files.Root, f), ["bytes"] = new FileInfo(f).Length }).ToArray()),
                ["more"] = files.Count > 500 ? "More than 500: narrow it with a pattern or a folder." : null,
            };
        }, cancel);

    [McpServerTool(Name = "read_file", ReadOnly = true, Idempotent = true)]
    [Description("Reads a text file in the working folder (source code, a script, a config, a log, Markdown), with its lines numbered. A long file comes in pieces: give start_line to read on from where the last piece ended. Read the whole of what you're asked to chart before drawing it.")]
    public Task<string> ReadFile(
        [Description("The file, relative to the working folder.")] string path,
        [Description("The first line to read, from 1. Default 1.")] int start_line = 1,
        [Description("At most this many lines. Default 600.")] int max_lines = 600,
        CancellationToken cancel = default) =>
        Read("read a file", _ =>
        {
            var full = _files.Resolve(path);
            if (!File.Exists(full)) throw new FileNotFoundException($"There's no {path} in the working folder. list_files shows what is there.");
            var info = new FileInfo(full);
            if (info.Length > 20_000_000) throw new IOException($"{path} is {info.Length / 1_000_000} MB: too big to read here.");
            var bytes = File.ReadAllBytes(full);
            if (bytes.Take(8000).Any(b => b == 0)) throw new NotSupportedException($"{path} isn't text (it has binary data in it).");
            var all = Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n").Split('\n');
            if (start_line < 1 || start_line > Math.Max(1, all.Length)) throw new ArgumentException($"start_line is from 1 to {all.Length}.");
            var take = Math.Clamp(max_lines, 1, 5000);
            var text = new StringBuilder();
            var last = start_line - 1;
            for (var i = start_line - 1; i < all.Length && i < start_line - 1 + take; i++)
            {
                var line = $"{i + 1,5}  {all[i]}\n";
                if (text.Length + line.Length > ReadLimit && text.Length > 0) break;
                text.Append(line);
                last = i + 1;
            }
            return new JsonObject
            {
                ["path"] = Path.GetRelativePath(_files.Root, full),
                ["lines"] = all.Length,
                ["from"] = start_line,
                ["to"] = last,
                ["text"] = text.ToString(),
                ["more"] = last < all.Length ? $"Lines {last + 1} to {all.Length} remain: call again with start_line {last + 1}." : null,
            };
        }, cancel);

    /// <summary>The stencil and size for a flowchart step of a kind, sized to its label.</summary>
    private static (ShapeKind Kind, double W, double H) FlowShape(string? kind, string label)
    {
        var longest = label.Replace("\\n", "\n").Split('\n').Max(l => l.Length);
        var lines = Math.Max(1, (int)Math.Ceiling(longest / 24.0)) + label.Count(c => c == '\n');
        var w = Math.Clamp(longest * 7.4 + 36, 120, 240);
        var h = Math.Clamp(26 + lines * 17, 56, 140);
        return (kind?.Trim().ToLowerInvariant() ?? "process") switch
        {
            "start" or "end" or "stop" or "terminator" or "begin" or "return" or "exit" => (ShapeKind.RoundedRectangle, w, Math.Min(h, 60)),
            "decision" or "if" or "condition" or "branch" or "loop" or "while" or "switch" => (ShapeKind.Diamond, Math.Clamp(longest * 8.5 + 70, 150, 260), Math.Clamp(h + 30, 90, 160)),
            "input" or "output" or "io" or "data" or "read" or "write" or "print" => (ShapeKind.Parallelogram, w + 20, h),
            "database" or "db" or "store" or "storage" => (ShapeKind.Cylinder, w, Math.Max(h, 70)),
            "document" or "report" or "file" => (ShapeKind.Document, w, Math.Max(h, 64)),
            "subroutine" or "call" or "function" or "predefined" or "method" => (ShapeKind.PredefinedProcess, w + 20, h),
            "preparation" or "init" or "setup" or "for" => (ShapeKind.Hexagon, w + 20, h),
            "manual" or "manual_input" or "user" => (ShapeKind.ManualInput, w, h),
            "delay" or "wait" => (ShapeKind.Delay, w, h),
            "display" or "show" => (ShapeKind.Display, w, h),
            "note" or "comment" or "text" => (ShapeKind.TextBox, w, h),
            "process" or "step" or "action" or "" => (ShapeKind.Rectangle, w, h),
            _ => throw new ArgumentException($"A node's kind is start, end, process, decision, input, output, data, database, document, subroutine, preparation, manual, delay, display or note, not \"{kind}\"."),
        };
    }

    private static string Clip(string s, int n) => s.Length <= n ? s : s[..n];
}
