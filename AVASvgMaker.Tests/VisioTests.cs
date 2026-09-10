using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
/// Reading and writing Visio drawings.
///
/// The reading tests build their own .vsdx rather than carrying one, so what each is about is
/// on the page next to the assertion: a file that says only "this shape is a stamp of that
/// master, moved" is the whole point of the inheritance the importer does, and it is worth
/// being able to see that file.
/// </summary>
public class VisioTests
{
    private const string Main = "http://schemas.microsoft.com/office/visio/2012/main";

    #region Building a drawing to read

    /// <summary>
    /// A .vsdx holding one page and whatever shapes and masters are given, wired up the way
    /// Visio wires one: a package relationship to the document, the document to its pages, and
    /// each page to the part that holds its contents.
    /// </summary>
    private static MemoryStream Drawing(
        string shapes,
        string masters = "",
        string connects = "",
        double width = 8,
        double height = 6,
        bool background = false)
    {
        var stream = new MemoryStream();

        using (var package = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Put(string path, string xml)
            {
                using var writer = new StreamWriter(package.CreateEntry(path).Open());
                writer.Write($"<?xml version='1.0' encoding='utf-8'?>{xml}");
            }

            const string Package = "http://schemas.openxmlformats.org/package/2006/relationships";
            const string Visio = "http://schemas.microsoft.com/visio/2010/relationships";
            const string Office = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

            Put("_rels/.rels",
                $"<Relationships xmlns='{Package}'>" +
                $"<Relationship Id='rId1' Type='{Visio}/document' Target='visio/document.xml'/>" +
                "</Relationships>");

            Put("visio/document.xml", $"<VisioDocument xmlns='{Main}'/>");

            var paper = $"<PageSheet><Cell N='PageWidth' V='{width}'/>" +
                        $"<Cell N='PageHeight' V='{height}'/></PageSheet>";

            // A background page is scenery shown behind another, and is written the same way.
            var scenery = background
                ? $"<Page ID='1' NameU='Backdrop' Background='1'>{paper}<Rel r:id='rId2'/></Page>"
                : string.Empty;

            Put("visio/pages/_rels/pages.xml.rels",
                $"<Relationships xmlns='{Package}'>" +
                $"<Relationship Id='rId1' Type='{Visio}/page' Target='page1.xml'/>" +
                (background ? $"<Relationship Id='rId2' Type='{Visio}/page' Target='page2.xml'/>" : string.Empty) +
                "</Relationships>");

            Put("visio/pages/pages.xml",
                $"<Pages xmlns='{Main}' xmlns:r='{Office}'>" +
                $"<Page ID='0' NameU='Page-1'>{paper}<Rel r:id='rId1'/></Page>" +
                scenery + "</Pages>");

            if (background)
                Put("visio/pages/page2.xml",
                    $"<PageContents xmlns='{Main}' xmlns:r='{Office}'><Shapes>{Square("9")}</Shapes></PageContents>");

            Put("visio/pages/page1.xml",
                $"<PageContents xmlns='{Main}' xmlns:r='{Office}'>" +
                $"<Shapes>{shapes}</Shapes>" +
                (connects.Length > 0 ? $"<Connects>{connects}</Connects>" : string.Empty) +
                "</PageContents>");

            if (masters.Length > 0)
            {
                Put("visio/masters/_rels/masters.xml.rels",
                    $"<Relationships xmlns='{Package}'>" +
                    $"<Relationship Id='rId1' Type='{Visio}/master' Target='master1.xml'/>" +
                    "</Relationships>");

                Put("visio/masters/masters.xml",
                    $"<Masters xmlns='{Main}' xmlns:r='{Office}'>" +
                    "<Master ID='7' NameU='Thing'><Rel r:id='rId1'/></Master></Masters>");

                Put("visio/masters/master1.xml",
                    $"<MasterContents xmlns='{Main}'><Shapes>{masters}</Shapes></MasterContents>");
            }
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>A plain square shape, an inch on a side, with whatever extra is handed in.</summary>
    private static string Square(string id = "1", string extra = "", string geometry = Box) =>
        $"<Shape ID='{id}' Type='Shape'>" +
        "<Cell N='PinX' V='2'/><Cell N='PinY' V='4'/>" +
        "<Cell N='Width' V='1'/><Cell N='Height' V='1'/>" +
        "<Cell N='LocPinX' V='0.5'/><Cell N='LocPinY' V='0.5'/>" +
        extra + geometry + "</Shape>";

    private const string Box =
        "<Section N='Geometry' IX='0'>" +
        "<Row T='MoveTo' IX='1'><Cell N='X' V='0'/><Cell N='Y' V='0'/></Row>" +
        "<Row T='LineTo' IX='2'><Cell N='X' V='1'/><Cell N='Y' V='0'/></Row>" +
        "<Row T='LineTo' IX='3'><Cell N='X' V='1'/><Cell N='Y' V='1'/></Row>" +
        "<Row T='LineTo' IX='4'><Cell N='X' V='0'/><Cell N='Y' V='1'/></Row>" +
        "<Row T='LineTo' IX='5'><Cell N='X' V='0'/><Cell N='Y' V='0'/></Row>" +
        "</Section>";

    #endregion

    #region Reading

    [AvaloniaFact]
    public void ThePageComesOverAtNinetySixToTheInch()
    {
        var read = VisioImporter.Read(Drawing(Square()));
        var page = Assert.Single(read.Document.Pages);

        Assert.Equal(768, page.Width, 1);
        Assert.Equal(576, page.Height, 1);
    }

    [AvaloniaFact]
    public void VisiosYRunsUpThePageAndOursRunsDown()
    {
        // The square is pinned at 2 across and 4 up on a page 6 inches tall, so its top edge
        // is 1.5 inches down from the top: 144 pixels.
        var read = VisioImporter.Read(Drawing(Square()));
        var shape = Assert.Single(read.Document.Pages[0].Shapes);

        Assert.Equal(144, shape.Bounds.X, 1);
        Assert.Equal(144, shape.Bounds.Y, 1);
        Assert.Equal(96, shape.Bounds.Width, 1);
        Assert.Equal(96, shape.Bounds.Height, 1);
    }

    [AvaloniaFact]
    public void AShapeWithNoGeometryOfItsOwnDrawsItsMasters()
    {
        // The whole point of a Visio file: the shape says only where it is and which master
        // it stamps. Without the master resolved there is nothing to draw at all.
        var stamp = "<Shape ID='1' Type='Shape' Master='7'>" +
                    "<Cell N='PinX' V='2'/><Cell N='PinY' V='4'/>" +
                    "<Cell N='Width' V='1'/><Cell N='Height' V='1'/></Shape>";

        var read = VisioImporter.Read(Drawing(stamp, masters: Square("5")));
        var shape = Assert.Single(read.Document.Pages[0].Shapes);

        Assert.Equal(1, read.Shapes);
        Assert.Equal(144, shape.Bounds.X, 1);
    }

    [AvaloniaFact]
    public void AShapeRestatingOneNumberKeepsTheRestOfWhatItInherits()
    {
        // The instance moves the third corner in to a third of the way across and says nothing
        // else. Every other row, and the y of that row, still come from the master.
        var stamp = "<Shape ID='1' Type='Shape' Master='7'>" +
                    "<Cell N='PinX' V='2'/><Cell N='PinY' V='4'/>" +
                    "<Cell N='Width' V='1'/><Cell N='Height' V='1'/>" +
                    "<Section N='Geometry' IX='0'>" +
                    "<Row T='LineTo' IX='3'><Cell N='X' V='0.3333'/></Row>" +
                    "</Section></Shape>";

        var read = VisioImporter.Read(Drawing(stamp, masters: Square("5")));
        var shape = Assert.IsType<PathShape>(read.Document.Pages[0].Shapes[0]);

        // Five corners still, and the moved one where it was put.
        Assert.Equal(5, shape.Outline.Count(character => character is 'M' or 'L'));
        Assert.Contains("0.3333,0", shape.Outline);
        Assert.Contains("1,1", shape.Outline);
    }

    [AvaloniaFact]
    public void AChildOfAStampAnswersToOneShapeInsideTheMaster()
    {
        // MasterShape is an ID within the master its parent stamped, not a master of its own.
        // Read as a master ID it would find the wrong shape, or none.
        var stamp = "<Shape ID='1' Type='Group' Master='7'>" +
                    "<Cell N='PinX' V='2'/><Cell N='PinY' V='4'/>" +
                    "<Cell N='Width' V='1'/><Cell N='Height' V='1'/>" +
                    "<Cell N='LocPinX' V='0.5'/><Cell N='LocPinY' V='0.5'/>" +
                    "<Shapes><Shape ID='2' Type='Shape' MasterShape='6'>" +
                    "<Cell N='PinX' V='0.5'/><Cell N='PinY' V='0.5'/>" +
                    "<Cell N='Width' V='1'/><Cell N='Height' V='1'/>" +
                    "<Cell N='LocPinX' V='0.5'/><Cell N='LocPinY' V='0.5'/>" +
                    "</Shape></Shapes></Shape>";

        var group = "<Shape ID='5' Type='Group'><Shapes>" + Square("6") + "</Shapes></Shape>";

        var read = VisioImporter.Read(Drawing(stamp, masters: group));

        Assert.Equal(1, read.Shapes);
        Assert.Empty(read.Skipped);
    }

    [AvaloniaFact]
    public void AnEllipseRowIsAWholeEllipse()
    {
        var round =
            "<Section N='Geometry' IX='0'><Row T='Ellipse' IX='1'>" +
            "<Cell N='X' V='0.5'/><Cell N='Y' V='0.5'/>" +
            "<Cell N='A' V='1'/><Cell N='B' V='0.5'/>" +
            "<Cell N='C' V='0.5'/><Cell N='D' V='1'/></Row></Section>";

        var read = VisioImporter.Read(Drawing(Square(geometry: round)));
        var shape = Assert.IsType<PathShape>(read.Document.Pages[0].Shapes[0]);

        // Four curves and nothing straight: a circle, not a diamond.
        Assert.Equal(4, shape.Outline.Count(character => character == 'C'));
        Assert.DoesNotContain('L', shape.Outline);
    }

    [AvaloniaFact]
    public void AnArcBowsAwayFromItsChordNotIntoIt()
    {
        // A quarter turn across the top right corner. Bowed the wrong way it cuts a notch out
        // of the shape instead of rounding it, which is what a sign error looks like.
        var corner =
            "<Section N='Geometry' IX='0'>" +
            "<Row T='MoveTo' IX='1'><Cell N='X' V='0'/><Cell N='Y' V='0'/></Row>" +
            "<Row T='LineTo' IX='2'><Cell N='X' V='0.5'/><Cell N='Y' V='0'/></Row>" +
            "<Row T='ArcTo' IX='3'><Cell N='X' V='1'/><Cell N='Y' V='0.5'/>" +
            "<Cell N='A' V='0.1464'/></Row>" +
            "<Row T='LineTo' IX='4'><Cell N='X' V='0'/><Cell N='Y' V='0'/></Row>" +
            "</Section>";

        var read = VisioImporter.Read(Drawing(Square(geometry: corner)));
        var shape = Assert.IsType<PathShape>(read.Document.Pages[0].Shapes[0]);

        // The arc runs across the shape's bottom right corner in our coordinates, so the
        // widest point of the curve has to sit beyond the straight line between its ends.
        var far = Corners(shape.Outline).Max(point => point.X + point.Y);

        Assert.True(far > 1.4, $"the arc bows inward: furthest corner reaches {far:0.###}");
    }

    [AvaloniaFact]
    public void ARunVisioWillNotFillIsKeptOffTheBody()
    {
        // A line ruled across the shape's own face. Folded into the body it would be filled,
        // and would cut the fill in half.
        var ruled = Box +
                    "<Section N='Geometry' IX='1'><Cell N='NoFill' V='1'/>" +
                    "<Row T='MoveTo' IX='1'><Cell N='X' V='0'/><Cell N='Y' V='0.5'/></Row>" +
                    "<Row T='LineTo' IX='2'><Cell N='X' V='1'/><Cell N='Y' V='0.5'/></Row>" +
                    "</Section>";

        var read = VisioImporter.Read(Drawing(Square(geometry: ruled)));
        var shape = Assert.IsType<PathShape>(read.Document.Pages[0].Shapes[0]);

        Assert.NotNull(shape.Detail);
        Assert.Equal(1, shape.Outline.Count(character => character == 'M'));
        Assert.Equal(1, shape.Detail!.Count(character => character == 'M'));
    }

    [AvaloniaFact]
    public void AHiddenSectionIsNotDrawn()
    {
        var guide = Box +
                    "<Section N='Geometry' IX='1'><Cell N='NoShow' V='1'/>" +
                    "<Row T='MoveTo' IX='1'><Cell N='X' V='0'/><Cell N='Y' V='0.5'/></Row>" +
                    "<Row T='LineTo' IX='2'><Cell N='X' V='1'/><Cell N='Y' V='0.5'/></Row>" +
                    "</Section>";

        var read = VisioImporter.Read(Drawing(Square(geometry: guide)));
        var shape = Assert.IsType<PathShape>(read.Document.Pages[0].Shapes[0]);

        Assert.Null(shape.Detail);
        Assert.Equal(1, shape.Outline.Count(character => character == 'M'));
    }

    [AvaloniaFact]
    public void AFontIsReadFromTheCharacterSectionRatherThanACellOfItsOwn()
    {
        var lettering =
            "<Section N='Character'><Row IX='0'>" +
            "<Cell N='Size' V='0.25'/><Cell N='Style' V='3'/>" +
            "<Cell N='Color' V='#ff0000'/></Row></Section>" +
            "<Section N='Paragraph'><Row IX='0'><Cell N='HorzAlign' V='0'/></Row></Section>";

        var read = VisioImporter.Read(Drawing(Square(extra: lettering)));
        var shape = read.Document.Pages[0].Shapes[0];

        Assert.Equal(24, shape.FontSize, 1);
        Assert.True(shape.Bold);
        Assert.True(shape.Italic);
        Assert.Equal(Colors.Red, shape.TextColor);
        Assert.Equal(TextAlign.Left, shape.TextAlign);
    }

    [AvaloniaFact]
    public void AConnectorIsGluedToWhateverTheConnectsSayItIs()
    {
        var line = "<Shape ID='3' Type='Shape'>" +
                   "<Cell N='PinX' V='3'/><Cell N='PinY' V='4'/>" +
                   "<Cell N='Width' V='2'/><Cell N='Height' V='0'/>" +
                   "<Cell N='BeginX' V='2.5'/><Cell N='BeginY' V='4'/>" +
                   "<Cell N='EndX' V='4.5'/><Cell N='EndY' V='4'/></Shape>";

        var far = Square("2").Replace("<Cell N='PinX' V='2'/>", "<Cell N='PinX' V='5'/>");

        var read = VisioImporter.Read(Drawing(
            Square() + far + line,
            connects:
            "<Connect FromSheet='3' FromCell='BeginX' FromPart='9' ToSheet='1' ToCell='PinX' ToPart='3'/>" +
            "<Connect FromSheet='3' FromCell='EndX' FromPart='12' ToSheet='2' ToCell='Connections.X2' ToPart='101'/>"));

        var shapes = read.Document.Pages[0].Shapes;
        var connector = Assert.Single(shapes.OfType<ConnectorShape>());

        Assert.Same(shapes[0], connector.StartShape);
        Assert.Same(shapes[1], connector.EndShape);

        // Part 3 is the shape itself, and leaves the line free to pick its own way out; 100
        // and up name a point, and hold it there.
        Assert.Equal(-1, connector.StartPort);
        Assert.Equal(1, connector.EndPort);
    }

    [AvaloniaFact]
    public void ABackgroundPageIsSceneryForAnotherAndIsLeftOut()
    {
        var read = VisioImporter.Read(Drawing(Square(), background: true));

        Assert.Single(read.Document.Pages);
        Assert.Equal("Page-1", read.Document.Pages[0].Name);
        Assert.Contains("background page", read.Summary);
    }

    [AvaloniaFact]
    public void SomethingThatIsNotADrawingSaysSo()
    {
        var rubbish = new MemoryStream(Encoding.UTF8.GetBytes("this is not a zip"));

        Assert.ThrowsAny<Exception>(() => VisioImporter.Read(rubbish));
    }

    #endregion

    #region Writing

    private static DiagramDocument RoundTrip(DiagramDocument document)
    {
        using var stream = new MemoryStream();
        VisioExporter.Write(document, stream, "Test");

        stream.Position = 0;
        return VisioImporter.Read(stream).Document;
    }

    private static DiagramDocument OnePage(params DiagramShape[] shapes)
    {
        var page = new DiagramPage("Sheet") { Width = 800, Height = 600 };

        foreach (var shape in shapes)
            page.Shapes.Add(shape);

        var document = new DiagramDocument();
        document.SetPages([page]);

        return document;
    }

    [AvaloniaFact]
    public void ThePackageHoldsThePartsAVisioFileIsMadeOf()
    {
        using var stream = new MemoryStream();
        VisioExporter.Write(OnePage(ShapeFactory.Create(ShapeKind.Rectangle, new Rect(10, 10, 100, 60))), stream);

        stream.Position = 0;
        using var package = new ZipArchive(stream, ZipArchiveMode.Read);

        var entries = package.Entries.Select(entry => entry.FullName).ToList();

        Assert.Contains("[Content_Types].xml", entries);
        Assert.Contains("_rels/.rels", entries);
        Assert.Contains("visio/document.xml", entries);
        Assert.Contains("visio/_rels/document.xml.rels", entries);
        Assert.Contains("visio/pages/pages.xml", entries);
        Assert.Contains("visio/pages/_rels/pages.xml.rels", entries);
        Assert.Contains("visio/pages/page1.xml", entries);
    }

    [AvaloniaFact]
    public void EveryPageIsDeclaredAndPointedAt()
    {
        var document = new DiagramDocument();

        document.SetPages([
            new DiagramPage("One") { Width = 800, Height = 600 },
            new DiagramPage("Two") { Width = 400, Height = 300 }
        ]);

        using var stream = new MemoryStream();
        VisioExporter.Write(document, stream);

        stream.Position = 0;
        using var package = new ZipArchive(stream, ZipArchiveMode.Read);

        string Read(string path)
        {
            using var reader = new StreamReader(package.GetEntry(path)!.Open());
            return reader.ReadToEnd();
        }

        Assert.Contains("/visio/pages/page2.xml", Read("[Content_Types].xml"));
        Assert.Contains("page2.xml", Read("visio/pages/_rels/pages.xml.rels"));
        Assert.Contains("rId2", Read("visio/pages/pages.xml"));
        Assert.NotNull(package.GetEntry("visio/pages/page2.xml"));
    }

    [AvaloniaFact]
    public void APageKeepsItsNameAndItsPaper()
    {
        var document = new DiagramDocument();
        document.SetPages([new DiagramPage("Elevation") { Width = 1200, Height = 900 }]);

        var page = Assert.Single(RoundTrip(document).Pages);

        Assert.Equal("Elevation", page.Name);
        Assert.Equal(1200, page.Width, 1);
        Assert.Equal(900, page.Height, 1);
    }

    [AvaloniaFact]
    public void AShapeComesBackWhereItWentAndTheSizeItWas()
    {
        var box = ShapeFactory.Create(ShapeKind.Rectangle, new Rect(120, 80, 200, 140));
        var back = RoundTrip(OnePage(box)).Pages[0].Shapes[0];

        Assert.Equal(120, back.Bounds.X, 1);
        Assert.Equal(80, back.Bounds.Y, 1);
        Assert.Equal(200, back.Bounds.Width, 1);
        Assert.Equal(140, back.Bounds.Height, 1);
    }

    [AvaloniaTheory]
    [InlineData(ShapeKind.Rectangle)]
    [InlineData(ShapeKind.RoundedRectangle)]
    [InlineData(ShapeKind.Ellipse)]
    [InlineData(ShapeKind.Diamond)]
    [InlineData(ShapeKind.Triangle)]
    [InlineData(ShapeKind.Hexagon)]
    [InlineData(ShapeKind.Parallelogram)]
    [InlineData(ShapeKind.Cylinder)]
    public void EveryFamilyOfShapeSurvivesTheTrip(ShapeKind kind)
    {
        var shape = ShapeFactory.Create(kind, new Rect(60, 40, 160, 120));
        shape.Fill = Colors.Gold;
        shape.Stroke = Colors.DarkSlateBlue;

        var back = Assert.Single(RoundTrip(OnePage(shape)).Pages[0].Shapes);

        Assert.Equal(60, back.Bounds.X, 1);
        Assert.Equal(160, back.Bounds.Width, 1);
        Assert.Equal(Colors.Gold, back.Fill);
        Assert.Equal(Colors.DarkSlateBlue, back.Stroke);

        // The outline arrives whole rather than as the square that stands in for one that
        // would not parse.
        if (kind != ShapeKind.Rectangle)
            Assert.NotEqual(PathShape.Fallback, back.UnitOutline);
    }

    [AvaloniaFact]
    public void ACylindersLipIsStillDrawnAndStillNotFilled()
    {
        var back = RoundTrip(OnePage(
            ShapeFactory.Create(ShapeKind.Cylinder, new Rect(20, 20, 200, 120)))).Pages[0].Shapes[0];

        Assert.NotNull(back.UnitDetail);
    }

    [AvaloniaFact]
    public void HowAShapeIsPaintedSurvivesTheTrip()
    {
        var box = ShapeFactory.Create(ShapeKind.Rectangle, new Rect(20, 20, 200, 120));
        box.Fill = Color.FromRgb(0x33, 0x66, 0x99);
        box.Stroke = Color.FromRgb(0xAA, 0x22, 0x11);
        box.StrokeThickness = 3;
        box.StrokeStyle = StrokeStyle.Dashed;

        var back = RoundTrip(OnePage(box)).Pages[0].Shapes[0];

        Assert.Equal(box.Fill, back.Fill);
        Assert.Equal(box.Stroke, back.Stroke);
        Assert.Equal(3, back.StrokeThickness, 1);
        Assert.Equal(StrokeStyle.Dashed, back.StrokeStyle);
    }

    [AvaloniaFact]
    public void WordsAndHowTheyAreSetSurviveTheTrip()
    {
        var box = ShapeFactory.Create(ShapeKind.Rectangle, new Rect(20, 20, 200, 120));
        box.Text = "Ampersands & angle brackets <like this>";
        box.FontSize = 21;
        box.Bold = true;
        box.TextAlign = TextAlign.Right;
        box.TextColor = Colors.DarkGreen;

        var back = RoundTrip(OnePage(box)).Pages[0].Shapes[0];

        Assert.Equal(box.Text, back.Text);
        Assert.Equal(21, back.FontSize, 1);
        Assert.True(back.Bold);
        Assert.False(back.Italic);
        Assert.Equal(TextAlign.Right, back.TextAlign);
        Assert.Equal(Colors.DarkGreen, back.TextColor);
    }

    [AvaloniaFact]
    public void ATurnedShapeComesBackTurnedTheSameWay()
    {
        var box = ShapeFactory.Create(ShapeKind.Rectangle, new Rect(100, 100, 200, 100));
        box.Rotation = 30;

        var back = RoundTrip(OnePage(box)).Pages[0].Shapes[0];

        Assert.Equal(30, back.Rotation, 1);
        Assert.Equal(box.Bounds.Center.X, back.Bounds.Center.X, 1);
        Assert.Equal(box.Bounds.Center.Y, back.Bounds.Center.Y, 1);
    }

    [AvaloniaFact]
    public void AGluedConnectorIsStillGluedToTheSameTwoShapes()
    {
        var one = ShapeFactory.Create(ShapeKind.Rectangle, new Rect(40, 40, 120, 80));
        var two = ShapeFactory.Create(ShapeKind.Ellipse, new Rect(400, 300, 120, 80));

        var line = new ConnectorShape(new Point(0, 0), new Point(0, 0))
        {
            StartShape = one,
            EndShape = two,
            Text = "flows to"
        };

        var back = RoundTrip(OnePage(one, two, line)).Pages[0].Shapes;
        var connector = Assert.Single(back.OfType<ConnectorShape>());

        Assert.Same(back[0], connector.StartShape);
        Assert.Same(back[1], connector.EndShape);
        Assert.Equal("flows to", connector.Text);
    }

    [AvaloniaFact]
    public void AConnectorHeldToOnePointIsStillHeldToThatPoint()
    {
        var one = ShapeFactory.Create(ShapeKind.Rectangle, new Rect(40, 40, 120, 80));
        var two = ShapeFactory.Create(ShapeKind.Rectangle, new Rect(400, 300, 120, 80));

        var line = new ConnectorShape(new Point(0, 0), new Point(0, 0))
        {
            StartShape = one,
            StartPort = 2,
            EndShape = two,
            EndPort = 0
        };

        var connector = Assert.Single(RoundTrip(OnePage(one, two, line)).Pages[0].Shapes.OfType<ConnectorShape>());

        Assert.Equal(2, connector.StartPort);
        Assert.Equal(0, connector.EndPort);
    }

    [AvaloniaFact]
    public void AnUngluedConnectorKeepsItsOwnTwoEnds()
    {
        var line = new ConnectorShape(new Point(50, 60), new Point(300, 220));
        var back = Assert.Single(RoundTrip(OnePage(line)).Pages[0].Shapes.OfType<ConnectorShape>());

        Assert.Equal(50, back.Start.X, 1);
        Assert.Equal(60, back.Start.Y, 1);
        Assert.Equal(300, back.End.X, 1);
        Assert.Equal(220, back.End.Y, 1);
    }

    [AvaloniaFact]
    public void EveryPageOfADocumentIsWrittenAndReadBack()
    {
        var document = new DiagramDocument();

        var pages = Enumerable.Range(1, 3)
            .Select(number => new DiagramPage($"Page {number}") { Width = 800, Height = 600 })
            .ToList();

        foreach (var (page, number) in pages.Select((page, index) => (page, index + 1)))
            for (var i = 0; i < number; i++)
                page.Shapes.Add(ShapeFactory.Create(ShapeKind.Rectangle, new Rect(20 * i, 20, 60, 40)));

        document.SetPages(pages);

        var back = RoundTrip(document);

        Assert.Equal(3, back.Pages.Count);
        Assert.Equal([1, 2, 3], back.Pages.Select(page => page.Shapes.Count));
        Assert.Equal(["Page 1", "Page 2", "Page 3"], back.Pages.Select(page => page.Name));
    }

    [AvaloniaFact]
    public void AnEmptyDrawingIsStillADrawing()
    {
        var document = new DiagramDocument();
        document.SetPages([new DiagramPage("Blank") { Width = 800, Height = 600 }]);

        var back = RoundTrip(document);

        Assert.Empty(Assert.Single(back.Pages).Shapes);
    }

    #endregion

    /// <summary>Every point named in a unit-square outline, for asking where a curve reaches.</summary>
    private static IEnumerable<Point> Corners(string outline) =>
        StencilPath.Steps(outline, new Rect(0, 0, 1, 1)).SelectMany(step => step.Points);
}
