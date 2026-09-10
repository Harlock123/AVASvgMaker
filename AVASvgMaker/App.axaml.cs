using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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
        FollowDesktopTheme();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Takes the desktop's colours where there are any, and keeps taking them. Off Omarchy
    /// this does nothing at all and the app keeps the palette it ships with.
    /// </summary>
    private void FollowDesktopTheme()
    {
        ApplyDesktopTheme();

        _themeSettle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _themeSettle.Tick += (_, _) =>
        {
            _themeSettle!.Stop();
            ApplyDesktopTheme();
        };

        _themeWatcher = OmarchyTheme.Watch(() => Dispatcher.UIThread.Post(() =>
        {
            _themeSettle!.Stop();
            _themeSettle.Start();
        }));
    }

    private void ApplyDesktopTheme()
    {
        if (OmarchyTheme.Read() is not { } palette)
            return;

        AppTheme.Apply(palette);

        // The Fluent controls - menus, combo boxes, checkboxes - follow the variant.
        RequestedThemeVariant = palette.IsLight ? ThemeVariant.Light : ThemeVariant.Dark;
    }
}
