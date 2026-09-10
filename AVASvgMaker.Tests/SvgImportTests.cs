using System;
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

public class SvgImportTests
{
    private static SvgImporter.Result Read(string svg) =>
        SvgImporter.Read(new MemoryStream(Encoding.UTF8.GetBytes(svg)));

    private static string Wrap(string body, string attributes = "width=\"400\" height=\"300\"") =>
        $"<svg xmlns=\"http://www.w3.org/2000/svg\" {attributes}>{body}</svg>";

    [AvaloniaFact]
    public void ThePageTakesTheFilesSize()
    {
        var read = Read(Wrap("<rect x='10' y='10' width='50' height='50'/>"));

        Assert.Equal(400, read.Document.PageWidth, 2);
        Assert.Equal(300, read.Document.PageHeight, 2);
    }

    [AvaloniaFact]
    public void AViewBoxBecomesTheTransformOntoThePage()
    {
        // The box is half the page's size, so everything in it lands at twice the coordinates.
        var read = Read(Wrap("<rect x='10' y='10' width='50' height='50'/>",
            "width=\"400\" height=\"300\" viewBox=\"0 0 200 150\""));

        var shape = Assert.Single(read.Document.Shapes);

        Assert.Equal(20, shape.Bounds.X, 1);
        Assert.Equal(100, shape.Bounds.Width, 1);
    }

    [AvaloniaFact]
    public void ThePrimitivesBecomeTheShapesTheyMatch()
    {
        var read = Read(Wrap("""
            <rect x='10' y='20' width='100' height='50'/>
            <rect x='10' y='20' width='100' height='50' rx='8'/>
            <circle cx='200' cy='100' r='40'/>
            <ellipse cx='300' cy='100' rx='50' ry='25'/>
            <line x1='0' y1='0' x2='100' y2='100'/>
            <polygon points='10,10 60,10 35,50'/>
            <path d='M 10 10 L 60 10 L 60 60 Z'/>
            <text x='100' y='200' font-size='14'>Hello</text>
            """));

        var kinds = read.Document.Shapes.Select(shape => shape.Kind).ToList();

        Assert.Contains(ShapeKind.Rectangle, kinds);
        Assert.Contains(ShapeKind.RoundedRectangle, kinds);
        Assert.Equal(2, kinds.Count(kind => kind == ShapeKind.Ellipse));
        Assert.Contains(ShapeKind.Connector, kinds);
        Assert.Equal(2, kinds.Count(kind => kind == ShapeKind.Path));
        Assert.Contains(ShapeKind.TextBox, kinds);
        Assert.Empty(read.Skipped);
    }

    [AvaloniaFact]
    public void ARectangleLandsWhereItSaysItIs()
    {
        var shape = Assert.Single(Read(Wrap("<rect x='10' y='20' width='100' height='50'/>")).Document.Shapes);

        Assert.Equal(new Rect(10, 20, 100, 50), shape.Bounds);
    }

    [AvaloniaFact]
    public void StylingIsReadFromAttributesAndFromAStyleAttribute()
    {
        var read = Read(Wrap("""
            <rect x='0' y='0' width='10' height='10' fill='#FF0000' stroke='#00FF00' stroke-width='3'/>
            <rect x='0' y='0' width='10' height='10' style='fill:#0000FF;stroke:none;stroke-width:5'/>
            <rect x='0' y='0' width='10' height='10' fill='none' stroke='#000' stroke-dasharray='6 3'/>
            """));

        var shapes = read.Document.Shapes;

        Assert.Equal(Colors.Red, shapes[0].Fill);
        Assert.Equal(Colors.Lime, shapes[0].Stroke);
        Assert.Equal(3, shapes[0].StrokeThickness, 2);

        Assert.Equal(Colors.Blue, shapes[1].Fill);
        Assert.Equal(0, shapes[1].Stroke.A);
        Assert.Equal(5, shapes[1].StrokeThickness, 2);

        Assert.Equal(0, shapes[2].Fill.A);
        Assert.Equal(StrokeStyle.Dashed, shapes[2].StrokeStyle);
    }

    [AvaloniaFact]
    public void StyleIsInheritedThroughGroups()
    {
        var read = Read(Wrap("<g fill='#FF0000' stroke-width='4'><rect x='0' y='0' width='10' height='10'/></g>"));
        var shape = Assert.Single(read.Document.Shapes);

        Assert.Equal(Colors.Red, shape.Fill);
        Assert.Equal(4, shape.StrokeThickness, 2);
    }

    [AvaloniaFact]
    public void TransformsAreFollowedThroughNestedGroups()
    {
        var read = Read(Wrap(
            "<g transform='translate(100,50)'><g transform='scale(2)'>" +
            "<rect x='10' y='10' width='20' height='20'/></g></g>"));

        var shape = Assert.Single(read.Document.Shapes);

        // Scaled first, then moved: (10,10) becomes (20,20) becomes (120,70).
        Assert.Equal(120, shape.Bounds.X, 1);
        Assert.Equal(70, shape.Bounds.Y, 1);
        Assert.Equal(40, shape.Bounds.Width, 1);
    }

    [AvaloniaFact]
    public void ATurnInTheTransformBecomesTheShapesOwnAngle()
    {
        var read = Read(Wrap("<rect x='100' y='100' width='80' height='40' transform='rotate(90 140 120)'/>"));
        var shape = Assert.Single(read.Document.Shapes);

        Assert.Equal(90, shape.Rotation, 0);
        Assert.Equal(80, shape.Bounds.Width, 1);
        Assert.Equal(40, shape.Bounds.Height, 1);
        Assert.Equal(140, shape.Bounds.Center.X, 1);
        Assert.Equal(120, shape.Bounds.Center.Y, 1);
    }

    [AvaloniaFact]
    public void RelativeCommandsAndShorthandsAreUnderstood()
    {
        // A square drawn entirely in relative and shorthand commands.
        var read = Read(Wrap("<path d='m 10,10 h 40 v 40 h -40 z'/>"));
        var shape = Assert.Single(read.Document.Shapes);

        Assert.Equal(ShapeKind.Path, shape.Kind);
        Assert.Equal(new Rect(10, 10, 40, 40), shape.Bounds);
    }

    [AvaloniaFact]
    public void AnArcBecomesCurvesAndKeepsItsExtent()
    {
        // A half circle of radius 50, from one side to the other.
        var read = Read(Wrap("<path d='M 100,100 A 50,50 0 0 1 200,100'/>"));
        var shape = Assert.Single(read.Document.Shapes);

        Assert.Equal(100, shape.Bounds.X, 1);
        Assert.Equal(100, shape.Bounds.Width, 1);

        // A sweep of 1 curves clockwise, which in SVG's downward y is upwards on the page, so
        // the box reaches one radius above the two ends and no further below them.
        Assert.Equal(50, shape.Bounds.Y, 1);
        Assert.Equal(50, shape.Bounds.Height, 1);

        Assert.Contains("C", ((PathShape)shape).Outline);
        Assert.DoesNotContain("A", ((PathShape)shape).Outline);
    }

    [AvaloniaFact]
    public void WhetherAPathIsFilledIsTheFillsBusinessAndNotTheZs()
    {
        // A path that never closes is still filled if it has a fill: SVG shuts each subpath
        // for itself when filling. A real drawing full of glyph outlines depends on this, and
        // getting it wrong makes them invisible rather than merely wrong.
        var filled = (PathShape)Assert.Single(
            Read(Wrap("<path d='M 10,10 L 50,10 L 50,50' fill='#FF0000'/>")).Document.Shapes);

        Assert.Equal(Colors.Red, filled.Fill);

        // And one that says it has none has none, closed or not.
        var hollow = (PathShape)Assert.Single(
            Read(Wrap("<path d='M 10,10 L 50,50 Z' fill='none' stroke='#000'/>")).Document.Shapes);

        Assert.Equal(0, hollow.Fill.A);
        Assert.Equal(Colors.Black, hollow.Stroke);
    }

    [AvaloniaFact]
    public void TextKeepsItsWordsAndItsFormatting()
    {
        var read = Read(Wrap(
            "<text x='100' y='50' font-size='20' font-weight='bold' font-style='italic' " +
            "text-anchor='end' fill='#FF0000'>Hello there</text>"));

        var shape = Assert.Single(read.Document.Shapes);

        Assert.Equal("Hello there", shape.Text);
        Assert.Equal(20, shape.FontSize, 2);
        Assert.True(shape.Bold);
        Assert.True(shape.Italic);
        Assert.Equal(TextAlign.Right, shape.TextAlign);
        Assert.Equal(Colors.Red, shape.TextColor);
    }

    [AvaloniaFact]
    public void WhatCannotBeReadIsCountedAndReported()
    {
        var read = Read(Wrap("""
            <rect x='0' y='0' width='10' height='10'/>
            <image href='cat.png' x='0' y='0' width='10' height='10'/>
            <image href='dog.png' x='0' y='0' width='10' height='10'/>
            <use href='#something'/>
            """));

        Assert.Equal(1, read.Shapes);
        Assert.Contains("2 images", read.Summary);
        Assert.Contains("1 use", read.Summary);
    }

    [AvaloniaFact]
    public void SomethingThatIsNotAnSvgIsRefused()
    {
        Assert.Throws<InvalidDataException>(() =>
            Read("<html xmlns='http://www.w3.org/2000/svg'><body/></html>"));
    }

    [AvaloniaFact]
    public void AnEmptyDrawingSaysSoRatherThanLookingLikeSuccess()
    {
        var read = Read(Wrap("<defs><rect x='0' y='0' width='10' height='10'/></defs>"));

        Assert.Equal(0, read.Shapes);
        Assert.Contains("Nothing in that file", read.Summary);
    }

    [AvaloniaFact]
    public void ADrawingSurvivesBeingExportedAndReadBackIn()
    {
        // The strongest check available: write this editor's own SVG and read it in again.
        var original = Harness.Page(500, 400);

        var box = Harness.Box(original, new Rect(40, 40, 120, 60), "One");
        box.Fill = Colors.LightBlue;
        box.Stroke = Colors.DarkBlue;
        box.StrokeThickness = 3;

        Harness.Box(original, new Rect(260, 40, 100, 100), "Two", ShapeKind.Ellipse);
        Harness.Box(original, new Rect(40, 200, 120, 80), "Three", ShapeKind.Triangle);

        var turned = Harness.Box(original, new Rect(260, 200, 120, 60), "Four");
        turned.Rotation = 30;

        var read = Read(SvgExporter.Export(original));

        Assert.Equal(500, read.Document.PageWidth, 1);
        Assert.Equal(400, read.Document.PageHeight, 1);

        // The white page rectangle the exporter writes comes back as a shape too.
        Assert.True(read.Shapes >= 8, $"only {read.Shapes} shapes came back");
        Assert.Empty(read.Skipped);

        // Every label survives, which is the part a user would notice first.
        var labels = read.Document.Shapes.Select(shape => shape.Text).ToList();

        foreach (var word in new[] { "One", "Two", "Three", "Four" })
            Assert.Contains(word, labels);

        // The turned one came back turned.
        Assert.Contains(read.Document.Shapes, shape => Math.Abs(shape.Rotation - 30) < 1);
    }

    [AvaloniaFact]
    public void AnImportedPathSurvivesTheFileFormat()
    {
        var read = Read(Wrap("<path d='M 10,10 C 20,0 40,0 50,10 L 50,50 Z' fill='#FF0000'/>"));

        var json = DiagramFile.ToJson(read.Document);
        Assert.Contains($"\"version\": {DiagramFile.CurrentVersion}", json);

        var back = (PathShape)DiagramFile.FromJson(json).Shapes.Single();
        var before = (PathShape)read.Document.Shapes.Single();

        Assert.Equal(before.Outline, back.Outline);
        Assert.Equal(before.Bounds, back.Bounds);
        Assert.Equal(Colors.Red, back.Fill);
    }

    [AvaloniaFact]
    public void AnOrdinaryDrawingWritesNoOutlines()
    {
        var document = Harness.Page();
        Harness.Box(document, new Rect(10, 10, 40, 40));

        Assert.DoesNotContain("outline", DiagramFile.ToJson(document));
    }
}
