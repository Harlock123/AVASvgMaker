using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using AVASvgMaker.Views;
using Xunit;

namespace AVASvgMaker.Tests;

/// <summary>
/// The chips, whose whole reason for existing is the pins: a named connection point per leg
/// that a wire can be drawn to and that stays put when the chip is moved. Pinouts are copied
/// from datasheets by hand, so what is checked here is the shape of the mistakes that makes -
/// a pinout with an odd number of legs, a power pin in the wrong place, a part number sitting
/// on top of the pin names.
///
/// Where a pin lands on its leg is checked in <see cref="ShapeTests"/>, next to the same rule
/// for every other shape.
/// </summary>
public class ChipTests
{
    private static Rect At(Chip chip, Point where)
    {
        var size = ChipShape.PreferredSize(chip);
        return new Rect(where.X, where.Y, size.Width, size.Height);
    }

    [AvaloniaFact]
    public void TheyAreAllThere()
    {
        Assert.Equal(14, ChipCatalogue.All.Count);
        Assert.Equal("Electronic", StencilCatalogue.CategoryName(StencilCategory.Electronic));

        // Every chip is offered in the toolbox, and nothing else is in that drawer.
        Assert.Equal(
            ChipCatalogue.All.Select(chip => chip.Kind).OrderBy(k => k),
            StencilCatalogue.InCategory(StencilCategory.Electronic).Select(s => s.Kind).OrderBy(k => k));
    }

    /// <summary>A dual in-line package has the same number of legs down both sides.</summary>
    [AvaloniaFact]
    public void EveryPinoutIsAWholePackage()
    {
        foreach (var chip in ChipCatalogue.All)
        {
            Assert.True(chip.Count is 8 or 14 or 16, $"{chip.Name} has {chip.Count} pins");
            Assert.Equal(chip.Count, chip.PerSide * 2);

            foreach (var pin in chip.Pins)
                Assert.False(string.IsNullOrWhiteSpace(pin), $"{chip.Name} has a nameless pin");

            Assert.True(chip.Keywords.Split(' ').Length >= 4, $"{chip.Name} is thin on keywords");
            Assert.True(ChipCatalogue.Is(chip.Kind));
            Assert.Equal(chip, ChipCatalogue.Find(chip.Kind));
        }
    }

    /// <summary>
    /// On the 74xx series power is always the same two legs - ground at the bottom of the left
    /// side, supply at the top of the right - and a pinout that has them anywhere else has been
    /// typed in the wrong order.
    /// </summary>
    [AvaloniaFact]
    public void TheLogicFamilyHasPowerWhereTheLogicFamilyHasPower()
    {
        var logic = ChipCatalogue.All.Where(chip => chip.Keywords.Contains("74xx")).ToList();

        Assert.Equal(9, logic.Count);

        foreach (var chip in logic)
        {
            Assert.Equal("GND", chip.Pins[chip.PerSide - 1]);
            Assert.Equal("VCC", chip.Pins[^1]);
        }
    }

    /// <summary>An inverted signal is written with a slash, and never with an overbar.</summary>
    [AvaloniaFact]
    public void InvertedSignalsAreWrittenWithASlash()
    {
        var inverted = ChipCatalogue.All
            .SelectMany(chip => chip.Pins)
            .Where(pin => pin.Contains('/'))
            .ToList();

        Assert.Contains("/RESET", inverted);
        Assert.Contains("/OE", inverted);

        foreach (var chip in ChipCatalogue.All)
        foreach (var pin in chip.Pins)
            Assert.DoesNotContain('̅', pin);
    }

    /// <summary>
    /// The size a chip is dropped at has to hold the longest pin name on both sides with the
    /// part number still clear between them. This is the arithmetic the drawing does, done
    /// again from the outside: pin font, package width, the gutter the names need.
    /// </summary>
    [AvaloniaFact]
    public void TheSizeItIsDroppedAtFitsTheNamesAndThePartNumber()
    {
        foreach (var chip in ChipCatalogue.All)
        {
            var size = ChipShape.PreferredSize(chip);
            var package = size.Width - 2 * Math.Clamp(size.Width * 0.14, 4, 20);

            var pinSize = Math.Clamp(size.Height / chip.PerSide * 0.55, 5, 10);
            var names = chip.Pins.Max(pin => Measure(pin, pinSize));
            var part = Measure(chip.Name, 13);

            Assert.True(names * 2 + part + 16 <= package,
                $"{chip.Name} is {package:0.#} wide, and needs {names * 2 + part + 16:0.#} " +
                "for its names and its part number");

            // And a row per pin, tall enough for the name in it to be read.
            Assert.True(size.Height / chip.PerSide >= 9, $"{chip.Name}'s rows are too shallow");
        }
    }

    private static double Measure(string text, double size) => new FormattedText(
        text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
        Typeface.Default, size, Brushes.Black).Width;

    /// <summary>The legs are outside the package, which is where a wire is supposed to meet them.</summary>
    [AvaloniaFact]
    public void TheLegsStickOutAndTheBodyIsWhatYouClick()
    {
        var chip = ChipCatalogue.Find(ShapeKind.Chip555)!;
        var shape = ShapeFactory.Create(chip.Kind, At(chip, new Point(100, 100)));

        Assert.True(shape.HitTest(shape.Bounds.Center), "the body cannot be clicked");

        foreach (var pin in shape.ConnectionPoints)
            Assert.False(shape.HitTest(pin), "a leg tip is inside the package");
    }

    /// <summary>
    /// The point of the whole exercise: a wire drawn to pin 4 is on pin 4 afterwards, wherever
    /// the chip has been dragged to.
    /// </summary>
    [AvaloniaFact]
    public void AWireStaysOnItsPinWhenTheChipMoves()
    {
        var chip = ChipCatalogue.Find(ShapeKind.Chip555)!;
        var document = Harness.Page(600, 400);

        var timer = ShapeFactory.Create(chip.Kind, At(chip, new Point(320, 120)));
        document.Add(timer);

        var box = Harness.Box(document, new Rect(60, 200, 90, 50), "R1");

        const int reset = 3;
        Assert.Equal("/RESET", chip.Pins[reset]);

        var wire = Harness.Join(document, box, 1, timer, reset);
        document.RouteConnectors();

        Assert.Equal(timer.ConnectionPoints[reset], wire.Path[^1]);

        timer.Translate(new Vector(-40, 90));
        document.RouteConnectors();

        Assert.Equal(reset, wire.EndPort);
        Assert.Equal(timer.ConnectionPoints[reset], wire.Path[^1]);

        // And the wire arrives sideways, along the leg, rather than across the body.
        Assert.Equal(0, timer.ConnectionDirection(reset).Y, 3);
    }

    /// <summary>Turn the chip and the pins turn with it, still one per leg, still facing out.</summary>
    [AvaloniaFact]
    public void TurningTheChipTurnsThePins()
    {
        var chip = ChipCatalogue.Find(ShapeKind.Chip7400)!;
        var shape = ShapeFactory.Create(chip.Kind, At(chip, new Point(100, 100)));

        var before = shape.ConnectionPoints.ToList();

        shape.Rotation = 90;

        var after = shape.ConnectionPoints;

        Assert.Equal(chip.Count, after.Count);
        Assert.NotEqual(before[0], after[0]);

        // Pin 1's leg pointed left; a quarter turn clockwise has it pointing up the page.
        var direction = shape.ConnectionDirection(0);

        Assert.Equal(0, direction.X, 3);
        Assert.Equal(-1, direction.Y, 3);

        // The same distance from the middle as before - a turn, not a rescaling.
        var centre = shape.Bounds.Center;

        for (var pin = 0; pin < chip.Count; pin++)
            Assert.Equal(
                Vector.Distance(before[pin], centre), Vector.Distance(after[pin], centre), 3);
    }

    /// <summary>
    /// A chip in a toolbox tile is an inch of nothing: two columns of pin names will not fit
    /// in it and used to be drawn on top of each other and on the part number. Below the size
    /// they fit at, the names are left off and the package speaks for itself.
    /// </summary>
    [AvaloniaFact]
    public void ASmallChipLeavesOffThePinNames()
    {
        var chip = ChipCatalogue.Find(ShapeKind.Chip555)!;

        var tile = ShapeFactory.Create(chip.Kind, new Rect(3, 3, 54, 58));
        var dropped = ShapeFactory.Create(chip.Kind, At(chip, new Point(3, 3)));

        Assert.DoesNotContain("TRIG", Svg(tile));
        Assert.Contains("TRIG", Svg(dropped));

        // The part number is still there either way - it is what says which chip it is.
        Assert.Contains("555", Svg(tile));
    }

    /// <summary>The part number gives way to the pin names rather than being drawn over them.</summary>
    [AvaloniaFact]
    public void ThePartNumberShrinksToWhatIsLeftOfThePackage()
    {
        var chip = ChipCatalogue.Find(ShapeKind.Chip74595)!;

        var squeezed = Svg(ShapeFactory.Create(chip.Kind, new Rect(0, 0, 40, 200)));
        var roomy = Svg(ShapeFactory.Create(chip.Kind, At(chip, new Point(0, 0))));

        Assert.Contains("font-size=\"13\"", roomy);
        Assert.DoesNotContain("font-size=\"13\"", squeezed);
    }

    /// <summary>One shape's worth of SVG, without the page around it.</summary>
    private static string Svg(DiagramShape shape)
    {
        var document = Harness.Page(400, 400);
        document.Add(shape);

        return SvgExporter.Export(document);
    }

    [AvaloniaFact]
    public void TheySurviveSavingAndLoadingAndGoOutAsSvg()
    {
        var document = Harness.Page(1200, 900);

        foreach (var (chip, i) in ChipCatalogue.All.Select((c, i) => (c, i)))
            document.Add(ShapeFactory.Create(
                chip.Kind, At(chip, new Point(20 + i % 4 * 260, 20 + i / 4 * 220))));

        var back = DiagramFile.FromJson(DiagramFile.ToJson(document));

        Assert.Equal(
            ChipCatalogue.All.Select(chip => chip.Kind).OrderBy(k => k),
            back.Shapes.Select(shape => shape.Kind).OrderBy(k => k));

        // The part number comes back, and so do the pins it implies.
        var timer = (ChipShape)back.Shapes.First(shape => shape.Kind == ShapeKind.Chip555);

        Assert.Equal("555", timer.Text);
        Assert.Equal(8, timer.ConnectionPoints.Count);

        var svg = SvgExporter.Export(document);

        Assert.DoesNotContain("NaN", svg);
        Assert.Contains("TRIG", svg);

        // One pin-1 notch per chip, and it is an arc rather than a straight line.
        Assert.Equal(ChipCatalogue.All.Count, Occurrences(svg, "<path d=\"M "));
    }

    private static int Occurrences(string text, string find) =>
        text.Split(find).Length - 1;

    /// <summary>
    /// The drawer is actually in the toolbox with a tile per chip in it. A chip carries no
    /// outline data - the shape draws itself - so a tile that came out empty would be the way
    /// that showed.
    /// </summary>
    [AvaloniaFact]
    public void TheToolboxHasADrawerOfThem()
    {
        var (window, _) = Harness.Editor();
        var toolbox = window.GetVisualDescendants().OfType<ToolboxPanel>().Single();

        var drawer = toolbox.GetVisualDescendants().OfType<Expander>()
            .Single(expander => (expander.Header as string) == "Electronic");

        drawer.IsExpanded = true;
        Harness.Settle(window);

        var tiles = drawer.GetVisualDescendants().OfType<ShapePreview>()
            .Select(tile => tile.Kind)
            .ToList();

        Assert.Equal(ChipCatalogue.All.Select(chip => chip.Kind).OrderBy(k => k), tiles.OrderBy(k => k));
    }

    /// <summary>Searching the toolbox the way somebody looking for a chip would.</summary>
    [AvaloniaFact]
    public void TheyAreFoundByTheWordsPeopleUse()
    {
        foreach (var (search, expected) in new[]
                 {
                     ("555", ShapeKind.Chip555),
                     ("timer", ShapeKind.Chip555),
                     ("opamp", ShapeKind.Chip741),
                     ("nand", ShapeKind.Chip7400),
                     ("inverter", ShapeKind.Chip7404),
                     ("flip-flop", ShapeKind.Chip7474),
                     ("decoder", ShapeKind.Chip74138),
                     ("shift", ShapeKind.Chip74595),
                     ("socket", ShapeKind.Dip16)
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
