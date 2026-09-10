using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using AVASvgMaker.Views;
using Xunit;

namespace AVASvgMaker.Tests;

/// <summary>
/// Shapes of your own. Every test points the library at a scratch file first, so the suite
/// never touches whatever is actually saved on the machine running it.
/// </summary>
public class CustomStencilTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "avasvg-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _was = StencilLibrary.Path;

    public CustomStencilTests()
    {
        StencilLibrary.Path = Path.Combine(_folder, "stencils.json");
        StencilLibrary.Reload();
    }

    public void Dispose()
    {
        StencilLibrary.Path = _was;
        StencilLibrary.Reload();

        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    /// <summary>A pair of glued, grouped, formatted shapes - the sort of thing worth saving.</summary>
    private static string Fragment(out DiagramDocument document)
    {
        document = Harness.Page();

        var box = Harness.Box(document, new Rect(100, 100, 120, 60), "Server");
        var label = Harness.Box(document, new Rect(100, 180, 120, 30), "eu-west-1");

        box.Bold = true;
        label.Italic = true;

        Harness.Join(document, box, 2, label, 0);
        document.Group([box, label]);

        return ShapeClipboard.Copy(document, document.Shapes.ToList())!;
    }

    [AvaloniaFact]
    public void ASavedShapeSurvivesBeingWrittenAndReadBack()
    {
        var fragment = Fragment(out _);

        var saved = StencilLibrary.Add("Server", fragment);

        Assert.NotNull(saved);
        Assert.Equal("Server", saved!.Name);
        Assert.True(File.Exists(StencilLibrary.Path));

        // Read afresh from the file rather than from what is held in memory.
        StencilLibrary.Reload();

        var read = Assert.Single(StencilLibrary.All);
        Assert.Equal("Server", read.Name);
        Assert.Equal(saved.Id, read.Id);

        var shapes = DiagramFile.FromJson(read.Fragment).Shapes;
        Assert.Equal(3, shapes.Count);
        Assert.True(shapes.First(shape => shape.Text == "Server").Bold);
        Assert.NotEqual(0, shapes[0].GroupId);
    }

    [AvaloniaFact]
    public void NothingUselessIsSaved()
    {
        Assert.Null(StencilLibrary.Add("   ", "{}"));
        Assert.Null(StencilLibrary.Add("Empty", "   "));
        Assert.Empty(StencilLibrary.All);
    }

    [AvaloniaFact]
    public void TwoShapesCannotShareAName()
    {
        var fragment = Fragment(out _);

        Assert.Equal("Server", StencilLibrary.Add("Server", fragment)!.Name);
        Assert.Equal("Server 2", StencilLibrary.Add("Server", fragment)!.Name);
        // The clash is judged without regard to case; the name itself is kept as it was typed.
        Assert.Equal("server 3", StencilLibrary.Add("server", fragment)!.Name);
    }

    [AvaloniaFact]
    public void ShapesAreRenamedAndRemoved()
    {
        var fragment = Fragment(out _);
        var first = StencilLibrary.Add("One", fragment)!;
        var second = StencilLibrary.Add("Two", fragment)!;

        Assert.True(StencilLibrary.Rename(first.Id, "Renamed"));
        Assert.False(StencilLibrary.Rename(first.Id, "Renamed"));
        Assert.False(StencilLibrary.Rename("nonsense", "Renamed"));

        StencilLibrary.Reload();
        Assert.Equal("Renamed", StencilLibrary.Find(first.Id)!.Name);

        Assert.True(StencilLibrary.Remove(second.Id));
        Assert.False(StencilLibrary.Remove(second.Id));

        StencilLibrary.Reload();
        Assert.Single(StencilLibrary.All);
    }

    [AvaloniaFact]
    public void ALibraryThatWillNotParseIsIgnoredRatherThanFatal()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(StencilLibrary.Path, "this is not json at all");

        StencilLibrary.Reload();

        Assert.Empty(StencilLibrary.All);
    }

    [AvaloniaFact]
    public void PlacingASavedShapePutsItsMiddleWhereItWasAimed()
    {
        var fragment = Fragment(out _);
        var extent = ShapeClipboard.Extent(fragment);

        Assert.NotNull(extent);

        var page = Harness.Page(800, 600);
        var placed = ShapeClipboard.Place(page, fragment, new Point(400, 300));

        Assert.Equal(3, placed.Count);

        var landed = ShapeClipboard.Union(placed);
        Assert.Equal(400, landed.Center.X, 1);
        Assert.Equal(300, landed.Center.Y, 1);
        Assert.Equal(extent!.Value.Width, landed.Width, 1);
    }

    [AvaloniaFact]
    public void APlacedShapeIsACopyWithItsOwnGlueAndGroup()
    {
        var fragment = Fragment(out var original);
        var originalGroup = original.Shapes[0].GroupId;

        var page = Harness.Page(800, 600);
        Harness.Box(page, new Rect(10, 10, 20, 20));
        page.Group([page.Shapes[0], Harness.Box(page, new Rect(40, 10, 20, 20))]);

        var placed = ShapeClipboard.Place(page, fragment, new Point(400, 300));

        // A saved shape comes back exactly as it was saved, grouping included: the two boxes
        // were grouped and the connector was not, so that is what is placed.
        var boxes = placed.Where(shape => shape is not ConnectorShape).ToList();
        var group = boxes[0].GroupId;

        Assert.NotEqual(0, group);
        Assert.NotEqual(originalGroup, group);
        Assert.All(boxes, shape => Assert.Equal(group, shape.GroupId));
        Assert.Equal(0, placed.OfType<ConnectorShape>().Single().GroupId);

        // Glue points at the copies, not at the shapes it was saved from.
        var connector = placed.OfType<ConnectorShape>().Single();
        Assert.NotNull(connector.StartShape);
        Assert.Contains(connector.StartShape!, placed);
        Assert.DoesNotContain(connector.StartShape!, original.Shapes);
    }

    [AvaloniaFact]
    public void PlacingTwiceGivesTwoSeparateCopies()
    {
        var fragment = Fragment(out _);
        var page = Harness.Page(900, 700);

        var first = ShapeClipboard.Place(page, fragment, new Point(300, 200));
        var second = ShapeClipboard.Place(page, fragment, new Point(600, 400));

        int Group(System.Collections.Generic.IReadOnlyList<DiagramShape> placed) =>
            placed.First(shape => shape is not ConnectorShape).GroupId;

        Assert.NotEqual(Group(first), Group(second));
        Assert.Equal(6, page.Shapes.Count);
    }

    [AvaloniaFact]
    public void TheToolboxShowsSavedShapesAndArmsThem()
    {
        var fragment = Fragment(out _);
        var (window, canvas) = Harness.Editor(800, 600);

        var toolbox = window.GetVisualDescendants().OfType<ToolboxPanel>().Single();

        // Nothing saved: no category in the way.
        var mine = toolbox.GetLogicalDescendants().OfType<Expander>()
            .First(group => (group.Header as string) == "My shapes");

        Assert.False(mine.IsVisible);

        var saved = StencilLibrary.Add("Server", fragment)!;
        Harness.Settle(window);

        Assert.True(mine.IsVisible);

        var item = ((StackPanel)mine.Content!).Children.OfType<Border>().Single();
        Assert.Equal(saved.Id, (item.Tag as CustomStencil)?.Id);

        // Arming it tells the canvas. The press itself is not simulated here: the handler
        // goes on to start a drag, and DragDrop.DoDragDrop never returns without a windowing
        // system to hand the drag to, which hangs the run rather than failing it.
        toolbox.ArmStencil(saved.Id);

        Assert.Equal(saved.Id, toolbox.ArmedStencil);
        Assert.Equal(saved.Id, canvas.ArmedStencil);

        // Arming a catalogue shape puts your own one down again.
        toolbox.Arm(ShapeKind.Ellipse);
        Assert.Null(toolbox.ArmedStencil);
    }

    [AvaloniaFact]
    public void ClickingThePageWithOneArmedPutsItDown()
    {
        var fragment = Fragment(out _);
        var saved = StencilLibrary.Add("Server", fragment)!;

        var (_, canvas) = Harness.Editor(800, 600);
        canvas.ArmedStencil = saved.Id;

        Harness.Click(canvas, new Point(400, 300));

        Assert.Equal(3, canvas.Document.Shapes.Count);
        Assert.Null(canvas.ArmedStencil);

        var landed = ShapeClipboard.Union(canvas.Document.Shapes.ToList());
        Assert.Equal(400, landed.Center.X, 1);
    }

    [AvaloniaFact]
    public void ADeletedShapeIsUnarmedRatherThanLeftPointingAtNothing()
    {
        var fragment = Fragment(out _);
        var saved = StencilLibrary.Add("Server", fragment)!;

        var (window, _) = Harness.Editor(800, 600);
        var toolbox = window.GetVisualDescendants().OfType<ToolboxPanel>().Single();

        toolbox.ArmStencil(saved.Id);
        Assert.Equal(saved.Id, toolbox.ArmedStencil);

        StencilLibrary.Remove(saved.Id);
        Harness.Settle(window);

        Assert.Null(toolbox.ArmedStencil);
    }
}
