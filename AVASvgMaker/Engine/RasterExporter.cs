using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AVASvgMaker.Models;
using SkiaSharp;

namespace AVASvgMaker.Engine;

/// <summary>
/// Renders the page to a picture.
///
/// SVG is the better format to keep a drawing in, but a picture is what goes into a document,
/// a ticket or a chat message. The shapes draw themselves the same way they do on screen -
/// there is no second rendering path to keep in step - onto a bare white page with none of the
/// editor's furniture: no grid, no selection, no workspace around it.
///
/// The four formats differ only in how the finished bitmap is written out, so they share
/// everything up to that point and one dialog drives them all.
/// </summary>
public static class RasterExporter
{
    /// <summary>The largest image that will be produced, to keep a big page at 4x sane.</summary>
    private const int MaximumPixels = 100_000_000;

    private const int BmpFileHeaderSize = 14;
    private const int BmpInfoHeaderSize = 40;

    public static PixelSize SizeAt(DiagramDocument document, double scale) => new(
        Math.Max(1, (int)Math.Round(document.PageWidth * scale)),
        Math.Max(1, (int)Math.Round(document.PageHeight * scale)));

    public static bool IsTooLarge(DiagramDocument document, double scale)
    {
        var size = SizeAt(document, scale);
        return (long)size.Width * size.Height > MaximumPixels;
    }

    /// <summary>
    /// The size of the file that will be written, where that is knowable. A BMP is
    /// uncompressed, so it is worth warning about before it is written; a PNG's size depends
    /// on the drawing and there is no honest number to give.
    /// </summary>
    public static long? FileSize(DiagramDocument document, double scale, RasterFormat format)
    {
        if (format != RasterFormat.Bmp)
            return null;

        var size = SizeAt(document, scale);
        return BmpFileHeaderSize + BmpInfoHeaderSize + (long)Stride(size.Width) * size.Height;
    }

    /// <summary>
    /// Quality for the lossy encoders. A diagram is flat colour and hard edges, which is the
    /// worst case for JPEG - the ringing shows up around every line - so the default is high
    /// enough that it does not, and the lower settings are there for when the file size is
    /// what matters.
    /// </summary>
    public const int DefaultQuality = 95;

    public static void Export(
        DiagramDocument document, Stream stream, double scale, RasterFormat format,
        int quality = DefaultQuality)
    {
        // Routes and lane positions are refreshed while painting on screen; an export must
        // not depend on the page having been looked at first.
        document.Refresh();

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

        switch (format)
        {
            case RasterFormat.Bmp:
                WriteBmp(bitmap, stream, 96 * scale);
                break;

            case RasterFormat.Jpeg:
                Encode(bitmap, stream, SKEncodedImageFormat.Jpeg, quality);
                break;

            case RasterFormat.Webp:
                Encode(bitmap, stream, SKEncodedImageFormat.Webp, quality);
                break;

            default:
                bitmap.Save(stream);
                break;
        }
    }

    /// <summary>
    /// Hands the pixels to Skia to compress. Of the thirteen formats Skia names, only PNG,
    /// JPEG and WebP actually have an encoder compiled in - the rest can be read but not
    /// written, which is why BMP is written by hand below.
    /// </summary>
    private static void Encode(RenderTargetBitmap bitmap, Stream stream, SKEncodedImageFormat format, int quality)
    {
        var colours = bitmap.Format == PixelFormat.Rgba8888 ? SKColorType.Rgba8888 : SKColorType.Bgra8888;
        var info = new SKImageInfo(bitmap.PixelSize.Width, bitmap.PixelSize.Height, colours, SKAlphaType.Premul);

        // Skia compresses from one contiguous buffer, so unlike the BMP writer this cannot be
        // done a row at a time.
        using var pixels = new SKBitmap(info);

        bitmap.CopyPixels(
            new PixelRect(0, 0, info.Width, info.Height), pixels.GetPixels(), pixels.ByteCount, pixels.RowBytes);

        using var image = SKImage.FromBitmap(pixels);
        using var data = image.Encode(format, quality)
            ?? throw new NotSupportedException($"Skia has no encoder for {format}.");

        data.SaveTo(stream);
    }

    /// <summary>A BMP row is packed to three bytes a pixel and padded out to four.</summary>
    private static int Stride(int width) => (width * 3 + 3) & ~3;

    /// <summary>
    /// Writes an uncompressed 24-bit bottom-up BMP, which every tool that reads the format at
    /// all can open. Skia can decode BMP but will not encode it, so this is written by hand.
    /// The page is painted opaque white before anything else, so there is no alpha to lose and
    /// nothing to un-premultiply.
    /// </summary>
    private static void WriteBmp(RenderTargetBitmap bitmap, Stream stream, double dpi)
    {
        var width = bitmap.PixelSize.Width;
        var height = bitmap.PixelSize.Height;
        var stride = Stride(width);
        var pixelsPerMetre = (int)Math.Round(dpi / 0.0254);

        var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);

        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(BmpFileHeaderSize + BmpInfoHeaderSize + stride * (long)height <= uint.MaxValue
            ? (uint)(BmpFileHeaderSize + BmpInfoHeaderSize + (long)stride * height)
            : uint.MaxValue);
        writer.Write(0u);
        writer.Write((uint)(BmpFileHeaderSize + BmpInfoHeaderSize));

        writer.Write(BmpInfoHeaderSize);
        writer.Write(width);
        writer.Write(height);            // Positive: the rows run bottom to top.
        writer.Write((ushort)1);         // Planes.
        writer.Write((ushort)24);        // Bits per pixel.
        writer.Write(0u);                // BI_RGB - no compression.
        writer.Write((uint)((long)stride * height));
        writer.Write(pixelsPerMetre);
        writer.Write(pixelsPerMetre);
        writer.Write(0u);                // Colours used - all of them.
        writer.Write(0u);                // Colours that matter - all of them.

        // A row at a time: a full copy of a 4x page would be hundreds of megabytes.
        var sourceStride = width * 4;
        var source = Marshal.AllocHGlobal(sourceStride);
        var argb = new byte[sourceStride];
        var row = new byte[stride];      // The padding bytes are written as the zeroes they start as.

        // Whichever way round the backend hands the channels over, green sits in the middle.
        var format = bitmap.Format ?? PixelFormat.Bgra8888;
        var blue = format == PixelFormat.Rgba8888 ? 2 : 0;
        var red = 2 - blue;

        try
        {
            for (var y = height - 1; y >= 0; y--)
            {
                bitmap.CopyPixels(new PixelRect(0, y, width, 1), source, sourceStride, sourceStride);
                Marshal.Copy(source, argb, 0, sourceStride);

                for (int x = 0, to = 0; x < width; x++, to += 3)
                {
                    var from = x * 4;
                    row[to] = argb[from + blue];
                    row[to + 1] = argb[from + 1];
                    row[to + 2] = argb[from + red];
                }

                writer.Write(row, 0, stride);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(source);
            writer.Flush();
            writer.Dispose();
        }
    }
}
