using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using AVASvgMaker.Views;

namespace AVASvgMaker.Tests;

/// <summary>
/// The bits every test wants: a document to draw on, and a way to drive the canvas with real
/// pointer events rather than by calling the methods a pointer would have called.
/// </summary>
internal static class Harness
{
    private static readonly IPointer Mouse = new Pointer(1, PointerType.Mouse, true);

    private static PointerPointProperties Down =>
        new(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);

    private static PointerPointProperties Up =>
        new(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);

    public static DiagramDocument Page(double width = 800, double height = 600)
    {
        var document = new DiagramDocument();
        document.SetPageSize(width, height);
        return document;
    }

    public static DiagramShape Box(DiagramDocument document, Rect at, string text = "",
        ShapeKind kind = ShapeKind.Rectangle)
    {
        var shape = ShapeFactory.Create(kind, at);
        shape.Text = text;
        document.Add(shape);
        return shape;
    }

    public static ConnectorShape Join(
        DiagramDocument document, DiagramShape from, int fromPort, DiagramShape to, int toPort)
    {
        var connector = new ConnectorShape(default, default)
        {
            StartShape = from, StartPort = fromPort, EndShape = to, EndPort = toPort,
            Routing = ConnectorRouting.Orthogonal
        };

        document.Add(connector);
        return connector;
    }

    /// <summary>A window showing, laid out, with the grid out of the way of the arithmetic.</summary>
    public static (MainWindow Window, DrawingCanvas Canvas) Editor(double width = 800, double height = 600)
    {
        var window = new MainWindow();
        window.Show();

        var canvas = window.FindControl<DrawingCanvas>("Canvas")!;

        canvas.Document.SetPageSize(width, height);
        canvas.Grid.SnapToGrid = false;
        canvas.SyncPageSize();

        // Headless input goes to the focused element of an active window; without both, a
        // key press is simply dropped and a test reads as a feature that does not work.
        window.Activate();
        canvas.Focus();

        Settle(window);
        return (window, canvas);
    }

    /// <summary>A key press through the real input path, as the windowing system would send it.</summary>
    public static void Press(MainWindow window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Avalonia.Headless.HeadlessWindowExtensions.KeyPressQwerty(window, Physical(key), modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    private static PhysicalKey Physical(Key key) => key switch
    {
        Key.Left => PhysicalKey.ArrowLeft,
        Key.Right => PhysicalKey.ArrowRight,
        Key.Up => PhysicalKey.ArrowUp,
        Key.Down => PhysicalKey.ArrowDown,
        Key.Delete => PhysicalKey.Delete,
        Key.Escape => PhysicalKey.Escape,
        Key.F2 => PhysicalKey.F2,
        _ => PhysicalKey.None
    };

    public static void Settle(MainWindow window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    public static void Press(DrawingCanvas canvas, Point page) =>
        Send(canvas, InputElement.PointerPressedEvent, page, Down);

    public static void MoveTo(DrawingCanvas canvas, Point page) =>
        Send(canvas, InputElement.PointerMovedEvent, page, Down);

    public static void Release(DrawingCanvas canvas, Point page) =>
        Send(canvas, InputElement.PointerReleasedEvent, page, Up);

    public static void Click(DrawingCanvas canvas, Point page)
    {
        Press(canvas, page);
        Release(canvas, page);
    }

    /// <summary>A drag with a step part-way, because one jump is not how a pointer moves.</summary>
    public static void Drag(DrawingCanvas canvas, Point from, Point to)
    {
        Press(canvas, from);
        MoveTo(canvas, new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2));
        MoveTo(canvas, to);
        Release(canvas, to);
    }

    /// <summary>Picks a shape up by its middle and puts its top-left corner where asked.</summary>
    public static void MoveShape(DrawingCanvas canvas, DiagramShape shape, Point corner)
    {
        var from = shape.Bounds.Center;

        Drag(canvas, from, new Point(
            from.X + (corner.X - shape.Bounds.X),
            from.Y + (corner.Y - shape.Bounds.Y)));
    }

    public static void Send(DrawingCanvas canvas, RoutedEvent routed, Point page, PointerPointProperties props)
    {
        // The canvas draws the page inset by its workspace margin, and scaled by the zoom.
        var at = new Point(
            (page.X + DrawingCanvas.PageMargin) * canvas.Zoom,
            (page.Y + DrawingCanvas.PageMargin) * canvas.Zoom);

        RoutedEventArgs args = routed == InputElement.PointerPressedEvent
            ? new PointerPressedEventArgs(canvas, Mouse, canvas, at, 0, props, KeyModifiers.None)
            : routed == InputElement.PointerReleasedEvent
                ? new PointerReleasedEventArgs(canvas, Mouse, canvas, at, 0, props, KeyModifiers.None, MouseButton.Left)
                : new PointerEventArgs(routed, canvas, Mouse, canvas, at, 0, props, KeyModifiers.None);

        canvas.RaiseEvent(args);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Raises a press in the middle of any control, for the ones that are not the canvas. The
    /// control's own parent stands in as the visual root, because a control's Bounds are given
    /// in its parent's coordinates and a handler asking where the pointer is will ask for them
    /// relative to something up that chain.
    /// </summary>
    public static void PressOn(Control control)
    {
        var root = (Visual?)control.GetVisualParent() ?? control;
        var at = new Point(control.Bounds.Center.X, control.Bounds.Center.Y);

        control.RaiseEvent(new PointerPressedEventArgs(
            control, Mouse, root, at, 0, Down, KeyModifiers.None));

        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>How far two routes run alongside one another, within the given spacing.</summary>
    public static double Company(ConnectorShape one, ConnectorShape two, double spacing)
    {
        var total = 0.0;

        foreach (var (a, b) in Segments(one))
        foreach (var (c, d) in Segments(two))
        {
            var horizontal = Math.Abs(a.Y - b.Y) < 0.01;

            if (horizontal != (Math.Abs(c.Y - d.Y) < 0.01))
                continue;

            if (horizontal)
            {
                if (Math.Abs(c.Y - a.Y) > spacing) continue;
                total += Overlap(a.X, b.X, c.X, d.X);
            }
            else
            {
                if (Math.Abs(c.X - a.X) > spacing) continue;
                total += Overlap(a.Y, b.Y, c.Y, d.Y);
            }
        }

        return total;
    }

    public static (Point, Point)[] Segments(ConnectorShape connector) => connector.Path
        .Zip(connector.Path.Skip(1))
        .Select(pair => (pair.First, pair.Second))
        .ToArray();

    public static double Overlap(double a1, double a2, double b1, double b2) => Math.Max(0,
        Math.Min(Math.Max(a1, a2), Math.Max(b1, b2)) - Math.Max(Math.Min(a1, a2), Math.Min(b1, b2)));

    public static string Path(ConnectorShape connector) => string.Join(" ", connector.Path);
}
