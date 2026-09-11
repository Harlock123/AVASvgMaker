using System;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using Xunit;

namespace AVASvgMaker.Tests;

/// <summary>Fills that run from one colour to another, on a shape and on the paper.</summary>
public class GradientTests
{
    [AvaloniaTheory]
    [InlineData(0, 0, 0.5, 1, 0.5)]      // across
    [InlineData(90, 0.5, 0, 0.5, 1)]     // down
    [InlineData(180, 1, 0.5, 0, 0.5)]    // back
    [InlineData(270, 0.5, 1, 0.5, 0)]    // up
    public void AFadeRunsTheWayItsAngleSays(
        double angle, double x1, double y1, double x2, double y2)
    {
        var (start, end) = new Gradient(Colors.Black, Colors.White, angle).Ends();

        Assert.Equal(x1, start.Point.X, 3);
        Assert.Equal(y1, start.Point.Y, 3);
        Assert.Equal(x2, end.Point.X, 3);
        Assert.Equal(y2, end.Point.Y, 3);
    }

    [AvaloniaFact]
    public void ADiagonalFadeReachesTwoOppositeCorners()
    {
        var (start, end) = new Gradient(Colors.Black, Colors.White, 45).Ends();

        Assert.Equal(0, start.Point.X, 3);
        Assert.Equal(0, start.Point.Y, 3);
        Assert.Equal(1, end.Point.X, 3);
        Assert.Equal(1, end.Point.Y, 3);
    }

    [AvaloniaFact]
    public void AShapeWithNoFarEndIsFilledFlat()
    {
        var shape = Harness.Box(Harness.Page(400, 300), new Rect(20, 20, 100, 60), "plain");

        Assert.Null(shape.FillTo);
        Assert.Null(shape.Fade);
    }

    [AvaloniaFact]
    public void AFadedShapeWritesTheDefinitionItPointsAt()
    {
        // SVG has no gradient without a definition to name, so the shape carries its own.
        var shape = Harness.Box(Harness.Page(400, 300), new Rect(20, 20, 100, 60), "plain");

        shape.Fill = Colors.SteelBlue;
        shape.FillTo = Colors.White;

        var svg = shape.ToSvg();

        Assert.Contains("<linearGradient", svg);
        Assert.Contains("#4682B4", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Matches("fill=\"url\\(#fade\\d+\\)\"", svg);
    }

    [AvaloniaFact]
    public void EachFadedShapeNamesItsOwnDefinition()
    {
        var document = Harness.Page(400, 300);

        var one = Harness.Box(document, new Rect(20, 20, 100, 60), "one");
        var two = Harness.Box(document, new Rect(160, 20, 100, 60), "two");

        one.FillTo = Colors.White;
        two.FillTo = Colors.White;

        var svg = SvgExporter.Export(document);
        var first = svg.IndexOf("id=\"fade", StringComparison.Ordinal);
        var second = svg.IndexOf("id=\"fade", first + 1, StringComparison.Ordinal);

        // Two definitions, and not the same name twice - one would paint the other's shape.
        Assert.True(second > first);
        Assert.NotEqual(
            svg.Substring(first, 16),
            svg.Substring(second, 16));
    }

    [AvaloniaFact]
    public void AFlatShapeWritesNoDefinitionAtAll()
    {
        var shape = Harness.Box(Harness.Page(400, 300), new Rect(20, 20, 100, 60), "plain");

        Assert.DoesNotContain("linearGradient", shape.ToSvg());
    }

    [AvaloniaFact]
    public void AFadeSurvivesSavingAndOpening()
    {
        var document = Harness.Page(400, 300);
        var shape = Harness.Box(document, new Rect(20, 20, 100, 60), "plain");

        shape.Fill = Colors.SteelBlue;
        shape.FillTo = Colors.White;
        shape.FillAngle = 45;

        var back = DiagramFile.FromJson(DiagramFile.ToJson(document)).Pages[0].Shapes[0];

        Assert.Equal(Colors.SteelBlue, back.Fill);
        Assert.Equal(Colors.White, back.FillTo);
        Assert.Equal(45, back.FillAngle, 3);
    }

    [AvaloniaFact]
    public void APageStartsAsPlainWhitePaper()
    {
        var page = Harness.Page(400, 300).CurrentPage;

        Assert.Equal(Colors.White, page.Background);
        Assert.Null(page.BackgroundTo);
        Assert.Null(page.Fade);
    }

    [AvaloniaFact]
    public void ThePaperSurvivesSavingAndOpening()
    {
        var document = Harness.Page(400, 300);

        document.SetPaper(Colors.White, Color.Parse("#DCE6F5"), 0);

        var back = DiagramFile.FromJson(DiagramFile.ToJson(document)).Pages[0];

        Assert.Equal(Colors.White, back.Background);
        Assert.Equal(Color.Parse("#DCE6F5"), back.BackgroundTo);
        Assert.Equal(0, back.BackgroundAngle, 3);
    }

    [AvaloniaFact]
    public void PlainWhitePaperIsNotWrittenDown()
    {
        var document = Harness.Page(400, 300);
        Harness.Box(document, new Rect(10, 10, 40, 40), "plain");

        var json = DiagramFile.ToJson(document);

        Assert.DoesNotContain("background", json);
        Assert.DoesNotContain("fillTo", json);
    }

    [AvaloniaFact]
    public void ThePaperGoesIntoTheSvgAndThePicture()
    {
        var document = Harness.Page(400, 300);
        document.SetPaper(Color.Parse("#FFEEDD"), null, 90);

        Assert.Contains("#FFEEDD", SvgExporter.Export(document), StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void AFileFromBeforeAnythingFadedStillOpens()
    {
        // Version 17 had white paper and flat fills and nowhere to say otherwise, so such a
        // file simply has none of these words in it.
        var document = Harness.Page(400, 300);
        Harness.Box(document, new Rect(20, 20, 100, 60), "plain");

        var json = DiagramFile.ToJson(document)
            .Replace($"\"version\": {DiagramFile.CurrentVersion}", "\"version\": 17");

        var back = DiagramFile.FromJson(json);

        Assert.Null(back.Pages[0].Shapes[0].FillTo);
        Assert.Equal(90, back.Pages[0].Shapes[0].FillAngle, 3);
        Assert.Equal(Colors.White, back.Pages[0].Background);
        Assert.Null(back.Pages[0].BackgroundTo);
    }
}
