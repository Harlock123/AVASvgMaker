using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using AVASvgMaker.Models;

namespace AVASvgMaker.Views;

/// <summary>
/// The data a shape carries, as a list of fields to fill in.
///
/// Returns the fields as they were left, or null if the dialog was called off - so that
/// cancelling leaves the shape exactly as it was found, editing being done on copies until
/// the moment it is accepted.
/// </summary>
public class ShapeDataDialog : Window
{
    private readonly StackPanel _rows;
    private readonly List<ShapeField> _fields;
    private readonly TextBlock _hint;

    private ShapeDataDialog(string caption, IEnumerable<ShapeField> fields)
    {
        Title = "Shape data";
        Width = 520;
        MinHeight = 260;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // Without this the window is only as opaque as what is drawn on it, and the page
        // shows through the gaps between the controls.
        Background = AppTheme.Panel;

        _fields = fields.Select(field => field.Copy()).ToList();
        _rows = new StackPanel { Spacing = 6 };

        _hint = new TextBlock
        {
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        };

        var add = new Button { Content = "Add a field" };
        add.Click += (_, _) =>
        {
            _fields.Add(new ShapeField { Name = Unused(), Label = string.Empty });
            Refill();
        };

        var ok = new Button { Content = "OK", MinWidth = 88, IsDefault = true };
        ok.Click += (_, _) => Close(Kept());

        var cancel = new Button { Content = "Cancel", MinWidth = 88, IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = caption, FontWeight = FontWeight.SemiBold },
                Heading(),
                new ScrollViewer
                {
                    MaxHeight = 320,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = _rows
                },
                add,
                _hint,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { ok, cancel }
                }
            }
        };

        Refill();
    }

    private static Grid Heading()
    {
        var grid = Row();

        void Put(string text, int column)
        {
            var block = new TextBlock { Text = text, Opacity = 0.6, FontSize = 11 };
            Grid.SetColumn(block, column);
            grid.Children.Add(block);
        }

        Put("NAME", 0);
        Put("LABEL", 2);
        Put("VALUE", 4);

        return grid;
    }

    private static Grid Row() => new()
    {
        ColumnDefinitions = new ColumnDefinitions("140,4,*,4,160,4,28")
    };

    private void Refill()
    {
        _rows.Children.Clear();

        foreach (var field in _fields)
        {
            var grid = Row();
            var held = field;

            var name = new TextBox { Text = held.Name, Watermark = "name" };
            name.TextChanged += (_, _) =>
            {
                held.Name = name.Text ?? string.Empty;
                Advise();
            };

            var label = new TextBox { Text = held.Label, Watermark = held.Name };
            label.TextChanged += (_, _) => held.Label = label.Text ?? string.Empty;

            var value = new TextBox { Text = held.Value };
            value.TextChanged += (_, _) => held.Value = value.Text ?? string.Empty;

            var remove = new Button { Content = "✕", Padding = new Thickness(0), Width = 28 };

            ToolTip.SetTip(remove, "Take this field out");

            remove.Click += (_, _) =>
            {
                _fields.Remove(held);
                Refill();
            };

            foreach (var (control, column) in new (Control, int)[]
                     { (name, 0), (label, 2), (value, 4), (remove, 6) })
            {
                Grid.SetColumn(control, column);
                grid.Children.Add(control);
            }

            _rows.Children.Add(grid);
        }

        if (_fields.Count == 0)
            _rows.Children.Add(new TextBlock
            {
                Text = "This shape carries no data yet.",
                Opacity = 0.6,
                Margin = new Thickness(0, 8, 0, 8)
            });

        Advise();
    }

    /// <summary>
    /// Says how to put a field into the shape's label, naming one the shape actually has - the
    /// mechanism is no use to anyone who cannot see what to type.
    /// </summary>
    private void Advise()
    {
        var named = _fields.FirstOrDefault(field => !string.IsNullOrWhiteSpace(field.Name));

        _hint.Text = named is null
            ? "A field is a name and a value. Once a shape has one, its label can show it."
            : $"Put {{{named.Name.Trim()}}} in the shape's label to show this field there.";
    }

    /// <summary>A name not already taken, so a new field is usable the moment it appears.</summary>
    private string Unused()
    {
        for (var i = 1; ; i++)
        {
            var name = $"Field{i}";

            if (_fields.All(field => !string.Equals(field.Name, name, System.StringComparison.OrdinalIgnoreCase)))
                return name;
        }
    }

    /// <summary>
    /// The fields worth keeping: those with a name. A row left blank was a row added and then
    /// thought better of, and is not worth making the user take out again.
    /// </summary>
    private List<ShapeField> Kept()
    {
        var kept = new List<ShapeField>();

        foreach (var field in _fields)
        {
            var name = field.Name.Trim();

            if (name.Length == 0 ||
                kept.Any(taken => string.Equals(taken.Name, name, System.StringComparison.OrdinalIgnoreCase)))
                continue;

            kept.Add(new ShapeField { Name = name, Label = field.Label.Trim(), Value = field.Value });
        }

        return kept;
    }

    public static async Task<List<ShapeField>?> ShowAsync(
        Window owner, string caption, IEnumerable<ShapeField> fields) =>
        await new ShapeDataDialog(caption, fields).ShowDialog<List<ShapeField>?>(owner);
}
