using Avalonia;
using Avalonia.Media;

namespace AVASvgMaker.Models;

/// <summary>
/// A shape carrying an outline of its own rather than one from the catalogue.
///
/// The outline is held in the same unit-square language a stencil uses, so this needs no
/// drawing or exporting of its own: it borrows both from <see cref="StencilPath"/>, and
/// resizing the shape scales the outline exactly as it would a stencil's.
/// </summary>
public class PathShape : DiagramShape
{
    /// <summary>A square, for an outline that will not parse - something to see and to click.</summary>
    public const string Fallback = "M 0,0 L 1,0 L 1,1 L 0,1 Z";

    public override ShapeKind Kind => ShapeKind.Path;

    /// <summary>The outline, every coordinate between 0 and 1.</summary>
    public string Outline { get; set; } = Fallback;

    public PathShape(Rect bounds) : base(bounds)
    {
    }

    public override Geometry CreateGeometry() => StencilPath.ToGeometry(Outline, Bounds);

    protected override void Draw(DrawingContext context, bool withText)
    {
        // Filled or not is the fill colour's business, not the outline's. An SVG path is
        // filled whether or not it closes - the fill shuts each subpath for itself - so a
        // shape that happens to end without a Z is not thereby a line.
        context.DrawGeometry(new SolidColorBrush(Fill), CreatePen(), CreateGeometry());

        if (withText)
            RenderText(context);
    }

    protected override string SvgBody() =>
        $"<path d=\"{StencilPath.ToSvgData(Outline, Bounds)}\" {SvgStyle()} />";
}
