using System.Collections.Generic;
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

    /// <summary>Index order is z-order.</summary>
    public List<DiagramShape> Shapes { get; } = [];
}
