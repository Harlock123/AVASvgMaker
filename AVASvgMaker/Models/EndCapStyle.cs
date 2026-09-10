namespace AVASvgMaker.Models;

/// <summary>
/// How a connector is terminated at one of its ends. The hollow and crow's foot entries are
/// what make UML and entity-relationship diagrams expressible: the notation lives in the
/// line ends, not in the boxes.
/// </summary>
public enum EndCapStyle
{
    None,
    Arrow,
    OpenArrow,
    Dot,
    Diamond,

    /// <summary>UML generalisation: a closed, unfilled triangle.</summary>
    HollowArrow,

    /// <summary>UML aggregation, against the filled <see cref="Diamond"/> for composition.</summary>
    HollowDiamond,

    /// <summary>Entity-relationship "many".</summary>
    CrowsFoot,

    /// <summary>"One": a single bar across the line.</summary>
    CrowsFootOne,

    /// <summary>"Zero or one": a ring and a bar.</summary>
    CrowsFootZeroOrOne,

    /// <summary>"One or many": a bar and a crow's foot.</summary>
    CrowsFootOneOrMany,

    /// <summary>"Zero or many": a ring and a crow's foot.</summary>
    CrowsFootZeroOrMany
}
