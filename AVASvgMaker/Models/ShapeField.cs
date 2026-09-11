namespace AVASvgMaker.Models;

/// <summary>
/// One named piece of data carried by a shape - an owner, a part number, a cost - as against
/// the words drawn on it and the colours it is drawn in.
///
/// This is what a diagram tool has and a drawing tool does not: a box that is a server rather
/// than a box that says "server". The data is the shape's own, travels with it, is saved with
/// the drawing, and can be put into the shape's label by naming it there.
/// </summary>
public sealed class ShapeField
{
    /// <summary>What the field is called in a label, and how it is found. Unique to its shape.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>What to call it on screen. The name itself, when there is nothing better.</summary>
    public string Label { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    /// <summary>The label to show, which is the name unless something else was given.</summary>
    public string Caption => string.IsNullOrWhiteSpace(Label) ? Name : Label;

    public ShapeField Copy() => new() { Name = Name, Label = Label, Value = Value };
}
