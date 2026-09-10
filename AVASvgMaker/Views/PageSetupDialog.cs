using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AVASvgMaker.Models;

namespace AVASvgMaker.Views;

/// <summary>The paper chosen, and whether every page is to be put on it.</summary>
public record PageSetup(Size Size, double Margin, bool AllPages);

/// <summary>Chooses the size of a page. Returns the choice, or null if it was cancelled.</summary>
public class PageSetupDialog : Window
{
    private readonly CheckBox _allPages = new()
    {
        Content = "Apply to every page",
        VerticalAlignment = VerticalAlignment.Center
    };

    private readonly ComboBox _preset = new() { Width = 150 };
    private readonly ComboBox _orientation = new() { Width = 150 };
    private readonly NumericUpDown _width = new() { Width = 150, Minimum = 100, Maximum = 20000, Increment = 10 };
    private readonly NumericUpDown _height = new() { Width = 150, Minimum = 100, Maximum = 20000, Increment = 10 };
    private readonly NumericUpDown _margin = new() { Width = 150, Minimum = 0, Maximum = 500, Increment = 8 };
    private readonly TextBlock _summary = new() { FontSize = 11, Opacity = 0.7 };
    private readonly Rect? _drawing;

    /// <summary>Guards the boxes against reacting while they are being filled in.</summary>
    private bool _syncing;

    private PageSetupDialog(double width, double height, double margin, Rect? drawing, int pageCount)
    {
        _drawing = drawing;

        Title = "Page setup";
        Width = 340;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        foreach (var preset in PageSize.Presets)
            _preset.Items.Add(new ComboBoxItem { Content = preset.Name, Tag = preset });

        _preset.Items.Add(new ComboBoxItem { Content = "Custom", Tag = null });

        _orientation.Items.Add(new ComboBoxItem { Content = "Portrait" });
        _orientation.Items.Add(new ComboBoxItem { Content = "Landscape" });

        _preset.SelectionChanged += (_, _) => OnPresetChanged();
        _orientation.SelectionChanged += (_, _) => OnOrientationChanged();
        _width.ValueChanged += (_, _) => OnSizeTyped();
        _height.ValueChanged += (_, _) => OnSizeTyped();

        var layout = new StackPanel { Margin = new Thickness(20), Spacing = 10 };
        layout.Children.Add(Row("Size", _preset));
        layout.Children.Add(Row("Orientation", _orientation));
        layout.Children.Add(Row("Width", _width));
        layout.Children.Add(Row("Height", _height));
        layout.Children.Add(Row("Margin", _margin));
        layout.Children.Add(_summary);

        // Only worth asking when there is more than one page to ask about.
        if (pageCount > 1)
            layout.Children.Add(_allPages);

        if (drawing is { Width: > 0, Height: > 0 })
        {
            var fit = new Button { Content = "Fit to the drawing", HorizontalAlignment = HorizontalAlignment.Left };
            fit.Click += (_, _) => FitToDrawing();
            layout.Children.Add(fit);
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 10, 0, 0)
        };

        var ok = new Button { Content = "OK", MinWidth = 88, IsDefault = true };
        ok.Click += (_, _) => Close(new PageSetup(
            new Size(Value(_width), Value(_height)), Value(_margin), _allPages.IsChecked == true));

        var cancel = new Button { Content = "Cancel", MinWidth = 88, IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        layout.Children.Add(buttons);

        Content = layout;

        Apply(width, height);
        _margin.Value = (decimal)margin;
    }

    private static Control Row(string label, Control field)
    {
        var row = new DockPanel { LastChildFill = false };

        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 100
        };

        DockPanel.SetDock(text, Dock.Left);
        DockPanel.SetDock(field, Dock.Right);

        row.Children.Add(text);
        row.Children.Add(field);
        return row;
    }

    private static double Value(NumericUpDown box) => (double)(box.Value ?? 0);

    /// <summary>Fills every control in from one size, without them answering back.</summary>
    private void Apply(double width, double height)
    {
        _syncing = true;

        _width.Value = (decimal)Math.Round(width);
        _height.Value = (decimal)Math.Round(height);

        var preset = PageSize.Match(width, height);

        _preset.SelectedItem = _preset.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => (item.Tag as PageSize)?.Name == preset?.Name)
            ?? _preset.Items.OfType<ComboBoxItem>().Last();

        _orientation.SelectedIndex = width > height ? 1 : 0;

        _syncing = false;
        Describe();
    }

    private void OnPresetChanged()
    {
        if (_syncing || _preset.SelectedItem is not ComboBoxItem { Tag: PageSize preset })
            return;

        var landscape = _orientation.SelectedIndex == 1;
        Apply(landscape ? preset.Height : preset.Width, landscape ? preset.Width : preset.Height);
    }

    private void OnOrientationChanged()
    {
        if (_syncing)
            return;

        var width = Value(_width);
        var height = Value(_height);
        var landscape = _orientation.SelectedIndex == 1;

        if (landscape == width > height)
            return;

        Apply(height, width);
    }

    /// <summary>Typing a size of your own means it is no longer one of the presets.</summary>
    private void OnSizeTyped()
    {
        if (_syncing)
            return;

        _syncing = true;

        var preset = PageSize.Match(Value(_width), Value(_height));

        _preset.SelectedItem = _preset.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => (item.Tag as PageSize)?.Name == preset?.Name)
            ?? _preset.Items.OfType<ComboBoxItem>().Last();

        _orientation.SelectedIndex = Value(_width) > Value(_height) ? 1 : 0;

        _syncing = false;
        Describe();
    }

    private void FitToDrawing()
    {
        if (_drawing is not { } drawing)
            return;

        // Room around the drawing, and room for whatever it was moved away from.
        const double margin = 40;
        Apply(drawing.Right + margin, drawing.Bottom + margin);
    }

    private void Describe()
    {
        var width = Value(_width);
        var height = Value(_height);

        var margin = Value(_margin);

        _summary.Text = (margin > 0 ? $"{margin:0} px margin  ·  " : string.Empty) +
                        $"{width:0} x {height:0} px  ·  " +
                        $"{width / 96:0.##} x {height / 96:0.##} in  ·  " +
                        $"{width / 96 * 25.4:0} x {height / 96 * 25.4:0} mm";
    }

    public static async Task<PageSetup?> ShowAsync(
        Window owner, double width, double height, double margin, Rect? drawing, int pageCount)
    {
        var dialog = new PageSetupDialog(width, height, margin, drawing, pageCount);
        return await dialog.ShowDialog<PageSetup?>(owner);
    }
}
