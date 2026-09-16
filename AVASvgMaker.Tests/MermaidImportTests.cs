using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using Xunit;

namespace AVASvgMaker.Tests;

/// <summary>
/// Reading a Mermaid flowchart back into a drawing. Mermaid says nothing about where anything
/// is, so what is checked here is what it does say: the shapes, what they are called, and what
/// joins what. Where it all ends up is the layout's business and is checked as such.
/// </summary>
public class MermaidImportTests
{
    private static IEnumerable<DiagramShape> Boxes(DiagramPage page) =>
        page.Shapes.Where(shape => shape is not ConnectorShape && !shape.IsContainer);

    private static DiagramShape Find(DiagramPage page, string text) =>
        Boxes(page).First(shape => shape.Text == text);

    /// <summary>Every line, as "from -> to" and what it says, so two drawings can be compared.</summary>
    private static string[] Wiring(DiagramPage page) => page.Shapes
        .OfType<ConnectorShape>()
        .Select(line =>
            $"{line.StartShape?.Text} -> {line.EndShape?.Text}" +
            (string.IsNullOrEmpty(line.Text) ? "" : $" [{line.Text}]"))
        .OrderBy(text => text, StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    /// Out and back. Positions are not expected to survive - Mermaid does not carry them - but
    /// the shapes, their words and the lines between them all should.
    /// </summary>
    [AvaloniaFact]
    public void ARoundTripKeepsTheDrawingsMeaning()
    {
        var document = Harness.Page(900, 700);

        var start = Harness.Box(document, new Rect(60, 60, 140, 50), "Order in", ShapeKind.RoundedRectangle);
        var check = Harness.Box(document, new Rect(60, 200, 160, 80), "In stock?", ShapeKind.Diamond);
        var pick = Harness.Box(document, new Rect(60, 360, 140, 50), "Pick it");
        var store = Harness.Box(document, new Rect(300, 360, 140, 50), "Records", ShapeKind.Cylinder);

        var yes = Harness.Join(document, check, 2, pick, 0);
        yes.Text = "yes";
        yes.EndCap = EndCapStyle.Arrow;

        var save = Harness.Join(document, pick, 1, store, 3);
        save.Text = "save";
        save.EndCap = EndCapStyle.Arrow;
        save.StrokeStyle = StrokeStyle.Dashed;

        var first = Harness.Join(document, start, 2, check, 0);
        first.EndCap = EndCapStyle.Arrow;

        var code = MermaidExporter.Export(document.CurrentPage).Code;
        var back = MermaidImporter.Read(code, 900, 700);

        Assert.Equal(4, back.Count);

        Assert.Equal(
            Boxes(document.CurrentPage).Select(s => $"{s.Kind}:{s.Text}").OrderBy(t => t, StringComparer.Ordinal),
            Boxes(back.Page).Select(s => $"{s.Kind}:{s.Text}").OrderBy(t => t, StringComparer.Ordinal));

        Assert.Equal(Wiring(document.CurrentPage), Wiring(back.Page));

        // The dashed line came back dashed.
        Assert.Equal(StrokeStyle.Dashed,
            back.Page.Shapes.OfType<ConnectorShape>().First(l => l.Text == "save").StrokeStyle);
    }

    [AvaloniaFact]
    public void TheBracketsChooseTheShape()
    {
        var page = MermaidImporter.Read("""
            flowchart TD
                a["plain"]
                b("round")
                c(("circle"))
                d{"choice"}
                e{{"six sides"}}
                f[/"leaning"/]
                g[("barrel")]
                h[["twice"]]
                i>"a note"]
                j(["stadium"])
            """).Page;

        Assert.Equal(ShapeKind.Rectangle, Find(page, "plain").Kind);
        Assert.Equal(ShapeKind.RoundedRectangle, Find(page, "round").Kind);
        Assert.Equal(ShapeKind.Ellipse, Find(page, "circle").Kind);
        Assert.Equal(ShapeKind.Diamond, Find(page, "choice").Kind);
        Assert.Equal(ShapeKind.Hexagon, Find(page, "six sides").Kind);
        Assert.Equal(ShapeKind.Parallelogram, Find(page, "leaning").Kind);
        Assert.Equal(ShapeKind.Cylinder, Find(page, "barrel").Kind);
        Assert.Equal(ShapeKind.PredefinedProcess, Find(page, "twice").Kind);
        Assert.Equal(ShapeKind.Document, Find(page, "a note").Kind);
        Assert.Equal(ShapeKind.RoundedRectangle, Find(page, "stadium").Kind);
    }

    [AvaloniaFact]
    public void AChainIsReadAsSeveralLines()
    {
        var page = MermaidImporter.Read("""
            flowchart LR
                a[One] --> b[Two] --> c[Three]
            """).Page;

        Assert.Equal(3, Boxes(page).Count());
        Assert.Equal(["One -> Two", "Two -> Three"], Wiring(page));
    }

    [AvaloniaFact]
    public void BothWaysOfLabellingALineAreRead()
    {
        var page = MermaidImporter.Read("""
            flowchart TD
                a[Ask] -->|yes| b[Do it]
                a -- no --> c[Stop]
            """).Page;

        Assert.Equal(["Ask -> Do it [yes]", "Ask -> Stop [no]"], Wiring(page));
    }

    [AvaloniaFact]
    public void TheArrowSaysHowTheLineIsDrawn()
    {
        var page = MermaidImporter.Read("""
            flowchart TD
                a[A] --> b[B]
                a -.-> c[C]
                a ==> d[D]
                a --- e[E]
                a <--> f[F]
            """).Page;

        ConnectorShape To(string text) =>
            page.Shapes.OfType<ConnectorShape>().First(l => l.EndShape?.Text == text);

        Assert.Equal(EndCapStyle.Arrow, To("B").EndCap);
        Assert.Equal(StrokeStyle.Dashed, To("C").StrokeStyle);
        Assert.Equal(4, To("D").StrokeThickness);
        Assert.Equal(EndCapStyle.None, To("E").EndCap);
        Assert.Equal(EndCapStyle.Arrow, To("F").StartCap);
        Assert.Equal(EndCapStyle.Arrow, To("F").EndCap);
    }

    [AvaloniaFact]
    public void ASubgraphBecomesAContainerRoundWhatItHolds()
    {
        var result = MermaidImporter.Read("""
            flowchart TD
                subgraph s1["Fulfilment"]
                    a[Pick]
                    b[Pack]
                end
                a --> b
                b --> c[Ship]
            """);

        var box = result.Page.Shapes.First(shape => shape.IsContainer);

        Assert.Equal("Fulfilment", box.Text);

        var pick = Find(result.Page, "Pick");
        var pack = Find(result.Page, "Pack");
        var ship = Find(result.Page, "Ship");

        Assert.Same(box, pick.Container);
        Assert.Same(box, pack.Container);
        Assert.Null(ship.Container);

        // It is drawn round them, and behind them.
        Assert.True(box.Bounds.Contains(pick.Bounds), "the container does not hold Pick");
        Assert.True(box.Bounds.Contains(pack.Bounds), "the container does not hold Pack");
        Assert.True(result.Page.Shapes.IndexOf(box) < result.Page.Shapes.IndexOf(pick));
    }

    /// <summary>What arrives has places, which is the whole reason this needed a layout pass.</summary>
    [AvaloniaFact]
    public void WhatArrivesIsLaidOutRatherThanPiledUp()
    {
        var page = MermaidImporter.Read("""
            flowchart TD
                a[Start] --> b[Middle]
                a --> c[Other]
                b --> d[End]
                c --> d
            """, 900, 700).Page;

        var boxes = Boxes(page).ToList();

        foreach (var one in boxes)
        foreach (var two in boxes.Where(other => !ReferenceEquals(other, one)))
            Assert.False(one.Bounds.Intersects(two.Bounds), $"{one.Text} is on top of {two.Text}");

        // And it runs the way it was asked to.
        Assert.True(Find(page, "Start").Bounds.Center.Y < Find(page, "Middle").Bounds.Center.Y);
        Assert.True(Find(page, "Middle").Bounds.Center.Y < Find(page, "End").Bounds.Center.Y);
        Assert.Equal(Find(page, "Middle").Bounds.Center.Y, Find(page, "Other").Bounds.Center.Y, 1);
    }

    /// <summary>Pasted out of a README, fences, comments, prose and all.</summary>
    [AvaloniaFact]
    public void TextPastedOutOfAReadmeIsStillRead()
    {
        var page = MermaidImporter.Read("""
            Here is the flow we agreed:

            ```mermaid
            %% the happy path
            flowchart LR
                a[Raise] --> b[Review]
            ```
            """).Page;

        Assert.Equal(2, Boxes(page).Count());
        Assert.Equal(["Raise -> Review"], Wiring(page));
    }

    [AvaloniaFact]
    public void AStyleLinePaintsTheShape()
    {
        var page = MermaidImporter.Read("""
            flowchart TD
                a[Painted] --> b[Plain]
                style a fill:#b0c4de,stroke:#2d6cdf,stroke-width:3px
            """).Page;

        var painted = Find(page, "Painted");

        Assert.Equal(Color.Parse("#b0c4de"), painted.Fill);
        Assert.Equal(Color.Parse("#2d6cdf"), painted.Stroke);
        Assert.Equal(3, painted.StrokeThickness);
    }

    [AvaloniaFact]
    public void SomethingThatIsNotAFlowchartSaysSo()
    {
        var result = MermaidImporter.Read("""
            sequenceDiagram
                Alice->>John: Hello John
            """);

        Assert.Equal(0, result.Count);
        Assert.Contains("Found no flowchart", result.Summary);
    }

    /// <summary>A name used again is the same shape, not another one beside it.</summary>
    [AvaloniaFact]
    public void ANameUsedTwiceIsOneShape()
    {
        var page = MermaidImporter.Read("""
            flowchart TD
                a[Hub] --> b[One]
                a --> c[Two]
                a --> d[Three]
            """).Page;

        Assert.Equal(4, Boxes(page).Count());
        Assert.Equal(3, page.Shapes.OfType<ConnectorShape>().Count(l => l.StartShape?.Text == "Hub"));
    }
}
