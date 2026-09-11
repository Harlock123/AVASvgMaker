using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVASvgMaker.Models;

namespace AVASvgMaker.Engine;

/// <summary>
/// What a page carries besides its shapes: a watermark across it, and a line of text along the
/// top and the bottom.
///
/// None of it is a shape. It cannot be selected, dragged or glued to, it is the same on every
/// page that asks for it, and it is drawn by the page rather than being part of the drawing -
/// which is the whole point: a footer that could be dragged out of place would be worse than
/// no footer.
/// </summary>
public static class PageFurniture
{
    /// <summary>How far in from the edge a header or footer sits.</summary>
    private const double Inset = 24;

    /// <summary>
    /// What a page's furniture can say besides plain words. Replaced in the same braces a
    /// shape's data uses, so there is one thing to learn rather than two.
    /// </summary>
    public static string Fill(string text, DiagramDocument document, DiagramPage page)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0)
            return text;

        var number = 0;

        for (var i = 0; i < document.Pages.Count; i++)
            if (ReferenceEquals(document.Pages[i], page))
                number = i + 1;

        var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["page"] = number.ToString(CultureInfo.InvariantCulture),
            ["pages"] = document.Pages.Count.ToString(CultureInfo.InvariantCulture),
            ["name"] = page.Name,
            ["date"] = DateTime.Now.ToString("d MMMM yyyy", CultureInfo.CurrentCulture),
            ["time"] = DateTime.Now.ToString("HH:mm", CultureInfo.CurrentCulture)
        };

        var sb = new System.Text.StringBuilder(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            var close = text[i] == '{' ? text.IndexOf('}', i + 1) : -1;

            if (close < 0 || !known.TryGetValue(text[(i + 1)..close], out var value))
            {
                sb.Append(text[i]);
                continue;
            }

            sb.Append(value);
            i = close;
        }

        return sb.ToString();
    }

    /// <summary>
    /// The watermark, drawn under everything so the drawing stays readable over it. Sized to
    /// cross the page rather than to a point size, since what it is for is to be unmissable.
    /// </summary>
    public static void DrawWatermark(DrawingContext context, DiagramDocument document, DiagramPage page)
    {
        var words = Fill(page.Watermark, document, page);

        if (string.IsNullOrWhiteSpace(words))
            return;

        var text = new FormattedText(
            words, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Typeface.Default, 100, new SolidColorBrush(page.WatermarkColor));

        // Turned about the middle of the page and scaled so it spans most of the diagonal.
        var centre = new Point(page.Width / 2, page.Height / 2);
        var across = Math.Sqrt(page.Width * page.Width + page.Height * page.Height) * 0.8;
        var scale = text.Width > 0 ? across / text.Width : 1;

        using var turned = context.PushTransform(
            Matrix.CreateTranslation(-centre.X, -centre.Y) *
            Matrix.CreateRotation(page.WatermarkAngle * Math.PI / 180) *
            Matrix.CreateScale(scale, scale) *
            Matrix.CreateTranslation(centre.X, centre.Y));

        context.DrawText(text, new Point(centre.X - text.Width / 2, centre.Y - text.Height / 2));
    }

    /// <summary>The header and the footer, drawn over the drawing as a page's furniture is.</summary>
    public static void DrawRunningHeads(DrawingContext context, DiagramDocument document, DiagramPage page)
    {
        Draw(context, Fill(page.Header, document, page), page, top: true);
        Draw(context, Fill(page.Footer, document, page), page, top: false);
    }

    private static void Draw(DrawingContext context, string words, DiagramPage page, bool top)
    {
        if (string.IsNullOrWhiteSpace(words))
            return;

        var text = new FormattedText(
            words, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Typeface.Default, page.HeadFootSize, new SolidColorBrush(page.HeadFootColor));

        context.DrawText(text, new Point(
            (page.Width - text.Width) / 2,
            top ? Inset : page.Height - Inset - text.Height));
    }

    /// <summary>The same furniture as SVG, for the export that is not a picture.</summary>
    public static string Svg(DiagramDocument document, DiagramPage page)
    {
        var sb = new System.Text.StringBuilder();

        string Hex(Color colour) => $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";
        string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        if (Fill(page.Watermark, document, page) is { Length: > 0 } mark &&
            !string.IsNullOrWhiteSpace(mark))
        {
            // Sized the way the screen sizes it, so the two agree.
            var measured = new FormattedText(
                mark, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                Typeface.Default, 100, Brushes.Black);

            var across = Math.Sqrt(page.Width * page.Width + page.Height * page.Height) * 0.8;
            var size = measured.Width > 0 ? 100 * across / measured.Width : 100;

            sb.AppendLine(
                $"  <text x=\"{Num(page.Width / 2)}\" y=\"{Num(page.Height / 2)}\" " +
                $"font-size=\"{Num(size)}\" fill=\"{Hex(page.WatermarkColor)}\" " +
                $"fill-opacity=\"{Num(page.WatermarkColor.A / 255.0)}\" " +
                $"text-anchor=\"middle\" dominant-baseline=\"central\" " +
                $"transform=\"rotate({Num(page.WatermarkAngle)} " +
                $"{Num(page.Width / 2)} {Num(page.Height / 2)})\">{Escape(mark)}</text>");
        }

        void Head(string text, bool top)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            sb.AppendLine(
                $"  <text x=\"{Num(page.Width / 2)}\" " +
                $"y=\"{Num(top ? Inset + page.HeadFootSize * 0.8 : page.Height - Inset)}\" " +
                $"font-size=\"{Num(page.HeadFootSize)}\" fill=\"{Hex(page.HeadFootColor)}\" " +
                $"text-anchor=\"middle\">{Escape(text)}</text>");
        }

        Head(Fill(page.Header, document, page), true);
        Head(Fill(page.Footer, document, page), false);

        return sb.ToString();
    }

    private static string Escape(string text) => text
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
