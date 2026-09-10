using System;
using Avalonia.Media;

namespace AVASvgMaker.Views;

/// <summary>The colours the application's own chrome is drawn in.</summary>
/// <param name="Workspace">Behind the page.</param>
/// <param name="Panel">Toolbar, side panels, status bar.</param>
/// <param name="PanelItem">Raised things inside a panel, such as a stencil.</param>
/// <param name="Border">Divisions between panels.</param>
/// <param name="Text">Panel text.</param>
/// <param name="Accent">Selection handles and the marquee.</param>
/// <param name="IsLight">Which Fluent theme variant the controls should use.</param>
public sealed record AppPalette(
    Color Workspace,
    Color Panel,
    Color PanelItem,
    Color Border,
    Color Text,
    Color Accent,
    bool IsLight)
{
    /// <summary>What the app ships with, and what it falls back to off the Linux desktop.</summary>
    public static readonly AppPalette Fallback = new(
        Workspace: Color.FromRgb(0x2A, 0x2A, 0x2E),
        Panel: Color.FromRgb(0x26, 0x26, 0x2A),
        PanelItem: Color.FromRgb(0x33, 0x33, 0x3A),
        Border: Color.FromRgb(0x3A, 0x3A, 0x42),
        Text: Color.FromRgb(0xE4, 0xE4, 0xE8),
        Accent: Color.FromRgb(0xFF, 0x8C, 0x00),
        IsLight: false);
}

/// <summary>
/// The live palette, held as brushes rather than colours.
///
/// Everything - XAML through <c>{x:Static}</c> and the hand-drawn views alike - binds to these
/// brush *instances*, and a theme change repaints simply by setting each brush's colour.
/// Nothing has to be rebound, and no control needs to know a theme exists.
/// </summary>
public static class AppTheme
{
    public static AppPalette Current { get; private set; } = AppPalette.Fallback;

    /// <summary>Raised after the palette changes, for views that draw themselves.</summary>
    public static event Action? Changed;

    public static SolidColorBrush Workspace { get; } = new(AppPalette.Fallback.Workspace);
    public static SolidColorBrush Panel { get; } = new(AppPalette.Fallback.Panel);
    public static SolidColorBrush PanelItem { get; } = new(AppPalette.Fallback.PanelItem);
    public static SolidColorBrush Border { get; } = new(AppPalette.Fallback.Border);
    public static SolidColorBrush Text { get; } = new(AppPalette.Fallback.Text);
    public static SolidColorBrush Accent { get; } = new(AppPalette.Fallback.Accent);

    public static void Apply(AppPalette palette)
    {
        if (palette == Current)
            return;

        Current = palette;

        Workspace.Color = palette.Workspace;
        Panel.Color = palette.Panel;
        PanelItem.Color = palette.PanelItem;
        Border.Color = palette.Border;
        Text.Color = palette.Text;
        Accent.Color = palette.Accent;

        Changed?.Invoke();
    }
}
