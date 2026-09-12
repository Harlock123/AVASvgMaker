using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using AVASvgMaker.Views;
using Xunit;

namespace AVASvgMaker.Tests;

/// <summary>
/// A callout's tail, which is the whole point of a callout: the report was that the tail was
/// baked into the drawing and could not be aimed at anything.
///
/// What matters is not where the tail is drawn but whether it stays aimed - a tail that only
/// looks as though it points at a shape drifts off it the moment the shape is moved, and
/// nothing on screen says so until it does.
/// </summary>
public class CalloutTests
{
    private static readonly ShapeKind[] Kinds =
    [
        ShapeKind.SpeechBubble, ShapeKind.OvalCallout,
        ShapeKind.RectangularCallout, ShapeKind.ThoughtBubble
    ];

    public static TheoryData<ShapeKind> EveryKind()
    {
        var data = new TheoryData<ShapeKind>();

        foreach (var kind in Kinds)
            data.Add(kind);

        return data;
    }

    [AvaloniaTheory]
    [MemberData(nameof(EveryKind))]
    public void ACalloutIsAShapeWithATailRatherThanAFixedStencil(ShapeKind kind)
    {
        var shape = ShapeFactory.Create(kind, new Rect(0, 0, 100, 100));

        Assert.IsType<CalloutShape>(shape);
    }

    [AvaloniaTheory]
    [MemberData(nameof(EveryKind))]
    public void AndItsTailStartsOutsideTheBubble(ShapeKind kind)
    {
        var callout = (CalloutShape)ShapeFactory.Create(kind, new Rect(100, 100, 160, 100));

        // Outside the box the bubble fills, or it would be pointing at itself.
        Assert.False(callout.Bounds.Contains(callout.TailTip));
    }

    [AvaloniaFact]
    public void TheBubbleFillsTheWholeShapeAndTheTailReachesPastIt()
    {
        // The tail used to be part of a fixed outline, which left the bubble squashed into the
        // top of its own box with a spike taking up the rest.
        var callout = (CalloutShape)ShapeFactory.Create(ShapeKind.SpeechBubble, new Rect(0, 0, 200, 100));

        var geometry = callout.CreateGeometry();

        Assert.True(geometry.FillContains(new Point(100, 90)), "the bubble should reach the bottom of its box");
        Assert.True(geometry.Bounds.Bottom > 100, "the tail should reach past the box");
    }

    [AvaloniaFact]
    public void MovingTheTailMovesWhereTheShapeIsDrawn()
    {
        var callout = (CalloutShape)ShapeFactory.Create(ShapeKind.SpeechBubble, new Rect(0, 0, 200, 100));
        var before = callout.CreateGeometry().Bounds;

        callout.Tail = new Point(1.6, 0.5);

        var after = callout.CreateGeometry().Bounds;

        Assert.True(after.Right > before.Right, "the tail now reaches out to the right");
        Assert.Equal(320, after.Right, 1);
    }

    [AvaloniaFact]
    public void ATailHeldAsFractionsTravelsWithItsBubble()
    {
        var callout = (CalloutShape)ShapeFactory.Create(ShapeKind.SpeechBubble, new Rect(0, 0, 200, 100));
        callout.Tail = new Point(0.5, 2);

        Assert.Equal(new Point(100, 200), callout.TailTip);

        callout.Bounds = new Rect(300, 400, 200, 100);

        Assert.Equal(new Point(400, 600), callout.TailTip);
    }

    [AvaloniaFact]
    public void AThoughtTrailsBubblesRatherThanASpike()
    {
        var thought = (CalloutShape)ShapeFactory.Create(ShapeKind.ThoughtBubble, new Rect(0, 0, 200, 100));
        var spoken = (CalloutShape)ShapeFactory.Create(ShapeKind.SpeechBubble, new Rect(0, 0, 200, 100));

        Assert.NotNull(thought.UnitDetail);
        Assert.Null(spoken.UnitDetail);

        // Its body is the plain bubble: nothing is cut out of it for a tail.
        Assert.Equal(0, thought.CreateGeometry().Bounds.Top, 1);
        Assert.Equal(100, thought.CreateGeometry().Bounds.Bottom, 1);
    }

    #region Pinning

    private static (DrawingCanvas Canvas, CalloutShape Callout, DiagramShape Target) Scene()
    {
        var (_, canvas) = Harness.Editor(800, 600);

        var callout = (CalloutShape)ShapeFactory.Create(ShapeKind.SpeechBubble, new Rect(100, 100, 160, 100));
        var target = ShapeFactory.Create(ShapeKind.Rectangle, new Rect(400, 300, 120, 80));

        canvas.Document.Add(callout);
        canvas.Document.Add(target);
        canvas.Document.SelectOnly(callout);

        return (canvas, callout, target);
    }

    [AvaloniaFact]
    public void TheTailCanBeDraggedOntoAnotherShapesConnectionPoint()
    {
        var (canvas, callout, target) = Scene();
        var port = target.ConnectionPoints[0];

        Harness.Drag(canvas, callout.TailTip, port);

        Assert.Same(target, callout.TailShape);
        Assert.True(callout.TailPort >= 0);
        Assert.Equal(port, callout.TailTip);
    }

    [AvaloniaFact]
    public void AndItThenFollowsThatShapeAboutThePage()
    {
        // The proof that it is really pinned rather than merely touching.
        var (canvas, callout, target) = Scene();

        Harness.Drag(canvas, callout.TailTip, target.ConnectionPoints[0]);

        canvas.Document.SelectOnly(target);
        Harness.MoveShape(canvas, target, new Point(600, 450));

        Assert.Equal(target.ConnectionPoints[callout.TailPort], callout.TailTip);
    }

    [AvaloniaFact]
    public void ATailDroppedOnBarePageIsSimplyLeftThere()
    {
        var (canvas, callout, _) = Scene();

        Harness.Drag(canvas, callout.TailTip, new Point(300, 500));

        Assert.Null(callout.TailShape);
        Assert.Equal(new Point(300, 500), callout.TailTip);
    }

    [AvaloniaFact]
    public void ACalloutIsNeverPinnedToItself()
    {
        var (canvas, callout, _) = Scene();

        // Dragged back onto its own bubble, where its own connection points are.
        Harness.Drag(canvas, callout.TailTip, callout.ConnectionPoints[2]);

        Assert.Null(callout.TailShape);
    }

    [AvaloniaFact]
    public void DeletingWhatACalloutPointsAtLeavesItPointingWhereItWas()
    {
        var (canvas, callout, target) = Scene();

        Harness.Drag(canvas, callout.TailTip, target.ConnectionPoints[0]);

        var pointed = callout.TailTip;
        canvas.Document.Remove(target);

        Assert.Null(callout.TailShape);
        Assert.Equal(pointed.X, callout.TailTip.X, 3);
        Assert.Equal(pointed.Y, callout.TailTip.Y, 3);
    }

    [AvaloniaFact]
    public void AimingTheTailIsOneStepToUndo()
    {
        var (canvas, callout, _) = Scene();
        var history = new UndoStack(canvas.Document);

        var before = callout.TailTip;
        Harness.Drag(canvas, before, new Point(300, 500));
        Assert.NotEqual(before, callout.TailTip);

        // One step for the whole drag, not one per pointer move.
        Assert.True(history.CanUndo);
        history.Undo();

        Assert.Equal(before, ((CalloutShape)canvas.Document.Shapes[0]).TailTip);
        Assert.False(history.CanUndo);
    }

    #endregion

    #region On disk

    [AvaloniaFact]
    public void AnAimedTailSurvivesBeingSavedAndOpened()
    {
        var document = Harness.Page(800, 600);

        var callout = (CalloutShape)ShapeFactory.Create(ShapeKind.OvalCallout, new Rect(100, 100, 160, 100));
        var target = ShapeFactory.Create(ShapeKind.Rectangle, new Rect(400, 300, 120, 80));

        document.Add(callout);
        document.Add(target);

        callout.TailShape = target;
        callout.TailPort = 2;

        var read = DiagramFile.FromJson(DiagramFile.ToJson(document));
        var opened = read.Pages[0].Shapes.OfType<CalloutShape>().Single();

        Assert.Same(read.Pages[0].Shapes[1], opened.TailShape);
        Assert.Equal(2, opened.TailPort);
    }

    [AvaloniaFact]
    public void AndSoDoesOneThatIsSimplyPointedSomewhere()
    {
        var document = Harness.Page(800, 600);

        var callout = (CalloutShape)ShapeFactory.Create(ShapeKind.RectangularCallout, new Rect(100, 100, 160, 100));
        callout.Tail = new Point(-0.4, 1.8);
        document.Add(callout);

        var read = DiagramFile.FromJson(DiagramFile.ToJson(document));
        var opened = read.Pages[0].Shapes.OfType<CalloutShape>().Single();

        Assert.Null(opened.TailShape);
        Assert.Equal(-0.4, opened.Tail.X, 3);
        Assert.Equal(1.8, opened.Tail.Y, 3);
    }

    [AvaloniaFact]
    public void ACalloutCopiedOnItsOwnKeepsTheTailWithoutWhatItPointedAt()
    {
        var document = Harness.Page(800, 600);

        var callout = (CalloutShape)ShapeFactory.Create(ShapeKind.SpeechBubble, new Rect(100, 100, 160, 100));
        var target = ShapeFactory.Create(ShapeKind.Rectangle, new Rect(400, 300, 120, 80));

        document.Add(callout);
        document.Add(target);

        callout.TailShape = target;
        callout.TailPort = 0;

        var json = ShapeClipboard.Copy(document, [callout]);
        var pasted = DiagramFile.FromJson(json).Pages[0].Shapes.OfType<CalloutShape>().Single();

        Assert.Null(pasted.TailShape);
    }

    [AvaloniaFact]
    public void AFileFromBeforeTailsCouldBeAimedStillOpens()
    {
        // Version 18 drew the tail into the outline and had no way to say where it pointed.
        // Such a callout comes back pointing where every callout used to point.
        var document = Harness.Page(800, 600);
        document.Add(ShapeFactory.Create(ShapeKind.SpeechBubble, new Rect(100, 100, 160, 100)));

        var json = DiagramFile.ToJson(document)
            .Replace($"\"version\": {DiagramFile.CurrentVersion}", "\"version\": 18");

        // Strip what version 18 could not have written.
        json = System.Text.RegularExpressions.Regex.Replace(json, @"\s*""tail"": \[[^\]]*\],", string.Empty);

        var opened = DiagramFile.FromJson(json).Pages[0].Shapes.OfType<CalloutShape>().Single();

        Assert.Equal(new Point(0.1, 1.45), opened.Tail);
    }

    #endregion

    [AvaloniaFact]
    public void TheTailGoesOutToVisioAsPartOfTheShape()
    {
        // Visio has no idea what a callout of ours is, so the tail travels as geometry. What
        // matters is that it is still there on the other side rather than a bare bubble.
        var page = new DiagramPage("Sheet") { Width = 800, Height = 600 };

        var callout = (CalloutShape)ShapeFactory.Create(ShapeKind.SpeechBubble, new Rect(100, 60, 160, 100));
        callout.Tail = new Point(0.5, 2.2);
        page.Shapes.Add(callout);

        var document = new DiagramDocument();
        document.SetPages([page]);

        using var stream = new System.IO.MemoryStream();
        VisioExporter.Write(document, stream, "Test");
        stream.Position = 0;

        var back = Assert.Single(VisioImporter.Read(stream).Document.Pages[0].Shapes);

        // Visio sizes a shape by its box, and the tail hangs outside that box at both ends of
        // the trip - so it is the ink, not the bounds, that says whether the tail survived.
        var ink = back.CreateGeometry().Bounds;

        Assert.Equal(100, back.Bounds.Height, 1);
        Assert.True(ink.Height > 190, $"the tail came back squashed to {ink.Height}");
    }

    [AvaloniaFact]
    public void TheTailIsDrawnIntoEveryExportedPicture()
    {
        var document = Harness.Page(400, 300);

        var callout = (CalloutShape)ShapeFactory.Create(ShapeKind.SpeechBubble, new Rect(100, 60, 160, 100));
        callout.Tail = new Point(0.5, 2.2);
        document.Add(callout);

        var svg = SvgExporter.Export(document);

        // The tip is 120 below the top of a 100-tall bubble that starts at 60.
        Assert.Contains("280", svg);
    }
}
