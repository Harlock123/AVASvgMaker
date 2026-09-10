using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AVASvgMaker.Engine;

namespace AVASvgMaker.Views;

/// <summary>
/// The page tabs along the bottom of the drawing area, as a spreadsheet has them: one tab per
/// page, the current one lit, and a button on the end to add another.
///
/// The strip does the things it can do on its own - switching, adding, reordering - and asks
/// the window for the two that need a dialog, renaming and deleting.
/// </summary>
public class PageTabStrip : UserControl
{
    private readonly StackPanel _tabs = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 2,
        VerticalAlignment = VerticalAlignment.Center
    };

    private DiagramDocument? _document;

    /// <summary>Raised with the page index when the window should put up a rename prompt.</summary>
    public event Action<int>? RenameRequested;

    /// <summary>Raised with the page index when the window should confirm a delete.</summary>
    public event Action<int>? DeleteRequested;

    public PageTabStrip()
    {
        Background = AppTheme.Panel;
        BorderBrush = AppTheme.Border;
        BorderThickness = new Thickness(0, 1, 0, 0);
        Padding = new Thickness(6, 4);

        var add = new Button
        {
            Content = "+",
            Padding = new Thickness(9, 2),
            Margin = new Thickness(4, 0, 0, 0),
            Background = AppTheme.PanelItem,
            Foreground = AppTheme.Text,
            BorderBrush = AppTheme.Border,
            BorderThickness = new Thickness(1),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };

        ToolTip.SetTip(add, "Add a page");
        add.Click += (_, _) => _document?.AddPage();

        Content = new DockPanel
        {
            LastChildFill = true,
            Children =
            {
                Docked(add, Dock.Right),
                new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = _tabs
                }
            }
        };

        AppTheme.Changed += Rebuild;
    }

    private static Control Docked(Control control, Dock dock)
    {
        DockPanel.SetDock(control, dock);
        return control;
    }

    public void Attach(DiagramDocument document)
    {
        _document = document;
        document.PageChanged += Rebuild;
        Rebuild();
    }

    private void Rebuild()
    {
        _tabs.Children.Clear();

        if (_document is null)
            return;

        // A single page is the ordinary case and does not need a tab bar in the way.
        IsVisible = _document.Pages.Count > 1;

        Control? current = null;

        for (var i = 0; i < _document.Pages.Count; i++)
        {
            var tab = Tab(i, _document.Pages[i].Name, i == _document.PageIndex);
            _tabs.Children.Add(tab);

            if (i == _document.PageIndex)
                current = tab;
        }

        // Turning a page from the menu or the keyboard can land on a tab that has scrolled
        // out of the strip, so the strip follows. Deferred, because the tab has no position
        // to scroll to until the layout pass has run.
        if (current is not null)
            Dispatcher.UIThread.Post(() => current.BringIntoView(), DispatcherPriority.Loaded);
    }

    private Control Tab(int index, string name, bool current)
    {
        var tab = new Border
        {
            Background = current ? AppTheme.Accent : AppTheme.PanelItem,
            BorderBrush = AppTheme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3, 3, 0, 0),
            Padding = new Thickness(11, 3),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock
            {
                Text = name,
                FontSize = 12,
                Foreground = current ? Brushes.White : AppTheme.Text,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        tab.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(tab).Properties.IsLeftButtonPressed)
                _document!.PageIndex = index;
        };

        // Double-click to rename, as the tab of a spreadsheet does.
        tab.DoubleTapped += (_, e) =>
        {
            e.Handled = true;
            RenameRequested?.Invoke(index);
        };

        tab.ContextMenu = Menu(index);
        return tab;
    }

    private ContextMenu Menu(int index)
    {
        var menu = new ContextMenu();

        menu.Items.Add(Item("_Rename...", () => RenameRequested?.Invoke(index)));
        menu.Items.Add(Item("_Duplicate", () => _document!.DuplicatePage(index)));
        menu.Items.Add(Item("De_lete", () => DeleteRequested?.Invoke(index),
            enabled: _document!.Pages.Count > 1));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Move _left", () => _document!.MovePage(index, index - 1),
            enabled: index > 0));
        menu.Items.Add(Item("Move _right", () => _document!.MovePage(index, index + 1),
            enabled: index < _document!.Pages.Count - 1));

        return menu;
    }

    private static MenuItem Item(string header, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }
}
