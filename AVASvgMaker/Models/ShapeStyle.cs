using Avalonia.Media;

namespace AVASvgMaker.Models;

/// <summary>
/// The formatting a shape carries. Held separately from the shapes so the editor can keep a
/// set of defaults for whatever gets drawn next.
/// </summary>
public record ShapeStyle(
    Color Fill,
    Color Stroke,
    Color TextColor,
    double StrokeThickness,
    StrokeStyle StrokeStyle,
    double FontSize)
{
    public static readonly ShapeStyle Default = new(
        DiagramShape.DefaultFill,
        DiagramShape.DefaultStroke,
        DiagramShape.DefaultTextColor,
        2,
        StrokeStyle.Solid,
        13);

    public static ShapeStyle From(DiagramShape shape) => new(
        shape.Fill,
        shape.Stroke,
        shape.TextColor,
        shape.StrokeThickness,
        shape.StrokeStyle,
        shape.FontSize);

    public void ApplyTo(DiagramShape shape)
    {
        shape.Fill = Fill;
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
    }
}
