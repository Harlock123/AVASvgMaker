using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using AVASvgMaker.Agents.Mcp;
using AVASvgMaker.Engine;

namespace AVASvgMaker.Agents;

/// <summary>The window's drawing as an agent sees it: its document, its undo history, its file, and opening another.</summary>
internal sealed class WindowSession(MainWindow window) : IDiagramSession
{
    public DiagramDocument Document => window.Canvas.Document;

    public UndoStack History => window.History;

    public string? FilePath => window.CurrentFilePath;

    public void Replace(DiagramDocument document, string? path) => window.AgentAdopt(document, path);

    public void Saved(string path) => window.AgentSaved(path);

    public void Refresh() => window.AgentRefresh();
}

/// <summary>
/// The window's door for AI agents, and the live host they and the AI Chat work through.
/// <para>
/// While agents are allowed (AI › Allow AI Agents), a <see cref="LiveListener"/> takes connections from
/// <c>AVASvgMaker --mcp --attach</c> bridges (and, while agents on other computers are allowed, a
/// <see cref="NetworkListener"/> MCP clients on the network). Every call runs on the UI thread against the window's
/// drawing, so the canvas redraws as the agent works and Ctrl+Z undoes its calls. The AI Chat gets a host of its own
/// (<see cref="ChatHost"/>). Calls wait while a dialog box is open.
/// </para>
/// </summary>
internal sealed class LiveAgents(MainWindow window) : IDiagramDesk, IUiThread, IAsyncDisposable
{
    private LiveListener? _listener;
    private NetworkListener? _network;
    private readonly WindowSession _session = new(window);

    public EngineLibrary Library { get; } = new();

    public bool Running => _listener != null;

    public TimeSpan BusyWait { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Raised (on the UI thread) when an outside agent connects or goes, with how many are connected.</summary>
    public event Action<int>? ConnectionsChanged;

    public IDiagramSession Session => _session;

    public string? Busy => window.OwnedWindows.Count > 0 || ChatLook.ModalsOpen > 0 ? "a dialog box is open in AVASvgMaker" : null;

    public void Report(string text) => window.StatusText.Text = text;

    public Task<T> InvokeAsync<T>(Func<Task<T>> work) => Dispatcher.UIThread.InvokeAsync(work);

    /// <summary>The host outside agents work through, in <paramref name="root"/>.</summary>
    public DiagramHost Host(string root) => new(this, this, Library, root) { BusyWait = BusyWait, Live = true };

    /// <summary>The host the AI Chat runs its in-process MCP server on; its status lines name the chat's model.</summary>
    public DiagramHost ChatHost(string root, Func<string> who) => new(this, new ChatDesk(this, who), Library, root) { BusyWait = BusyWait, Live = true };

    private sealed class ChatDesk(LiveAgents agents, Func<string> who) : IDiagramDesk
    {
        public IDiagramSession Session => agents.Session;

        public string? Busy => agents.Busy;

        public void Report(string text) =>
            agents.Report(text.StartsWith("AI agent: ", StringComparison.Ordinal) ? who() + ": " + text["AI agent: ".Length..] : text);
    }

    /// <summary>Opens the door on a pipe; returns why it couldn't (another AVASvgMaker has it), or null.</summary>
    public string? Start(string pipeName, Func<string> root)
    {
        if (_listener != null) return null;
        var listener = new LiveListener(pipeName, asked => Host(Directory.Exists(asked) ? asked : root()));
        listener.ConnectionsChanged += n => Dispatcher.UIThread.Post(() => ConnectionsChanged?.Invoke(n));
        try
        {
            listener.Start();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _ = listener.DisposeAsync();
            return e.Message;
        }
        _listener = listener;
        return null;
    }

    public async Task StopAsync()
    {
        var listener = _listener;
        _listener = null;
        if (listener == null) return;
        listener.Stop();
        await listener.DisposeAsync();
    }

    public bool NetworkRunning => _network != null;

    public NetworkSettings? NetworkSettings { get; private set; }

    public string? NetworkRoot { get; private set; }

    public IReadOnlyList<string> NetworkUrls => _network?.Urls ?? [];

    public async Task<string?> StartNetworkAsync(NetworkSettings settings, string root)
    {
        if (_network != null) return null;
        if (!Directory.Exists(root)) return $"the working folder {root} doesn't exist.";
        var listener = new NetworkListener(settings, DiagramServer.Name, o => DiagramServer.Configure(o, Host(root)));
        listener.ConnectionsChanged += n => Dispatcher.UIThread.Post(() => ConnectionsChanged?.Invoke(n));
        try
        {
            await listener.StartAsync();
        }
        catch (IOException e)
        {
            return e.Message;
        }
        _network = listener;
        NetworkSettings = settings;
        NetworkRoot = root;
        return null;
    }

    public async Task StopNetworkAsync()
    {
        var listener = _network;
        _network = null;
        NetworkSettings = null;
        NetworkRoot = null;
        if (listener != null) await listener.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await StopNetworkAsync();
    }
}
