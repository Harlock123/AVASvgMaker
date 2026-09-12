using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AVASvgMaker.Engine;

namespace AVASvgMaker.Views;

/// <summary>
/// The application's own settings, as opposed to the drawing's. One tab so far; it is a
/// TabControl because the next things to go here - default fonts, default paper - are not
/// appearance and should not be filed under it.
///
/// The theme is applied as you move down the list rather than on OK, because a colour scheme
/// cannot be judged from its name. Cancel puts back whatever was on when the dialog opened,
/// so trying them all costs nothing.
/// </summary>
public class SettingsDialog : Window
{
    private readonly ListBox _themes = new() { Height = 300 };
    private readonly TextBlock _note = new()
    {
        FontSize = 11,
        Opacity = 0.7,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 0)
    };

    private readonly string _opened;
    private bool _syncing;

    private SettingsDialog()
    {
        _opened = Preferences.Theme;

        Title = "Settings";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _themes.Items.Add(Entry(Preferences.DesktopTheme, null));

        foreach (var theme in ThemeCatalogue.All)
            _themes.Items.Add(Entry(theme.Name, theme.Palette));

        Select(_opened);
        _themes.SelectionChanged += (_, _) => OnPicked();

        var appearance = new StackPanel { Spacing = 6, Margin = new Thickness(14) };
        appearance.Children.Add(new TextBlock { Text = "Theme", FontWeight = FontWeight.SemiBold });
        appearance.Children.Add(_themes);
        appearance.Children.Add(_note);

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Appearance", Content = appearance });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(14, 0, 14, 14)
        };

        var ok = new Button { Content = "OK", MinWidth = 88, IsDefault = true };
        ok.Click += (_, _) => Close(true);

        // Whatever was picked has already been applied, so cancelling has to put it back.
        var cancel = new Button { Content = "Cancel", MinWidth = 88, IsCancel = true };
        cancel.Click += (_, _) =>
        {
            Preferences.Theme = _opened;
            Close(false);
        };

        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var layout = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);
        layout.Children.Add(tabs);

        Content = layout;
        Describe();
    }

    /// <summary>A row showing the theme's own colours, since the name alone says nothing.</summary>
    private static ListBoxItem Entry(string name, AppPalette? palette)
    {
        var row = new DockPanel { LastChildFill = true };

        var label = new TextBlock
        {
            Text = name,
            VerticalAlignment = VerticalAlignment.Center
        };

        DockPanel.SetDock(label, Dock.Left);
        row.Children.Add(label);

        if (palette is { } colours)
        {
            var swatches = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 3,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            foreach (var colour in new[]
                     { colours.Workspace, colours.Panel, colours.PanelItem, colours.Text, colours.Accent })
            {
                swatches.Children.Add(new Border
                {
                    Width = 18,
                    Height = 14,
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(colour),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80))
                });
            }

            DockPanel.SetDock(swatches, Dock.Right);
            row.Children.Add(swatches);
        }

        return new ListBoxItem { Content = row, Tag = name };
    }

    private void Select(string name)
    {
        _syncing = true;

        _themes.SelectedItem = _themes.Items.OfType<ListBoxItem>().FirstOrDefault(item =>
            string.Equals(item.Tag as string, name, StringComparison.OrdinalIgnoreCase))
            ?? _themes.Items.OfType<ListBoxItem>().FirstOrDefault();

        _syncing = false;
    }

    private void OnPicked()
    {
        if (_syncing || _themes.SelectedItem is not ListBoxItem { Tag: string name })
            return;

        // Straight on, so the window behind changes while the list is still open. Nothing
        // has to be told to repaint: the application is listening to the setting.
        Preferences.Theme = name;
        Describe();
    }

    private void Describe()
    {
        _note.Text = Preferences.FollowsDesktop
            ? "Follows the desktop where the desktop says what it is doing - Omarchy, Windows " +
              "and macOS all do. A plain Wayland or X11 session usually does not, and there " +
              "the app stays on the theme it starts with. Name one below to be certain."
            : $"Always {Preferences.Theme}, whatever the desktop is set to.";
    }

    public static async Task<bool> ShowAsync(Window owner) =>
        await new SettingsDialog().ShowDialog<bool>(owner);
}
