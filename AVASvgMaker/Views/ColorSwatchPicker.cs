using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace AVASvgMaker.Views;

/// <summary>
/// A colour button that opens a palette.
///
/// It raises <see cref="ColorPicked"/> only when someone actually chooses a colour - never
/// when <see cref="Color"/> is set from code. That distinction matters: the properties panel
/// pushes the selection's colours into these controls constantly, and a control that reported
/// those as changes would apply them straight back onto the drawing.
/// </summary>
public class ColorSwatchPicker : UserControl
{
    private static readonly string[][] Palette =
    [
        ["#FFFFFF", "#F2F2F2", "#D9D9D9", "#BFBFBF", "#808080", "#595959", "#404040", "#000000"],
        ["#FBD5D5", "#FDEBC8", "#FFF6BF", "#DDF3D8", "#D6ECFB", "#DCE9FB", "#E6DCFA", "#F5D9EC"],
        ["#D02121", "#E07B00", "#C9A400", "#1FA055", "#0E7C9B", "#2D6CDF", "#6B3FC4", "#B5297E"]
    ];

    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<ColorSwatchPicker, Color>(nameof(Color), Colors.White);

    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public static readonly StyledProperty<bool> IsMixedProperty =
        AvaloniaProperty.Register<ColorSwatchPicker, bool>(nameof(IsMixed));

    /// <summary>True when the selected shapes do not agree, so no one colour can be shown.</summary>
    public bool IsMixed
    {
        get => GetValue(IsMixedProperty);
        set => SetValue(IsMixedProperty, value);
    }

    /// <summary>Raised only for a colour the user chose.</summary>
    public event Action<Color>? ColorPicked;

    private readonly Border _swatch;
    private readonly Popup _popup;
    private readonly TextBox _hex;

    public ColorSwatchPicker()
    {
        _swatch = new Border
        {
            Width = 34,
            Height = 18,
            CornerRadius = new CornerRadius(2),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x72)),
            Background = new SolidColorBrush(Color)
        };

        var button = new Button
        {
            Padding = new Thickness(6, 4),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    _swatch,
                    new Avalonia.Controls.Shapes.Path
                    {
                        Data = Geometry.Parse("M 0,0 L 8,0 L 4,5 Z"),
                        Fill = Brushes.Gray,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            }
        };

        _hex = new TextBox
        {
            Watermark = "#RRGGBB",
            FontSize = 12,
            Width = 110
        };

        _hex.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter)
                return;

            if (Avalonia.Media.Color.TryParse(_hex.Text, out var parsed))
                Choose(parsed);

            args.Handled = true;
        };

        _popup = new Popup
        {
            PlacementTarget = button,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            IsLightDismissEnabled = true,
            Child = BuildPalette()
        };

        // Subscribed after the popup exists, so the handler cannot capture a null field.
        button.Click += (_, _) => _popup.IsOpen = !_popup.IsOpen;

        Content = new Panel { Children = { button, _popup } };
    }

    private Control BuildPalette()
    {
        var rows = new StackPanel { Spacing = 4 };

        foreach (var row in Palette)
        {
            var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

            foreach (var hex in row)
            {
                var colour = Avalonia.Media.Color.Parse(hex);

                var cell = new Border
                {
                    Width = 22,
                    Height = 22,
                    Background = new SolidColorBrush(colour),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x72)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(2),
                    Cursor = new Cursor(StandardCursorType.Hand)
                };

                cell.PointerPressed += (_, args) =>
                {
                    Choose(colour);
                    args.Handled = true;
                };

                strip.Children.Add(cell);
            }

            rows.Children.Add(strip);
        }

        rows.Children.Add(new TextBlock
        {
            Text = "Or type a hex value and press Enter",
            FontSize = 11,
            Opacity = 0.6,
            Margin = new Thickness(0, 6, 0, 0)
        });

        rows.Children.Add(_hex);

        return new Border
        {
            Background = AppTheme.Panel,
            BorderBrush = AppTheme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Child = rows
        };
    }

    private void UpdateSwatch()
    {
        if (IsMixed)
        {
            _swatch.Background = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x52));
            _swatch.Child = new TextBlock
            {
                Text = "\u2014",
                FontSize = 11,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            return;
        }

        _swatch.Child = null;
        _swatch.Background = new SolidColorBrush(Color);
        _hex.Text = $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}";
    }

    /// <summary>The one path that reports a change: an explicit choice.</summary>
    private void Choose(Color colour)
    {
        _popup.IsOpen = false;

        IsMixed = false;
        Color = colour;
        ColorPicked?.Invoke(colour);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ColorProperty || change.Property == IsMixedProperty)
            UpdateSwatch();
    }
}
