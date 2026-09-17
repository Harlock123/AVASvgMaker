using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using Xunit;

namespace AVASvgMaker.Tests;

/// <summary>
/// The electrical symbols, and the rules every stencil in the catalogue has to keep. Path data
/// is written by hand, so what is checked here is the sort of thing a typo produces: a symbol
/// that draws outside its own box, or a name in the enum with nothing behind it.
/// </summary>
public class ElectricalStencilTests
{
    private static IReadOnlyList<Stencil> Electrical =>
        StencilCatalogue.InCategory(StencilCategory.Electrical).ToList();

    /// <summary>
    /// What a path actually covers, in the unit square the outlines are written in.
    ///
    /// The drawn extent rather than the points the data names: a curve is steered by control
    /// points that sit outside it, and the cloud's are a couple of per cent past the edge on
    /// purpose. What matters is where the ink lands.
    /// </summary>
    private static Rect Covers(string data) =>
        StencilPath.ToGeometry(data, new Rect(0, 0, 1, 1)).Bounds;

    [AvaloniaFact]
    public void TheyAreAllThere()
    {
        Assert.Equal(20, Electrical.Count);
        Assert.Equal("Electrical", StencilCatalogue.CategoryName(StencilCategory.Electrical));
    }

    /// <summary>
    /// A symbol that draws outside its box overlaps its neighbours on the page and is clipped
    /// in its own toolbox tile. This is the check that a stray digit fails.
    /// </summary>
    [AvaloniaFact]
    public void EveryStencilStaysInsideItsOwnBox()
    {
        foreach (var stencil in StencilCatalogue.All)
        foreach (var data in new[] { stencil.Outline, stencil.Detail })
        {
            if (data is null)
                continue;

            var covers = Covers(data);

            Assert.True(
                covers.X >= -0.01 && covers.Y >= -0.01 &&
                covers.Right <= 1.01 && covers.Bottom <= 1.01,
                $"{stencil.Name} covers {covers}, which is outside its box");
        }
    }

    /// <summary>Something to draw, and something to find it by.</summary>
    [AvaloniaFact]
    public void EachHasAnOutlineOrSomeLineWorkAndKeywords()
    {
        foreach (var stencil in Electrical)
        {
            Assert.True(stencil.Outline is not null || stencil.Detail is not null,
                $"{stencil.Name} has nothing to draw");

            Assert.Contains("electric", stencil.Keywords, StringComparison.OrdinalIgnoreCase);
            Assert.True(stencil.Keywords.Split(' ').Length >= 3, $"{stencil.Name} is thin on keywords");
        }
    }

    /// <summary>Searching the toolbox the way somebody would.</summary>
    [AvaloniaFact]
    public void TheyAreFoundByTheWordsPeopleUse()
    {
        foreach (var (search, expected) in new[]
                 {
                     ("resistor", ShapeKind.Resistor),
                     ("ohm", ShapeKind.Resistor),
                     ("coil", ShapeKind.Inductor),
                     ("earth", ShapeKind.Ground),
                     ("opamp", ShapeKind.Amplifier),
                     ("bulb", ShapeKind.Lamp),
                     ("npn", ShapeKind.Transistor),
                     ("mains", ShapeKind.AcSource)
                 })
        {
            var found = StencilCatalogue.All.Where(s => StencilCatalogue.Matches(s, search)).ToList();

            Assert.Contains(expected, found.Select(s => s.Kind));
        }
    }

    /// <summary>
    /// Most of these have no body at all - a resistor is a zigzag - and a shape with nothing
    /// filled in still has to be clickable, or it cannot be selected once it is on the page.
    /// </summary>
    [AvaloniaFact]
    public void OneWithNoBodyIsStillClickable()
    {
        var strokeOnly = Electrical.Where(stencil => stencil.Outline is null).ToList();

        Assert.NotEmpty(strokeOnly);

        foreach (var stencil in strokeOnly)
        {
            var shape = ShapeFactory.Create(stencil.Kind, new Rect(100, 100, 90, 60));

            Assert.True(shape.HitTest(new Point(145, 130)), $"{stencil.Name} cannot be clicked");
        }
    }

    /// <summary>Every kind in the enum has an entry, and every entry can be made.</summary>
    [AvaloniaFact]
    public void NothingIsOrphaned()
    {
        foreach (var stencil in Electrical)
        {
            Assert.NotNull(StencilCatalogue.Find(stencil.Kind));

            var shape = ShapeFactory.Create(stencil.Kind, new Rect(0, 0, 90, 60));
            Assert.Equal(stencil.Kind, shape.Kind);
        }
    }

    /// <summary>The formatting a symbol needs to mean what it means is applied when placed.</summary>
    [AvaloniaFact]
    public void TheHollowOnesComeOutHollow()
    {
        foreach (var kind in new[]
                 { ShapeKind.AcSource, ShapeKind.DcSource, ShapeKind.Lamp,
                   ShapeKind.Motor, ShapeKind.Fuse, ShapeKind.Amplifier, ShapeKind.Transistor })
        {
            var stencil = StencilCatalogue.Find(kind)!;
            var shape = ShapeFactory.Create(kind, new Rect(0, 0, 90, 60));

            stencil.ApplyDefaults(shape);

            Assert.Equal(0, shape.Fill.A);
        }
    }

    [AvaloniaFact]
    public void TheySurviveSavingAndLoadingAndGoOutAsSvg()
    {
        var document = Harness.Page(900, 700);

        foreach (var (stencil, i) in Electrical.Select((s, i) => (s, i)))
        {
            var shape = ShapeFactory.Create(stencil.Kind, new Rect(20 + i % 5 * 120, 20 + i / 5 * 90, 90, 60));
            stencil.ApplyDefaults(shape);
            document.Add(shape);
        }

        var back = DiagramFile.FromJson(DiagramFile.ToJson(document));

        Assert.Equal(
            Electrical.Select(s => s.Kind).OrderBy(k => k),
            back.Shapes.Select(s => s.Kind).OrderBy(k => k));

        var svg = SvgExporter.Export(document);

        Assert.Contains("<path", svg);
        Assert.DoesNotContain("NaN", svg);
    }
}
