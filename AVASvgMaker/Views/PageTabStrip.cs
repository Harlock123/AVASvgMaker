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

    /// <summary>How far the pointer has to travel before a click becomes a drag.</summary>
    private const double DragThreshold = 5;

    private bool _armed;        // The pointer went down on a tab; it may yet become a drag.
    private bool _dragging;
    private int _dragFrom;      // Where the page started, for the one move recorded at the end.
    private int _dragTo;        // Where the tab sits now, among the others.
    private Point _pressedAt;
    private Border? _dragTab;

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

        // Rebuilding mid-drag would throw away the tab being dragged.
        if (_document is null || _dragging)
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
            if (!e.GetCurrentPoint(tab).Properties.IsLeftButtonPressed)
                return;

            _document!.PageIndex = index;

            // Armed rather than dragging: a click that never moves is still a click, and
            // capturing here would keep the second press of a double-click from arriving.
            _armed = true;
            _dragFrom = _dragTo = index;
            _pressedAt = e.GetPosition(_tabs);
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

    #region Dragging a tab

    /// <summary>
    /// Tabs are dragged into place directly: the tab follows the pointer through its
    /// neighbours and the page moves with it on release, as one undoable step rather than one
    /// per neighbour passed.
    ///
    /// The pointer is captured by the strip rather than by the tab, because the tab is taken
    /// out of the panel and put back at each swap, and a control that leaves the visual tree
    /// loses its capture.
    /// </summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_armed || _document is null)
            return;

        var at = e.GetPosition(_tabs);

        if (!_dragging)
        {
            if (Math.Abs(at.X - _pressedAt.X) < DragThreshold)
                return;

            if (_dragFrom >= _tabs.Children.Count || _tabs.Children[_dragFrom] is not Border tab)
                return;

            _dragging = true;
            _dragTab = tab;
            _dragTab.Opacity = 0.65;

            e.Pointer.Capture(this);
        }

        var target = PositionAt(at.X);

        if (target == _dragTo)
            return;

        _tabs.Children.RemoveAt(_dragTo);
        _tabs.Children.Insert(target, _dragTab!);
        _dragTo = target;

        // Dragging towards an end can push the tab past the edge of a strip too narrow to
        // show them all. Scrolling moves the content, not the coordinates the drag is
        // measured in, so this is safe to do mid-drag.
        _dragTab!.BringIntoView();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!_armed)
            return;

        var moved = _dragging && _dragTo != _dragFrom;
        var from = _dragFrom;
        var to = _dragTo;

        Settle(e.Pointer);

        // One step for the whole drag, however many tabs it crossed.
        if (moved)
            _document!.MovePage(from, to);
        else
            Rebuild();
    }

    /// <summary>A drag cut short - the window lost focus, or something else took the pointer.</summary>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        if (!_armed)
            return;

        Settle(e.Pointer);
        Rebuild();
    }

    private void Settle(IPointer pointer)
    {
        if (_dragTab is not null)
            _dragTab.Opacity = 1;

        if (_dragging)
            pointer.Capture(null);

        _armed = false;
        _dragging = false;
        _dragTab = null;
    }

    /// <summary>Which slot the pointer is over, by the middles of the tabs as they sit now.</summary>
    private int PositionAt(double x)
    {
        for (var i = 0; i < _tabs.Children.Count; i++)
        {
            var bounds = _tabs.Children[i].Bounds;

            if (x < bounds.X + bounds.Width / 2)
                return i;
        }

        return Math.Max(0, _tabs.Children.Count - 1);
    }

    #endregion

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
