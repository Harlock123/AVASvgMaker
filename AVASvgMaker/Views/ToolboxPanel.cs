using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;

namespace AVASvgMaker.Views;

/// <summary>
/// The stencil catalogue down the left edge, grouped into categories that fold away and
/// filtered by a search box. A shape can be dragged onto the page, or clicked to arm it and
/// then placed with a click on the page.
/// </summary>
public class ToolboxPanel : UserControl
{
    /// <summary>Drag payload format carrying the <see cref="ShapeKind"/> name.</summary>
    public const string DragFormat = "AVASvgMaker.ShapeKind";

    /// <summary>Drag payload for a shape of your own, carrying its id.</summary>
    public const string CustomDragFormat = "AVASvgMaker.CustomStencil";

    private static readonly IBrush ItemBackground = AppTheme.PanelItem;
    private static readonly IBrush ItemArmedBackground = AppTheme.Accent;
    private static readonly IBrush ItemBorder = AppTheme.Border;

    private readonly List<(Stencil Stencil, Border Item)> _items = [];
    private readonly List<(StencilCategory Category, Expander Group, StackPanel Items)> _groups = [];

    /// <summary>The shapes you saved yourself, in a category of their own above the rest.</summary>
    private readonly StackPanel _mine = new() { Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };

    private readonly Expander _mineGroup;

    /// <summary>Which of your own shapes is armed, or null. Kept apart from ArmedKind: a
    /// saved fragment is several shapes and has no single kind to be.</summary>
    public string? ArmedStencil { get; private set; }

    /// <summary>Raised when one of your own shapes is armed, or unarmed with null.</summary>
    public event Action<string?>? StencilArmed;

    /// <summary>Raised when a saved shape should be renamed or deleted, which needs a dialog.</summary>
    public event Action<CustomStencil>? RenameRequested;

    public event Action<CustomStencil>? DeleteRequested;
    private readonly TextBox _search;
    private readonly TextBlock _empty;

    /// <summary>Raised when a stencil is clicked; null means the armed stencil was cleared.</summary>
    public event Action<ShapeKind?>? ShapeArmed;

    public ShapeKind? ArmedKind { get; private set; }

    public ToolboxPanel()
    {
        _search = new TextBox
        {
            Watermark = "Search shapes",
            FontSize = 12,
            Margin = new Thickness(8, 8, 8, 4)
        };

        // The property change rather than TextChanged, so the filter also follows the box
        // being set from code and not only someone typing into it.
        _search.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.TextProperty)
                ApplyFilter();
        };

        var stack = new StackPanel { Spacing = 4, Margin = new Thickness(8, 0, 8, 8) };

        _mineGroup = new Expander
        {
            Header = "My shapes",
            IsExpanded = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = _mine
        };

        stack.Children.Add(_mineGroup);

        StencilLibrary.Changed += RefreshMine;
        RefreshMine();

        foreach (var category in Enum.GetValues<StencilCategory>())
        {
            var items = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };

            foreach (var stencil in StencilCatalogue.InCategory(category))
            {
                var item = CreateItem(stencil);
                _items.Add((stencil, item));
                items.Children.Add(item);
            }

            var group = new Expander
            {
                Header = StencilCatalogue.CategoryName(category),
                // Only the first category starts open, so the whole catalogue is not a wall.
                IsExpanded = category == StencilCategory.Basic,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = items
            };

            _groups.Add((category, group, items));
            stack.Children.Add(group);
        }

        _empty = new TextBlock
        {
            Text = "No shapes match.",
            FontSize = 11,
            Opacity = 0.6,
            Margin = new Thickness(2, 8, 2, 0),
            IsVisible = false
        };

        stack.Children.Add(_empty);

        stack.Children.Add(new TextBlock
        {
            Text = "Drag a shape onto the page, or click one and then click the page.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Opacity = 0.6,
            Margin = new Thickness(2, 12, 2, 0)
        });

        var header = new TextBlock
        {
            Text = "SHAPES",
            FontWeight = FontWeight.Bold,
            FontSize = 11,
            Opacity = 0.7,
            Margin = new Thickness(10, 10, 10, 0)
        };

        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = stack
        };

        // Header and search stay put; the catalogue below them scrolls.
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_search, Dock.Top);

        Content = new DockPanel
        {
            LastChildFill = true,
            Children = { header, _search, scroller }
        };
    }

    private Border CreateItem(Stencil stencil)
    {
        var layout = new DockPanel { LastChildFill = true };

        var preview = new ShapePreview(stencil.Kind)
        {
            Width = 40,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0)
        };

        DockPanel.SetDock(preview, Dock.Left);
        layout.Children.Add(preview);

        layout.Children.Add(new TextBlock
        {
            Text = stencil.Name,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12
        });

        var border = new Border
        {
            Background = ItemBackground,
            BorderBrush = ItemBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6),
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = stencil.Kind,
            Child = layout
        };

        border.PointerPressed += OnItemPointerPressed;
        return border;
    }

    /// <summary>
    /// Rebuilds the list of your own shapes. The category is hidden entirely while there are
    /// none, so an empty heading is not the first thing in the toolbox.
    /// </summary>
    private void RefreshMine()
    {
        _mine.Children.Clear();

        var saved = StencilLibrary.All;
        _mineGroup.IsVisible = saved.Count > 0;

        foreach (var stencil in saved)
            _mine.Children.Add(CreateCustomItem(stencil));

        if (ArmedStencil is not null && StencilLibrary.Find(ArmedStencil) is null)
            ArmStencil(null);
    }

    private Border CreateCustomItem(CustomStencil stencil)
    {
        var layout = new DockPanel { LastChildFill = true };

        var preview = new FragmentPreview(stencil.Fragment)
        {
            Width = 40,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0)
        };

        DockPanel.SetDock(preview, Dock.Left);
        layout.Children.Add(preview);

        layout.Children.Add(new TextBlock
        {
            Text = stencil.Name,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12
        });

        var border = new Border
        {
            Background = stencil.Id == ArmedStencil ? ItemArmedBackground : ItemBackground,
            BorderBrush = ItemBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6),
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = stencil,
            Child = layout
        };

        var menu = new ContextMenu();
        menu.Items.Add(Item("_Rename...", () => RenameRequested?.Invoke(stencil)));
        menu.Items.Add(Item("De_lete", () => DeleteRequested?.Invoke(stencil)));
        border.ContextMenu = menu;

        border.PointerPressed += OnCustomItemPointerPressed;
        return border;
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private async void OnCustomItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { Tag: CustomStencil stencil })
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        ArmStencil(ArmedStencil == stencil.Id ? null : stencil.Id);
        e.Handled = true;

        if (ArmedStencil is null)
            return;

        var data = new DataObject();
        data.Set(CustomDragFormat, stencil.Id);

        await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
    }

    /// <summary>Arms one of your own shapes, which unarms any catalogue shape and vice versa.</summary>
    public void ArmStencil(string? id)
    {
        ArmedStencil = id;

        if (id is not null && ArmedKind is not null)
            Arm(null);

        foreach (var item in _mine.Children.OfType<Border>())
            item.Background = (item.Tag as CustomStencil)?.Id == id ? ItemArmedBackground : ItemBackground;

        StencilArmed?.Invoke(id);
    }

    /// <summary>Hides what does not match, and opens any category that still has something.</summary>
    private void ApplyFilter()
    {
        var search = _search.Text ?? string.Empty;
        var searching = !string.IsNullOrWhiteSpace(search);
        var found = 0;

        foreach (var (stencil, item) in _items)
        {
            var matches = StencilCatalogue.Matches(stencil, search);
            item.IsVisible = matches;

            if (matches)
                found++;
        }

        foreach (var (category, group, _) in _groups)
        {
            var any = _items.Any(entry =>
                entry.Stencil.Category == category && entry.Item.IsVisible);

            group.IsVisible = any;

            if (searching)
                group.IsExpanded = any;
            else if (category != StencilCategory.Basic)
                group.IsExpanded = false;
        }

        // Your own shapes are not searched: there are few of them and they are yours to find.
        _mineGroup.IsVisible = StencilLibrary.All.Count > 0 && !searching;

        _empty.IsVisible = found == 0;
    }

    private async void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { Tag: ShapeKind kind })
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        Arm(ArmedKind == kind ? null : kind);
        e.Handled = true;

        if (ArmedKind is null)
            return;

        var data = new DataObject();
        data.Set(DragFormat, kind.ToString());
        data.Set(DataFormats.Text, kind.ToString());

        await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
    }

    public void Arm(ShapeKind? kind)
    {
        ArmedKind = kind;

        if (kind is not null && ArmedStencil is not null)
            ArmStencil(null);

        foreach (var (stencil, item) in _items)
            item.Background = stencil.Kind == kind ? ItemArmedBackground : ItemBackground;

        ShapeArmed?.Invoke(kind);
    }
}
