namespace AVASvgMaker.Models;

/// <summary>How a connector gets from one end to the other.</summary>
public enum ConnectorRouting
{
    /// <summary>A single straight line, whatever is in the way.</summary>
    Straight,

    /// <summary>Right-angled segments that keep clear of the shapes on the page.</summary>
    Orthogonal
}
