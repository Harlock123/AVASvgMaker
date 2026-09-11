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

    /// <summary>
    /// Markings drawn over the outline and never filled, in the same unit square. A path
    /// imported from a drawing that ruled a line across its own face keeps that line here
    /// rather than letting the fill swallow it.
    /// </summary>
    public string? Detail { get; set; }

    public PathShape(Rect bounds) : base(bounds)
    {
    }

    public override Geometry CreateGeometry() => StencilPath.ToGeometry(Outline, Bounds);

    public override string UnitOutline => Outline;

    public override string? UnitDetail => Detail;

    protected override void Draw(DrawingContext context, bool withText)
    {
        // Filled or not is the fill colour's business, not the outline's. An SVG path is
        // filled whether or not it closes - the fill shuts each subpath for itself - so a
        // shape that happens to end without a Z is not thereby a line.
        context.DrawGeometry(FillBrush(), CreatePen(), CreateGeometry());

        if (Detail is { } detail)
            context.DrawGeometry(null, CreatePen(), StencilPath.ToGeometry(detail, Bounds));

        if (withText)
            RenderText(context);
    }

    protected override string SvgBody()
    {
        var body = $"<path d=\"{StencilPath.ToSvgData(Outline, Bounds)}\" {SvgStyle()} />";

        if (Detail is not { } detail)
            return body;

        return $"<g>{body}<path d=\"{StencilPath.ToSvgData(detail, Bounds)}\" fill=\"none\" " +
               $"stroke=\"{SvgPaint(Stroke)}\" stroke-width=\"{Num(StrokeThickness)}\"{SvgDash()} /></g>";
    }
}
