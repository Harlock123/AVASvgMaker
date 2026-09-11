using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Skia.Helpers;
using AVASvgMaker.Models;
using SkiaSharp;

namespace AVASvgMaker.Engine;

/// <summary>
/// Writes the page as a PDF.
///
/// This is a vector export, like the SVG one, but it is produced the way the raster exports
/// are: the shapes draw themselves through their own <c>Render</c>, and Skia writes what they
/// drew into the document. So the curves stay curves, the text stays selectable and
/// searchable, and there is still no second rendering path to keep in step with the screen.
/// </summary>
public static class PdfExporter
{
    /// <summary>
    /// PDF measures in points, at 72 to the inch; the drawing is in pixels at 96. A Letter
    /// page 816 px wide is 612 pt, which is 8.5 inches, and prints as such.
    /// </summary>
    private const double PointsPerPixel = 72.0 / 96.0;

    /// <summary>
    /// Writes the whole document, one PDF page per diagram page, or just the page being
    /// edited when <paramref name="allPages"/> is false.
    /// </summary>
    public static async Task ExportAsync(
        DiagramDocument document, Stream stream, string? title, bool allPages = true)
    {
        var metadata = new SKDocumentPdfMetadata
        {
            Creator = "AVASvgMaker",
            Producer = "AVASvgMaker",
            Title = string.IsNullOrWhiteSpace(title) ? "Diagram" : title
        };

        using var pdf = SKDocument.CreatePdf(stream, metadata)
            ?? throw new IOException("Skia would not open a PDF document.");

        var pages = allPages ? document.Pages : [document.CurrentPage];

        foreach (var page in pages)
        {
            // Each page is written at its own size. PDF has no trouble with a document whose
            // pages differ, so a landscape page among portrait ones comes out landscape.
            var width = page.Width * PointsPerPixel;
            var height = page.Height * PointsPerPixel;

            // Routes and lane positions are refreshed while painting on screen. Only the page
            // being edited has been painted, so every page is brought up to date here rather
            // than the export depending on which ones have been looked at.
            document.Refresh(page);

            var canvas = pdf.BeginPage((float)width, (float)height);

            var visual = new PageVisual(document, page);
            visual.Measure(new Size(width, height));
            visual.Arrange(new Rect(0, 0, width, height));

            await DrawingContextHelper.RenderAsync(canvas, visual);

            pdf.EndPage();
        }

        pdf.Close();
    }

    /// <summary>
    /// The page with nothing else: white paper and the shapes on it. The editor's own canvas
    /// draws the grid, the selection and the workspace as well, so it cannot be used here.
    ///
    /// The pixels-to-points scale is applied here rather than to the Skia canvas, because
    /// <c>RenderAsync</c> installs its own transform and discards whatever the canvas was
    /// carrying. Skia's <c>RasterDpi</c> would scale the content too - it divides by it - but
    /// that is a side effect of a setting that means something else, so it is left alone.
    /// </summary>
    private sealed class PageVisual(DiagramDocument document, DiagramPage page) : Control
    {
        public override void Render(DrawingContext context)
        {
            using var scale = context.PushTransform(
                Matrix.CreateScale(PointsPerPixel, PointsPerPixel));

            context.DrawRectangle(page.Paper(), null,
                new Rect(0, 0, page.Width, page.Height));

            PageFurniture.DrawWatermark(context, document, page);

            foreach (var shape in page.Shapes)
                shape.Render(context);

            PageFurniture.DrawRunningHeads(context, document, page);
        }
    }
}
