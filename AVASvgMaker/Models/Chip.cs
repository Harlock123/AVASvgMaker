using System.Collections.Generic;
using System.Linq;

namespace AVASvgMaker.Models;

/// <summary>The shapes of package a chip is drawn in.</summary>
public enum ChipPackage
{
    /// <summary>Dual in-line: legs down both sides, numbered anti-clockwise from the notch.</summary>
    Dip,

    /// <summary>A regulator's tabbed package: three legs out of the bottom, left to right.</summary>
    To220
}

/// <summary>
/// A chip: what it is called, and what each of its legs does.
///
/// Pins are listed in the order they are numbered, which for a DIP means down the left side
/// from the top and then back up the right - so pin 1 is top-left and the last pin is
/// top-right, with the notch between them. An inverted signal is written with a leading
/// slash, "/CLR" rather than an overbar, because an overbar is a combining character and
/// renders differently in every font it meets.
/// </summary>
public record Chip(ShapeKind Kind, string Name, string Keywords, params string[] Pins)
{
    /// <summary>Which package it is drawn in. Nearly everything here is a DIP.</summary>
    public ChipPackage Package { get; init; } = ChipPackage.Dip;

    public int Count => Pins.Length;

    /// <summary>How many legs run down each side. A DIP has the same number on both.</summary>
    public int PerSide => Package == ChipPackage.To220 ? Pins.Length : Pins.Length / 2;
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

        new(ShapeKind.Chip4017, "4017", "counter decade johnson cmos 4000 cd4017 divider",
            "Q5", "Q1", "Q0", "Q2", "Q6", "Q7", "Q3", "VSS",
            "Q8", "Q4", "Q9", "CO", "/CE", "CLK", "RST", "VDD"),

        new(ShapeKind.Chip7447, "7447", "decoder driver bcd seven segment display 74xx 7447",
            "B", "C", "/LT", "/BI", "/RBI", "D", "A", "GND",
            "e", "d", "c", "b", "a", "g", "f", "VCC"),

        new(ShapeKind.Chip74245, "74245", "buffer transceiver octal bus three state 74xx 74hc245",
            "DIR", "A1", "A2", "A3", "A4", "A5", "A6", "A7", "A8", "GND",
            "B8", "B7", "B6", "B5", "B4", "B3", "B2", "B1", "/OE", "VCC"),

        new(ShapeKind.Chip556, "556", "timer dual 556 oscillator monostable astable ne556",
            "DIS1", "THR1", "CTRL1", "/RST1", "OUT1", "TRIG1", "GND",
            "TRIG2", "OUT2", "/RST2", "CTRL2", "THR2", "DIS2", "VCC"),

        new(ShapeKind.ChipLm358, "LM358", "opamp op-amp amplifier dual lm358 operational analog",
            "OUT1", "IN1-", "IN1+", "V-", "IN2+", "IN2-", "OUT2", "V+"),

        new(ShapeKind.ChipLm324, "LM324", "opamp op-amp amplifier quad lm324 operational analog",
            "OUT1", "IN1-", "IN1+", "V+", "IN2+", "IN2-", "OUT2",
            "OUT3", "IN3-", "IN3+", "V-", "IN4+", "IN4-", "OUT4"),

        new(ShapeKind.ChipUln2003, "ULN2003", "darlington driver array relay stepper uln2003 sink",
            "IN1", "IN2", "IN3", "IN4", "IN5", "IN6", "IN7", "GND",
            "COM", "OUT7", "OUT6", "OUT5", "OUT4", "OUT3", "OUT2", "OUT1"),

        new(ShapeKind.ChipL293D, "L293D", "motor driver h-bridge l293d half bridge dc stepper",
            "EN1", "IN1", "OUT1", "GND", "GND", "OUT2", "IN2", "VS",
            "EN2", "IN3", "OUT3", "GND", "GND", "OUT4", "IN4", "VSS"),

        new(ShapeKind.ChipMax232, "MAX232", "rs232 serial level shifter driver receiver max232 uart",
            "C1+", "V+", "C1-", "C2+", "C2-", "V-", "T2OUT", "R2IN",
            "R2OUT", "T2IN", "T1IN", "R1OUT", "R1IN", "T1OUT", "GND", "VCC"),

        new(ShapeKind.Chip4N35, "4N35", "optocoupler optoisolator photocoupler 4n35 pc817 isolation",
            "AN", "CATH", "NC", "EMIT", "COLL", "BASE"),

        new(ShapeKind.ChipAtmega328, "ATmega328P",
            "microcontroller avr atmega328 arduino mcu uno atmel",
            "/RESET", "PD0", "PD1", "PD2", "PD3", "PD4", "VCC", "GND",
            "PB6", "PB7", "PD5", "PD6", "PD7", "PB0",
            "PB1", "PB2", "PB3", "PB4", "PB5", "AVCC", "AREF",
            "GND", "PC0", "PC1", "PC2", "PC3", "PC4", "PC5"),

        new(ShapeKind.ChipMcp23017, "MCP23017",
            "expander port io i2c mcp23017 gpio microchip sixteen",
            "GPB0", "GPB1", "GPB2", "GPB3", "GPB4", "GPB5", "GPB6", "GPB7",
            "VDD", "VSS", "NC", "SCL", "SDA", "NC",
            "A0", "A1", "A2", "/RESET", "INTB", "INTA", "GPA0",
            "GPA1", "GPA2", "GPA3", "GPA4", "GPA5", "GPA6", "GPA7"),

        // A regulator is not a DIP at all: a tab, a body and three legs out of the bottom.
        new(ShapeKind.Regulator7805, "7805", "regulator linear 7805 78xx supply 5v power lm317 to220",
            "IN", "GND", "OUT") { Package = ChipPackage.To220 },

        // Blanks, for a chip that is not in the list. The legs are numbered and nothing else
        // is claimed about them - which is better than offering a pinout that is almost right.
        new(ShapeKind.Dip8, "DIP-8", "chip ic dip 8 pin blank generic package socket",
            Numbers(8)),

        new(ShapeKind.Dip14, "DIP-14", "chip ic dip 14 pin blank generic package socket",
            Numbers(14)),

        new(ShapeKind.Dip16, "DIP-16", "chip ic dip 16 pin blank generic package socket",
            Numbers(16)),

        new(ShapeKind.Dip6, "DIP-6", "chip ic dip 6 pin blank generic package socket",
            Numbers(6)),

        new(ShapeKind.Dip18, "DIP-18", "chip ic dip 18 pin blank generic package socket",
            Numbers(18)),

        new(ShapeKind.Dip20, "DIP-20", "chip ic dip 20 pin blank generic package socket",
            Numbers(20)),

        new(ShapeKind.Dip24, "DIP-24", "chip ic dip 24 pin blank generic package socket",
            Numbers(24)),

        new(ShapeKind.Dip28, "DIP-28", "chip ic dip 28 pin blank generic package socket",
            Numbers(28))
    ];

    private static string[] Numbers(int count) =>
        Enumerable.Range(1, count).Select(n => n.ToString()).ToArray();

    private static readonly Dictionary<ShapeKind, Chip> ByKind =
        All.ToDictionary(chip => chip.Kind);

    public static Chip? Find(ShapeKind kind) =>
        ByKind.TryGetValue(kind, out var chip) ? chip : null;

    public static bool Is(ShapeKind kind) => ByKind.ContainsKey(kind);
}
