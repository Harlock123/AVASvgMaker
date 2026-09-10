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

    public static async Task ExportAsync(DiagramDocument document, Stream stream, string? title)
    {
        // Routes and lane positions are refreshed while painting on screen; an export must
        // not depend on the page having been looked at first.
        document.LayoutContainers();
        document.NormaliseOrder();
        document.RouteConnectors();

        var metadata = new SKDocumentPdfMetadata
        {
            Creator = "AVASvgMaker",
            Producer = "AVASvgMaker",
            Title = string.IsNullOrWhiteSpace(title) ? "Diagram" : title
        };

        using var pdf = SKDocument.CreatePdf(stream, metadata)
            ?? throw new IOException("Skia would not open a PDF document.");

        var width = document.PageWidth * PointsPerPixel;
        var height = document.PageHeight * PointsPerPixel;

        var canvas = pdf.BeginPage((float)width, (float)height);

        var page = new PageVisual(document);
        page.Measure(new Size(width, height));
        page.Arrange(new Rect(0, 0, width, height));

        await DrawingContextHelper.RenderAsync(canvas, page);

        pdf.EndPage();
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
    private sealed class PageVisual(DiagramDocument document) : Control
    {
        public override void Render(DrawingContext context)
        {
            using var scale = context.PushTransform(
                Matrix.CreateScale(PointsPerPixel, PointsPerPixel));

            context.DrawRectangle(Brushes.White, null,
                new Rect(0, 0, document.PageWidth, document.PageHeight));

            foreach (var shape in document.Shapes)
                shape.Render(context);
        }
    }
}
