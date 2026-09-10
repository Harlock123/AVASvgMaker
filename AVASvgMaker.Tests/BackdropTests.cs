using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using Xunit;

namespace AVASvgMaker.Tests;

/// <summary>
/// A shape the size of the page is the background, whether it was drawn or imported. Clicking
/// one used to raise it over everything else, which buried a whole drawing behind an opaque
/// white sheet and looked for all the world as though the shapes had been deleted.
/// </summary>
public class BackdropTests
{
    private const string WithBackground = """
        <svg xmlns="http://www.w3.org/2000/svg" width="400" height="300">
          <rect x="0" y="0" width="400" height="300" fill="#FFFFFF"/>
          <rect x="40" y="40" width="90" height="50" fill="#d6ecfb" stroke="#2a6fd6"/>
          <circle cx="220" cy="70" r="30" fill="#fff6bf" stroke="#c9a227"/>
          <path d="M 40,160 L 130,160 L 85,220 Z" fill="#d6ecfb" stroke="#2a6fd6"/>
        </svg>
        """;

    private static SvgImporter.Result Read(string svg) =>
        SvgImporter.Read(new MemoryStream(Encoding.UTF8.GetBytes(svg)));

    [AvaloniaFact]
    public void APageSizedBackgroundIsTheePageAndNotAShapeOnIt()
    {
        var read = Read(WithBackground);

        Assert.Equal(3, read.Shapes);
        Assert.DoesNotContain(read.Document.Shapes, shape => shape.Bounds.Width >= 400);
        Assert.Contains("page background", read.Summary);
    }

    [AvaloniaFact]
    public void ADrawingThatIsOneBigRectangleKeepsIt()
    {
        // Only a leading background is dropped, and only when there is something behind it to
        // be the drawing. A file whose whole content is one rectangle still imports it.
        var read = Read("""
            <svg xmlns="http://www.w3.org/2000/svg" width="400" height="300">
              <rect x="0" y="0" width="400" height="300" fill="#FFFFFF"/>
            </svg>
            """);

        Assert.Equal(1, read.Shapes);
    }

    [AvaloniaFact]
    public void ARectangleWithAnOutlineIsAShapeAndNotABackground()
    {
        var read = Read("""
            <svg xmlns="http://www.w3.org/2000/svg" width="400" height="300">
              <rect x="0" y="0" width="400" height="300" fill="#FFFFFF" stroke="#000"/>
              <circle cx="220" cy="70" r="30" fill="#fff6bf"/>
            </svg>
            """);

        Assert.Equal(2, read.Shapes);
    }

    [AvaloniaFact]
    public void OurOwnSvgComesBackWithoutItsPage()
    {
        var original = Harness.Page(500, 400);
        Harness.Box(original, new Rect(40, 40, 120, 60), "One");
        Harness.Box(original, new Rect(260, 40, 100, 100), "Two", ShapeKind.Ellipse);

        var read = Read(SvgExporter.Export(original));

        // Two shapes and two labels, and no sheet of white over the top of them.
        Assert.DoesNotContain(read.Document.Shapes, shape =>
            shape.Bounds.Width >= 500 && shape.Bounds.Height >= 400);
    }

    [AvaloniaFact]
    public void ClickingABackdropDoesNotBuryEverythingBehindIt()
    {
        var (window, canvas) = Harness.Editor(400, 300);
        var document = canvas.Document;

        // Drawn by hand rather than imported: the rule is about the shape, not where it came from.
        var background = Harness.Box(document, new Rect(0, 0, 400, 300));
        background.Fill = Colors.White;

        var one = Harness.Box(document, new Rect(40, 40, 90, 50), "One");
        var two = Harness.Box(document, new Rect(190, 40, 60, 60), "Two", ShapeKind.Ellipse);

        document.ClearSelection();
        Harness.Settle(window);

        Assert.Same(background, document.Shapes[0]);

        // Pick a shape, then click a bare part of the page - which lands on the backdrop.
        Harness.Click(canvas, one.Bounds.Center);
        Harness.Click(canvas, new Point(330, 260));

        Assert.True(document.IsSelected(background), "the click selected what was under it");

        // Selected, yes - but still behind everything, so nothing has been hidden.
        Assert.Same(background, document.Shapes[0]);
        Assert.Equal(3, document.Shapes.Count);
        Assert.Contains(one, document.Shapes);
        Assert.Contains(two, document.Shapes);
    }

    [AvaloniaFact]
    public void AnOrdinaryShapeIsStillRaisedByAClick()
    {
        var (window, canvas) = Harness.Editor(400, 300);
        var document = canvas.Document;

        var under = Harness.Box(document, new Rect(40, 40, 120, 80), "under");
        Harness.Box(document, new Rect(60, 60, 120, 80), "over");

        document.ClearSelection();
        Harness.Settle(window);

        Assert.Same(under, document.Shapes[0]);

        // A click on a part of it that is not covered brings it forward, as it always did.
        Harness.Click(canvas, new Point(50, 50));

        Assert.Same(under, document.Shapes[^1]);
    }
}
