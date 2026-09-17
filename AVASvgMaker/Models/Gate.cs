using System.Collections.Generic;
using System.Linq;

namespace AVASvgMaker.Models;

/// <summary>
/// A logic gate drawn as its own distinctive shape, with a lead per input and one for the
/// output - the ANSI symbols, the ones a circuit is drawn with when the packages are not the
/// point and the logic is.
/// </summary>
/// <param name="Kind">Identity, and what gets written to the file.</param>
/// <param name="Name">The label in the toolbox.</param>
/// <param name="Keywords">Extra words the toolbox search should match.</param>
/// <param name="Body">The outline, in the unit square, as every stencil is written.</param>
/// <param name="Inputs">How many input leads run into the back of it.</param>
/// <param name="Bubble">An inverting output: the small circle between the body and the lead.</param>
/// <param name="Curved">A curved back, as the OR family has - the input leads reach the curve.</param>
/// <param name="Extra">The second back arc that tells an XOR from an OR.</param>
/// <param name="Enable">An enable lead into the top, as a tri-state buffer has.</param>
public record Gate(
    ShapeKind Kind,
    string Name,
    string Keywords,
    string Body,
    int Inputs = 2,
    bool Bubble = false,
    bool Curved = false,
    bool Extra = false,
    bool Enable = false)
{
    /// <summary>Inputs, then the output, then the enable if it has one.</summary>
    public int Count => Inputs + 1 + (Enable ? 1 : 0);

    public int Output => Inputs;
}

/// <summary>
/// The gates the toolbox offers. The three bodies are written once each: the AND's flat back
/// and round front, the OR's curved back and pointed front, and the triangle a buffer is.
/// Everything else - the inverting bubble, the XOR's second arc, a third input - is the same
/// body with something added to it, which is also how the symbols themselves are taught.
/// </summary>
public static class GateCatalogue
{
    private const string And =
        "M 0,0 L 0.42,0 C 0.79,0 1,0.22 1,0.5 C 1,0.78 0.79,1 0.42,1 L 0,1 Z";

    private const string Or =
        "M 0,0 Q 0.38,0.5 0,1 Q 0.62,0.94 1,0.5 Q 0.62,0.06 0,0 Z";

    private const string Triangle = "M 0,0 L 1,0.5 L 0,1 Z";

    /// <summary>The XOR's outer arc, drawn to the same curve as the body's own back.</summary>
    public const string BackArc = "M 0,0 Q 0.38,0.5 0,1";

    public static readonly IReadOnlyList<Gate> All =
    [
        new(ShapeKind.GateAnd, "AND", "gate logic and conjunction 7408 ttl cmos", And),

        new(ShapeKind.GateNand, "NAND", "gate logic nand inverted and 7400 ttl cmos", And,
            Bubble: true),

        new(ShapeKind.GateOr, "OR", "gate logic or disjunction 7432 ttl cmos", Or,
            Curved: true),

        new(ShapeKind.GateNor, "NOR", "gate logic nor inverted or 7402 ttl cmos", Or,
            Bubble: true, Curved: true),

        new(ShapeKind.GateXor, "XOR", "gate logic xor exclusive or 7486 ttl cmos parity", Or,
            Curved: true, Extra: true),

        new(ShapeKind.GateXnor, "XNOR", "gate logic xnor exclusive nor equivalence 74266", Or,
            Bubble: true, Curved: true, Extra: true),

        new(ShapeKind.GateAnd3, "AND (3-input)", "gate logic and three input 7411 7410 ttl", And,
            Inputs: 3),

        new(ShapeKind.GateNand3, "NAND (3-input)", "gate logic nand three input 7410 ttl cmos", And,
            Inputs: 3, Bubble: true),

        new(ShapeKind.GateOr3, "OR (3-input)", "gate logic or three input 4075 ttl cmos", Or,
            Inputs: 3, Curved: true),

        new(ShapeKind.GateBuffer, "Buffer", "gate logic buffer driver non-inverting 7407 ttl",
            Triangle, Inputs: 1),

        new(ShapeKind.GateInverter, "Inverter", "gate logic inverter not invert bubble 7404 ttl",
            Triangle, Inputs: 1, Bubble: true),

        new(ShapeKind.GateTriState, "Tri-state buffer",
            "gate logic buffer tristate three state enable bus 74245 74125",
            Triangle, Inputs: 1, Enable: true)
    ];

    private static readonly Dictionary<ShapeKind, Gate> ByKind = All.ToDictionary(gate => gate.Kind);

    public static Gate? Find(ShapeKind kind) => ByKind.TryGetValue(kind, out var gate) ? gate : null;

    public static bool Is(ShapeKind kind) => ByKind.ContainsKey(kind);
}
