using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Media;

namespace AVASvgMaker.Models;

/// <summary>
/// Stencil outlines are written once in a unit square - every coordinate between 0 and 1 -
/// and scaled to whatever bounds the shape has. That keeps a stencil to a line of data
/// rather than a class, and lets the same definition produce both the on-screen geometry and
/// the exported SVG, so the two cannot drift apart.
///
/// The supported commands are M, L, C, Q and Z, absolute only. Arcs are deliberately left
/// out: every curve here is expressible as a bezier, and leaving them out keeps the parser
/// small enough to be obviously correct.
/// </summary>
public static class StencilPath
{
    public static Geometry ToGeometry(string data, Rect bounds)
    {
        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            var open = false;

            foreach (var (command, points) in Parse(data, bounds))
            {
                switch (command)
                {
                    case 'M':
                        if (open)
                            context.EndFigure(false);

                        context.BeginFigure(points[0], true);
                        open = true;
                        break;

                    case 'L':
                        context.LineTo(points[0]);
                        break;

                    case 'C':
                        context.CubicBezierTo(points[0], points[1], points[2]);
                        break;

                    case 'Q':
                        context.QuadraticBezierTo(points[0], points[1]);
                        break;

                    case 'Z':
                        context.EndFigure(true);
                        open = false;
                        break;
                }
            }

            if (open)
                context.EndFigure(false);
        }

        return geometry;
    }

    /// <summary>The same outline as SVG path data, in page units.</summary>
    public static string ToSvgData(string data, Rect bounds)
    {
        var sb = new StringBuilder();

        foreach (var (command, points) in Parse(data, bounds))
        {
            if (sb.Length > 0)
                sb.Append(' ');

            sb.Append(command);

            foreach (var point in points)
                sb.Append($" {Num(point.X)},{Num(point.Y)}");
        }

        return sb.ToString();
    }

    /// <summary>Reads the commands, scaling each coordinate out of the unit square.</summary>
    private static IEnumerable<(char Command, Point[] Points)> Parse(string data, Rect bounds)
    {
        var index = 0;

        while (index < data.Length)
        {
            var character = data[index];

            if (char.IsWhiteSpace(character) || character == ',')
            {
                index++;
                continue;
            }

            if (!char.IsLetter(character))
                throw new FormatException($"Expected a command at position {index} of '{data}'.");

            index++;

            var wanted = character switch
            {
                'M' or 'L' => 1,
                'Q' => 2,
                'C' => 3,
                'Z' => 0,
                _ => throw new FormatException($"Unsupported command '{character}' in '{data}'.")
            };

            var points = new Point[wanted];

            for (var i = 0; i < wanted; i++)
            {
                var x = ReadNumber(data, ref index);
                var y = ReadNumber(data, ref index);

                points[i] = new Point(bounds.X + x * bounds.Width, bounds.Y + y * bounds.Height);
            }

            yield return (char.ToUpperInvariant(character), points);
        }
    }

    private static double ReadNumber(string data, ref int index)
    {
        while (index < data.Length && (char.IsWhiteSpace(data[index]) || data[index] == ','))
            index++;

        var start = index;

        if (index < data.Length && (data[index] == '-' || data[index] == '+'))
            index++;

        while (index < data.Length && (char.IsDigit(data[index]) || data[index] == '.'))
            index++;

        if (start == index)
            throw new FormatException($"Expected a number at position {start} of '{data}'.");

        return double.Parse(data.AsSpan(start, index - start), CultureInfo.InvariantCulture);
    }

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
