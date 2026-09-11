using System;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using AVASvgMaker.Engine;
using Xunit;

namespace AVASvgMaker.Tests;

/// <summary>What a page carries besides its shapes.</summary>
public class PageFurnitureTests
{
    private static DiagramDocument Pages(int count)
    {
        var document = Harness.Page(400, 300);

        for (var i = 1; i < count; i++)
            document.AddPage();

        document.PageIndex = 0;
        return document;
    }

    [AvaloniaFact]
    public void APageCarriesNothingUntilItIsAskedTo()
    {
        var page = Harness.Page(400, 300).CurrentPage;

        Assert.Equal(string.Empty, page.Watermark);
        Assert.Equal(string.Empty, page.Header);
        Assert.Equal(string.Empty, page.Footer);
    }

    [AvaloniaFact]
    public void APageKnowsItsOwnNumberAndHowManyThereAre()
    {
        var document = Pages(3);
        document.PageIndex = 1;

        var filled = PageFurniture.Fill("Page {page} of {pages}", document, document.CurrentPage);

        Assert.Equal("Page 2 of 3", filled);
    }

    [AvaloniaFact]
    public void APageKnowsItsOwnName()
    {
        var document = Harness.Page(400, 300);
        document.RenamePage(0, "Elevation");

        Assert.Equal("- Elevation -", PageFurniture.Fill("- {name} -", document, document.CurrentPage));
    }

    [AvaloniaFact]
    public void AWordThatNamesNothingIsLeftExactlyAsTyped()
    {
        var document = Harness.Page(400, 300);

        Assert.Equal(
            "{nonsense} and page 1",
            PageFurniture.Fill("{nonsense} and page {page}", document, document.CurrentPage));
    }

    [AvaloniaFact]
    public void TheDateIsFilledInWithSomethingThatLooksLikeOne()
    {
        var document = Harness.Page(400, 300);
        var filled = PageFurniture.Fill("{date}", document, document.CurrentPage);

        Assert.NotEqual("{date}", filled);
        Assert.Contains(DateTime.Now.Year.ToString(), filled);
    }

    [AvaloniaFact]
    public void TheFurnitureGoesIntoTheSvg()
    {
        var document = Harness.Page(400, 300);

        document.SetFurniture("DRAFT", Colors.Gray, -30, "Top", "Page {page}", 11, Colors.DimGray);

        var svg = SvgExporter.Export(document);

        Assert.Contains(">DRAFT<", svg);
        Assert.Contains(">Top<", svg);
        Assert.Contains(">Page 1<", svg);
        Assert.Contains("rotate(-30", svg);
    }

    [AvaloniaFact]
    public void ABarePagePutsNoneOfItIntoTheSvg()
    {
        var svg = SvgExporter.Export(Harness.Page(400, 300));

        Assert.DoesNotContain("<text", svg);
    }

    [AvaloniaFact]
    public void TheFurnitureCanBePutOnEveryPageAtOnce()
    {
        var document = Pages(3);

        document.SetFurniture("DRAFT", Colors.Gray, -30, string.Empty, "{page}", 11, Colors.DimGray,
            allPages: true);

        Assert.All(document.Pages, page => Assert.Equal("DRAFT", page.Watermark));
    }

    [AvaloniaFact]
    public void OrOnThisPageAlone()
    {
        var document = Pages(3);

        document.SetFurniture("DRAFT", Colors.Gray, -30, string.Empty, string.Empty, 11, Colors.DimGray);

        Assert.Equal("DRAFT", document.Pages[0].Watermark);
        Assert.Equal(string.Empty, document.Pages[1].Watermark);
    }

    [AvaloniaFact]
    public void TheFurnitureSurvivesSavingAndOpening()
    {
        var document = Harness.Page(400, 300);

        document.SetFurniture(
            "CONFIDENTIAL", Color.Parse("#40404040"), -60,
            "{name}", "Page {page} of {pages}", 13, Color.Parse("#556677"));

        var back = DiagramFile.FromJson(DiagramFile.ToJson(document)).Pages[0];

        Assert.Equal("CONFIDENTIAL", back.Watermark);
        Assert.Equal(Color.Parse("#40404040"), back.WatermarkColor);
        Assert.Equal(-60, back.WatermarkAngle, 3);
        Assert.Equal("{name}", back.Header);
        Assert.Equal("Page {page} of {pages}", back.Footer);
        Assert.Equal(13, back.HeadFootSize, 3);
        Assert.Equal(Color.Parse("#556677"), back.HeadFootColor);
    }

    [AvaloniaFact]
    public void ABarePageIsNotWrittenDown()
    {
        var document = Harness.Page(400, 300);
        Harness.Box(document, new Rect(10, 10, 40, 40), "plain");

        var json = DiagramFile.ToJson(document);

        Assert.DoesNotContain("watermark", json);
        Assert.DoesNotContain("header", json);
        Assert.DoesNotContain("footer", json);
    }

    [AvaloniaFact]
    public void TheFooterIsSavedAsTheQuestionRatherThanTheAnswer()
    {
        // Written out as "{page}", so page three of a saved document still says three when a
        // page is inserted before it.
        var document = Pages(2);
        document.SetFurniture(string.Empty, Colors.Gray, -30, string.Empty, "{page}", 11,
            Colors.DimGray, allPages: true);

        var json = DiagramFile.ToJson(document);

        Assert.Contains("{page}", json);
    }
}
