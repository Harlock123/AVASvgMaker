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

    private static string? Tag(MainWindow window, string name) =>
        (window.FindControl<ComboBox>(name)!.SelectedItem as ComboBoxItem)?.Tag as string;
}
