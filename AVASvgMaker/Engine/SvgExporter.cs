using System.Globalization;
using System.Text;

namespace AVASvgMaker.Engine;

/// <summary>Writes a document out as a standalone SVG file.</summary>
public static class SvgExporter
{
    public static string Export(DiagramDocument document)
    {
        var width = Num(document.PageWidth);
        var height = Num(document.PageHeight);

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>");
        sb.AppendLine(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\" " +
            $"width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">");
        sb.AppendLine($"  <rect x=\"0\" y=\"0\" width=\"{width}\" height=\"{height}\" fill=\"#FFFFFF\" />");

        foreach (var shape in document.Shapes)
            sb.Append(shape.ToSvg());

        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
