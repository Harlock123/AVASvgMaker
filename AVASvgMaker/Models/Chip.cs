using System.Collections.Generic;
using System.Linq;

namespace AVASvgMaker.Models;

/// <summary>
/// A chip in a dual in-line package: what it is called, and what each of its legs does.
///
/// Pins are listed in the order they are numbered, which for a DIP means down the left side
/// from the top and then back up the right - so pin 1 is top-left and the last pin is
/// top-right, with the notch between them. An inverted signal is written with a leading
/// slash, "/CLR" rather than an overbar, because an overbar is a combining character and
/// renders differently in every font it meets.
/// </summary>
public record Chip(ShapeKind Kind, string Name, string Keywords, params string[] Pins)
{
    public int Count => Pins.Length;

    /// <summary>How many legs run down each side. A DIP has the same number on both.</summary>
    public int PerSide => Pins.Length / 2;
}

/// <summary>
/// The chips the toolbox offers. Pinouts are from the datasheets and are the whole point of
/// the shape: a 7400 whose pin 7 is not ground is worse than no 7400 at all.
/// </summary>
public static class ChipCatalogue
{
    public static readonly IReadOnlyList<Chip> All =
    [
        new(ShapeKind.Chip555, "555", "timer 555 oscillator monostable astable ne555",
            "GND", "TRIG", "OUT", "/RESET", "CTRL", "THR", "DIS", "VCC"),

        new(ShapeKind.Chip741, "741", "opamp op-amp amplifier 741 ua741 operational",
            "OFF1", "IN-", "IN+", "V-", "OFF2", "OUT", "V+", "NC"),

        new(ShapeKind.Chip7400, "7400", "nand gate quad logic ttl 74xx 74hc00",
            "1A", "1B", "1Y", "2A", "2B", "2Y", "GND",
            "3Y", "3A", "3B", "4Y", "4A", "4B", "VCC"),

        new(ShapeKind.Chip7402, "7402", "nor gate quad logic ttl 74xx 74hc02",
            "1Y", "1A", "1B", "2Y", "2A", "2B", "GND",
            "3A", "3B", "3Y", "4A", "4B", "4Y", "VCC"),

        new(ShapeKind.Chip7404, "7404", "inverter not hex gate logic ttl 74xx 74hc04",
            "1A", "1Y", "2A", "2Y", "3A", "3Y", "GND",
            "4Y", "4A", "5Y", "5A", "6Y", "6A", "VCC"),

        new(ShapeKind.Chip7408, "7408", "and gate quad logic ttl 74xx 74hc08",
            "1A", "1B", "1Y", "2A", "2B", "2Y", "GND",
            "3Y", "3A", "3B", "4Y", "4A", "4B", "VCC"),

        new(ShapeKind.Chip7432, "7432", "or gate quad logic ttl 74xx 74hc32",
            "1A", "1B", "1Y", "2A", "2B", "2Y", "GND",
            "3Y", "3A", "3B", "4Y", "4A", "4B", "VCC"),

        new(ShapeKind.Chip7474, "7474", "flip-flop flipflop dual d latch clock logic 74xx",
            "1/CLR", "1D", "1CLK", "1/PRE", "1Q", "1/Q", "GND",
            "2/Q", "2Q", "2/PRE", "2CLK", "2D", "2/CLR", "VCC"),

        new(ShapeKind.Chip7486, "7486", "xor exclusive or gate quad logic ttl 74xx 74hc86",
            "1A", "1B", "1Y", "2A", "2B", "2Y", "GND",
            "3Y", "3A", "3B", "4Y", "4A", "4B", "VCC"),

        new(ShapeKind.Chip74138, "74138", "decoder demultiplexer 3 to 8 logic 74xx 74hc138",
            "A", "B", "C", "/G2A", "/G2B", "G1", "Y7", "GND",
            "Y6", "Y5", "Y4", "Y3", "Y2", "Y1", "Y0", "VCC"),

        new(ShapeKind.Chip74595, "74595", "shift register serial parallel latch 74xx 74hc595",
            "Q1", "Q2", "Q3", "Q4", "Q5", "Q6", "Q7", "GND",
            "Q7S", "/MR", "SHCP", "STCP", "/OE", "DS", "Q0", "VCC"),

        // Blanks, for a chip that is not in the list. The legs are numbered and nothing else
        // is claimed about them - which is better than offering a pinout that is almost right.
        new(ShapeKind.Dip8, "DIP-8", "chip ic dip 8 pin blank generic package socket",
            Numbers(8)),

        new(ShapeKind.Dip14, "DIP-14", "chip ic dip 14 pin blank generic package socket",
            Numbers(14)),

        new(ShapeKind.Dip16, "DIP-16", "chip ic dip 16 pin blank generic package socket",
            Numbers(16))
    ];

    private static string[] Numbers(int count) =>
        Enumerable.Range(1, count).Select(n => n.ToString()).ToArray();

    private static readonly Dictionary<ShapeKind, Chip> ByKind =
        All.ToDictionary(chip => chip.Kind);

    public static Chip? Find(ShapeKind kind) =>
        ByKind.TryGetValue(kind, out var chip) ? chip : null;

    public static bool Is(ShapeKind kind) => ByKind.ContainsKey(kind);
}
