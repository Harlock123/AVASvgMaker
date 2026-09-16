namespace AVASvgMaker.Models;

/// <summary>How a connector gets from one end to the other.</summary>
public enum ConnectorRouting
{
    /// <summary>A single straight line, whatever is in the way.</summary>
    Straight,

    /// <summary>Right-angled segments that keep clear of the shapes on the page.</summary>
    Orthogonal,

    /// <summary>
    /// The same route, drawn with its corners rounded off. Routed exactly as an orthogonal
    /// connector is - it keeps the same clearances, glue and hand-placed bends - and differs
    /// only in how the turns between its runs are drawn.
    /// </summary>
    Curved
}
