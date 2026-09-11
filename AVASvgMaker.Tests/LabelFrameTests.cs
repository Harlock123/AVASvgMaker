using System;
using Avalonia;
using Avalonia.Headless.XUnit;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using AVASvgMaker.Views;
using Xunit;

namespace AVASvgMaker.Tests;

/// <summary>
/// Moving a shape's label off the shape, with the pointer, through the real canvas.
/// </summary>
public class LabelFrameTests
{
    /// <summary>
    /// Where the grip sits: out to the left of the block the words are in, and carried round
    /// with the shape when the shape has been turned.
    /// </summary>
    private static Point Grip(DrawingCanvas canvas, DiagramShape shape)
    {
        var area = shape.LabelArea;
        var beside = new Point(area.Left - 20 / canvas.Zoom, area.Center.Y);

        if (!shape.IsRotated)
            return beside;

        var centre = shape.Bounds.Center;
        var radians = shape.Rotation * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var dx = beside.X - centre.X;
        var dy = beside.Y - centre.Y;

        return new Point(centre.X + dx * cos - dy * sin, centre.Y + dx * sin + dy * cos);
    }

    private static (DrawingCanvas Canvas, DiagramShape Shape) Lettered()
    {
        var (_, canvas) = Harness.Editor(700, 500);
        var shape = Harness.Box(canvas.Document, new Rect(200, 200, 200, 100), "a label");

        Harness.Click(canvas, shape.Bounds.Center);

        return (canvas, shape);
    }

    [AvaloniaFact]
    public void AShapeStartsWithItsLabelSimplyInIt()
    {
        var (_, shape) = Lettered();

        Assert.Null(shape.TextFrame);
        Assert.Equal(shape.Bounds, shape.LabelArea);
    }

    [AvaloniaFact]
    public void DraggingTheGripTakesTheLabelWithIt()
    {
        var (canvas, shape) = Lettered();
        var where = shape.Bounds;

        Harness.Drag(canvas, Grip(canvas, shape), Grip(canvas, shape) + new Vector(0, 150));

        var frame = Assert.NotNull(shape.TextFrame);

        // A hundred and fifty pixels down a shape a hundred tall is one and a half of it.
        Assert.Equal(1.5, frame.Y, 2);
        Assert.Equal(0, frame.X, 2);

        // The shape itself has not moved, and the label has left it.
        Assert.Equal(where, shape.Bounds);
        Assert.Equal(where.Y + 150, shape.LabelArea.Y, 1);
    }

    [AvaloniaFact]
    public void TheLabelKeepsItsPlaceAsTheShapeIsMovedAndResized()
    {
        // The frame is fractions of the shape, so the label rides along rather than being
        // left behind on the page where it was dropped.
        var (canvas, shape) = Lettered();

        Harness.Drag(canvas, Grip(canvas, shape), Grip(canvas, shape) + new Vector(0, 150));

        var below = shape.LabelArea.Y - shape.Bounds.Y;

        shape.Bounds = new Rect(400, 300, 200, 100);
        Assert.Equal(below, shape.LabelArea.Y - shape.Bounds.Y, 1);

        // Twice as tall, so a label one and a half heights down is twice as far down.
        shape.Bounds = new Rect(400, 300, 200, 200);
        Assert.Equal(below * 2, shape.LabelArea.Y - shape.Bounds.Y, 1);
    }

    [AvaloniaFact]
    public void DraggingTheGripDoesNotDragTheShape()
    {
        var (canvas, shape) = Lettered();
        var where = shape.Bounds;

        Harness.Drag(canvas, Grip(canvas, shape), Grip(canvas, shape) + new Vector(60, 40));

        Assert.Equal(where, shape.Bounds);
        Assert.NotNull(shape.TextFrame);
    }

    [AvaloniaFact]
    public void DraggingTheShapeItselfDoesNotMoveTheLabel()
    {
        var (canvas, shape) = Lettered();

        Harness.Drag(canvas, shape.Bounds.Center,
            new Point(shape.Bounds.Center.X + 50, shape.Bounds.Center.Y));

        Assert.Null(shape.TextFrame);
    }

    [AvaloniaFact]
    public void MovingALabelIsOneStepToUndo()
    {
        var (canvas, shape) = Lettered();
        var history = new UndoStack(canvas.Document);

        Harness.Drag(canvas, Grip(canvas, shape), Grip(canvas, shape) + new Vector(0, 150));
        Assert.NotNull(shape.TextFrame);

        // One step for the whole drag, not one per pointer move.
        Assert.True(history.CanUndo);
        history.Undo();

        Assert.Null(canvas.Document.Shapes[0].TextFrame);
        Assert.False(history.CanUndo);
    }

    [AvaloniaFact]
    public void ALabelCanBePutBackWhereItStarted()
    {
        var (canvas, shape) = Lettered();

        Harness.Drag(canvas, Grip(canvas, shape), Grip(canvas, shape) + new Vector(0, 150));
        Assert.NotNull(shape.TextFrame);

        canvas.ResetLabels();

        Assert.Null(shape.TextFrame);
        Assert.Equal(shape.Bounds, shape.LabelArea);
    }

    [AvaloniaFact]
    public void AShapeWithNoWordsHasNothingToMove()
    {
        var (_, canvas) = Harness.Editor(700, 500);
        var shape = Harness.Box(canvas.Document, new Rect(200, 200, 200, 100), string.Empty);

        Harness.Click(canvas, shape.Bounds.Center);
        Harness.Drag(canvas, Grip(canvas, shape), Grip(canvas, shape) + new Vector(0, 150));

        Assert.Null(shape.TextFrame);
    }

    [AvaloniaFact]
    public void ATurnedShapesLabelTravelsWithTheTurn()
    {
        // The frame is measured in the shape's own upright frame, so dragging the grip of a
        // shape lying on its side moves the label along the shape rather than down the screen.
        var (canvas, shape) = Lettered();

        shape.Rotation = 90;
        Harness.Click(canvas, shape.Bounds.Center);

        // On screen the grip is now below the shape's middle; dragging it further down the
        // screen moves the label across the shape.
        var grip = Grip(canvas, shape);
        Harness.Drag(canvas, grip, grip + new Vector(0, 100));

        var frame = Assert.NotNull(shape.TextFrame);

        Assert.Equal(0, frame.Y, 2);
        Assert.NotEqual(0, Math.Round(frame.X, 2));
    }
}
