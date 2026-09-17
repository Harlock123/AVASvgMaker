using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Media;

namespace AVASvgMaker.Models;

/// <summary>
/// A chip in a dual in-line package, drawn with a leg for every pin - and a connection point
/// on every leg, which is the reason it is a class of its own rather than an outline.
///
/// Everything else in the catalogue offers the four points of its box. A 555 offers eight, a
/// 7400 fourteen, each one named, each one facing out of the side of the package it is on. So
/// a wire can be drawn to TRIG and stay on TRIG while the chip is moved about, which is the
/// whole of what makes a chip worth having as a shape rather than as a labelled rectangle.
/// </summary>
public class ChipShape : DiagramShape
{
    private readonly Chip _chip;

    private Rect _pinBounds;
    private double _pinRotation;
    private Point[]? _pins;

    public override ShapeKind Kind => _chip.Kind;

    public Chip Chip => _chip;

    public ChipShape(Chip chip, Rect bounds) : base(bounds)
    {
        _chip = chip;

        // The part number is the shape's own text, so it can be renamed, restyled and
        // exported like any other label rather than being painted on.
        Text = chip.Name;
    }

    /// <summary>How far the legs stick out of the package, each side.</summary>
    private double Lead => Math.Clamp(Bounds.Width * 0.14, 4, 20);

    /// <summary>The package itself, inside the legs.</summary>
    private Rect Package => new(
        Bounds.X + Lead, Bounds.Y, Math.Max(1, Bounds.Width - Lead * 2), Math.Max(1, Bounds.Height));

    /// <summary>Which side a pin is on, and how far down that side it sits.</summary>
    private (bool Left, int Row) Seat(int pin) =>
        pin < _chip.PerSide ? (true, pin) : (false, _chip.Count - 1 - pin);

    private Point Leg(int pin)
    {
        var (left, row) = Seat(pin);
        var package = Package;
        var step = package.Height / Math.Max(1, _chip.PerSide);

        return new Point(left ? Bounds.X : Bounds.Right, package.Y + (row + 0.5) * step);
    }

    /// <summary>
    /// One point per pin, at the outer tip of its leg, numbered as a DIP is: down the left
    /// side from the top, then back up the right.
    /// </summary>
    public override IReadOnlyList<Point> ConnectionPoints
    {
        get
        {
            // Cached on the same terms the base class caches its four, and for the same
            // reason: the router asks for these constantly.
            if (_pins is not null && _pinBounds == Bounds &&
                Math.Abs(_pinRotation - Rotation) < 0.0001)
                return _pins;

            _pinBounds = Bounds;
            _pinRotation = Rotation;
            _pins = Enumerable.Range(0, _chip.Count).Select(Leg).ToArray();

            if (IsRotated)
            {
                var turn = RotationMatrix;

                for (var i = 0; i < _pins.Length; i++)
                    _pins[i] = _pins[i].Transform(turn);
            }

            return _pins;
        }
    }

    /// <summary>A leg faces out of the side of the package it is on, and no other way.</summary>
    protected override Vector OutwardDirection(int index) =>
        index < 0 || index >= _chip.Count
            ? default
            : new Vector(Seat(index).Left ? -1 : 1, 0);

    /// <summary>The package, not the legs: a wire should meet a pin, not the empty air by it.</summary>
    public override Geometry CreateGeometry() => new RectangleGeometry(Package);

    /// <summary>
    /// The middle of the package. The pin names run down both edges, and a part number
    /// centred over the whole of it would sit on top of them.
    /// </summary>
    protected override Rect TextArea
    {
        get
        {
            var package = Package;

            var gutter = ShowNames
                ? Math.Min(package.Width * 0.42, Names(PinSize(package)) + 5)
                : 0;

            return new Rect(
                package.X + gutter, package.Y,
                Math.Max(8, package.Width - gutter * 2), package.Height);
        }
    }

    /// <summary>A font small enough that a pin name fits in the row it belongs to.</summary>
    private double PinSize(Rect package) =>
        Math.Clamp(package.Height / Math.Max(1, _chip.PerSide) * 0.55, 5, 10);

    /// <summary>How wide the longest pin name is, so the part number can keep clear of them.</summary>
    private double Names(double size) =>
        _chip.Pins.Max(pin => Small(pin, size).Width);

    private FormattedText Small(string text, double size) => new(
        text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, size,
        new SolidColorBrush(TextColor));

    /// <summary>
    /// Whether the pin names are drawn at all. On a chip too small for two columns of them -
    /// a toolbox tile, mostly - they land on top of each other and on the part number, and a
    /// package with legs and a notch says what it is without them.
    /// </summary>
    private bool ShowNames
    {
        get
        {
            var package = Package;
            var size = PinSize(package);

            return size >= 6 && Names(size) * 2 + 10 <= package.Width;
        }
    }

    /// <summary>
    /// The part number, shrunk to what is left of the package between the pin names. A label
    /// that does not fit is drawn over them, and on a chip the names are the part that has to
    /// stay readable.
    /// </summary>
    protected override double LabelSize
    {
        get
        {
            var text = DisplayText;

            if (string.IsNullOrWhiteSpace(text))
                return FontSize;

            var room = Math.Max(4, TextArea.Width - 4);
            var wanted = Small(text, FontSize).Width;

            return wanted <= room ? FontSize : Math.Max(4, FontSize * room / wanted);
        }
    }

    protected override void Draw(DrawingContext context, bool withText)
    {
        var pen = CreatePen();
        var package = Package;

        context.DrawRectangle(FillBrush(), pen, package);

        var size = PinSize(package);
        var names = ShowNames;

        for (var pin = 0; pin < _chip.Count; pin++)
        {
            var tip = Leg(pin);
            var (left, _) = Seat(pin);
            var inner = new Point(left ? package.X : package.Right, tip.Y);

            context.DrawLine(pen, inner, tip);

            if (!names)
                continue;

            var label = Small(_chip.Pins[pin], size);
            var x = left ? package.X + 3 : package.Right - 3 - label.Width;

            context.DrawText(label, new Point(x, tip.Y - label.Height / 2));
        }

        // The notch that says which end pin 1 is, without which the pinout is a guess. It is
        // on the top edge, away from the names, because a dot in the corner sat on pin 1's own.
        context.DrawGeometry(null, new Pen(new SolidColorBrush(StrokeColour()), StrokeThickness), Notch());

        if (withText)
            RenderText(context);
    }

    /// <summary>How deep the pin-1 notch is cut into the top edge.</summary>
    private double NotchRadius => Math.Clamp(Package.Width * 0.07, 3, 7);

    /// <summary>A half-circle bitten out of the middle of the top edge, as the packages have.</summary>
    private Geometry Notch()
    {
        var package = Package;
        var radius = NotchRadius;
        var geometry = new StreamGeometry();

        using (var open = geometry.Open())
        {
            open.BeginFigure(new Point(package.Center.X - radius, package.Y), false);
            open.ArcTo(new Point(package.Center.X + radius, package.Y),
                new Size(radius, radius), 0, false, SweepDirection.CounterClockwise);
            open.EndFigure(false);
        }

        return geometry;
    }

    /// <summary>
    /// How big a chip wants to be: a row down each side for every pin on it, and enough width
    /// for the longest pin name on both sides with the part number still clear between them.
    /// Measuring text here would need a font, so this counts characters instead - the numbers
    /// are the average glyph widths of the default face at the sizes the two labels use.
    /// </summary>
    public static Size PreferredSize(Chip chip)
    {
        var names = chip.Pins.Max(pin => pin.Length) * 5.5 + 4;
        var part = chip.Name.Length * 7.2 + 20;

        return new Size(
            Math.Clamp(Math.Ceiling(names * 2 + part) + 40, 110, 220),
            Math.Max(80, chip.PerSide * 24));
    }

    /// <summary>The line colour, or the text colour where the line has been turned off.</summary>
    private Color StrokeColour() => Stroke.A == 0 ? TextColor : Stroke;

    protected override string SvgBody()
    {
        var package = Package;
        var size = PinSize(package);
        var names = ShowNames;
        var sb = new StringBuilder();

        sb.Append("<g>");
        sb.Append($"<rect x=\"{Num(package.X)}\" y=\"{Num(package.Y)}\" " +
                  $"width=\"{Num(package.Width)}\" height=\"{Num(package.Height)}\" {SvgStyle()} />");

        var stroke = $"stroke=\"{ToHex(StrokeColour())}\" stroke-width=\"{Num(StrokeThickness)}\"";

        for (var pin = 0; pin < _chip.Count; pin++)
        {
            var tip = Leg(pin);
            var (left, _) = Seat(pin);
            var inner = left ? package.X : package.Right;

            sb.Append($"<line x1=\"{Num(inner)}\" y1=\"{Num(tip.Y)}\" " +
                      $"x2=\"{Num(tip.X)}\" y2=\"{Num(tip.Y)}\" {stroke} />");

            if (!names)
                continue;

            var x = left ? package.X + 3 : package.Right - 3;

            sb.Append($"<text x=\"{Num(x)}\" y=\"{Num(tip.Y + size * 0.35)}\" " +
                      $"font-size=\"{Num(size)}\" fill=\"{ToHex(TextColor)}\" " +
                      $"text-anchor=\"{(left ? "start" : "end")}\">{Escape(_chip.Pins[pin])}</text>");
        }

        var radius = NotchRadius;

        sb.Append($"<path d=\"M {Num(package.Center.X - radius)} {Num(package.Y)} " +
                  $"A {Num(radius)} {Num(radius)} 0 0 0 " +
                  $"{Num(package.Center.X + radius)} {Num(package.Y)}\" fill=\"none\" {stroke} />");

        sb.Append("</g>");
        return sb.ToString();
    }

    private static string Escape(string text) => text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;");
}
