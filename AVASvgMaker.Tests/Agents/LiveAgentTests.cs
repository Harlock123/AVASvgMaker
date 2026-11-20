using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AVASvgMaker.Agents.Chat;
using AVASvgMaker.Agents.Mcp;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace AVASvgMaker.Tests.Agents;

/// <summary>Agents in the open window: the live host, the AI menu, the chat panel and the doors.</summary>
public class LiveAgentTests
{
    private static MainWindow Open()
    {
        var (window, _) = Harness.Editor();
        window.AgentPipe = "avasvgmaker-test-" + Guid.NewGuid().ToString("N");
        window.ChatKeys = (new ApiKeyStore(null, null), new ApiKeyStore(null, null));
        return window;
    }

    [AvaloniaFact]
    public void An_agents_calls_draw_in_the_window_and_ctrl_z_undoes_them()
    {
        var w = Open();
        var root = Directory.CreateTempSubdirectory("avasvg-root-").FullName;
        var bridge = AgentTests.Wait(McpToolBridge.StartAsync(w.Agents.Host(root)));
        var input = JsonSerializer.SerializeToElement(new { shapes = new object[] { new { kind = "Decision", text = "Ready?", x = 100, y = 100 } } })
            .EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        var outcome = AgentTests.Wait(bridge.CallAsync("add_shapes", input));
        Assert.False(outcome.IsError, outcome.Text);
        Assert.Equal("Ready?", Assert.Single(w.Canvas.Document.Shapes).Text);
        Assert.Equal("AI agent: added shapes.", w.StatusText.Text);

        w.Canvas.Focus();
        Harness.Press(w, Key.Z, RawInputModifiers.Control);
        Assert.Empty(w.Canvas.Document.Shapes);
        AgentTests.Wait(bridge.DisposeAsync().AsTask().ContinueWith(_ => true));
    }

    [AvaloniaFact]
    public void F12_shows_the_ai_chat_and_allowing_agents_opens_the_door()
    {
        var w = Open();
        Assert.False(w.ChatHolder.IsVisible);
        Harness.Press(w, Key.F12);
        Assert.True(w.ChatHolder.IsVisible);
        Assert.NotNull(w.Chat);
        Assert.Contains("draw something", w.Chat!.Input.Watermark);

        Assert.StartsWith("AI agents may connect", w.SetAllowAgents(true));
        Assert.True(LiveListener.Answers(w.AgentPipe));
        Assert.Equal("AI agents are shut out.", w.SetAllowAgents(false));
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.Agents.Running);
    }

    [AvaloniaFact]
    public void An_outside_agent_attaches_through_the_pipe_and_draws_in_the_window()
    {
        var w = Open();
        Assert.StartsWith("AI agents may connect", w.SetAllowAgents(true));
        var root = Directory.CreateTempSubdirectory("avasvg-root-").FullName;
        var bridge = new LiveBridge(w.AgentPipe, root);
        Pipe up = new(), down = new();
        var server = McpServer.Create(new StreamServerTransport(up.Reader.AsStream(), down.Writer.AsStream(), "attach"), bridge.Options());
        _ = Task.Run(() => server.RunAsync());
        var client = AgentTests.Wait(McpClient.CreateAsync(new StreamClientTransport(up.Writer.AsStream(), down.Reader.AsStream())));
        Assert.Contains("live app", client.ServerInstructions);
        var result = AgentTests.Wait(client.CallToolAsync("import_mermaid", new Dictionary<string, object?> { ["code"] = "flowchart TD\n A --> B" }).AsTask());
        Assert.False(result.IsError == true, string.Join(" ", result.Content.OfType<TextContentBlock>().Select(t => t.Text)));
        Assert.Equal(2, w.Canvas.Document.Pages.Count);
        AgentTests.Wait(client.DisposeAsync().AsTask().ContinueWith(_ => true));
        w.SetAllowAgents(false);
    }

    [AvaloniaFact]
    public void The_network_door_opens_on_the_settings_and_wants_the_token()
    {
        var w = Open();
        w.AgentSettings.AllowNetworkAgents = true;
        w.AgentSettings.NetworkAgentsAddress = "localhost";
        w.AgentSettings.NetworkAgentsPort = 0;
        w.AgentSettings.NetworkAgentsFolder = Directory.CreateTempSubdirectory("avasvg-net-").FullName;
        Assert.StartsWith("AI agents on other computers may connect", AgentTests.Wait(w.SyncNetworkAsync()));
        using var http = new System.Net.Http.HttpClient();
        var refused = AgentTests.Wait(http.PostAsync(w.Agents.NetworkUrls.First(), new System.Net.Http.StringContent("{}", System.Text.Encoding.UTF8, "application/json")));
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, refused.StatusCode);
        w.AgentSettings.Save();
        Assert.DoesNotContain(w.NetworkToken, File.ReadAllText(AVASvgMaker.Agents.AgentSettings.Path));
        w.AgentSettings.AllowNetworkAgents = false;
        Assert.Equal("AI agents on other computers are shut out.", AgentTests.Wait(w.SyncNetworkAsync()));
    }
}
