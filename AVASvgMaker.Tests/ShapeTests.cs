using System;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using AVASvgMaker.Models;
using Xunit;

namespace AVASvgMaker.Tests;

public class ShapeTests
{
    private static readonly Rect Box = new(100, 100, 200, 120);

    private static Point BoxPoint(int index) => index switch
    {
        0 => new Point(Box.Center.X, Box.Top),
        1 => new Point(Box.Right, Box.Center.Y),
        2 => new Point(Box.Center.X, Box.Bottom),
        _ => new Point(Box.Left, Box.Center.Y)
    };

    private static bool Near(Point a, Point b) => Math.Abs(a.X - b.X) < 0.5 && Math.Abs(a.Y - b.Y) < 0.5;

    [AvaloniaFact]
    public void EveryConnectionPointSitsOnItsShapesOutline()
    {
        var checkedKinds = 0;
        var moved = 0;

        foreach (var kind in Enum.GetValues<ShapeKind>().Where(k => k != ShapeKind.Connector))
        {
            var shape = ShapeFactory.Create(kind, Box);
            var geometry = shape.CreateGeometry();

            // A few stencils are hollow in the middle - a bowtie, a stick figure - and have no
            // outline to walk out to. Those keep the box, which is what every shape had before.
            if (!geometry.FillContains(Box.Center))
                continue;

            checkedKinds++;

            foreach (var (point, index) in shape.ConnectionPoints.Select((p, i) => (p, i)))
            {
                var away = DiagramShape.ConnectionDirections[index];
                var justInside = new Point(point.X - away.X * 0.5, point.Y - away.Y * 0.5);
                var justOutside = new Point(point.X + away.X * 0.5, point.Y + away.Y * 0.5);

                Assert.True(geometry.FillContains(justInside),
                    $"{kind} point {index} at {point} has nothing inside it");
                Assert.False(geometry.FillContains(justOutside),
                    $"{kind} point {index} at {point} is not at the edge");

                if (!Near(point, BoxPoint(index)))
                    moved++;
            }
        }

        Assert.True(checkedKinds > 80, $"only {checkedKinds} kinds were checked");
        Assert.True(moved > 50, $"only {moved} points came in off the box");
    }

    [AvaloniaTheory]
    [InlineData(ShapeKind.Rectangle)]
    [InlineData(ShapeKind.Ellipse)]
    [InlineData(ShapeKind.Diamond)]
    [InlineData(ShapeKind.TextBox)]
    public void AShapeThatAlreadyTouchedTheBoxDoesNotMove(ShapeKind kind)
    {
        var points = ShapeFactory.Create(kind, Box).ConnectionPoints;

        for (var i = 0; i < points.Count; i++)
            Assert.True(Near(points[i], BoxPoint(i)), $"{kind} point {i} moved to {points[i]}");
    }

    [AvaloniaFact]
    public void ATrianglesSidesBringItsSidePointsIn()
    {
        var points = ShapeFactory.Create(ShapeKind.Triangle, Box).ConnectionPoints;

        Assert.True(Near(points[0], BoxPoint(0)), "the apex is still the north point");
        Assert.True(Near(points[2], BoxPoint(2)), "the base is still the south point");

        Assert.True(points[1].X < Box.Right - 20, $"east came in to {points[1].X}");
        Assert.True(points[3].X > Box.Left + 20, $"west came in to {points[3].X}");

        Assert.Equal(points[1].X - Box.Center.X, Box.Center.X - points[3].X, 2);
    }

    [AvaloniaFact]
    public void ConnectionPointsAreKeptUntilTheShapeMoves()
    {
        var shape = ShapeFactory.Create(ShapeKind.Hexagon, Box);

        var first = shape.ConnectionPoints;
        Assert.Same(first, shape.ConnectionPoints);

        shape.Translate(new Vector(50, 25));

        var after = shape.ConnectionPoints;
        Assert.NotSame(first, after);
        Assert.True(Near(after[0], new Point(first[0].X + 50, first[0].Y + 25)));
    }

    [AvaloniaFact]
    public void BoldTextIsWiderThanPlainText()
    {
        // The cheapest proof the typeface is really in use rather than merely recorded.
        var plain = ShapeFactory.Create(ShapeKind.Rectangle, Box);
        plain.Text = "Wide enough to measure";

        var heavy = ShapeFactory.Create(ShapeKind.Rectangle, Box);
        heavy.Text = plain.Text;
        heavy.Bold = true;

        Assert.True(Width(heavy) > Width(plain), $"{Width(heavy)} against {Width(plain)}");
    }

    private static double Width(DiagramShape shape) => new Avalonia.Media.FormattedText(
        shape.Text, System.Globalization.CultureInfo.CurrentCulture,
        Avalonia.Media.FlowDirection.LeftToRight,
        new Avalonia.Media.Typeface(
            Avalonia.Media.FontFamily.Default,
            shape.Italic ? Avalonia.Media.FontStyle.Italic : Avalonia.Media.FontStyle.Normal,
            shape.Bold ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal),
        shape.FontSize, Avalonia.Media.Brushes.Black).Width;
}
