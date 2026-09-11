using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AVASvgMaker.Views;

/// <summary>What a page carries besides its shapes, and whether every page is to carry it.</summary>
public record PageFurnitureChoice(
    string Watermark, Color WatermarkColor, double WatermarkAngle,
    string Header, string Footer, double HeadFootSize, Color HeadFootColor,
    bool AllPages);

/// <summary>
/// Sets a page's watermark, header and footer. Returns the choice, or null if it was called off.
/// </summary>
public class PageFurnitureDialog : Window
{
    private readonly TextBox _watermark = new() { Watermark = "DRAFT", Width = 210 };
    private readonly ColorSwatchPicker _watermarkColour = new();
    private readonly ComboBox _angle = new() { Width = 104 };

    private readonly TextBox _header = new() { Width = 210 };
    private readonly TextBox _footer = new() { Width = 210 };
    private readonly NumericUpDown _size = new()
        { Width = 104, Minimum = 6, Maximum = 48, Increment = 1 };

    private readonly ColorSwatchPicker _headFootColour = new();

    private readonly CheckBox _allPages = new()
    {
        Content = "Apply to every page",
        VerticalAlignment = VerticalAlignment.Center
    };

    private PageFurnitureDialog(
        string watermark, Color watermarkColour, double angle,
        string header, string footer, double size, Color headFootColour, int pageCount)
    {
        Title = "Page furniture";
        Width = 400;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = AppTheme.Panel;

        _watermark.Text = watermark;
        _watermarkColour.Color = watermarkColour;
        _header.Text = header;
        _footer.Text = footer;
        _size.Value = (decimal)size;
        _headFootColour.Color = headFootColour;
        _allPages.IsChecked = true;
        _allPages.IsVisible = pageCount > 1;

        foreach (var (caption, degrees) in new[]
                 { ("Diagonal", -30), ("Across", 0), ("Steep", -60), ("Up", -90) })
            _angle.Items.Add(new ComboBoxItem { Content = caption, Tag = degrees });

        _angle.SelectedItem = _angle.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => (int)item.Tag! == (int)angle)
            ?? _angle.Items.OfType<ComboBoxItem>().First();

        var layout = new StackPanel { Margin = new Thickness(20), Spacing = 8 };

        layout.Children.Add(Heading("WATERMARK"));
        layout.Children.Add(Row("Words", _watermark));
        layout.Children.Add(Row("Colour", new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _watermarkColour, _angle }
        }));

        layout.Children.Add(Heading("HEADER AND FOOTER"));
        layout.Children.Add(Row("Header", _header));
        layout.Children.Add(Row("Footer", _footer));
        layout.Children.Add(Row("Size", new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _size, _headFootColour }
        }));

        layout.Children.Add(new TextBlock
        {
            Text = "{page}, {pages}, {name}, {date} and {time} are filled in as they are drawn.",
            FontSize = 11,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });

        if (_allPages.IsVisible)
            layout.Children.Add(_allPages);

        var ok = new Button { Content = "OK", MinWidth = 88, IsDefault = true };
        ok.Click += (_, _) => Close(Chosen());

        var cancel = new Button { Content = "Cancel", MinWidth = 88, IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        layout.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { ok, cancel }
        });

        Content = layout;
    }

    private PageFurnitureChoice Chosen() => new(
        _watermark.Text?.Trim() ?? string.Empty,
        _watermarkColour.Color,
        _angle.SelectedItem is ComboBoxItem { Tag: int degrees } ? degrees : -30,
        _header.Text?.Trim() ?? string.Empty,
        _footer.Text?.Trim() ?? string.Empty,
        (double)(_size.Value ?? 11),
        _headFootColour.Color,
        _allPages.IsChecked == true);

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontSize = 11,
        Opacity = 0.6,
        Margin = new Thickness(0, 6, 0, 0)
    };

    private static Control Row(string label, Control field) => new Grid
    {
        ColumnDefinitions = new ColumnDefinitions("90,*"),
        Children =
        {
            new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center
            },
            Placed(field)
        }
    };

    private static Control Placed(Control field)
    {
        Grid.SetColumn(field, 1);
        return field;
    }

    public static async Task<PageFurnitureChoice?> ShowAsync(
        Window owner, string watermark, Color watermarkColour, double angle,
        string header, string footer, double size, Color headFootColour, int pageCount)
    {
        var dialog = new PageFurnitureDialog(
            watermark, watermarkColour, angle, header, footer, size, headFootColour, pageCount);

        return await dialog.ShowDialog<PageFurnitureChoice?>(owner);
    }
}
