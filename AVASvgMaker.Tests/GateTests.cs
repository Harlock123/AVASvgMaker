using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using AVASvgMaker.Views;
using Xunit;

namespace AVASvgMaker.Tests;

/// <summary>
/// The logic gates. The bodies are hand-written curves, so what is checked here is what a
/// wrong digit in one of them produces - a symbol drawn outside its own box, an input lead
/// hanging in the air short of a curved back, a bubble on a gate that does not invert.
///
/// The leads matter more than the outline. A gate's inputs are not interchangeable to look
/// at: a wire drawn to the upper input of an adder's XOR has to still be on the upper input
/// after the gate has been dragged, or the drawing has quietly changed what it says.
/// </summary>
public class GateTests
{
    private static Rect At(Gate gate, Point where)
    {
        var size = GateShape.PreferredSize(gate);
        return new Rect(where.X, where.Y, size.Width, size.Height);
    }

    private static string Svg(DiagramShape shape)
    {
        var document = Harness.Page(400, 400);
        document.Add(shape);

        return SvgExporter.Export(document);
    }

    /// <summary>The line elements of a shape's SVG, which is where its leads are.</summary>
    private static List<(Point From, Point To)> Lines(string svg) =>
        Regex.Matches(svg, "<line x1=\"([-0-9.]+)\" y1=\"([-0-9.]+)\" x2=\"([-0-9.]+)\" y2=\"([-0-9.]+)\"")
            .Select(m =>
            {
                var n = m.Groups.Values.Skip(1)
                    .Select(g => double.Parse(g.Value, CultureInfo.InvariantCulture)).ToArray();

                return (new Point(n[0], n[1]), new Point(n[2], n[3]));
            })
            .ToList();

    [AvaloniaFact]
    public void TheyAreAllThere()
    {
        Assert.Equal(12, GateCatalogue.All.Count);
        Assert.Equal("Logic gates", StencilCatalogue.CategoryName(StencilCategory.Logic));

        Assert.Equal(
            GateCatalogue.All.Select(gate => gate.Kind).OrderBy(k => k),
            StencilCatalogue.InCategory(StencilCategory.Logic).Select(s => s.Kind).OrderBy(k => k));

        foreach (var gate in GateCatalogue.All)
        {
            Assert.True(GateCatalogue.Is(gate.Kind));
            Assert.Equal(gate, GateCatalogue.Find(gate.Kind));
            Assert.True(gate.Keywords.Split(' ').Length >= 4, $"{gate.Name} is thin on keywords");
            Assert.Contains("gate", gate.Keywords);
        }
    }

    /// <summary>A body that draws outside its box overlaps whatever is beside it on the page.</summary>
    [AvaloniaFact]
    public void EveryBodyStaysInsideItsOwnBox()
    {
        foreach (var gate in GateCatalogue.All)
        {
            var covers = StencilPath.ToGeometry(gate.Body, new Rect(0, 0, 1, 1)).Bounds;

            Assert.True(
                covers.X >= -0.01 && covers.Y >= -0.01 &&
                covers.Right <= 1.01 && covers.Bottom <= 1.01,
                $"{gate.Name} covers {covers}, which is outside its box");
        }
    }

    /// <summary>
    /// Inputs down the back facing backwards, one output on the front facing forwards, and an
    /// enable on top where there is one - in that order, because that order is what a saved
    /// file holds.
    /// </summary>
    [AvaloniaFact]
    public void EveryGateHasALeadPerInputAndOneForTheOutput()
    {
        var box = new Rect(100, 100, 90, 60);

        foreach (var gate in GateCatalogue.All)
        {
            var shape = ShapeFactory.Create(gate.Kind, box);
            var pins = shape.ConnectionPoints;

            Assert.Equal(gate.Inputs + 1 + (gate.Enable ? 1 : 0), pins.Count);

            for (var input = 0; input < gate.Inputs; input++)
            {
                Assert.Equal(box.X, pins[input].X, 1);

                Assert.True(pins[input].Y > box.Y && pins[input].Y < box.Bottom,
                    $"{gate.Name} input {input + 1} is off the back of it");

                Assert.Equal(-1, shape.ConnectionDirection(input).X, 3);
                Assert.Equal(0, shape.ConnectionDirection(input).Y, 3);
            }

            // Evenly spaced down the back, and never two on the same line.
            var down = Enumerable.Range(0, gate.Inputs).Select(i => pins[i].Y).ToList();

            Assert.Equal(down.OrderBy(y => y), down);
            Assert.Equal(down.Count, down.Distinct().Count());

            // The output is on the front, halfway down, whatever the body is.
            var output = pins[gate.Output];

            Assert.Equal(box.Right, output.X, 1);
            Assert.Equal(box.Center.Y, output.Y, 1);
            Assert.Equal(1, shape.ConnectionDirection(gate.Output).X, 3);
            Assert.Equal(0, shape.ConnectionDirection(gate.Output).Y, 3);

            if (!gate.Enable)
                continue;

            // An enable comes down into the top of it.
            Assert.Equal(box.Y, pins[^1].Y, 1);
            Assert.Equal(0, shape.ConnectionDirection(pins.Count - 1).X, 3);
            Assert.Equal(-1, shape.ConnectionDirection(pins.Count - 1).Y, 3);
        }
    }

    /// <summary>
    /// The bubble is what tells a NAND from an AND, and it belongs between the body and the
    /// output lead - not on the end of it, where the wire has to meet the gate.
    /// </summary>
    [AvaloniaFact]
    public void TheInvertingOnesHaveABubbleAndTheRestDoNot()
    {
        var inverting = new[]
        {
            ShapeKind.GateNand, ShapeKind.GateNor, ShapeKind.GateXnor,
            ShapeKind.GateNand3, ShapeKind.GateInverter
        };

        foreach (var gate in GateCatalogue.All)
        {
            var svg = Svg(ShapeFactory.Create(gate.Kind, At(gate, new Point(40, 40))));
            var bubbles = svg.Split("<circle").Length - 1;

            Assert.Equal(gate.Bubble ? 1 : 0, bubbles);
            Assert.Equal(inverting.Contains(gate.Kind), gate.Bubble);
        }

        // And the output point is still the end of the lead, bubble or no bubble.
        foreach (var kind in new[] { ShapeKind.GateAnd, ShapeKind.GateNand })
        {
            var box = new Rect(0, 0, 90, 60);
            var shape = ShapeFactory.Create(kind, box);

            Assert.Equal(new Point(box.Right, box.Center.Y), shape.ConnectionPoints[2]);
        }
    }

    /// <summary>
    /// The OR family's back is a curve, and its input leads run on to meet it rather than
    /// stopping at the edge of the box and leaving a gap nobody drew.
    /// </summary>
    [AvaloniaFact]
    public void TheCurvedBackIsMetByItsInputLeads()
    {
        var box = new Rect(0, 0, 90, 60);

        var flat = Lines(Svg(ShapeFactory.Create(ShapeKind.GateAnd, box)));
        var curved = Lines(Svg(ShapeFactory.Create(ShapeKind.GateOr, box)));

        // The upper input of each, whose lead is the one that has to reach further on a curve.
        var straightLead = flat.First(line => line.From.Y < box.Center.Y);
        var curvedLead = curved.First(line => line.From.Y < box.Center.Y);

        var straight = straightLead.To.X - straightLead.From.X;
        var reaching = curvedLead.To.X - curvedLead.From.X;

        Assert.True(reaching > straight + 4,
            $"the OR's input lead reaches {reaching:0.#} where the AND's reaches {straight:0.#}");

        // And it stops *at* the back curve rather than short of it or through it: a step
        // further in is inside the body, a step further out is not.
        var body = ShapeFactory.Create(ShapeKind.GateOr, box).CreateGeometry();

        Assert.True(body.FillContains(curvedLead.To + new Vector(1.5, 0)),
            "the lead stops short of the curved back");

        Assert.False(body.FillContains(curvedLead.To - new Vector(1.5, 0)),
            "the lead runs on past the curved back");
    }

    /// <summary>
    /// The point of the leads: a wire drawn to the lower input of a gate is on the lower input
    /// afterwards, wherever the gate has been dragged to.
    /// </summary>
    [AvaloniaFact]
    public void AWireStaysOnTheInputItWasDrawnTo()
    {
        var gate = GateCatalogue.Find(ShapeKind.GateAnd)!;
        var document = Harness.Page(600, 400);

        var and = ShapeFactory.Create(gate.Kind, At(gate, new Point(360, 180)));
        document.Add(and);

        var source = Harness.Box(document, new Rect(60, 60, 110, 50), "B");

        const int lower = 1;
        var wire = Harness.Join(document, source, 2, and, lower);
        document.RouteConnectors();

        Assert.Equal(and.ConnectionPoints[lower], wire.Path[^1]);

        and.Translate(new Vector(-60, 120));
        document.RouteConnectors();

        Assert.Equal(lower, wire.EndPort);
        Assert.Equal(and.ConnectionPoints[lower], wire.Path[^1]);

        // Still the lower of the two, and still arriving level with it.
        Assert.True(and.ConnectionPoints[lower].Y > and.ConnectionPoints[0].Y);
        Assert.Equal(0, and.ConnectionDirection(lower).Y, 3);
    }

    [AvaloniaFact]
    public void TurningAGateTurnsItsLeads()
    {
        var gate = GateCatalogue.Find(ShapeKind.GateNand)!;
        var shape = ShapeFactory.Create(gate.Kind, At(gate, new Point(100, 100)));

        var before = shape.ConnectionPoints.ToList();

        shape.Rotation = 90;

        Assert.Equal(before.Count, shape.ConnectionPoints.Count);
        Assert.NotEqual(before[0], shape.ConnectionPoints[0]);

        // An input faced backwards; a quarter turn clockwise has it facing up the page.
        Assert.Equal(0, shape.ConnectionDirection(0).X, 3);
        Assert.Equal(-1, shape.ConnectionDirection(0).Y, 3);

        // And the output, which faced forwards, now faces down.
        Assert.Equal(0, shape.ConnectionDirection(gate.Output).X, 3);
        Assert.Equal(1, shape.ConnectionDirection(gate.Output).Y, 3);
    }

    /// <summary>A gate is wider than it is tall, and a third input makes it taller.</summary>
    [AvaloniaFact]
    public void TheSizeItIsDroppedAtSuitsTheNumberOfInputs()
    {
        foreach (var gate in GateCatalogue.All)
        {
            var size = GateShape.PreferredSize(gate);

            Assert.True(size.Width > size.Height, $"{gate.Name} is not wider than it is tall");
            Assert.True(size.Height / (gate.Inputs + 1) >= 12,
                $"{gate.Name} has its inputs too close together to wire");
        }

        Assert.True(
            GateShape.PreferredSize(GateCatalogue.Find(ShapeKind.GateAnd3)!).Height >
            GateShape.PreferredSize(GateCatalogue.Find(ShapeKind.GateAnd)!).Height);
    }

    [AvaloniaFact]
    public void TheySurviveSavingAndLoadingAndGoOutAsSvg()
    {
        var document = Harness.Page(900, 700);

        foreach (var (gate, i) in GateCatalogue.All.Select((g, i) => (g, i)))
            document.Add(ShapeFactory.Create(
                gate.Kind, At(gate, new Point(20 + i % 4 * 200, 20 + i / 4 * 160))));

        var back = DiagramFile.FromJson(DiagramFile.ToJson(document));

        Assert.Equal(
            GateCatalogue.All.Select(gate => gate.Kind).OrderBy(k => k),
            back.Shapes.Select(shape => shape.Kind).OrderBy(k => k));

        var svg = SvgExporter.Export(document);

        Assert.DoesNotContain("NaN", svg);

        // One bubble per inverting gate, and the XOR pair's second arc on top of their bodies.
        Assert.Equal(5, svg.Split("<circle").Length - 1);
        Assert.Equal(GateCatalogue.All.Count + 2, svg.Split("<path").Length - 1);
    }

    /// <summary>The drawer is in the toolbox, with a tile per gate.</summary>
    [AvaloniaFact]
    public void TheToolboxHasADrawerOfThem()
    {
        var (window, _) = Harness.Editor();
        var toolbox = window.GetVisualDescendants().OfType<ToolboxPanel>().Single();

        var drawer = toolbox.GetVisualDescendants().OfType<Expander>()
            .Single(expander => (expander.Header as string) == "Logic gates");

        drawer.IsExpanded = true;
        Harness.Settle(window);

        Assert.Equal(
            GateCatalogue.All.Select(gate => gate.Kind).OrderBy(k => k),
            drawer.GetVisualDescendants().OfType<ShapePreview>()
                .Select(tile => tile.Kind).OrderBy(k => k));
    }

    [AvaloniaFact]
    public void TheyAreFoundByTheWordsPeopleUse()
    {
        foreach (var (search, expected) in new[]
                 {
                     ("nand", ShapeKind.GateNand),
                     ("xor", ShapeKind.GateXor),
                     ("xnor", ShapeKind.GateXnor),
                     ("inverter", ShapeKind.GateInverter),
                     ("not", ShapeKind.GateInverter),
                     ("tristate", ShapeKind.GateTriState),
                     ("three input", ShapeKind.GateAnd3),
                     ("disjunction", ShapeKind.GateOr)
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
