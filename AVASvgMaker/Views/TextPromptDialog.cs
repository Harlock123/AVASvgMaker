using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace AVASvgMaker.Views;

/// <summary>Asks for one line of text. Returns it, or null if the prompt was called off.</summary>
public class TextPromptDialog : Window
{
    private readonly TextBox _entry;

    private TextPromptDialog(string title, string caption, string initial)
    {
        Title = title;
        Width = 340;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _entry = new TextBox { Text = initial };

        var ok = new Button { Content = "OK", MinWidth = 88, IsDefault = true };
        ok.Click += (_, _) => Close(Entered());

        var cancel = new Button { Content = "Cancel", MinWidth = 88, IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = caption },
                _entry,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Margin = new Thickness(0, 10, 0, 0),
                    Children = { ok, cancel }
                }
            }
        };

        // Opened with the existing text selected, so typing simply replaces it.
        Opened += (_, _) =>
        {
            _entry.Focus();
            _entry.SelectAll();
        };
    }

    private string? Entered() => string.IsNullOrWhiteSpace(_entry.Text) ? null : _entry.Text.Trim();

    public static async Task<string?> ShowAsync(Window owner, string title, string caption, string initial)
    {
        var dialog = new TextPromptDialog(title, caption, initial);
        return await dialog.ShowDialog<string?>(owner);
    }
}
