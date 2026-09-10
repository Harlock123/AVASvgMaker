using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AVASvgMaker.Models;

namespace AVASvgMaker.Engine;

/// <summary>
/// Renders the page to a PNG.
///
/// SVG is the better format to keep a drawing in, but a picture is what goes into a document,
/// a ticket or a chat message. The shapes draw themselves the same way they do on screen -
/// there is no second rendering path to keep in step - onto a bare white page with none of the
/// editor's furniture: no grid, no selection, no workspace around it.
/// </summary>
public static class PngExporter
{
    /// <summary>The largest image that will be produced, to keep a big page at 4x sane.</summary>
    private const int MaximumPixels = 100_000_000;

    public static PixelSize SizeAt(DiagramDocument document, double scale) => new(
        Math.Max(1, (int)Math.Round(document.PageWidth * scale)),
        Math.Max(1, (int)Math.Round(document.PageHeight * scale)));

    public static bool IsTooLarge(DiagramDocument document, double scale)
    {
        var size = SizeAt(document, scale);
        return (long)size.Width * size.Height > MaximumPixels;
    }

    public static void Export(DiagramDocument document, Stream stream, double scale)
    {
        // Routes and lane positions are refreshed while painting on screen; an export must
        // not depend on the page having been looked at first.
        document.LayoutContainers();
        document.NormaliseOrder();
        document.RouteConnectors();

        var size = SizeAt(document, scale);

        // Drawing happens in page units; the bitmap's resolution does the scaling, so a
        // shape's geometry and stroke widths do not have to know anything about it.
        using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));

        using (var context = bitmap.CreateDrawingContext())
        {
            context.DrawRectangle(Brushes.White, null,
                new Rect(0, 0, document.PageWidth, document.PageHeight));

            foreach (var shape in document.Shapes)
                shape.Render(context);
        }

        bitmap.Save(stream);
    }
}
