using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace AVASvgMaker.Views;

/// <summary>One of the palettes the app ships with, under the name it is chosen by.</summary>
/// <param name="Name">What the settings dialog shows, and what the settings file records.</param>
public sealed record NamedPalette(string Name, AppPalette Palette);

/// <summary>
/// The themes the app ships with.
///
/// Until there was a way to choose one, the app had exactly one palette off Omarchy - the dark
/// one in <see cref="AppPalette.Fallback"/> - and no light mode at all. A desktop that
/// announces its theme is a luxury: Omarchy has a file to watch and Windows and macOS answer
/// when asked, but a bare Wayland compositor does neither, and there the only way the app can
/// know is to be told.
///
/// The palettes are the published ones, not approximations: someone who runs Nord everywhere
/// notices when an app is nearly Nord. Each is six colours - see <see cref="AppPalette"/> -
/// which is the whole of the app's own chrome. The drawing is never themed; a diagram has to
/// look the same to whoever it is sent to.
/// </summary>
public static class ThemeCatalogue
{
    /// <summary>The name meaning "whatever the desktop is doing", rather than a palette.</summary>
    public const string Desktop = "Desktop";

    public static IReadOnlyList<NamedPalette> All { get; } =
    [
        new("Dark", AppPalette.Fallback),

        new("Light", new AppPalette(
            Workspace: Rgb(0xE8, 0xE8, 0xEC), Panel: Rgb(0xF5, 0xF5, 0xF7),
            PanelItem: Rgb(0xFF, 0xFF, 0xFF), Border: Rgb(0xD0, 0xD0, 0xD8),
            Text: Rgb(0x1A, 0x1A, 0x1E), Accent: Rgb(0x00, 0x78, 0xD7), IsLight: true)),

        new("Solarized Dark", new AppPalette(
            Workspace: Rgb(0x00, 0x2B, 0x36), Panel: Rgb(0x07, 0x36, 0x42),
            PanelItem: Rgb(0x0A, 0x41, 0x4E), Border: Rgb(0x58, 0x6E, 0x75),
            Text: Rgb(0x93, 0xA1, 0xA1), Accent: Rgb(0x26, 0x8B, 0xD2), IsLight: false)),

        new("Solarized Light", new AppPalette(
            Workspace: Rgb(0xEE, 0xE8, 0xD5), Panel: Rgb(0xFD, 0xF6, 0xE3),
            PanelItem: Rgb(0xFF, 0xFC, 0xF0), Border: Rgb(0x93, 0xA1, 0xA1),
            Text: Rgb(0x58, 0x6E, 0x75), Accent: Rgb(0x26, 0x8B, 0xD2), IsLight: true)),

        new("Nord", new AppPalette(
            Workspace: Rgb(0x2E, 0x34, 0x40), Panel: Rgb(0x3B, 0x42, 0x52),
            PanelItem: Rgb(0x43, 0x4C, 0x5E), Border: Rgb(0x4C, 0x56, 0x6A),
            Text: Rgb(0xEC, 0xEF, 0xF4), Accent: Rgb(0x88, 0xC0, 0xD0), IsLight: false)),

        new("Gruvbox Dark", new AppPalette(
            Workspace: Rgb(0x1D, 0x20, 0x21), Panel: Rgb(0x28, 0x28, 0x28),
            PanelItem: Rgb(0x3C, 0x38, 0x36), Border: Rgb(0x50, 0x49, 0x45),
            Text: Rgb(0xEB, 0xDB, 0xB2), Accent: Rgb(0xFE, 0x80, 0x19), IsLight: false)),

        new("Gruvbox Light", new AppPalette(
            Workspace: Rgb(0xF2, 0xE5, 0xBC), Panel: Rgb(0xFB, 0xF1, 0xC7),
            PanelItem: Rgb(0xFF, 0xFB, 0xE8), Border: Rgb(0xD5, 0xC4, 0xA1),
            Text: Rgb(0x3C, 0x38, 0x36), Accent: Rgb(0xAF, 0x3A, 0x03), IsLight: true)),

        new("Dracula", new AppPalette(
            Workspace: Rgb(0x1E, 0x1F, 0x29), Panel: Rgb(0x28, 0x2A, 0x36),
            PanelItem: Rgb(0x44, 0x47, 0x5A), Border: Rgb(0x62, 0x72, 0xA4),
            Text: Rgb(0xF8, 0xF8, 0xF2), Accent: Rgb(0xBD, 0x93, 0xF9), IsLight: false)),

        new("Tokyo Night", new AppPalette(
            Workspace: Rgb(0x1A, 0x1B, 0x26), Panel: Rgb(0x24, 0x28, 0x3B),
            PanelItem: Rgb(0x2F, 0x33, 0x49), Border: Rgb(0x41, 0x48, 0x68),
            Text: Rgb(0xC0, 0xCA, 0xF5), Accent: Rgb(0x7A, 0xA2, 0xF7), IsLight: false)),

        new("Catppuccin Mocha", new AppPalette(
            Workspace: Rgb(0x11, 0x11, 0x1B), Panel: Rgb(0x1E, 0x1E, 0x2E),
            PanelItem: Rgb(0x31, 0x32, 0x44), Border: Rgb(0x45, 0x47, 0x5A),
            Text: Rgb(0xCD, 0xD6, 0xF4), Accent: Rgb(0xCB, 0xA6, 0xF7), IsLight: false)),

        new("Catppuccin Latte", new AppPalette(
            Workspace: Rgb(0xDC, 0xE0, 0xE8), Panel: Rgb(0xEF, 0xF1, 0xF5),
            PanelItem: Rgb(0xFF, 0xFF, 0xFF), Border: Rgb(0xBC, 0xC0, 0xCC),
            Text: Rgb(0x4C, 0x4F, 0x69), Accent: Rgb(0x88, 0x39, 0xEF), IsLight: true)),

        // Not a fashion: a pairing for anyone who needs the edges to actually be edges.
        new("High contrast dark", new AppPalette(
            Workspace: Rgb(0x00, 0x00, 0x00), Panel: Rgb(0x00, 0x00, 0x00),
            PanelItem: Rgb(0x1A, 0x1A, 0x1A), Border: Rgb(0xFF, 0xFF, 0xFF),
            Text: Rgb(0xFF, 0xFF, 0xFF), Accent: Rgb(0xFF, 0xFF, 0x00), IsLight: false)),

        new("High contrast light", new AppPalette(
            Workspace: Rgb(0xFF, 0xFF, 0xFF), Panel: Rgb(0xFF, 0xFF, 0xFF),
            PanelItem: Rgb(0xF0, 0xF0, 0xF0), Border: Rgb(0x00, 0x00, 0x00),
            Text: Rgb(0x00, 0x00, 0x00), Accent: Rgb(0x00, 0x00, 0xCC), IsLight: true))
    ];

    /// <summary>The named theme, or null for a name that is not one - including "Desktop".</summary>
    public static AppPalette? Find(string? name) =>
        name is null
            ? null
            : All.FirstOrDefault(theme =>
                string.Equals(theme.Name, name, StringComparison.OrdinalIgnoreCase))?.Palette;

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}

/// <summary>
/// Which palette wins. Kept apart from the application object so the rule can be read - and
/// tested - without anything being on screen.
/// </summary>
public static class ThemeChoice
{
    /// <summary>
    /// A named theme beats the desktop, always: someone who has picked Nord has said what
    /// they want, and a desktop that changes underneath them is not new information. Only
    /// when nothing is named does the desktop get a say, and only then does its absence
    /// fall through to what the app ships with.
    ///
    /// The desktop is asked for rather than handed over, because asking is not free - on
    /// Omarchy it runs the desktop's own resolver in a subprocess - and a named theme means
    /// the answer cannot matter. Arrowing down the theme list should not start thirteen
    /// processes to ignore what they say.
    /// </summary>
    public static AppPalette Resolve(string? preference, Func<AppPalette?> desktop) =>
        ThemeCatalogue.Find(preference) ?? desktop() ?? AppPalette.Fallback;
}
