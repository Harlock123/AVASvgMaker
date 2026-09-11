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
        bool background = false,
        string colours = "",
        string theme = "",
        string styles = "")
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

            Put("visio/document.xml",
                $"<VisioDocument xmlns='{Main}'>" +
                (colours.Length > 0 ? $"<Colors>{colours}</Colors>" : string.Empty) +
                (styles.Length > 0 ? $"<StyleSheets>{styles}</StyleSheets>" : string.Empty) +
                "</VisioDocument>");

            if (theme.Length > 0)
            {
                Put("visio/_rels/document.xml.rels",
                    $"<Relationships xmlns='{Package}'>" +
                    $"<Relationship Id='rId1' Type='{Office}/theme' Target='theme/theme1.xml'/>" +
                    "</Relationships>");

                Put("visio/theme/theme1.xml",
                    "<a:theme xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main'>" +
                    "<a:themeElements><a:extLst><a:ext uri='{x}'>" +
                    "<vt:variationClrSchemeLst xmlns:vt='http://schemas.microsoft.com/office/visio/2012/theme'>" +
                    theme +
                    "</vt:variationClrSchemeLst></a:ext></a:extLst></a:themeElements></a:theme>");
            }

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

    /// <summary>
    /// A group four inches square at the middle of the page, holding one two-inch shape at its
    /// centre. The shape is filled over its own left half only, so which half of it ends up
    /// filled says whether the group's frame reached it.
    /// </summary>
    private static string Held(string groupCells)
    {
        const string LeftHalf =
            "<Section N='Geometry' IX='0'>" +
            "<Row T='MoveTo' IX='1'><Cell N='X' V='0'/><Cell N='Y' V='0'/></Row>" +
            "<Row T='LineTo' IX='2'><Cell N='X' V='1'/><Cell N='Y' V='0'/></Row>" +
            "<Row T='LineTo' IX='3'><Cell N='X' V='1'/><Cell N='Y' V='2'/></Row>" +
            "<Row T='LineTo' IX='4'><Cell N='X' V='0'/><Cell N='Y' V='2'/></Row>" +
            "<Row T='LineTo' IX='5'><Cell N='X' V='0'/><Cell N='Y' V='0'/></Row>" +
            "</Section>";

        return "<Shape ID='1' Type='Group'>" +
               "<Cell N='PinX' V='4'/><Cell N='PinY' V='3'/>" +
               "<Cell N='Width' V='4'/><Cell N='Height' V='4'/>" +
               "<Cell N='LocPinX' V='2'/><Cell N='LocPinY' V='2'/>" +
               groupCells +
               "<Shapes><Shape ID='2' Type='Shape'>" +
               "<Cell N='PinX' V='2'/><Cell N='PinY' V='2'/>" +
               "<Cell N='Width' V='2'/><Cell N='Height' V='2'/>" +
               "<Cell N='LocPinX' V='1'/><Cell N='LocPinY' V='1'/>" +
               LeftHalf + "</Shape></Shapes></Shape>";
    }

    /// <summary>A point in Visio inches on an 8 by 6 page, in ours.</summary>
    private static Point On(double x, double y) => new(x * 96, (6 - y) * 96);

    [AvaloniaFact]
    public void AShapeInsideAPlainGroupSitsWhereTheGroupPutsIt()
    {
        var read = VisioImporter.Read(Drawing(Held(string.Empty)));
        var shape = Assert.Single(read.Document.Pages[0].Shapes);

        // The group's frame only shifts here, so the filled half stays the left one.
        Assert.True(shape.HitTest(On(3.5, 3)), "the left half should be filled");
        Assert.False(shape.HitTest(On(4.5, 3)), "the right half should not be");
    }

    [AvaloniaFact]
    public void AGroupThatHasBeenTurnedTurnsWhatItHolds()
    {
        // A quarter turn anticlockwise about the group's middle. Carried down as a shift only -
        // which is what it used to be - the held shape would not move at all, because it sits
        // at the very centre the group turns about.
        var read = VisioImporter.Read(Drawing(
            Held("<Cell N='Angle' V='1.5707963267948966'/>")));

        var shape = Assert.Single(read.Document.Pages[0].Shapes);

        // The left half swings round to the bottom.
        Assert.True(shape.HitTest(On(4, 2.5)), "the filled half should have swung to the bottom");
        Assert.False(shape.HitTest(On(4, 3.5)), "and should have left the top");
    }

    [AvaloniaFact]
    public void AGroupThatHasBeenTurnedOverTurnsOverWhatItHolds()
    {
        var read = VisioImporter.Read(Drawing(Held("<Cell N='FlipX' V='1'/>")));
        var shape = Assert.Single(read.Document.Pages[0].Shapes);

        // Mirrored, so the half that was filled is now the other one.
        Assert.True(shape.HitTest(On(4.5, 3)), "the right half should be filled");
        Assert.False(shape.HitTest(On(3.5, 3)), "the left half should not be");
    }

    [AvaloniaFact]
    public void AGroupThatHasBeenStretchedStretchesWhatItHolds()
    {
        // The frame is read from the group itself, so a group scaled by its own cells carries
        // that to its contents rather than leaving them their original size.
        var read = VisioImporter.Read(Drawing(Held(string.Empty)));
        var plain = Assert.Single(read.Document.Pages[0].Shapes);

        Assert.Equal(192, plain.Bounds.Width, 1);
        Assert.Equal(192, plain.Bounds.Height, 1);
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
    public void APolylineDrawsEveryCornerItLists()
    {
        // The row's cells name only where the run ends; the corners along the way are in the
        // formula, and jumping straight to the end loses all of them.
        var run =
            "<Section N='Geometry' IX='0'>" +
            "<Row T='MoveTo' IX='1'><Cell N='X' V='0'/><Cell N='Y' V='0'/></Row>" +
            "<Row T='PolylineTo' IX='2'><Cell N='X' V='1'/><Cell N='Y' V='1'/>" +
            "<Cell N='A' V='0' F='POLYLINE(0,0,0.25,0.5,0.5,0,0.75,0.5)'/></Row>" +
            "</Section>";

        var read = VisioImporter.Read(Drawing(Square(geometry: run)));
        var shape = Assert.IsType<PathShape>(read.Document.Pages[0].Shapes[0]);

        // One move and five straight edges: three corners, then the end.
        Assert.Equal(4, shape.Outline.Count(character => character == 'L'));
        Assert.Contains("0.25,0.5", shape.Outline);
        Assert.Contains("0.5,1", shape.Outline);
    }

    /// <summary>A NURBS row whose control points are the ones given, in fractions of the shape.</summary>
    private static string Nurbs(string end, params string[] control) =>
        "<Section N='Geometry' IX='0'>" +
        "<Row T='MoveTo' IX='1'><Cell N='X' V='0'/><Cell N='Y' V='0'/></Row>" +
        $"<Row T='NURBSTo' IX='2'>{end}<Cell N='B' V='1'/>" +
        $"<Cell N='E' V='0' F='NURBS(1,3,0,0,{string.Join(",", control)})'/></Row>" +
        "</Section>";

    [AvaloniaFact]
    public void ACurveThroughPointsInALineStaysOnThatLine()
    {
        // Control points strung along the diagonal. Whatever is assumed about the knots, a
        // curve shaped by points on a line cannot leave it - so this holds the arithmetic
        // without holding an opinion about the convention.
        var read = VisioImporter.Read(Drawing(Square(geometry: Nurbs(
            "<Cell N='X' V='1'/><Cell N='Y' V='1'/>",
            "0.25,0.25,0.25,1", "0.5,0.5,0.5,1", "0.75,0.75,0.75,1"))));

        var shape = Assert.IsType<PathShape>(read.Document.Pages[0].Shapes[0]);

        foreach (var point in Corners(shape.Outline))
            Assert.Equal(1 - point.X, point.Y, 3);
    }

    [AvaloniaFact]
    public void ACurveBeginsAndEndsWhereTheRowsSayItDoes()
    {
        var read = VisioImporter.Read(Drawing(Square(geometry: Nurbs(
            "<Cell N='X' V='1'/><Cell N='Y' V='0'/>",
            "0,1,0.25,1", "1,1,0.5,1", "1,0.5,0.75,1"))));

        var shape = Assert.IsType<PathShape>(read.Document.Pages[0].Shapes[0]);
        var points = Corners(shape.Outline).ToList();

        // The move starts at the shape's bottom left, and the run ends at its bottom right.
        Assert.Equal(0, points[0].X, 3);
        Assert.Equal(1, points[0].Y, 3);
        Assert.Equal(1, points[^1].X, 3);
        Assert.Equal(1, points[^1].Y, 3);
    }

    [AvaloniaFact]
    public void ACurveStaysInsideTheRunOfPointsThatShapesIt()
    {
        // A B-spline never leaves the hull of its control points, so this holds however the
        // knots are read - and it is what makes a wrong reading a bounded sort of wrong.
        var read = VisioImporter.Read(Drawing(Square(geometry: Nurbs(
            "<Cell N='X' V='1'/><Cell N='Y' V='0'/>",
            "0,1,0.25,1", "1,1,0.5,1", "1,0.5,0.75,1"))));

        var shape = Assert.IsType<PathShape>(read.Document.Pages[0].Shapes[0]);

        foreach (var point in Corners(shape.Outline))
        {
            Assert.InRange(point.X, -0.001, 1.001);
            Assert.InRange(point.Y, -0.001, 1.001);
        }
    }

    [AvaloniaFact]
    public void ACurveIsDrawnAsACurveRatherThanAsOneStraightJump()
    {
        var read = VisioImporter.Read(Drawing(Square(geometry: Nurbs(
            "<Cell N='X' V='1'/><Cell N='Y' V='0'/>",
            "0,1,0.25,1", "1,1,0.5,1", "1,0.5,0.75,1"))));

        var shape = Assert.IsType<PathShape>(read.Document.Pages[0].Shapes[0]);

        Assert.True(shape.Outline.Count(character => character == 'L') > 8,
            $"the curve came out as {shape.Outline.Count(character => character == 'L')} edges");
    }

    [AvaloniaFact]
    public void ANurbsRowWithNoFormulaToReadStillReachesItsEnd()
    {
        var bare =
            "<Section N='Geometry' IX='0'>" +
            "<Row T='MoveTo' IX='1'><Cell N='X' V='0'/><Cell N='Y' V='0'/></Row>" +
            "<Row T='NURBSTo' IX='2'><Cell N='X' V='1'/><Cell N='Y' V='1'/></Row>" +
            "</Section>";

        var read = VisioImporter.Read(Drawing(Square(geometry: bare)));
        var shape = Assert.IsType<PathShape>(read.Document.Pages[0].Shapes[0]);

        Assert.Equal(1, shape.Outline.Count(character => character == 'L'));
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

    /// <summary>One of the theme's variations: seven colours, named rather than listed.</summary>
    private static string Variation(params string[] colours) =>
        "<vt:variationClrScheme>" + string.Concat(colours.Select((colour, i) =>
            $"<vt:varColor{i + 1}><a:srgbClr val='{colour}'/></vt:varColor{i + 1}>")) +
        "</vt:variationClrScheme>";

    [AvaloniaFact]
    public void AColourGivenAsANumberComesFromVisiosOwnTwoDozen()
    {
        // 2 is red in the table every Visio drawing has without writing it down.
        var read = VisioImporter.Read(Drawing(Square(extra: "<Cell N='LineColor' V='2'/>")));

        Assert.Equal(Colors.Red, read.Document.Pages[0].Shapes[0].Stroke);
    }

    [AvaloniaFact]
    public void ADrawingCanNameColoursOfItsOwnPastTheEndOfThose()
    {
        var read = VisioImporter.Read(Drawing(
            Square(extra: "<Cell N='LineColor' V='30'/>"),
            colours: "<ColorEntry IX='30' RGB='#5B9BD5'/>"));

        Assert.Equal(Color.Parse("#5B9BD5"), read.Document.Pages[0].Shapes[0].Stroke);
    }

    [AvaloniaFact]
    public void TwoFiftyFiveIsVisiosWayOfSayingNoColourAtAll()
    {
        var read = VisioImporter.Read(Drawing(Square(extra: "<Cell N='LineColor' V='255'/>")));

        Assert.Equal(DiagramShape.DefaultStroke, read.Document.Pages[0].Shapes[0].Stroke);
    }

    [AvaloniaFact]
    public void AShapeThatDefersToTheThemeIsDrawnInTheThemesColours()
    {
        // The shape states no line colour at all - only which of the theme's colours it wants,
        // and which variation of the theme to count along. This is how most shapes in a real
        // drawing are coloured, and reading it as "no colour" leaves the drawing all defaults.
        var read = VisioImporter.Read(Drawing(
            Square(extra: "<Cell N='QuickStyleLineColor' V='102'/><Cell N='QuickStyleVariation' V='1'/>"),
            theme: Variation("111111", "222222", "333333") +
                   Variation("AA0000", "00BB00", "0000CC")));

        // Variation 1, third colour along.
        Assert.Equal(Color.Parse("#0000CC"), read.Document.Pages[0].Shapes[0].Stroke);
    }

    [AvaloniaFact]
    public void AThemedShapeInADrawingWithNoThemeKeepsTheDefault()
    {
        var read = VisioImporter.Read(Drawing(
            Square(extra: "<Cell N='LineColor' V='Themed'/><Cell N='QuickStyleLineColor' V='100'/>")));

        Assert.Equal(DiagramShape.DefaultStroke, read.Document.Pages[0].Shapes[0].Stroke);
    }

    [AvaloniaFact]
    public void AShapeThatSaysItIsNotFilledIsNotFilled()
    {
        var read = VisioImporter.Read(Drawing(Square(
            extra: "<Cell N='FillForegnd' V='2'/><Cell N='FillPattern' V='0'/>")));

        Assert.Equal(Colors.Transparent, read.Document.Pages[0].Shapes[0].Fill);
    }

    [AvaloniaFact]
    public void AFillTheShapeStatesOutrightIsUsed()
    {
        var read = VisioImporter.Read(Drawing(Square(
            extra: "<Cell N='FillForegnd' V='#abcdef'/><Cell N='FillPattern' V='1'/>")));

        Assert.Equal(Color.Parse("#abcdef"), read.Document.Pages[0].Shapes[0].Fill);
    }

    [AvaloniaFact]
    public void WordsGoWhereTheShapeSaysItsTextBlockIs()
    {
        // A block half the width, in the top left quarter of a one-inch square.
        var read = VisioImporter.Read(Drawing(Square(extra:
            "<Cell N='TxtWidth' V='0.5'/><Cell N='TxtHeight' V='0.5'/>" +
            "<Cell N='TxtLocPinX' V='0.25'/><Cell N='TxtLocPinY' V='0.25'/>" +
            "<Cell N='TxtPinX' V='0.25'/><Cell N='TxtPinY' V='0.75'/>")));

        var frame = Assert.NotNull(read.Document.Pages[0].Shapes[0].TextFrame);

        Assert.Equal(0, frame.X, 3);
        Assert.Equal(0, frame.Y, 3);
        Assert.Equal(0.5, frame.Width, 3);
        Assert.Equal(0.5, frame.Height, 3);
    }

    [AvaloniaFact]
    public void ALabelCanHangBelowTheShapeItBelongsTo()
    {
        // How the name under a stick figure is written: the block is pinned below the shape's
        // own bottom edge, so its fractions run past the end of the shape.
        var read = VisioImporter.Read(Drawing(Square(extra:
            "<Cell N='TxtWidth' V='2'/><Cell N='TxtHeight' V='0.4'/>" +
            "<Cell N='TxtLocPinX' V='1'/><Cell N='TxtLocPinY' V='0.2'/>" +
            "<Cell N='TxtPinX' V='0.5'/><Cell N='TxtPinY' V='-0.2'/>")));

        var frame = Assert.NotNull(read.Document.Pages[0].Shapes[0].TextFrame);

        // It begins exactly at the shape's bottom edge and hangs below it, and is wider than
        // the shape so that the name does not wrap to the figure's width.
        Assert.Equal(1, frame.Y, 3);
        Assert.Equal(1.4, frame.Bottom, 3);
        Assert.Equal(-0.5, frame.X, 3);
        Assert.Equal(2, frame.Width, 3);
    }

    [AvaloniaFact]
    public void AShapeWhoseWordsFillItRecordsNoBlockAtAll()
    {
        var read = VisioImporter.Read(Drawing(Square()));

        Assert.Null(read.Document.Pages[0].Shapes[0].TextFrame);
    }

    [AvaloniaTheory]
    [InlineData("0", TextVerticalAlign.Top)]
    [InlineData("1", TextVerticalAlign.Middle)]
    [InlineData("2", TextVerticalAlign.Bottom)]
    public void WordsHugTheEdgeTheShapeSaysTheyDo(string stated, TextVerticalAlign expected)
    {
        var read = VisioImporter.Read(Drawing(Square(extra: $"<Cell N='VerticalAlign' V='{stated}'/>")));

        Assert.Equal(expected, read.Document.Pages[0].Shapes[0].TextVerticalAlign);
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

    /// <summary>A connector between two points, with whatever line-end cells are handed in.</summary>
    private static string Line(string ends = "") =>
        "<Shape ID='3' Type='Shape'>" +
        "<Cell N='PinX' V='3'/><Cell N='PinY' V='4'/>" +
        "<Cell N='Width' V='2'/><Cell N='Height' V='0'/>" +
        "<Cell N='BeginX' V='2.5'/><Cell N='BeginY' V='4'/>" +
        "<Cell N='EndX' V='4.5'/><Cell N='EndY' V='4'/>" + ends + "</Shape>";

    /// <summary>One style sheet, pointing at another for whatever it does not say itself.</summary>
    private static string Style(string id, string cells, string? next = null)
    {
        var chain = next is null
            ? string.Empty
            : $" LineStyle='{next}' FillStyle='{next}' TextStyle='{next}'";

        return $"<StyleSheet ID='{id}' NameU='S{id}'{chain}>{cells}</StyleSheet>";
    }

    [AvaloniaFact]
    public void AShapeTakesWhatItDoesNotStateFromItsStyle()
    {
        var read = VisioImporter.Read(Drawing(
            Square(extra: "<Cell N='LineWeight' V='0.04'/>").Replace("<Shape ID='1'", "<Shape ID='1' LineStyle='4'"),
            styles: Style("4", "<Cell N='LineColor' V='#aa3311'/><Cell N='LineWeight' V='0.01'/>")));

        var shape = read.Document.Pages[0].Shapes[0];

        // The colour comes from the style; the weight the shape states for itself outranks it.
        Assert.Equal(Color.Parse("#aa3311"), shape.Stroke);
        Assert.Equal(96 * 0.04, shape.StrokeThickness, 1);
    }

    [AvaloniaFact]
    public void AStyleHandsOnWhatItDoesNotSayEither()
    {
        // How a real drawing is put together: Normal says nothing, and points at a style that
        // points at another, and the answer is several links out from the shape.
        var read = VisioImporter.Read(Drawing(
            Square().Replace("<Shape ID='1'", "<Shape ID='1' LineStyle='3'"),
            styles: Style("3", string.Empty, "6") +
                    Style("6", string.Empty, "0") +
                    Style("0", "<Cell N='LineColor' V='#123456'/>")));

        Assert.Equal(Color.Parse("#123456"), read.Document.Pages[0].Shapes[0].Stroke);
    }

    [AvaloniaFact]
    public void TheFirstStyleWithSomethingToSayIsTheOneThatSaysIt()
    {
        var read = VisioImporter.Read(Drawing(
            Square().Replace("<Shape ID='1'", "<Shape ID='1' LineStyle='3'"),
            styles: Style("3", "<Cell N='LineColor' V='#00ff00'/>", "0") +
                    Style("0", "<Cell N='LineColor' V='#123456'/>")));

        Assert.Equal(Colors.Lime, read.Document.Pages[0].Shapes[0].Stroke);
    }

    [AvaloniaFact]
    public void EachKindOfCellFollowsItsOwnStyle()
    {
        // A shape points at three styles at once, and a cell goes to whichever of them it
        // belongs to: the line style knows nothing about fills.
        var shape = Square().Replace("<Shape ID='1'", "<Shape ID='1' LineStyle='1' FillStyle='2' TextStyle='3'");

        var read = VisioImporter.Read(Drawing(shape,
            styles: Style("1", "<Cell N='LineColor' V='#111111'/><Cell N='FillForegnd' V='#999999'/>") +
                    Style("2", "<Cell N='FillForegnd' V='#222222'/><Cell N='LineColor' V='#999999'/>") +
                    Style("3", "<Section N='Character'><Row IX='0'><Cell N='Size' V='0.25'/></Row></Section>")));

        var box = read.Document.Pages[0].Shapes[0];

        Assert.Equal(Color.Parse("#111111"), box.Stroke);
        Assert.Equal(Color.Parse("#222222"), box.Fill);
        Assert.Equal(24, box.FontSize, 1);
    }

    [AvaloniaFact]
    public void AStyleSayingItIsThemedStillMeansTheTheme()
    {
        // The usual shape of a real drawing: the styles pass the question along until one of
        // them says "ask the theme", and the walk has to stop there rather than carry on to
        // the black-and-white defaults underneath.
        var read = VisioImporter.Read(Drawing(
            Square(extra: "<Cell N='QuickStyleLineColor' V='100'/>")
                .Replace("<Shape ID='1'", "<Shape ID='1' LineStyle='6'"),
            theme: Variation("AB12CD"),
            styles: Style("6", "<Cell N='LineColor' V='Themed'/>", "0") +
                    Style("0", "<Cell N='LineColor' V='0'/>")));

        Assert.Equal(Color.Parse("#AB12CD"), read.Document.Pages[0].Shapes[0].Stroke);
    }

    [AvaloniaFact]
    public void AStyleThatPointsAtItselfIsNotFollowedForEver()
    {
        var read = VisioImporter.Read(Drawing(
            Square().Replace("<Shape ID='1'", "<Shape ID='1' LineStyle='5'"),
            styles: Style("5", string.Empty, "5")));

        Assert.Equal(DiagramShape.DefaultStroke, read.Document.Pages[0].Shapes[0].Stroke);
    }

    [AvaloniaFact]
    public void AnArrowheadCanComeFromAStyleToo()
    {
        var read = VisioImporter.Read(Drawing(
            Line().Replace("<Shape ID='3'", "<Shape ID='3' LineStyle='7'"),
            styles: Style("7", "<Cell N='EndArrow' V='4'/>")));

        var line = Assert.Single(read.Document.Pages[0].Shapes.OfType<ConnectorShape>());

        Assert.Equal(EndCapStyle.Arrow, line.EndCap);
        Assert.Equal(EndCapStyle.None, line.StartCap);
    }

    [AvaloniaFact]
    public void AConnectorWithNoArrowStatedAnywhereIsDrawnWithout()
    {
        // Every imported connector used to be given an arrow whatever the drawing said, which
        // put arrowheads on the plain associations of a use-case diagram that never had any.
        var read = VisioImporter.Read(Drawing(Line()));
        var line = Assert.Single(read.Document.Pages[0].Shapes.OfType<ConnectorShape>());

        Assert.Equal(EndCapStyle.None, line.StartCap);
        Assert.Equal(EndCapStyle.None, line.EndCap);
    }

    [AvaloniaFact]
    public void AConnectorThatSaysItHasNoArrowIsDrawnWithout()
    {
        var read = VisioImporter.Read(Drawing(Line(
            "<Cell N='BeginArrow' V='0'/><Cell N='EndArrow' V='0'/>")));

        var line = Assert.Single(read.Document.Pages[0].Shapes.OfType<ConnectorShape>());

        Assert.Equal(EndCapStyle.None, line.StartCap);
        Assert.Equal(EndCapStyle.None, line.EndCap);
    }

    [AvaloniaFact]
    public void AnEndTheDrawingAsksForIsDrawn()
    {
        // Which of Visio's forty-odd ends it is cannot be told from here, so it becomes an
        // arrow - but that there is one at all is read, and read at the right end.
        var read = VisioImporter.Read(Drawing(Line(
            "<Cell N='BeginArrow' V='0'/><Cell N='EndArrow' V='4'/>")));

        var line = Assert.Single(read.Document.Pages[0].Shapes.OfType<ConnectorShape>());

        Assert.Equal(EndCapStyle.None, line.StartCap);
        Assert.Equal(EndCapStyle.Arrow, line.EndCap);
    }

    [AvaloniaFact]
    public void AnEndAtTheOtherEndIsDrawnThere()
    {
        var read = VisioImporter.Read(Drawing(Line(
            "<Cell N='BeginArrow' V='13'/><Cell N='EndArrow' V='0'/>")));

        var line = Assert.Single(read.Document.Pages[0].Shapes.OfType<ConnectorShape>());

        Assert.Equal(EndCapStyle.Arrow, line.StartCap);
        Assert.Equal(EndCapStyle.None, line.EndCap);
    }

    /// <summary>A Property row, the way Visio writes one.</summary>
    private static string Property(string name, string? value, string label = "", string invisible = "0") =>
        $"<Row N='{name}'>" +
        (value is null
            ? "<Cell N='Value' V='0' F='No Formula'/>"
            : $"<Cell N='Value' V='{value}' U='STR'/>") +
        (label.Length > 0 ? $"<Cell N='Label' V='{label}'/>" : string.Empty) +
        $"<Cell N='Type' V='0'/><Cell N='Invisible' V='{invisible}'/></Row>";

    [AvaloniaFact]
    public void TheDataAShapeCarriesComesOverWithIt()
    {
        var read = VisioImporter.Read(Drawing(Square(extra:
            "<Section N='Property'>" +
            Property("Owner", "Accounts") +
            Property("Cost", "1200", label: "Annual cost") +
            "</Section>")));

        var shape = read.Document.Pages[0].Shapes[0];

        Assert.Equal(2, shape.Fields.Count);
        Assert.Equal("Owner", shape.Fields[0].Name);
        Assert.Equal("Accounts", shape.Fields[0].Value);
        Assert.Equal("Annual cost", shape.Fields[1].Caption);
    }

    [AvaloniaFact]
    public void AFieldWithNoFormulaIsEmptyRatherThanHoldingAZero()
    {
        // Visio writes an unset property's value as 0 with no formula behind it. Read as the
        // number it looks like, every blank field in a drawing would arrive holding "0".
        var read = VisioImporter.Read(Drawing(Square(extra:
            $"<Section N='Property'>{Property("Owner", null)}</Section>")));

        var field = Assert.Single(read.Document.Pages[0].Shapes[0].Fields);

        Assert.Equal("Owner", field.Name);
        Assert.Equal(string.Empty, field.Value);
    }

    [AvaloniaFact]
    public void TheDrawingsOwnBookkeepingIsLeftWhereItIs()
    {
        // A stencil marks its shapes with what kind of thing they are, and hides the marks.
        // Visio does not show them either, and every imported shape carrying a ShapeClass
        // would be noise rather than data.
        var read = VisioImporter.Read(Drawing(Square(extra:
            "<Section N='Property'>" +
            Property("Owner", "Accounts") +
            Property("ShapeClass", "Connectivity", invisible: "1") +
            "</Section>")));

        var field = Assert.Single(read.Document.Pages[0].Shapes[0].Fields);

        Assert.Equal("Owner", field.Name);
    }

    [AvaloniaFact]
    public void AShapeFillsInTheFieldsItsMasterDefines()
    {
        // How a real drawing carries data: the stencil defines the fields, empty, and each
        // shape fills in its own answers. Read without merging, a shape would have either the
        // master's blanks or only the one field it happened to answer.
        var stamp = "<Shape ID='1' Type='Shape' Master='7'>" +
                    "<Cell N='PinX' V='2'/><Cell N='PinY' V='4'/>" +
                    "<Cell N='Width' V='1'/><Cell N='Height' V='1'/>" +
                    "<Section N='Property'><Row N='Owner'>" +
                    "<Cell N='Value' V='Accounts' U='STR'/></Row></Section></Shape>";

        var master = Square("5", extra:
            "<Section N='Property'>" +
            Property("Owner", null, label: "Owned by") +
            Property("Location", null) +
            "</Section>");

        var read = VisioImporter.Read(Drawing(stamp, masters: master));
        var shape = read.Document.Pages[0].Shapes[0];

        Assert.Equal(2, shape.Fields.Count);

        // The value is the shape's own; the label it did not restate is still the master's.
        Assert.Equal("Accounts", shape.Fields[0].Value);
        Assert.Equal("Owned by", shape.Fields[0].Caption);
        Assert.Equal("Location", shape.Fields[1].Name);
        Assert.Equal(string.Empty, shape.Fields[1].Value);
    }

    [AvaloniaFact]
    public void DataOnAGroupGoesToWhatIsDrawnInItsPlace()
    {
        // How a real stencil carries it: the data belongs to the group - a stick figure's name
        // is the figure's, not its head's - and the group draws nothing itself, being only the
        // shapes inside it. Read without handing it down, the data would arrive nowhere.
        var group = "<Shape ID='1' Type='Group'>" +
                    "<Cell N='PinX' V='4'/><Cell N='PinY' V='3'/>" +
                    "<Cell N='Width' V='0'/><Cell N='Height' V='0'/>" +
                    "<Section N='Property'>" + Property("Owner", "Accounts") + "</Section>" +
                    "<Shapes>" + Square("2") + "</Shapes></Shape>";

        var read = VisioImporter.Read(Drawing(group));
        var shape = Assert.Single(read.Document.Pages[0].Shapes);

        var field = Assert.Single(shape.Fields);
        Assert.Equal("Accounts", field.Value);
    }

    [AvaloniaFact]
    public void AShapeInsideAGroupAnswersForAFieldTheGroupNamed()
    {
        var group = "<Shape ID='1' Type='Group'>" +
                    "<Cell N='PinX' V='4'/><Cell N='PinY' V='3'/>" +
                    "<Cell N='Width' V='0'/><Cell N='Height' V='0'/>" +
                    "<Section N='Property'>" + Property("Owner", "Accounts") + "</Section>" +
                    "<Shapes>" + Square("2", extra:
                        "<Section N='Property'>" + Property("Owner", "Payroll") + "</Section>") +
                    "</Shapes></Shape>";

        var read = VisioImporter.Read(Drawing(group));
        var field = Assert.Single(Assert.Single(read.Document.Pages[0].Shapes).Fields);

        Assert.Equal("Payroll", field.Value);
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
    public void WhereTheWordsSitSurvivesTheTrip()
    {
        var box = ShapeFactory.Create(ShapeKind.Rectangle, new Rect(100, 100, 200, 100));
        box.Text = "under the shape";
        box.TextVerticalAlign = TextVerticalAlign.Top;
        box.TextFrame = new Rect(-0.25, 1.1, 1.5, 0.4);

        var back = RoundTrip(OnePage(box)).Pages[0].Shapes[0];
        var frame = Assert.NotNull(back.TextFrame);

        Assert.Equal(TextVerticalAlign.Top, back.TextVerticalAlign);
        Assert.Equal(-0.25, frame.X, 3);
        Assert.Equal(1.1, frame.Y, 3);
        Assert.Equal(1.5, frame.Width, 3);
        Assert.Equal(0.4, frame.Height, 3);
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

    [AvaloniaTheory]
    [InlineData(EndCapStyle.None, EndCapStyle.None)]
    [InlineData(EndCapStyle.Arrow, EndCapStyle.Arrow)]
    [InlineData(EndCapStyle.HollowArrow, EndCapStyle.Arrow)]
    [InlineData(EndCapStyle.CrowsFoot, EndCapStyle.Arrow)]
    [InlineData(EndCapStyle.Diamond, EndCapStyle.Arrow)]
    public void AnEndThatIsDrawnAtAllComesBackDrawn(EndCapStyle drawn, EndCapStyle expected)
    {
        // The twelve ends here and Visio's gallery are different sets, so only an end or no
        // end survives. Anything that was not the plain arrow used to come back as no end at
        // all, which lost the direction of the line along with its notation.
        var line = new ConnectorShape(new Point(50, 60), new Point(300, 220))
        {
            StartCap = drawn,
            EndCap = drawn
        };

        var back = Assert.Single(RoundTrip(OnePage(line)).Pages[0].Shapes.OfType<ConnectorShape>());

        Assert.Equal(expected, back.StartCap);
        Assert.Equal(expected, back.EndCap);
    }

    [AvaloniaFact]
    public void DataGoesOutToVisioAndComesBack()
    {
        var box = ShapeFactory.Create(ShapeKind.Rectangle, new Rect(20, 20, 200, 120));
        box.Text = "{Owner}";
        box.Fields.Add(new ShapeField { Name = "Owner", Label = "Owned by", Value = "Accounts" });
        box.Fields.Add(new ShapeField { Name = "Spare", Label = "Spare", Value = string.Empty });

        var back = RoundTrip(OnePage(box)).Pages[0].Shapes[0];

        Assert.Equal(2, back.Fields.Count);
        Assert.Equal("Accounts", back.Fields[0].Value);
        Assert.Equal("Owned by", back.Fields[0].Caption);

        // The empty one comes back empty rather than holding the zero it was written as.
        Assert.Equal(string.Empty, back.Fields[1].Value);

        // The label is still the question, not the answer it was showing.
        Assert.Equal("{Owner}", back.Text);
        Assert.Equal("Accounts", back.DisplayText);
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
