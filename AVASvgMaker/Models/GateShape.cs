using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Media;

namespace AVASvgMaker.Models;

/// <summary>
/// A logic gate: the body from the catalogue, a lead for every input, one for the output, and
/// a connection point on the end of each - which is the reason this is a class rather than an
/// outline, the same reason <see cref="ChipShape"/> is.
///
/// A gate's inputs are not interchangeable to look at: a wire has to arrive at the upper input
/// and stay there while the gate is dragged, or a drawing of a half adder is a drawing of
/// nothing. So the points are the lead tips, the inputs face out of the back and the output out
/// of the front, and an enable faces out of the top.
/// </summary>
public class GateShape : DiagramShape
{
    private readonly Gate _gate;

    private Rect _pinBounds;
    private double _pinRotation;
    private Point[]? _pins;

    public override ShapeKind Kind => _gate.Kind;

    public Gate Gate => _gate;

    public GateShape(Gate gate, Rect bounds) : base(bounds)
    {
        _gate = gate;
    }

    /// <summary>How far the leads stick out of the body, front and back.</summary>
    private double Lead => Math.Clamp(Bounds.Width * 0.16, 5, 18);

    /// <summary>The inverting circle on the output, or nothing for a gate without one.</summary>
    private double Radius => _gate.Bubble ? Math.Clamp(Bounds.Height * 0.085, 2, 5) : 0;

    /// <summary>The room the XOR's second arc needs behind the body.</summary>
    private double Gap => _gate.Extra ? Math.Clamp(Bounds.Width * 0.11, 4, 11) : 0;

    /// <summary>The symbol itself, inside the leads, the bubble and the second arc.</summary>
    private Rect Body => new(
        Bounds.X + Lead + Gap,
        Bounds.Y,
        Math.Max(1, Bounds.Width - Lead * 2 - Gap - Radius * 2),
        Math.Max(1, Bounds.Height));

    /// <summary>Where the second arc is drawn: the body's own back curve, moved behind it.</summary>
    private Rect Behind => Body.Translate(new Vector(-Gap, 0));

    private Point Bubble => new(Body.Right + Radius, Bounds.Center.Y);

    /// <summary>How far down the back the given input sits, as a fraction of the height.</summary>
    private double Down(int input) => (input + 1) / (double)(_gate.Inputs + 1);

    /// <summary>
    /// How far an input lead reaches into the body. A flat back is met at its edge; the OR
    /// family's back is a curve, and a lead that stopped at the edge of the box would hang in
    /// the air short of it - so it runs on to the curve, worked out at the height it enters.
    /// </summary>
    private double Reach(int input)
    {
        if (!_gate.Curved)
            return 0;

        // The back is the quadratic of BackArc: with both ends at x = 0 and the control point
        // at 0.38, the curve at height t is 2t(1-t) * 0.38 across.
        var t = Down(input);

        return 2 * t * (1 - t) * 0.38 * Body.Width;
    }

    private Point Pin(int index)
    {
        if (_gate.Enable && index == _gate.Count - 1)
            return new Point(Body.Center.X, Bounds.Y);

        return index == _gate.Output
            ? new Point(Bounds.Right, Bounds.Center.Y)
            : new Point(Bounds.X, Bounds.Y + Bounds.Height * Down(index));
    }

    /// <summary>The inputs down the back, then the output, then the enable if there is one.</summary>
    public override IReadOnlyList<Point> ConnectionPoints
    {
        get
        {
            if (_pins is not null && _pinBounds == Bounds &&
                Math.Abs(_pinRotation - Rotation) < 0.0001)
                return _pins;

            _pinBounds = Bounds;
            _pinRotation = Rotation;
            _pins = Enumerable.Range(0, _gate.Count).Select(Pin).ToArray();

            if (IsRotated)
            {
                var turn = RotationMatrix;

                for (var i = 0; i < _pins.Length; i++)
                    _pins[i] = _pins[i].Transform(turn);
            }

            return _pins;
        }
    }

    /// <summary>Inputs leave backwards, the output forwards, an enable upwards.</summary>
    protected override Vector OutwardDirection(int index)
    {
        if (index < 0 || index >= _gate.Count)
            return default;

        if (_gate.Enable && index == _gate.Count - 1)
            return new Vector(0, -1);

        return index == _gate.Output ? new Vector(1, 0) : new Vector(-1, 0);
    }

    public override Geometry CreateGeometry() => StencilPath.ToGeometry(_gate.Body, Body);

    public override string UnitOutline => _gate.Body;

    /// <summary>
    /// The back half of the body, where a reference like U1A can be written without landing on
    /// the point of an OR gate or outside the round front of an AND.
    /// </summary>
    protected override Rect TextArea
    {
        get
        {
            var body = Body;

            return new Rect(
                body.X + body.Width * 0.12, body.Y + body.Height * 0.25,
                Math.Max(8, body.Width * 0.6), Math.Max(8, body.Height * 0.5));
        }
    }

    /// <summary>What a gate wants to be: wider than tall, with a row for every input.</summary>
    public static Size PreferredSize(Gate gate)
    {
        var height = Math.Max(48, gate.Inputs * 22);

        return new Size(Math.Ceiling(height * 1.5), height);
    }

    protected override void Draw(DrawingContext context, bool withText)
    {
        var pen = CreatePen();
        var body = Body;

        foreach (var (from, to) in Leads())
            context.DrawLine(pen, from, to);

        context.DrawGeometry(FillBrush(), pen, StencilPath.ToGeometry(_gate.Body, body));

        if (_gate.Extra)
            context.DrawGeometry(null, pen, StencilPath.ToGeometry(GateCatalogue.BackArc, Behind));

        if (_gate.Bubble)
            context.DrawEllipse(FillBrush(), pen, Bubble, Radius, Radius);

        if (withText)
            RenderText(context);
    }

    /// <summary>
    /// Every lead, as drawn: from the connection point on the edge of the box to the body. The
    /// leads are drawn first and the body over them, so a lead that runs under a curved back
    /// does not show through it.
    /// </summary>
    private IEnumerable<(Point From, Point To)> Leads()
    {
        var body = Body;

        for (var input = 0; input < _gate.Inputs; input++)
        {
            var at = Pin(input);

            yield return (at, new Point(body.X + Reach(input), at.Y));
        }

        yield return (_gate.Bubble
            ? new Point(Bubble.X + Radius, Bubble.Y)
            : new Point(body.Right, Bounds.Center.Y), Pin(_gate.Output));

        if (!_gate.Enable)
            yield break;

        // Onto the slope of the triangle, which at the middle of its width is a quarter of the
        // way down it.
        yield return (Pin(_gate.Count - 1), new Point(body.Center.X, body.Y + body.Height * 0.25));
    }

    protected override string SvgBody()
    {
        var body = Body;
        var sb = new StringBuilder();

        var stroke = $"stroke=\"{SvgPaint(Stroke)}\" stroke-width=\"{Num(StrokeThickness)}\"{SvgDash()}";

        sb.Append("<g>");

        foreach (var (from, to) in Leads())
            sb.Append($"<line x1=\"{Num(from.X)}\" y1=\"{Num(from.Y)}\" " +
                      $"x2=\"{Num(to.X)}\" y2=\"{Num(to.Y)}\" {stroke} />");

        sb.Append($"<path d=\"{StencilPath.ToSvgData(_gate.Body, body)}\" {SvgStyle()} />");

        if (_gate.Extra)
            sb.Append($"<path d=\"{StencilPath.ToSvgData(GateCatalogue.BackArc, Behind)}\" " +
                      $"fill=\"none\" {stroke} />");

        if (_gate.Bubble)
            sb.Append($"<circle cx=\"{Num(Bubble.X)}\" cy=\"{Num(Bubble.Y)}\" " +
                      $"r=\"{Num(Radius)}\" {SvgStyle()} />");

        sb.Append("</g>");
        return sb.ToString();
    }
}
