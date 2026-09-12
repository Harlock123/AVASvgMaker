using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;

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
    private readonly ListBox _themes = new() { Height = 330 };
    private readonly TextBlock _note = new()
    {
        FontSize = 11,
        Opacity = 0.7,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 0)
    };

    private readonly ColorSwatchPicker _fill = new();
    private readonly ColorSwatchPicker _line = new();
    private readonly ColorSwatchPicker _textColour = new();
    private readonly NumericUpDown _weight = new()
        { Width = 150, Minimum = 0.5m, Maximum = 20, Increment = 0.5m };
    private readonly ComboBox _lineStyle = new() { Width = 150 };
    private readonly ComboBox _font = new() { Width = 150 };
    private readonly NumericUpDown _fontSize = new()
        { Width = 150, Minimum = 4, Maximum = 200, Increment = 1 };
    private readonly CheckBox _bold = new() { Content = "Bold" };
    private readonly CheckBox _italic = new() { Content = "Italic" };

    /// <summary>The same five the toolbar offers - two lists of grid sizes would disagree.</summary>
    private readonly ComboBox _grid = new() { Width = 150 };
    private readonly CheckBox _snap = new() { Content = "Snap to grid" };
    private readonly CheckBox _showGrid = new() { Content = "Show grid" };
    private readonly ComboBox _paper = new() { Width = 150 };
    private readonly ComboBox _orientation = new() { Width = 150 };

    private readonly string _opened;
    private readonly ShapeStyle _session;
    private bool _syncing;

    private SettingsDialog(ShapeStyle session)
    {
        _session = session;
        _opened = Preferences.Theme;

        Title = "Settings";

        // Fixed rather than sized to content: the tabs are different heights and a window
        // that resizes as you move between them is a window that will not sit still.
        Width = 520;
        Height = 600;
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
        tabs.Items.Add(new TabItem { Header = "Appearance", Content = Scrolled(appearance) });
        tabs.Items.Add(new TabItem { Header = "Defaults", Content = Scrolled(Defaults()) });
        tabs.Items.Add(new TabItem { Header = "Grid", Content = Scrolled(GridAndPage()) });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(14, 0, 14, 14)
        };

        var ok = new Button { Content = "OK", MinWidth = 88, IsDefault = true };
        ok.Click += (_, _) =>
        {
            // The theme has been applied as it was picked; the rest is written on the way out,
            // so that half-typed numbers never become anybody's defaults.
            Preferences.Style = Chosen();
            Preferences.GridSize = _grid.SelectedItem is ComboBoxItem { Tag: double step } ? step : 10;
            Preferences.SnapToGrid = _snap.IsChecked == true;
            Preferences.ShowGrid = _showGrid.IsChecked == true;
            Preferences.Paper = ChosenPaper();

            Close(true);
        };

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
        Fill(Preferences.Style);
        FillGrid();
    }

    #region Defaults

    /// <summary>
    /// The formatting a new shape is given. Kept apart from the properties panel on purpose:
    /// setting a colour there with nothing selected arms the next shape, which is what people
    /// do while trying colours out, and if that wrote here then experimenting would quietly
    /// rewrite what the application starts with.
    /// </summary>
    private Control Defaults()
    {
        var layout = new StackPanel { Spacing = 6, Margin = new Thickness(14) };

        layout.Children.Add(new TextBlock { Text = "Shape", FontWeight = FontWeight.SemiBold });
        layout.Children.Add(Row("Fill", _fill));
        layout.Children.Add(Row("Line", _line));
        layout.Children.Add(Row("Weight", _weight));
        layout.Children.Add(Row("Line style", _lineStyle));

        layout.Children.Add(new TextBlock
            { Text = "Text", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
        layout.Children.Add(Row("Font", _font));
        layout.Children.Add(Row("Size", _fontSize));
        layout.Children.Add(Row("Colour", _textColour));

        var marks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        marks.Children.Add(_bold);
        marks.Children.Add(_italic);
        layout.Children.Add(marks);

        foreach (var style in Enum.GetValues<StrokeStyle>())
            _lineStyle.Items.Add(new ComboBoxItem { Content = style.ToString(), Tag = style });

        _font.Items.Add(new ComboBoxItem { Content = "Default font", Tag = string.Empty });

        foreach (var family in FontManager.Current.SystemFonts
                     .Select(family => family.Name).OrderBy(name => name, StringComparer.CurrentCulture))
            _font.Items.Add(new ComboBoxItem { Content = family, Tag = family });

        var take = new Button
        {
            Content = "Take from the current drawing",
            Margin = new Thickness(0, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };

        // The bridge between the two, crossed deliberately rather than by accident.
        take.Click += (_, _) => Fill(_session);

        layout.Children.Add(take);
        layout.Children.Add(new TextBlock
        {
            Text = "Whatever the properties panel is set to now, saved as the default.",
            FontSize = 11,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap
        });

        return layout;
    }

    private void Fill(ShapeStyle style)
    {
        _syncing = true;

        _fill.Color = style.Fill;
        _line.Color = style.Stroke;
        _textColour.Color = style.TextColor;
        _weight.Value = (decimal)style.StrokeThickness;
        _fontSize.Value = (decimal)style.FontSize;
        _bold.IsChecked = style.Bold;
        _italic.IsChecked = style.Italic;

        Choose(_lineStyle, style.StrokeStyle);
        Choose(_font, string.IsNullOrEmpty(style.FontName) ? string.Empty : style.FontName);

        _syncing = false;
    }

    /// <summary>What the boxes currently say, as a style. Anything not offered keeps its value.</summary>
    private ShapeStyle Chosen() => Preferences.Style with
    {
        Fill = _fill.Color,
        Stroke = _line.Color,
        TextColor = _textColour.Color,
        StrokeThickness = (double)(_weight.Value ?? 2),
        StrokeStyle = _lineStyle.SelectedItem is ComboBoxItem { Tag: StrokeStyle line }
            ? line
            : StrokeStyle.Solid,
        FontSize = (double)(_fontSize.Value ?? 13),
        FontName = _font.SelectedItem is ComboBoxItem { Tag: string family } ? family : string.Empty,
        Bold = _bold.IsChecked == true,
        Italic = _italic.IsChecked == true
    };

    #endregion

    #region Grid and page

    private Control GridAndPage()
    {
        var layout = new StackPanel { Spacing = 6, Margin = new Thickness(14) };

        layout.Children.Add(new TextBlock { Text = "Grid", FontWeight = FontWeight.SemiBold });
        layout.Children.Add(Row("Size", _grid));
        layout.Children.Add(_snap);
        layout.Children.Add(_showGrid);

        layout.Children.Add(new TextBlock
            { Text = "New drawings", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
        layout.Children.Add(Row("Paper", _paper));
        layout.Children.Add(Row("Orientation", _orientation));

        layout.Children.Add(new TextBlock
        {
            Text = "The paper a new drawing starts on. Page setup changes the drawing you " +
                   "have open; this is what the next one begins with.",
            FontSize = 11,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });

        foreach (var size in GridSettings.Sizes)
            _grid.Items.Add(new ComboBoxItem { Content = $"{size:0} px", Tag = size });

        foreach (var preset in PageSize.Presets)
            _paper.Items.Add(new ComboBoxItem { Content = preset.Name, Tag = preset });

        _orientation.Items.Add(new ComboBoxItem { Content = "Portrait" });
        _orientation.Items.Add(new ComboBoxItem { Content = "Landscape" });

        return layout;
    }

    private void FillGrid()
    {
        _syncing = true;

        Choose(_grid, Preferences.GridSize);
        _snap.IsChecked = Preferences.SnapToGrid;
        _showGrid.IsChecked = Preferences.ShowGrid;

        var paper = Preferences.Paper;
        var preset = PageSize.Match(paper.Width, paper.Height) ?? PageSize.Presets[0];

        _paper.SelectedItem = _paper.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => (item.Tag as PageSize)?.Name == preset.Name);
        _orientation.SelectedIndex = paper.Width > paper.Height ? 1 : 0;

        _syncing = false;
    }

    private Size ChosenPaper()
    {
        var preset = _paper.SelectedItem is ComboBoxItem { Tag: PageSize chosen }
            ? chosen
            : PageSize.Presets[0];

        return _orientation.SelectedIndex == 1
            ? new Size(preset.Height, preset.Width)
            : new Size(preset.Width, preset.Height);
    }

    #endregion

    private static Control Row(string label, Control field)
    {
        var row = new DockPanel { LastChildFill = false };

        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 110
        };

        DockPanel.SetDock(text, Dock.Left);
        DockPanel.SetDock(field, Dock.Right);

        row.Children.Add(text);
        row.Children.Add(field);

        return row;
    }

    private static void Choose<T>(ComboBox box, T value) =>
        box.SelectedItem = box.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => Equals(item.Tag, value)) ?? box.Items.OfType<ComboBoxItem>().FirstOrDefault();

    /// <summary>
    /// Room to scroll, so that a tab which does not fit is awkward rather than unusable - a
    /// short screen or a large font should not put the last setting out of reach.
    /// </summary>
    private static Control Scrolled(Control content) => new ScrollViewer
    {
        Content = content,
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
    };

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

    /// <summary>
    /// Opens the settings. The session's own formatting is handed in so that "take from the
    /// current drawing" has something to take.
    /// </summary>
    public static async Task<bool> ShowAsync(Window owner, ShapeStyle session) =>
        await new SettingsDialog(session).ShowDialog<bool>(owner);
}
