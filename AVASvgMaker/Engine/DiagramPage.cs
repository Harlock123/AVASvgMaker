using System.Collections.Generic;
using Avalonia.Media;
using AVASvgMaker.Models;

namespace AVASvgMaker.Engine;

/// <summary>
/// One page of a document: a name and the shapes on it, in drawing order.
///
/// Pages are independent of one another. A connector glues to shapes on its own page and a
/// container holds shapes on its own page, so nothing on one page can reference anything on
/// another - which is what lets a page be deleted, duplicated or reordered without having to
/// look at the rest of the document.
/// </summary>
public class DiagramPage(string name)
{
    public string Name { get; set; } = name;

    // US Letter at 96 DPI, until told otherwise.
    public double Width { get; set; } = 816;
    public double Height { get; set; } = 1056;

    /// <summary>
    /// How far in from the edge the margin guide is drawn, or 0 for none. A guide only: it is
    /// not a boundary, and nothing is stopped from being put outside it or clipped by it.
    /// </summary>
    public double Margin { get; set; }

    /// <summary>
    /// The paper. White until told otherwise, and a page that fades runs from this colour to
    /// <see cref="BackgroundTo"/> - so a page that stops fading keeps the colour it had.
    /// </summary>
    public Color Background { get; set; } = Colors.White;

    public Color? BackgroundTo { get; set; }

    /// <summary>Degrees clockwise from left-to-right: 0 runs across, 90 runs down.</summary>
    public double BackgroundAngle { get; set; } = 90;

    /// <summary>What the paper is painted with, be it one colour or a run between two.</summary>
    public Gradient? Fade =>
        BackgroundTo is { } far ? new Gradient(Background, far, BackgroundAngle) : null;

    public IBrush Paper() => Fade?.Brush() ?? new SolidColorBrush(Background);

    /// <summary>
    /// Words across the page, under the drawing - DRAFT, CONFIDENTIAL, the usual. Empty for a
    /// page with none, which is the ordinary case.
    /// </summary>
    public string Watermark { get; set; } = string.Empty;

    public Color WatermarkColor { get; set; } = Color.FromArgb(0x1E, 0x60, 0x60, 0x70);

    /// <summary>Degrees clockwise. The diagonal, unless something else is wanted.</summary>
    public double WatermarkAngle { get; set; } = -30;

    /// <summary>
    /// A line along the top and the bottom of the page. Both may name the page number, the
    /// count, the page's name or the date in braces, as a shape's label names its data.
    /// </summary>
    public string Header { get; set; } = string.Empty;

    public string Footer { get; set; } = string.Empty;

    public double HeadFootSize { get; set; } = 11;

    public Color HeadFootColor { get; set; } = Color.FromRgb(0x70, 0x76, 0x82);

    /// <summary>Index order is z-order.</summary>
    public List<DiagramShape> Shapes { get; } = [];
}
