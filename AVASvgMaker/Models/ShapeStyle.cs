using Avalonia.Media;

namespace AVASvgMaker.Models;

/// <summary>
/// The formatting a shape carries. Held separately from the shapes so the editor can keep a
/// set of defaults for whatever gets drawn next.
/// </summary>
public record ShapeStyle(
    Color Fill,
    Color? FillTo,
    double FillAngle,
    Color Stroke,
    Color TextColor,
    double StrokeThickness,
    StrokeStyle StrokeStyle,
    double FontSize,
    string FontName,
    bool Bold,
    bool Italic,
    TextAlign TextAlign,
    TextVerticalAlign TextVerticalAlign)
{
    public static readonly ShapeStyle Default = new(
        DiagramShape.DefaultFill,
        null,
        90,
        DiagramShape.DefaultStroke,
        DiagramShape.DefaultTextColor,
        2,
        StrokeStyle.Solid,
        13,
        string.Empty,
        false,
        false,
        TextAlign.Center,
        TextVerticalAlign.Middle);

    public static ShapeStyle From(DiagramShape shape) => new(
        shape.Fill,
        shape.FillTo,
        shape.FillAngle,
        shape.Stroke,
        shape.TextColor,
        shape.StrokeThickness,
        shape.StrokeStyle,
        shape.FontSize,
        shape.FontName,
        shape.Bold,
        shape.Italic,
        shape.TextAlign,
        shape.TextVerticalAlign);

    public void ApplyTo(DiagramShape shape)
    {
        shape.Fill = Fill;
        shape.FillTo = FillTo;
        shape.FillAngle = FillAngle;
        shape.Stroke = Stroke;
        shape.StrokeThickness = StrokeThickness;
        shape.StrokeStyle = StrokeStyle;

        ApplyTextTo(shape);
    }

    /// <summary>Text settings only - a new text box keeps its transparent fill and outline.</summary>
    public void ApplyTextTo(DiagramShape shape)
    {
        shape.TextColor = TextColor;
        shape.FontSize = FontSize;
        shape.FontName = FontName;
        shape.Bold = Bold;
        shape.Italic = Italic;
        shape.TextAlign = TextAlign;
        shape.TextVerticalAlign = TextVerticalAlign;
    }
}
