using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using AVASvgMaker.Models;

namespace AVASvgMaker.Engine;

/// <summary>
/// Copy and paste, carried as the same JSON the native format uses. That makes the
/// clipboard readable, lets shapes be pasted into another instance of the app, and means
/// pasted shapes are genuine copies rather than shared references.
/// </summary>
public static class ShapeClipboard
{
    /// <summary>Serialises a selection, keeping its z-order. Returns null when nothing is selected.</summary>
    public static string? Copy(DiagramDocument document, IReadOnlyList<DiagramShape> shapes)
    {
        if (shapes.Count == 0)
            return null;

        var wanted = new HashSet<DiagramShape>(shapes);

        var slice = new DiagramDocument
        {
            PageWidth = document.PageWidth,
            PageHeight = document.PageHeight
        };

        // Walk the document rather than the selection so z-order survives the round trip.
        foreach (var shape in document.Shapes.Where(wanted.Contains))
            slice.Shapes.Add(shape);

        return DiagramFile.ToJson(slice);
    }

    /// <summary>True when the text looks like something this clipboard wrote.</summary>
    public static bool CanPaste(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            return DiagramFile.FromJson(json).Shapes.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Adds a copy of the clipboard contents to the document, offset by <paramref name="offset"/>
    /// and nudged back onto the page if that would push it off. The pasted shapes become the
    /// selection, and the whole paste is a single undo step.
    /// </summary>
    public static IReadOnlyList<DiagramShape> Paste(DiagramDocument document, string json, Vector offset)
    {
        List<DiagramShape> pasted;

        try
        {
            pasted = DiagramFile.FromJson(json).Shapes.ToList();
        }
        catch
        {
            return [];
        }

        if (pasted.Count == 0)
            return [];

        Translate(document, pasted, offset);

        using (document.BeginBatch())
        {
            // A pasted copy of a group is a group of its own, not more members of the one it
            // was copied from - which may well be sitting on the page already.
            document.RenumberGroups(pasted);

            foreach (var shape in pasted)
                document.Shapes.Add(shape);

            document.MarkModified();
            document.SetSelection(pasted);
        }

        return pasted;
    }

    /// <summary>
    /// Moves the whole group by one offset, so the shapes keep their positions relative to
    /// each other, and pulls the group back if the offset would take it off the page.
    /// </summary>
    private static void Translate(DiagramDocument document, IReadOnlyList<DiagramShape> shapes, Vector offset)
    {
        var union = Union(shapes);
        var target = document.ClampToPage(new Rect(
            union.X + offset.X,
            union.Y + offset.Y,
            union.Width,
            union.Height));

        var applied = new Vector(target.X - union.X, target.Y - union.Y);

        foreach (var shape in shapes)
            shape.Translate(applied);
    }

    /// <summary>The rectangle covering every shape given.</summary>
    public static Rect Union(IReadOnlyList<DiagramShape> shapes)
    {
        if (shapes.Count == 0)
            return default;

        var union = shapes[0].Bounds;

        for (var i = 1; i < shapes.Count; i++)
            union = union.Union(shapes[i].Bounds);

        return union;
    }
}
