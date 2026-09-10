namespace AVASvgMaker.Engine;

/// <summary>The rasterised export formats. All four come off the same render.</summary>
public enum RasterFormat
{
    Png,
    Jpeg,
    Webp,
    Bmp
}

public static class RasterFormatExtensions
{
    public static string Label(this RasterFormat format) => format switch
    {
        RasterFormat.Jpeg => "JPEG",
        RasterFormat.Webp => "WebP",
        RasterFormat.Bmp => "BMP",
        _ => "PNG"
    };

    public static string Extension(this RasterFormat format) => format switch
    {
        RasterFormat.Jpeg => "jpg",
        RasterFormat.Webp => "webp",
        RasterFormat.Bmp => "bmp",
        _ => "png"
    };

    /// <summary>Everything the file picker should accept, which for JPEG is both spellings.</summary>
    public static string[] Patterns(this RasterFormat format) => format switch
    {
        RasterFormat.Jpeg => ["*.jpg", "*.jpeg"],
        _ => [$"*.{format.Extension()}"]
    };

    public static string MimeType(this RasterFormat format) => format switch
    {
        RasterFormat.Jpeg => "image/jpeg",
        RasterFormat.Webp => "image/webp",
        RasterFormat.Bmp => "image/bmp",
        _ => "image/png"
    };

    public static string Description(this RasterFormat format) => format switch
    {
        RasterFormat.Jpeg => "JPEG image",
        RasterFormat.Webp => "WebP image",
        RasterFormat.Bmp => "Windows bitmap",
        _ => "PNG image"
    };

    /// <summary>Whether the encoder throws detail away, and so wants a quality to be chosen.</summary>
    public static bool IsLossy(this RasterFormat format) =>
        format is RasterFormat.Jpeg or RasterFormat.Webp;
}
