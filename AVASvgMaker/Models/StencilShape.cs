using Avalonia;
using Avalonia.Media;

namespace AVASvgMaker.Models;

/// <summary>
/// A shape drawn from a <see cref="Stencil"/>'s outline. Everything in the catalogue that is
/// not one of the handful of shapes with special drawing needs is one of these.
/// </summary>
public class StencilShape : DiagramShape
{
    private readonly Stencil _stencil;

    public override ShapeKind Kind => _stencil.Kind;

    public StencilShape(Stencil stencil, Rect bounds) : base(bounds)
    {
        _stencil = stencil;
    }

    /// <summary>
    /// The filled body, or the whole box for a stencil that has none - a bracket is all
    /// stroke, and still needs something to click on.
    /// </summary>
    public override Geometry CreateGeometry() =>
        StencilPath.ToGeometry(_stencil.Outline ?? "M 0,0 L 1,0 L 1,1 L 0,1 Z", Bounds);

    protected override void Draw(DrawingContext context, bool withText)
    {
        var pen = CreatePen();

        if (_stencil.Outline is not null)
            context.DrawGeometry(new SolidColorBrush(Fill), pen, CreateGeometry());

        if (_stencil.Detail is { } detail)
            context.DrawGeometry(null, pen, StencilPath.ToGeometry(detail, Bounds));

        if (withText)
            RenderText(context);
    }

    protected override string SvgBody()
    {
        var outline = _stencil.Outline is { } body
            ? $"<path d=\"{StencilPath.ToSvgData(body, Bounds)}\" {SvgStyle()} />"
            : string.Empty;

        if (_stencil.Detail is not { } detail)
            return outline;

        var strokeOnly = $"<path d=\"{StencilPath.ToSvgData(detail, Bounds)}\" fill=\"none\" " +
                         $"stroke=\"{SvgPaint(Stroke)}\" stroke-width=\"{Num(StrokeThickness)}\"{SvgDash()} />";

        return outline.Length == 0 ? strokeOnly : $"<g>{outline}{strokeOnly}</g>";
    }
}
