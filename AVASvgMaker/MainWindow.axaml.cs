using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using AVASvgMaker.Views;

namespace AVASvgMaker;

public partial class MainWindow : Window
{
    private const string UntitledName = "Untitled";

    /// <summary>Guards the toolbar against reacting to its own updates while it syncs to the selection.</summary>
    private bool _syncing;

    /// <summary>The file the document was last opened from or saved to.</summary>
    private IStorageFile? _currentFile;

    /// <summary>Set once the unsaved-changes prompt has been answered, to let the close through.</summary>
    private bool _closeConfirmed;

    private readonly UndoStack _history;

    /// <summary>Used when the system clipboard is unavailable, and as the fallback on paste.</summary>
    private string? _internalClipboard;

    private readonly DisplayScaleWatcher? _scaleWatcher;

    public MainWindow()
    {
        InitializeComponent();

        Toolbox.ShapeArmed += kind =>
        {
            Canvas.ArmedKind = kind;

            // Picking a stencil goes back to the select tool.
            if (kind is not null)
                SetTool(EditorTool.Select);

            Canvas.ReportStatus();
        };

        // The canvas clears the armed stencil once it has been placed.
        Canvas.ArmedKindChanged += kind => Toolbox.Arm(kind);
        Canvas.ToolChanged += SyncToolButtons;
        Canvas.SelectionChanged += SyncConnectorStyle;
        Canvas.SelectionChanged += SyncProperties;

        // Anything that edits the page - the connector toolbar, undo, paste, a file being
        // opened - refreshes the panels, so they cannot drift from what is on the page.
        Canvas.Document.Changed += SyncConnectorStyle;
        Canvas.Document.Changed += SyncProperties;

        FillPicker.ColorPicked += OnFillPicked;
        LinePicker.ColorPicked += OnLinePicked;
        TextPicker.ColorPicked += OnTextPicked;
        Canvas.StatusChanged += text => StatusText.Text = text;
        Canvas.DeleteRequested += () => _ = DeleteAsync();
        Canvas.Document.ModifiedChanged += UpdateTitle;

        Canvas.ZoomChanged += SyncZoomBox;

        // A label part-way through being typed belongs to the page it was started on.
        Canvas.Document.PageChanging += Canvas.CommitEdit;
        Canvas.Document.PageChanged += SyncPages;

        PageTabs.Attach(Canvas.Document);
        PageTabs.RenameRequested += index => _ = RenamePageAsync(index);
        PageTabs.DeleteRequested += index => _ = DeletePageAsync(index);

        _history = new UndoStack(Canvas.Document);
        _history.StateChanged += SyncHistoryMenu;

        KeyDown += OnWindowKeyDown;
        Closing += OnWindowClosing;

        // The app takes its scale from the environment at startup and cannot change it while
        // running, so a rescale is reported rather than half-applied. See DisplayScaleWatcher.
        if (DisplayScaleWatcher.IsAvailable)
        {
            _scaleWatcher = new DisplayScaleWatcher(TimeSpan.FromSeconds(10));
            _scaleWatcher.ScaleChanged += scale =>
                StatusText.Text = $"Display scale is now {scale:0.##}x - restart AVASvgMaker " +
                                  "to redraw at that scale";
        }

        FillFontList();

        UpdateTitle();
        SyncHistoryMenu();
        SyncProperties();
        SyncPages();
        Canvas.ReportStatus();
    }

    private void OnGroupClick(object? sender, RoutedEventArgs e)
    {
        var document = Canvas.Document;

        if (document.Group(document.Selection))
        {
            // The group is now the selection, so what was picked and what moves agree.
            document.SetSelection(document.WithGroups(document.Selection));
            StatusText.Text = $"Grouped {document.Selection.Count} shapes";
        }
        else
        {
            StatusText.Text = "Select two or more shapes to group them";
        }

        Canvas.InvalidateVisual();
    }

    private void OnUngroupClick(object? sender, RoutedEventArgs e)
    {
        var document = Canvas.Document;

        StatusText.Text = document.Ungroup(document.Selection)
            ? "Ungrouped"
            : "Nothing selected is in a group";

        Canvas.InvalidateVisual();
    }

    /// <summary>
    /// Puts a pool's lanes back on equal shares. Works from whatever is selected: the pool
    /// itself, or a lane, or a shape sitting in one.
    /// </summary>
    private void OnEvenLanesClick(object? sender, RoutedEventArgs e)
    {
        var pool = PoolOf(Canvas.Document.Selected);

        if (pool is null)
        {
            StatusText.Text = "Select a pool, or something in one, to even its lanes";
            return;
        }

        StatusText.Text = Canvas.Document.EvenLaneHeights(pool)
            ? "Lane heights evened"
            : "Those lanes are already even";

        Canvas.InvalidateVisual();
    }

    /// <summary>The pool a shape belongs to, walking out through whatever contains it.</summary>
    private static DiagramShape? PoolOf(DiagramShape? shape)
    {
        for (var at = shape; at is not null; at = at.Container)
        {
            if (at is ContainerShape { Kind: ShapeKind.Pool })
                return at;
        }

        return null;
    }

    #region Pages

    private void OnNewPageClick(object? sender, RoutedEventArgs e) => Canvas.Document.AddPage();

    private void OnDuplicatePageClick(object? sender, RoutedEventArgs e) =>
        Canvas.Document.DuplicatePage(Canvas.Document.PageIndex);

    private void OnRenamePageClick(object? sender, RoutedEventArgs e) =>
        _ = RenamePageAsync(Canvas.Document.PageIndex);

    private void OnDeletePageClick(object? sender, RoutedEventArgs e) =>
        _ = DeletePageAsync(Canvas.Document.PageIndex);

    private void OnPreviousPageClick(object? sender, RoutedEventArgs e) => Canvas.Document.PageIndex--;

    private void OnNextPageClick(object? sender, RoutedEventArgs e) => Canvas.Document.PageIndex++;

    private void OnMovePageLeftClick(object? sender, RoutedEventArgs e) =>
        Canvas.Document.MovePage(Canvas.Document.PageIndex, Canvas.Document.PageIndex - 1);

    private void OnMovePageRightClick(object? sender, RoutedEventArgs e) =>
        Canvas.Document.MovePage(Canvas.Document.PageIndex, Canvas.Document.PageIndex + 1);

    private async Task RenamePageAsync(int index)
    {
        var document = Canvas.Document;

        if (index < 0 || index >= document.Pages.Count)
            return;

        var name = await TextPromptDialog.ShowAsync(
            this, "Rename page", "Page name", document.Pages[index].Name);

        if (name is not null)
            document.RenamePage(index, name);
    }

    /// <summary>
    /// Deleting a page takes its contents with it and there is no other way back to them, so
    /// a page with anything on it asks first.
    /// </summary>
    private async Task DeletePageAsync(int index)
    {
        var document = Canvas.Document;

        if (index < 0 || index >= document.Pages.Count || document.Pages.Count <= 1)
            return;

        var page = document.Pages[index];

        if (page.Shapes.Count > 0)
        {
            var answer = await ConfirmDialog.ShowAsync(this,
                $"Delete \"{page.Name}\" and the {page.Shapes.Count} " +
                $"{(page.Shapes.Count == 1 ? "shape" : "shapes")} on it?",
                "Delete", "Keep");

            if (answer != ConfirmResult.Primary)
                return;
        }

        document.RemovePage(index);
    }

    /// <summary>Follows the page list: the menu, the indicator, and what the canvas is drawing.</summary>
    private void SyncPages()
    {
        var document = Canvas.Document;
        var count = document.Pages.Count;
        var index = document.PageIndex;

        DeletePageMenuItem.IsEnabled = count > 1;
        PreviousPageMenuItem.IsEnabled = index > 0;
        NextPageMenuItem.IsEnabled = index < count - 1;
        MovePageLeftMenuItem.IsEnabled = index > 0;
        MovePageRightMenuItem.IsEnabled = index < count - 1;

        PageText.Text = count > 1
            ? $"{document.CurrentPage.Name} - {index + 1} of {count}"
            : string.Empty;

        Canvas.CancelInteraction();

        // Pages can be different sizes, so turning to one resizes the canvas under it.
        // SyncPageSize repaints, so there is no separate invalidate here.
        Canvas.SyncPageSize();

        // The status line counts the shapes on the page in front of you, so it has to be
        // asked again once that is a different page.
        Canvas.ReportStatus();
    }

    #endregion

    #region Arranging

    private void OnAlignClick(object? sender, RoutedEventArgs e)
    {
        if (TagOf<AlignEdge>(sender) is not { } edge)
            return;

        if (!Canvas.AlignSelection(edge))
            StatusText.Text = "Select two or more shapes to line up";
    }

    private void OnDistributeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag })
            return;

        if (!Canvas.DistributeSelection(tag == "H"))
            StatusText.Text = "Select three or more shapes to space out";
    }

    private void OnMatchSizeClick(object? sender, RoutedEventArgs e)
    {
        if (TagOf<SizeMatch>(sender) is not { } match)
            return;

        if (!Canvas.MatchSelectionSize(match))
            StatusText.Text = "Select two or more shapes; the last one selected sets the size";
    }

    private void OnOrderClick(object? sender, RoutedEventArgs e)
    {
        if (TagOf<ZOrder>(sender) is not { } order)
            return;

        if (!Canvas.ChangeOrder(order))
            StatusText.Text = "Nothing to reorder";
    }

    private static T? TagOf<T>(object? sender) where T : struct, Enum =>
        sender is MenuItem { Tag: string tag } && Enum.TryParse<T>(tag, out var value) ? value : null;

    #endregion

    #region Formatting

    private void OnTogglePropertiesClick(object? sender, RoutedEventArgs e) =>
        Collapse(PropertiesPanel, PropertiesToggle, "properties", expanded: "\u203A", collapsed: "\u2039");

    private void OnToggleToolboxClick(object? sender, RoutedEventArgs e) =>
        Collapse(ShapesPanel, ToolboxToggle, "shapes", expanded: "\u2039", collapsed: "\u203A");

    /// <summary>
    /// Folds a side panel away. The thin toggle stays behind so the panel can be brought
    /// back, and its arrow turns round to point the way it will open.
    /// </summary>
    private static void Collapse(Border panel, Button toggle, string name, string expanded, string collapsed)
    {
        panel.IsVisible = !panel.IsVisible;

        toggle.Content = panel.IsVisible ? expanded : collapsed;
        ToolTip.SetTip(toggle, panel.IsVisible
            ? $"Hide the {name} panel"
            : $"Show the {name} panel");
    }

    /// <summary>
    /// Choosing a colour also means "not None". The checkbox is set inside the sync guard so
    /// it cannot bounce back through its own handler and re-apply the picker's colour.
    /// </summary>
    private void OnFillPicked(Color colour)
    {
        _syncing = true;
        FillNoneCheck.IsChecked = false;
        _syncing = false;

        Canvas.SetFill(colour);
    }

    private void OnFillNoneChanged(object? sender, RoutedEventArgs e)
    {
        // Null is the "these shapes disagree" state, which is a display, not an instruction.
        if (Canvas is null || _syncing || FillNoneCheck.IsChecked is null)
            return;

        Canvas.SetFill(FillNoneCheck.IsChecked == true ? Colors.Transparent : FillPicker.Color);
    }

    private void OnLinePicked(Color colour)
    {
        _syncing = true;
        LineNoneCheck.IsChecked = false;
        _syncing = false;

        Canvas.SetStroke(colour);
    }

    private void OnLineNoneChanged(object? sender, RoutedEventArgs e)
    {
        if (Canvas is null || _syncing || LineNoneCheck.IsChecked is null)
            return;

        Canvas.SetStroke(LineNoneCheck.IsChecked == true ? Colors.Transparent : LinePicker.Color);
    }

    private void OnLineStyleChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Canvas is null || _syncing)
            return;

        if (LineStyleBox.SelectedItem is ComboBoxItem { Tag: string tag } &&
            Enum.TryParse<StrokeStyle>(tag, out var style))
            Canvas.SetStrokeStyle(style);
    }

    private void OnLineWeightChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Canvas is null || _syncing)
            return;

        Canvas.SetStrokeThickness(SelectedNumber(LineWeightBox, 2));
    }

    private void OnTextPicked(Color colour) => Canvas.SetTextColor(colour);

    private void OnFontSizeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Canvas is null || _syncing)
            return;

        Canvas.SetFontSize(SelectedNumber(FontSizeBox, 13));
    }

    /// <summary>
    /// Fills the font list with what is actually installed, with the application's own font at
    /// the top. Offering fonts the machine does not have would only produce labels that draw
    /// in something else.
    /// </summary>
    private void FillFontList()
    {
        FontBox.Items.Add(new ComboBoxItem { Content = "Default font", Tag = string.Empty });

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var family in FontManager.Current.SystemFonts
                     .Select(f => f.Name)
                     .Where(name => !string.IsNullOrWhiteSpace(name) && seen.Add(name))
                     .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase))
        {
            FontBox.Items.Add(new ComboBoxItem { Content = family, Tag = family });
        }

        FontBox.SelectedIndex = 0;
    }

    private void OnFontChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Canvas is null || _syncing)
            return;

        Canvas.SetFontName(FontBox.SelectedItem is ComboBoxItem { Tag: string name } ? name : string.Empty);
    }

    private void OnBoldClick(object? sender, RoutedEventArgs e)
    {
        if (_syncing)
            return;

        Canvas.SetBold(BoldToggle.IsChecked == true);
    }

    private void OnItalicClick(object? sender, RoutedEventArgs e)
    {
        if (_syncing)
            return;

        Canvas.SetItalic(ItalicToggle.IsChecked == true);
    }

    /// <summary>The three alignment buttons behave as one group: picking one drops the others.</summary>
    private void OnTextAlignClick(object? sender, RoutedEventArgs e)
    {
        if (_syncing || sender is not ToggleButton { Tag: string tag })
            return;

        var wanted = Enum.TryParse<TextAlign>(tag, out var value) ? value : TextAlign.Center;

        Canvas.SetTextAlign(wanted);
        SyncProperties();
    }

    /// <summary>
    /// Shows the formatting of the selection, or of the defaults when nothing is selected.
    /// Where the selected shapes disagree the control shows a mixed state rather than the
    /// first shape's value, which would claim a uniformity that is not there.
    /// </summary>
    private void SyncProperties()
    {
        var selection = Canvas.Document.Selection;
        var style = Canvas.Document.Selected is { } shape
            ? ShapeStyle.From(shape)
            : Canvas.DefaultStyle;

        var wasSyncing = _syncing;
        _syncing = true;

        PropertiesHint.Text = selection.Count switch
        {
            0 => "Nothing selected - changes set the formatting for the next shape.",
            1 => $"{ShapeFactory.DisplayName(Canvas.Document.Selected!.Kind)} selected.",
            _ => $"{selection.Count} shapes selected."
        };

        var fillMixed = Differs(selection, s => s.Fill);
        FillPicker.IsMixed = fillMixed;
        FillNoneCheck.IsChecked = fillMixed ? null : style.Fill.A == 0;

        if (!fillMixed && style.Fill.A != 0)
            FillPicker.Color = style.Fill;

        var lineMixed = Differs(selection, s => s.Stroke);
        LinePicker.IsMixed = lineMixed;
        LineNoneCheck.IsChecked = lineMixed ? null : style.Stroke.A == 0;

        if (!lineMixed && style.Stroke.A != 0)
            LinePicker.Color = style.Stroke;

        var textMixed = Differs(selection, s => s.TextColor);
        TextPicker.IsMixed = textMixed;

        if (!textMixed)
            TextPicker.Color = style.TextColor;

        Choose(LineStyleBox, style.StrokeStyle.ToString(), Differs(selection, s => s.StrokeStyle));
        Choose(LineWeightBox, Whole(style.StrokeThickness), Differs(selection, s => s.StrokeThickness));
        Choose(FontSizeBox, Whole(style.FontSize), Differs(selection, s => s.FontSize));
        Choose(FontBox, style.FontName, Differs(selection, s => s.FontName));

        // A mixed selection leaves the toggle indeterminate rather than claiming either state.
        BoldToggle.IsChecked = Differs(selection, s => s.Bold) ? null : style.Bold;
        ItalicToggle.IsChecked = Differs(selection, s => s.Italic) ? null : style.Italic;

        var alignMixed = Differs(selection, s => s.TextAlign);

        AlignLeftToggle.IsChecked = !alignMixed && style.TextAlign == TextAlign.Left;
        AlignCenterToggle.IsChecked = !alignMixed && style.TextAlign == TextAlign.Center;
        AlignRightToggle.IsChecked = !alignMixed && style.TextAlign == TextAlign.Right;

        _syncing = wasSyncing;
    }

    /// <summary>True when the selected shapes do not all share one value.</summary>
    private static bool Differs<T>(IReadOnlyList<DiagramShape> shapes, Func<DiagramShape, T> value) =>
        shapes.Count > 1 &&
        shapes.Skip(1).Any(shape => !EqualityComparer<T>.Default.Equals(value(shape), value(shapes[0])));

    private static string Whole(double value) => ((int)value).ToString(CultureInfo.InvariantCulture);

    /// <summary>Picks the matching entry, or shows a dash when the shapes disagree.</summary>
    private static void Choose(ComboBox box, string tag, bool mixed)
    {
        if (mixed)
        {
            box.SelectedItem = null;
            box.PlaceholderText = "\u2014";
            return;
        }

        SelectByTag(box, tag);
    }

    #endregion

    #region Zoom

    private void OnZoomInClick(object? sender, RoutedEventArgs e) => Canvas.ZoomBy(1.25);

    private void OnZoomOutClick(object? sender, RoutedEventArgs e) => Canvas.ZoomBy(1 / 1.25);

    private void OnZoomResetClick(object? sender, RoutedEventArgs e) => Canvas.SetZoom(1);

    private void OnZoomFitClick(object? sender, RoutedEventArgs e) => Canvas.ZoomToFit();

    private void OnZoomBoxChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Canvas is null || _syncing)
            return;

        if (ZoomBox.SelectedItem is not ComboBoxItem { Tag: string tag })
            return;

        if (tag == "fit")
            Canvas.ZoomToFit();
        else if (double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out var zoom))
            Canvas.SetZoom(zoom);
    }

    /// <summary>Shows the zoom the canvas actually settled on, which may be no listed step.</summary>
    private void SyncZoomBox(double zoom)
    {
        _syncing = true;

        var percent = $"{zoom * 100:0}%";
        var match = ZoomBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => (string?)item.Content == percent);

        if (match is not null)
        {
            ZoomBox.SelectedItem = match;
        }
        else
        {
            // Not one of the steps - show the real figure rather than a stale one.
            ZoomBox.SelectedItem = null;
            ZoomBox.PlaceholderText = percent;
        }

        _syncing = false;
    }

    #endregion

    #region Clipboard

    private async void OnCopyClick(object? sender, RoutedEventArgs e) => await CopyAsync();

    private async void OnCutClick(object? sender, RoutedEventArgs e)
    {
        if (await CopyAsync())
            Canvas.DeleteSelected();
    }

    private async void OnPasteClick(object? sender, RoutedEventArgs e)
    {
        var json = await ReadClipboardAsync() ?? _internalClipboard;

        if (!Canvas.Paste(json))
            StatusText.Text = "Nothing to paste";
    }

    private void OnDuplicateClick(object? sender, RoutedEventArgs e)
    {
        if (!Canvas.DuplicateSelection())
            StatusText.Text = "Select something to duplicate";
    }

    private void OnSelectAllClick(object? sender, RoutedEventArgs e) => Canvas.SelectAll();

    private async Task<bool> CopyAsync()
    {
        Canvas.CommitEdit();

        var json = Canvas.CopySelection();

        if (json is null)
        {
            StatusText.Text = "Select something to copy";
            return false;
        }

        _internalClipboard = json;

        // The clipboard carries the same JSON as the file format, so shapes can be
        // pasted into another instance of the app.
        try
        {
            if (Clipboard is { } clipboard)
                await clipboard.SetTextAsync(json);
        }
        catch
        {
            // A clipboard the platform will not give us is not worth failing the copy over.
        }

        var count = Canvas.Document.Selection.Count;
        StatusText.Text = $"Copied {count} shape{(count == 1 ? string.Empty : "s")}";
        return true;
    }

    /// <summary>The system clipboard, but only when it holds something this app wrote.</summary>
    private async Task<string?> ReadClipboardAsync()
    {
        try
        {
            if (Clipboard is not { } clipboard)
                return null;

            var text = await clipboard.GetTextAsync();
            return ShapeClipboard.CanPaste(text) ? text : null;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Undo

    private void OnUndoClick(object? sender, RoutedEventArgs e)
    {
        // Commit first so a label being typed is not lost, then undo it along with the rest.
        Canvas.CommitEdit();

        _history.Undo();
        AfterHistoryStep();
    }

    private void OnRedoClick(object? sender, RoutedEventArgs e)
    {
        Canvas.CommitEdit();

        _history.Redo();
        AfterHistoryStep();
    }

    /// <summary>The page size and the selection can both have changed, so refresh everything.</summary>
    private void AfterHistoryStep()
    {
        Canvas.CancelInteraction();
        Canvas.SyncPageSize();
        SyncConnectorStyle();
        Canvas.ReportStatus();
        Canvas.Focus();
    }

    private void SyncHistoryMenu()
    {
        UndoMenuItem.IsEnabled = _history.CanUndo;
        RedoMenuItem.IsEnabled = _history.CanRedo;
    }

    #endregion

    #region File

    private void UpdateTitle()
    {
        var name = _currentFile?.Name ?? UntitledName;
        var marker = Canvas.Document.IsModified ? "*" : string.Empty;

        Title = $"AVASvgMaker - {name}{marker}";
    }

    private async void OnNewClick(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardAsync())
            return;

        Canvas.CommitEdit();
        Canvas.Document.ReplaceWith(new DiagramDocument());
        Canvas.SyncPageSize();

        _currentFile = null;
        _history.Reset();
        UpdateTitle();
        Canvas.ReportStatus();
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardAsync())
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open diagram",
            AllowMultiple = false,
            FileTypeFilter = [DiagramFileType, FilePickerFileTypes.All]
        });

        if (files.Count == 0)
            return;

        await OpenAsync(files[0]);
    }

    private async Task OpenAsync(IStorageFile file)
    {
        try
        {
            Canvas.CommitEdit();

            await using var stream = await file.OpenReadAsync();
            var document = DiagramFile.Read(stream);

            Canvas.Document.ReplaceWith(document);
            Canvas.SyncPageSize();

            _currentFile = file;
            _history.Reset();
            UpdateTitle();

            StatusText.Text = $"Opened {file.Name}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not open {file.Name}: {ex.Message}";
        }
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e) => await SaveAsync();

    private async void OnSaveAsClick(object? sender, RoutedEventArgs e) => await SaveAsAsync();

    /// <summary>Saves to the current file, asking for one if the document has never been saved.</summary>
    private async Task<bool> SaveAsync()
    {
        if (_currentFile is null)
            return await SaveAsAsync();

        return await WriteAsync(_currentFile);
    }

    private async Task<bool> SaveAsAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save diagram",
            SuggestedFileName = _currentFile?.Name ?? $"{UntitledName}.{DiagramFile.Extension}",
            DefaultExtension = DiagramFile.Extension,
            FileTypeChoices = [DiagramFileType]
        });

        return file is not null && await WriteAsync(file);
    }

    private async Task<bool> WriteAsync(IStorageFile file)
    {
        try
        {
            Canvas.CommitEdit();

            await using var stream = await file.OpenWriteAsync();
            DiagramFile.Write(Canvas.Document, stream);
            await stream.FlushAsync();

            // Overwriting a longer file would otherwise leave its tail behind.
            if (stream.CanSeek)
                stream.SetLength(stream.Position);

            _currentFile = file;
            Canvas.Document.MarkSaved();
            _history.MarkSaved();
            UpdateTitle();

            StatusText.Text = $"Saved {file.Name}";
            return true;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not save: {ex.Message}";
            return false;
        }
    }

    /// <summary>Returns true when it is safe to throw the current document away.</summary>
    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!Canvas.Document.IsModified)
            return true;

        var name = _currentFile?.Name ?? UntitledName;
        var answer = await ConfirmDialog.ShowAsync(this, $"Save the changes you made to {name}?");

        return answer switch
        {
            ConfirmResult.Primary => await SaveAsync(),
            ConfirmResult.Secondary => true,
            _ => false
        };
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || !Canvas.Document.IsModified)
            return;

        e.Cancel = true;

        if (!await ConfirmDiscardAsync())
            return;

        _closeConfirmed = true;
        Close();
    }

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _scaleWatcher?.Dispose();
    }

    private static FilePickerFileType DiagramFileType => new("AVASvgMaker diagram")
    {
        Patterns = [$"*.{DiagramFile.Extension}"]
    };

    #endregion

    #region Tools

    private void OnToolClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string tag })
            return;

        if (!Enum.TryParse<EditorTool>(tag, out var tool))
            return;

        SetTool(tool);
    }

    private void SetTool(EditorTool tool)
    {
        Canvas.Tool = tool;

        if (tool != EditorTool.Select)
            Toolbox.Arm(null);

        SyncToolButtons(tool);
        Canvas.ReportStatus();
        Canvas.Focus();
    }

    private void SyncToolButtons(EditorTool tool)
    {
        SelectToolButton.IsChecked = tool == EditorTool.Select;
        TextToolButton.IsChecked = tool == EditorTool.Text;
        ConnectorToolButton.IsChecked = tool == EditorTool.Connector;
    }

    #endregion

    #region Connector styling

    private void OnConnectorStyleChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Canvas is null || _syncing)
            return;

        Canvas.DefaultStartCap = SelectedCap(StartCapBox);
        Canvas.DefaultEndCap = SelectedCap(EndCapBox);
        Canvas.DefaultLineWidth = SelectedNumber(WeightBox, 2);
        Canvas.DefaultRouting = SelectedRouting(RoutingBox);

        Canvas.ApplyConnectorStyle();

        // Weight is shape formatting; both this box and the panel's go through one path.
        Canvas.SetStrokeThickness(Canvas.DefaultLineWidth);

        Canvas.ReportStatus();
    }

    /// <summary>Selecting a connector pulls its line ends back into the toolbar.</summary>
    private void SyncConnectorStyle()
    {
        if (Canvas.Document.Selected is not ConnectorShape connector)
            return;

        // A sync can be triggered from inside another one; restore rather than clear.
        var wasSyncing = _syncing;
        _syncing = true;

        SelectByTag(StartCapBox, connector.StartCap.ToString());
        SelectByTag(EndCapBox, connector.EndCap.ToString());
        SelectByTag(WeightBox, ((int)connector.StrokeThickness).ToString(CultureInfo.InvariantCulture));
        SelectByTag(RoutingBox, connector.Routing.ToString());

        Canvas.DefaultStartCap = connector.StartCap;
        Canvas.DefaultEndCap = connector.EndCap;
        Canvas.DefaultLineWidth = connector.StrokeThickness;
        Canvas.DefaultRouting = connector.Routing;

        _syncing = wasSyncing;
    }

    private static ConnectorRouting SelectedRouting(ComboBox box) =>
        box.SelectedItem is ComboBoxItem { Tag: string tag } &&
        Enum.TryParse<ConnectorRouting>(tag, out var routing)
            ? routing
            : ConnectorRouting.Orthogonal;

    private static EndCapStyle SelectedCap(ComboBox box) =>
        box.SelectedItem is ComboBoxItem { Tag: string tag } && Enum.TryParse<EndCapStyle>(tag, out var cap)
            ? cap
            : EndCapStyle.None;

    private static double SelectedNumber(ComboBox box, double fallback) =>
        box.SelectedItem is ComboBoxItem { Tag: string tag } &&
        double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static void SelectByTag(ComboBox box, string tag)
    {
        foreach (var item in box.Items)
        {
            if (item is ComboBoxItem candidate && (string?)candidate.Tag == tag)
            {
                box.SelectedItem = candidate;
                return;
            }
        }
    }

    #endregion

    #region Grid

    private void OnGridOptionChanged(object? sender, RoutedEventArgs e)
    {
        // Fires once during XAML load, before the canvas field is assigned.
        if (Canvas is null)
            return;

        Canvas.Grid.SnapToGrid = SnapCheck.IsChecked == true;
        Canvas.Grid.ShowGrid = ShowGridCheck.IsChecked == true;
        Canvas.InvalidateVisual();
    }

    private void OnGridSizeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Canvas is null)
            return;

        Canvas.Grid.Size = SelectedNumber(GridSizeBox, Canvas.Grid.Size);
        Canvas.InvalidateVisual();
    }

    #endregion

    private async void OnDeleteClick(object? sender, RoutedEventArgs e) => await DeleteAsync();

    /// <summary>
    /// Deleting a container asks what should become of what is inside it. Answering no keeps
    /// the contents and drops them onto the page, which is nearly always what was meant when
    /// a pool is deleted by accident.
    /// </summary>
    private async Task DeleteAsync()
    {
        var contents = Canvas.SelectedContainerContents();

        if (contents > 0)
        {
            var answer = await ConfirmDialog.ShowAsync(this,
                $"Delete the {contents} shape{(contents == 1 ? string.Empty : "s")} inside as well?",
                primary: "Delete contents",
                secondary: "Keep contents");

            if (answer == ConfirmResult.Cancel)
                return;

            Canvas.DeleteSelected(deleteContents: answer == ConfirmResult.Primary);
            return;
        }

        Canvas.DeleteSelected();
    }

    private void OnResetRouteClick(object? sender, RoutedEventArgs e) => Canvas.ResetRoutes();

    private void OnClearClick(object? sender, RoutedEventArgs e) => Canvas.ClearPage();

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F9:
                OnToggleToolboxClick(sender, e);
                e.Handled = true;
                return;

            case Key.F10:
                OnTogglePropertiesClick(sender, e);
                e.Handled = true;
                return;
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return;

        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        // While a label is being typed, these belong to the text box.
        if (e.Key is Key.Z or Key.Y or Key.C or Key.X or Key.V or Key.A &&
            FocusManager?.GetFocusedElement() is TextBox)
            return;

        switch (e.Key)
        {
            case Key.Z when shift:
            case Key.Y:
                OnRedoClick(sender, e);
                break;

            case Key.Z:
                OnUndoClick(sender, e);
                break;

            case Key.N:
                OnNewClick(sender, e);
                break;

            case Key.G when shift:
                OnUngroupClick(sender, e);
                break;

            case Key.G:
                OnGroupClick(sender, e);
                break;

            case Key.P when shift:
                OnNewPageClick(sender, e);
                break;

            case Key.PageUp:
                Canvas.Document.PageIndex--;
                break;

            case Key.PageDown:
                Canvas.Document.PageIndex++;
                break;

            case Key.O:
                OnOpenClick(sender, e);
                break;

            case Key.S when shift:
                _ = SaveAsAsync();
                break;

            case Key.S:
                _ = SaveAsync();
                break;

            case Key.E when shift:
                _ = ExportRasterAsync(RasterFormat.Png);
                break;

            case Key.E:
                _ = ExportSvgAsync();
                break;

            case Key.C:
                OnCopyClick(sender, e);
                break;

            case Key.X:
                OnCutClick(sender, e);
                break;

            case Key.V:
                OnPasteClick(sender, e);
                break;

            case Key.D:
                OnDuplicateClick(sender, e);
                break;

            case Key.A:
                Canvas.SelectAll();
                break;

            case Key.OemPlus:
            case Key.Add:
                Canvas.ZoomBy(1.25);
                break;

            case Key.OemMinus:
            case Key.Subtract:
                Canvas.ZoomBy(1 / 1.25);
                break;

            case Key.D0:
                Canvas.SetZoom(1);
                break;

            case Key.D9:
                Canvas.ZoomToFit();
                break;

            case Key.F when shift:
                Canvas.ChangeOrder(ZOrder.Front);
                break;

            case Key.B when shift:
                Canvas.ChangeOrder(ZOrder.Back);
                break;

            case Key.OemCloseBrackets:
                Canvas.ChangeOrder(ZOrder.Forward);
                break;

            case Key.OemOpenBrackets:
                Canvas.ChangeOrder(ZOrder.Backward);
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    private async void OnPageSetupClick(object? sender, RoutedEventArgs e)
    {
        Canvas.CommitEdit();

        var document = Canvas.Document;
        var setup = await PageSetupDialog.ShowAsync(
            this, document.PageWidth, document.PageHeight, document.DrawingBounds,
            document.Pages.Count);

        if (setup is null)
            return;

        document.SetPageSize(setup.Size.Width, setup.Size.Height, setup.AllPages);
        Canvas.SyncPageSize();

        var what = setup.AllPages && document.Pages.Count > 1 ? "Every page" : "This page";
        StatusText.Text = $"{what} is now {setup.Size.Width:0} x {setup.Size.Height:0}";
    }

    private void OnExportSvgClick(object? sender, RoutedEventArgs e) => _ = ExportSvgAsync();

    private void OnExportPngClick(object? sender, RoutedEventArgs e) =>
        _ = ExportRasterAsync(RasterFormat.Png);

    private void OnExportJpegClick(object? sender, RoutedEventArgs e) =>
        _ = ExportRasterAsync(RasterFormat.Jpeg);

    private void OnExportWebpClick(object? sender, RoutedEventArgs e) =>
        _ = ExportRasterAsync(RasterFormat.Webp);

    private void OnExportBmpClick(object? sender, RoutedEventArgs e) =>
        _ = ExportRasterAsync(RasterFormat.Bmp);

    private void OnExportPdfClick(object? sender, RoutedEventArgs e) => _ = ExportPdfAsync();

    /// <summary>The four raster formats are one export; only the bytes at the end of it differ.</summary>
    private async Task ExportRasterAsync(RasterFormat format)
    {
        Canvas.CommitEdit();

        var options = await RasterExportDialog.ShowAsync(this, Canvas.Document, format);

        if (options is null)
            return;

        var file = await AskWhereToPutAsync(format.Label(), format.Extension(),
            new FilePickerFileType(format.Description())
            {
                Patterns = format.Patterns(),
                MimeTypes = [format.MimeType()]
            });

        if (file is null)
            return;

        try
        {
            await using var stream = await file.OpenWriteAsync();
            RasterExporter.Export(Canvas.Document, stream, options.Scale, format, options.Quality);
            await stream.FlushAsync();

            if (stream.CanSeek)
                stream.SetLength(stream.Position);

            var size = RasterExporter.SizeAt(Canvas.Document, options.Scale);
            StatusText.Text = $"Exported {file.Name} at {size.Width} x {size.Height}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Export failed: {ex.Message}";
        }
    }

    /// <summary>PDF is a vector export, so there is no size to settle first.</summary>
    private async Task ExportPdfAsync()
    {
        Canvas.CommitEdit();

        var document = Canvas.Document;
        var allPages = true;

        // PDF is the one export that can hold a whole document, so it is the one that has to
        // ask. Everything else writes a single picture, and so writes the page in front of you.
        if (document.Pages.Count > 1)
        {
            var answer = await ConfirmDialog.ShowAsync(this,
                $"This document has {document.Pages.Count} pages.",
                "All pages", "This page only");

            if (answer == ConfirmResult.Cancel)
                return;

            allPages = answer == ConfirmResult.Primary;
        }

        var file = await AskWhereToPutAsync("PDF", "pdf",
            new FilePickerFileType("PDF document")
            {
                Patterns = ["*.pdf"],
                MimeTypes = ["application/pdf"]
            });

        if (file is null)
            return;

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await PdfExporter.ExportAsync(
                document, stream, Path.GetFileNameWithoutExtension(file.Name), allPages);
            await stream.FlushAsync();

            if (stream.CanSeek)
                stream.SetLength(stream.Position);

            var written = allPages ? document.Pages.Count : 1;

            StatusText.Text = written == 1
                ? $"Exported {file.Name}"
                : $"Exported {file.Name} - {written} pages";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Export failed: {ex.Message}";
        }
    }

    /// <summary>The save picker every export shows, named after the drawing.</summary>
    private async Task<IStorageFile?> AskWhereToPutAsync(
        string label, string extension, FilePickerFileType type)
    {
        var suggested = Path.GetFileNameWithoutExtension(_currentFile?.Name ?? "diagram");

        return await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Export {label}",
            SuggestedFileName = $"{suggested}.{extension}",
            DefaultExtension = extension,
            FileTypeChoices = [type]
        });
    }

    private async Task ExportSvgAsync()
    {
        Canvas.CommitEdit();

        var suggested = Path.GetFileNameWithoutExtension(_currentFile?.Name ?? "diagram");

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export SVG",
            SuggestedFileName = $"{suggested}.svg",
            DefaultExtension = "svg",
            FileTypeChoices =
            [
                new FilePickerFileType("SVG image")
                {
                    Patterns = ["*.svg"],
                    MimeTypes = ["image/svg+xml"]
                }
            ]
        });

        if (file is null)
            return;

        try
        {
            var svg = SvgExporter.Export(Canvas.Document);

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(svg);

            StatusText.Text = $"Exported to {file.Name}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Export failed: {ex.Message}";
        }
    }
}
