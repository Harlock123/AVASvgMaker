using System;
using System.Linq;
using System.Text.Json;
using Avalonia;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using Avalonia.Headless.XUnit;
using Xunit;

namespace AVASvgMaker.Tests;

/// <summary>
/// The format is the one thing a user cannot work around when it goes wrong, so every version
/// that has ever been written is checked to still open, and every setting to survive the trip.
/// </summary>
public class FileFormatTests
{
    private static DiagramDocument Furnished()
    {
        var document = Harness.Page(700, 500);
        document.SetMargin(24);

        var a = Harness.Box(document, new Rect(60, 60, 120, 60), "A");
        var b = Harness.Box(document, new Rect(400, 60, 120, 60), "B");

        a.Bold = true;
        a.Italic = true;
        a.FontName = "DejaVu Serif";
        a.TextAlign = TextAlign.Right;
        a.StrokeStyle = StrokeStyle.Dashed;

        Harness.Join(document, a, 1, b, 3);
        document.Group([a, b]);

        var pool = (ContainerShape)ShapeFactory.Create(ShapeKind.Pool, new Rect(60, 200, 500, 240));
        document.Add(pool);

        foreach (var _ in Enumerable.Range(0, 2))
        {
            var lane = (ContainerShape)ShapeFactory.Create(ShapeKind.Lane, new Rect(70, 210, 480, 100));
            document.Add(lane);
            document.Adopt(lane, pool);
        }

        document.LayoutContainers();
        document.ResizeLane(document.LanesOf(pool)[0], pool.Body.Top + pool.Body.Height * 0.7);

        document.AddPage();
        document.RenamePage(1, "Second");
        document.SetPageSize(1056, 816);
        Harness.Box(document, new Rect(40, 40, 80, 40), "on two");

        return document;
    }

    [AvaloniaFact]
    public void EverythingSurvivesTheRoundTrip()
    {
        var written = Furnished();
        var json = DiagramFile.ToJson(written);
        var read = DiagramFile.FromJson(json);

        Assert.Contains($"\"version\": {DiagramFile.CurrentVersion}", json);
        Assert.Equal(2, read.Pages.Count);
        Assert.Equal(0, read.PageIndex);

        // Page one: paper, margin, name.
        Assert.Equal(700, read.Pages[0].Width, 2);
        Assert.Equal(24, read.Pages[0].Margin, 2);
        Assert.Equal("Second", read.Pages[1].Name);
        Assert.Equal(1056, read.Pages[1].Width, 2);

        // Formatting.
        var a = read.Pages[0].Shapes.First(shape => shape.Text == "A");
        Assert.True(a.Bold);
        Assert.True(a.Italic);
        Assert.Equal("DejaVu Serif", a.FontName);
        Assert.Equal(TextAlign.Right, a.TextAlign);
        Assert.Equal(StrokeStyle.Dashed, a.StrokeStyle);

        // Grouping, glue and lane shares.
        Assert.NotEqual(0, a.GroupId);
        Assert.Equal(2, read.Pages[0].Shapes.Count(shape => shape.GroupId == a.GroupId));

        var connector = read.Pages[0].Shapes.OfType<ConnectorShape>().Single();
        Assert.NotNull(connector.StartShape);
        Assert.NotNull(connector.EndShape);
        Assert.Contains(connector.StartShape!, read.Pages[0].Shapes);

        var pool = read.Pages[0].Shapes.OfType<ContainerShape>().First(c => c.Kind == ShapeKind.Pool);
        var lanes = read.LanesOf(pool);
        Assert.Equal(2, lanes.Count);
        Assert.NotEqual(lanes[0].LaneShare, lanes[1].LaneShare, 3);
    }

    [AvaloniaFact]
    public void APlainDrawingWritesNothingItDoesNotNeed()
    {
        var document = Harness.Page();
        Harness.Box(document, new Rect(10, 10, 40, 40), "plain");

        var json = DiagramFile.ToJson(document);

        Assert.DoesNotContain("fontName", json);
        Assert.DoesNotContain("bold", json);
        Assert.DoesNotContain("italic", json);
        Assert.DoesNotContain("textAlign", json);
        Assert.DoesNotContain("groupId", json);
        Assert.DoesNotContain("laneShare", json);
        Assert.DoesNotContain("margin", json);
    }

    [AvaloniaTheory]
    [InlineData(5)]   // one page, shapes at the top level, no page record
    [InlineData(6)]   // pages, but one paper size for the document
    [InlineData(7)]   // per-page paper, but no lane shares
    [InlineData(8)]   // lane shares, but no font settings
    [InlineData(9)]   // font settings, but no groups
    [InlineData(10)]  // groups, but no margins
    public void EveryOlderVersionStillOpens(int version)
    {
        var json = Downgrade(DiagramFile.ToJson(Furnished()), version);
        var read = DiagramFile.FromJson(json);

        Assert.NotEmpty(read.Pages);
        Assert.NotEmpty(read.Pages[0].Shapes);

        // Whatever the version could not say comes back as it was drawn before it could.
        if (version < 6)
            Assert.Single(read.Pages);
        else
            Assert.Equal(2, read.Pages.Count);

        if (version < 11)
            Assert.All(read.Pages, page => Assert.Equal(0, page.Margin, 2));

        if (version < 10)
            Assert.All(read.Pages[0].Shapes, shape => Assert.Equal(0, shape.GroupId));

        if (version < 9)
            Assert.All(read.Pages[0].Shapes, shape =>
            {
                Assert.False(shape.Bold);
                Assert.Equal(TextAlign.Center, shape.TextAlign);
            });

        // Glue is the thing most likely to break silently, so it is checked at every version.
        var connector = read.Pages[0].Shapes.OfType<ConnectorShape>().Single();
        Assert.NotNull(connector.StartShape);
        Assert.NotNull(connector.EndShape);
    }

    [AvaloniaFact]
    public void APathKeepsTheMarkingsDrawnOverItsFace()
    {
        var document = Harness.Page(400, 300);

        document.Add(new PathShape(new Rect(20, 20, 100, 80))
        {
            Outline = PathShape.Fallback,
            Detail = "M 0,0.5 L 1,0.5"
        });

        var read = DiagramFile.FromJson(DiagramFile.ToJson(document));
        var path = Assert.IsType<PathShape>(read.Pages[0].Shapes[0]);

        Assert.Equal("M 0,0.5 L 1,0.5", path.Detail);
    }

    [AvaloniaFact]
    public void AFileFromBeforePathsCouldHaveMarkingsStillOpens()
    {
        // Version 14 could say what a path's outline was but had no way to say that anything
        // was drawn over it. Such a file is still a path, with nothing over its face.
        var document = Harness.Page(400, 300);

        document.Add(new PathShape(new Rect(20, 20, 100, 80))
        {
            Outline = PathShape.Fallback,
            Detail = "M 0,0.5 L 1,0.5"
        });

        var json = Downgrade(DiagramFile.ToJson(document), 14);
        var path = Assert.IsType<PathShape>(DiagramFile.FromJson(json).Pages[0].Shapes[0]);

        Assert.Null(path.Detail);
        Assert.Equal(PathShape.Fallback, path.Outline);
    }

    [AvaloniaFact]
    public void AFileFromTheFutureIsRefusedRatherThanMisread()
    {
        var json = DiagramFile.ToJson(Harness.Page())
            .Replace($"\"version\": {DiagramFile.CurrentVersion}", "\"version\": 999");

        var thrown = Assert.Throws<System.IO.InvalidDataException>(() => DiagramFile.FromJson(json));
        Assert.Contains("newer version", thrown.Message);
    }

    [AvaloniaFact]
    public void SomethingElseEntirelyIsRefused()
    {
        Assert.Throws<System.IO.InvalidDataException>(
            () => DiagramFile.FromJson("{\"format\":\"something.else\",\"version\":1}"));
    }

    /// <summary>
    /// Rewrites a current file as an older version would have written it: stripping the fields
    /// that version did not have, and putting back the shape it used instead.
    /// </summary>
    private static string Downgrade(string json, int version)
    {
        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;

        string Pages()
        {
            var pages = root.GetProperty("pages").EnumerateArray().Select(page =>
            {
                var shapes = Strip(page.GetProperty("shapes").GetRawText(), version);
                var size = version >= 7
                    ? $",\"width\":{page.GetProperty("width").GetRawText()}," +
                      $"\"height\":{page.GetProperty("height").GetRawText()}"
                    : string.Empty;

                return $"{{\"name\":{page.GetProperty("name").GetRawText()}{size},\"shapes\":{shapes}}}";
            });

            return string.Join(",", pages);
        }

        // Up to version 5 there were no page records at all, only the one page's shapes.
        if (version < 6)
        {
            var shapes = Strip(root.GetProperty("pages")[0].GetProperty("shapes").GetRawText(), version);

            return $"{{\"format\":\"{DiagramFile.FormatId}\",\"version\":{version}," +
                   "\"pageWidth\":700,\"pageHeight\":500," +
                   $"\"shapes\":{shapes}}}";
        }

        var documentSize = version < 7 ? "\"pageWidth\":700,\"pageHeight\":500," : string.Empty;

        return $"{{\"format\":\"{DiagramFile.FormatId}\",\"version\":{version}," +
               documentSize + $"\"pages\":[{Pages()}]}}";
    }

    private static string Strip(string shapes, int version)
    {
        using var parsed = JsonDocument.Parse(shapes);

        var kept = parsed.RootElement.EnumerateArray().Select(shape =>
        {
            var fields = shape.EnumerateObject()
                .Where(field => Kept(field.Name, version))
                .Select(field => $"\"{field.Name}\":{field.Value.GetRawText()}");

            return "{" + string.Join(",", fields) + "}";
        });

        return "[" + string.Join(",", kept) + "]";
    }

    private static bool Kept(string field, int version) => field switch
    {
        "laneShare" => version >= 8,
        "fontName" or "bold" or "italic" or "textAlign" => version >= 9,
        "groupId" => version >= 10,
        "detail" => version >= 15,
        _ => true
    };
}
