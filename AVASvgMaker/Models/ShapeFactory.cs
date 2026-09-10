using System;
using Avalonia;

namespace AVASvgMaker.Models;

public static class ShapeFactory
{
    /// <summary>
    /// Builds a shape. Most come straight from the catalogue's outline; the handful listed
    /// here draw themselves, because they need something the path language does not do -
    /// corner radii that follow the size, or a second stroked pass over the fill.
    /// </summary>
    public static DiagramShape Create(ShapeKind kind, Rect bounds) => kind switch
    {
        ShapeKind.Rectangle => new RectangleShape(bounds),
        ShapeKind.RoundedRectangle => new RoundedRectangleShape(bounds),
        ShapeKind.Ellipse => new EllipseShape(bounds),
        ShapeKind.Diamond => new DiamondShape(bounds),
        ShapeKind.Triangle => new TriangleShape(bounds),
        ShapeKind.Hexagon => new HexagonShape(bounds),
        ShapeKind.Parallelogram => new ParallelogramShape(bounds),
        ShapeKind.Cylinder => new CylinderShape(bounds),
        ShapeKind.TextBox => new TextBoxShape(bounds),
        ShapeKind.Pool => new ContainerShape(kind, bounds, HeaderEdge.Left),
        ShapeKind.Lane => new ContainerShape(kind, bounds, HeaderEdge.Left),
        ShapeKind.ContainerBox => new ContainerShape(kind, bounds, HeaderEdge.Top),
        ShapeKind.Connector => throw new ArgumentException(
            "Connectors are built from their end points - see ConnectorShape.", nameof(kind)),
        _ => StencilCatalogue.Find(kind) is { } stencil
            ? new StencilShape(stencil, bounds)
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "No stencil for this kind.")
    };

    public static string DisplayName(ShapeKind kind) => kind switch
    {
        // The flowchart names these differently from their plain geometry.
        ShapeKind.Rectangle => "Process",
        ShapeKind.RoundedRectangle => "Terminator",
        ShapeKind.Diamond => "Decision",
        ShapeKind.Parallelogram => "Data",
        ShapeKind.Hexagon => "Preparation",
        ShapeKind.Cylinder => "Database",
        ShapeKind.TextBox => "Text box",
        ShapeKind.Connector => "Connector",
        _ => StencilCatalogue.Find(kind)?.Name ?? kind.ToString()
    };
}
