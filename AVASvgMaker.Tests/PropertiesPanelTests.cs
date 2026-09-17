using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using AVASvgMaker.Models;
using AVASvgMaker.Views;
using Xunit;

namespace AVASvgMaker.Tests;

/// <summary>
/// The properties panel follows what is being worked on. A connector has no fill and a shape
/// has no line ends, so rather than carry every control at once - some of them in the toolbar
/// and some in the panel - the panel shows the ones that apply.
/// </summary>
public class PropertiesPanelTests
{
    private static bool Showing(MainWindow window, string name) =>
        window.FindControl<StackPanel>(name)!.IsVisible;

    private static void Click(MainWindow window, string button) =>
        window.FindControl<ToggleButton>(button)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void AShapeGetsTheFillSectionAndNotTheConnectorOne()
    {
        var (window, canvas) = Harness.Editor();
        var box = Harness.Box(canvas.Document, new Rect(60, 60, 120, 60), "A");

        canvas.Document.SetSelection([box]);
        Harness.Settle(window);

        Assert.True(Showing(window, "FillSection"));
        Assert.False(Showing(window, "ConnectorSection"));
    }

    [AvaloniaFact]
    public void AConnectorGetsTheConnectorSectionAndNotTheFill()
    {
        var (window, canvas) = Harness.Editor();
        var a = Harness.Box(canvas.Document, new Rect(60, 60, 120, 60), "A");
        var b = Harness.Box(canvas.Document, new Rect(400, 300, 120, 60), "B");
        var line = Harness.Join(canvas.Document, a, 1, b, 3);

        canvas.Document.SetSelection([line]);
        Harness.Settle(window);

        Assert.True(Showing(window, "ConnectorSection"));
        Assert.False(Showing(window, "FillSection"));
    }

    /// <summary>
    /// The settings are wanted before there is a connector to select, which is the whole point
    /// of them having been in the toolbar. Picking up the tool is what stands in for that.
    /// </summary>
    [AvaloniaFact]
    public void ThePanelOffersConnectorSettingsBeforeThereIsOneToSelect()
    {
        var (window, canvas) = Harness.Editor();

        Assert.False(Showing(window, "ConnectorSection"));

        Click(window, "ConnectorToolButton");
        Harness.Settle(window);

        Assert.True(Showing(window, "ConnectorSection"));
        Assert.False(Showing(window, "FillSection"));

        Click(window, "SelectToolButton");
        Harness.Settle(window);

        Assert.False(Showing(window, "ConnectorSection"));
        Assert.True(Showing(window, "FillSection"));
    }

    /// <summary>A selection holding both gets both: both are being formatted.</summary>
    [AvaloniaFact]
    public void ASelectionOfBothGetsBothSections()
    {
        var (window, canvas) = Harness.Editor();
        var a = Harness.Box(canvas.Document, new Rect(60, 60, 120, 60), "A");
        var b = Harness.Box(canvas.Document, new Rect(400, 300, 120, 60), "B");
        var line = Harness.Join(canvas.Document, a, 1, b, 3);

        canvas.Document.SetSelection([a, line]);
        Harness.Settle(window);

        Assert.True(Showing(window, "ConnectorSection"));
        Assert.True(Showing(window, "FillSection"));
    }

    /// <summary>Nothing selected and no connector tool: the panel is set up for the next shape.</summary>
    [AvaloniaFact]
    public void NothingSelectedGetsTheShapeSections()
    {
        var (window, _) = Harness.Editor();

        Assert.True(Showing(window, "FillSection"));
        Assert.False(Showing(window, "ConnectorSection"));
    }

    [AvaloniaFact]
    public void TheConnectorBoxesFollowTheSelectedConnector()
    {
        var (window, canvas) = Harness.Editor();
        var a = Harness.Box(canvas.Document, new Rect(60, 60, 120, 60), "A");
        var b = Harness.Box(canvas.Document, new Rect(400, 300, 120, 60), "B");

        var line = Harness.Join(canvas.Document, a, 1, b, 3);
        line.StartCap = EndCapStyle.Diamond;
        line.EndCap = EndCapStyle.CrowsFoot;
        line.Routing = ConnectorRouting.Curved;

        canvas.Document.SetSelection([line]);
        Harness.Settle(window);

        Assert.Equal("Diamond", Tag(window, "StartCapBox"));
        Assert.Equal("CrowsFoot", Tag(window, "EndCapBox"));
        Assert.Equal("Curved", Tag(window, "RoutingBox"));
    }

    /// <summary>
    /// Weight used to be two boxes saying the same thing, one in the toolbar for connectors and
    /// one in the panel for shapes. It is one box, and it still does both jobs.
    /// </summary>
    [AvaloniaFact]
    public void TheOneWeightBoxSetsTheShapeAndTheNextConnector()
    {
        var (window, canvas) = Harness.Editor();
        var a = Harness.Box(canvas.Document, new Rect(60, 60, 120, 60), "A");
        var b = Harness.Box(canvas.Document, new Rect(400, 300, 120, 60), "B");
        var line = Harness.Join(canvas.Document, a, 1, b, 3);

        canvas.Document.SetSelection([line]);
        Harness.Settle(window);

        var weight = window.FindControl<ComboBox>("LineWeightBox")!;
        weight.SelectedItem = weight.Items.OfType<ComboBoxItem>().First(i => (string?)i.Tag == "4");
        Harness.Settle(window);

        Assert.Equal(4, line.StrokeThickness);
        Assert.Equal(4, canvas.DefaultLineWidth);
    }

    /// <summary>
    /// Picking up the connector tool straight after dropping a shape leaves that shape
    /// selected, and the connector settings are still what is wanted next - they were what the
    /// toolbar carried them for. Both sections show: the shape is still there to format.
    /// </summary>
    [AvaloniaFact]
    public void TheConnectorToolShowsItsSettingsEvenWithAShapeSelected()
    {
        var (window, canvas) = Harness.Editor();
        var box = Harness.Box(canvas.Document, new Rect(60, 60, 120, 60), "A");

        canvas.Document.SetSelection([box]);
        Harness.Settle(window);

        Assert.False(Showing(window, "ConnectorSection"));

        Click(window, "ConnectorToolButton");
        Harness.Settle(window);

        Assert.True(Showing(window, "ConnectorSection"), "the connector settings should be up");
        Assert.True(Showing(window, "FillSection"), "the selected shape is still there to format");
    }

    private static (MainWindow Window, Views.DrawingCanvas Canvas, ConnectorShape One, ConnectorShape Two)
        TwoLines()
    {
        var (window, canvas) = Harness.Editor();

        var a = Harness.Box(canvas.Document, new Rect(60, 60, 110, 50), "A");
        var b = Harness.Box(canvas.Document, new Rect(400, 60, 110, 50), "B");
        var c = Harness.Box(canvas.Document, new Rect(60, 300, 110, 50), "C");
        var d = Harness.Box(canvas.Document, new Rect(400, 300, 110, 50), "D");

        var one = Harness.Join(canvas.Document, a, 1, b, 3);
        var two = Harness.Join(canvas.Document, c, 1, d, 3);

        return (window, canvas, one, two);
    }

    private static void Pick(MainWindow window, string box, string tag)
    {
        var combo = window.FindControl<ComboBox>(box)!;
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().First(i => (string?)i.Tag == tag);
        Harness.Settle(window);
    }

    /// <summary>A change to one setting reaches every connector selected, not just the last.</summary>
    [AvaloniaFact]
    public void ChangingAnEndingChangesEverySelectedConnector()
    {
        var (window, canvas, one, two) = TwoLines();

        canvas.Document.SetSelection([one, two]);
        Harness.Settle(window);

        Pick(window, "EndCapBox", "Diamond");

        Assert.Equal(EndCapStyle.Diamond, one.EndCap);
        Assert.Equal(EndCapStyle.Diamond, two.EndCap);
    }

    /// <summary>
    /// Connectors that disagree show a dash rather than the first one's value, and changing
    /// something else does not write that dash back as though it were a value. This is the one
    /// that bites: changing the route used to give every selected connector the same ends.
    /// </summary>
    [AvaloniaFact]
    public void ChangingOneSettingLeavesTheOthersAsTheyWere()
    {
        var (window, canvas, one, two) = TwoLines();

        one.StartCap = EndCapStyle.Diamond;
        two.StartCap = EndCapStyle.Dot;
        one.EndCap = EndCapStyle.Arrow;
        two.EndCap = EndCapStyle.OpenArrow;

        canvas.Document.SetSelection([one, two]);
        Harness.Settle(window);

        // They disagree, so the boxes say so instead of picking one.
        Assert.Null(window.FindControl<ComboBox>("StartCapBox")!.SelectedItem);
        Assert.Null(window.FindControl<ComboBox>("EndCapBox")!.SelectedItem);

        Pick(window, "RoutingBox", "Curved");

        Assert.Equal(ConnectorRouting.Curved, one.Routing);
        Assert.Equal(ConnectorRouting.Curved, two.Routing);

        // And the ends they each had are the ends they still have.
        Assert.Equal(EndCapStyle.Diamond, one.StartCap);
        Assert.Equal(EndCapStyle.Dot, two.StartCap);
        Assert.Equal(EndCapStyle.Arrow, one.EndCap);
        Assert.Equal(EndCapStyle.OpenArrow, two.EndCap);
    }

    /// <summary>A shape caught up in the selection is not given connector formatting.</summary>
    [AvaloniaFact]
    public void AShapeInAMixedSelectionIsLeftAlone()
    {
        var (window, canvas, one, _) = TwoLines();
        var box = canvas.Document.Shapes.First(s => s.Text == "A");

        var fill = box.Fill;
        var stroke = box.Stroke;

        canvas.Document.SetSelection([box, one]);
        Harness.Settle(window);

        Assert.True(Showing(window, "ConnectorSection"));
        Assert.True(Showing(window, "FillSection"));

        Pick(window, "EndCapBox", "Dot");

        Assert.Equal(EndCapStyle.Dot, one.EndCap);
        Assert.Equal(fill, box.Fill);
        Assert.Equal(stroke, box.Stroke);
    }

    /// <summary>The boxes follow the selection even when the last thing clicked was a shape.</summary>
    [AvaloniaFact]
    public void TheBoxesFollowAConnectorSelectedBeforeAShape()
    {
        var (window, canvas, one, _) = TwoLines();
        var box = canvas.Document.Shapes.First(s => s.Text == "A");

        one.Routing = ConnectorRouting.Curved;
        one.StartCap = EndCapStyle.Dot;

        // The connector first, the shape last - so Document.Selected is the shape.
        canvas.Document.SetSelection([one, box]);
        Harness.Settle(window);

        Assert.Equal("Curved", Tag(window, "RoutingBox"));
        Assert.Equal("Dot", Tag(window, "StartCapBox"));
    }

    private static string? Tag(MainWindow window, string name) =>
        (window.FindControl<ComboBox>(name)!.SelectedItem as ComboBoxItem)?.Tag as string;
}
