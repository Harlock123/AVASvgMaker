using System;
using System.Linq;
using Avalonia;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using Avalonia.Headless.XUnit;
using Xunit;

namespace AVASvgMaker.Tests;

public class PageTests
{
    [AvaloniaFact]
    public void PagesAreAddedRenamedMovedAndRemoved()
    {
        var document = Harness.Page();
        var first = Harness.Box(document, new Rect(10, 10, 40, 40), "on page one");

        var second = document.AddPage();

        Assert.Equal(2, document.Pages.Count);
        Assert.Equal(1, document.PageIndex);
        Assert.Equal("Page 2", second.Name);
        Assert.Empty(document.Shapes);
        Assert.Single(document.Pages[0].Shapes);

        Assert.True(document.RenamePage(1, "Detail"));
        Assert.False(document.RenamePage(1, "   "));
        Assert.Equal("Detail", document.Pages[1].Name);

        document.PageIndex = 0;
        Assert.Same(first, document.Shapes[0]);

        Assert.True(document.MovePage(0, 1));
        Assert.Equal("Page 1", document.Pages[1].Name);
        Assert.Equal(1, document.PageIndex);

        Assert.True(document.RemovePage(1));
        Assert.Single(document.Pages);
        Assert.False(document.RemovePage(0));
    }

    [AvaloniaFact]
    public void ADuplicatedPageIsACopyAndNotAReference()
    {
        var document = Harness.Page();
        var original = Harness.Box(document, new Rect(10, 10, 40, 40), "one");

        var copy = document.DuplicatePage(0);

        Assert.Equal(2, document.Pages.Count);
        Assert.Single(copy.Shapes);
        Assert.NotSame(original, copy.Shapes[0]);
        Assert.Equal("one", copy.Shapes[0].Text);
        Assert.DoesNotContain(document.Pages, page => page.Name == copy.Name && !ReferenceEquals(page, copy));
    }

    [AvaloniaFact]
    public void TheSelectionDoesNotFollowAcrossPages()
    {
        var document = Harness.Page();
        var shape = Harness.Box(document, new Rect(10, 10, 40, 40));

        Assert.True(document.IsSelected(shape));

        document.AddPage();
        Assert.Empty(document.Selection);
    }

    [AvaloniaFact]
    public void EachPageCarriesItsOwnPaper()
    {
        var document = Harness.Page(600, 300);

        document.AddPage();
        Assert.Equal(600, document.PageWidth, 2);

        document.SetPageSize(400, 900);
        document.PageIndex = 0;

        Assert.Equal(600, document.PageWidth, 2);
        Assert.True(document.Pages[0].Width > document.Pages[0].Height, "page 1 is landscape");
        Assert.True(document.Pages[1].Height > document.Pages[1].Width, "page 2 is portrait");

        document.SetPageSize(500, 700, allPages: true);
        Assert.All(document.Pages, page => Assert.Equal(500, page.Width, 2));
    }

    [AvaloniaFact]
    public void AMarginBelongsToItsPage()
    {
        var document = Harness.Page(600, 300);

        Assert.Equal(0, document.Margin, 2);

        document.SetMargin(48);
        document.AddPage();
        Assert.Equal(48, document.Margin, 2);

        document.SetMargin(0);
        Assert.Equal(48, document.Pages[0].Margin, 2);

        document.SetMargin(-5);
        Assert.Equal(0, document.Margin, 2);
    }

    [AvaloniaFact]
    public void TurningPagesIsNotAnEditButChangingThemIs()
    {
        var document = Harness.Page(600, 300);
        Harness.Box(document, new Rect(10, 10, 40, 40));
        document.AddPage();
        Harness.Box(document, new Rect(10, 10, 40, 40));

        var history = new UndoStack(document);

        document.PageIndex = 0;
        document.PageIndex = 1;
        document.PageIndex = 0;

        Assert.False(history.CanUndo);

        // An edit on another page is undone from this one, and lands you back on it.
        document.PageIndex = 1;
        Harness.Box(document, new Rect(80, 80, 30, 30));
        document.PageIndex = 0;

        history.Undo();

        Assert.Equal(1, document.PageIndex);
        Assert.Single(document.Pages[1].Shapes);
    }

    [AvaloniaFact]
    public void RemovingAPageUndoesWithItsContents()
    {
        var document = Harness.Page(600, 300);
        Harness.Box(document, new Rect(10, 10, 40, 40));
        document.AddPage();
        Harness.Box(document, new Rect(10, 10, 40, 40));
        Harness.Box(document, new Rect(60, 10, 40, 40));

        var history = new UndoStack(document);

        document.RemovePage(1);
        Assert.Single(document.Pages);

        history.Undo();

        Assert.Equal(2, document.Pages.Count);
        Assert.Equal(2, document.Pages[1].Shapes.Count);
    }
}
