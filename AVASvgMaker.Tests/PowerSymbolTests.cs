using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using Xunit;

namespace AVASvgMaker.Tests;

/// <summary>
/// The grounds and the supply rails. These are the first stencils whose label is part of what
/// they are - a bar on a stub does not say 5 from 3.3 - so what is checked here is that the
/// label arrives, that it is the shape's own text rather than line work, and that it sits
/// somewhere other than on top of the symbol.
/// </summary>
public class PowerSymbolTests
{
    /// <summary>The rails, with the label each is defined to arrive with.</summary>
    private static readonly (ShapeKind Kind, string Label, TextVerticalAlign Align)[] Rails =
    [
        (ShapeKind.Rail5V, "+5V", TextVerticalAlign.Top),
        (ShapeKind.Rail3V3, "+3.3V", TextVerticalAlign.Top),
        (ShapeKind.SupplyRail, "+V", TextVerticalAlign.Top),
        (ShapeKind.NegativeRail, "-5V", TextVerticalAlign.Bottom),
        (ShapeKind.SupplyFlag, "VCC", TextVerticalAlign.Top)
    ];

    /// <summary>The ones whose symbol says it all, and which arrive with nothing written on them.</summary>
    private static readonly ShapeKind[] Unlabelled =
    [
        ShapeKind.Ground, ShapeKind.DigitalGround, ShapeKind.Chassis, ShapeKind.NoConnect
    ];

    private static Rect Covers(string data) =>
        StencilPath.ToGeometry(data, new Rect(0, 0, 1, 1)).Bounds;

    [AvaloniaFact]
    public void TheyAreAllInTheElectricalDrawer()
    {
        foreach (var kind in Rails.Select(rail => rail.Kind).Concat(Unlabelled))
        {
            var stencil = StencilCatalogue.Find(kind);

            Assert.NotNull(stencil);
            Assert.Equal(StencilCategory.Electrical, stencil.Category);
        }
    }

    /// <summary>Dropped on the page, a rail is already labelled - and the label is editable text.</summary>
    [AvaloniaFact]
    public void ARailArrivesWithItsVoltageOnIt()
    {
        var (_, canvas) = Harness.Editor();

        foreach (var (kind, label, align) in Rails)
        {
            var shape = canvas.PlaceShape(kind, new Point(400, 300));

            Assert.Equal(label, shape.Text);
            Assert.Equal(align, shape.TextVerticalAlign);
        }

        foreach (var kind in Unlabelled)
            Assert.Equal(string.Empty, canvas.PlaceShape(kind, new Point(400, 300)).Text);
    }

    /// <summary>
    /// Renaming is the point of it being text: a rail is dropped as +5V and becomes +12V or
    /// VBAT, and the new name is what gets saved.
    /// </summary>
    [AvaloniaFact]
    public void ARailCanBeRenamedAndKeepsIt()
    {
        var document = Harness.Page();
        var stencil = StencilCatalogue.Find(ShapeKind.Rail5V)!;

        var rail = ShapeFactory.Create(ShapeKind.Rail5V, new Rect(100, 100, 64, 72));
        stencil.ApplyDefaults(rail);
        document.Add(rail);

        rail.Text = "+12V";

        // Placing the same stencil again does not go back over a label somebody has changed.
        stencil.ApplyDefaults(rail);
        Assert.Equal("+12V", rail.Text);

        var back = DiagramFile.FromJson(DiagramFile.ToJson(document));

        Assert.Equal("+12V", back.Shapes.Single().Text);
        Assert.Contains("+12V", SvgExporter.Export(document));
    }

    /// <summary>
    /// The label has to have somewhere to go. Each of these leaves half its box empty on the
    /// side the label sits on - a rail's line work stays low and the label goes above it.
    /// </summary>
    [AvaloniaFact]
    public void TheLabelKeepsClearOfTheLineWork()
    {
        foreach (var (kind, label, align) in Rails)
        {
            var stencil = StencilCatalogue.Find(kind)!;
            var covers = Covers(stencil.Detail!);

            if (align == TextVerticalAlign.Top)
                Assert.True(covers.Y >= 0.45,
                    $"{label} draws from {covers.Y:0.##} down, leaving no room for its label");
            else
                Assert.True(covers.Bottom <= 0.55,
                    $"{label} draws to {covers.Bottom:0.##}, leaving no room for its label");
        }
    }

    /// <summary>
    /// A rail's stub has to reach the connection point a wire attaches to, or the wire stops
    /// in mid-air a few pixels short of it.
    /// </summary>
    [AvaloniaFact]
    public void TheStubReachesThePointAWireAttachesTo()
    {
        // Round caps, so a point exactly on the end of the stub counts as on it rather than
        // as the boundary case a butt cap makes of it.
        var pen = new Pen(Brushes.Black, 4, null, PenLineCap.Round);

        foreach (var (kind, expected) in new[]
                 {
                     (ShapeKind.Rail5V, 2), (ShapeKind.Rail3V3, 2), (ShapeKind.SupplyRail, 2),
                     (ShapeKind.SupplyFlag, 2), (ShapeKind.NegativeRail, 0),
                     (ShapeKind.Ground, 0), (ShapeKind.DigitalGround, 0), (ShapeKind.Chassis, 0)
                 })
        {
            var box = new Rect(100, 100, 64, 72);
            var stencil = StencilCatalogue.Find(kind)!;
            var shape = ShapeFactory.Create(kind, box);
            var at = shape.ConnectionPoints[expected];

            var line = StencilPath.ToGeometry(stencil.Detail!, box);

            Assert.True(line.StrokeContains(pen, at),
                $"{stencil.Name}'s line work stops short of {at}");
        }
    }

    /// <summary>A wire drawn to a rail sticks to it, and comes away square.</summary>
    [AvaloniaFact]
    public void AWireGluesToTheRailAndLeavesItSquare()
    {
        var document = Harness.Page(600, 400);

        var rail = ShapeFactory.Create(ShapeKind.Rail5V, new Rect(280, 40, 64, 72));
        document.Add(rail);

        var chip = ShapeFactory.Create(ShapeKind.Chip555, new Rect(220, 220, 152, 96));
        document.Add(chip);

        // The bottom of the rail, to pin 8 - VCC - on the chip.
        var wire = Harness.Join(document, rail, 2, chip, 7);
        document.RouteConnectors();

        Assert.Equal(rail.ConnectionPoints[2], wire.Path[0]);
        Assert.Equal(chip.ConnectionPoints[7], wire.Path[^1]);

        rail.Translate(new Vector(60, 0));
        document.RouteConnectors();

        Assert.Equal(rail.ConnectionPoints[2], wire.Path[0]);
    }

    [AvaloniaFact]
    public void TheyAreFoundByTheWordsPeopleUse()
    {
        foreach (var (search, expected) in new[]
                 {
                     ("5v", ShapeKind.Rail5V),
                     ("3v3", ShapeKind.Rail3V3),
                     ("rail", ShapeKind.SupplyRail),
                     ("vee", ShapeKind.NegativeRail),
                     ("vdd", ShapeKind.SupplyFlag),
                     ("digital", ShapeKind.DigitalGround),
                     ("unused", ShapeKind.NoConnect)
                 })
        {
            var found = StencilCatalogue.All
                .Where(stencil => StencilCatalogue.Matches(stencil, search))
                .Select(stencil => stencil.Kind)
                .ToList();

            Assert.Contains(expected, found);
        }
    }
}
