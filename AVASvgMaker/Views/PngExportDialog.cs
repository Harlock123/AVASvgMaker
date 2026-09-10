using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using AVASvgMaker.Engine;

namespace AVASvgMaker.Views;

/// <summary>Chooses how large the exported image should be. Returns the scale, or null.</summary>
public class PngExportDialog : Window
{
    private static readonly double[] Scales = [1, 2, 3, 4];

    private readonly ComboBox _scale = new() { Width = 190 };
    private readonly TextBlock _summary = new() { FontSize = 11, Opacity = 0.7 };
    private readonly Button _export;
    private readonly DiagramDocument _document;

    private PngExportDialog(DiagramDocument document)
    {
        _document = document;

        Title = "Export PNG";
        Width = 330;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        foreach (var scale in Scales)
        {
            var size = PngExporter.SizeAt(document, scale);
            _scale.Items.Add(new ComboBoxItem
            {
                Content = $"{scale:0}x  -  {size.Width} x {size.Height} px",
                Tag = scale
            });
        }

        // Twice the page is the useful default: sharp on a high-resolution screen and still
        // a reasonable size to send to someone.
        _scale.SelectedIndex = 1;
        _scale.SelectionChanged += (_, _) => Describe();

        _export = new Button { Content = "Export", MinWidth = 88, IsDefault = true };
        _export.Click += (_, _) => Close(Selected());

        var cancel = new Button { Content = "Cancel", MinWidth = 88, IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { _export, cancel }
        };

        var row = new DockPanel { LastChildFill = false };
        var label = new TextBlock { Text = "Size", VerticalAlignment = VerticalAlignment.Center, Width = 60 };
        DockPanel.SetDock(label, Dock.Left);
        DockPanel.SetDock(_scale, Dock.Right);
        row.Children.Add(label);
        row.Children.Add(_scale);

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children = { row, _summary, buttons }
        };

        Describe();
    }

    private double Selected() =>
        _scale.SelectedItem is ComboBoxItem { Tag: double scale } ? scale : 2;

    private void Describe()
    {
        var scale = Selected();
        var size = PngExporter.SizeAt(_document, scale);
        var tooLarge = PngExporter.IsTooLarge(_document, scale);

        _summary.Text = tooLarge
            ? $"{size.Width} x {size.Height} px is too large to render."
            : $"{size.Width} x {size.Height} px on a white page, at {96 * scale:0} DPI.";

        _export.IsEnabled = !tooLarge;
    }

    public static async Task<double?> ShowAsync(Window owner, DiagramDocument document)
    {
        var dialog = new PngExportDialog(document);
        return await dialog.ShowDialog<double?>(owner);
    }
}
