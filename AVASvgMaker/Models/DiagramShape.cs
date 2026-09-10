using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Media;

namespace AVASvgMaker.Models;

/// <summary>
/// Base for every shape that can live on the page. A shape owns its bounds and
/// knows how to draw itself both to a <see cref="DrawingContext"/> and to SVG.
/// </summary>
public abstract class DiagramShape
{
    public static readonly Color DefaultFill = Color.FromRgb(0xDC, 0xE9, 0xFB);
    public static readonly Color DefaultStroke = Color.FromRgb(0x2D, 0x6C, 0xDF);
    public static readonly Color DefaultTextColor = Color.FromRgb(0x1A, 0x1A, 0x1A);

    public const double MinSize = 16;

    protected const double TextPadding = 6;

    private Rect _bounds;

    /// <summary>Position and size on the page. Overridden where the shape is not defined by a box.</summary>
    public virtual Rect Bounds
    {
        get => _bounds;
        set => _bounds = value;
    }

    public string Text { get; set; } = string.Empty;
    public Color Fill { get; set; } = DefaultFill;
    public Color Stroke { get; set; } = DefaultStroke;
    public Color TextColor { get; set; } = DefaultTextColor;
    public double StrokeThickness { get; set; } = 2;
    public StrokeStyle StrokeStyle { get; set; } = StrokeStyle.Solid;
    public double FontSize { get; set; } = 13;

    /// <summary>The font a label is drawn in. Empty means whatever the application draws in.</summary>
    public string FontName { get; set; } = string.Empty;

    public bool Bold { get; set; }

    public bool Italic { get; set; }

    public TextAlign TextAlign { get; set; } = TextAlign.Center;

    public abstract ShapeKind Kind { get; }

    /// <summary>
    /// The container this shape sits in, or null. Containment is a relation held on the child
    /// rather than a list on the parent: a shape has exactly one container, and the document
    /// stays one flat list in z-order.
    /// </summary>
    public DiagramShape? Container { get; set; }

    /// <summary>True for shapes that can hold others.</summary>
    public virtual bool IsContainer => false;

    /// <summary>False for shapes sized by their end points rather than by a box.</summary>
    public virtual bool IsBoxResizable => true;

    protected DiagramShape(Rect bounds)
    {
        _bounds = bounds;
    }

    /// <summary>
    /// Moves the shape by a delta. Prefer this over assigning <see cref="Bounds"/> when
    /// moving something: a shape whose bounds are derived cannot be positioned absolutely.
    /// </summary>
    public virtual void Translate(Vector delta)
    {
        var bounds = Bounds;
        Bounds = new Rect(bounds.X + delta.X, bounds.Y + delta.Y, bounds.Width, bounds.Height);
    }

    #region Connection points

    /// <summary>The outward direction of each connection point, in the same order.</summary>
    public static readonly Vector[] ConnectionDirections =
    [
        new(0, -1),
        new(1, 0),
        new(0, 1),
        new(-1, 0)
    ];

    /// <summary>
    /// Where connectors attach, in page coordinates: north, east, south then west by default.
    /// Shapes with a more useful set of their own can override this, keeping the order so the
    /// directions above still line up.
    /// </summary>
    public virtual IReadOnlyList<Point> ConnectionPoints
    {
        get
        {
            var bounds = Bounds;

            return
            [
                new Point(bounds.Center.X, bounds.Top),
                new Point(bounds.Right, bounds.Center.Y),
                new Point(bounds.Center.X, bounds.Bottom),
                new Point(bounds.Left, bounds.Center.Y)
            ];
        }
    }

    /// <summary>The direction a connector should leave the given connection point.</summary>
    public Vector ConnectionDirection(int index) =>
        index >= 0 && index < ConnectionDirections.Length ? ConnectionDirections[index] : default;

    #endregion

    /// <summary>Geometry for the shape, in page coordinates.</summary>
    public abstract Geometry CreateGeometry();

    public void Render(DrawingContext context) => Render(context, true);

    /// <summary>Draws the shape; <paramref name="withText"/> is false while its label is being edited.</summary>
    public virtual void Render(DrawingContext context, bool withText)
    {
        var brush = new SolidColorBrush(Fill);
        context.DrawGeometry(brush, CreatePen(), CreateGeometry());

        if (withText)
            RenderText(context);
    }

    /// <summary>
    /// The outline pen. Dash lengths are multiples of the stroke width, so a dashed line keeps
    /// its proportions as the weight changes - and the SVG side multiplies them back out.
    /// </summary>
    protected IPen CreatePen()
    {
        var brush = new SolidColorBrush(Stroke);

        return StrokeStyle switch
        {
            StrokeStyle.Dashed => new Pen(brush, StrokeThickness, new DashStyle([4, 2], 0)),
            StrokeStyle.Dotted => new Pen(brush, StrokeThickness, new DashStyle([1, 2], 0))
            {
                LineCap = PenLineCap.Round
            },
            _ => new Pen(brush, StrokeThickness)
        };
    }

    public virtual bool HitTest(Point point) => CreateGeometry().FillContains(point);

    /// <summary>
    /// Hit test with slack, in page units, for targets too thin to click accurately.
    /// Filled shapes ignore it; a line needs it, and needs more of it when zoomed out.
    /// </summary>
    public virtual bool HitTest(Point point, double slack) => HitTest(point);

    #region Text

    protected double LineHeight => FontSize * 1.3;

    /// <summary>
    /// The face a label is drawn with. A font that is not installed falls back to the default
    /// rather than failing, which is also what the SVG font-family list does at the far end.
    /// </summary>
    protected Typeface Face => new(
        string.IsNullOrEmpty(FontName) ? FontFamily.Default : new FontFamily(FontName),
        Italic ? FontStyle.Italic : FontStyle.Normal,
        Bold ? FontWeight.Bold : FontWeight.Normal);

    protected FormattedText Format(string line) => new(
        line,
        CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight,
        Face,
        FontSize,
        new SolidColorBrush(TextColor));

    /// <summary>
    /// Greedy word wrap. The same line breaks are used for drawing and for export,
    /// so the SVG matches what is on screen.
    /// </summary>
    protected List<string> WrapText(double maxWidth)
    {
        var lines = new List<string>();

        foreach (var paragraph in Text.Replace("\r\n", "\n").Split('\n'))
        {
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            var current = words[0];
            for (var i = 1; i < words.Length; i++)
            {
                var candidate = current + " " + words[i];
                if (Format(candidate).Width <= maxWidth)
                {
                    current = candidate;
                }
                else
                {
                    lines.Add(current);
                    current = words[i];
                }
            }

            lines.Add(current);
        }

        return lines;
    }

    protected virtual Rect TextArea => Bounds;

    /// <summary>Where a line of the given width starts, for the alignment in force.</summary>
    private double LineLeft(Rect area, double width) => TextAlign switch
    {
        TextAlign.Left => area.X + TextPadding,
        TextAlign.Right => area.Right - TextPadding - width,
        _ => area.Center.X - width / 2
    };

    protected void RenderText(DrawingContext context)
    {
        if (string.IsNullOrWhiteSpace(Text))
            return;

        var area = TextArea;
        var lines = WrapText(Math.Max(8, area.Width - TextPadding * 2));
        var top = area.Center.Y - lines.Count * LineHeight / 2;

        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Length == 0)
                continue;

            var formatted = Format(lines[i]);
            context.DrawText(formatted, new Point(
                LineLeft(area, formatted.Width),
                top + i * LineHeight + (LineHeight - formatted.Height) / 2));
        }
    }

    #endregion

    #region SVG

    /// <summary>The SVG element(s) for the shape body, without the label. Empty for shapes that draw nothing.</summary>
    protected abstract string SvgBody();

    protected string SvgStyle() =>
        $"fill=\"{SvgPaint(Fill)}\" stroke=\"{SvgPaint(Stroke)}\" " +
        $"stroke-width=\"{Num(StrokeThickness)}\"{SvgDash()}";

    /// <summary>Dash lengths in user units, matching what <see cref="CreatePen"/> draws.</summary>
    protected string SvgDash() => StrokeStyle switch
    {
        StrokeStyle.Dashed =>
            $" stroke-dasharray=\"{Num(4 * StrokeThickness)},{Num(2 * StrokeThickness)}\"",
        StrokeStyle.Dotted =>
            $" stroke-dasharray=\"{Num(StrokeThickness)},{Num(2 * StrokeThickness)}\" stroke-linecap=\"round\"",
        _ => string.Empty
    };

    /// <summary>A fully transparent colour is "none" in SVG, not a black or white fill.</summary>
    protected static string SvgPaint(Color colour) => colour.A == 0 ? "none" : ToHex(colour);

    public string ToSvg()
    {
        var sb = new StringBuilder();

        var body = SvgBody();
        if (!string.IsNullOrEmpty(body))
            sb.AppendLine("  " + body);

        if (!string.IsNullOrWhiteSpace(Text))
            sb.Append(SvgText());

        return sb.ToString();
    }

    private string SvgText()
    {
        var area = TextArea;
        var lines = WrapText(Math.Max(8, area.Width - TextPadding * 2));
        var top = area.Center.Y - lines.Count * LineHeight / 2;

        // The anchor and the x it is measured from have to agree, or the text lands
        // somewhere the editor never drew it.
        var (anchor, x) = TextAlign switch
        {
            TextAlign.Left => ("start", area.X + TextPadding),
            TextAlign.Right => ("end", area.Right - TextPadding),
            _ => ("middle", area.Center.X)
        };

        var sb = new StringBuilder();
        sb.AppendLine(
            $"  <text font-family=\"{SvgFontFamily()}\" font-size=\"{Num(FontSize)}\" " +
            $"fill=\"{ToHex(TextColor)}\"" +
            (Bold ? " font-weight=\"bold\"" : string.Empty) +
            (Italic ? " font-style=\"italic\"" : string.Empty) +
            $" text-anchor=\"{anchor}\" dominant-baseline=\"central\">");

        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Length == 0)
                continue;

            var y = top + i * LineHeight + LineHeight / 2;
            sb.AppendLine($"    <tspan x=\"{Num(x)}\" y=\"{Num(y)}\">{Escape(lines[i])}</tspan>");
        }

        sb.AppendLine("  </text>");
        return sb.ToString();
    }

    /// <summary>
    /// The named font with a generic behind it, so the drawing still reads on a machine that
    /// does not have the font - which, for a file being handed to someone else, is the
    /// ordinary case.
    /// </summary>
    private string SvgFontFamily() => string.IsNullOrEmpty(FontName)
        ? "sans-serif"
        : $"{Escape(FontName)}, sans-serif";

    protected static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    protected static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    protected static string Escape(string text) => text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;");

    #endregion
}
