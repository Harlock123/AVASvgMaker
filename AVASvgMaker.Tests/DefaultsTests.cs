using System;
using System.IO;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Media;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using Xunit;

namespace AVASvgMaker.Tests;

/// <summary>
/// The formatting a new shape is given, and where it is kept.
///
/// The thing worth guarding is the line between the two places a "default" could mean: the
/// properties panel's live value, which arms the next shape and is how people try colours out,
/// and the saved one the application starts with. If setting a colour with nothing selected
/// wrote to the saved one, then experimenting would quietly rewrite what the app opens with
/// and there would be no way back to it.
/// </summary>
public class DefaultsTests : IDisposable
{
    // Every test here runs with the platform up, even the ones that only touch a file: a
    // settings change repaints, a repaint rebuilds the page tabs, and rebuilding a control
    // wants a cursor factory. Without it the test that happens to run after a window was
    // opened fails and the one before it does not, which is the worst kind of red.

    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), $"avasvgmaker-defaults-{Guid.NewGuid():N}.json");

    private readonly string _was = Preferences.Path;

    public DefaultsTests()
    {
        Preferences.Path = _settings;
        Preferences.Reload();
    }

    public void Dispose()
    {
        Preferences.Path = _was;
        Preferences.Reload();

        if (File.Exists(_settings))
            File.Delete(_settings);
    }

    #region The line between the two

    [AvaloniaFact]
    public void FormattingWithNothingSelectedArmsTheNextShape()
    {
        var (_, canvas) = Harness.Editor(800, 600);
        canvas.Document.ClearSelection();

        canvas.SetFill(Colors.Teal);

        Assert.Equal(Colors.Teal, canvas.DefaultStyle.Fill);
    }

    [AvaloniaFact]
    public void ButDoesNotRewriteWhatTheApplicationStartsWith()
    {
        // The trap: trying colours out is not the same as changing your settings.
        var (_, canvas) = Harness.Editor(800, 600);
        canvas.Document.ClearSelection();

        var saved = Preferences.Style;
        canvas.SetFill(Colors.Teal);

        Assert.Equal(saved, Preferences.Style);
        Assert.NotEqual(Colors.Teal, Preferences.Style.Fill);
    }

    [AvaloniaFact]
    public void SavingADefaultDeliberatelyDoesRewriteIt()
    {
        var (_, canvas) = Harness.Editor(800, 600);
        canvas.Document.ClearSelection();
        canvas.SetFill(Colors.Teal);

        // What the settings dialog's "take from the current drawing" does.
        Preferences.Style = canvas.DefaultStyle;
        Preferences.Reload();

        Assert.Equal(Colors.Teal, Preferences.Style.Fill);
    }

    [AvaloniaFact]
    public void AndTheSavedDefaultCarriesIntoTheSessionWhenItIsSet()
    {
        // Changing the default fill and finding the next shape ignores it would be a puzzle.
        var (_, canvas) = Harness.Editor(800, 600);

        canvas.UseDefaultStyle(Preferences.Style with { Fill = Colors.Firebrick });

        Assert.Equal(Colors.Firebrick, canvas.DefaultStyle.Fill);
    }

    #endregion

    #region On disk

    [AvaloniaFact]
    public void AFreshInstallationDrawsWithWhatTheAppAlwaysDrewWith()
    {
        Assert.Equal(ShapeStyle.Default, Preferences.Style);
    }

    [AvaloniaFact]
    public void AChosenStyleOutlastsTheSession()
    {
        Preferences.Style = ShapeStyle.Default with
        {
            Fill = Colors.Teal,
            FontName = "Cascadia Mono",
            FontSize = 17,
            Bold = true,
            StrokeStyle = StrokeStyle.Dashed
        };

        Preferences.Reload();

        var read = Preferences.Style;

        Assert.Equal(Colors.Teal, read.Fill);
        Assert.Equal("Cascadia Mono", read.FontName);
        Assert.Equal(17, read.FontSize);
        Assert.True(read.Bold);
        Assert.Equal(StrokeStyle.Dashed, read.StrokeStyle);
    }

    [AvaloniaFact]
    public void AFadeSurvivesToo()
    {
        Preferences.Style = ShapeStyle.Default with { FillTo = Colors.Navy, FillAngle = 45 };
        Preferences.Reload();

        Assert.Equal(Colors.Navy, Preferences.Style.FillTo);
        Assert.Equal(45, Preferences.Style.FillAngle);
    }

    [AvaloniaFact]
    public void AndSoDoesNotHavingOne()
    {
        Preferences.Style = ShapeStyle.Default with { FillTo = Colors.Navy };
        Preferences.Style = ShapeStyle.Default with { FillTo = null };
        Preferences.Reload();

        Assert.Null(Preferences.Style.FillTo);
    }

    [AvaloniaFact]
    public void TheGridAndThePaperAreRemembered()
    {
        Preferences.GridSize = 25;
        Preferences.SnapToGrid = false;
        Preferences.ShowGrid = false;
        Preferences.Paper = new Size(1123, 794);

        Preferences.Reload();

        Assert.Equal(25, Preferences.GridSize);
        Assert.False(Preferences.SnapToGrid);
        Assert.False(Preferences.ShowGrid);
        Assert.Equal(new Size(1123, 794), Preferences.Paper);
    }

    [AvaloniaFact]
    public void ASettingsFileFromBeforeThereWereDefaultsStillOpens()
    {
        // Version 1 could say which theme, and nothing else.
        Directory.CreateDirectory(Path.GetDirectoryName(_settings)!);
        File.WriteAllText(_settings, """{"version": 1, "theme": "Nord"}""");
        Preferences.Reload();

        Assert.Equal("Nord", Preferences.Theme);
        Assert.Equal(ShapeStyle.Default, Preferences.Style);
        Assert.Equal(10, Preferences.GridSize);
        Assert.True(Preferences.SnapToGrid);
        Assert.Equal(new Size(816, 1056), Preferences.Paper);
    }

    [AvaloniaFact]
    public void SettingTheSameStyleAgainIsNotAChange()
    {
        Preferences.Style = ShapeStyle.Default with { Fill = Colors.Teal };

        var told = 0;

        void Count() => told++;

        Preferences.Changed += Count;

        try
        {
            Preferences.Style = ShapeStyle.Default with { Fill = Colors.Teal };
        }
        finally
        {
            Preferences.Changed -= Count;
        }

        Assert.Equal(0, told);
    }

    [AvaloniaFact]
    public void TheGridSizesOfferedAreTheOnesTheToolbarOffers()
    {
        // Two lists of grid sizes would sooner or later disagree.
        Assert.Contains(Preferences.GridSize, GridSettings.Sizes);
        Assert.Equal([5d, 10, 20, 25, 50], GridSettings.Sizes);
    }

    #endregion

    #region In the window

    [AvaloniaFact]
    public void ANewWindowStartsOnTheSavedGrid()
    {
        Preferences.GridSize = 25;
        Preferences.SnapToGrid = false;

        var (_, canvas) = Harness.Editor(800, 600);

        Assert.Equal(25, canvas.Grid.Size);
        Assert.False(canvas.Grid.SnapToGrid);
    }

    [AvaloniaFact]
    public void AndOnTheSavedPaper()
    {
        Preferences.Paper = new Size(1123, 794);

        var window = new AVASvgMaker.MainWindow();
        window.Show();

        var canvas = window.FindControl<AVASvgMaker.Views.DrawingCanvas>("Canvas")!;

        Assert.Equal(1123, canvas.Document.PageWidth);
        Assert.Equal(794, canvas.Document.PageHeight);

        // And is not dirty for having been put on it.
        Assert.False(canvas.Document.IsModified);

        window.Close();
    }

    #endregion
}
