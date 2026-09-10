using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using AVASvgMaker.Models;

namespace AVASvgMaker.Engine;

/// <summary>Which edge or axis shapes are lined up on.</summary>
public enum AlignEdge
{
    Left,
    CenterX,
    Right,
    Top,
    Middle,
    Bottom
}

public enum SizeMatch
{
    Width,
    Height,
    Both
}

public enum ZOrder
{
    Front,
    Forward,
    Backward,
    Back
}

/// <summary>
/// Tidying operations on a selection: lining shapes up, spacing them evenly, matching their
/// sizes, and moving them through the drawing order. Each returns whether it actually changed
/// anything, so a command that would do nothing costs neither an undo step nor a dirty flag.
///
/// Named for what it arranges rather than just "Arrange", which every Avalonia control
/// already has as a layout method.
/// </summary>
public static class ShapeArranger
{
    /// <summary>Lines shapes up on one edge of the box that encloses them all.</summary>
    public static bool Align(IReadOnlyList<DiagramShape> shapes, AlignEdge edge)
    {
        if (shapes.Count < 2)
            return false;

        var bounds = ShapeClipboard.Union(shapes);
        var moved = false;

        foreach (var shape in shapes)
        {
            var box = shape.Bounds;

            var delta = edge switch
            {
                AlignEdge.Left => new Vector(bounds.Left - box.Left, 0),
                AlignEdge.CenterX => new Vector(bounds.Center.X - box.Center.X, 0),
                AlignEdge.Right => new Vector(bounds.Right - box.Right, 0),
                AlignEdge.Top => new Vector(0, bounds.Top - box.Top),
                AlignEdge.Middle => new Vector(0, bounds.Center.Y - box.Center.Y),
                AlignEdge.Bottom => new Vector(0, bounds.Bottom - box.Bottom),
                _ => default
            };

            if (Math.Abs(delta.X) < 0.01 && Math.Abs(delta.Y) < 0.01)
                continue;

            shape.Translate(delta);
            moved = true;
        }

        return moved;
    }

    /// <summary>
    /// Spreads shapes out so the gaps between them are equal, holding the two outermost
    /// where they are. Equal gaps rather than equal centres, which reads better when the
    /// shapes are different sizes.
    /// </summary>
    public static bool Distribute(IReadOnlyList<DiagramShape> shapes, bool horizontal)
    {
        // With two shapes there is one gap, and nothing to even out.
        if (shapes.Count < 3)
            return false;

        var ordered = shapes
            .OrderBy(shape => horizontal ? shape.Bounds.Left : shape.Bounds.Top)
            .ToList();

        var first = ordered[0].Bounds;
        var last = ordered[^1].Bounds;

        var span = horizontal
            ? last.Right - first.Left
            : last.Bottom - first.Top;

        var occupied = ordered.Sum(shape => horizontal ? shape.Bounds.Width : shape.Bounds.Height);
        var gap = (span - occupied) / (ordered.Count - 1);

        var cursor = horizontal ? first.Left : first.Top;
        var moved = false;

        foreach (var shape in ordered)
        {
            var box = shape.Bounds;
            var current = horizontal ? box.Left : box.Top;
            var delta = cursor - current;

            if (Math.Abs(delta) > 0.01)
            {
                shape.Translate(horizontal ? new Vector(delta, 0) : new Vector(0, delta));
                moved = true;
            }

            cursor += (horizontal ? box.Width : box.Height) + gap;
        }

        return moved;
    }

    /// <summary>Sizes shapes to match a reference - the last one selected.</summary>
    public static bool MatchSize(IReadOnlyList<DiagramShape> shapes, DiagramShape reference, SizeMatch match)
    {
        if (shapes.Count < 2)
            return false;

        var target = reference.Bounds;
        var changed = false;

        foreach (var shape in shapes)
        {
            if (ReferenceEquals(shape, reference) || !shape.IsBoxResizable)
                continue;

            var box = shape.Bounds;

            var width = match == SizeMatch.Height ? box.Width : target.Width;
            var height = match == SizeMatch.Width ? box.Height : target.Height;

            if (Math.Abs(width - box.Width) < 0.01 && Math.Abs(height - box.Height) < 0.01)
                continue;

            shape.Bounds = new Rect(box.X, box.Y, width, height);
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Moves the selection through the drawing order. Shapes keep their order relative to
    /// each other, and a step is not wasted swapping two shapes that are both moving.
    /// </summary>
    public static bool Reorder(List<DiagramShape> all, IReadOnlyList<DiagramShape> selection, ZOrder order)
    {
        if (selection.Count == 0 || selection.Count == all.Count)
            return false;

        var moving = new HashSet<DiagramShape>(selection);
        var before = all.ToList();

        switch (order)
        {
            case ZOrder.Front:
            {
                var lifted = all.Where(moving.Contains).ToList();
                all.RemoveAll(moving.Contains);
                all.AddRange(lifted);
                break;
            }

            case ZOrder.Back:
            {
                var dropped = all.Where(moving.Contains).ToList();
                all.RemoveAll(moving.Contains);
                all.InsertRange(0, dropped);
                break;
            }

            case ZOrder.Forward:
                // From the top down, so a shape cannot be stepped over twice in one go.
                for (var i = all.Count - 2; i >= 0; i--)
                {
                    if (moving.Contains(all[i]) && !moving.Contains(all[i + 1]))
                        (all[i], all[i + 1]) = (all[i + 1], all[i]);
                }

                break;

            case ZOrder.Backward:
                for (var i = 1; i < all.Count; i++)
                {
                    if (moving.Contains(all[i]) && !moving.Contains(all[i - 1]))
                        (all[i], all[i - 1]) = (all[i - 1], all[i]);
                }

                break;
        }

        return !all.SequenceEqual(before);
    }

    /// <summary>
    /// Stretches shapes from one rectangle into another, each keeping its place and its size
    /// in proportion - which is what resizing a whole selection, or a group, comes to.
    ///
    /// The starting bounds are passed in rather than read off the shapes, because a drag
    /// applies this again on every pointer move and reading the shapes would compound the
    /// scaling instead of replacing it.
    /// </summary>
    public static void Scale(IReadOnlyList<(DiagramShape Shape, Rect Start, Point[] Points)> shapes, Rect from, Rect to)
    {
        if (from.Width <= 0 || from.Height <= 0)
            return;

        var scaleX = to.Width / from.Width;
        var scaleY = to.Height / from.Height;

        Point Map(Point point) => new(
            to.X + (point.X - from.X) * scaleX,
            to.Y + (point.Y - from.Y) * scaleY);

        foreach (var (shape, start, points) in shapes)
        {
            if (shape is ConnectorShape connector)
            {
                // A connector has no bounds of its own to set; its ends and bends are the
                // shape. Glued ends are left alone - the shape they are stuck to moves them.
                if (connector.StartShape is null && points.Length > 0)
                    connector.Start = Map(points[0]);

                if (connector.EndShape is null && points.Length > 1)
                    connector.End = Map(points[1]);

                if (points.Length > 2)
                    connector.Waypoints = points.Skip(2).Select(Map).ToList();

                continue;
            }

            var corner = Map(start.TopLeft);

            shape.Bounds = new Rect(
                corner.X,
                corner.Y,
                Math.Max(DiagramShape.MinSize, start.Width * scaleX),
                Math.Max(DiagramShape.MinSize, start.Height * scaleY));
        }
    }

    /// <summary>
    /// What a scale needs to remember about a shape before the drag starts: its bounds, and
    /// for a connector the points that stand in for them.
    /// </summary>
    public static (DiagramShape Shape, Rect Start, Point[] Points) Snapshot(DiagramShape shape) =>
        shape is ConnectorShape connector
            ? (shape, shape.Bounds, [connector.Start, connector.End, .. connector.Waypoints])
            : (shape, shape.Bounds, []);
}
