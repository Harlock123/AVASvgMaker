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

    /// <summary>Index order is z-order.</summary>
    public List<DiagramShape> Shapes { get; } = [];
}
