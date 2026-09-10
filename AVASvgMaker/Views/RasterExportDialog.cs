using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AVASvgMaker.Engine;

namespace AVASvgMaker.Views;

/// <summary>How large the exported image should be, and how hard to compress it.</summary>
public record RasterExportOptions(double Scale, int Quality);

/// <summary>Settles the export settings. Returns them, or null if the export was called off.</summary>
public class RasterExportDialog : Window
{
    private static readonly double[] Scales = [1, 2, 3, 4];

    private static readonly (string Label, int Quality)[] Qualities =
    [
        ("Maximum", 100),
        ("High", 95),
        ("Medium", 85),
        ("Smaller file", 70)
    ];

    private readonly ComboBox _scale = new() { Width = 190 };
    private readonly ComboBox _quality = new() { Width = 190 };
    private readonly TextBlock _summary = new()
        { FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
    private readonly Button _export;
    private readonly DiagramDocument _document;
    private readonly RasterFormat _format;

    private RasterExportDialog(DiagramDocument document, RasterFormat format)
    {
        _document = document;
        _format = format;

        Title = $"Export {format.Label()}";
        Width = 330;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        foreach (var scale in Scales)
        {
            var size = RasterExporter.SizeAt(document, scale);
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

        foreach (var (label, quality) in Qualities)
            _quality.Items.Add(new ComboBoxItem { Content = $"{label}  -  {quality}", Tag = quality });

        _quality.SelectedIndex = 1;
        _quality.SelectionChanged += (_, _) => Describe();

        _export = new Button { Content = "Export", MinWidth = 88, IsDefault = true };
        _export.Click += (_, _) => Close(Chosen());

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

        var rows = new StackPanel { Spacing = 10, Children = { Row("Size", _scale) } };

        // Only the lossy encoders have anything to decide here.
        if (format.IsLossy())
            rows.Children.Add(Row("Quality", _quality));

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children = { rows, _summary, buttons }
        };

        Describe();
    }

    private static DockPanel Row(string label, Control control)
    {
        var row = new DockPanel { LastChildFill = false };
        var caption = new TextBlock
            { Text = label, VerticalAlignment = VerticalAlignment.Center, Width = 60 };

        DockPanel.SetDock(caption, Dock.Left);
        DockPanel.SetDock(control, Dock.Right);
        row.Children.Add(caption);
        row.Children.Add(control);

        return row;
    }

    private double SelectedScale() =>
        _scale.SelectedItem is ComboBoxItem { Tag: double scale } ? scale : 2;

    private int SelectedQuality() =>
        _quality.SelectedItem is ComboBoxItem { Tag: int quality } ? quality : RasterExporter.DefaultQuality;

    private RasterExportOptions Chosen() => new(SelectedScale(), SelectedQuality());

    private void Describe()
    {
        var scale = SelectedScale();
        var size = RasterExporter.SizeAt(_document, scale);
        var tooLarge = RasterExporter.IsTooLarge(_document, scale);

        var summary = tooLarge
            ? $"{size.Width} x {size.Height} px is too large to render."
            : $"{size.Width} x {size.Height} px on a white page, at {96 * scale:0} DPI.";

        // A BMP keeps every pixel, so say how big that comes to before it is written.
        if (!tooLarge && RasterExporter.FileSize(_document, scale, _format) is { } bytes)
            summary += $" Uncompressed, about {bytes / 1024.0 / 1024.0:0.#} MB.";

        if (!tooLarge && _format == RasterFormat.Jpeg)
            summary += " JPEG has no transparency and softens hard edges.";

        _summary.Text = summary;
        _export.IsEnabled = !tooLarge;
    }

    public static async Task<RasterExportOptions?> ShowAsync(
        Window owner, DiagramDocument document, RasterFormat format)
    {
        var dialog = new RasterExportDialog(document, format);
        return await dialog.ShowDialog<RasterExportOptions?>(owner);
    }
}
