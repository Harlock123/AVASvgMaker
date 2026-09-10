using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;

namespace AVASvgMaker.Views;

/// <summary>What a click on the page does.</summary>
public enum EditorTool
{
    Select,
    Text,
    Connector
}

/// <summary>
/// The page surface: draws the grid and the shapes, and handles placing, selecting,
/// moving, resizing, connecting and labelling them. All shape coordinates are page
/// coordinates; the control adds <see cref="PageMargin"/> around the page so it floats
/// on the workspace. The single visual child is an overlay that carries the inline
/// label editor, which has to be a real control to take keyboard input.
/// </summary>
public class DrawingCanvas : Decorator
{
    private enum DragMode
    {
        None,
        Moving,
        Resizing,
        MovingEndpoint,
        MovingBend,
        MovingSegment,
        DrawingConnector,
        Marquee
    }

    private const double PageMargin = 24;
    private const double HandleSize = 8;
    private const double DefaultShapeWidth = 120;
    private const double DefaultShapeHeight = 80;
    private const double DefaultTextWidth = 160;
    private const double DefaultTextHeight = 40;

    /// <summary>How far a connector has to be dragged before it is worth creating.</summary>
    private const double MinConnectorLength = 8;

    public const double MinZoom = 0.25;
    public const double MaxZoom = 4.0;

    /// <summary>How close to a line a click has to land, in screen pixels, at any zoom.</summary>
    private const double LineHitPixels = 6;

    private static readonly IBrush WorkspaceBrush = AppTheme.Workspace;
    private static readonly IBrush PageBrush = Brushes.White;
    private static readonly IBrush ShadowBrush = new SolidColorBrush(Color.FromArgb(0x50, 0, 0, 0));
    private static readonly IBrush HandleBrush = Brushes.White;
    private static readonly IBrush PageBorderBrush = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x80));
    private static readonly IBrush MinorGridBrush = new SolidColorBrush(Color.FromRgb(0xE4, 0xE8, 0xEF));
    private static readonly IBrush MajorGridBrush = new SolidColorBrush(Color.FromRgb(0xC5, 0xCD, 0xDB));
    private static readonly IBrush SelectionBrush = AppTheme.Accent;
    private static readonly IBrush GlueBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0xA0, 0x55));
    private static readonly IBrush GhostBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x6C, 0xDF));
    // A wash of the accent: its own brush, since only the outline shares the accent's colour.
    private static readonly SolidColorBrush MarqueeBrush = new(Wash(AppTheme.Current.Accent));

    private static Color Wash(Color accent) => Color.FromArgb(0x20, accent.R, accent.G, accent.B);
    private static readonly IBrush MidpointHandleBrush = new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xE6, 0xC0));

    /// <summary>Handle order: NW, N, NE, E, SE, S, SW, W.</summary>
    private static readonly StandardCursorType[] HandleCursors =
    [
        StandardCursorType.TopLeftCorner, StandardCursorType.TopSide, StandardCursorType.TopRightCorner,
        StandardCursorType.RightSide, StandardCursorType.BottomRightCorner, StandardCursorType.BottomSide,
        StandardCursorType.BottomLeftCorner, StandardCursorType.LeftSide
    ];

    private DragMode _dragMode = DragMode.None;

    /// <summary>Set once a drag actually alters a shape, so a bare click does not dirty the document.</summary>
    private bool _dragChanged;

    private int _activeHandle = -1;

    /// <summary>The segment a slide started on; the handle list shifts underneath it.</summary>
    private int _activeSegment = -1;
    private Point _dragOrigin;
    private Rect _dragStartBounds;
    private Rect? _ghost;

    /// <summary>The shapes a move applies to, captured when the drag began.</summary>
    private readonly List<DiagramShape> _dragShapes = [];

    private Rect _dragStartUnion;

    /// <summary>How far the current move has been applied so far.</summary>
    private Vector _dragApplied;

    private double _zoom = 1.0;
    private bool _spaceHeld;
    private bool _panning;
    private Point _panOrigin;
    private Rect? _marquee;
    private bool _marqueeAdds;

    private ConnectorShape? _pendingConnector;
    private DiagramShape? _glueTarget;

    /// <summary>The connection point the pointer is snapping to, and the shape it belongs to.</summary>
    private DiagramShape? _portShape;

    private int _portIndex = -1;

    /// <summary>How close, in screen pixels, the pointer has to be to snap to a connection point.</summary>
    private const double PortSnapPixels = 12;

    private const double PortMarkPixels = 4;

    /// <summary>Midpoint grab points are drawn smaller, so they read as "add a bend here".</summary>
    private const double MidpointHandleScale = 0.7;

    private readonly Canvas _overlay = new();
    private TextBox? _editor;
    private DiagramShape? _editing;

    public DiagramDocument Document { get; } = new();
    public GridSettings Grid { get; } = new();

    /// <summary>What a click on the page does. Setting this clears any armed stencil.</summary>
    public EditorTool Tool { get; set; } = EditorTool.Select;

    /// <summary>The stencil waiting to be placed by the next click on the page, if any.</summary>
    public ShapeKind? ArmedKind { get; set; }

    /// <summary>Line ends given to the next connector drawn.</summary>
    public EndCapStyle DefaultStartCap { get; set; } = EndCapStyle.None;

    public EndCapStyle DefaultEndCap { get; set; } = EndCapStyle.Arrow;

    public double DefaultLineWidth { get; set; } = 2;

    /// <summary>New connectors route around shapes; existing documents keep whatever they had.</summary>
    public ConnectorRouting DefaultRouting { get; set; } = ConnectorRouting.Orthogonal;

    /// <summary>The formatting given to whatever is drawn next.</summary>
    public ShapeStyle DefaultStyle { get; private set; } = ShapeStyle.Default;

    /// <summary>Raised when the armed stencil is consumed or cleared by the canvas.</summary>
    public event Action<ShapeKind?>? ArmedKindChanged;

    /// <summary>Raised when the canvas changes the tool itself, so the toolbar can resync.</summary>
    public event Action<EditorTool>? ToolChanged;

    public event Action? SelectionChanged;

    public event Action<string>? StatusChanged;

    /// <summary>The window handles this, because deleting a container may need to ask first.</summary>
    public event Action? DeleteRequested;

    public DrawingCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        Child = _overlay;

        Width = Document.PageWidth + PageMargin * 2;
        Height = Document.PageHeight + PageMargin * 2;

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);

        DoubleTapped += OnDoubleTapped;

        AppTheme.Changed += OnThemeChanged;

        Document.SelectionChanged += () =>
        {
            SelectionChanged?.Invoke();
            ReportStatus();
            InvalidateVisual();
        };
    }

    private void OnThemeChanged()
    {
        MarqueeBrush.Color = Wash(AppTheme.Current.Accent);
        InvalidateVisual();
    }

    /// <summary>Re-reads the page size after it changes, then repaints.</summary>
    public void SyncPageSize()
    {
        Width = (Document.PageWidth + PageMargin * 2) * Zoom;
        Height = (Document.PageHeight + PageMargin * 2) * Zoom;

        PositionEditor();
        InvalidateVisual();
    }

    #region Zoom

    /// <summary>Scale the page is drawn at. 1.0 is 100%.</summary>
    public double Zoom
    {
        get => _zoom;
        private set
        {
            var clamped = Math.Clamp(value, MinZoom, MaxZoom);

            if (Math.Abs(clamped - _zoom) < 0.0001)
                return;

            _zoom = clamped;
            SyncPageSize();
            ZoomChanged?.Invoke(_zoom);
        }
    }

    public event Action<double>? ZoomChanged;

    /// <summary>
    /// Sets the zoom, keeping the page point under <paramref name="anchor"/> - a position in
    /// this control - where it is on screen. Without an anchor the viewport centre is held.
    /// </summary>
    public void SetZoom(double zoom, Point? anchor = null)
    {
        var scroll = this.FindAncestorOfType<ScrollViewer>();
        var focus = anchor ?? (scroll is null ? default : new Point(
            scroll.Offset.X + scroll.Viewport.Width / 2,
            scroll.Offset.Y + scroll.Viewport.Height / 2));

        var pagePoint = ToPage(focus);
        var offsetBefore = scroll?.Offset ?? default;

        Zoom = zoom;

        if (scroll is null)
            return;

        // Keep the anchored page point still: move the viewport by however far it drifted.
        scroll.UpdateLayout();

        var moved = ToControl(pagePoint);
        scroll.Offset = offsetBefore + new Vector(moved.X - focus.X, moved.Y - focus.Y);
    }

    /// <summary>The largest zoom that fits the whole page in the viewport.</summary>
    public void ZoomToFit()
    {
        if (this.FindAncestorOfType<ScrollViewer>() is not { } scroll)
            return;

        if (scroll.Viewport.Width <= 0 || scroll.Viewport.Height <= 0)
            return;

        var fit = Math.Min(
            scroll.Viewport.Width / (Document.PageWidth + PageMargin * 2),
            scroll.Viewport.Height / (Document.PageHeight + PageMargin * 2));

        SetZoom(fit);
    }

    /// <summary>Steps through the zoom levels the toolbar offers.</summary>
    public void ZoomBy(double factor, Point? anchor = null) => SetZoom(Zoom * factor, anchor);

    #endregion

    private Point ToPage(Point control) => new(
        control.X / Zoom - PageMargin,
        control.Y / Zoom - PageMargin);

    private Point ToControl(Point page) => new(
        (page.X + PageMargin) * Zoom,
        (page.Y + PageMargin) * Zoom);

    /// <summary>A length in screen pixels, expressed in page units at the current zoom.</summary>
    private double Screen(double pixels) => pixels / Zoom;

    private Rect PageRect => new(0, 0, Document.PageWidth, Document.PageHeight);

    /// <summary>Shift or Ctrl extends the selection instead of replacing it.</summary>
    private static bool IsExtending(KeyModifiers modifiers) =>
        modifiers.HasFlag(KeyModifiers.Shift) || modifiers.HasFlag(KeyModifiers.Control);

    public void SelectAll()
    {
        Document.SelectAll();
        Focus();
        ReportStatus();
    }

    #region Rendering

    public override void Render(DrawingContext context)
    {
        Document.LayoutContainers();
        Document.NormaliseOrder();
        Document.RouteConnectors();

        context.DrawRectangle(WorkspaceBrush, null, new Rect(Bounds.Size));

        // Everything below is drawn in page coordinates; the transform does the zoom.
        using (context.PushTransform(
                   Matrix.CreateScale(Zoom, Zoom) *
                   Matrix.CreateTranslation(PageMargin * Zoom, PageMargin * Zoom)))
        {
            var page = PageRect;
            context.DrawRectangle(ShadowBrush, null, page.Translate(new Vector(Screen(3), Screen(3))));
            context.DrawRectangle(PageBrush, null, page);

            if (Grid.ShowGrid)
                RenderGrid(context);

            context.DrawRectangle(null, ScreenPen(PageBorderBrush, 1), page);

            foreach (var shape in Document.Shapes)
                shape.Render(context, !ReferenceEquals(shape, _editing));

            _pendingConnector?.Render(context, false);

            if (_glueTarget is { } glued)
                context.DrawRectangle(null, ScreenPen(GlueBrush, 2), glued.Bounds.Inflate(Screen(2)));

            RenderConnectionPoints(context);

            if (_ghost is { } ghost)
                context.DrawRectangle(null, ScreenDashPen(GhostBrush, 1.5, 4), ghost);

            RenderSelection(context);

            if (_marquee is { } marquee)
                context.DrawRectangle(MarqueeBrush, ScreenDashPen(SelectionBrush, 1, 3), marquee);
        }
    }

    /// <summary>
    /// Shows where a connector can attach: every connection point on the shape under the
    /// pointer, with the one being snapped to filled in.
    /// </summary>
    private void RenderConnectionPoints(DrawingContext context)
    {
        if (!IsAttaching)
            return;

        var mark = Screen(PortMarkPixels);
        var pen = ScreenPen(GlueBrush, 1.5);

        foreach (var shape in PortCandidates())
        {
            var points = shape.ConnectionPoints;

            for (var i = 0; i < points.Count; i++)
            {
                var snapped = ReferenceEquals(shape, _portShape) && i == _portIndex;
                var box = new Rect(points[i].X - mark, points[i].Y - mark, mark * 2, mark * 2);

                context.DrawRectangle(snapped ? GlueBrush : PageBrush, pen, box);
            }
        }
    }

    /// <summary>True while a connector end is being placed, when ports are worth showing.</summary>
    private bool IsAttaching =>
        Tool == EditorTool.Connector || _dragMode == DragMode.DrawingConnector ||
        _dragMode == DragMode.MovingEndpoint;

    /// <summary>Only the shape under the pointer offers its ports, to keep the page readable.</summary>
    private IEnumerable<DiagramShape> PortCandidates() =>
        _glueTarget is null ? [] : [_glueTarget];

    /// <summary>
    /// Finds the connection point nearest the pointer on a shape, within the snap radius.
    /// Returns -1 to attach to the shape without pinning to a point.
    /// </summary>
    private int NearestPort(DiagramShape? shape, Point pagePoint)
    {
        if (shape is null)
            return -1;

        var radius = Screen(PortSnapPixels);
        var best = -1;
        var bestDistance = double.MaxValue;

        var points = shape.ConnectionPoints;

        for (var i = 0; i < points.Count; i++)
        {
            var dx = points[i].X - pagePoint.X;
            var dy = points[i].Y - pagePoint.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);

            if (distance > radius || distance >= bestDistance)
                continue;

            best = i;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>
    /// A pen whose stroke stays the same width on screen at any zoom. Chrome - grid lines,
    /// selection outlines, handles - should not thicken when the drawing is scaled up.
    /// </summary>
    private IPen ScreenPen(IBrush brush, double pixels) => new Pen(brush, Screen(pixels));

    private IPen ScreenDashPen(IBrush brush, double pixels, double dash) =>
        new Pen(brush, Screen(pixels), new DashStyle([dash, dash], 0));

    private void RenderGrid(DrawingContext context)
    {
        var size = Grid.Size;
        if (size <= 0)
            return;

        // Below about half scale the minor lines merge into a grey wash, so drop them.
        var showMinor = size * Zoom >= 4;

        var minorPen = ScreenPen(MinorGridBrush, 1);
        var majorPen = ScreenPen(MajorGridBrush, 1);

        var index = 0;
        for (var x = 0.0; x <= Document.PageWidth; x += size, index++)
        {
            var major = index % GridSettings.MajorEvery == 0;

            if (!major && !showMinor)
                continue;

            context.DrawLine(major ? majorPen : minorPen,
                new Point(x, 0),
                new Point(x, Document.PageHeight));
        }

        index = 0;
        for (var y = 0.0; y <= Document.PageHeight; y += size, index++)
        {
            var major = index % GridSettings.MajorEvery == 0;

            if (!major && !showMinor)
                continue;

            context.DrawLine(major ? majorPen : minorPen,
                new Point(0, y),
                new Point(Document.PageWidth, y));
        }
    }

    private void RenderSelection(DrawingContext context)
    {
        var selection = Document.Selection;

        if (selection.Count == 0)
            return;

        var outline = ScreenPen(SelectionBrush, 1.5);

        foreach (var shape in selection)
        {
            if (shape.IsBoxResizable)
                context.DrawRectangle(null, outline, shape.Bounds.Inflate(Screen(2)));
        }

        // Handles only make sense for one shape; a group gets a dashed box instead.
        if (selection.Count == 1)
        {
            if (selection[0] is ConnectorShape connector)
            {
                foreach (var handle in ConnectorHandles(connector))
                {
                    // A solid handle moves what is there; a hollow one adds a new bend.
                    var fill = handle.Kind == HandleKind.Midpoint ? MidpointHandleBrush : HandleBrush;
                    context.DrawRectangle(fill, outline, handle.Rect);
                }

                return;
            }

            foreach (var handle in SelectionHandles(selection[0]))
                context.DrawRectangle(HandleBrush, outline, handle);

            return;
        }

        context.DrawRectangle(null, ScreenDashPen(SelectionBrush, 1, 6),
            ShapeClipboard.Union(selection).Inflate(Screen(5)));
    }

    /// <summary>What a connector handle does when it is dragged.</summary>
    private enum HandleKind
    {
        /// <summary>One of the two ends, which re-glues the connector.</summary>
        Endpoint,

        /// <summary>A bend, which moves freely.</summary>
        Corner,

        /// <summary>The middle of a segment, which slides it - adding bends as it goes.</summary>
        Midpoint
    }

    private readonly record struct ConnectorHandle(Rect Rect, HandleKind Kind, int Index);

    /// <summary>
    /// A connector's handles: both ends, then a grab point on every bend, then one on the
    /// middle of every segment. Ends and bends come first so they win when handles overlap.
    /// </summary>
    private List<ConnectorHandle> ConnectorHandles(ConnectorShape connector)
    {
        var path = connector.Path;

        var handles = new List<ConnectorHandle>
        {
            new(HandleRect(path[0]), HandleKind.Endpoint, 0),
            new(HandleRect(path[^1]), HandleKind.Endpoint, 1)
        };

        for (var i = 1; i < path.Count - 1; i++)
            handles.Add(new ConnectorHandle(HandleRect(path[i]), HandleKind.Corner, i));

        for (var i = 0; i < path.Count - 1; i++)
        {
            var middle = new Point(
                (path[i].X + path[i + 1].X) / 2,
                (path[i].Y + path[i + 1].Y) / 2);

            handles.Add(new ConnectorHandle(HandleRect(middle, MidpointHandleScale), HandleKind.Midpoint, i));
        }

        return handles;
    }

    /// <summary>Eight box handles for a shape, or the connector handles above.</summary>
    private Rect[] SelectionHandles(DiagramShape shape)
    {
        if (shape is ConnectorShape connector)
            return ConnectorHandles(connector).Select(handle => handle.Rect).ToArray();

        var bounds = shape.Bounds;

        Point[] points =
        [
            new(bounds.Left, bounds.Top),
            new(bounds.Center.X, bounds.Top),
            new(bounds.Right, bounds.Top),
            new(bounds.Right, bounds.Center.Y),
            new(bounds.Right, bounds.Bottom),
            new(bounds.Center.X, bounds.Bottom),
            new(bounds.Left, bounds.Bottom),
            new(bounds.Left, bounds.Center.Y)
        ];

        return points.Select(point => HandleRect(point)).ToArray();
    }

    /// <summary>Handles are a fixed size on screen, so they stay grabbable at any zoom.</summary>
    private Rect HandleRect(Point centre, double scale = 1)
    {
        var size = Screen(HandleSize) * scale;
        return new Rect(centre.X - size / 2, centre.Y - size / 2, size, size);
    }

    /// <summary>The handle under the point, or -1. Handles are offered for a single shape only.</summary>
    private int HandleAt(Point pagePoint)
    {
        if (Document.Selection.Count != 1)
            return -1;

        var selected = Document.Selection[0];
        var handles = SelectionHandles(selected);
        for (var i = 0; i < handles.Length; i++)
        {
            if (handles[i].Inflate(Screen(2)).Contains(pagePoint))
                return i;
        }

        return -1;
    }

    #endregion

    #region Placing shapes

    /// <summary>Places a new shape centred on the page point, snapped and clamped to the page.</summary>
    public DiagramShape PlaceShape(ShapeKind kind, Point pageCentre)
    {
        var shape = ShapeFactory.Create(kind, DefaultBoundsAt(kind, pageCentre));

        // A new text box keeps its transparent fill and outline; everything else takes the
        // formatting currently set in the properties panel.
        if (shape is TextBoxShape)
            DefaultStyle.ApplyTextTo(shape);
        else
            DefaultStyle.ApplyTo(shape);

        // A stencil that is defined to be thick or dashed says so last.
        StencilCatalogue.Find(kind)?.ApplyDefaults(shape);

        Document.Add(shape);

        // Dropping a shape onto a container puts it in the container.
        if (shape.Kind != ShapeKind.Lane)
            Document.Adopt(shape, Document.ContainerFor(shape));

        SelectionChanged?.Invoke();

        InvalidateVisual();
        ReportStatus();
        return shape;
    }

    #region Formatting

    /// <summary>
    /// Applies one formatting change to everything selected, and remembers it as the default
    /// for the next shape drawn. With nothing selected it only sets the default.
    /// </summary>
    private void ApplyFormat(Action<DiagramShape> change, Func<ShapeStyle, ShapeStyle> remember)
    {
        DefaultStyle = remember(DefaultStyle);

        if (Document.Selection.Count == 0)
            return;

        using (Document.BeginBatch())
        {
            var changed = false;

            foreach (var shape in Document.Selection)
            {
                var before = ShapeStyle.From(shape);
                change(shape);

                if (ShapeStyle.From(shape) != before)
                    changed = true;
            }

            // Re-applying the formatting a shape already has is not an edit: it should not
            // dirty the document or take up a step in the undo history.
            if (!changed)
                return;

            Document.MarkModified();
        }

        InvalidateVisual();
        ReportStatus();
    }

    public void SetFill(Color value) =>
        ApplyFormat(shape => shape.Fill = value, style => style with { Fill = value });

    public void SetStroke(Color value) =>
        ApplyFormat(shape => shape.Stroke = value, style => style with { Stroke = value });

    public void SetTextColor(Color value) =>
        ApplyFormat(shape => shape.TextColor = value, style => style with { TextColor = value });

    public void SetStrokeThickness(double value) =>
        ApplyFormat(shape => shape.StrokeThickness = value, style => style with { StrokeThickness = value });

    public void SetStrokeStyle(StrokeStyle value) =>
        ApplyFormat(shape => shape.StrokeStyle = value, style => style with { StrokeStyle = value });

    public void SetFontSize(double value) =>
        ApplyFormat(shape => shape.FontSize = value, style => style with { FontSize = value });

    #endregion

    private Rect DefaultBoundsAt(ShapeKind kind, Point pageCentre)
    {
        var (defaultWidth, defaultHeight) = kind switch
        {
            ShapeKind.TextBox => (DefaultTextWidth, DefaultTextHeight),
            ShapeKind.Pool => (520.0, 260.0),
            ShapeKind.Lane => (480.0, 110.0),
            ShapeKind.ContainerBox => (320.0, 220.0),
            _ => (DefaultShapeWidth, DefaultShapeHeight)
        };

        var width = Grid.Snap(defaultWidth);
        var height = Grid.Snap(defaultHeight);

        return Document.ClampToPage(Grid.Snap(new Rect(
            pageCentre.X - width / 2,
            pageCentre.Y - height / 2,
            width,
            height)));
    }

    private static ShapeKind? KindFromData(IDataObject data)
    {
        var raw = data.Get(ToolboxPanel.DragFormat) as string ?? data.GetText();
        return Enum.TryParse<ShapeKind>(raw, out var kind) ? kind : null;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (KindFromData(e.Data) is not { } kind)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        _ghost = DefaultBoundsAt(kind, ToPage(e.GetPosition(this)));
        InvalidateVisual();
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        _ghost = null;
        InvalidateVisual();
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        _ghost = null;

        if (KindFromData(e.Data) is not { } kind)
            return;

        PlaceShape(kind, ToPage(e.GetPosition(this)));
        ClearArmed();
        Focus();
        e.Handled = true;
    }

    private void ClearArmed()
    {
        if (ArmedKind is null)
            return;

        ArmedKind = null;
        ArmedKindChanged?.Invoke(null);
    }

    private void SetTool(EditorTool tool)
    {
        if (Tool == tool)
            return;

        Tool = tool;
        ToolChanged?.Invoke(tool);
    }

    #endregion

    #region Pointer and keyboard

    /// <summary>True while the click landed inside the inline label editor.</summary>
    private bool IsEditorEvent(object? source) =>
        _editor is not null && source is Visual visual &&
        (ReferenceEquals(source, _editor) || _editor.IsVisualAncestorOf(visual));

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        // Plain wheel scrolls, as the scroll viewer would; Ctrl zooms about the pointer.
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return;

        ZoomBy(e.Delta.Y > 0 ? 1.2 : 1 / 1.2, e.GetPosition(this));
        e.Handled = true;
    }

    /// <summary>Middle button, or space with the left button, drags the page around.</summary>
    private bool TryStartPan(PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;

        if (!properties.IsMiddleButtonPressed && !(_spaceHeld && properties.IsLeftButtonPressed))
            return false;

        if (this.FindAncestorOfType<ScrollViewer>() is null)
            return false;

        _panning = true;
        _panOrigin = e.GetPosition(this);
        Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Pointer.Capture(this);
        e.Handled = true;
        return true;
    }

    private void Pan(PointerEventArgs e)
    {
        if (this.FindAncestorOfType<ScrollViewer>() is not { } scroll)
            return;

        // The drag is measured in this control, which moves with the offset, so the origin
        // stays put and each move reports the whole remaining delta.
        var delta = e.GetPosition(this) - _panOrigin;
        scroll.Offset -= new Vector(delta.X, delta.Y);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (IsEditorEvent(e.Source))
            return;

        if (TryStartPan(e))
            return;

        CommitEdit();
        Focus();

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        var pagePoint = ToPage(point.Position);

        var handle = HandleAt(pagePoint);
        if (handle >= 0 && Document.Selected is { } active)
        {
            _activeHandle = handle;
            _activeSegment = -1;

            if (active is ConnectorShape connector)
            {
                var info = ConnectorHandles(connector)[handle];

                _dragMode = info.Kind switch
                {
                    HandleKind.Endpoint => DragMode.MovingEndpoint,
                    HandleKind.Corner => DragMode.MovingBend,
                    _ => DragMode.MovingSegment
                };

                // A slide rewrites the path as it goes, so remember which segment it started on.
                if (info.Kind == HandleKind.Midpoint)
                    _activeSegment = info.Index;
            }
            else
            {
                _dragMode = DragMode.Resizing;
            }

            _dragStartBounds = active.Bounds;
            _dragOrigin = pagePoint;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        switch (Tool)
        {
            case EditorTool.Connector:
                StartConnector(pagePoint, e);
                return;

            case EditorTool.Text:
                PlaceTextBox(pagePoint, e);
                return;
        }

        if (ArmedKind is { } armed)
        {
            PlaceShape(armed, pagePoint);
            ClearArmed();
            _ghost = null;
            e.Handled = true;
            return;
        }

        var hit = Document.HitTest(pagePoint, Screen(LineHitPixels));
        var extending = IsExtending(e.KeyModifiers);

        if (hit is null)
        {
            // An empty-page drag sweeps out a selection marquee.
            if (!extending)
                Document.ClearSelection();

            _dragMode = DragMode.Marquee;
            _marqueeAdds = extending;
            _marquee = new Rect(pagePoint, pagePoint);
            _dragOrigin = pagePoint;
            e.Pointer.Capture(this);

            InvalidateVisual();
            ReportStatus();
            e.Handled = true;
            return;
        }

        if (extending)
        {
            Document.ToggleSelection(hit);
        }
        else if (!Document.IsSelected(hit))
        {
            // Clicking inside an existing multi-selection keeps it, so the group can be dragged.
            Document.SelectOnly(hit);

            // A container is a backdrop: raising one because it was clicked would bury the
            // very contents the click was aimed past.
            if (!hit.IsContainer)
                Document.BringToFront(hit);
        }

        if (Document.IsSelected(hit))
            BeginMove(pagePoint, e);

        InvalidateVisual();
        ReportStatus();
        e.Handled = true;
    }

    /// <summary>Remembers where everything sat, so a drag moves the whole selection together.</summary>
    private void BeginMove(Point pagePoint, PointerPressedEventArgs e)
    {
        _dragShapes.Clear();

        // Dragging a container takes what is inside it along, without moving anything twice.
        foreach (var shape in Document.Selection)
        {
            if (!_dragShapes.Contains(shape))
                _dragShapes.Add(shape);

            if (!shape.IsContainer)
                continue;

            foreach (var child in Document.DescendantsOf(shape))
            {
                if (!_dragShapes.Contains(child))
                    _dragShapes.Add(child);
            }
        }

        _dragStartUnion = ShapeClipboard.Union(Document.Selection);
        _dragApplied = default;
        _dragMode = DragMode.Moving;
        _dragOrigin = pagePoint;

        e.Pointer.Capture(this);
    }

    private void StartConnector(Point pagePoint, PointerPressedEventArgs e)
    {
        var target = Document.HitTest(pagePoint, Screen(LineHitPixels)) as DiagramShape;
        var port = NearestPort(target, pagePoint);
        var anchor = target?.Bounds.Center ?? Grid.Snap(pagePoint);

        _pendingConnector = new ConnectorShape(anchor, Grid.Snap(pagePoint))
        {
            StartShape = target,
            StartPort = port,
            StartCap = DefaultStartCap,
            EndCap = DefaultEndCap,
            StrokeThickness = DefaultLineWidth,
            Routing = DefaultRouting,
            Stroke = DefaultStyle.Stroke,
            StrokeStyle = DefaultStyle.StrokeStyle,
            TextColor = DefaultStyle.TextColor,
            FontSize = DefaultStyle.FontSize
        };

        _dragMode = DragMode.DrawingConnector;
        _dragOrigin = pagePoint;
        e.Pointer.Capture(this);
        e.Handled = true;

        InvalidateVisual();
    }

    private void PlaceTextBox(Point pagePoint, PointerPressedEventArgs e)
    {
        // Clicking existing text with the text tool edits it rather than stacking a new box on top.
        var existing = Document.HitTest(pagePoint, Screen(LineHitPixels));

        var shape = existing ?? PlaceShape(ShapeKind.TextBox, pagePoint);
        Document.SelectOnly(shape);

        SetTool(EditorTool.Select);
        BeginEdit(shape);

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_panning)
        {
            Pan(e);
            return;
        }

        var pagePoint = ToPage(e.GetPosition(this));

        switch (_dragMode)
        {
            case DragMode.Moving when _dragShapes.Count > 0:
                MoveSelection(pagePoint);
                return;

            case DragMode.Marquee:
                _marquee = new Rect(_dragOrigin, pagePoint);
                InvalidateVisual();
                return;

            case DragMode.Resizing when Document.Selection.Count == 1:
                var sizing = Document.Selection[0];
                sizing.Bounds = Document.ClampToPage(Resize(_dragStartBounds, _activeHandle, pagePoint));
                _dragChanged = true;
                InvalidateVisual();
                ReportStatus();
                return;

            case DragMode.MovingEndpoint when Document.Selected is ConnectorShape connector
                                              && Document.Selection.Count == 1:
                DragEndpoint(connector, pagePoint);
                return;

            case DragMode.MovingBend when Document.Selected is ConnectorShape bending
                                          && Document.Selection.Count == 1:
                DragBend(bending, pagePoint);
                return;

            case DragMode.MovingSegment when Document.Selected is ConnectorShape sliding
                                             && Document.Selection.Count == 1:
                DragSegment(sliding, pagePoint);
                return;

            case DragMode.DrawingConnector when _pendingConnector is { } pending:
                _glueTarget = Document.Shapes
                    .Where(shape => shape is not ConnectorShape)
                    .LastOrDefault(shape => shape.HitTest(pagePoint));

                _portShape = _glueTarget;
                _portIndex = NearestPort(_glueTarget, pagePoint);

                pending.End = _glueTarget?.Bounds.Center ?? Grid.Snap(pagePoint);
                pending.EndShape = _glueTarget;
                pending.EndPort = _portIndex;

                InvalidateVisual();
                return;
        }

        if (ArmedKind is { } armed)
        {
            _ghost = DefaultBoundsAt(armed, pagePoint);
            InvalidateVisual();
        }

        if (Tool == EditorTool.Connector)
        {
            var over = Document.Shapes
                .Where(shape => shape is not ConnectorShape)
                .LastOrDefault(shape => shape.HitTest(pagePoint));

            var port = NearestPort(over, pagePoint);

            if (!ReferenceEquals(over, _glueTarget) || port != _portIndex)
            {
                _glueTarget = over;
                _portShape = over;
                _portIndex = port;
                InvalidateVisual();
            }
        }

        UpdateCursor(pagePoint);
    }

    /// <summary>
    /// Moves everything selected by one delta, snapping the group's own top left corner and
    /// holding the shapes' positions relative to each other.
    /// </summary>
    private void MoveSelection(Point pagePoint)
    {
        var delta = pagePoint - _dragOrigin;

        var target = Document.ClampToPage(new Rect(
            Grid.Snap(_dragStartUnion.X + delta.X),
            Grid.Snap(_dragStartUnion.Y + delta.Y),
            _dragStartUnion.Width,
            _dragStartUnion.Height));

        var wanted = new Vector(target.X - _dragStartUnion.X, target.Y - _dragStartUnion.Y);
        var step = wanted - _dragApplied;

        if (step.X == 0 && step.Y == 0)
            return;

        // Shapes are moved by a step rather than to an absolute position. A connector's
        // bounds are derived from the shapes it is glued to, so they shift on their own as
        // those shapes move and cannot be used as a fixed point to measure from.
        foreach (var shape in _dragShapes)
            shape.Translate(step);

        _dragApplied = wanted;
        _dragChanged = true;
        InvalidateVisual();
        ReportStatus();
    }

    private void DragEndpoint(ConnectorShape connector, Point pagePoint)
    {
        // An end point dropped on a shape glues to it; dropped on the page it un-glues.
        _glueTarget = Document.Shapes
            .Where(shape => shape is not ConnectorShape)
            .LastOrDefault(shape => shape.HitTest(pagePoint));

        _portShape = _glueTarget;
        _portIndex = NearestPort(_glueTarget, pagePoint);

        var anchor = _glueTarget?.Bounds.Center ?? Grid.Snap(pagePoint);

        if (ActiveConnectorHandle(connector) is { Kind: HandleKind.Endpoint, Index: 0 })
        {
            connector.Start = anchor;
            connector.StartShape = _glueTarget;
            connector.StartPort = _portIndex;
        }
        else
        {
            connector.End = anchor;
            connector.EndShape = _glueTarget;
            connector.EndPort = _portIndex;
        }

        _dragChanged = true;
        InvalidateVisual();
        ReportStatus();
    }

    /// <summary>The handle a drag is working on, or null when it is not a connector handle.</summary>
    private ConnectorHandle? ActiveConnectorHandle(ConnectorShape connector)
    {
        var handles = ConnectorHandles(connector);
        return _activeHandle >= 0 && _activeHandle < handles.Count ? handles[_activeHandle] : null;
    }

    /// <summary>
    /// Moves a bend. The first such edit freezes the route as hand-placed, so a connector that
    /// has been tidied up by hand is not undone the next time something moves.
    /// </summary>
    private void DragBend(ConnectorShape connector, Point pagePoint)
    {
        if (ActiveConnectorHandle(connector) is not { Kind: HandleKind.Corner } handle)
            return;

        connector.MoveBend(handle.Index, Grid.Snap(pagePoint),
            connector.Routing == ConnectorRouting.Orthogonal);

        _dragChanged = true;
        InvalidateVisual();
        ReportStatus();
    }

    /// <summary>
    /// Slides a segment sideways. Where the segment meets a shape a new bend appears at that
    /// end, so one drag turns a single run into three - each with a grab point of its own.
    /// </summary>
    private void DragSegment(ConnectorShape connector, Point pagePoint)
    {
        // The handle list is rebuilt from the path, which this drag is changing, so the
        // segment being dragged is captured once at the start instead of looked up again.
        if (_activeSegment < 0)
            return;

        connector.SlideSegment(_activeSegment, Grid.Snap(pagePoint));

        _dragChanged = true;
        InvalidateVisual();
        ReportStatus();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_panning)
        {
            _panning = false;
            Cursor = new Cursor(_spaceHeld ? StandardCursorType.Hand : StandardCursorType.Arrow);
            e.Pointer.Capture(null);
            return;
        }

        if (_dragMode == DragMode.DrawingConnector && _pendingConnector is { } pending)
            FinishConnector(pending);

        if (_dragMode == DragMode.Marquee)
            FinishMarquee();

        if (_dragMode == DragMode.None)
            return;

        if (_dragChanged)
        {
            ReviewContainment();
            Document.MarkModified();
        }

        _dragChanged = false;
        _dragMode = DragMode.None;
        _activeHandle = -1;
        _activeSegment = -1;
        _glueTarget = null;
        _portShape = null;
        _portIndex = -1;
        _dragShapes.Clear();
        e.Pointer.Capture(null);

        InvalidateVisual();
        ReportStatus();
    }

    private void FinishConnector(ConnectorShape pending)
    {
        _pendingConnector = null;

        var span = pending.ResolvedEnd - pending.ResolvedStart;
        var isLongEnough = Math.Sqrt(span.X * span.X + span.Y * span.Y) >= MinConnectorLength;

        // A connector glued at both ends is worth keeping even when the shapes overlap.
        if (!isLongEnough && (pending.StartShape is null || pending.EndShape is null ||
                              ReferenceEquals(pending.StartShape, pending.EndShape)))
            return;

        Document.Add(pending);
        SelectionChanged?.Invoke();
    }

    /// <summary>
    /// Takes everything the marquee encloses. Enclosing rather than touching is what Visio
    /// does, and it makes it possible to sweep past a large background shape.
    /// </summary>
    private void FinishMarquee()
    {
        if (_marquee is not { } marquee)
            return;

        _marquee = null;

        var caught = Document.Shapes.Where(shape => marquee.Contains(shape.Bounds)).ToList();

        if (caught.Count == 0)
        {
            if (!_marqueeAdds)
                Document.ClearSelection();

            return;
        }

        if (_marqueeAdds)
            Document.SetSelection(Document.Selection.Concat(caught).ToList());
        else
            Document.SetSelection(caught);
    }

    /// <summary>
    /// Re-homes whatever was just moved. A shape dropped inside a container joins it, and one
    /// dragged out is let go; a lane's home is decided by the pool, not by where it was left.
    /// </summary>
    private void ReviewContainment()
    {
        foreach (var shape in _dragShapes.ToList())
        {
            if (shape is ConnectorShape || shape.Kind == ShapeKind.Lane)
                continue;

            Document.Adopt(shape, Document.ContainerFor(shape));
        }
    }

    private void UpdateCursor(Point pagePoint)
    {
        if (_spaceHeld || _panning)
        {
            Cursor = new Cursor(StandardCursorType.Hand);
            return;
        }

        var handle = HandleAt(pagePoint);

        StandardCursorType cursor;

        if (handle >= 0)
        {
            if (Document.Selected is ConnectorShape connector)
            {
                var info = ConnectorHandles(connector)[handle];

                cursor = info.Kind switch
                {
                    HandleKind.Endpoint => StandardCursorType.Cross,
                    HandleKind.Corner => StandardCursorType.SizeAll,
                    _ => SegmentCursor(connector, info.Index)
                };
            }
            else
            {
                cursor = HandleCursors[handle];
            }
        }
        else if (Tool != EditorTool.Select || ArmedKind is not null)
        {
            cursor = StandardCursorType.Cross;
        }
        else
        {
            cursor = Document.HitTest(pagePoint, Screen(LineHitPixels)) is not null
                ? StandardCursorType.SizeAll
                : StandardCursorType.Arrow;
        }

        Cursor = new Cursor(cursor);
    }

    /// <summary>A bend slides at right angles to the segment it sits on.</summary>
    /// <summary>A segment slides at right angles to itself.</summary>
    private static StandardCursorType SegmentCursor(ConnectorShape connector, int segment)
    {
        var path = connector.Path;

        if (segment < 0 || segment + 1 >= path.Count)
            return StandardCursorType.Arrow;

        return Math.Abs(path[segment].X - path[segment + 1].X) < 0.01
            ? StandardCursorType.SizeWestEast
            : StandardCursorType.SizeNorthSouth;
    }

    /// <summary>Applies a handle drag to the bounds, keeping the shape at least <see cref="DiagramShape.MinSize"/> across.</summary>
    private Rect Resize(Rect start, int handle, Point pagePoint)
    {
        var snapped = Grid.Snap(pagePoint);

        var left = start.Left;
        var top = start.Top;
        var right = start.Right;
        var bottom = start.Bottom;

        switch (handle)
        {
            case 0: left = snapped.X; top = snapped.Y; break;
            case 1: top = snapped.Y; break;
            case 2: right = snapped.X; top = snapped.Y; break;
            case 3: right = snapped.X; break;
            case 4: right = snapped.X; bottom = snapped.Y; break;
            case 5: bottom = snapped.Y; break;
            case 6: left = snapped.X; bottom = snapped.Y; break;
            case 7: left = snapped.X; break;
        }

        if (right - left < DiagramShape.MinSize)
        {
            if (handle is 0 or 6 or 7)
                left = right - DiagramShape.MinSize;
            else
                right = left + DiagramShape.MinSize;
        }

        if (bottom - top < DiagramShape.MinSize)
        {
            if (handle is 0 or 1 or 2)
                top = bottom - DiagramShape.MinSize;
            else
                bottom = top + DiagramShape.MinSize;
        }

        return new Rect(new Point(left, top), new Point(right, bottom));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (_editing is not null)
            return;

        if (e.Key == Key.Space && !_spaceHeld)
        {
            _spaceHeld = true;
            Cursor = new Cursor(StandardCursorType.Hand);
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Delete:
            case Key.Back:
                DeleteRequested?.Invoke();
                e.Handled = true;
                return;

            case Key.F2:
                if (Document.Selected is { } selected)
                    BeginEdit(selected);
                e.Handled = true;
                return;

            case Key.Escape:
                ClearArmed();
                SetTool(EditorTool.Select);
                Document.ClearSelection();
                _ghost = null;
                InvalidateVisual();
                ReportStatus();
                e.Handled = true;
                return;

            case Key.Left:
            case Key.Right:
            case Key.Up:
            case Key.Down:
                Nudge(e.Key);
                e.Handled = true;
                return;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (e.Key != Key.Space)
            return;

        _spaceHeld = false;

        if (!_panning)
            Cursor = new Cursor(StandardCursorType.Arrow);
    }

    private void Nudge(Key key)
    {
        if (Document.Selection.Count == 0)
            return;

        var step = Grid.SnapToGrid ? Grid.Size : 1;
        var dx = key == Key.Left ? -step : key == Key.Right ? step : 0;
        var dy = key == Key.Up ? -step : key == Key.Down ? step : 0;

        // Nudge the group as a unit so it stops at the page edge together.
        var union = ShapeClipboard.Union(Document.Selection);
        var target = Document.ClampToPage(new Rect(union.X + dx, union.Y + dy, union.Width, union.Height));
        var applied = new Vector(target.X - union.X, target.Y - union.Y);

        if (applied.X == 0 && applied.Y == 0)
            return;

        foreach (var shape in Document.Selection)
            shape.Translate(applied);

        Document.MarkModified();
        InvalidateVisual();
        ReportStatus();
    }

    #endregion

    #region Inline label editing

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (IsEditorEvent(e.Source))
            return;

        if (Document.HitTest(ToPage(e.GetPosition(this))) is { } shape)
        {
            Document.SelectOnly(shape);
            BeginEdit(shape);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Takes out the bend under the point, if there is one. Used by the double-click gesture,
    /// and separated from it so the behaviour can be exercised without the gesture.
    /// </summary>
    public bool TryRemoveBend(Point pagePoint)
    {
        if (Document.Selected is not ConnectorShape connector || Document.Selection.Count != 1)
            return false;

        var handle = HandleAt(pagePoint);

        if (handle < 0 || ConnectorHandles(connector)[handle] is not { Kind: HandleKind.Corner } corner)
            return false;

        connector.RemoveBend(corner.Index);
        Document.MarkModified();

        InvalidateVisual();
        ReportStatus();
        return true;
    }

    /// <summary>Opens a real TextBox over the shape so its label can be typed in place.</summary>
    public void BeginEdit(DiagramShape shape)
    {
        _editor ??= CreateEditor();
        _editing = shape;

        _editor.Text = shape.Text;
        _editor.FontSize = shape.FontSize;
        _editor.IsVisible = true;

        PositionEditor();
        InvalidateVisual();

        Dispatcher.UIThread.Post(() =>
        {
            _editor.Focus();
            _editor.SelectAll();
        }, DispatcherPriority.Input);
    }

    public void CommitEdit()
    {
        if (_editing is null || _editor is null)
            return;

        var text = _editor.Text ?? string.Empty;

        if (!string.Equals(_editing.Text, text, StringComparison.Ordinal))
        {
            _editing.Text = text;
            Document.MarkModified();
        }

        _editing = null;
        _editor.IsVisible = false;

        InvalidateVisual();
        ReportStatus();
    }

    private TextBox CreateEditor()
    {
        var editor = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Padding = new Thickness(2),
            BorderThickness = new Thickness(1),
            IsVisible = false
        };

        editor.LostFocus += (_, _) => CommitEdit();

        editor.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape)
                return;

            CommitEdit();
            Focus();
            args.Handled = true;
        };

        _overlay.Children.Add(editor);
        return editor;
    }

    /// <summary>
    /// Puts the label editor over the shape being edited. The editor is a real control in an
    /// untransformed overlay, so it is placed in control coordinates and scaled by hand -
    /// that way its text zooms with the drawing underneath it.
    /// </summary>
    private void PositionEditor()
    {
        if (_editor is null || _editing is null)
            return;

        var bounds = _editing.Bounds;
        var origin = ToControl(bounds.TopLeft);

        Canvas.SetLeft(_editor, origin.X);
        Canvas.SetTop(_editor, origin.Y);

        _editor.Width = Math.Max(bounds.Width, DiagramShape.MinSize * 4);
        _editor.Height = Math.Max(bounds.Height, DiagramShape.MinSize);

        _editor.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        _editor.RenderTransform = new ScaleTransform(Zoom, Zoom);
    }

    #endregion

    /// <summary>How many shapes sit inside the containers in the selection.</summary>
    public int SelectedContainerContents() => Document.Selection
        .Where(shape => shape.IsContainer)
        .SelectMany(Document.DescendantsOf)
        .Distinct()
        .Count(shape => !Document.Selection.Contains(shape));

    /// <summary>
    /// Deletes the selection. Where a container is being deleted, its contents either go with
    /// it or are let go onto the page.
    /// </summary>
    public void DeleteSelected(bool deleteContents = true)
    {
        if (Document.Selection.Count == 0)
            return;

        var doomed = Document.Selection.ToList();

        foreach (var container in doomed.Where(shape => shape.IsContainer).ToList())
        {
            foreach (var child in Document.DescendantsOf(container).ToList())
            {
                if (deleteContents)
                {
                    if (!doomed.Contains(child))
                        doomed.Add(child);
                }
                else
                {
                    // Kept: let go onto the page rather than left pointing at nothing.
                    child.Container = null;
                }
            }
        }

        // The shapes and the connectors glued to them are one edit, not several.
        using (Document.BeginBatch())
        {
            foreach (var connector in Document.Shapes.OfType<ConnectorShape>().ToList())
            {
                if (doomed.Any(shape => ReferenceEquals(connector.StartShape, shape) ||
                                        ReferenceEquals(connector.EndShape, shape)))
                    Document.Remove(connector);
            }

            foreach (var shape in doomed)
                Document.Remove(shape);
        }

        InvalidateVisual();
        ReportStatus();
    }

    /// <summary>
    /// Abandons any in-flight edit or drag without writing it back. Undo replaces every
    /// shape with a fresh instance, so references held across it would be stale.
    /// </summary>
    public void CancelInteraction()
    {
        if (_editor is not null)
            _editor.IsVisible = false;

        _editing = null;
        _pendingConnector = null;
        _glueTarget = null;
        _portShape = null;
        _portIndex = -1;
        _ghost = null;
        _marquee = null;
        _dragMode = DragMode.None;
        _dragChanged = false;
        _activeHandle = -1;
        _dragShapes.Clear();

        InvalidateVisual();
    }

    #region Clipboard

    /// <summary>How far each paste is offset from the last, so copies do not hide each other.</summary>
    private const double PasteStep = 20;

    private int _pasteDepth;

    /// <summary>
    /// The selection as clipboard JSON, or null when nothing is selected. A fresh copy
    /// starts the paste cascade over, so the first paste lands one step from the original.
    /// </summary>
    public string? CopySelection()
    {
        var json = ShapeClipboard.Copy(Document, Document.Selection);

        if (json is not null)
            _pasteDepth = 0;

        return json;
    }

    /// <summary>Pastes clipboard JSON, cascading repeated pastes of the same content.</summary>
    public bool Paste(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;

        CommitEdit();

        var offset = Grid.Snap(PasteStep * ++_pasteDepth);
        var pasted = ShapeClipboard.Paste(Document, json, new Vector(offset, offset));

        if (pasted.Count == 0)
        {
            _pasteDepth--;
            return false;
        }

        Focus();
        InvalidateVisual();
        ReportStatus();
        return true;
    }

    /// <summary>Copies the selection and pastes it straight back, without touching the clipboard.</summary>
    public bool DuplicateSelection() => Paste(CopySelection());

    #endregion

    public void ClearPage()
    {
        CommitEdit();
        Document.Clear();
        SelectionChanged?.Invoke();

        InvalidateVisual();
        ReportStatus();
    }

    /// <summary>Applies the toolbar's line-end settings to the selected connector, if there is one.</summary>
    /// <summary>Applies the toolbar's line-end settings to every selected connector.</summary>
    public void ApplyConnectorStyle()
    {
        var connectors = Document.Selection.OfType<ConnectorShape>().ToList();

        if (connectors.Count == 0)
            return;

        // The line weight is ordinary shape formatting, so it goes through SetStrokeThickness
        // rather than being written here as well - one field, one write path.
        using (Document.BeginBatch())
        {
            var changed = false;

            foreach (var connector in connectors)
            {
                if (connector.StartCap != DefaultStartCap || connector.EndCap != DefaultEndCap ||
                    connector.Routing != DefaultRouting)
                    changed = true;

                connector.StartCap = DefaultStartCap;
                connector.EndCap = DefaultEndCap;
                connector.Routing = DefaultRouting;
            }

            if (!changed)
                return;

            Document.MarkModified();
        }

        InvalidateVisual();
    }

    #region Arranging

    /// <summary>
    /// The shapes that lining up and spacing apply to. A connector's position comes from the
    /// shapes it is glued to, so moving one directly would be undone on the next repaint.
    /// </summary>
    private List<DiagramShape> Arrangeable() =>
        Document.Selection.Where(shape => shape is not ConnectorShape).ToList();

    public bool AlignSelection(AlignEdge edge) => Edit(() => ShapeArranger.Align(Arrangeable(), edge));

    public bool DistributeSelection(bool horizontal) =>
        Edit(() => ShapeArranger.Distribute(Arrangeable(), horizontal));

    /// <summary>Sizes the selection to match the shape selected last.</summary>
    public bool MatchSelectionSize(SizeMatch match)
    {
        var reference = Document.Selected;

        return reference is not null &&
               Edit(() => ShapeArranger.MatchSize(Arrangeable(), reference, match));
    }

    public bool ChangeOrder(ZOrder order) =>
        Edit(() => ShapeArranger.Reorder(Document.Shapes, Document.Selection.ToList(), order));

    /// <summary>Runs an operation as one undo step, and only if it changed something.</summary>
    private bool Edit(Func<bool> operation)
    {
        bool changed;

        using (Document.BeginBatch())
        {
            changed = operation();

            if (changed)
                Document.MarkModified();
        }

        if (changed)
        {
            InvalidateVisual();
            ReportStatus();
        }

        return changed;
    }

    #endregion

    /// <summary>Hands the selected connectors back to the router.</summary>
    public void ResetRoutes()
    {
        var connectors = Document.Selection.OfType<ConnectorShape>().ToList();

        if (connectors.Count == 0)
            return;

        using (Document.BeginBatch())
        {
            foreach (var connector in connectors)
                connector.ResetRoute();

            Document.MarkModified();
        }

        InvalidateVisual();
        ReportStatus();
    }

    public void ReportStatus()
    {
        if (Document.Selection.Count > 1)
        {
            var union = ShapeClipboard.Union(Document.Selection);
            StatusChanged?.Invoke(
                $"{Document.Selection.Count} shapes selected  " +
                $"W {union.Width:0}  H {union.Height:0}");
            return;
        }

        var text = Document.Selected switch
        {
            ConnectorShape connector =>
                $"Connector  {connector.StartCap} to {connector.EndCap}" +
                $"{(connector.HasManualRoute ? "  (bends placed by hand)" : string.Empty)}" +
                $"{(connector.StartShape is not null || connector.EndShape is not null ? "  (glued)" : string.Empty)}",
            { } shape =>
                $"{ShapeFactory.DisplayName(shape.Kind)}  " +
                $"X {shape.Bounds.X:0}  Y {shape.Bounds.Y:0}  " +
                $"W {shape.Bounds.Width:0}  H {shape.Bounds.Height:0}",
            _ => Tool switch
            {
                EditorTool.Connector => "Drag between two shapes to connect them",
                EditorTool.Text => "Click the page to add a text box",
                _ => ArmedKind is { } armed
                    ? $"Click the page to place a {ShapeFactory.DisplayName(armed)}"
                    : $"{Document.Shapes.Count} shape(s) on the page"
            }
        };

        StatusChanged?.Invoke(text);
    }
}
