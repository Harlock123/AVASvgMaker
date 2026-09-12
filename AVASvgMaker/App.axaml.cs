using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using AVASvgMaker.Engine;
using AVASvgMaker.Views;

namespace AVASvgMaker;

public partial class App : Application
{
    private IDisposable? _themeWatcher;

    /// <summary>Collapses the burst of file writes a single theme change produces.</summary>
    private DispatcherTimer? _themeSettle;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        SettleTheme();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Puts the chosen theme on, and keeps watching the desktop in case that is what was
    /// chosen. Both ends are live: the desktop can change, and so can the choice.
    /// </summary>
    private void SettleTheme()
    {
        ApplyTheme();

        // Changing the setting repaints at once, the same way a desktop change does. The
        // theme variant may only be set from the UI thread, and Changed is a general event
        // that anything might one day raise from anywhere, so say which thread this is on
        // rather than trusting the caller to be on the right one.
        Preferences.Changed += () =>
        {
            if (Dispatcher.UIThread.CheckAccess())
                ApplyTheme();
            else
                Dispatcher.UIThread.Post(ApplyTheme);
        };

        _themeSettle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _themeSettle.Tick += (_, _) =>
        {
            _themeSettle!.Stop();
            ApplyTheme();
        };

        _themeWatcher = OmarchyTheme.Watch(() => Dispatcher.UIThread.Post(() =>
        {
            _themeSettle!.Stop();
            _themeSettle.Start();
        }));

        // Windows and macOS say when the system switches between light and dark. A bare
        // Wayland compositor says nothing, which is the whole reason a theme can be named.
        if (PlatformSettings is { } platform)
            platform.ColorValuesChanged += (_, _) => Dispatcher.UIThread.Post(ApplyTheme);
    }

    public void ApplyTheme()
    {
        var palette = ThemeChoice.Resolve(Preferences.Theme, DesktopPalette);

        AppTheme.Apply(palette);

        // The Fluent controls - menus, combo boxes, checkboxes - follow the variant.
        RequestedThemeVariant = palette.IsLight ? ThemeVariant.Light : ThemeVariant.Dark;
    }

    /// <summary>
    /// What the desktop is doing, as well as it can be made out. Omarchy publishes a whole
    /// palette; everywhere else the most that can be had is light or dark, which is mapped
    /// onto the two plain themes.
    /// </summary>
    private AppPalette? DesktopPalette()
    {
        if (OmarchyTheme.Read() is { } omarchy)
            return omarchy;

        return PlatformSettings?.GetColorValues().ThemeVariant switch
        {
            PlatformThemeVariant.Light => ThemeCatalogue.Find("Light"),
            PlatformThemeVariant.Dark => ThemeCatalogue.Find("Dark"),
            _ => null
        };
    }
}
