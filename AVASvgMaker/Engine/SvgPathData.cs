using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Avalonia;

namespace AVASvgMaker.Engine;

/// <summary>
/// Reads an SVG path's <c>d</c> attribute and gives back the same shape written the way the
/// stencil language wants it: absolute, in a unit square, and with every curve a bezier.
///
/// Two things are normalised away. Relative commands and the shorthands (H, V, S, T) become
/// their absolute long forms, because the stencil parser knows only M, L, C, Q and Z. And arcs
/// become cubics, because the stencil language deliberately has none - real drawings are full
/// of arcs, so they are converted rather than refused.
/// </summary>
public static class SvgPathData
{
    private sealed record Segment(char Command, Point[] Points);

    /// <summary>
    /// The path in user coordinates, its extent, and whether it closes. Returns null when
    /// there is nothing drawable in it.
    /// </summary>
    public static (string Outline, Rect Extent, bool Closed)? Normalise(string data, Matrix transform)
    {
        var segments = Read(data);

        if (segments.Count == 0)
            return null;

        var closed = false;
        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;

        foreach (var segment in segments)
        {
            if (segment.Command == 'Z')
            {
                closed = true;
                continue;
            }

            for (var i = 0; i < segment.Points.Length; i++)
            {
                var point = segment.Points[i].Transform(transform);
                segment.Points[i] = point;

                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }
        }

        if (minX > maxX || minY > maxY)
            return null;

        // A line with no width, or none with no height, still has to have a box to live in.
        var width = Math.Max(maxX - minX, 0.01);
        var height = Math.Max(maxY - minY, 0.01);
        var extent = new Rect(minX, minY, width, height);

        var outline = new StringBuilder();

        foreach (var segment in segments)
        {
            outline.Append(segment.Command);

            foreach (var point in segment.Points)
                outline.Append($" {Num((point.X - minX) / width)},{Num((point.Y - minY) / height)}");

            outline.Append(' ');
        }

        return (outline.ToString().Trim(), extent, closed);
    }

    /// <summary>Every command as an absolute M, L, C, Q or Z.</summary>
    private static List<Segment> Read(string data)
    {
        var segments = new List<Segment>();

        if (string.IsNullOrWhiteSpace(data))
            return segments;

        var at = 0;
        var cursor = new Point();
        var start = new Point();

        // Kept for the S and T shorthands, which mirror the previous curve's last control point.
        var lastCubic = new Point();
        var lastQuadratic = new Point();
        var previous = ' ';

        while (at < data.Length)
        {
            var command = NextCommand(data, ref at, previous);

            if (command == '\0')
                break;

            var relative = char.IsLower(command);
            var upper = char.ToUpperInvariant(command);

            Point Take()
            {
                var x = Number(data, ref at);
                var y = Number(data, ref at);
                return relative ? new Point(cursor.X + x, cursor.Y + y) : new Point(x, y);
            }

            switch (upper)
            {
                case 'M':
                {
                    var to = Take();
                    segments.Add(new Segment('M', [to]));
                    cursor = start = to;

                    // Further pairs after a move are implicit line-tos, as the spec has it.
                    while (More(data, at))
                    {
                        var next = Take();
                        segments.Add(new Segment('L', [next]));
                        cursor = next;
                    }

                    break;
                }

                case 'L':
                    while (More(data, at) || segments.Count == 0)
                    {
                        var to = Take();
                        segments.Add(new Segment('L', [to]));
                        cursor = to;

                        if (!More(data, at))
                            break;
                    }

                    break;

                case 'H':
                    do
                    {
                        var x = Number(data, ref at);
                        cursor = new Point(relative ? cursor.X + x : x, cursor.Y);
                        segments.Add(new Segment('L', [cursor]));
                    }
                    while (More(data, at));

                    break;

                case 'V':
                    do
                    {
                        var y = Number(data, ref at);
                        cursor = new Point(cursor.X, relative ? cursor.Y + y : y);
                        segments.Add(new Segment('L', [cursor]));
                    }
                    while (More(data, at));

                    break;

                case 'C':
                    do
                    {
                        var one = Take();
                        var two = Take();
                        var to = Take();

                        segments.Add(new Segment('C', [one, two, to]));
                        lastCubic = two;
                        cursor = to;
                    }
                    while (More(data, at));

                    break;

                case 'S':
                    do
                    {
                        // The first control point is the reflection of the last one.
                        var one = upper == 'S' && previous is 'C' or 'c' or 'S' or 's'
                            ? new Point(2 * cursor.X - lastCubic.X, 2 * cursor.Y - lastCubic.Y)
                            : cursor;

                        var two = Take();
                        var to = Take();

                        segments.Add(new Segment('C', [one, two, to]));
                        lastCubic = two;
                        cursor = to;
                        previous = 'S';
                    }
                    while (More(data, at));

                    break;

                case 'Q':
                    do
                    {
                        var control = Take();
                        var to = Take();

                        segments.Add(new Segment('Q', [control, to]));
                        lastQuadratic = control;
                        cursor = to;
                    }
                    while (More(data, at));

                    break;

                case 'T':
                    do
                    {
                        var control = previous is 'Q' or 'q' or 'T' or 't'
                            ? new Point(2 * cursor.X - lastQuadratic.X, 2 * cursor.Y - lastQuadratic.Y)
                            : cursor;

                        var to = Take();

                        segments.Add(new Segment('Q', [control, to]));
                        lastQuadratic = control;
                        cursor = to;
                        previous = 'T';
                    }
                    while (More(data, at));

                    break;

                case 'A':
                    do
                    {
                        var rx = Number(data, ref at);
                        var ry = Number(data, ref at);
                        var angle = Number(data, ref at);
                        var large = Number(data, ref at) != 0;
                        var sweep = Number(data, ref at) != 0;
                        var to = Take();

                        foreach (var curve in Arc(cursor, to, rx, ry, angle, large, sweep))
                            segments.Add(curve);

                        cursor = to;
                    }
                    while (More(data, at));

                    break;

                case 'Z':
                    segments.Add(new Segment('Z', []));
                    cursor = start;
                    break;
            }

            if (upper is not ('S' or 'T'))
                previous = command;
        }

        return segments;
    }

    /// <summary>
    /// An elliptical arc as up to four cubics. The endpoint form the file gives is turned into
    /// the centre form the maths wants, following the conversion in the SVG specification's
    /// implementation notes, and each quarter turn or less is then a bezier good to well under
    /// a thousandth of the radius.
    /// </summary>
    private static IEnumerable<Segment> Arc(
        Point from, Point to, double rx, double ry, double angle, bool large, bool sweep)
    {
        if (Math.Abs(rx) < 1e-9 || Math.Abs(ry) < 1e-9)
        {
            yield return new Segment('L', [to]);
            yield break;
        }

        rx = Math.Abs(rx);
        ry = Math.Abs(ry);

        var phi = angle * Math.PI / 180;
        var cos = Math.Cos(phi);
        var sin = Math.Sin(phi);

        var dx = (from.X - to.X) / 2;
        var dy = (from.Y - to.Y) / 2;

        var x1 = cos * dx + sin * dy;
        var y1 = -sin * dx + cos * dy;

        // Radii too small to reach are scaled up until they just do.
        var lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);

        if (lambda > 1)
        {
            var scale = Math.Sqrt(lambda);
            rx *= scale;
            ry *= scale;
        }

        var numerator = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
        var denominator = rx * rx * y1 * y1 + ry * ry * x1 * x1;
        var factor = Math.Sqrt(Math.Max(0, numerator / denominator)) * (large == sweep ? -1 : 1);

        var cx1 = factor * rx * y1 / ry;
        var cy1 = -factor * ry * x1 / rx;

        var centre = new Point(
            cos * cx1 - sin * cy1 + (from.X + to.X) / 2,
            sin * cx1 + cos * cy1 + (from.Y + to.Y) / 2);

        double Angle(double ux, double uy) => Math.Atan2(uy, ux);

        var start = Angle((x1 - cx1) / rx, (y1 - cy1) / ry);
        var end = Angle((-x1 - cx1) / rx, (-y1 - cy1) / ry);
        var sweepAngle = end - start;

        if (!sweep && sweepAngle > 0)
            sweepAngle -= 2 * Math.PI;
        else if (sweep && sweepAngle < 0)
            sweepAngle += 2 * Math.PI;

        var steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweepAngle) / (Math.PI / 2)));
        var step = sweepAngle / steps;

        // The control-point distance that makes a bezier match a circular arc of this angle.
        var reach = 4.0 / 3 * Math.Tan(step / 4);

        Point At(double t) => new(
            centre.X + rx * Math.Cos(t) * cos - ry * Math.Sin(t) * sin,
            centre.Y + rx * Math.Cos(t) * sin + ry * Math.Sin(t) * cos);

        Point Slope(double t) => new(
            -rx * Math.Sin(t) * cos - ry * Math.Cos(t) * sin,
            -rx * Math.Sin(t) * sin + ry * Math.Cos(t) * cos);

        for (var i = 0; i < steps; i++)
        {
            var a = start + i * step;
            var b = a + step;

            var pa = At(a);
            var pb = At(b);
            var sa = Slope(a);
            var sb = Slope(b);

            yield return new Segment('C', [
                new Point(pa.X + reach * sa.X, pa.Y + reach * sa.Y),
                new Point(pb.X - reach * sb.X, pb.Y - reach * sb.Y),
                pb
            ]);
        }
    }

    private static char NextCommand(string data, ref int at, char previous)
    {
        SkipSeparators(data, ref at);

        if (at >= data.Length)
            return '\0';

        if (char.IsLetter(data[at]))
            return data[at++];

        // A run of numbers with no letter repeats the previous command, except that a repeated
        // move is a line, which is what the spec says and what every drawing tool emits.
        return previous switch
        {
            'M' => 'L',
            'm' => 'l',
            ' ' or '\0' => '\0',
            _ => previous
        };
    }

    private static bool More(string data, int at)
    {
        SkipSeparators(data, ref at);
        return at < data.Length && (char.IsDigit(data[at]) || data[at] is '-' or '+' or '.');
    }

    private static void SkipSeparators(string data, ref int at)
    {
        while (at < data.Length && (char.IsWhiteSpace(data[at]) || data[at] == ','))
            at++;
    }

    private static double Number(string data, ref int at)
    {
        SkipSeparators(data, ref at);

        var from = at;

        if (at < data.Length && data[at] is '-' or '+')
            at++;

        while (at < data.Length && (char.IsDigit(data[at]) || data[at] == '.'))
            at++;

        // An exponent, which a machine-written path may well have.
        if (at < data.Length && (data[at] == 'e' || data[at] == 'E'))
        {
            at++;

            if (at < data.Length && data[at] is '-' or '+')
                at++;

            while (at < data.Length && char.IsDigit(data[at]))
                at++;
        }

        return double.TryParse(data.AsSpan(from, at - from), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static string Num(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
