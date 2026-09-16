using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using AVASvgMaker.Models;

namespace AVASvgMaker.Engine;

/// <summary>Which way the layers of a laid-out drawing run.</summary>
public enum LayoutFlow
{
    /// <summary>Down the page, as a flowchart is usually drawn.</summary>
    Down,

    /// <summary>Across the page, left to right.</summary>
    Right
}

/// <summary>
/// Lays shapes out in layers, the way a directed drawing wants to be read: everything a shape
/// points at sits one layer further on, and the layers are spread out along the flow.
///
/// The method is the usual one for this - Sugiyama's - in four passes. Cycles are broken by
/// turning the edges that close them round, so that what is left can be layered at all. Each
/// shape is put in the layer one past the furthest thing pointing at it. The order within each
/// layer is then shuffled to pull lines that cross apart from one another. Finally the shapes
/// are given positions, with the ones in a layer nudged towards the middle of whatever they
/// are joined to.
///
/// What it does not do is draw the lines. The shapes are moved and the connectors are left to
/// route themselves, which they already do better than a layout pass could tell them to.
/// </summary>
public static class GraphLayout
{
    /// <summary>A place in a layer: either a shape, or the room a line passing through needs.</summary>
    private sealed class Node
    {
        public DiagramShape? Shape;
        public int Layer;
        public int Order;
        public double Centre;
        public readonly List<int> Up = [];
        public readonly List<int> Down = [];

        /// <summary>How much room it takes across the layer. A line needs a little, not none.</summary>
        public double Across(LayoutFlow flow, double laneWidth) =>
            Shape is null
                ? laneWidth
                : flow == LayoutFlow.Down ? Shape.Bounds.Width : Shape.Bounds.Height;

        public double Along(LayoutFlow flow) =>
            Shape is null
                ? 0
                : flow == LayoutFlow.Down ? Shape.Bounds.Height : Shape.Bounds.Width;
    }

    /// <summary>
    /// Arranges <paramref name="shapes"/> by the connectors between them. Shapes nothing is
    /// joined to are laid out too, in a row of their own after the rest.
    ///
    /// The block ends up where the shapes already were: the top-left of what they covered
    /// before is the top-left of what they cover after, so the page does not jump about.
    /// </summary>
    public static bool Apply(
        IReadOnlyList<DiagramShape> shapes,
        IReadOnlyList<ConnectorShape> links,
        LayoutFlow flow,
        Rect? within = null,
        double layerGap = 70,
        double nodeGap = 40)
    {
        var laid = shapes.Where(shape => shape is not ConnectorShape).ToList();

        if (laid.Count < 2)
            return false;

        var index = new Dictionary<DiagramShape, int>();

        for (var i = 0; i < laid.Count; i++)
            index[laid[i]] = i;

        var nodes = laid.Select(shape => new Node { Shape = shape }).ToList();

        // Only the lines that join two shapes being laid out. A connector with a loose end, or
        // one reaching a shape that is staying put, has no say in the order of anything.
        var edges = new List<(int From, int To)>();

        foreach (var link in links)
        {
            if (link.StartShape is null || link.EndShape is null ||
                !index.TryGetValue(link.StartShape, out var from) ||
                !index.TryGetValue(link.EndShape, out var to) ||
                from == to)
                continue;

            edges.Add((from, to));
        }

        Break(nodes.Count, edges);
        var layers = Layer(nodes, edges);
        var grid = Fill(nodes, edges, layers, flow, nodeGap);

        Order(nodes, grid);
        Place(nodes, grid, flow, nodeGap);

        return Move(laid, nodes, flow, layerGap, within);
    }

    #region Making it acyclic

    /// <summary>
    /// Turns round the edges that close a cycle, so the rest can be layered. A drawing with a
    /// loop in it has no first layer otherwise, and the loop is usually the thing the drawing
    /// is about - so the edge is reversed rather than dropped, and comes back as a line the
    /// router draws going the other way, which is what a reader expects of a loop.
    /// </summary>
    private static void Break(int count, List<(int From, int To)> edges)
    {
        var state = new int[count];
        var outgoing = new List<int>[count];

        for (var i = 0; i < count; i++)
            outgoing[i] = [];

        for (var e = 0; e < edges.Count; e++)
            outgoing[edges[e].From].Add(e);

        var reversed = new List<int>();

        void Walk(int at)
        {
            state[at] = 1;

            foreach (var e in outgoing[at])
            {
                var next = edges[e].To;

                if (state[next] == 1)
                    reversed.Add(e);
                else if (state[next] == 0)
                    Walk(next);
            }

            state[at] = 2;
        }

        for (var i = 0; i < count; i++)
        {
            if (state[i] == 0)
                Walk(i);
        }

        foreach (var e in reversed)
            edges[e] = (edges[e].To, edges[e].From);
    }

    #endregion

    #region Layers

    /// <summary>
    /// One past the furthest thing pointing at it. Relaxed until nothing moves, which on a
    /// drawing without cycles - and there are none left by now - always settles.
    /// </summary>
    private static int Layer(List<Node> nodes, List<(int From, int To)> edges)
    {
        var moved = true;
        var guard = 0;

        while (moved && guard++ <= nodes.Count + 1)
        {
            moved = false;

            foreach (var (from, to) in edges)
            {
                if (nodes[to].Layer >= nodes[from].Layer + 1)
                    continue;

                nodes[to].Layer = nodes[from].Layer + 1;
                moved = true;
            }
        }

        return nodes.Max(node => node.Layer) + 1;
    }

    /// <summary>
    /// Puts every node in its layer, and gives a line that skips layers somewhere to sit in
    /// each one it passes. Without that, a long line is free to run through the middle of a
    /// layer it has no business in, and everything shuffles round it for nothing.
    /// </summary>
    private static List<List<int>> Fill(
        List<Node> nodes, List<(int From, int To)> edges, int layers, LayoutFlow flow, double nodeGap)
    {
        var grid = new List<List<int>>();

        for (var i = 0; i < layers; i++)
            grid.Add([]);

        foreach (var (from, to) in edges)
        {
            var step = nodes[from].Layer + 1;
            var previous = from;

            while (step < nodes[to].Layer)
            {
                var ghost = new Node { Layer = step };
                nodes.Add(ghost);

                var id = nodes.Count - 1;
                nodes[previous].Down.Add(id);
                ghost.Up.Add(previous);

                previous = id;
                step++;
            }

            nodes[previous].Down.Add(to);
            nodes[to].Up.Add(previous);
        }

        for (var i = 0; i < nodes.Count; i++)
            grid[nodes[i].Layer].Add(i);

        // Started from where the shapes already are, so a drawing that was nearly tidy comes
        // back looking like itself rather than like something else that happens to be tidy.
        foreach (var layer in grid)
        {
            layer.Sort((a, b) =>
            {
                var one = Seed(nodes[a], flow);
                var two = Seed(nodes[b], flow);
                var by = one.CompareTo(two);
                return by != 0 ? by : a.CompareTo(b);
            });

            for (var i = 0; i < layer.Count; i++)
                nodes[layer[i]].Order = i;
        }

        return grid;
    }

    /// <summary>How far a run of the given length has to move to sit inside another.</summary>
    private static double Slide(double at, double length, double low, double room)
    {
        if (length >= room)
            return low - at;

        if (at < low)
            return low - at;

        return at + length > low + room ? low + room - length - at : 0;
    }

    private static double Seed(Node node, LayoutFlow flow) =>
        node.Shape is null
            ? double.MaxValue
            : flow == LayoutFlow.Down ? node.Shape.Bounds.Center.X : node.Shape.Bounds.Center.Y;

    #endregion

    #region Order within a layer

    /// <summary>
    /// Sweeps up and down, each time putting the nodes of a layer in the order of the middle
    /// of whatever they are joined to in the layer before. Crossings are counted after every
    /// sweep and the best arrangement kept, because the sweep is a heuristic and is allowed to
    /// make things worse on the way to making them better.
    /// </summary>
    private static void Order(List<Node> nodes, List<List<int>> grid)
    {
        var best = Crossings(nodes, grid);
        var keep = grid.Select(layer => layer.ToList()).ToList();

        for (var pass = 0; pass < 8 && best > 0; pass++)
        {
            var down = pass % 2 == 0;

            for (var i = 1; i < grid.Count; i++)
            {
                var at = down ? i : grid.Count - 1 - i;
                Sort(nodes, grid[at], down);
            }

            Renumber(nodes, grid);
            var now = Crossings(nodes, grid);

            if (now >= best)
                continue;

            best = now;
            keep = grid.Select(layer => layer.ToList()).ToList();
        }

        for (var i = 0; i < grid.Count; i++)
        {
            grid[i].Clear();
            grid[i].AddRange(keep[i]);
        }

        Renumber(nodes, grid);
    }

    private static void Sort(List<Node> nodes, List<int> layer, bool down)
    {
        var middle = new Dictionary<int, double>();

        foreach (var id in layer)
        {
            var neighbours = down ? nodes[id].Up : nodes[id].Down;

            middle[id] = neighbours.Count == 0
                ? nodes[id].Order
                : neighbours.Average(other => (double)nodes[other].Order);
        }

        layer.Sort((a, b) =>
        {
            var by = middle[a].CompareTo(middle[b]);
            return by != 0 ? by : nodes[a].Order.CompareTo(nodes[b].Order);
        });
    }

    private static void Renumber(List<Node> nodes, List<List<int>> grid)
    {
        foreach (var layer in grid)
        {
            for (var i = 0; i < layer.Count; i++)
                nodes[layer[i]].Order = i;
        }
    }

    /// <summary>
    /// How many pairs of lines cross between one layer and the next: two lines cross when the
    /// order of their tops and the order of their bottoms disagree.
    /// </summary>
    private static int Crossings(List<Node> nodes, List<List<int>> grid)
    {
        var total = 0;

        for (var i = 0; i + 1 < grid.Count; i++)
        {
            var pairs = new List<(int Top, int Bottom)>();

            foreach (var id in grid[i])
            foreach (var other in nodes[id].Down)
                pairs.Add((nodes[id].Order, nodes[other].Order));

            for (var a = 0; a < pairs.Count; a++)
            for (var b = a + 1; b < pairs.Count; b++)
            {
                var one = pairs[a];
                var two = pairs[b];

                if ((one.Top - two.Top) * (one.Bottom - two.Bottom) < 0)
                    total++;
            }
        }

        return total;
    }

    #endregion

    #region Positions

    /// <summary>
    /// Gives every node a place. Across a layer, each is pulled towards the middle of what it
    /// is joined to and then pushed apart until nothing overlaps; along the flow, each layer
    /// clears the deepest shape in the one before it.
    /// </summary>
    private static void Place(
        List<Node> nodes, List<List<int>> grid, LayoutFlow flow, double nodeGap)
    {
        var lane = nodeGap / 2;

        foreach (var layer in grid)
        {
            var at = 0.0;

            foreach (var id in layer)
            {
                var half = nodes[id].Across(flow, lane) / 2;
                nodes[id].Centre = at + half;
                at += half * 2 + nodeGap;
            }
        }

        for (var pass = 0; pass < 4; pass++)
        {
            var down = pass % 2 == 0;

            for (var i = 0; i < grid.Count; i++)
            {
                var at = down ? i : grid.Count - 1 - i;
                Pull(nodes, grid[at], down, flow, nodeGap, lane);
            }
        }
    }

    private static void Pull(
        List<Node> nodes, List<int> layer, bool down, LayoutFlow flow, double nodeGap, double lane)
    {
        foreach (var id in layer)
        {
            var neighbours = down ? nodes[id].Up : nodes[id].Down;

            if (neighbours.Count > 0)
                nodes[id].Centre = neighbours.Average(other => nodes[other].Centre);
        }

        var wanted = layer.Select(id => nodes[id].Centre).ToList();

        // Pushed apart in the order the layer is in, which the pull above is not allowed to
        // change - the order is what the crossing count was measured on.
        for (var i = 1; i < layer.Count; i++)
        {
            var behind = nodes[layer[i - 1]];
            var here = nodes[layer[i]];
            var least = behind.Centre + behind.Across(flow, lane) / 2 + nodeGap + here.Across(flow, lane) / 2;

            if (here.Centre < least)
                here.Centre = least;
        }

        // Pushing apart always pushes one way, which walks the whole layer off to that side:
        // two shapes that both want to sit under the same parent come out with the first one
        // under it and the second beside it, rather than one either side. Sliding the layer
        // back by how far it drifted on average puts it where it asked to be, with the
        // spacing the push gave it.
        var drift = layer.Select((id, i) => nodes[id].Centre - wanted[i]).Average();

        foreach (var id in layer)
            nodes[id].Centre -= drift;
    }

    /// <summary>Writes the places back, keeping the block where the shapes already were.</summary>
    private static bool Move(
        List<DiagramShape> laid, List<Node> nodes, LayoutFlow flow, double layerGap, Rect? within)
    {
        var was = laid[0].Bounds;

        foreach (var shape in laid)
            was = was.Union(shape.Bounds);

        var deepest = new Dictionary<int, double>();

        foreach (var node in nodes.Where(n => n.Shape is not null))
        {
            deepest.TryGetValue(node.Layer, out var known);
            deepest[node.Layer] = Math.Max(known, node.Along(flow));
        }

        var along = new Dictionary<int, double>();
        var at = 0.0;

        foreach (var layer in deepest.Keys.OrderBy(key => key))
        {
            along[layer] = at;
            at += deepest[layer] + layerGap;
        }

        var placed = new List<(DiagramShape Shape, Rect At)>();

        foreach (var node in nodes)
        {
            if (node.Shape is null)
                continue;

            var size = node.Shape.Bounds;
            var main = along[node.Layer] + (deepest[node.Layer] - node.Along(flow)) / 2;

            placed.Add((node.Shape, flow == LayoutFlow.Down
                ? new Rect(node.Centre - size.Width / 2, main, size.Width, size.Height)
                : new Rect(main, node.Centre - size.Height / 2, size.Width, size.Height)));
        }

        // Put back where it came from, so a tidy-up does not also move the drawing.
        var now = placed[0].At;

        foreach (var (_, rect) in placed)
            now = now.Union(rect);

        var shift = new Vector(was.X - now.X, was.Y - now.Y);

        // Tidied, a drawing is usually taller than the sprawl it came from, so putting it back
        // where it started can hang it off the bottom of the paper. It is slid back on where
        // there is room. Where there is not - a drawing with more layers than the page is deep
        // - it goes to the top corner and hangs over the edge, which at least leaves the part
        // you are reading first on the page.
        if (within is { } page)
        {
            var block = new Rect(now.X + shift.X, now.Y + shift.Y, now.Width, now.Height);

            shift = new Vector(
                shift.X + Slide(block.X, block.Width, page.X, page.Width),
                shift.Y + Slide(block.Y, block.Height, page.Y, page.Height));
        }

        var changed = false;

        foreach (var (shape, rect) in placed)
        {
            var to = new Rect(rect.X + shift.X, rect.Y + shift.Y, rect.Width, rect.Height);

            if (Math.Abs(to.X - shape.Bounds.X) < 0.01 && Math.Abs(to.Y - shape.Bounds.Y) < 0.01)
                continue;

            shape.Bounds = to;
            changed = true;
        }

        return changed;
    }

    #endregion
}
