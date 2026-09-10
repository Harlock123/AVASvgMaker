using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using Xunit;

namespace AVASvgMaker.Tests;

public class NudgeTests
{
    [AvaloniaFact]
    public void AltAndAnArrowMovesTheSelectionByOneGridStep()
    {
        var (window, canvas) = Harness.Editor(600, 400);
        var document = canvas.Document;

        canvas.Grid.SnapToGrid = true;
        canvas.Grid.Size = 10;

        var shape = Harness.Box(document, new Rect(100, 100, 80, 40));
        document.SelectOnly(shape);

        Harness.Press(window, Key.Right, RawInputModifiers.Alt);
        Assert.Equal(110, shape.Bounds.X, 2);

        Harness.Press(window, Key.Down, RawInputModifiers.Alt);
        Assert.Equal(110, shape.Bounds.Y, 2);

        Harness.Press(window, Key.Left, RawInputModifiers.Alt);
        Harness.Press(window, Key.Up, RawInputModifiers.Alt);

        Assert.Equal(new Rect(100, 100, 80, 40), shape.Bounds);
    }

    [AvaloniaFact]
    public void TheStepFollowsTheGrid()
    {
        var (window, canvas) = Harness.Editor(600, 400);
        var document = canvas.Document;

        var shape = Harness.Box(document, new Rect(100, 100, 80, 40));
        document.SelectOnly(shape);

        canvas.Grid.SnapToGrid = true;
        canvas.Grid.Size = 25;
        Harness.Press(window, Key.Right, RawInputModifiers.Alt);
        Assert.Equal(125, shape.Bounds.X, 2);

        // With snapping off there is no grid to follow, so it moves by the smallest step there is.
        canvas.Grid.SnapToGrid = false;
        Harness.Press(window, Key.Right, RawInputModifiers.Alt);
        Assert.Equal(126, shape.Bounds.X, 2);
    }

    [AvaloniaFact]
    public void AContainerTakesItsContentsWithIt()
    {
        var (window, canvas) = Harness.Editor(700, 500);
        var document = canvas.Document;

        canvas.Grid.SnapToGrid = true;
        canvas.Grid.Size = 10;

        var box = (ContainerShape)ShapeFactory.Create(ShapeKind.ContainerBox, new Rect(60, 60, 300, 200));
        document.Add(box);

        var inside = Harness.Box(document, new Rect(100, 120, 80, 40), "in it");
        document.Adopt(inside, box);

        document.SelectOnly(box);
        Harness.Press(window, Key.Right, RawInputModifiers.Alt);

        Assert.Equal(70, box.Bounds.X, 2);
        Assert.Equal(110, inside.Bounds.X, 2);
    }

    [AvaloniaFact]
    public void NothingMovesTwice()
    {
        var (window, canvas) = Harness.Editor(700, 500);
        var document = canvas.Document;

        canvas.Grid.SnapToGrid = true;
        canvas.Grid.Size = 10;

        var box = (ContainerShape)ShapeFactory.Create(ShapeKind.ContainerBox, new Rect(60, 60, 300, 200));
        document.Add(box);

        var inside = Harness.Box(document, new Rect(100, 120, 80, 40), "in it");
        document.Adopt(inside, box);

        // Both the container and its child selected: the child must still move only one step.
        document.SetSelection([box, inside]);
        Harness.Press(window, Key.Right, RawInputModifiers.Alt);

        Assert.Equal(110, inside.Bounds.X, 2);
    }

    [AvaloniaFact]
    public void AGroupMovesTogether()
    {
        var (window, canvas) = Harness.Editor(700, 500);
        var document = canvas.Document;

        canvas.Grid.SnapToGrid = true;
        canvas.Grid.Size = 10;

        var one = Harness.Box(document, new Rect(100, 100, 60, 40));
        var two = Harness.Box(document, new Rect(300, 200, 60, 40));

        document.Group([one, two]);
        document.SetSelection(document.WithGroups([one]));

        Harness.Press(window, Key.Down, RawInputModifiers.Alt);

        Assert.Equal(110, one.Bounds.Y, 2);
        Assert.Equal(210, two.Bounds.Y, 2);
    }

    [AvaloniaFact]
    public void AFreeConnectorEndMovesAndAGluedOneFollowsItsShape()
    {
        var (window, canvas) = Harness.Editor(700, 400);
        var document = canvas.Document;

        canvas.Grid.SnapToGrid = true;
        canvas.Grid.Size = 10;

        var a = Harness.Box(document, new Rect(80, 160, 100, 50), "A");
        var b = Harness.Box(document, new Rect(400, 160, 100, 50), "B");
        var glued = Harness.Join(document, a, 1, b, 3);

        var free = new ConnectorShape(new Point(100, 320), new Point(300, 320))
        {
            Routing = ConnectorRouting.Straight
        };

        document.Add(free);
        document.RouteConnectors();

        // A free connector moves as a shape does.
        document.SelectOnly(free);
        Harness.Press(window, Key.Right, RawInputModifiers.Alt);

        Assert.Equal(110, free.Start.X, 2);
        Assert.Equal(310, free.End.X, 2);

        // A glued one is moved by the shape it is stuck to, not by itself.
        var before = glued.ResolvedStart;
        document.SelectOnly(a);
        Harness.Press(window, Key.Down, RawInputModifiers.Alt);
        document.RouteConnectors();

        Assert.Equal(before.Y + 10, glued.ResolvedStart.Y, 2);
    }

    [AvaloniaFact]
    public void TheSelectionStopsAtThePageEdgeTogether()
    {
        var (window, canvas) = Harness.Editor(400, 300);
        var document = canvas.Document;

        canvas.Grid.SnapToGrid = true;
        canvas.Grid.Size = 10;

        var leading = Harness.Box(document, new Rect(330, 100, 60, 40));
        var trailing = Harness.Box(document, new Rect(100, 100, 60, 40));

        document.SetSelection([leading, trailing]);

        // The leading shape has 10 left; both move that far and then neither moves again.
        Harness.Press(window, Key.Right, RawInputModifiers.Alt);
        Assert.Equal(340, leading.Bounds.X, 2);
        Assert.Equal(110, trailing.Bounds.X, 2);

        Harness.Press(window, Key.Right, RawInputModifiers.Alt);
        Assert.Equal(340, leading.Bounds.X, 2);
        Assert.Equal(110, trailing.Bounds.X, 2);
    }

    [AvaloniaFact]
    public void NudgingIsAnEditThatUndoes()
    {
        var (window, canvas) = Harness.Editor(600, 400);
        var document = canvas.Document;

        canvas.Grid.SnapToGrid = true;
        canvas.Grid.Size = 10;

        var shape = Harness.Box(document, new Rect(100, 100, 80, 40));
        document.SelectOnly(shape);

        var history = new UndoStack(document);

        Harness.Press(window, Key.Right, RawInputModifiers.Alt);
        Assert.True(history.CanUndo);

        history.Undo();
        Assert.Equal(100, document.Shapes[0].Bounds.X, 2);
    }

    [AvaloniaFact]
    public void WithNothingSelectedNothingHappens()
    {
        var (window, canvas) = Harness.Editor(600, 400);
        var document = canvas.Document;

        var shape = Harness.Box(document, new Rect(100, 100, 80, 40));
        document.ClearSelection();

        Harness.Press(window, Key.Right, RawInputModifiers.Alt);

        Assert.Equal(100, shape.Bounds.X, 2);
    }

    [AvaloniaFact]
    public void ADroppedShapeJoinsWhateverItWasNudgedInto()
    {
        var (window, canvas) = Harness.Editor(700, 500);
        var document = canvas.Document;

        canvas.Grid.SnapToGrid = true;
        canvas.Grid.Size = 10;

        var box = (ContainerShape)ShapeFactory.Create(ShapeKind.ContainerBox, new Rect(200, 60, 300, 300));
        document.Add(box);

        // Just outside the container, one step from being inside it.
        var wanderer = Harness.Box(document, new Rect(110, 150, 80, 40), "moving in");
        document.SelectOnly(wanderer);

        Assert.Null(wanderer.Container);

        for (var i = 0; i < 12; i++)
            Harness.Press(window, Key.Right, RawInputModifiers.Alt);

        Assert.Same(box, wanderer.Container);
    }
}
