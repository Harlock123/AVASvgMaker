namespace AVASvgMaker.Models;

/// <summary>The groups the toolbox splits its stencils into.</summary>
public enum StencilCategory
{
    Basic,
    Arrow,
    Callout,
    Flowchart,
    Bpmn,
    Uml,
    Network,
    Electrical,
    Logic,
    Electronic
}

/// <summary>
/// Formatting a stencil carries as part of what it *is*, rather than a choice the user makes.
/// A BPMN end event is a thick circle and a group is a dashed outline: get those wrong and the
/// shape means something else. Anything left null keeps whatever the properties panel is set to.
/// </summary>
public record StencilDefaults(
    double? StrokeThickness = null,
    StrokeStyle? StrokeStyle = null,
    bool Hollow = false,
    string? Label = null,
    TextVerticalAlign? TextVerticalAlign = null);

/// <summary>
/// One entry in the stencil catalogue: what it is called, where it lives in the toolbox, and
/// the outline that draws it.
/// </summary>
/// <param name="Kind">Identity, and what gets written to the file.</param>
/// <param name="Category">Which toolbox group it appears under.</param>
/// <param name="Name">The label in the toolbox.</param>
/// <param name="Outline">Filled and stroked. Null for shapes that draw themselves in code.</param>
/// <param name="Detail">Stroked over the outline, never filled - dividing lines and the like.</param>
/// <param name="Keywords">Extra words the toolbox search should match.</param>
/// <param name="Defaults">Formatting the shape needs in order to mean what it means.</param>
public record Stencil(
    ShapeKind Kind,
    StencilCategory Category,
    string Name,
    string? Outline = null,
    string? Detail = null,
    string Keywords = "",
    StencilDefaults? Defaults = null)
{
    /// <summary>Applies the formatting this stencil is defined to have, if any.</summary>
    public void ApplyDefaults(DiagramShape shape)
    {
        if (Defaults is not { } defaults)
            return;

        if (defaults.StrokeThickness is { } thickness)
            shape.StrokeThickness = thickness;

        if (defaults.StrokeStyle is { } style)
            shape.StrokeStyle = style;

        if (defaults.Hollow)
            shape.Fill = Avalonia.Media.Colors.Transparent;

        // A supply rail is its label - the symbol alone does not say 5 from 3.3 - so it
        // arrives with one, as the shape's own text and renameable like any other. Only when
        // there is nothing there already: a shape being read back from a file has its own.
        if (defaults.Label is { } label && string.IsNullOrEmpty(shape.Text))
            shape.Text = label;

        if (defaults.TextVerticalAlign is { } align)
            shape.TextVerticalAlign = align;
    }
}
