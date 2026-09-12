using System;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using AVASvgMaker.Engine;
using AVASvgMaker.Views;
using Xunit;

namespace AVASvgMaker.Tests;

/// <summary>
/// Choosing a theme, and having the choice stick.
///
/// The thing worth testing is not that a palette can be looked up but that a named one is not
/// quietly taken away again: the desktop can change at any moment, and before there was a
/// setting the app simply did as it was told by whatever the desktop said last.
/// </summary>
public class ThemeTests : IDisposable
{
    // Every test here runs with the platform up, even the ones that only touch a file - see
    // the note in DefaultsTests for why.

    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), $"avasvgmaker-settings-{Guid.NewGuid():N}.json");

    private readonly string _was = Preferences.Path;

    public ThemeTests()
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

    #region The catalogue

    [AvaloniaFact]
    public void TheAppShipsWithThemesToChooseFrom()
    {
        Assert.True(ThemeCatalogue.All.Count >= 10);

        // Light and dark both, which is more than it had: before this there was one palette.
        Assert.Contains(ThemeCatalogue.All, theme => theme.Palette.IsLight);
        Assert.Contains(ThemeCatalogue.All, theme => !theme.Palette.IsLight);
    }

    [AvaloniaFact]
    public void EachOfThemIsNamedOnceAndCanBeFoundByName()
    {
        var names = ThemeCatalogue.All.Select(theme => theme.Name).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(names, name => Assert.NotNull(ThemeCatalogue.Find(name)));
    }

    [AvaloniaFact]
    public void AndTheLookupIsNotFussyAboutCase()
    {
        Assert.NotNull(ThemeCatalogue.Find("nord"));
        Assert.NotNull(ThemeCatalogue.Find("NORD"));
    }

    [AvaloniaFact]
    public void FollowingTheDesktopIsNotItselfAPalette()
    {
        // "Desktop" is a choice about where the colours come from, not a set of colours.
        Assert.Null(ThemeCatalogue.Find(Preferences.DesktopTheme));
        Assert.DoesNotContain(ThemeCatalogue.All, theme =>
            string.Equals(theme.Name, Preferences.DesktopTheme, StringComparison.OrdinalIgnoreCase));
    }

    [AvaloniaFact]
    public void EveryThemeIsLegibleAgainstItsOwnPanel()
    {
        // A theme whose text disappears into its panel is not a theme, it is a bug that ships.
        foreach (var theme in ThemeCatalogue.All)
        {
            var gap = Math.Abs(Luminance(theme.Palette.Text) - Luminance(theme.Palette.Panel));

            Assert.True(gap > 0.35, $"{theme.Name}: text and panel are {gap:0.00} apart");
        }
    }

    private static double Luminance(Avalonia.Media.Color colour) =>
        (0.2126 * colour.R + 0.7152 * colour.G + 0.0722 * colour.B) / 255;

    #endregion

    #region Which palette wins

    private static readonly AppPalette Desktop = new(
        Workspace: Avalonia.Media.Colors.Red, Panel: Avalonia.Media.Colors.Red,
        PanelItem: Avalonia.Media.Colors.Red, Border: Avalonia.Media.Colors.Red,
        Text: Avalonia.Media.Colors.White, Accent: Avalonia.Media.Colors.Red, IsLight: false);

    [AvaloniaFact]
    public void ANamedThemeBeatsTheDesktop()
    {
        Assert.Equal(ThemeCatalogue.Find("Nord"), ThemeChoice.Resolve("Nord", () => Desktop));
    }

    [AvaloniaFact]
    public void WithNoneNamedTheDesktopIsUsed()
    {
        Assert.Equal(Desktop, ThemeChoice.Resolve(Preferences.DesktopTheme, () => Desktop));
    }

    [AvaloniaFact]
    public void AndWithNoDesktopEitherTheAppFallsBackToWhatItShipsWith()
    {
        Assert.Equal(AppPalette.Fallback, ThemeChoice.Resolve(Preferences.DesktopTheme, () => null));
        Assert.Equal(AppPalette.Fallback, ThemeChoice.Resolve(null, () => null));
    }

    [AvaloniaFact]
    public void AThemeThatIsNoLongerShippedFallsBackRatherThanFailing()
    {
        // A settings file written by a later version, opened by an earlier one.
        Assert.Equal(Desktop, ThemeChoice.Resolve("Some Theme From The Future", () => Desktop));
    }

    [AvaloniaFact]
    public void ADesktopChangeDoesNotTakeAwayAThemeThatWasChosen()
    {
        // The report this guards: pick Nord, change the desktop, and Nord used to vanish.
        Preferences.Theme = "Nord";

        var afterDesktopChanged = ThemeChoice.Resolve(Preferences.Theme, () => Desktop);

        Assert.Equal(ThemeCatalogue.Find("Nord"), afterDesktopChanged);
    }

    #endregion

    #region Remembering it

    [AvaloniaFact]
    public void ANamedThemeDoesNotEvenAskTheDesktop()
    {
        // Asking costs a subprocess on Omarchy, and the answer cannot change the outcome.
        var asked = 0;

        ThemeChoice.Resolve("Dracula", () => { asked++; return Desktop; });

        Assert.Equal(0, asked);
    }

    [AvaloniaFact]
    public void AFreshInstallationFollowsTheDesktop()
    {
        Assert.True(Preferences.FollowsDesktop);
        Assert.Equal(Preferences.DesktopTheme, Preferences.Theme);
    }

    [AvaloniaFact]
    public void AChosenThemeOutlastsTheSession()
    {
        Preferences.Theme = "Gruvbox Dark";
        Preferences.Reload();

        Assert.Equal("Gruvbox Dark", Preferences.Theme);
        Assert.False(Preferences.FollowsDesktop);
    }

    [AvaloniaFact]
    public void AndIsWrittenWhenItChangesRatherThanOnTheWayOut()
    {
        Preferences.Theme = "Dracula";

        Assert.True(File.Exists(_settings));
        Assert.Contains("Dracula", File.ReadAllText(_settings));
    }

    [AvaloniaFact]
    public void GoingBackToTheDesktopIsRemembered()
    {
        Preferences.Theme = "Nord";
        Preferences.Theme = Preferences.DesktopTheme;
        Preferences.Reload();

        Assert.True(Preferences.FollowsDesktop);
    }

    [AvaloniaFact]
    public void ChangingItSaysSoOnce()
    {
        var told = 0;

        void Count() => told++;

        Preferences.Changed += Count;

        try
        {
            Preferences.Theme = "Nord";
            Preferences.Theme = "Nord";
        }
        finally
        {
            Preferences.Changed -= Count;
        }

        Assert.Equal(1, told);
    }

    [AvaloniaFact]
    public void SettingsThatWillNotParseAreNotWorthRefusingToStartOver()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settings)!);
        File.WriteAllText(_settings, "this is not json{{{");
        Preferences.Reload();

        Assert.True(Preferences.FollowsDesktop);
    }

    #endregion

    #region On screen

    [AvaloniaFact]
    public void ApplyingAThemeRepaintsWithoutAnythingBeingRebound()
    {
        var was = AppTheme.Current;

        try
        {
            // Everything binds to the brush instances, so this is the whole repaint.
            var nord = ThemeCatalogue.Find("Nord")!;
            AppTheme.Apply(nord);

            Assert.Equal(nord.Panel, AppTheme.Panel.Color);
            Assert.Equal(nord.Accent, AppTheme.Accent.Color);
            Assert.Equal(nord, AppTheme.Current);
        }
        finally
        {
            AppTheme.Apply(was);
        }
    }

    [AvaloniaFact]
    public void ChoosingAThemeRepaintsTheApplicationWithoutBeingAsked()
    {
        // The wiring the settings dialog relies on: it writes the setting and nothing else.
        var was = Preferences.Theme;

        try
        {
            Preferences.Theme = "Tokyo Night";

            Assert.Equal(ThemeCatalogue.Find("Tokyo Night"), AppTheme.Current);
            Assert.Equal(ThemeCatalogue.Find("Tokyo Night")!.Panel, AppTheme.Panel.Color);
        }
        finally
        {
            Preferences.Theme = was;
        }
    }

    [AvaloniaFact]
    public void AndSaysSoForTheViewsThatDrawThemselves()
    {
        var was = AppTheme.Current;
        var told = 0;

        void Count() => told++;

        AppTheme.Changed += Count;

        try
        {
            AppTheme.Apply(ThemeCatalogue.Find("Solarized Light")!);
            AppTheme.Apply(ThemeCatalogue.Find("Solarized Light")!);
        }
        finally
        {
            AppTheme.Changed -= Count;
            AppTheme.Apply(was);
        }

        // Once for the change, and nothing for setting the same thing again.
        Assert.Equal(1, told);
    }

    #endregion
}
