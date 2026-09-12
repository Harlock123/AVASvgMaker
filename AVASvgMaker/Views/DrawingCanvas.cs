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
        ReorderingLane,
        ResizingLane,
        ResizingSelection,
        Rotating,
        MovingLabel,
        SizingLabel,
        MovingTail,
        DrawingConnector,
        Marquee
    }

    /// <summary>
    /// How near the line between its neighbours a bend has to be dropped to be taken out,
    /// in screen pixels.
    /// </summary>
    private const double BendRemovalPixels = 6;

    /// <summary>The workspace showing round the page, in page units.</summary>
    public const double PageMargin = 24;
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

    /// <summary>The margin guide, and the lines that show what a drag has lined up with.</summary>
    private static readonly IBrush MarginBrush = new SolidColorBrush(Color.FromArgb(0x70, 0x90, 0x90, 0xA0));

    private static readonly IBrush GuideBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x4A, 0x8A));

    /// <summary>The handle that turns a shape, round rather than square so it reads differently.</summary>
    private static readonly IBrush RotateHandleBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0x66));

    /// <summary>
    /// The handle that moves a shape's label, and the box the label sits in. A fixed violet,
    /// like the turn handle's fixed yellow: the selection is drawn in the desktop's accent
    /// colour, and a handle that means something else has to stay legible whatever that is.
    /// </summary>
    private static readonly IBrush LabelHandleBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0x8A, 0xE8));

    /// <summary>A bend that letting go of would remove.</summary>
    /// <summary>A callout's tail handle, in a colour of its own so it is not taken for a corner.</summary>
    private static readonly IBrush TailHandleBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0xA8, 0xB0));

    private static readonly IBrush DoomedHandleBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x5A, 0x4A));

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

    /// <summary>Set while a bend is being dragged and has come back onto the line.</summary>
    private bool _bendDoomed;

    /// <summary>How far above the shape the turn handle sits, in screen pixels.</summary>
    private const double RotateHandleReach = 22;

    /// <summary>What the turn snaps to while shift is held.</summary>
    private const double RotateStep = 15;

    /// <summary>How far to the side of the label its grip sits, in screen pixels.</summary>
    private const double LabelGripReach = 20;

    /// <summary>How near an edge has to come before a drag lines up with it, in screen pixels.</summary>
    private const double GuideReachPixels = 6;

    /// <summary>Whether a drag lines itself up with the other shapes on the page.</summary>
    public bool SmartGuides { get; set; } = true;

    /// <summary>The lines to draw showing what the current drag has lined up with.</summary>
    private readonly List<(Point A, Point B)> _guides = [];

    /// <summary>How far the pointer was from the shape's own angle when a turn began.</summary>
    private double _rotateGrip;

    /// <summary>Where the label offset stood when a drag of it began.</summary>
    private Vector _labelGrip;

    /// <summary>The label's frame when a drag of it began, as fractions of its shape.</summary>
    private Rect _labelStart;

    /// <summary>
    /// How far the pointer was from the block's corner when a stretch of it began. The corner
    /// handles are drawn clear of the block while it is still the shape, so without this the
    /// block would jump that far outwards before it began to follow the pointer.
    /// </summary>
    private Vector _labelGrab;

    /// <summary>Where each shape of a selection stood when a stretch of the whole lot began.</summary>
    private readonly List<(DiagramShape Shape, Rect Start, Point[] Points)> _scaling = [];

    private int _activeHandle = -1;

    /// <summary>The segment a slide started on; the handle list shifts underneath it.</summary>
    private int _activeSegment = -1;

    /// <summary>The lane being dragged through its pool's running order.</summary>
    private ContainerShape? _dragLane;
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

    /// <summary>
    /// The id of one of your own saved shapes, waiting to be put down. Held apart from
    /// <see cref="ArmedKind"/> because a saved shape is several shapes and has no one kind.
    /// </summary>
    public string? ArmedStencil { get; set; }

    /// <summary>Line ends given to the next connector drawn.</summary>
    public EndCapStyle DefaultStartCap { get; set; } = EndCapStyle.None;

    public EndCapStyle DefaultEndCap { get; set; } = EndCapStyle.Arrow;

    public double DefaultLineWidth { get; set; } = 2;

    /// <summary>New connectors route around shapes; existing documents keep whatever they had.</summary>
    public ConnectorRouting DefaultRouting { get; set; } = ConnectorRouting.Orthogonal;

    /// <summary>The formatting given to whatever is drawn next.</summary>
    public ShapeStyle DefaultStyle { get; private set; } = Preferences.Style;

    /// <summary>
    /// Starts the session over from a set of defaults. Used when they are changed in the
    /// settings dialog: changing the default fill and then finding the next shape ignores it
    /// would be a puzzle, so the live value follows the saved one when the saved one is set
    /// deliberately. It does not go the other way - see <see cref="Preferences.Style"/>.
    /// </summary>
    public void UseDefaultStyle(ShapeStyle style) => DefaultStyle = style;

    /// <summary>Raised when the armed stencil is consumed or cleared by the canvas.</summary>
    public event Action<ShapeKind?>? ArmedKindChanged;

    /// <summary>Raised when one of your own shapes has been put down and is no longer armed.</summary>
    public event Action? StencilPlaced;

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

    /// <summary>Where the pointer is on the page, or null when it has left the canvas.</summary>
    public event Action<Point?>? PointerOnPage;

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
        Document.Refresh();

        context.DrawRectangle(WorkspaceBrush, null, new Rect(Bounds.Size));

        // Everything below is drawn in page coordinates; the transform does the zoom.
        using (context.PushTransform(
                   Matrix.CreateScale(Zoom, Zoom) *
                   Matrix.CreateTranslation(PageMargin * Zoom, PageMargin * Zoom)))
        {
            var page = PageRect;
            context.DrawRectangle(ShadowBrush, null, page.Translate(new Vector(Screen(3), Screen(3))));
            context.DrawRectangle(Document.CurrentPage.Paper(), null, page);

            // The page's own furniture, under the drawing, and clipped to the paper so a
            // watermark that overruns does not spill onto the workspace round it.
            using (context.PushClip(page))
                PageFurniture.DrawWatermark(context, Document, Document.CurrentPage);

            if (Grid.ShowGrid)
                RenderGrid(context);

            context.DrawRectangle(null, ScreenPen(PageBorderBrush, 1), page);

            // The margin guide sits under the drawing, as the grid does: it is something to
            // line work up against, not part of it.
            if (Document.Margin > 0)
            {
                var inside = page.Deflate(Document.Margin);

                if (inside.Width > 0 && inside.Height > 0)
                    context.DrawRectangle(null, ScreenDashPen(MarginBrush, 1, 4), inside);
            }

            foreach (var shape in Document.Shapes)
                shape.Render(context, !ReferenceEquals(shape, _editing));

            _pendingConnector?.Render(context, false);

            if (_glueTarget is { } glued)
                context.DrawRectangle(null, ScreenPen(GlueBrush, 2), glued.Bounds.Inflate(Screen(2)));

            RenderConnectionPoints(context);

            if (_ghost is { } ghost)
                context.DrawRectangle(null, ScreenDashPen(GhostBrush, 1.5, 4), ghost);

            // Drawn last, over everything, since the whole job of a guide is to be seen.
            foreach (var (a, b) in _guides)
                context.DrawLine(ScreenPen(GuideBrush, 1), a, b);

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
        _dragMode == DragMode.MovingEndpoint || _dragMode == DragMode.MovingTail;

    /// <summary>Only the shape under the pointer offers its ports, to keep the page readable.</summary>
    private IEnumerable<DiagramShape> PortCandidates() =>
        _glueTarget is null ? [] : [_glueTarget];

    /// <summary>
    /// What an end dropped here should glue to, and which of that shape's points it should be
    /// pinned to - or nothing, for an end dropped on bare page.
    ///
    /// A shape under the pointer is the obvious answer, but it is not the only one. A shape is
    /// only as big as its outline, and an outline can sit a long way inside the box around it:
    /// the corner of a diamond's box is outside the diamond, and so is most of an ellipse's.
    /// Dropping an end there used to glue to nothing at all while looking for all the world
    /// like it had landed on the shape - and it stayed looking that way until the shape was
    /// moved and the line stayed behind.
    ///
    /// So a point near a shape's connection point counts as being on that shape. Near means
    /// within a grid step, which is what the pointer is already being snapped to, and never
    /// less than the radius a port has always snapped from.
    /// </summary>
    private (DiagramShape? Shape, int Port) GlueAt(Point pagePoint, DiagramShape? skip = null)
    {
        var under = Document.Shapes
            .Where(shape => shape is not ConnectorShape && !ReferenceEquals(shape, skip))
            .LastOrDefault(shape => shape.HitTest(pagePoint));

        if (under is not null)
            return (under, NearestPort(under, pagePoint));

        var reach = Math.Max(Grid.Size, Screen(PortSnapPixels));
        var closest = double.MaxValue;

        DiagramShape? found = null;
        var port = -1;

        foreach (var shape in Document.Shapes)
        {
            if (shape is ConnectorShape || ReferenceEquals(shape, skip))
                continue;

            var points = shape.ConnectionPoints;

            for (var i = 0; i < points.Count; i++)
            {
                var gap = Math.Sqrt(
                    Math.Pow(points[i].X - pagePoint.X, 2) +
                    Math.Pow(points[i].Y - pagePoint.Y, 2));

                if (gap > reach || gap >= closest)
                    continue;

                closest = gap;
                found = shape;
                port = i;
            }
        }

        return (found, port);
    }

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
                var doomedHandle = _bendDoomed ? _activeHandle : -1;

                for (var i = 0; i < ConnectorHandles(connector).Count; i++)
                {
                    var handle = ConnectorHandles(connector)[i];

                    // A solid handle moves what is there; a hollow one adds a new bend; and a
                    // bend that would go if it were let go now is marked as such.
                    var fill = i == doomedHandle
                        ? DoomedHandleBrush
                        : handle.Kind == HandleKind.Midpoint
                            ? MidpointHandleBrush
                            : HandleBrush;

                    context.DrawRectangle(fill, outline, handle.Rect);
                }

                return;
            }

            foreach (var handle in SelectionHandles(selection[0]))
                context.DrawRectangle(HandleBrush, outline, handle);

            // The turn handle, on a stalk from the top of the shape so it is clear what it
            // belongs to and clear that it is not another corner to drag.
            if (RotateHandle(selection[0]) is { } turn)
            {
                var bounds = selection[0].Bounds;
                var top = Turned(selection[0], new Point(bounds.Center.X, bounds.Top));

                context.DrawLine(ScreenPen(SelectionBrush, 1), top, turn.Center);
                context.DrawEllipse(RotateHandleBrush, outline, turn.Center,
                    turn.Width / 2, turn.Height / 2);
            }

            DrawTailHandle(context, selection[0], outline);
            DrawLabelGrip(context, selection[0], outline);

            return;
        }

        // A group of shapes gets one dashed box round the lot, with the same handles a single
        // shape has: the selection resizes as though it were one shape.
        context.DrawRectangle(null, ScreenDashPen(SelectionBrush, 1, 6),
            ShapeClipboard.Union(selection).Inflate(Screen(5)));

        if (!CanResizeSelection())
            return;

        foreach (var handle in BoxHandles(SelectionBox()))
            context.DrawRectangle(HandleBrush, outline, handle);
    }

    /// <summary>
    /// True when the selection has something in it that a stretch would actually move. A lane
    /// is placed by its pool, so a selection of nothing but lanes has no size of its own.
    /// </summary>
    private bool CanResizeSelection() =>
        Document.Selection.Count > 1 && Document.Selection.Any(Stretchable);

    /// <summary>
    /// Whether a stretch of the whole selection moves this shape. A connector has no box to
    /// resize but its ends and bends still travel; a lane is placed by its pool, so a
    /// selection of nothing but lanes has no size of its own to change.
    /// </summary>
    private static bool Stretchable(DiagramShape shape) =>
        shape is ConnectorShape || shape.IsBoxResizable;

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
    /// <summary>The handles as drawn, for the tests to look at.</summary>
    internal Rect[] HandlesForTest(DiagramShape shape) => SelectionHandles(shape);

    private Rect[] SelectionHandles(DiagramShape shape)
    {
        if (shape is ConnectorShape connector)
            return ConnectorHandles(connector).Select(handle => handle.Rect).ToArray();

        // The handles ride round with the shape, so the corner you grab is the corner you see.
        return BoxPoints(shape.Bounds)
            .Select(point => HandleRect(Turned(shape, point)))
            .ToArray();
    }

    /// <summary>
    /// Aims a callout's tail. Dropped on another shape's connection point the tail pins to it
    /// and then follows that shape about; dropped anywhere else it simply stays where it was
    /// put, as a fraction of the bubble, and so travels and stretches with the bubble.
    ///
    /// The bubble itself is left out of the search - a callout that pointed at itself would be
    /// telling nobody anything.
    /// </summary>
    private void MoveTail(CalloutShape callout, Point pagePoint)
    {
        var (target, port) = GlueAt(pagePoint, skip: callout);

        // Only a point will do here, not merely a shape: the tail has to land somewhere exact.
        var pinned = target is not null && port >= 0;

        _glueTarget = pinned ? target : null;
        _portShape = pinned ? target : null;
        _portIndex = pinned ? port : -1;

        if (pinned)
        {
            callout.TailShape = target;
            callout.TailPort = port;
        }
        else
        {
            callout.TailShape = null;
            callout.TailPort = -1;

            var box = callout.Bounds;

            if (box.Width > 0 && box.Height > 0)
            {
                var local = callout.Unrotate(pagePoint);

                callout.Tail = new Point(
                    (local.X - box.X) / box.Width,
                    (local.Y - box.Y) / box.Height);
            }
        }

        _dragChanged = true;
        InvalidateVisual();
        ReportStatus();
    }

    /// <summary>
    /// Puts the shape's label where the drag has taken it. The frame is held as fractions of
    /// the shape, so the move is measured in the shape's own upright frame - a turned shape's
    /// label travels with the turn rather than across the screen.
    /// </summary>
    private void MoveLabel(DiagramShape shape, Point pagePoint)
    {
        var box = shape.Bounds;

        if (box.Width <= 0 || box.Height <= 0)
            return;

        var moved = shape.Unrotate(pagePoint) - shape.Unrotate(_dragOrigin);

        shape.TextFrame = new Rect(
            _labelStart.X + moved.X / box.Width,
            _labelStart.Y + moved.Y / box.Height,
            _labelStart.Width,
            _labelStart.Height);

        _dragChanged = true;
        InvalidateVisual();
        ReportStatus();
    }

    /// <summary>
    /// Stretches the block the label is wrapped into. It is measured in the shape's upright
    /// frame and then kept as fractions of the shape, the same as the block's position, so a
    /// block widened on a turned shape widens along the shape rather than across the screen.
    /// </summary>
    private void SizeLabel(DiagramShape shape, Point pagePoint)
    {
        var box = shape.Bounds;

        if (box.Width <= 0 || box.Height <= 0)
            return;

        var area = Resize(_dragStartBounds, _activeHandle, shape.Unrotate(pagePoint) - _labelGrab);

        shape.TextFrame = new Rect(
            (area.X - box.X) / box.Width,
            (area.Y - box.Y) / box.Height,
            area.Width / box.Width,
            area.Height / box.Height);

        _dragChanged = true;
        InvalidateVisual();
        ReportStatus();
    }

    /// <summary>Puts every selected shape's label back in the middle of the shape.</summary>
    public void ResetLabels()
    {
        var moved = Document.Selection.Where(shape => shape.TextFrame is not null).ToList();

        if (moved.Count == 0)
            return;

        using (Document.BeginBatch())
        {
            foreach (var shape in moved)
                shape.TextFrame = null;

            Document.MarkModified();
        }

        InvalidateVisual();
    }

    /// <summary>
    /// The corners of the block the label is wrapped into, for dragging it wider or taller.
    ///
    /// Where the block is still the shape itself, its corners would sit exactly on the shape's
    /// own and neither could be grabbed - so they step outside it by a handle's width. Once
    /// the label has been moved somewhere of its own there is nothing to collide with and they
    /// sit on the block, where they belong.
    /// </summary>
    private Rect[] LabelHandles(DiagramShape shape)
    {
        if (LabelGrip(shape) is null)
            return [];

        var area = shape.LabelArea;
        var clear = shape.TextFrame is null ? Screen(HandleSize) : 0;

        Rect Corner(double x, double y) =>
            HandleRect(Turned(shape, new Point(
                x < area.Center.X ? x - clear : x + clear,
                y < area.Center.Y ? y - clear : y + clear)), 0.8);

        return
        [
            Corner(area.Left, area.Top),
            Corner(area.Right, area.Top),
            Corner(area.Right, area.Bottom),
            Corner(area.Left, area.Bottom)
        ];
    }

    /// <summary>Which corner of the label's block is under the pointer, if any.</summary>
    private int LabelHandleAt(Point pagePoint)
    {
        if (Document.Selection.Count != 1 || Document.Selected is not { } shape)
            return -1;

        var handles = LabelHandles(shape);

        for (var i = 0; i < handles.Length; i++)
            if (handles[i].Inflate(Screen(2)).Contains(pagePoint))
                return i;

        return -1;
    }

    /// <summary>Which corner of a block one of its four handles belongs to.</summary>
    private static Point BlockCorner(Rect block, int index) => index switch
    {
        1 => new Point(block.Right, block.Top),
        2 => new Point(block.Right, block.Bottom),
        3 => new Point(block.Left, block.Bottom),
        _ => new Point(block.Left, block.Top)
    };

    /// <summary>A corner of the label's block, as one of the eight a shape is resized by.</summary>
    private static int LabelCorner(int index) => index switch
    {
        1 => 2,
        2 => 4,
        3 => 6,
        _ => 0
    };

    /// <summary>
    /// The handle that aims a callout's tail, on a stalk back to the bubble so it is plain
    /// what it belongs to. Green once the tail is pinned to another shape's connection point,
    /// which is the difference between pointing near a thing and pointing at it.
    /// </summary>
    private void DrawTailHandle(DrawingContext context, DiagramShape shape, IPen outline)
    {
        if (shape is not CalloutShape callout || TailHandle(shape) is not { } grip)
            return;

        var brush = callout.TailShape is null ? TailHandleBrush : GlueBrush;
        var centre = grip.Center;
        var half = grip.Width / 2;

        // Only where the shape itself does not already lead the eye out to the tip: a spike
        // is its own stalk, and drawing another down the middle of it says nothing twice.
        if (!callout.Spiked)
            context.DrawLine(ScreenPen(brush, 1), Turned(shape, shape.Bounds.Center), centre);

        // A diamond rather than a square, to say it aims rather than resizes.
        var diamond = new StreamGeometry();

        using (var sink = diamond.Open())
        {
            sink.BeginFigure(new Point(centre.X, centre.Y - half), true);
            sink.LineTo(new Point(centre.X + half, centre.Y));
            sink.LineTo(new Point(centre.X, centre.Y + half));
            sink.LineTo(new Point(centre.X - half, centre.Y));
            sink.EndFigure(true);
        }

        context.DrawGeometry(brush, outline, diamond);
    }

    /// <summary>Where that handle sits: on the tip of the tail, wherever the tail has got to.</summary>
    private Rect? TailHandle(DiagramShape shape) =>
        shape is CalloutShape callout ? HandleRect(Turned(shape, callout.TailTip)) : null;

    /// <summary>
    /// The grip that moves the label, and - once the label has been moved off the shape - the
    /// box the words are wrapped into, so it is clear where they will go.
    /// </summary>
    private void DrawLabelGrip(DrawingContext context, DiagramShape shape, IPen outline)
    {
        if (LabelGrip(shape) is not { } grip)
            return;

        var area = shape.LabelArea;
        var pen = ScreenPen(LabelHandleBrush, 1);

        if (shape.TextFrame is not null)
        {
            var corners = new[]
            {
                new Point(area.Left, area.Top), new Point(area.Right, area.Top),
                new Point(area.Right, area.Bottom), new Point(area.Left, area.Bottom)
            };

            var dashed = ScreenDashPen(LabelHandleBrush, 1, 4);

            for (var i = 0; i < corners.Length; i++)
                context.DrawLine(dashed,
                    Turned(shape, corners[i]),
                    Turned(shape, corners[(i + 1) % corners.Length]));
        }

        context.DrawLine(pen, Turned(shape, new Point(area.Left, area.Center.Y)), grip.Center);
        context.DrawEllipse(LabelHandleBrush, outline, grip.Center, grip.Width / 2, grip.Height / 2);

        // Round, and in the label's own colour, so they are not taken for the shape's corners.
        foreach (var handle in LabelHandles(shape))
            context.DrawEllipse(LabelHandleBrush, outline, handle.Center,
                handle.Width / 2, handle.Height / 2);
    }

    /// <summary>
    /// Where the turn handle sits: above the top of the shape, on a short stalk, in the frame
    /// the shape is already turned into so it travels round with it.
    /// </summary>
    private Rect? RotateHandle(DiagramShape shape)
    {
        if (!shape.CanRotate)
            return null;

        var bounds = shape.Bounds;
        var above = new Point(bounds.Center.X, bounds.Top - Screen(RotateHandleReach));

        return HandleRect(Turned(shape, above));
    }

    /// <summary>
    /// Where the grip that moves a shape's label sits: out to the side of the block the words
    /// are in, on a short stalk, so it is clear what it belongs to and clear that it is not
    /// another corner to drag. Off to the side rather than on the label, because the middle of
    /// a shape is where one grabs the shape itself.
    ///
    /// Nothing for a shape with no words to move, or for a connector, whose label is dragged
    /// by taking hold of the label itself - there being no shape underneath to confuse it with.
    /// </summary>
    private Rect? LabelGrip(DiagramShape shape)
    {
        if (shape is ConnectorShape || string.IsNullOrWhiteSpace(shape.Text))
            return null;

        var area = shape.LabelArea;
        var beside = new Point(area.Left - Screen(LabelGripReach), area.Center.Y);

        return HandleRect(Turned(shape, beside));
    }

    /// <summary>A point in the shape's upright frame, moved to where the turn puts it.</summary>
    private static Point Turned(DiagramShape shape, Point point)
    {
        if (!shape.IsRotated)
            return point;

        var centre = shape.Bounds.Center;
        var radians = shape.Rotation * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var dx = point.X - centre.X;
        var dy = point.Y - centre.Y;

        return new Point(centre.X + dx * cos - dy * sin, centre.Y + dx * sin + dy * cos);
    }

    /// <summary>The eight handles round a rectangle, in the order the resize maths expects.</summary>
    private Rect[] BoxHandles(Rect bounds) =>
        BoxPoints(bounds).Select(point => HandleRect(point)).ToArray();

    private static Point[] BoxPoints(Rect bounds) =>
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

    /// <summary>
    /// The box the handles of a multiple selection sit on, and the box a stretch maps out of.
    /// It is the plain union: the dashed outline is drawn a few pixels outside it so as not to
    /// sit on the shapes, but stretching has to work from where the shapes actually are or the
    /// anchored corner drifts and the scale comes out slightly wrong.
    /// </summary>
    private Rect SelectionBox() => ShapeClipboard.Union(Document.Selection);

    /// <summary>
    /// Whether a shape is really the background rather than something on it. Judged by area
    /// against the page, so it catches a rectangle drawn over everything as readily as one
    /// that arrived with an import.
    /// </summary>
    private bool IsBackdrop(DiagramShape shape)
    {
        var page = Document.PageWidth * Document.PageHeight;

        return page > 0 && shape.Bounds.Width * shape.Bounds.Height >= page * 0.8;
    }

    /// <summary>Handles are a fixed size on screen, so they stay grabbable at any zoom.</summary>
    private Rect HandleRect(Point centre, double scale = 1)
    {
        var size = Screen(HandleSize) * scale;
        return new Rect(centre.X - size / 2, centre.Y - size / 2, size, size);
    }

    /// <summary>The handle under the point, or -1.</summary>
    private int HandleAt(Point pagePoint)
    {
        var handles = Document.Selection.Count switch
        {
            1 => SelectionHandles(Document.Selection[0]),
            > 1 when CanResizeSelection() => BoxHandles(SelectionBox()),
            _ => []
        };

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

    /// <summary>The far end of a fade, or nothing at all to fill flat again.</summary>
    public void SetFillTo(Color? value) =>
        ApplyFormat(shape => shape.FillTo = value, style => style with { FillTo = value });

    public void SetFillAngle(double value) =>
        ApplyFormat(shape => shape.FillAngle = value, style => style with { FillAngle = value });

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

    public void SetFontName(string value) =>
        ApplyFormat(shape => shape.FontName = value, style => style with { FontName = value });

    public void SetBold(bool value) =>
        ApplyFormat(shape => shape.Bold = value, style => style with { Bold = value });

    public void SetItalic(bool value) =>
        ApplyFormat(shape => shape.Italic = value, style => style with { Italic = value });

    public void SetTextAlign(TextAlign value) =>
        ApplyFormat(shape => shape.TextAlign = value, style => style with { TextAlign = value });

    public void SetTextVerticalAlign(TextVerticalAlign value) =>
        ApplyFormat(shape => shape.TextVerticalAlign = value,
            style => style with { TextVerticalAlign = value });

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

    private static string? StencilFromData(IDataObject data) =>
        data.Get(ToolboxPanel.CustomDragFormat) as string;

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var at = ToPage(e.GetPosition(this));

        if (StencilFromData(e.Data) is { } id)
        {
            e.DragEffects = DragDropEffects.Copy;
            _ghost = StencilGhost(id, at);
            InvalidateVisual();
            return;
        }

        if (KindFromData(e.Data) is not { } kind)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        _ghost = DefaultBoundsAt(kind, at);
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

        if (StencilFromData(e.Data) is { } id)
        {
            PlaceStencil(id, ToPage(e.GetPosition(this)));
            ClearArmed();
            Focus();
            e.Handled = true;
            return;
        }

        if (KindFromData(e.Data) is not { } kind)
            return;

        PlaceShape(kind, ToPage(e.GetPosition(this)));
        ClearArmed();
        Focus();
        e.Handled = true;
    }

    private void ClearArmed()
    {
        if (ArmedStencil is not null)
        {
            ArmedStencil = null;
            StencilPlaced?.Invoke();
        }

        if (ArmedKind is null)
            return;

        ArmedKind = null;
        ArmedKindChanged?.Invoke(null);
    }

    /// <summary>
    /// Drops a saved fragment with its middle at the point, and leaves it selected - the same
    /// as a paste, so it can be moved straight away if it did not land where it was wanted.
    /// </summary>
    public IReadOnlyList<DiagramShape> PlaceStencil(string id, Point pageCentre)
    {
        if (StencilLibrary.Find(id) is not { } stencil)
            return [];

        var placed = ShapeClipboard.Place(Document, stencil.Fragment, Grid.Snap(pageCentre));

        if (placed.Count > 0)
        {
            ReviewContainment();
            InvalidateVisual();
            ReportStatus();
        }

        return placed;
    }

    /// <summary>The outline a saved fragment would take up, centred where it would land.</summary>
    private Rect? StencilGhost(string id, Point pageCentre)
    {
        if (StencilLibrary.Find(id) is not { } stencil ||
            ShapeClipboard.Extent(stencil.Fragment) is not { } extent)
            return null;

        var centre = Grid.Snap(pageCentre);

        return new Rect(
            centre.X - extent.Width / 2,
            centre.Y - extent.Height / 2,
            extent.Width,
            extent.Height);
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

        if (Document.Selection.Count == 1 && Document.Selected is { } turnable &&
            RotateHandle(turnable) is { } spot && spot.Inflate(Screen(2)).Contains(pagePoint))
        {
            BeginRotate(turnable, pagePoint, e);
            return;
        }

        // A callout's tail is aimed by its own handle, and that is looked for early: it sits
        // outside the bubble, where nothing else of the shape's is.
        if (Document.Selection.Count == 1 && Document.Selected is CalloutShape aimed &&
            TailHandle(aimed) is { } tip && tip.Inflate(Screen(2)).Contains(pagePoint))
        {
            _dragMode = DragMode.MovingTail;
            _dragOrigin = pagePoint;

            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // A connector's label can be dragged clear of whatever it has landed on.
        if (Document.Selection.Count == 1 &&
            Document.Selected is ConnectorShape { Text.Length: > 0 } labelled &&
            labelled.LabelArea.Contains(pagePoint))
        {
            _dragMode = DragMode.MovingLabel;
            _dragOrigin = pagePoint;
            _labelGrip = labelled.LabelOffset;

            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // A shape's label is moved by its grip rather than by taking hold of the words, which
        // are in the middle of the shape and so are how one takes hold of the shape itself.
        if (Document.Selection.Count == 1 && Document.Selected is { } lettered &&
            LabelGrip(lettered) is { } gripped && gripped.Inflate(Screen(2)).Contains(pagePoint))
        {
            _dragMode = DragMode.MovingLabel;
            _dragOrigin = pagePoint;
            _labelStart = lettered.TextFrame ?? new Rect(0, 0, 1, 1);

            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // A corner of the label's block, before the shape's own corners: where the two overlap
        // the label is the one that has been deliberately put there.
        if (LabelHandleAt(pagePoint) is var corner and >= 0 && Document.Selected is { } sizing)
        {
            _dragMode = DragMode.SizingLabel;
            _activeHandle = LabelCorner(corner);
            _dragStartBounds = sizing.LabelArea;
            _labelGrab = sizing.Unrotate(pagePoint) - BlockCorner(_dragStartBounds, corner);

            // A label that has not been moved has no block of its own yet; stretching one
            // gives it the block it was drawn in, which is the shape.
            sizing.TextFrame ??= new Rect(0, 0, 1, 1);

            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        var handle = HandleAt(pagePoint);

        if (handle >= 0 && Document.Selection.Count > 1)
        {
            BeginSelectionResize(handle, e);
            return;
        }

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

        if (ArmedStencil is { } saved)
        {
            PlaceStencil(saved, pagePoint);
            ClearArmed();
            _ghost = null;
            e.Handled = true;
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

        // The line between two lanes is draggable, but only where there is nothing else to
        // click: a shape sitting across the boundary keeps the click.
        if (hit is null or ContainerShape && LaneDividerAt(pagePoint) is { } divider)
        {
            BeginLaneResize(divider, e);
            return;
        }

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
            // Grouped shapes go in and out of the selection together.
            var members = Document.GroupOf(hit);

            Document.SetSelection(Document.IsSelected(hit)
                ? Document.Selection.Where(shape => !members.Contains(shape)).ToList()
                : Document.Selection.Concat(members).ToList());
        }
        else if (!Document.IsSelected(hit))
        {
            // Clicking inside an existing multi-selection keeps it, so the group can be dragged.
            Document.SetSelection(Document.GroupOf(hit));

            // A backdrop is not raised by being clicked: doing so would bury the very
            // contents the click was aimed past. A container is one by its nature, and so is
            // anything covering most of the page - which is what an imported drawing's
            // background rectangle is, and what makes one swallow a whole page of work.
            if (!hit.IsContainer && !IsBackdrop(hit))
                Document.BringToFront(hit);
        }

        if (Document.IsSelected(hit))
        {
            // A lane has no position of its own to drag - the pool gives it one - so
            // dragging one moves it through the running order instead.
            if (Document.Selection.Count == 1 && hit is ContainerShape { Kind: ShapeKind.Lane } lane)
                BeginLaneReorder(lane, e);
            else
                BeginMove(pagePoint, e);
        }

        InvalidateVisual();
        ReportStatus();
        e.Handled = true;
    }

    /// <summary>How near the line between two lanes counts as grabbing it, in screen pixels.</summary>
    private const double LaneDividerPixels = 4;

    /// <summary>
    /// The lane whose bottom edge the point is on, or null. Only the boundaries between lanes
    /// count: the bottom of the last lane is the bottom of the pool, which is the pool's own
    /// edge to drag.
    /// </summary>
    private ContainerShape? LaneDividerAt(Point pagePoint)
    {
        var reach = Screen(LaneDividerPixels);

        foreach (var pool in Document.Shapes.OfType<ContainerShape>().Where(c => c.Kind == ShapeKind.Pool))
        {
            var body = pool.Body;

            if (pagePoint.X < body.Left || pagePoint.X > body.Right)
                continue;

            var lanes = Document.LanesOf(pool);

            // The last lane's bottom is the pool's, so it is not a divider between lanes.
            for (var i = 0; i < lanes.Count - 1; i++)
            {
                if (Math.Abs(pagePoint.Y - lanes[i].Bounds.Bottom) <= reach)
                    return lanes[i];
            }
        }

        return null;
    }

    private void BeginLaneResize(ContainerShape lane, PointerPressedEventArgs e)
    {
        _dragLane = lane;
        _dragMode = DragMode.ResizingLane;

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <summary>Drags the line between two lanes, giving one the height the other loses.</summary>
    private void ResizeLane(ContainerShape lane, Point pagePoint)
    {
        if (!Document.ResizeLane(lane, Grid.Snap(pagePoint.Y)))
            return;

        _dragChanged = true;
        InvalidateVisual();
        ReportStatus();
    }

    private void BeginLaneReorder(ContainerShape lane, PointerPressedEventArgs e)
    {
        _dragLane = lane;
        _dragMode = DragMode.ReorderingLane;

        e.Pointer.Capture(this);
    }

    /// <summary>Drops the lane into whichever band the pointer is over.</summary>
    private void ReorderLane(ContainerShape lane, Point pagePoint)
    {
        if (lane.Container is not ContainerShape pool)
            return;

        var lanes = Document.LanesOf(pool);
        var body = pool.Body;

        if (lanes.Count < 2 || body.Height <= 0)
            return;

        // Lanes can be different heights, so the band under the pointer is the first one whose
        // bottom it has not passed rather than a fixed fraction of the pool.
        var position = lanes.FindIndex(band => pagePoint.Y < band.Bounds.Bottom);

        if (position < 0)
            position = lanes.Count - 1;

        if (!Document.MoveLaneTo(lane, position))
            return;

        _dragChanged = true;
        InvalidateVisual();
        ReportStatus();
    }

    /// <summary>Remembers where everything sat, so a drag moves the whole selection together.</summary>
    private void BeginMove(Point pagePoint, PointerPressedEventArgs e)
    {
        _dragShapes.Clear();

        // Dragging a container takes what is inside it along.
        _dragShapes.AddRange(Document.WithContents(Document.Selection));

        _dragStartUnion = ShapeClipboard.Union(Document.Selection);
        _dragApplied = default;
        _dragMode = DragMode.Moving;
        _dragOrigin = pagePoint;

        e.Pointer.Capture(this);
    }

    private void StartConnector(Point pagePoint, PointerPressedEventArgs e)
    {
        // The same question the far end is asked, so a line begun near a shape is glued to it
        // rather than merely starting next to it.
        var (target, port) = GlueAt(pagePoint);
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

                // A turned shape is resized in the frame it is described in - its bounds are
                // still the upright rectangle - so the pointer is turned back to meet it.
                sizing.Bounds = Document.ClampToPage(
                    Resize(_dragStartBounds, _activeHandle, sizing.Unrotate(pagePoint)));
                _dragChanged = true;
                InvalidateVisual();
                ReportStatus();
                return;

            case DragMode.MovingLabel when Document.Selected is ConnectorShape labelled:
                labelled.LabelOffset = _labelGrip + (pagePoint - _dragOrigin);
                _dragChanged = true;
                InvalidateVisual();
                ReportStatus();
                return;

            case DragMode.MovingLabel when Document.Selected is { } lettered:
                MoveLabel(lettered, pagePoint);
                return;

            case DragMode.SizingLabel when Document.Selected is { } stretching:
                SizeLabel(stretching, pagePoint);
                return;

            case DragMode.MovingTail when Document.Selected is CalloutShape aiming:
                MoveTail(aiming, pagePoint);
                return;

            case DragMode.Rotating when Document.Selected is { } turning:
                RotateTo(turning, pagePoint, e.KeyModifiers);
                return;

            case DragMode.ResizingSelection:
                ScaleSelection(pagePoint);
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

            case DragMode.ResizingLane when _dragLane is { } resizing:
                ResizeLane(resizing, pagePoint);
                break;

            case DragMode.ReorderingLane when _dragLane is { } lane:
                ReorderLane(lane, pagePoint);
                return;

            case DragMode.DrawingConnector when _pendingConnector is { } pending:
                (_glueTarget, _portIndex) = GlueAt(pagePoint);
                _portShape = _glueTarget;

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
            var (over, port) = GlueAt(pagePoint);

            if (!ReferenceEquals(over, _glueTarget) || port != _portIndex)
            {
                _glueTarget = over;
                _portShape = over;
                _portIndex = port;
                InvalidateVisual();
            }
        }

        UpdateCursor(pagePoint);
        PointerOnPage?.Invoke(pagePoint);
    }

    /// <summary>
    /// Moves everything selected by one delta, snapping the group's own top left corner and
    /// holding the shapes' positions relative to each other.
    /// </summary>
    private void MoveSelection(Point pagePoint)
    {
        var delta = pagePoint - _dragOrigin;

        var loose = new Rect(
            _dragStartUnion.X + delta.X,
            _dragStartUnion.Y + delta.Y,
            _dragStartUnion.Width,
            _dragStartUnion.Height);

        // Lining up with another shape beats landing on the grid: the grid is a fallback for
        // the axis no other shape had anything to say about.
        var (lined, alongX, alongY) = LineUp(loose);

        var target = Document.ClampToPage(new Rect(
            alongX ? lined.X : Grid.Snap(loose.X),
            alongY ? lined.Y : Grid.Snap(loose.Y),
            loose.Width,
            loose.Height));

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

    /// <summary>
    /// Looks for edges and middles on the page that the dragged box has come close to lining
    /// up with, and pulls it onto the nearest on each axis. The lines to draw are collected as
    /// it goes, so what snapped and what is shown cannot disagree.
    /// </summary>
    private (Rect Target, bool AlongX, bool AlongY) LineUp(Rect box)
    {
        _guides.Clear();

        if (!SmartGuides)
            return (box, false, false);

        var moving = new HashSet<DiagramShape>(_dragShapes);

        var others = Document.Shapes
            .Where(shape => shape is not ConnectorShape && !moving.Contains(shape))
            .Select(shape => shape.Bounds)
            .ToList();

        if (others.Count == 0)
            return (box, false, false);

        var reach = Screen(GuideReachPixels);

        var (shiftX, atX, withX) = Nearest(box, others, reach, vertical: true);
        var (shiftY, atY, withY) = Nearest(box, others, reach, vertical: false);

        var target = new Rect(box.X + shiftX, box.Y + shiftY, box.Width, box.Height);

        // Drawn long enough to reach both the shape that moved and the one it lined up with.
        if (withX is { } alignedX)
            _guides.Add((new Point(atX, Math.Min(target.Top, alignedX.Top)),
                new Point(atX, Math.Max(target.Bottom, alignedX.Bottom))));

        if (withY is { } alignedY)
            _guides.Add((new Point(Math.Min(target.Left, alignedY.Left), atY),
                new Point(Math.Max(target.Right, alignedY.Right), atY)));

        return (target, withX is not null, withY is not null);
    }

    /// <summary>
    /// The smallest move along one axis that brings a leading, middle or trailing edge of the
    /// box onto one of another shape's, or nothing when none is near enough.
    /// </summary>
    private static (double Shift, double At, Rect? With) Nearest(
        Rect box, List<Rect> others, double reach, bool vertical)
    {
        double[] mine = vertical
            ? [box.Left, box.Center.X, box.Right]
            : [box.Top, box.Center.Y, box.Bottom];

        var shift = 0.0;
        var at = 0.0;
        Rect? with = null;
        var best = reach;

        foreach (var other in others)
        {
            double[] theirs = vertical
                ? [other.Left, other.Center.X, other.Right]
                : [other.Top, other.Center.Y, other.Bottom];

            foreach (var a in mine)
            foreach (var b in theirs)
            {
                var gap = b - a;

                if (Math.Abs(gap) > best)
                    continue;

                best = Math.Abs(gap);
                shift = gap;
                at = b;
                with = other;
            }
        }

        return (shift, at, with);
    }

    private void DragEndpoint(ConnectorShape connector, Point pagePoint)
    {
        // An end point dropped on a shape - or near enough to one of its points - glues to it;
        // dropped on bare page it un-glues.
        (_glueTarget, _portIndex) = GlueAt(pagePoint);
        _portShape = _glueTarget;

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

        _bendDoomed = IsRedundantBend(connector);
        _dragChanged = true;
        InvalidateVisual();
        ReportStatus();
    }

    /// <summary>
    /// True when the bend being dragged has come back onto the line between its neighbours,
    /// where it is no longer bending anything. Letting go there takes it out - which is the
    /// gesture for removing a bend, alongside double-clicking it.
    /// </summary>
    private bool IsRedundantBend(ConnectorShape connector)
    {
        if (ActiveConnectorHandle(connector) is not { Kind: HandleKind.Corner } handle)
            return false;

        var path = connector.Path;

        // The ends are not bends; there is nothing either side of them to be in line with.
        if (handle.Index <= 0 || handle.Index >= path.Count - 1)
            return false;

        return DistanceToSegment(path[handle.Index], path[handle.Index - 1], path[handle.Index + 1])
               <= Screen(BendRemovalPixels);
    }

    /// <summary>How far a point lies off a line segment, in page units.</summary>
    private static double DistanceToSegment(Point point, Point from, Point to)
    {
        var run = to - from;
        var length = run.X * run.X + run.Y * run.Y;

        if (length <= 0)
            return Distance(point, from);

        // Where along the segment the nearest point is, kept inside its ends.
        var along = Math.Clamp(((point.X - from.X) * run.X + (point.Y - from.Y) * run.Y) / length, 0, 1);

        return Distance(point, new Point(from.X + run.X * along, from.Y + run.Y * along));
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
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

        // A bend dropped back on the line between its neighbours is taken out.
        if (_dragMode == DragMode.MovingBend && _bendDoomed &&
            Document.Selection.Count == 1 && Document.Selected is ConnectorShape doomed &&
            ActiveConnectorHandle(doomed) is { Kind: HandleKind.Corner } corner)
        {
            doomed.RemoveBend(corner.Index);
            _dragChanged = true;
        }

        _bendDoomed = false;

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
        _dragLane = null;
        _scaling.Clear();
        _guides.Clear();
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

        // Sweeping up part of a group takes the whole of it.
        if (_marqueeAdds)
            Document.SetSelection(Document.WithGroups(Document.Selection.Concat(caught)));
        else
            Document.SetSelection(Document.WithGroups(caught));
    }

    /// <summary>
    /// Re-homes whatever was just moved. A shape dropped inside a container joins it, and one
    /// dragged out is let go; a lane's home is decided by the pool, not by where it was left.
    /// </summary>
    /// <summary>
    /// Re-homes whatever has just moved. The set is given rather than assumed, because a
    /// nudge from the keyboard moves things without a drag ever having started - and reading
    /// the drag's own list would then review an empty one and re-home nothing.
    /// </summary>
    private void ReviewContainment(IEnumerable<DiagramShape>? moved = null)
    {
        foreach (var shape in (moved ?? _dragShapes).ToList())
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

        if (Document.Selection.Count == 1 && Document.Selected is { } turnable &&
            RotateHandle(turnable) is { } spot && spot.Inflate(Screen(2)).Contains(pagePoint))
        {
            Cursor = new Cursor(StandardCursorType.Hand);
            return;
        }

        if (Document.Selection.Count == 1 && Document.Selected is CalloutShape pointing &&
            TailHandle(pointing) is { } aim && aim.Inflate(Screen(2)).Contains(pagePoint))
        {
            Cursor = new Cursor(StandardCursorType.Cross);
            return;
        }

        if (Document.Selection.Count == 1 && Document.Selected is { } lettered &&
            LabelGrip(lettered) is { } gripped && gripped.Inflate(Screen(2)).Contains(pagePoint))
        {
            Cursor = new Cursor(StandardCursorType.SizeAll);
            return;
        }

        if (LabelHandleAt(pagePoint) is var labelCorner and >= 0)
        {
            Cursor = new Cursor(HandleCursors[LabelCorner(labelCorner)]);
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
        else if (Tool != EditorTool.Select || ArmedKind is not null || ArmedStencil is not null)
        {
            cursor = StandardCursorType.Cross;
        }
        else
        {
            var hit = Document.HitTest(pagePoint, Screen(LineHitPixels));

            cursor = hit switch
            {
                // The same cursor as a lane's own band, because it does the same kind of thing.
                null or ContainerShape when LaneDividerAt(pagePoint) is not null =>
                    StandardCursorType.SizeNorthSouth,
                ContainerShape { Kind: ShapeKind.Lane } => StandardCursorType.SizeNorthSouth,
                not null => StandardCursorType.SizeAll,
                _ => StandardCursorType.Arrow
            };
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
    /// <summary>
    /// Starts stretching a whole selection. Every shape's starting place is taken now, so each
    /// pointer move maps from where things were rather than from where the last move left them,
    /// which would compound the scaling.
    /// </summary>
    private void BeginSelectionResize(int handle, PointerPressedEventArgs e)
    {
        _scaling.Clear();

        // A pool carries its lanes, and its lanes carry their contents, so anything inside a
        // pool that is itself being stretched is left to the pool rather than moved twice.
        foreach (var shape in Document.Selection)
        {
            if (!Stretchable(shape) || InsideScaledPool(shape))
                continue;

            _scaling.Add(ShapeArranger.Snapshot(shape));
        }

        _dragMode = DragMode.ResizingSelection;
        _activeHandle = handle;
        _dragStartBounds = SelectionBox();

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private bool InsideScaledPool(DiagramShape shape)
    {
        for (var at = shape.Container; at is not null; at = at.Container)
        {
            if (at is ContainerShape { Kind: ShapeKind.Pool } && Document.IsSelected(at))
                return true;
        }

        return false;
    }

    private void BeginRotate(DiagramShape shape, Point pagePoint, PointerPressedEventArgs e)
    {
        _dragMode = DragMode.Rotating;

        // Remembered as the difference between where the pointer is and where the shape is
        // already turned to, so the shape does not jump to meet the pointer on the first move.
        _rotateGrip = Angle(shape.Bounds.Center, pagePoint) - shape.Rotation;

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void RotateTo(DiagramShape shape, Point pagePoint, KeyModifiers modifiers)
    {
        var wanted = Angle(shape.Bounds.Center, pagePoint) - _rotateGrip;

        if (modifiers.HasFlag(KeyModifiers.Shift))
            wanted = Math.Round(wanted / RotateStep) * RotateStep;

        if (!Document.Rotate([shape], wanted, absolute: true))
            return;

        _dragChanged = true;
        InvalidateVisual();
        ReportStatus();
    }

    private static double Angle(Point centre, Point point) =>
        Math.Atan2(point.Y - centre.Y, point.X - centre.X) * 180 / Math.PI + 90;

    private void ScaleSelection(Point pagePoint)
    {
        if (_scaling.Count == 0)
            return;

        ShapeArranger.Scale(_scaling, _dragStartBounds,
            Resize(_dragStartBounds, _activeHandle, pagePoint));

        _dragChanged = true;
        InvalidateVisual();
        ReportStatus();
    }

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

    /// <summary>
    /// Moves the selection by one step of the grid. Public because the window offers the same
    /// thing on Alt and the arrows, which has to work wherever the keyboard focus happens to
    /// be - a plain arrow only reaches here while the page itself has it.
    /// </summary>
    public bool Nudge(Key key)
    {
        if (Document.Selection.Count == 0)
            return false;

        var step = Grid.SnapToGrid ? Grid.Size : 1;
        var dx = key == Key.Left ? -step : key == Key.Right ? step : 0;
        var dy = key == Key.Up ? -step : key == Key.Down ? step : 0;

        if (dx == 0 && dy == 0)
            return false;

        // Nudged as a unit so the whole selection stops at the page edge together, rather
        // than the leading shape stopping and the rest closing up behind it.
        var union = ShapeClipboard.Union(Document.Selection);
        var target = Document.ClampToPage(new Rect(union.X + dx, union.Y + dy, union.Width, union.Height));
        var applied = new Vector(target.X - union.X, target.Y - union.Y);

        if (applied.X == 0 && applied.Y == 0)
            return false;

        // The same set a drag would move: a container takes its contents with it.
        var moving = Document.WithContents(Document.Selection);

        foreach (var shape in moving)
            shape.Translate(applied);

        ReviewContainment(moving);
        Document.MarkModified();
        InvalidateVisual();
        ReportStatus();

        return true;
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

        var bounds = _editing.LabelArea;
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
        _dragLane = null;
        _scaling.Clear();
        _guides.Clear();
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
        if (_bendDoomed)
        {
            StatusChanged?.Invoke("Let go to remove this bend");
            return;
        }

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
                $"W {shape.Bounds.Width:0}  H {shape.Bounds.Height:0}" +
                (shape.IsRotated ? $"  {shape.Rotation:0}°" : string.Empty),
            _ => Tool switch
            {
                EditorTool.Connector => "Drag between two shapes to connect them",
                EditorTool.Text => "Click the page to add a text box",
                _ => ArmedStencil is { } saved && StencilLibrary.Find(saved) is { } stencil
                    ? $"Click the page to place {stencil.Name}"
                    : ArmedKind is { } armed
                        ? $"Click the page to place a {ShapeFactory.DisplayName(armed)}"
                        : $"{Document.Shapes.Count} shape(s) on the page"
            }
        };

        StatusChanged?.Invoke(text);
    }
}
