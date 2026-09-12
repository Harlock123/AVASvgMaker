using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SyntaxColorizer;
using SyntaxColorizer.Controls;
using SyntaxColorizer.Themes;

namespace AVASvgMaker.Views;

/// <summary>
/// Shows the page as Mermaid, coloured, to be read and taken away.
///
/// Read-only on purpose: this is the drawing said another way, and the drawing is still on the
/// page behind it. Somewhere to edit the text would invite edits that go nowhere.
///
/// The colouring is <c>SyntaxColorizer</c>'s, which learned Mermaid for this - one of the few
/// highlighters that knows the language rather than approximating it with something adjacent.
/// </summary>
public class MermaidDialog : Window
{
    private readonly string _code;

    private MermaidDialog(string code, string caption)
    {
        _code = code;

        Title = "Mermaid";
        Width = 720;
        Height = 560;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = AppTheme.Panel;

        var editor = new SyntaxHighlightingTextBox
        {
            // The words first and the language after: the highlighter colours what it has
            // when it is told what it is, and told the other way round it has nothing yet.
            Text = code,
            Language = SyntaxLanguage.Mermaid,
            IsReadOnly = true,
            ShowLineNumbers = true,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,Menlo,monospace"),
            FontSize = 13
        };

        // The app takes its colours from the desktop, so the code is written on paper that may
        // be white or nearly black - and a set of colours legible on one is invisible on the
        // other. Followed rather than fixed, and followed again when the desktop changes.
        var paper = new Border
        {
            BorderBrush = AppTheme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = editor
        };

        void Dress()
        {
            var theme = AppTheme.Current.IsLight ? BuiltInThemes.GitHubLight : BuiltInThemes.GitHubDark;

            editor.SyntaxTheme = theme;

            // The editor paints its words but not what they sit on, so the paper is laid
            // under it - otherwise the drawing behind shows through the code.
            paper.Background = theme.DefaultBackground;
        }

        Dress();

        void Redress() => Dispatcher.UIThread.Post(Dress);

        AppTheme.Changed += Redress;
        Closed += (_, _) => AppTheme.Changed -= Redress;

        var copy = new Button { Content = "Copy", MinWidth = 88, IsDefault = true };
        copy.Click += async (_, _) =>
        {
            if (Clipboard is { } board)
                await board.SetTextAsync(_code);

            copy.Content = "Copied";
        };

        var close = new Button { Content = "Close", MinWidth = 88, IsCancel = true };
        close.Click += (_, _) => Close();

        Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                Docked(new TextBlock
                {
                    Text = caption,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = AppTheme.Text,
                    Opacity = 0.75,
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 10)
                }, Dock.Top),

                Docked(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Margin = new Thickness(0, 12, 0, 0),
                    Children = { copy, close }
                }, Dock.Bottom),

                paper
            }
        };
    }

    private static Control Docked(Control control, Dock side)
    {
        DockPanel.SetDock(control, side);
        return control;
    }

    public static async Task ShowAsync(Window owner, string code, string caption) =>
        await new MermaidDialog(code, caption).ShowDialog(owner);
}
