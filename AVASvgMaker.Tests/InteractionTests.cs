using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using AVASvgMaker.Views;
using Xunit;

namespace AVASvgMaker.Tests;

/// <summary>
/// The things a user does with a pointer, driven with real pointer events through the real
/// canvas. Calling the method a drag would have called proves rather less.
/// </summary>
public class InteractionTests
{
    [AvaloniaFact]
    public void ClickingAMemberOfAGroupPicksTheGroupAndDragsItWhole()
    {
        var (_, canvas) = Harness.Editor(700, 460);
        var document = canvas.Document;

        var a = Harness.Box(document, new Rect(100, 100, 100, 60), "A");
        var b = Harness.Box(document, new Rect(300, 200, 100, 60), "B");
        var loose = Harness.Box(document, new Rect(100, 320, 80, 40), "C");

        document.Group([a, b]);
        document.ClearSelection();

        Harness.Click(canvas, a.Bounds.Center);
        Assert.Equal(2, document.Selection.Count);

        Harness.Click(canvas, loose.Bounds.Center);
        Assert.Single(document.Selection);

        // Dragging one member takes the other with it.
        Harness.Click(canvas, b.Bounds.Center);
        var before = a.Bounds.X;

        Harness.Drag(canvas, b.Bounds.Center,
            new Point(b.Bounds.Center.X + 40, b.Bounds.Center.Y));

        Assert.Equal(before + 40, a.Bounds.X, 1);
    }

    [AvaloniaFact]
    public void ABendDroppedBackOnTheLineIsRemoved()
    {
        var (window, canvas) = Harness.Editor(700, 420);
        var document = canvas.Document;

        Harness.Box(document, new Rect(60, 180, 100, 60));
        Harness.Box(document, new Rect(500, 180, 100, 60));

        var line = new ConnectorShape(new Point(160, 210), new Point(500, 210))
        {
            Routing = ConnectorRouting.Straight
        };

        document.Add(line);

        void Bend(double y)
        {
            line.Waypoints = [new Point(330, y)];
            document.SelectOnly(line);
            Harness.Settle(window);
        }

        Bend(90);
        Assert.Equal(3, line.Path.Count);

        // Moved elsewhere off the line, it stays.
        Harness.Drag(canvas, new Point(330, 90), new Point(330, 140));
        Assert.Equal(3, line.Path.Count);
        Assert.Equal(140, line.Path[1].Y, 1);

        // Dropped back on the line between its neighbours, it goes.
        Harness.Drag(canvas, new Point(330, 140), new Point(330, 210));
        Assert.Equal(2, line.Path.Count);

        // Within the tolerance counts as on the line; beyond it does not.
        Bend(90);
        Harness.Drag(canvas, new Point(330, 90), new Point(330, 214));
        Assert.Equal(2, line.Path.Count);

        Bend(90);
        Harness.Drag(canvas, new Point(330, 90), new Point(330, 230));
        Assert.Equal(3, line.Path.Count);

        // Double-clicking is still the other way to remove one.
        Bend(90);
        Assert.True(canvas.TryRemoveBend(new Point(330, 90)));
        Assert.Equal(2, line.Path.Count);
    }

    [AvaloniaFact]
    public void DraggingTheLineBetweenTwoLanesMovesIt()
    {
        var (window, canvas) = Harness.Editor(700, 500);
        var document = canvas.Document;

        var pool = (ContainerShape)ShapeFactory.Create(ShapeKind.Pool, new Rect(40, 40, 620, 360));
        document.Add(pool);

        foreach (var _ in Enumerable.Range(0, 3))
        {
            var lane = (ContainerShape)ShapeFactory.Create(ShapeKind.Lane, pool.Body);
            document.Add(lane);
            document.Adopt(lane, pool);
        }

        document.NormaliseOrder();
        document.LayoutContainers();
        document.ClearSelection();
        Harness.Settle(window);

        var lanes = document.LanesOf(pool);
        var boundary = lanes[0].Bounds.Bottom;
        var middle = pool.Body.Center.X;

        Harness.Drag(canvas, new Point(middle, boundary), new Point(middle, boundary + 60));

        Assert.Equal(boundary + 60, lanes[0].Bounds.Bottom, 1);
        Assert.Empty(document.Selection);

        // A click in the middle of a lane is not a grab at its edge.
        var height = lanes[0].Bounds.Height;
        var inside = lanes[0].Bounds.Center.Y;

        Harness.Drag(canvas, new Point(middle, inside), new Point(middle, inside + 40));

        Assert.Equal(height, lanes[0].Bounds.Height, 2);
    }

    [AvaloniaFact]
    public void DraggingAShapeLinesItUpWithTheOthers()
    {
        var (_, canvas) = Harness.Editor(700, 480);
        var document = canvas.Document;

        var anchor = Harness.Box(document, new Rect(200, 100, 120, 60), "anchor");
        var mover = Harness.Box(document, new Rect(200, 300, 80, 40), "mover");

        document.ClearSelection();

        // Four pixels out of line with the anchor's left edge: it snaps on.
        Harness.MoveShape(canvas, mover, new Point(204, 300));
        Assert.Equal(200, mover.Bounds.X, 1);

        // Middles line up too.
        Harness.MoveShape(canvas, mover, new Point(anchor.Bounds.Center.X - 40 + 5, 320));
        Assert.Equal(anchor.Bounds.Center.X, mover.Bounds.Center.X, 1);

        // Well out of line, it is left where it was put.
        Harness.MoveShape(canvas, mover, new Point(140, 300));
        Assert.Equal(140, mover.Bounds.X, 1);

        // Turned off, nothing lines up.
        canvas.SmartGuides = false;
        Harness.MoveShape(canvas, mover, new Point(204, 300));
        Assert.Equal(204, mover.Bounds.X, 1);
        canvas.SmartGuides = true;

        // The grid still catches the axis nothing lined up on.
        canvas.Grid.SnapToGrid = true;
        canvas.Grid.Size = 10;

        Harness.MoveShape(canvas, mover, new Point(204, 337));

        Assert.Equal(200, mover.Bounds.X, 1);
        Assert.Equal(0, mover.Bounds.Y % 10, 1);
    }

    [AvaloniaFact]
    public void APageTabIsClickedToTurnAndDraggedToReorder()
    {
        var (window, canvas) = Harness.Editor(600, 400);
        var document = canvas.Document;

        Harness.Box(document, new Rect(40, 40, 80, 40), "one");
        document.AddPage();
        document.AddPage();
        document.RenamePage(2, "Summary");
        document.PageIndex = 0;
        document.ClearSelection();
        Harness.Settle(window);

        var strip = window.GetVisualDescendants().OfType<PageTabStrip>().Single();

        Border[] Tabs() => strip.GetLogicalDescendants()
            .OfType<Border>()
            .Where(border => border.Child is TextBlock)
            .ToArray();

        string Order() => string.Join(", ", document.Pages.Select(page => page.Name));

        Assert.Equal(3, Tabs().Length);
        Assert.Equal("Page 1, Page 2, Summary", Order());

        // Clicking the third tab turns to it.
        Harness.PressOn(Tabs()[2]);
        Assert.Equal(2, document.PageIndex);
        Assert.Equal("Summary", document.CurrentPage.Name);

        // Its shapes come with it.
        document.PageIndex = 0;
        Assert.Single(document.Shapes);
    }

    [AvaloniaFact]
    public void StretchingASelectionThroughItsHandlesDoesNotCompound()
    {
        var (window, canvas) = Harness.Editor(900, 700);
        var document = canvas.Document;

        var one = Harness.Box(document, new Rect(100, 100, 100, 50));
        var two = Harness.Box(document, new Rect(300, 200, 100, 50));

        document.SetSelection([one, two]);
        Harness.Settle(window);

        // The handles sit on the plain union, so the corner is the union's corner.
        var union = ShapeClipboard.Union(document.Selection);

        Harness.Drag(canvas, new Point(union.Right, union.Bottom),
            new Point(union.X + 600, union.Y + 300));

        Assert.Equal(100, one.Bounds.X, 1);
        Assert.Equal(200, one.Bounds.Width, 2);
        Assert.Equal(500, two.Bounds.X, 2);

        // A second drag works from where the first left off rather than scaling it again.
        var settled = ShapeClipboard.Union(document.Selection);

        Harness.Drag(canvas, new Point(settled.Right, settled.Bottom),
            new Point(settled.X + 300, settled.Y + 150));

        Assert.Equal(300, ShapeClipboard.Union(document.Selection).Width, 4);
    }
}
