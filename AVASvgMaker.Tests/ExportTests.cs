using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless.XUnit;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using Xunit;

namespace AVASvgMaker.Tests;

public class ExportTests
{
    private static DiagramDocument Drawn(double width = 400, double height = 300)
    {
        var document = Harness.Page(width, height);
        Harness.Box(document, new Rect(40, 40, 140, 70), "Something to draw");
        Harness.Box(document, new Rect(220, 160, 140, 70), "and something else");
        return document;
    }

    [AvaloniaTheory]
    [InlineData(RasterFormat.Png)]
    [InlineData(RasterFormat.Jpeg)]
    [InlineData(RasterFormat.Webp)]
    [InlineData(RasterFormat.Bmp)]
    public void EveryPictureFormatWritesSomethingItsOwnReaderWouldKnow(RasterFormat format)
    {
        var document = Drawn();

        using var stream = new MemoryStream();
        RasterExporter.Export(document, stream, 2, format);

        var bytes = stream.ToArray();
        Assert.True(bytes.Length > 500, $"{format} wrote {bytes.Length} bytes");

        Assert.True(format switch
        {
            RasterFormat.Png => bytes[0] == 0x89 && bytes[1] == 'P' && bytes[2] == 'N' && bytes[3] == 'G',
            RasterFormat.Jpeg => bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[^2] == 0xFF && bytes[^1] == 0xD9,
            RasterFormat.Webp => bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F'
                                 && bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P',
            _ => bytes[0] == 'B' && bytes[1] == 'M'
        }, $"{format} signature");
    }

    [AvaloniaFact]
    public void ScaleDecidesTheSizeAndOversizeIsRefused()
    {
        var document = Drawn(400, 300);

        Assert.Equal(new PixelSize(400, 300), RasterExporter.SizeAt(document, 1));
        Assert.Equal(new PixelSize(1600, 1200), RasterExporter.SizeAt(document, 4));
        Assert.False(RasterExporter.IsTooLarge(document, 4));

        document.SetPageSize(20000, 20000);
        Assert.True(RasterExporter.IsTooLarge(document, 1));
    }

    [AvaloniaFact]
    public void ABmpIsExactlyTheSizeItSaidItWouldBe()
    {
        var document = Drawn();

        using var stream = new MemoryStream();
        RasterExporter.Export(document, stream, 2, RasterFormat.Bmp);

        var expected = RasterExporter.FileSize(document, 2, RasterFormat.Bmp);
        Assert.NotNull(expected);
        Assert.Equal(expected!.Value, stream.Length);

        // A PNG's size depends on the drawing, so nothing is promised about it.
        Assert.Null(RasterExporter.FileSize(document, 2, RasterFormat.Png));

        var header = stream.ToArray();
        var size = RasterExporter.SizeAt(document, 2);

        Assert.Equal(stream.Length, BitConverter.ToUInt32(header, 2));
        Assert.Equal(54u, BitConverter.ToUInt32(header, 10));
        Assert.Equal(size.Width, BitConverter.ToInt32(header, 18));
        Assert.Equal(size.Height, BitConverter.ToInt32(header, 22));
        Assert.Equal(24, BitConverter.ToUInt16(header, 28));
        Assert.Equal(0u, BitConverter.ToUInt32(header, 30));
    }

    [AvaloniaTheory]
    [InlineData(RasterFormat.Jpeg)]
    [InlineData(RasterFormat.Webp)]
    public void QualityDoesSomething(RasterFormat format)
    {
        var document = Drawn();

        long At(int quality)
        {
            using var stream = new MemoryStream();
            RasterExporter.Export(document, stream, 2, format, quality);
            return stream.Length;
        }

        Assert.True(At(100) > At(70), $"{format} at 100 is larger than at 70");
    }

    [AvaloniaFact]
    public async Task APdfPageComesOutItsTruePhysicalSize()
    {
        // 760 x 460 px at 96 DPI is 570 x 345 pt at 72. Getting this wrong is silent - the
        // drawing still looks right and only misbehaves when printed - so it is pinned here.
        var document = Harness.Page(760, 460);
        Harness.Box(document, new Rect(40, 40, 120, 60), "One");

        using var stream = new MemoryStream();
        await PdfExporter.ExportAsync(document, stream, "Test");

        var pdf = Encoding.Latin1.GetString(stream.ToArray());

        Assert.StartsWith("%PDF", pdf);
        Assert.Contains("%%EOF", pdf);
        Assert.Contains("/MediaBox [0 0 570 345]", pdf);
    }

    [AvaloniaFact]
    public async Task APdfHoldsEveryPageAtItsOwnSize()
    {
        var document = Harness.Page(816, 1056);
        Harness.Box(document, new Rect(40, 40, 120, 60), "Portrait");

        document.AddPage();
        document.SetPageSize(1056, 816);
        Harness.Box(document, new Rect(40, 40, 120, 60), "Landscape");

        document.PageIndex = 0;

        using var all = new MemoryStream();
        await PdfExporter.ExportAsync(document, all, "All", allPages: true);
        var everything = Encoding.Latin1.GetString(all.ToArray());

        Assert.Equal(2, Count(everything, "/Type /Page\n"));
        Assert.Contains("/MediaBox [0 0 612 792]", everything);
        Assert.Contains("/MediaBox [0 0 792 612]", everything);

        using var one = new MemoryStream();
        await PdfExporter.ExportAsync(document, one, "One", allPages: false);

        Assert.Equal(1, Count(Encoding.Latin1.GetString(one.ToArray()), "/Type /Page\n"));

        // Writing the document must not move the user off the page they were editing.
        Assert.Equal(0, document.PageIndex);
    }

    [AvaloniaFact]
    public void AnSvgLabelAnchorsWhereItIsDrawn()
    {
        var document = Harness.Page();
        var shape = Harness.Box(document, new Rect(40, 40, 200, 80), "Label");

        shape.TextAlign = TextAlign.Left;
        Assert.Contains("text-anchor=\"start\"", shape.ToSvg());

        shape.TextAlign = TextAlign.Right;
        Assert.Contains("text-anchor=\"end\"", shape.ToSvg());

        shape.TextAlign = TextAlign.Center;
        Assert.Contains("text-anchor=\"middle\"", shape.ToSvg());

        shape.Bold = true;
        shape.Italic = true;
        shape.FontName = "DejaVu Serif";

        var svg = shape.ToSvg();
        Assert.Contains("font-weight=\"bold\"", svg);
        Assert.Contains("font-style=\"italic\"", svg);
        Assert.Contains("font-family=\"DejaVu Serif, sans-serif\"", svg);

        // A plain label says none of it.
        var plain = Harness.Box(document, new Rect(40, 200, 200, 80), "Plain").ToSvg();
        Assert.DoesNotContain("font-weight", plain);
        Assert.DoesNotContain("font-style", plain);
    }

    [AvaloniaFact]
    public void TheSvgIsAWholePageOfTheRightSize()
    {
        var document = Harness.Page(640, 480);
        Harness.Box(document, new Rect(40, 40, 120, 60), "One");

        var svg = SvgExporter.Export(document);

        Assert.StartsWith("<?xml", svg);
        Assert.Contains("width=\"640\" height=\"480\"", svg);
        Assert.Contains("viewBox=\"0 0 640 480\"", svg);
        Assert.EndsWith("</svg>\n", svg.Replace("\r\n", "\n"));
    }

    private static int Count(string text, string needle)
    {
        int found = 0, at = 0;

        while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            found++;
            at += needle.Length;
        }

        return found;
    }
}
