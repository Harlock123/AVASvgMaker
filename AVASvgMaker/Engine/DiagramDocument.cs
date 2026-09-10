using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using AVASvgMaker.Models;

namespace AVASvgMaker.Engine;

/// <summary>The page and the shapes on it. Index order in <see cref="Shapes"/> is z-order.</summary>
public class DiagramDocument
{
    // US Letter at 96 DPI.
    public double PageWidth { get; set; } = 816;
    public double PageHeight { get; set; } = 1056;

    public List<DiagramShape> Shapes { get; } = new();

    private readonly List<DiagramShape> _selection = [];

    /// <summary>The selected shapes, in the order they were selected.</summary>
    public IReadOnlyList<DiagramShape> Selection => _selection;

    /// <summary>
    /// The shape that single-shape operations act on: the last one selected, or null when
    /// nothing is. Handles and the connector toolbar only apply to a selection of one.
    /// </summary>
    public DiagramShape? Selected => _selection.Count > 0 ? _selection[^1] : null;

    /// <summary>Raised when the selection changes. Selection is not saved, but undo restores it.</summary>
    public event Action? SelectionChanged;

    public bool IsSelected(DiagramShape shape) => _selection.Contains(shape);

    /// <summary>Replaces the selection with one shape, or clears it when given null.</summary>
    public void SelectOnly(DiagramShape? shape)
    {
        if (shape is null)
        {
            ClearSelection();
            return;
        }

        if (_selection.Count == 1 && ReferenceEquals(_selection[0], shape))
            return;

        _selection.Clear();
        _selection.Add(shape);
        SelectionChanged?.Invoke();
    }

    /// <summary>Adds a shape to the selection, or removes it if it is already there.</summary>
    public void ToggleSelection(DiagramShape shape)
    {
        if (!_selection.Remove(shape))
            _selection.Add(shape);

        SelectionChanged?.Invoke();
    }

    public void SetSelection(IEnumerable<DiagramShape> shapes)
    {
        _selection.Clear();

        foreach (var shape in shapes)
        {
            if (!_selection.Contains(shape))
                _selection.Add(shape);
        }

        SelectionChanged?.Invoke();
    }

    public void SelectAll() => SetSelection(Shapes);

    public void ClearSelection()
    {
        if (_selection.Count == 0)
            return;

        _selection.Clear();
        SelectionChanged?.Invoke();
    }

    private void Deselect(DiagramShape shape)
    {
        if (_selection.Remove(shape))
            SelectionChanged?.Invoke();
    }

    /// <summary>True when there are changes that have not been written to disk.</summary>
    public bool IsModified { get; private set; }

    /// <summary>Raised whenever the modified flag changes, so the title bar can follow it.</summary>
    public event Action? ModifiedChanged;

    /// <summary>Raised after every change to the page, once per edit. Drives the undo history.</summary>
    public event Action? Changed;

    private int _batchDepth;
    private bool _batchPending;

    public void MarkModified()
    {
        if (!IsModified)
        {
            IsModified = true;
            ModifiedChanged?.Invoke();
        }

        if (_batchDepth > 0)
        {
            _batchPending = true;
            return;
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Groups several mutations into one edit, so a compound operation such as deleting a
    /// shape along with its connectors becomes a single step in the undo history.
    /// </summary>
    public IDisposable BeginBatch() => new Batch(this);

    private void EndBatch()
    {
        if (--_batchDepth > 0 || !_batchPending)
            return;

        _batchPending = false;
        Changed?.Invoke();
    }

    private sealed class Batch : IDisposable
    {
        private readonly DiagramDocument _document;
        private bool _disposed;

        public Batch(DiagramDocument document)
        {
            _document = document;
            _document._batchDepth++;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _document.EndBatch();
        }
    }

    public void MarkSaved()
    {
        if (!IsModified)
            return;

        IsModified = false;
        ModifiedChanged?.Invoke();
    }

    public void Add(DiagramShape shape)
    {
        Shapes.Add(shape);
        MarkModified();
        SelectOnly(shape);
    }

    public void Remove(DiagramShape shape)
    {
        if (!Shapes.Remove(shape))
            return;

        MarkModified();
        Deselect(shape);

        // Nothing may point at a shape that is gone.
        foreach (var orphan in Shapes.Where(other => ReferenceEquals(other.Container, shape)))
            orphan.Container = null;
    }

    public void Clear()
    {
        if (Shapes.Count == 0)
            return;

        Shapes.Clear();
        MarkModified();
        ClearSelection();
    }

    /// <summary>Takes on the contents of a freshly loaded document, keeping this instance.</summary>
    public void ReplaceWith(DiagramDocument source)
    {
        PageWidth = source.PageWidth;
        PageHeight = source.PageHeight;

        Shapes.Clear();
        Shapes.AddRange(source.Shapes);
        ClearSelection();

        MarkSaved();
    }

    /// <summary>
    /// Topmost shape under the point, or null. <paramref name="slack"/> is extra tolerance in
    /// page units for thin targets, so a line stays clickable at any zoom.
    /// </summary>
    public DiagramShape? HitTest(Point point, double slack = 0)
    {
        for (var i = Shapes.Count - 1; i >= 0; i--)
        {
            if (Shapes[i].HitTest(point, slack))
                return Shapes[i];
        }

        return null;
    }

    public void BringToFront(DiagramShape shape)
    {
        // Selecting the frontmost shape must not count as an edit.
        if (Shapes.Count == 0 || ReferenceEquals(Shapes[^1], shape))
            return;

        if (!Shapes.Remove(shape))
            return;

        Shapes.Add(shape);
        MarkModified();
    }

    /// <summary>
    /// How far a routed connector keeps away from the shapes it passes. Also the length of
    /// the stub it leaves a connection point by, so lines meet shapes square on.
    /// </summary>
    public double RouteClearance { get; set; } = 12;

    /// <summary>
    /// Refreshes every routed connector. Each one only pays for the search when something it
    /// depends on has actually moved, so this is cheap to call on every repaint.
    /// </summary>
    public void RouteConnectors()
    {
        var obstacles = Shapes.Where(shape => shape is not ConnectorShape).ToList();

        foreach (var connector in Shapes.OfType<ConnectorShape>())
            connector.UpdateRoute(obstacles, RouteClearance);
    }

    #region Containers

    public IEnumerable<DiagramShape> ChildrenOf(DiagramShape container) =>
        Shapes.Where(shape => ReferenceEquals(shape.Container, container));

    /// <summary>Everything inside a container, including what is inside its lanes.</summary>
    public IEnumerable<DiagramShape> DescendantsOf(DiagramShape container)
    {
        foreach (var child in ChildrenOf(container).ToList())
        {
            yield return child;

            foreach (var deeper in DescendantsOf(child))
                yield return deeper;
        }
    }

    private bool IsInside(DiagramShape shape, DiagramShape possibleAncestor)
    {
        for (var container = shape.Container; container is not null; container = container.Container)
        {
            if (ReferenceEquals(container, possibleAncestor))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The container a shape should belong to, judged by where its middle falls. The innermost
    /// wins, so a shape dropped on a lane joins the lane rather than the pool around it. A
    /// container is never offered one of its own descendants, which would make a loop.
    /// </summary>
    public ContainerShape? ContainerFor(DiagramShape shape)
    {
        ContainerShape? best = null;

        foreach (var candidate in Shapes.OfType<ContainerShape>())
        {
            if (ReferenceEquals(candidate, shape) || IsInside(candidate, shape))
                continue;

            if (!candidate.Holds(shape.Bounds))
                continue;

            // Deeper containers are smaller; prefer the tightest fit.
            if (best is null || Area(candidate.Bounds) <= Area(best.Bounds))
                best = candidate;
        }

        return best;
    }

    private static double Area(Rect rect) => rect.Width * rect.Height;

    /// <summary>Puts a shape in a container, or takes it out when given null.</summary>
    public void Adopt(DiagramShape shape, DiagramShape? container)
    {
        if (ReferenceEquals(shape.Container, container))
            return;

        shape.Container = container;

        NormaliseOrder();
        MarkModified();
    }

    /// <summary>
    /// Puts every container before the shapes it holds.
    ///
    /// A container is a backdrop, so it has to be drawn first. Any reordering can break that -
    /// bringing a lane to the front puts it in front of its own contents - and a lane with an
    /// opaque fill then hides everything in it. Rather than teaching each reordering operation
    /// about containers, the rule is restored here afterwards.
    /// </summary>
    public void NormaliseOrder()
    {
        if (IsOrdered())
            return;

        var ordered = new List<DiagramShape>(Shapes.Count);
        var placed = new HashSet<DiagramShape>();

        foreach (var shape in Shapes)
            Place(shape, ordered, placed, 0);

        Shapes.Clear();
        Shapes.AddRange(ordered);
    }

    /// <summary>Emits a shape's container before the shape, and so on up the chain.</summary>
    private static void Place(DiagramShape shape, List<DiagramShape> ordered,
        HashSet<DiagramShape> placed, int depth)
    {
        if (placed.Contains(shape))
            return;

        // The depth limit is for a malformed file; ContainerFor will not make a loop.
        if (shape.Container is { } container && depth < 32)
            Place(container, ordered, placed, depth + 1);

        if (placed.Add(shape))
            ordered.Add(shape);
    }

    private bool IsOrdered()
    {
        var index = new Dictionary<DiagramShape, int>(Shapes.Count);

        for (var i = 0; i < Shapes.Count; i++)
            index[Shapes[i]] = i;

        foreach (var shape in Shapes)
        {
            if (shape.Container is { } container &&
                index.TryGetValue(container, out var position) &&
                position > index[shape])
                return false;
        }

        return true;
    }

    /// <summary>The lanes of a pool, in the order they are drawn from top to bottom.</summary>
    public List<ContainerShape> LanesOf(DiagramShape pool) => ChildrenOf(pool)
        .OfType<ContainerShape>()
        .Where(lane => lane.Kind == ShapeKind.Lane)
        .ToList();

    /// <summary>
    /// Lays a pool's lanes out across its body. Lanes are positioned by the pool rather than by
    /// their own handles, which is what makes them follow when the pool is moved or resized.
    ///
    /// Whatever a lane holds travels with it. Without that, a lane that is reordered or a pool
    /// that is resized leaves its contents behind, sitting over whichever lane has taken the
    /// space - the shapes stay put while the band beneath them slides away.
    /// </summary>
    public void LayoutContainers()
    {
        foreach (var pool in Shapes.OfType<ContainerShape>().Where(c => c.Kind == ShapeKind.Pool))
        {
            var lanes = LanesOf(pool);

            if (lanes.Count == 0)
                continue;

            var body = pool.Body;
            var height = body.Height / lanes.Count;

            for (var i = 0; i < lanes.Count; i++)
            {
                var lane = lanes[i];
                var wanted = new Rect(body.X, body.Y + i * height, body.Width, height);

                if (lane.Bounds == wanted)
                    continue;

                var delta = new Vector(wanted.X - lane.Bounds.X, wanted.Y - lane.Bounds.Y);
                lane.Bounds = wanted;

                if (delta.X == 0 && delta.Y == 0)
                    continue;

                foreach (var child in DescendantsOf(lane).ToList())
                    child.Translate(delta);
            }
        }
    }

    /// <summary>
    /// Moves a lane to a new place among its siblings, contents and all.
    ///
    /// A lane cannot be dragged around freely - the pool decides where it sits - so dragging
    /// one reorders it instead. The lane and everything inside it move through the drawing
    /// order as one block, or the contents would be left pointing at a container that now sits
    /// after them.
    /// </summary>
    public bool MoveLaneTo(ContainerShape lane, int position)
    {
        if (lane.Container is not { } pool)
            return false;

        var lanes = LanesOf(pool);
        var current = lanes.IndexOf(lane);

        if (current < 0)
            return false;

        position = Math.Clamp(position, 0, lanes.Count - 1);

        if (position == current)
            return false;

        var block = new HashSet<DiagramShape>(DescendantsOf(lane)) { lane };
        var moving = Shapes.Where(block.Contains).ToList();

        Shapes.RemoveAll(block.Contains);

        var remaining = LanesOf(pool);

        int insertAt;

        if (position >= remaining.Count)
        {
            // Past the last lane: after everything that lane holds.
            var last = remaining[^1];
            var lastBlock = new HashSet<DiagramShape>(DescendantsOf(last)) { last };
            insertAt = Shapes.FindLastIndex(lastBlock.Contains) + 1;
        }
        else
        {
            insertAt = Shapes.IndexOf(remaining[position]);
        }

        Shapes.InsertRange(insertAt, moving);
        MarkModified();
        return true;
    }

    #endregion

    /// <summary>Resizes the page, as an edit that can be undone.</summary>
    public void SetPageSize(double width, double height)
    {
        if (Math.Abs(PageWidth - width) < 0.01 && Math.Abs(PageHeight - height) < 0.01)
            return;

        PageWidth = width;
        PageHeight = height;
        MarkModified();
    }

    /// <summary>The box around everything on the page, or nothing when it is empty.</summary>
    public Rect? DrawingBounds
    {
        get
        {
            if (Shapes.Count == 0)
                return null;

            var bounds = Shapes[0].Bounds;

            foreach (var shape in Shapes.Skip(1))
                bounds = bounds.Union(shape.Bounds);

            return bounds;
        }
    }

    /// <summary>Keeps a shape inside the page after a move.</summary>
    public Rect ClampToPage(Rect bounds)
    {
        var x = System.Math.Clamp(bounds.X, 0, System.Math.Max(0, PageWidth - bounds.Width));
        var y = System.Math.Clamp(bounds.Y, 0, System.Math.Max(0, PageHeight - bounds.Height));
        return new Rect(x, y, bounds.Width, bounds.Height);
    }
}
