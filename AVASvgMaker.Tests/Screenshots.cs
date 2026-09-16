using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using AVASvgMaker.Models;
using Xunit;

namespace AVASvgMaker.Tests;

/// <summary>
/// Rebuilds the screenshots in Images/ that show the application window, so that a change to
/// the look of the app does not leave the guide describing one thing and showing another.
///
/// It does nothing unless AVASVG_SHOOT is set, because it writes into the repository and a
/// test run should not. To take the pictures:
///
///     AVASVG_SHOOT=1 dotnet test AVASvgMaker.Tests --filter FullyQualifiedName~Screenshots
///
/// Only the shots of the whole window are taken here. The rest of Images/ is close-ups of the
/// page, menus and dialogs, which are not affected by the shape of the window and are still
/// taken by hand.
/// </summary>
public class Screenshots
{
    private static readonly string Folder =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Images");

    private static bool Asked => Environment.GetEnvironmentVariable("AVASVG_SHOOT") is not null;

    private static void Save(MainWindow window, string name)
    {
        var canvas = window.FindControl<Views.DrawingCanvas>("Canvas")!;

        // A capture hands back the last frame that was drawn, and adding shapes to the document
        // does not on its own ask for another - so the page is told to repaint and the status
        // bar to say what is on it, as a pointer or a menu would have done.
        canvas.ReportStatus();
        canvas.InvalidateVisual();
        window.InvalidateVisual();

        Harness.Settle(window);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Harness.Settle(window);

        Console.WriteLine($"SHOOT {name}: {canvas.Document.Shapes.Count} shapes, " +
                          $"{canvas.Document.Selection.Count} selected, window {window.Width}x{window.Height}");

        using var file = File.Create(Path.GetFullPath(Path.Combine(Folder, name + ".png")));
        window.CaptureRenderedFrame()!.Save(file);
    }

    private static (MainWindow Window, Views.DrawingCanvas Canvas) Window(double page = 800)
    {
        var (window, canvas) = Harness.Editor(page, 600);

        window.Width = 1312;
        window.Height = 824;

        Harness.Settle(window);
        Harness.Settle(window);

        return (window, canvas);
    }

    private static DiagramShape Put(
        Views.DrawingCanvas canvas, ShapeKind kind, Rect at, string text = "")
    {
        var shape = ShapeFactory.Create(kind, at);
        shape.Text = text;
        canvas.Document.Add(shape);
        return shape;
    }

    private static void Join(Views.DrawingCanvas canvas, DiagramShape from, int fromPort,
        DiagramShape to, int toPort, ConnectorRouting routing = ConnectorRouting.Orthogonal)
    {
        var line = Harness.Join(canvas.Document, from, fromPort, to, toPort);
        line.Routing = routing;
        line.EndCap = EndCapStyle.Arrow;
    }

    /// <summary>The window, with a flowchart on it and a shape selected.</summary>
    [AvaloniaFact]
    public void Overview()
    {
        if (!Asked) return;

        var (window, canvas) = Window();

        var placed = Put(canvas, ShapeKind.RoundedRectangle, new Rect(300, 40, 180, 60), "Order placed");
        var stock = Put(canvas, ShapeKind.Diamond, new Rect(290, 170, 200, 100), "In stock?");
        var pick = Put(canvas, ShapeKind.Rectangle, new Rect(120, 330, 150, 60), "Pick and pack");
        var reorder = Put(canvas, ShapeKind.Rectangle, new Rect(510, 330, 150, 60), "Reorder");
        var dispatch = Put(canvas, ShapeKind.RoundedRectangle, new Rect(300, 470, 180, 60), "Dispatch");

        Join(canvas, placed, 2, stock, 0);
        Join(canvas, stock, 3, pick, 0);
        Join(canvas, stock, 1, reorder, 0);
        Join(canvas, pick, 2, dispatch, 3);
        Join(canvas, reorder, 2, dispatch, 1);

        canvas.Document.SetSelection([pick]);
        Save(window, "overview");
    }

    /// <summary>The window with the connector panel up, which is what a connector is formatted from.</summary>
    [AvaloniaFact]
    public void ConnectorProperties()
    {
        if (!Asked) return;

        var (window, canvas) = Window();

        var raise = Put(canvas, ShapeKind.RoundedRectangle, new Rect(120, 90, 170, 60), "Raise ticket");
        var triage = Put(canvas, ShapeKind.Diamond, new Rect(400, 210, 180, 100), "Urgent?");
        var now = Put(canvas, ShapeKind.Rectangle, new Rect(150, 400, 150, 60), "Work it now");
        var queue = Put(canvas, ShapeKind.Rectangle, new Rect(520, 400, 150, 60), "Queue it");

        Join(canvas, raise, 1, triage, 3, ConnectorRouting.Curved);
        Join(canvas, triage, 3, now, 0, ConnectorRouting.Curved);
        Join(canvas, triage, 2, queue, 0, ConnectorRouting.Curved);

        var line = canvas.Document.Shapes.OfType<ConnectorShape>().First();
        line.Stroke = Color.FromRgb(0x2D, 0x6C, 0xDF);
        line.StrokeThickness = 3;

        canvas.Document.SetSelection([line]);
        Save(window, "connector-properties");
    }

    /// <summary>A pool with two lanes, which is the other picture of the whole window.</summary>
    [AvaloniaFact]
    public void Swimlanes()
    {
        if (!Asked) return;

        var (window, canvas) = Window();

        var pool = Put(canvas, ShapeKind.Pool, new Rect(40, 70, 700, 400), "Order handling");
        var sales = Put(canvas, ShapeKind.Lane, new Rect(0, 0, 10, 10), "Sales");
        var store = Put(canvas, ShapeKind.Lane, new Rect(0, 0, 10, 10), "Warehouse");

        canvas.Document.Adopt(sales, pool as ContainerShape);
        canvas.Document.Adopt(store, pool as ContainerShape);
        canvas.Document.Refresh();

        // The gateway sits over Pack goods, so the line down between the lanes is a plain drop
        // rather than a dog-leg that arrives along the top edge of the box.
        var start = Put(canvas, ShapeKind.BpmnStartEvent, new Rect(150, 150, 50, 50));
        var take = Put(canvas, ShapeKind.BpmnUserTask, new Rect(250, 140, 130, 70), "Take order");
        var gate = Put(canvas, ShapeKind.BpmnExclusiveGateway, new Rect(440, 150, 50, 50));
        var pack = Put(canvas, ShapeKind.Rectangle, new Rect(400, 330, 130, 60), "Pack goods");
        var ship = Put(canvas, ShapeKind.Rectangle, new Rect(580, 330, 130, 60), "Ship");

        foreach (var shape in new[] { start, take, gate, pack, ship })
            canvas.Document.Adopt(shape, canvas.Document.ContainerFor(shape));

        Join(canvas, start, 1, take, 3);
        Join(canvas, take, 1, gate, 3);
        Join(canvas, gate, 2, pack, 0);
        Join(canvas, pack, 1, ship, 3);

        canvas.Document.SetSelection([]);
        Save(window, "swimlanes");
    }

    /// <summary>A shape part-way through a drag, with the guides it has caught on showing.</summary>
    [AvaloniaFact]
    public void SmartGuides()
    {
        if (!Asked) return;

        var (window, canvas) = Window();
        canvas.Grid.SnapToGrid = false;

        var anchor = Put(canvas, ShapeKind.Rectangle, new Rect(260, 100, 170, 70), "Anchor");
        var moving = Put(canvas, ShapeKind.Rectangle, new Rect(180, 260, 170, 70), "Dragging");

        canvas.Document.SetSelection([moving]);

        // Left short of where it lines up, then on to it - and caught there, undropped, because
        // the guides are only drawn while a drag is in hand.
        var from = moving.Bounds.Center;
        Harness.Press(canvas, from);
        Harness.MoveTo(canvas, new Point(from.X + 40, from.Y - 10));
        Harness.MoveTo(canvas, new Point(from.X + 80, from.Y));

        Save(window, "smart-guides");

        Harness.Release(canvas, new Point(from.X + 80, from.Y));
    }

    /// <summary>The File menu, open on the list of things a page can be exported as.</summary>
    [AvaloniaFact]
    public void ExportMenu()
    {
        if (!Asked) return;

        var (window, canvas) = Window();

        var placed = Put(canvas, ShapeKind.RoundedRectangle, new Rect(300, 60, 180, 60), "Order placed");
        var stock = Put(canvas, ShapeKind.Diamond, new Rect(290, 190, 200, 100), "In stock?");
        var pick = Put(canvas, ShapeKind.Rectangle, new Rect(140, 360, 150, 60), "Pick and pack");

        Join(canvas, placed, 2, stock, 0);
        Join(canvas, stock, 3, pick, 0);
        canvas.Document.SetSelection([]);

        var menu = window.GetVisualDescendants().OfType<Menu>().First();
        var file = menu.Items.OfType<MenuItem>().First(m => (m.Header as string)?.Contains("File") == true);

        file.Open();
        Harness.Settle(window);

        var export = file.Items.OfType<MenuItem>()
            .First(m => (m.Header as string)?.Contains("Export") == true);

        export.Open();
        Harness.Settle(window);

        Save(window, "export-menu");
    }
}
