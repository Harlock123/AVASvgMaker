using Avalonia;
using Avalonia.Media;

namespace AVASvgMaker.Models;

/// <summary>
/// A borderless block of text. An empty one draws a faint dashed guide so it can still
/// be found on the page; the guide is never exported.
/// </summary>
public class TextBoxShape : DiagramShape
{
    private static readonly IPen GuidePen = new Pen(
        new SolidColorBrush(Color.FromRgb(0xB0, 0xB8, 0xC4)), 1, new DashStyle([3, 3], 0));

    public override ShapeKind Kind => ShapeKind.TextBox;

    public TextBoxShape(Rect bounds) : base(bounds)
    {
        Fill = Colors.Transparent;
        Stroke = Colors.Transparent;
    }

    public override Geometry CreateGeometry() => new RectangleGeometry(Bounds);

    protected override void Draw(DrawingContext context, bool withText)
    {
        if (Fill != Colors.Transparent || Stroke != Colors.Transparent)
            base.Draw(context, withText);
        else if (string.IsNullOrWhiteSpace(Text))
            context.DrawRectangle(null, GuidePen, Bounds);

        if (withText && (Fill == Colors.Transparent && Stroke == Colors.Transparent))
            RenderText(context);
    }

    /// <summary>A text box is always clickable, including while it is empty.</summary>
    protected override bool HitTestUpright(Point point) => Bounds.Contains(point);

    protected override string SvgBody() =>
        Fill == Colors.Transparent && Stroke == Colors.Transparent
            ? string.Empty
            : $"<rect x=\"{Num(Bounds.X)}\" y=\"{Num(Bounds.Y)}\" " +
              $"width=\"{Num(Bounds.Width)}\" height=\"{Num(Bounds.Height)}\" {SvgStyle()} />";
}
