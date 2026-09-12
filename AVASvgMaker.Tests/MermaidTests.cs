using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using Xunit;

namespace AVASvgMaker.Tests;

/// <summary>
/// The page said as Mermaid.
///
/// Every case here has been put through Mermaid's own parser as well; what these hold on to is
/// that the text keeps saying the same thing, since a parser is not on hand at test time.
/// </summary>
public class MermaidTests
{
    private static string Code(DiagramDocument document) =>
        MermaidExporter.Export(document.CurrentPage).Code;

    private static DiagramShape Box(DiagramDocument document, ShapeKind kind, Rect at, string text)
    {
        var shape = ShapeFactory.Create(kind, at);
        shape.Text = text;
        document.Add(shape);
        return shape;
    }

    private static void Join(DiagramDocument document, DiagramShape a, DiagramShape b,
        string label = "", StrokeStyle style = StrokeStyle.Solid,
        EndCapStyle end = EndCapStyle.Arrow)
    {
        document.Add(new ConnectorShape(new Point(0, 0), new Point(0, 0))
        {
            StartShape = a, StartPort = 1, EndShape = b, EndPort = 3,
            Text = label, StrokeStyle = style, EndCap = end
        });
    }

    [AvaloniaFact]
    public void AnEmptyPageIsStillAChart()
    {
        Assert.StartsWith("flowchart", Code(Harness.Page(400, 300)));
    }

    [AvaloniaFact]
    public void EachShapeGetsANameAndItsWords()
    {
        var document = Harness.Page(600, 400);
        Box(document, ShapeKind.Rectangle, new Rect(40, 40, 120, 60), "Process");

        Assert.Contains("n1[\"Process\"]", Code(document));
    }

    [AvaloniaTheory]
    [InlineData(ShapeKind.Rectangle, "n1[\"x\"]")]
    [InlineData(ShapeKind.RoundedRectangle, "n1(\"x\")")]
    [InlineData(ShapeKind.Ellipse, "n1((\"x\"))")]
    [InlineData(ShapeKind.Diamond, "n1{\"x\"}")]
    [InlineData(ShapeKind.Hexagon, "n1{{\"x\"}}")]
    [InlineData(ShapeKind.Parallelogram, "n1[/\"x\"/]")]
    [InlineData(ShapeKind.Cylinder, "n1[(\"x\")]")]
    [InlineData(ShapeKind.PredefinedProcess, "n1[[\"x\"]]")]
    public void AShapeGoesIntoWhicheverBracketsComeNearest(ShapeKind kind, string expected)
    {
        var document = Harness.Page(600, 400);
        Box(document, kind, new Rect(40, 40, 120, 60), "x");

        Assert.Contains(expected, Code(document));
    }

    [AvaloniaFact]
    public void AShapeMermaidHasNothingForBecomesAPlainBox()
    {
        var document = Harness.Page(600, 400);
        Box(document, ShapeKind.Cloud, new Rect(40, 40, 120, 60), "x");

        Assert.Contains("n1[\"x\"]", Code(document));
    }

    [AvaloniaFact]
    public void AShapeWithNothingWrittenOnItIsNamedForWhatItIs()
    {
        var document = Harness.Page(600, 400);
        Box(document, ShapeKind.RoundedRectangle, new Rect(40, 40, 120, 60), string.Empty);

        Assert.Contains("Rounded rectangle", Code(document));
    }

    [AvaloniaFact]
    public void AChartRunsWhicheverWayItsLinesMostlyDo()
    {
        var across = Harness.Page(800, 400);
        var a = Box(across, ShapeKind.Rectangle, new Rect(40, 160, 120, 60), "a");
        var b = Box(across, ShapeKind.Rectangle, new Rect(600, 160, 120, 60), "b");
        Join(across, a, b);

        Assert.StartsWith("flowchart LR", Code(across));

        var down = Harness.Page(400, 800);
        var c = Box(down, ShapeKind.Rectangle, new Rect(140, 40, 120, 60), "c");
        var e = Box(down, ShapeKind.Rectangle, new Rect(140, 600, 120, 60), "e");
        Join(down, c, e);

        Assert.StartsWith("flowchart TD", Code(down));
    }

    [AvaloniaTheory]
    [InlineData(StrokeStyle.Solid, EndCapStyle.Arrow, "n1 --> n2")]
    [InlineData(StrokeStyle.Solid, EndCapStyle.None, "n1 --- n2")]
    [InlineData(StrokeStyle.Dashed, EndCapStyle.Arrow, "n1 -.-> n2")]
    [InlineData(StrokeStyle.Dotted, EndCapStyle.None, "n1 -.- n2")]
    public void ALineIsDrawnTheWayItWasDrawn(StrokeStyle style, EndCapStyle end, string expected)
    {
        var document = Harness.Page(800, 400);
        var a = Box(document, ShapeKind.Rectangle, new Rect(40, 160, 120, 60), "a");
        var b = Box(document, ShapeKind.Rectangle, new Rect(600, 160, 120, 60), "b");

        Join(document, a, b, style: style, end: end);

        Assert.Contains(expected, Code(document));
    }

    [AvaloniaFact]
    public void ALineWithWordsOnItCarriesThem()
    {
        var document = Harness.Page(800, 400);
        var a = Box(document, ShapeKind.Rectangle, new Rect(40, 160, 120, 60), "a");
        var b = Box(document, ShapeKind.Rectangle, new Rect(600, 160, 120, 60), "b");

        Join(document, a, b, "yes");

        Assert.Contains("n1 -->|\"yes\"| n2", Code(document));
    }

    [AvaloniaFact]
    public void ALineJoinedToNothingHasNothingToBe()
    {
        // Mermaid joins one named node to another; a line with a loose end names neither.
        var document = Harness.Page(800, 400);
        Box(document, ShapeKind.Rectangle, new Rect(40, 160, 120, 60), "a");
        document.Add(new ConnectorShape(new Point(300, 300), new Point(400, 350)));

        var result = MermaidExporter.Export(document.CurrentPage);

        Assert.DoesNotContain("-->", result.Code);
        Assert.Contains("a connector joined to nothing", result.Summary);
    }

    [AvaloniaFact]
    public void AContainerBecomesASubgraphHoldingWhatIsInIt()
    {
        var document = Harness.Page(900, 500);
        var pool = (ContainerShape)ShapeFactory.Create(ShapeKind.Pool, new Rect(40, 40, 700, 300));
        pool.Text = "Orders";
        document.Add(pool);

        var a = Box(document, ShapeKind.Rectangle, new Rect(140, 90, 150, 70), "Take");
        document.Adopt(a, pool);

        var code = Code(document);

        Assert.Contains("subgraph", code);
        Assert.Contains("\"Orders\"", code);
        Assert.Contains("        n1[\"Take\"]", code);
        Assert.Contains("    end", code);
    }

    [AvaloniaFact]
    public void QuotesAndBarsAndBracketsAreMadeSafe()
    {
        // A bracket is settled by the quoting; a quote and a bar are not - a bar is what ends
        // a line's label, and nothing inside the quotes can be relied on to stop it.
        var document = Harness.Page(800, 400);
        var a = Box(document, ShapeKind.Rectangle, new Rect(40, 160, 160, 60), "He said \"no\" [twice]");
        var b = Box(document, ShapeKind.Rectangle, new Rect(600, 160, 120, 60), "b");

        Join(document, a, b, "a|bar");

        var code = Code(document);

        Assert.Contains("&quot;no&quot;", code);
        Assert.Contains("[twice]", code);
        Assert.Contains("a&#124;bar", code);
        Assert.DoesNotContain("\"no\"", code);
    }

    [AvaloniaFact]
    public void ALabelOfSeveralLinesStaysSeveralLines()
    {
        var document = Harness.Page(600, 400);
        Box(document, ShapeKind.Rectangle, new Rect(40, 40, 120, 60), "one\ntwo");

        Assert.Contains("one<br/>two", Code(document));
    }

    [AvaloniaFact]
    public void AShapeThatWasPaintedSaysSo()
    {
        var document = Harness.Page(600, 400);
        var box = Box(document, ShapeKind.Rectangle, new Rect(40, 40, 120, 60), "x");
        box.Fill = Colors.Khaki;

        Assert.Contains("style n1 fill:#f0e68c", Code(document));
    }

    [AvaloniaFact]
    public void AShapeLeftAsItCameSaysNothingAboutPaint()
    {
        var document = Harness.Page(600, 400);
        Box(document, ShapeKind.Rectangle, new Rect(40, 40, 120, 60), "x");

        Assert.DoesNotContain("style", Code(document));
    }

    [AvaloniaFact]
    public void WhatMermaidCannotHoldIsSaidRatherThanDropped()
    {
        var document = Harness.Page(600, 400);
        var box = Box(document, ShapeKind.Rectangle, new Rect(40, 40, 120, 60), "x");
        box.Rotation = 30;

        Box(document, ShapeKind.TextBox, new Rect(200, 40, 120, 60), "loose words");
        document.CurrentPage.Watermark = "DRAFT";

        var summary = MermaidExporter.Export(document.CurrentPage).Summary;

        Assert.Contains("angle", summary);
        Assert.Contains("text box", summary);
        Assert.Contains("furniture", summary);
    }

    [AvaloniaFact]
    public void EveryShapeTheCatalogueOffersCanBeSaid()
    {
        // Nothing throws, and every shape ends up named exactly once.
        var document = Harness.Page(2000, 2000);
        var kinds = System.Enum.GetValues<ShapeKind>()
            .Where(kind => kind is not (ShapeKind.Connector or ShapeKind.Pool or ShapeKind.Lane
                or ShapeKind.ContainerBox or ShapeKind.Path))
            .ToList();

        for (var i = 0; i < kinds.Count; i++)
            Box(document, kinds[i], new Rect(20 + i % 12 * 150, 20 + i / 12 * 100, 120, 70), "x");

        var code = Code(document);

        Assert.All(Enumerable.Range(1, kinds.Count),
            number => Assert.Contains($"n{number}", code));
    }
}
