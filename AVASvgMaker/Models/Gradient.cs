using System;
using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace AVASvgMaker.Models;

/// <summary>
/// A fill that runs from one colour to another across whatever it fills.
///
/// Two stops and an angle, which is what a diagram wants: a lane that fades from its heading,
/// a page that is not flat white. Anything richer - three stops, a radial sweep, a mid-point
/// that is not the middle - would be a painting tool's business, and would have to be carried
/// through the file, the SVG, the PDF and Visio for the sake of an effect nobody asked for.
///
/// The angle is in degrees clockwise from left-to-right, so 0 runs across, 90 runs down.
/// </summary>
public readonly record struct Gradient(Color From, Color To, double Angle)
{
    /// <summary>Where the run starts and ends, as fractions of the box it fills.</summary>
    public (RelativePoint Start, RelativePoint End) Ends()
    {
        var radians = Angle * Math.PI / 180;
        var dx = Math.Cos(radians);
        var dy = Math.Sin(radians);

        // Taken from the middle out to the edge, so the whole run is used whatever the angle.
        var reach = Math.Max(Math.Abs(dx), Math.Abs(dy));

        if (reach < 1e-9)
            return (new RelativePoint(0, 0.5, RelativeUnit.Relative),
                    new RelativePoint(1, 0.5, RelativeUnit.Relative));

        dx /= reach * 2;
        dy /= reach * 2;

        return (new RelativePoint(0.5 - dx, 0.5 - dy, RelativeUnit.Relative),
                new RelativePoint(0.5 + dx, 0.5 + dy, RelativeUnit.Relative));
    }

    public IBrush Brush()
    {
        var (start, end) = Ends();

        return new LinearGradientBrush
        {
            StartPoint = start,
            EndPoint = end,
            GradientStops =
            {
                new GradientStop(From, 0),
                new GradientStop(To, 1)
            }
        };
    }

    /// <summary>
    /// The gradient as an SVG definition and the paint that names it. SVG has no gradient
    /// without a definition to point at, so each one carries its own, with an id of its own.
    /// </summary>
    public (string Defs, string Paint) Svg()
    {
        var id = $"fade{System.Threading.Interlocked.Increment(ref _next)}";
        var (start, end) = Ends();

        string Stop(Color colour, int offset) =>
            $"<stop offset=\"{offset}\" stop-color=\"{Hex(colour)}\"" +
            (colour.A == 255 ? string.Empty : $" stop-opacity=\"{Num(colour.A / 255.0)}\"") +
            " />";

        var defs =
            $"<defs><linearGradient id=\"{id}\" " +
            $"x1=\"{Num(start.Point.X)}\" y1=\"{Num(start.Point.Y)}\" " +
            $"x2=\"{Num(end.Point.X)}\" y2=\"{Num(end.Point.Y)}\">" +
            Stop(From, 0) + Stop(To, 1) +
            "</linearGradient></defs>";

        return (defs, $"url(#{id})");
    }

    private static int _next;

    private static string Hex(Color colour) => $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";

    private static string Num(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
