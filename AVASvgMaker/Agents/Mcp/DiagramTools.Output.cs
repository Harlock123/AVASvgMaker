using System.Collections.Generic;
using System.Net.Http;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AVASvgMaker.Engine;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AVASvgMaker.Agents.Mcp;

/// <summary>The engine's own drawing and PDF writer, as the server uses them.</summary>
public sealed class EngineLibrary : IDiagramLibrary
{
    public byte[] Picture(DiagramDocument document, int page, double scale)
    {
        var shown = document.PageIndex;
        try
        {
            if (page != shown) document.PageIndex = page;
            using var stream = new MemoryStream();
            RasterExporter.Export(document, stream, scale, RasterFormat.Png);
            return stream.ToArray();
        }
        finally
        {
            if (document.PageIndex != shown) document.PageIndex = shown;
        }
    }

    public async Task ExportPdf(DiagramDocument document, string path)
    {
        await using var stream = File.Create(path);
        await PdfExporter.ExportAsync(document, stream, Path.GetFileNameWithoutExtension(path));
    }
}

/// <summary>The tools for looking at the drawing and sending it out.</summary>
public sealed partial class DiagramTools
{
    [McpServerTool(Name = "render_page", ReadOnly = true, Idempotent = true)]
    [Description("A picture of a page as it exports: look at it to check the drawing reads well (no shapes on top of each other, lines not crossing through shapes, labels fitting, everything on the page).")]
    public async Task<CallToolResult> RenderPage(
        [Description(PageHelp)] int? page = null,
        [Description("Pixels per page unit, from 0.25 to 2. Default 0.8 (a Letter page is then 653 by 845).")] double scale = 0.8,
        CancellationToken cancel = default)
    {
        if (!double.IsFinite(scale) || scale is < 0.25 or > 2) throw new McpException("scale is from 0.25 to 2.");
        var (png, about) = await Run("drew a page", s =>
        {
            var d = s.Document;
            var index = PageIndex(d, page);
            var p = d.Pages[index];
            if (RasterExporter.IsTooLarge(d, scale)) throw new InvalidOperationException("That page is too big to draw at this scale: use a smaller scale.");
            var image = host.Library.Picture(d, index, scale);
            var off = p.Shapes.Count(x => x.Bounds.X < -1 || x.Bounds.Y < -1 || x.Bounds.Right > p.Width + 1 || x.Bounds.Bottom > p.Height + 1);
            return (image, $"Page {index + 1} ({p.Name}, {R(p.Width)} x {R(p.Height)}) at {scale:0.##}x: {p.Shapes.Count(x => x is not Models.ConnectorShape)} shapes, {p.Shapes.Count(x => x is Models.ConnectorShape)} connectors."
                + (off > 0 ? $" {off} reach past the page's edge." : ""));
        }, cancel).ConfigureAwait(false);
        return new CallToolResult { Content = [ImageContentBlock.FromBytes(png, "image/png"), new TextContentBlock { Text = about }] };
    }

    [McpServerTool(Name = "export")]
    [Description("Exports the drawing into the working folder: svg, png, jpeg, webp or bmp (the page shown, or the one given), pdf (every page), mermaid (a page as flowchart text) or vsdx (a Visio drawing, every page).")]
    public Task<string> Export(
        [Description("The file, relative to the working folder; the format's extension is added if missing.")] string path,
        [Description("svg, pdf, png, jpeg, webp, bmp, mermaid or vsdx. Default: from the path's extension.")] string? format = null,
        [Description(PageHelp + " (for svg, pictures and mermaid).")] int? page = null,
        [Description("For pictures: pixels per page unit, from 0.25 to 8. Default 2.")] double scale = 2,
        [Description("Write over an existing file. Default false.")] bool overwrite = false,
        CancellationToken cancel = default) =>
        RunAsync("exported the drawing", async s =>
        {
            var d = s.Document;
            var f = (format ?? Path.GetExtension(path).TrimStart('.')).Trim().ToLowerInvariant() switch
            {
                "jpg" => "jpeg", "mmd" => "mermaid", "visio" => "vsdx", var x => x,
            };
            var ext = f switch
            {
                "svg" => ".svg", "pdf" => ".pdf", "png" => ".png", "jpeg" => ".jpg", "webp" => ".webp", "bmp" => ".bmp", "mermaid" => ".mmd", "vsdx" => ".vsdx",
                _ => throw new ArgumentException($"format is svg, pdf, png, jpeg, webp, bmp, mermaid or vsdx, not \"{format ?? Path.GetExtension(path)}\"."),
            };
            var full = _files.Resolve(Path.HasExtension(path) && (Path.GetExtension(path).Equals(ext, StringComparison.OrdinalIgnoreCase) || (f == "jpeg" && Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase))) ? path : path + ext);
            _files.CheckWritable(full, overwrite);
            var index = PageIndex(d, page);
            var shown = d.PageIndex;
            try
            {
                if (f is "svg" or "png" or "jpeg" or "webp" or "bmp" or "mermaid") d.PageIndex = index;
                switch (f)
                {
                    case "svg":
                        await File.WriteAllTextAsync(full, SvgExporter.Export(d));
                        break;
                    case "pdf":
                        await host.Library.ExportPdf(d, full);
                        break;
                    case "mermaid":
                        await File.WriteAllTextAsync(full, MermaidExporter.Export(d.CurrentPage).Code);
                        break;
                    case "vsdx":
                        await using (var stream = File.Create(full)) VisioExporter.Write(d, stream, Path.GetFileNameWithoutExtension(full));
                        break;
                    default:
                        if (!double.IsFinite(scale) || scale is < 0.25 or > 8) throw new ArgumentException("scale is from 0.25 to 8.");
                        if (RasterExporter.IsTooLarge(d, scale)) throw new ArgumentException("That's too big a picture: use a smaller scale.");
                        await using (var stream = File.Create(full))
                            RasterExporter.Export(d, stream, scale, f switch { "png" => RasterFormat.Png, "jpeg" => RasterFormat.Jpeg, "webp" => RasterFormat.Webp, _ => RasterFormat.Bmp });
                        break;
                }
            }
            finally
            {
                if (d.PageIndex != shown) d.PageIndex = shown;
            }
            _files.Wrote(full);
            return new JsonObject
            {
                ["exported"] = Path.GetRelativePath(host.Root, full),
                ["format"] = f,
                ["pages"] = f is "pdf" or "vsdx" ? d.Pages.Count : 1,
                ["bytes"] = new FileInfo(full).Length,
            }.ToJsonString(Json.Options);
        }, cancel);
}
