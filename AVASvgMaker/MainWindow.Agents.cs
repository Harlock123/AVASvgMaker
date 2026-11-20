using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AVASvgMaker.Agents;
using AVASvgMaker.Agents.Chat;
using AVASvgMaker.Agents.Mcp;
using AVASvgMaker.Engine;
using AVASvgMaker.Views;

namespace AVASvgMaker;

/// <summary>
/// The window's AI side: the AI menu, the AI Chat docked on the right (F12), the door for agents outside the program
/// (AI › Allow AI Agents: <c>AVASvgMaker --mcp --attach</c>) and the network door (AI › Connect an Agent).
/// </summary>
public partial class MainWindow
{
    private LazyToolBridge? _chatTools;
    private string? _networkToken;
    private string? _agentFilePath;

    internal AgentSettings AgentSettings { get; private set; } = null!;

    internal LiveAgents Agents { get; private set; } = null!;

    internal ChatPanel? Chat { get; private set; }

    internal UndoStack History => _history;

    /// <summary>The pipe outside agents connect on (tests give one of their own).</summary>
    public string AgentPipe { get; set; } = LiveProtocol.PipeName;

    /// <summary>Tests' own API key stores (never the person's real keys).</summary>
    internal (ApiKeyStore Keys, ApiKeyStore ServerKeys)? ChatKeys { get; set; }

    /// <summary>The drawing's file, if it has one.</summary>
    internal string? CurrentFilePath => _currentFile?.TryGetLocalPath() ?? _agentFilePath;

    /// <summary>The folder the chat and attached agents work in: the one chosen, else the drawing's, else Documents.</summary>
    internal string AgentRoot =>
        AgentSettings.WorkingFolder is { Length: > 0 } chosen && Directory.Exists(chosen) ? chosen
        : CurrentFilePath is { } f && Path.GetDirectoryName(f) is { Length: > 0 } folder ? folder
        : DocumentsFolder;

    private static string DocumentsFolder =>
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) is { Length: > 0 } d && Directory.Exists(d) ? d
        : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private void SetUpAgents()
    {
        AgentSettings = AgentSettings.Load();
        Agents = new LiveAgents(this);
        Agents.ConnectionsChanged += n => StatusText.Text = n == 0 ? "No AI agents are connected now." : $"{n} AI agent{(n == 1 ? " is" : "s are")} connected.";
        AllowAgentsMenuItem.IsChecked = AgentSettings.AllowAgents;
        Opened += async (_, _) =>
        {
            if (AgentSettings.AllowAgents) SetAllowAgents(true);
            if (AgentSettings.AllowNetworkAgents && await SyncNetworkAsync() is { Length: > 0 } line) StatusText.Text = line;
            if (AgentSettings.ChatOpen) ToggleChat();
        };
        Closed += async (_, _) =>
        {
            AgentSettings.Save();
            if (_chatTools is not null) await _chatTools.DisposeAsync();
            await Agents.DisposeAsync();
        };
    }

    // ---- what agents do to the window's drawing ----

    internal void AgentAdopt(DiagramDocument document, string? path)
    {
        Canvas.CommitEdit();
        Canvas.Document.ReplaceWith(document);
        Canvas.Document.PageIndex = 0;
        Canvas.Document.ClearSelection();
        Canvas.Document.MarkSaved();
        _history.Reset();
        _currentFile = null;
        _agentFilePath = path;
        if (path is not null) _ = AdoptFileAsync(path);
        AgentRefresh();
    }

    internal void AgentSaved(string path)
    {
        Canvas.Document.MarkSaved();
        _history.MarkSaved();
        _currentFile = null;
        _agentFilePath = path;
        _ = AdoptFileAsync(path);
        UpdateTitle();
    }

    /// <summary>The window's own file, for Save, once the storage provider has it.</summary>
    private async Task AdoptFileAsync(string path)
    {
        try
        {
            if (await StorageProvider.TryGetFileFromPathAsync(path) is { } file && _agentFilePath == path)
            {
                _currentFile = file;
                UpdateTitle();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
    }

    internal void AgentRefresh()
    {
        Canvas.SyncPageSize();
        Canvas.InvalidateVisual();
        SyncPages();
        UpdateTitle();
    }

    // ---- the AI menu ----

    private void OnToggleChatClick(object? sender, RoutedEventArgs e) => ToggleChat();

    private void OnAllowAgentsClick(object? sender, RoutedEventArgs e) => StatusText.Text = SetAllowAgents(!AgentSettings.AllowAgents);

    private void OnConnectAgentClick(object? sender, RoutedEventArgs e) => _ = ShowConnect();

    private async void OnWorkingFolderClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "The folder AI agents read and write in", AllowMultiple = false });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } path) return;
        AgentSettings.WorkingFolder = path;
        AgentSettings.Save();
        await RestartChatToolsAsync();
        StatusText.Text = $"AI agents now read and write in {path}.";
    }

    /// <summary>The chat's tools start again in the new working folder (its conversation stays).</summary>
    private async Task RestartChatToolsAsync()
    {
        if (_toolsHolder is null || Chat is null) return;
        var old = _toolsHolder.Bridge;
        _toolsHolder.Bridge = NewChatTools(Chat);
        _chatTools = _toolsHolder.Bridge;
        await old.DisposeAsync();
    }

    /// <summary>Lets agents outside the program in, or shuts them out, and remembers it.</summary>
    public string SetAllowAgents(bool allow)
    {
        if (AgentSettings.AllowAgents != allow)
        {
            AgentSettings.AllowAgents = allow;
            AgentSettings.Save();
        }
        AllowAgentsMenuItem.IsChecked = allow;
        if (!allow)
        {
            _ = Agents.StopAsync();
            return "AI agents are shut out.";
        }
        return Agents.Start(AgentPipe, () => AgentRoot) is { } problem
            ? $"AI agents can't connect: {problem}"
            : "AI agents may connect (AVASvgMaker --mcp --attach). They work on the drawing in this window.";
    }

    public string NetworkToken => _networkToken ??= TryToken(t => t.Get());

    private static string TryToken(Func<NetworkToken, string> get)
    {
        try
        {
            return get(global::AVASvgMaker.Agents.Mcp.NetworkToken.In(AgentSettings.Folder));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return NetworkListener.NewToken();
        }
    }

    public async Task<string> SyncNetworkAsync()
    {
        var s = AgentSettings;
        var want = s.AllowNetworkAgents
            ? new NetworkSettings(s.NetworkAgentsAddress, s.NetworkAgentsPort, NetworkToken, Blank(s.NetworkAgentsCertificate), Blank(s.NetworkAgentsKey))
            : null;
        var root = Blank(s.NetworkAgentsFolder) ?? DocumentsFolder;
        if (Agents.NetworkRunning && (want == null || want != Agents.NetworkSettings || root != Agents.NetworkRoot))
        {
            await Agents.StopNetworkAsync();
            if (want == null) return "AI agents on other computers are shut out.";
        }
        if (want == null || Agents.NetworkRunning) return "";
        if (want.Problem() is { } bad) return $"AI agents on other computers can't connect: {bad}";
        return await Agents.StartNetworkAsync(want, root) is { } problem
            ? $"AI agents on other computers can't connect: {problem}"
            : $"AI agents on other computers may connect (MCP): {Agents.NetworkUrls.FirstOrDefault()}, with the access token (AI › Connect an Agent).";
    }

    private static string? Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>How to connect an agent: on this computer through the pipe, or from another over the network.</summary>
    public async Task ShowConnect()
    {
        var exe = Environment.ProcessPath ?? "AVASvgMaker";
        var local = new SelectableTextBlock
        {
            Foreground = AppTheme.Text, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Text = $"On this computer: turn on AI › Allow AI Agents (it's {(AgentSettings.AllowAgents ? "on" : "off")} now), then add the server to the agent. For Claude Code:\n\n" +
                $"    claude mcp add avasvgmaker -- \"{exe}\" --mcp --attach\n\n" +
                $"The agent then draws in this window, reading and writing files in {AgentRoot} (AI › Working Folder changes it), and Ctrl+Z undoes its steps. Without --attach the server has a drawing of its own and no window.",
        };
        var allow = new CheckBox { Content = "Let AI agents on other computers connect (MCP over HTTP)", IsChecked = AgentSettings.AllowNetworkAgents, Foreground = AppTheme.Text };
        var port = new TextBox { Text = AgentSettings.NetworkAgentsPort.ToString(System.Globalization.CultureInfo.InvariantCulture), Width = 80 };
        var folder = new TextBox { Text = AgentSettings.NetworkAgentsFolder, Watermark = DocumentsFolder, MinWidth = 300 };
        var token = new TextBox { Text = new string('•', 20), IsReadOnly = true, Width = 300 };
        var show = new Button { Content = "Show" };
        show.Click += (_, _) => token.Text = NetworkToken;
        var copy = new Button { Content = "Copy" };
        copy.Click += async (_, _) => { if (Clipboard is { } c) await c.SetTextAsync(NetworkToken); };
        var renew = new Button { Content = "New token" };
        renew.Click += (_, _) => { _networkToken = TryToken(t => t.Renew()); token.Text = NetworkToken; };
        var where = new SelectableTextBlock { Foreground = AppTheme.Text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
        void ShowWhere() => where.Text = Agents.NetworkRunning
            ? "Listening at " + string.Join(", ", Agents.NetworkUrls) + ". Agents send \"Authorization: Bearer <token>\"."
            : "Not listening.";
        ShowWhere();
        var close = new Button { Content = "Close", MinWidth = 90 };
        var apply = new Button { Content = "Apply", MinWidth = 90 };
        var dialog = new Window
        {
            Title = "Connect an Agent", Width = 620, SizeToContent = SizeToContent.Height, CanResize = false,
            Background = AppTheme.Panel,
        };
        apply.Click += async (_, _) =>
        {
            if (!int.TryParse(port.Text, out var p) || p is < 1 or > 65535) { where.Text = "The port is a number from 1 to 65535."; return; }
            AgentSettings.AllowNetworkAgents = allow.IsChecked == true;
            AgentSettings.NetworkAgentsPort = p;
            AgentSettings.NetworkAgentsFolder = folder.Text?.Trim() ?? "";
            AgentSettings.Save();
            var line = await SyncNetworkAsync();
            ShowWhere();
            if (line.Length > 0) { StatusText.Text = line; if (line.Contains("can't")) where.Text = line; }
        };
        close.Click += (_, _) => dialog.Close();
        Label Words(string text) => new() { Content = text, Foreground = AppTheme.Text, VerticalAlignment = VerticalAlignment.Center };
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 12,
            Children =
            {
                local,
                new Separator(),
                allow,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Words("Port"), port, Words("Working folder"), folder } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Words("Access token"), token, show, copy, renew } },
                where,
                new TextBlock
                {
                    Foreground = AppTheme.Text, Opacity = 0.7, FontSize = 11, TextWrapping = TextWrapping.Wrap,
                    Text = "Plain HTTP sends the token and the work unencrypted: use it on a network you trust, or run AVASvgMaker --mcp --http with --cert and --key for HTTPS. Anyone with the token can change the open drawing.",
                },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { apply, close } },
            },
        };
        await dialog.ShowCentred(this);
    }

    // ---- the AI Chat ----

    /// <summary>Shows or hides the AI Chat (F12), making it the first time.</summary>
    public void ToggleChat()
    {
        if (Chat is null) MakeChat();
        ChatHolder.IsVisible = !ChatHolder.IsVisible;
        ChatMenuItem.IsChecked = ChatHolder.IsVisible;
        if (AgentSettings.ChatOpen != ChatHolder.IsVisible)
        {
            AgentSettings.ChatOpen = ChatHolder.IsVisible;
            AgentSettings.Save();
        }
        if (ChatHolder.IsVisible) Chat!.Input.Focus();
        else Canvas.Focus();
    }

    private LazyToolBridge NewChatTools(ChatPanel panel) =>
        new(async cancel => await McpToolBridge.StartAsync(Agents.ChatHost(AgentRoot, () => panel.Session.Provider == ChatProvider.Anthropic ? "Claude" : "AI Chat"), cancel));

    private void MakeChat()
    {
        var folder = AgentSettings.Folder;
        var keys = ChatKeys?.Keys ?? ApiKeyStore.ForApp(folder);
        var serverKeys = ChatKeys?.ServerKeys ?? ApiKeyStore.ForServer(folder);
        var holder = new ToolsHolder();
        ChatPanel? panel = null;
        holder.Bridge = new LazyToolBridge(async cancel => await McpToolBridge.StartAsync(Agents.ChatHost(AgentRoot, () => panel?.Session.Provider == ChatProvider.Anthropic ? "Claude" : "AI Chat"), cancel));
        _chatTools = holder.Bridge;
        panel = new ChatPanel(holder, keys, text => StatusText.Text = text, AgentSettings, serverKeys) { SaveSettings = AgentSettings.Save };
        panel.CloseRequested += ToggleChat;
        if (ChatKeys is null)
        {
            panel.History = new ChatHistory(Path.Combine(folder, "Chats"));
            panel.Ledger = new UsageLedger(Path.Combine(folder, "chat-usage.json"));
            panel.RestoreLast();
        }
        Chat = panel;
        _toolsHolder = holder;
        ChatHolder.Child = panel;
    }

    private ToolsHolder? _toolsHolder;

    /// <summary>The chat's tools, which can be swapped for a new bridge when the working folder changes.</summary>
    private sealed class ToolsHolder : IToolBridge
    {
        private LazyToolBridge _bridge = null!;
        private Func<string, System.Threading.CancellationToken, Task<bool>>? _ask;

        public LazyToolBridge Bridge
        {
            get => _bridge;
            set
            {
                _bridge = value;
                _bridge.Ask = _ask;
            }
        }

        public Task<System.Collections.Generic.IReadOnlyList<ModelContextProtocol.Protocol.Tool>> ListToolsAsync(System.Threading.CancellationToken cancel = default) => Bridge.ListToolsAsync(cancel);

        public Task<ToolOutcome> CallAsync(string name, System.Collections.Generic.IReadOnlyDictionary<string, System.Text.Json.JsonElement> input, System.Threading.CancellationToken cancel = default) => Bridge.CallAsync(name, input, cancel);

        public Task<ToolOutcome> CallAsync(string name, System.Collections.Generic.IReadOnlyDictionary<string, System.Text.Json.JsonElement> input, IProgress<(double Done, string? Message)>? progress, System.Threading.CancellationToken cancel = default) => Bridge.CallAsync(name, input, progress, cancel);

        public Func<string, System.Threading.CancellationToken, Task<bool>>? Ask
        {
            get => _ask;
            set
            {
                _ask = value;
                _bridge.Ask = value;
            }
        }

        public string? CheckpointFolder { get => Bridge.CheckpointFolder; set => Bridge.CheckpointFolder = value; }
    }
}
