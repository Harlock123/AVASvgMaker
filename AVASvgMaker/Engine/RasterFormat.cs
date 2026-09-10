namespace AVASvgMaker.Engine;

/// <summary>The rasterised export formats. Both come off the same render.</summary>
public enum RasterFormat
{
    Png,
    Bmp
}

public static class RasterFormatExtensions
{
    public static string Label(this RasterFormat format) => format switch
    {
        RasterFormat.Bmp => "BMP",
        _ => "PNG"
    };

    public static string Extension(this RasterFormat format) => format switch
    {
        RasterFormat.Bmp => "bmp",
        _ => "png"
    };

    public static string MimeType(this RasterFormat format) => format switch
    {
        RasterFormat.Bmp => "image/bmp",
        _ => "image/png"
    };

    public static string Description(this RasterFormat format) => format switch
    {
        RasterFormat.Bmp => "Windows bitmap",
        _ => "PNG image"
    };
}
