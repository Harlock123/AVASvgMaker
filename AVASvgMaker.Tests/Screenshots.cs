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
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using AVASvgMaker.Engine;
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
        Views.DrawingCanvas canvas, ShapeKind kind, Rect at, string text = "") =>
        Put(canvas.Document, kind, at, text);

    private static DiagramShape Put(
        DiagramDocument document, ShapeKind kind, Rect at, string text = "")
    {
        var shape = ShapeFactory.Create(kind, at);
        shape.Text = text;
        document.Add(shape);
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

    /// <summary>A drawing before and after it is laid out, as two pictures of the page.</summary>
    [AvaloniaFact]
    public void LayOut()
    {
        if (!Asked) return;

        DiagramDocument Tangle()
        {
            var page = Harness.Page(700, 700);

            var start = Put(page, ShapeKind.RoundedRectangle, new Rect(470, 60, 130, 50), "Ticket in");
            var triage = Put(page, ShapeKind.Rectangle, new Rect(80, 420, 130, 60), "Triage");
            var bug = Put(page, ShapeKind.Diamond, new Rect(500, 290, 140, 70), "Bug?");
            var fix = Put(page, ShapeKind.Rectangle, new Rect(50, 110, 120, 50), "Fix it");
            var doc = Put(page, ShapeKind.Rectangle, new Rect(300, 460, 130, 50), "Document");
            var test = Put(page, ShapeKind.Rectangle, new Rect(530, 175, 120, 50), "Test");
            var close = Put(page, ShapeKind.RoundedRectangle, new Rect(230, 230, 120, 50), "Close");

            foreach (var (from, to) in new[]
                     { (start, triage), (triage, bug), (bug, fix), (bug, doc),
                       (fix, test), (doc, test), (test, close) })
            {
                var line = Harness.Join(page, from, 2, to, 0);
                line.EndCap = EndCapStyle.Arrow;
            }

            return page;
        }

        void Paper(DiagramDocument page, string name)
        {
            page.RouteConnectors();

            using var file = File.Create(Path.GetFullPath(Path.Combine(Folder, name + ".png")));
            RasterExporter.Export(page, file, 2, RasterFormat.Png);
        }

        Paper(Tangle(), "lay-out-before");

        var tidy = Tangle();
        GraphLayout.Apply(
            tidy.Shapes.Where(shape => shape is not ConnectorShape).ToList(),
            tidy.Shapes.OfType<ConnectorShape>().ToList(),
            LayoutFlow.Down,
            new Rect(0, 0, tidy.PageWidth, tidy.PageHeight));

        Paper(tidy, "lay-out-after");
    }

    /// <summary>
    /// The drawing that comes back from a Mermaid flowchart. The source is not shown as a
    /// picture - it is text, and the guide prints it as text, which reads better than a
    /// photograph of it would.
    /// </summary>
    [AvaloniaFact]
    public void MermaidImport()
    {
        if (!Asked) return;

        const string code = """
            flowchart TD
                a([Idea]) --> b{Worth doing?}
                b -->|no| z[/Drop it/]
                b -- yes --> c[Write it]
                subgraph review["Review"]
                    d[Read it]
                    e{Happy?}
                end
                c --> d
                d --> e
                e -->|no| c
                e -->|yes| f[(Merge)]
                f ==> g([Shipped])
                style z fill:#f4c7c3,stroke:#c0392b
                style g fill:#b7e1cd,stroke:#1e8449
            """;

        var read = MermaidImporter.Read(code, 700, 880);
        var page = new DiagramDocument();
        page.SetPageSize(700, 880);

        foreach (var shape in read.Page.Shapes)
            page.Shapes.Add(shape);

        page.NormaliseOrder();
        page.RouteConnectors();

        using var file = File.Create(Path.GetFullPath(Path.Combine(Folder, "mermaid-import.png")));
        RasterExporter.Export(page, file, 2, RasterFormat.Png);
    }

    /// <summary>
    /// Saved shapes sitting in the toolbox. The toolbox is rendered on its own rather than
    /// cropped out of a window shot, so the picture is the panel and nothing else.
    ///
    /// The library is pointed at a temporary file for the duration: this writes shapes, and
    /// writing them into whoever is running it's own collection would be rude.
    /// </summary>
    [AvaloniaFact]
    public void CustomShapes()
    {
        if (!Asked) return;

        var was = StencilLibrary.Path;
        var scratch = Path.Combine(Path.GetTempPath(), "ava-shot-" + Guid.NewGuid().ToString("N"));

        try
        {
            StencilLibrary.Path = Path.Combine(scratch, "stencils.json");
            StencilLibrary.Reload();

            StencilLibrary.Add("Server", Fragment("Server", "eu-west-1"));
            StencilLibrary.Add("Decision pair", Fragment("Ready?", "Ship it"));

            var (window, _) = Window();
            var toolbox = window.FindControl<Views.ToolboxPanel>("Toolbox")!;

            Harness.Settle(window);

            var size = new PixelSize(
                (int)Math.Round(toolbox.Bounds.Width * 2),
                (int)Math.Round(Math.Min(toolbox.Bounds.Height, 420) * 2));

            using var bitmap = new RenderTargetBitmap(size, new Vector(192, 192));
            bitmap.Render(toolbox);

            using var file = File.Create(Path.GetFullPath(Path.Combine(Folder, "custom-shapes.png")));
            bitmap.Save(file);
        }
        finally
        {
            StencilLibrary.Path = was;
            StencilLibrary.Reload();

            if (Directory.Exists(scratch))
                Directory.Delete(scratch, recursive: true);
        }
    }

    /// <summary>A little group of shapes, as saving a selection would produce.</summary>
    private static string Fragment(string top, string bottom)
    {
        var page = Harness.Page();

        var box = Harness.Box(page, new Rect(100, 100, 120, 60), top);
        var label = Harness.Box(page, new Rect(100, 180, 120, 30), bottom);

        box.Bold = true;
        Harness.Join(page, box, 2, label, 0);
        page.Group([box, label]);

        return ShapeClipboard.Copy(page, page.Shapes.ToList())!;
    }

    /// <summary>
    /// The page setup dialog, shown with a fade set so the paper rows are on display - they
    /// are the part of it people do not know is there.
    /// </summary>
    [AvaloniaFact]
    public void PageSetup()
    {
        if (!Asked) return;

        var dialog = new Views.PageSetupDialog(
            816, 1056, 32, null, 1,
            Color.Parse("#FFFFFF"), Color.Parse("#DCE9FB"), 90);

        dialog.Show();

        Harness.Settle(dialog);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Harness.Settle(dialog);

        using (var file = File.Create(Path.GetFullPath(Path.Combine(Folder, "page-setup.png"))))
            dialog.CaptureRenderedFrame()!.Save(file);

        dialog.Close();
    }

    /// <summary>The electrical symbols, laid out as a sheet for the documentation.</summary>
    [AvaloniaFact]
    public void Electrical()
    {
        if (!Asked) return;

        var set = StencilCatalogue.InCategory(StencilCategory.Electrical).ToList();

        const int columns = 5;
        const double wide = 150, tall = 116;

        var rows = (set.Count + columns - 1) / columns;
        var page = new DiagramDocument();
        page.SetPageSize(columns * wide, rows * tall);

        for (var i = 0; i < set.Count; i++)
        {
            var x = i % columns * wide;
            var y = i / columns * tall;

            var symbol = ShapeFactory.Create(set[i].Kind, new Rect(x + 30, y + 16, 90, 60));
            set[i].ApplyDefaults(symbol);
            page.Shapes.Add(symbol);

            var name = ShapeFactory.Create(ShapeKind.TextBox, new Rect(x + 6, y + 80, wide - 12, 22));
            name.Text = set[i].Name;
            name.FontSize = 11;
            page.Shapes.Add(name);
        }

        page.RouteConnectors();

        using var file = File.Create(Path.GetFullPath(Path.Combine(Folder, "electrical.png")));
        RasterExporter.Export(page, file, 2, RasterFormat.Png);
    }
}
