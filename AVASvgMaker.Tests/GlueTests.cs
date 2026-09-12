using Avalonia;
using Avalonia.Headless.XUnit;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using AVASvgMaker.Views;
using Xunit;

namespace AVASvgMaker.Tests;

/// <summary>
/// Whether a connector drawn near a shape is actually stuck to it.
///
/// The test that matters is not what it looks like but what happens when the shape moves: a
/// line that only appears attached stays behind, and nothing on screen says so until it does.
/// </summary>
public class GlueTests
{
    private static (DrawingCanvas Canvas, DiagramShape A, DiagramShape B) Two(
        ShapeKind kind = ShapeKind.Rectangle)
    {
        var (_, canvas) = Harness.Editor(800, 600);

        var a = ShapeFactory.Create(kind, new Rect(100, 200, 140, 100));
        var b = ShapeFactory.Create(kind, new Rect(500, 200, 140, 100));

        canvas.Document.Add(a);
        canvas.Document.Add(b);
        canvas.Document.ClearSelection();

        return (canvas, a, b);
    }

    /// <summary>Draws a connector with the connector tool, from one point to another.</summary>
    private static ConnectorShape Draw(DrawingCanvas canvas, Point from, Point to)
    {
        canvas.Tool = EditorTool.Connector;
        Harness.Drag(canvas, from, to);
        canvas.Tool = EditorTool.Select;

        return (ConnectorShape)canvas.Document.Shapes[^1];
    }

    [AvaloniaFact]
    public void ALineDrawnFromInsideAShapeToInsideAnotherIsStuckToBoth()
    {
        var (canvas, a, b) = Two();
        var line = Draw(canvas, a.Bounds.Center, b.Bounds.Center);

        Assert.Same(a, line.StartShape);
        Assert.Same(b, line.EndShape);
    }

    [AvaloniaFact]
    public void ALineDroppedJustOutsideAShapeStillFindsIt()
    {
        // The report: it looks attached, and it is not. A few pixels past the outline used to
        // glue to nothing at all.
        var (canvas, a, b) = Two();

        var justOutside = new Point(b.Bounds.Left - 3, b.Bounds.Center.Y);
        var line = Draw(canvas, a.Bounds.Center, justOutside);

        Assert.Same(b, line.EndShape);
    }

    [AvaloniaFact]
    public void AndTheLineThenFollowsTheShapeWhenItMoves()
    {
        // The proof that it is really glued rather than merely touching.
        var (canvas, a, b) = Two();

        var line = Draw(canvas, a.Bounds.Center, new Point(b.Bounds.Left - 3, b.Bounds.Center.Y));
        var before = line.ResolvedEnd;

        b.Bounds = new Rect(b.Bounds.X, b.Bounds.Y + 120, b.Bounds.Width, b.Bounds.Height);

        Assert.NotEqual(before.Y, line.ResolvedEnd.Y, 1);
    }

    [AvaloniaFact]
    public void JustOutsideAPointedShapeFindsItToo()
    {
        // A shape is only as big as its outline, and a diamond's is a long way inside the box
        // round it. Just past the tip is where a line aimed at a diamond lands.
        var (canvas, a, b) = Two(ShapeKind.Diamond);

        var pastTheTip = new Point(b.Bounds.Left - 3, b.Bounds.Center.Y);

        Assert.False(b.HitTest(pastTheTip), "it really is outside the diamond");
        Assert.Same(b, Draw(canvas, a.Bounds.Center, pastTheTip).EndShape);
    }

    [AvaloniaFact]
    public void ButTheEmptyCornerOfItsBoxDoesNot()
    {
        // The far corner of a diamond's box is not the diamond, and is not near any of its
        // points either - so it goes on meaning bare page, which is what it looks like.
        var (canvas, a, b) = Two(ShapeKind.Diamond);

        Assert.Null(Draw(canvas, a.Bounds.Center,
            new Point(b.Bounds.Left + 6, b.Bounds.Top + 6)).EndShape);
    }

    [AvaloniaFact]
    public void ALineDroppedWellClearOfEverythingIsStuckToNothing()
    {
        // The other half of it: dropping on bare page has to go on meaning bare page.
        var (canvas, a, _) = Two();
        var line = Draw(canvas, a.Bounds.Center, new Point(380, 500));

        Assert.Null(line.EndShape);
    }

    [AvaloniaFact]
    public void ALineBegunJustOutsideAShapeIsStuckToItToo()
    {
        var (canvas, a, b) = Two();

        var justOutside = new Point(a.Bounds.Right + 3, a.Bounds.Center.Y);
        var line = Draw(canvas, justOutside, b.Bounds.Center);

        Assert.Same(a, line.StartShape);
    }

    [AvaloniaFact]
    public void DraggingAnEndNearAShapeGluesItThere()
    {
        // The same question is asked when an existing end is dragged, not only when one is drawn.
        var (canvas, a, b) = Two();
        var line = Draw(canvas, a.Bounds.Center, new Point(380, 500));

        Assert.Null(line.EndShape);

        Harness.Click(canvas, line.ResolvedStart);
        canvas.Document.SetSelection([line]);

        Harness.Drag(canvas, line.ResolvedEnd, new Point(b.Bounds.Left - 3, b.Bounds.Center.Y));

        Assert.Same(b, line.EndShape);
    }

    [AvaloniaFact]
    public void SnappingReachesAsFarAsAGridStep()
    {
        // Near enough is a grid step, which is what the pointer is already snapped to.
        var (canvas, a, b) = Two();

        canvas.Grid.Size = 20;

        var nearly = new Point(b.Bounds.Left - 14, b.Bounds.Center.Y);
        Assert.Same(b, Draw(canvas, a.Bounds.Center, nearly).EndShape);

        var tooFar = new Point(b.Bounds.Left - 90, b.Bounds.Center.Y + 200);
        Assert.Null(Draw(canvas, a.Bounds.Center, tooFar).EndShape);
    }
}
