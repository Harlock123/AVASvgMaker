using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AVASvgMaker.Views;

/// <summary>Which of a prompt's three buttons was pressed.</summary>
public enum ConfirmResult
{
    Primary,
    Secondary,
    Cancel
}

/// <summary>A three-way "you have unsaved changes" prompt. Avalonia has no built-in message box.</summary>
public class ConfirmDialog : Window
{
    private ConfirmDialog(string message, string primary, string secondary)
    {
        Title = "AVASvgMaker";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 20, 0, 0)
        };

        buttons.Children.Add(Button(primary, ConfirmResult.Primary, isDefault: true));
        buttons.Children.Add(Button(secondary, ConfirmResult.Secondary));
        buttons.Children.Add(Button("Cancel", ConfirmResult.Cancel));

        var layout = new StackPanel { Margin = new Avalonia.Thickness(20) };
        layout.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap
        });
        layout.Children.Add(buttons);

        Content = layout;
    }

    private Button Button(string text, ConfirmResult result, bool isDefault = false)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 88,
            IsDefault = isDefault,
            IsCancel = result == ConfirmResult.Cancel
        };

        button.Click += (_, _) => Close(result);
        return button;
    }

    public static async Task<ConfirmResult> ShowAsync(
        Window owner, string message, string primary = "Save", string secondary = "Discard")
    {
        var dialog = new ConfirmDialog(message, primary, secondary);
        return await dialog.ShowDialog<ConfirmResult>(owner);
    }
}
