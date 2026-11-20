using System.Net.Http;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;

namespace AVASvgMaker.Agents.Mcp;

/// <summary>
/// What the server needs from the program that it can't do itself: drawing a page as a picture and writing a PDF,
/// both of which need Avalonia's renderer. The program supplies it; everything here runs on the host's thread.
/// </summary>
public interface IDiagramLibrary
{
    /// <summary>A page drawn as a PNG, at <paramref name="scale"/> pixels per page unit (a page unit is 1/96 inch).</summary>
    byte[] Picture(DiagramDocument document, int page, double scale);

    /// <summary>Writes the drawing (every page) as a PDF.</summary>
    Task ExportPdf(DiagramDocument document, string path);
}

/// <summary>
/// The drawing an agent works on, and what goes with it: its document, its undo history and its file. Used on the
/// host's thread only (<see cref="IDiagramHost.RunAsync{T}"/>).
/// </summary>
public interface IDiagramSession
{
    /// <summary>The open drawing. Changes to it are undo steps in <see cref="History"/>, the person's own history.</summary>
    DiagramDocument Document { get; }

    UndoStack History { get; }

    /// <summary>The file the drawing was opened from or saved to, or null.</summary>
    string? FilePath { get; }

    /// <summary>Adopts another drawing (new, open): a fresh history, and not changed.</summary>
    void Replace(DiagramDocument document, string? path);

    /// <summary>The drawing was written to <paramref name="path"/>, which becomes its file.</summary>
    void Saved(string path);

    /// <summary>The drawing changed: the window redraws, sizes the canvas to the page and shows the page.</summary>
    void Refresh();
}

/// <summary>
/// Where the MCP tools' work runs. The handlers (<see cref="DiagramTools"/>) only touch the drawing through
/// <see cref="RunAsync{T}"/>, so the same handlers serve the headless server and the open window, both on the thread
/// Avalonia runs on.
/// </summary>
public interface IDiagramHost
{
    /// <summary>The folder relative paths start from; open, save, export and reading files stay inside it.</summary>
    string Root { get; }

    IDiagramLibrary Library { get; }

    /// <summary>
    /// Runs <paramref name="work"/> on the thread that owns the drawing, one call at a time, and returns what it
    /// returns (or throws what it throws). <paramref name="action"/> names it for the status bar.
    /// </summary>
    Task<T> RunAsync<T>(string action, Func<IDiagramSession, Task<T>> work, CancellationToken cancel = default);
}

public static class DiagramHostExtensions
{
    public static Task<T> RunAsync<T>(this IDiagramHost host, string action, Func<IDiagramSession, T> work, CancellationToken cancel = default) =>
        host.RunAsync(action, s => Task.FromResult(work(s)), cancel);
}

/// <summary>Runs work on a UI thread (the program's Avalonia dispatcher, or a test's).</summary>
public interface IUiThread
{
    Task<T> InvokeAsync<T>(Func<Task<T>> work);
}

/// <summary>The program as the host sees it: the drawing, whether it can take a call now, and the status bar.</summary>
public interface IDiagramDesk
{
    IDiagramSession Session { get; }

    /// <summary>Why the program can't take a call just now ("a dialog box is open"), or null. The call waits.</summary>
    string? Busy { get; }

    /// <summary>Tells the person what an agent just did.</summary>
    void Report(string text);
}

/// <summary>
/// The host: calls run against the desk's drawing on its UI thread, one at a time, so the canvas redraws and Ctrl+Z
/// undoes them like the person's own edits. While the person is busy a call waits up to <see cref="BusyWait"/>.
/// </summary>
public sealed class DiagramHost(IUiThread ui, IDiagramDesk desk, IDiagramLibrary library, string root) : IDiagramHost
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string Root { get; } = Path.GetFullPath(root);

    public IDiagramLibrary Library { get; } = library;

    /// <summary>Working in the open window (so a big deletion asks the person first).</summary>
    public bool Live { get; init; }

    public TimeSpan BusyWait { get; init; } = TimeSpan.FromSeconds(20);

    public TimeSpan BusyPoll { get; init; } = TimeSpan.FromMilliseconds(200);

    public async Task<T> RunAsync<T>(string action, Func<IDiagramSession, Task<T>> work, CancellationToken cancel = default)
    {
        await _gate.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            var clock = Stopwatch.StartNew();
            while (true)
            {
                cancel.ThrowIfCancellationRequested();
                var (ran, result, busy) = await ui.InvokeAsync(async () =>
                {
                    if (desk.Busy is { } why) return (false, default(T), why);
                    try
                    {
                        var r = await work(desk.Session);
                        desk.Report($"AI agent: {action}.");
                        return (true, r, (string?)null);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        desk.Report($"AI agent: {action} failed: {e.Message}");
                        throw;
                    }
                }).ConfigureAwait(false);
                if (ran) return result!;
                if (clock.Elapsed >= BusyWait)
                    throw new DiagramBusyException($"AVASvgMaker is busy: {busy}. The call waited {BusyWait.TotalSeconds:0} seconds; ask the person to finish, then try again.");
                await Task.Delay(BusyPoll, cancel).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}

public sealed class DiagramBusyException(string message) : Exception(message);

/// <summary>A drawing with no window round it, as <c>AVASvgMaker --mcp</c> serves it.</summary>
public sealed class HeadlessSession : IDiagramSession
{
    public HeadlessSession() => History = new UndoStack(Document);

    public DiagramDocument Document { get; } = new();

    public UndoStack History { get; }

    public string? FilePath { get; private set; }

    public void Replace(DiagramDocument document, string? path)
    {
        Document.ReplaceWith(document);
        Document.PageIndex = 0;
        Document.ClearSelection();
        Document.MarkSaved();
        History.Reset();
        FilePath = path;
    }

    public void Saved(string path)
    {
        FilePath = path;
        Document.MarkSaved();
        History.MarkSaved();
    }

    public void Refresh() { }
}

public sealed class HeadlessDesk(TextWriter? log = null) : IDiagramDesk
{
    public IDiagramSession Session { get; } = new HeadlessSession();

    public string? Busy => null;

    public void Report(string text) => log?.WriteLine(text);
}

/// <summary>
/// Shapes' lasting ids (<see cref="DiagramShape.Uid"/>): every shape on every page gets one, "s1", "s2"…, and a
/// pasted or duplicated shape that came with another's id gets a new one.
/// </summary>
public static class ShapeIds
{
    public static void Ensure(DiagramDocument document)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var all = document.Pages.SelectMany(p => p.Shapes).ToList();
        var next = all.Select(s => s.Uid.Length > 1 && s.Uid[0] == 's' && int.TryParse(s.Uid.AsSpan(1), out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
        foreach (var shape in all)
            if (shape.Uid.Length == 0 || !seen.Add(shape.Uid))
            {
                shape.Uid = "s" + next++;
                seen.Add(shape.Uid);
            }
    }

    /// <summary>The shape with an id, and the page (0-based) it is on.</summary>
    public static (DiagramShape Shape, int Page)? Find(DiagramDocument document, string id)
    {
        for (var p = 0; p < document.Pages.Count; p++)
            foreach (var shape in document.Pages[p].Shapes)
                if (string.Equals(shape.Uid, id.Trim(), StringComparison.OrdinalIgnoreCase)) return (shape, p);
        return null;
    }
}
