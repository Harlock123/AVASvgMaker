using System;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using Xunit;

namespace AVASvgMaker.Tests;

public class RotationTests
{
    private static readonly Rect Box = new(100, 100, 200, 100);

    [AvaloniaFact]
    public void TurningLeavesTheBoundsAlone()
    {
        // Rotation is a way of drawing the shape, not a change to it: everything that moves,
        // snaps, aligns or clamps still works on the upright rectangle.
        var shape = ShapeFactory.Create(ShapeKind.Rectangle, Box);
        shape.Rotation = 30;

        Assert.Equal(Box, shape.Bounds);
        Assert.True(shape.IsRotated);
    }

    [AvaloniaFact]
    public void ATurnedShapeIsHitWhereItIsDrawnAndNotWhereItWas()
    {
        var shape = ShapeFactory.Create(ShapeKind.Rectangle, Box);

        // A point just past the middle of the right edge, and one just below the bottom edge.
        var pastRight = new Point(Box.Right - 5, Box.Center.Y);
        var belowBottom = new Point(Box.Center.X, Box.Bottom + 30);

        Assert.True(shape.HitTest(pastRight));
        Assert.False(shape.HitTest(belowBottom));

        // Turned a quarter turn, the tall way is now across, so the second point is inside.
        shape.Rotation = 90;

        Assert.False(shape.HitTest(pastRight));
        Assert.True(shape.HitTest(belowBottom));
    }

    [AvaloniaFact]
    public void ConnectionPointsAndTheirDirectionsTurnWithTheShape()
    {
        var shape = ShapeFactory.Create(ShapeKind.Rectangle, Box);

        var upright = shape.ConnectionPoints[0];
        Assert.Equal(Box.Center.X, upright.X, 2);
        Assert.Equal(Box.Top, upright.Y, 2);

        shape.Rotation = 90;

        // A quarter turn clockwise puts the north point on the right of the box.
        var turned = shape.ConnectionPoints[0];
        Assert.Equal(Box.Center.X + Box.Height / 2, turned.X, 1);
        Assert.Equal(Box.Center.Y, turned.Y, 1);

        // And the direction a connector leaves by turns with it.
        var away = shape.ConnectionDirection(0);
        Assert.Equal(1, away.X, 2);
        Assert.Equal(0, away.Y, 2);
    }

    [AvaloniaFact]
    public void TheAngleIsKeptInRange()
    {
        Assert.Equal(90, DiagramDocument.Normalise(450), 4);
        Assert.Equal(270, DiagramDocument.Normalise(-90), 4);
        Assert.Equal(0, DiagramDocument.Normalise(360), 4);
    }

    [AvaloniaFact]
    public void EachShapeTurnsAboutItsOwnMiddle()
    {
        var document = Harness.Page();
        var one = Harness.Box(document, new Rect(100, 100, 80, 40));
        var two = Harness.Box(document, new Rect(400, 300, 80, 40));

        Assert.True(document.Rotate([one, two], 90));

        // Turned, but not moved: turning a selection about the selection's middle would be a
        // different operation, and not the one a turn handle offers.
        Assert.Equal(90, one.Rotation, 4);
        Assert.Equal(90, two.Rotation, 4);
        Assert.Equal(new Rect(100, 100, 80, 40), one.Bounds);
        Assert.Equal(new Rect(400, 300, 80, 40), two.Bounds);

        Assert.True(document.Rotate([one], 90));
        Assert.Equal(180, one.Rotation, 4);

        Assert.True(document.Rotate([one], 0, absolute: true));
        Assert.Equal(0, one.Rotation, 4);
        Assert.False(document.Rotate([one], 0, absolute: true));
    }

    [AvaloniaFact]
    public void SomeThingsHaveNoAngleToHave()
    {
        var document = Harness.Page();
        var a = Harness.Box(document, new Rect(100, 100, 80, 40));
        var b = Harness.Box(document, new Rect(300, 100, 80, 40));
        var connector = Harness.Join(document, a, 1, b, 3);

        var pool = ShapeFactory.Create(ShapeKind.Pool, new Rect(40, 300, 400, 200));
        document.Add(pool);

        Assert.False(connector.CanRotate);
        Assert.False(pool.CanRotate);
        Assert.True(a.CanRotate);

        Assert.False(document.Rotate([connector, pool], 90));
        Assert.Equal(0, connector.Rotation, 4);
        Assert.Equal(0, pool.Rotation, 4);
    }

    [AvaloniaFact]
    public void TheTurnReachesTheSvgAndTheFile()
    {
        var document = Harness.Page();
        var shape = Harness.Box(document, new Rect(100, 100, 200, 100), "Turned");
        shape.Rotation = 45;

        var svg = shape.ToSvg();

        Assert.Contains("<g transform=\"rotate(45 200 150)\">", svg);
        Assert.Contains("</g>", svg);

        // An upright shape says nothing about it.
        Assert.DoesNotContain("rotate(", Harness.Box(document, new Rect(0, 0, 10, 10)).ToSvg());

        var json = DiagramFile.ToJson(document);
        Assert.Contains($"\"version\": {DiagramFile.CurrentVersion}", json);

        var read = DiagramFile.FromJson(json).Shapes.First(s => s.Text == "Turned");
        Assert.Equal(45, read.Rotation, 4);
    }

    [AvaloniaFact]
    public void AnUprightDrawingWritesNoAngles()
    {
        var document = Harness.Page();
        Harness.Box(document, new Rect(10, 10, 40, 40), "plain");

        Assert.DoesNotContain("rotation", DiagramFile.ToJson(document));
    }

    [AvaloniaFact]
    public void AVersion11FileComesBackUpright()
    {
        var document = Harness.Page();
        var shape = Harness.Box(document, new Rect(10, 10, 40, 40), "was turned");
        shape.Rotation = 33;

        // Strip the field the way a version 11 writer would have left it out.
        var json = System.Text.RegularExpressions.Regex.Replace(
            DiagramFile.ToJson(document), ",?\\s*\"rotation\": [0-9.]+", string.Empty)
            .Replace($"\"version\": {DiagramFile.CurrentVersion}", "\"version\": 11");

        var read = DiagramFile.FromJson(json);

        Assert.Equal(0, read.Shapes[0].Rotation, 4);
    }

    [AvaloniaFact]
    public void ATurnedShapeIsDraggedByItsTurnHandle()
    {
        var (window, canvas) = Harness.Editor(700, 500);
        var document = canvas.Document;

        var shape = Harness.Box(document, new Rect(250, 200, 160, 80), "Turn me");
        document.SelectOnly(shape);
        Harness.Settle(window);

        // The handle sits above the middle of the top edge; dragging it round to the right of
        // the shape is a quarter turn.
        var centre = shape.Bounds.Center;
        var above = new Point(centre.X, shape.Bounds.Top - 22);
        var toTheRight = new Point(centre.X + 200, centre.Y);

        Harness.Drag(canvas, above, toTheRight);

        Assert.Equal(90, shape.Rotation, 0);
        Assert.Equal(new Rect(250, 200, 160, 80), shape.Bounds);
    }

    [AvaloniaFact]
    public void ATurnedShapeStillResizesFromTheCornerYouSee()
    {
        var (window, canvas) = Harness.Editor(700, 500);
        var document = canvas.Document;

        var shape = Harness.Box(document, new Rect(200, 200, 200, 100));
        shape.Rotation = 180;
        document.SelectOnly(shape);
        Harness.Settle(window);

        // Turned half round, the shape's own top-left corner is drawn at the bottom right.
        var handles = canvas.HandlesForTest(shape);
        Assert.Equal(400, handles[0].Center.X, 1);
        Assert.Equal(300, handles[0].Center.Y, 1);
    }
}
