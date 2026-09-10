using System;
using System.Collections.Generic;
using System.Linq;

namespace AVASvgMaker.Engine;

/// <summary>
/// Undo history built on whole-document snapshots rather than per-operation commands.
/// Every edit is already serialisable, so a snapshot is cheap to take and impossible to
/// get subtly wrong - there is no per-operation inverse to keep in step with the editor.
/// </summary>
public class UndoStack
{
    /// <summary>How many steps back the history goes before the oldest is dropped.</summary>
    private const int Limit = 100;

    private readonly DiagramDocument _document;
    private readonly List<Snapshot> _undo = [];
    private readonly List<Snapshot> _redo = [];

    private Snapshot _current;

    /// <summary>The snapshot that matches what is on disk, so undoing back to it clears the modified flag.</summary>
    private string _savedJson;

    private bool _restoring;

    /// <summary>Raised when undo or redo becomes available or unavailable.</summary>
    public event Action? StateChanged;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public UndoStack(DiagramDocument document)
    {
        _document = document;
        _current = Capture();
        _savedJson = _current.Json;

        document.Changed += OnDocumentChanged;
        document.SelectionChanged += OnSelectionChanged;
    }

    /// <summary>Selection is not part of the saved document, so it is carried alongside the snapshot.</summary>
    private sealed record Snapshot(string Json, int[] SelectedIndices);

    private Snapshot Capture() => new(DiagramFile.ToJson(_document), SelectedIndices());

    private int[] SelectedIndices() => _document.Selection
        .Select(shape => _document.Shapes.IndexOf(shape))
        .Where(index => index >= 0)
        .ToArray();

    /// <summary>
    /// Selection moves without changing the document, so it is folded into the current
    /// snapshot rather than recorded as a step of its own. Undo then restores the
    /// selection as it stood just before the edit.
    /// </summary>
    private void OnSelectionChanged()
    {
        if (_restoring)
            return;

        _current = _current with { SelectedIndices = SelectedIndices() };
    }

    private void OnDocumentChanged()
    {
        if (_restoring)
            return;

        var snapshot = Capture();

        // Some edits do not actually alter the page; they do not deserve a step.
        if (snapshot.Json == _current.Json)
            return;

        _undo.Add(_current);

        if (_undo.Count > Limit)
            _undo.RemoveAt(0);

        _redo.Clear();
        _current = snapshot;

        StateChanged?.Invoke();
    }

    public void Undo()
    {
        if (!CanUndo)
            return;

        _redo.Add(_current);
        Restore(TakeLast(_undo));
    }

    public void Redo()
    {
        if (!CanRedo)
            return;

        _undo.Add(_current);
        Restore(TakeLast(_redo));
    }

    /// <summary>Forgets the history - after opening a file or starting a new document.</summary>
    public void Reset()
    {
        _undo.Clear();
        _redo.Clear();

        _current = Capture();
        _savedJson = _current.Json;

        StateChanged?.Invoke();
    }

    /// <summary>Records that the current state is the one on disk.</summary>
    public void MarkSaved() => _savedJson = _current.Json;

    private void Restore(Snapshot snapshot)
    {
        _restoring = true;

        try
        {
            _document.ReplaceWith(DiagramFile.FromJson(snapshot.Json));

            _document.SetSelection(snapshot.SelectedIndices
                .Where(index => index >= 0 && index < _document.Shapes.Count)
                .Select(index => _document.Shapes[index]));

            _current = snapshot;

            // Stepping back onto the saved state means there is nothing left to save.
            if (snapshot.Json == _savedJson)
                _document.MarkSaved();
            else
                _document.MarkModified();
        }
        finally
        {
            _restoring = false;
        }

        StateChanged?.Invoke();
    }

    private static Snapshot TakeLast(List<Snapshot> list)
    {
        var snapshot = list[^1];
        list.RemoveAt(list.Count - 1);
        return snapshot;
    }
}
