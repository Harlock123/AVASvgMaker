using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Media;

namespace AVASvgMaker.Models;

/// <summary>
/// A chip, drawn with a leg for every pin - and a connection point on every leg, which is the
/// reason it is a class of its own rather than an outline.
///
/// Everything else in the catalogue offers the four points of its box. A 555 offers eight, a
/// 7400 fourteen, each one named, each one facing out of the side of the package it is on. So
/// a wire can be drawn to TRIG and stay on TRIG while the chip is moved about, which is the
/// whole of what makes a chip worth having as a shape rather than as a labelled rectangle.
///
/// Two packages are drawn: the dual in-line the logic comes in, with legs down both sides,
/// and the TO-220 a regulator comes in, with three legs out of the bottom under a tab.
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

    /// <summary>Legs out of the bottom, rather than down both sides.</summary>
    private bool Upright => _chip.Package == ChipPackage.To220;

    /// <summary>How far the legs stick out of the package.</summary>
    private double Lead => Upright
        ? Math.Clamp(Bounds.Height * 0.2, 6, 24)
        : Math.Clamp(Bounds.Width * 0.14, 4, 20);

    /// <summary>The package itself, inside the legs.</summary>
    private Rect Package => Upright
        ? new(Bounds.X, Bounds.Y, Math.Max(1, Bounds.Width), Math.Max(1, Bounds.Height - Lead))
        : new(Bounds.X + Lead, Bounds.Y, Math.Max(1, Bounds.Width - Lead * 2), Math.Max(1, Bounds.Height));

    /// <summary>The mounting tab across the top of a TO-220, and the hole through it.</summary>
    private (Rect Tab, Point Hole, double Radius) Tabbing()
    {
        var package = Package;
        var height = package.Height * 0.28;
        var inset = package.Width * 0.12;

        var tab = new Rect(
            package.X + inset, package.Y, Math.Max(1, package.Width - inset * 2), Math.Max(1, height));

        return (tab, new Point(tab.Center.X, tab.Y + height * 0.45),
            Math.Min(height * 0.28, package.Width * 0.055));
    }

    /// <summary>The plastic part: under the tab on a TO-220, and the whole of a DIP.</summary>
    private Rect Body
    {
        get
        {
            var package = Package;

            if (!Upright)
                return package;

            var top = package.Y + Tabbing().Tab.Height;

            return new Rect(package.X, top, package.Width, Math.Max(1, package.Bottom - top));
        }
    }

    /// <summary>Which side a pin is on, and how far down that side it sits.</summary>
    private (bool Left, int Row) Seat(int pin) =>
        pin < _chip.PerSide ? (true, pin) : (false, _chip.Count - 1 - pin);

    private Point Leg(int pin)
    {
        var body = Body;

        if (Upright)
        {
            var across = body.Width / Math.Max(1, _chip.Count);

            return new Point(body.X + (pin + 0.5) * across, Bounds.Bottom);
        }

        var (left, row) = Seat(pin);
        var step = body.Height / Math.Max(1, _chip.PerSide);

        return new Point(left ? Bounds.X : Bounds.Right, body.Y + (row + 0.5) * step);
    }

    /// <summary>
    /// One point per pin, at the outer tip of its leg. A DIP is numbered the way a DIP is:
    /// down the left side from the top, then back up the right. A TO-220's three run left to
    /// right along the foot.
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
        index < 0 || index >= _chip.Count ? default
        : Upright ? new Vector(0, 1)
        : new Vector(Seat(index).Left ? -1 : 1, 0);

    /// <summary>The package, not the legs: a wire should meet a pin, not the empty air by it.</summary>
    public override Geometry CreateGeometry() => new RectangleGeometry(Package);

    /// <summary>
    /// The middle of the body. The pin names run down both edges of a DIP and along the foot
    /// of a TO-220, and a part number centred over the whole of it would sit on top of them.
    /// </summary>
    protected override Rect TextArea
    {
        get
        {
            var body = Body;

            if (Upright)
            {
                var foot = ShowNames ? PinSize(body) * 1.7 : 0;

                return new Rect(body.X, body.Y, body.Width, Math.Max(8, body.Height - foot));
            }

            var gutter = ShowNames
                ? Math.Min(body.Width * 0.42, Names(PinSize(body)) + 5)
                : 0;

            return new Rect(
                body.X + gutter, body.Y,
                Math.Max(8, body.Width - gutter * 2), body.Height);
        }
    }

    /// <summary>A font small enough that a pin name fits in the room its own leg has.</summary>
    private double PinSize(Rect body) => Upright
        ? Math.Clamp(body.Width / Math.Max(1, _chip.Count) * 0.28, 5, 10)
        : Math.Clamp(body.Height / Math.Max(1, _chip.PerSide) * 0.55, 5, 10);

    /// <summary>How wide the longest pin name is, so the part number can keep clear of them.</summary>
    private double Names(double size) =>
        _chip.Pins.Max(pin => Small(pin, size).Width);

    private FormattedText Small(string text, double size) => new(
        text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, size,
        new SolidColorBrush(TextColor));

    /// <summary>
    /// Whether the pin names are drawn at all. On a chip too small for them - a toolbox tile,
    /// mostly - they land on top of each other and on the part number, and a package with
    /// legs and a notch says what it is without them.
    /// </summary>
    private bool ShowNames
    {
        get
        {
            var body = Body;
            var size = PinSize(body);

            if (size < 6)
                return false;

            return Upright
                ? Names(size) + 4 <= body.Width / Math.Max(1, _chip.Count)
                : Names(size) * 2 + 10 <= body.Width;
        }
    }

    /// <summary>
    /// The part number, shrunk to what is left of the package beside the pin names. A label
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
        var body = Body;
        var size = PinSize(body);
        var names = ShowNames;

        if (Upright)
        {
            var (tab, hole, radius) = Tabbing();

            context.DrawRectangle(FillBrush(), pen, tab);
            context.DrawEllipse(null, pen, hole, radius, radius);
        }

        context.DrawRectangle(FillBrush(), pen, body);

        for (var pin = 0; pin < _chip.Count; pin++)
        {
            var tip = Leg(pin);

            if (Upright)
            {
                context.DrawLine(pen, new Point(tip.X, body.Bottom), tip);

                if (!names)
                    continue;

                var foot = Small(_chip.Pins[pin], size);

                context.DrawText(foot, new Point(
                    tip.X - foot.Width / 2, body.Bottom - 3 - foot.Height));

                continue;
            }

            var (left, _) = Seat(pin);

            context.DrawLine(pen, new Point(left ? body.X : body.Right, tip.Y), tip);

            if (!names)
                continue;

            var label = Small(_chip.Pins[pin], size);
            var x = left ? body.X + 3 : body.Right - 3 - label.Width;

            context.DrawText(label, new Point(x, tip.Y - label.Height / 2));
        }

        // The notch that says which end pin 1 is, without which the pinout is a guess. It is
        // on the top edge, away from the names, because a dot in the corner sat on pin 1's own.
        // A TO-220 needs none: it has a tab, and three named legs in one order.
        if (!Upright)
            context.DrawGeometry(
                null, new Pen(new SolidColorBrush(StrokeColour()), StrokeThickness), Notch());

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
    /// How big a chip wants to be: room for every leg, and for the longest pin name with the
    /// part number still clear of it. Measuring text here would need a font, so this counts
    /// characters instead - the numbers are the average glyph widths of the default face at
    /// the sizes the two labels use.
    /// </summary>
    public static Size PreferredSize(Chip chip)
    {
        var longest = chip.Pins.Max(pin => pin.Length);
        var part = chip.Name.Length * 8.0 + 20;

        if (chip.Package == ChipPackage.To220)
            return new Size(
                Math.Clamp(Math.Ceiling(Math.Max(chip.Count * (longest * 6.2 + 12), part)), 90, 220),
                88);

        return new Size(
            Math.Clamp(Math.Ceiling((longest * 6.0 + 4) * 2 + part) + 40, 110, 220),
            Math.Max(80, chip.PerSide * 24));
    }

    /// <summary>The line colour, or the text colour where the line has been turned off.</summary>
    private Color StrokeColour() => Stroke.A == 0 ? TextColor : Stroke;

    protected override string SvgBody()
    {
        var body = Body;
        var size = PinSize(body);
        var names = ShowNames;
        var sb = new StringBuilder();

        var stroke = $"stroke=\"{ToHex(StrokeColour())}\" stroke-width=\"{Num(StrokeThickness)}\"";

        sb.Append("<g>");

        if (Upright)
        {
            var (tab, hole, radius) = Tabbing();

            sb.Append($"<rect x=\"{Num(tab.X)}\" y=\"{Num(tab.Y)}\" " +
                      $"width=\"{Num(tab.Width)}\" height=\"{Num(tab.Height)}\" {SvgStyle()} />");

            sb.Append($"<circle cx=\"{Num(hole.X)}\" cy=\"{Num(hole.Y)}\" r=\"{Num(radius)}\" " +
                      $"fill=\"none\" {stroke} />");
        }

        sb.Append($"<rect x=\"{Num(body.X)}\" y=\"{Num(body.Y)}\" " +
                  $"width=\"{Num(body.Width)}\" height=\"{Num(body.Height)}\" {SvgStyle()} />");

        for (var pin = 0; pin < _chip.Count; pin++)
        {
            var tip = Leg(pin);
            var name = Escape(_chip.Pins[pin]);

            if (Upright)
            {
                sb.Append($"<line x1=\"{Num(tip.X)}\" y1=\"{Num(body.Bottom)}\" " +
                          $"x2=\"{Num(tip.X)}\" y2=\"{Num(tip.Y)}\" {stroke} />");

                if (!names)
                    continue;

                sb.Append($"<text x=\"{Num(tip.X)}\" y=\"{Num(body.Bottom - 3 - size * 0.35)}\" " +
                          $"font-size=\"{Num(size)}\" fill=\"{ToHex(TextColor)}\" " +
                          $"text-anchor=\"middle\">{name}</text>");

                continue;
            }

            var (left, _) = Seat(pin);
            var inner = left ? body.X : body.Right;

            sb.Append($"<line x1=\"{Num(inner)}\" y1=\"{Num(tip.Y)}\" " +
                      $"x2=\"{Num(tip.X)}\" y2=\"{Num(tip.Y)}\" {stroke} />");

            if (!names)
                continue;

            var x = left ? body.X + 3 : body.Right - 3;

            sb.Append($"<text x=\"{Num(x)}\" y=\"{Num(tip.Y + size * 0.35)}\" " +
                      $"font-size=\"{Num(size)}\" fill=\"{ToHex(TextColor)}\" " +
                      $"text-anchor=\"{(left ? "start" : "end")}\">{name}</text>");
        }

        if (!Upright)
        {
            var radius = NotchRadius;
            var package = Package;

            sb.Append($"<path d=\"M {Num(package.Center.X - radius)} {Num(package.Y)} " +
                      $"A {Num(radius)} {Num(radius)} 0 0 0 " +
                      $"{Num(package.Center.X + radius)} {Num(package.Y)}\" fill=\"none\" {stroke} />");
        }

        sb.Append("</g>");
        return sb.ToString();
    }

    private static string Escape(string text) => text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;");
}
