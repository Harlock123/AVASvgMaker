using System;
using Avalonia;
using Avalonia.Media;

namespace AVASvgMaker.Models;

/// <summary>Which edge a container's title band runs along.</summary>
public enum HeaderEdge
{
    Left,
    Top
}

/// <summary>
/// A titled box that holds other shapes: a BPMN pool or lane, or a plain grouping box.
///
/// Only the title band and the border are clickable. The interior is left alone so it can be
/// swept with the marquee and dropped into, which is what a container is for - clicking the
/// middle of a pool to pick up the pool would make it almost impossible to use.
/// </summary>
public class ContainerShape : DiagramShape
{
    /// <summary>Title band thickness, in page units. Fixed rather than proportional, so it
    /// stays legible however large the container grows.</summary>
    public const double HeaderSize = 28;

    /// <summary>How near the border counts as clicking the container.</summary>
    private const double EdgeTolerance = 5;

    private readonly ShapeKind _kind;

    public override ShapeKind Kind => _kind;

    public override bool IsContainer => true;

    public HeaderEdge Header { get; }

    /// <summary>A lane is positioned by the pool that owns it, not by its own handles.</summary>
    public override bool CanRotate => false;

    public override bool IsBoxResizable => _kind != ShapeKind.Lane;

    /// <summary>
    /// A lane's share of its pool. Lanes divide the pool's body in proportion to these, so
    /// equal shares - which is what every lane starts with - divide it equally, and a lane
    /// given twice the share of its neighbour is drawn twice as tall. Held as a share rather
    /// than a height so that resizing the pool keeps the proportions the lanes were given.
    /// </summary>
    public double LaneShare { get; set; } = 1;

    public ContainerShape(ShapeKind kind, Rect bounds, HeaderEdge header) : base(bounds)
    {
        _kind = kind;
        Header = header;
        Fill = Colors.Transparent;
    }

    /// <summary>The title band.</summary>
    public Rect HeaderBounds => Header == HeaderEdge.Left
        ? new Rect(Bounds.X, Bounds.Y, Math.Min(HeaderSize, Bounds.Width), Bounds.Height)
        : new Rect(Bounds.X, Bounds.Y, Bounds.Width, Math.Min(HeaderSize, Bounds.Height));

    /// <summary>What is left for the contents once the title band is taken out.</summary>
    public Rect Body => Header == HeaderEdge.Left
        ? new Rect(Bounds.X + HeaderBounds.Width, Bounds.Y,
            Math.Max(0, Bounds.Width - HeaderBounds.Width), Bounds.Height)
        : new Rect(Bounds.X, Bounds.Y + HeaderBounds.Height,
            Bounds.Width, Math.Max(0, Bounds.Height - HeaderBounds.Height));

    public override Geometry CreateGeometry() => new RectangleGeometry(Bounds);

    /// <summary>The line the title band is ruled off by.</summary>
    public override string? UnitDetail
    {
        get
        {
            var band = HeaderBounds;

            var rule = Header == HeaderEdge.Left
                ? Bounds.Width <= 0 ? 0 : band.Width / Bounds.Width
                : Bounds.Height <= 0 ? 0 : band.Height / Bounds.Height;

            return Header == HeaderEdge.Left
                ? $"M {Num(rule)},0 L {Num(rule)},1"
                : $"M 0,{Num(rule)} L 1,{Num(rule)}";
        }
    }

    protected override void Draw(DrawingContext context, bool withText)
    {
        var pen = CreatePen();

        context.DrawRectangle(FillBrush(), pen, Bounds);
        context.DrawRectangle(new SolidColorBrush(HeaderFill()), pen, HeaderBounds);

        if (withText)
            RenderTitle(context);
    }

    /// <summary>A wash of the outline colour, so the band reads as part of the container.</summary>
    private Color HeaderFill() => Color.FromArgb(0x22, Stroke.R, Stroke.G, Stroke.B);

    /// <summary>
    /// The title sits in the band, turned on its side when the band runs down the left edge -
    /// which is how a pool is drawn, and the only way the name fits.
    /// </summary>
    private void RenderTitle(DrawingContext context)
    {
        if (string.IsNullOrWhiteSpace(Text))
            return;

        var band = HeaderBounds;
        var formatted = Format(Text);

        if (Header == HeaderEdge.Top)
        {
            context.DrawText(formatted, new Point(
                band.Center.X - formatted.Width / 2,
                band.Center.Y - formatted.Height / 2));

            return;
        }

        using (context.PushTransform(
                   Matrix.CreateTranslation(-band.Center.X, -band.Center.Y) *
                   Matrix.CreateRotation(-Math.PI / 2) *
                   Matrix.CreateTranslation(band.Center.X, band.Center.Y)))
        {
            context.DrawText(formatted, new Point(
                band.Center.X - formatted.Width / 2,
                band.Center.Y - formatted.Height / 2));
        }
    }

    /// <summary>The title band and the border, but not the space inside.</summary>
    protected override bool HitTestUpright(Point point) => HitTestUpright(point, 0);

    protected override bool HitTestUpright(Point point, double slack)
    {
        if (HeaderBounds.Contains(point))
            return true;

        var tolerance = Math.Max(slack, EdgeTolerance);

        return Bounds.Inflate(tolerance).Contains(point) &&
               !Bounds.Deflate(tolerance).Contains(point);
    }

    /// <summary>Whether a shape's middle falls inside this container's body.</summary>
    public bool Holds(Rect bounds) => Body.Contains(bounds.Center);

    protected override string SvgBody()
    {
        var body = $"<rect x=\"{Num(Bounds.X)}\" y=\"{Num(Bounds.Y)}\" " +
                   $"width=\"{Num(Bounds.Width)}\" height=\"{Num(Bounds.Height)}\" {SvgStyle()} />";

        var band = HeaderBounds;
        var header = $"<rect x=\"{Num(band.X)}\" y=\"{Num(band.Y)}\" " +
                     $"width=\"{Num(band.Width)}\" height=\"{Num(band.Height)}\" " +
                     $"fill=\"{ToHex(Stroke)}\" fill-opacity=\"0.13\" " +
                     $"stroke=\"{SvgPaint(Stroke)}\" stroke-width=\"{Num(StrokeThickness)}\" />";

        return $"<g>{body}{header}</g>";
    }

    /// <summary>The title is drawn in the band, so the base class's centred label is not used.</summary>
    protected override Rect TextArea => Framed(HeaderBounds);
}
