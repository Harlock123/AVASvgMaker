using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVASvgMaker.Agents;
using AVASvgMaker.Agents.Chat;
using AVASvgMaker.Agents.Mcp;
using AVASvgMaker.Engine;
using AVASvgMaker.Models;
using ModelContextProtocol.Server;
using Xunit;
using static AVASvgMaker.Tests.Agents.FakeModelServer;

namespace AVASvgMaker.Tests.Agents;

/// <summary>
/// The MCP server and the AI Chat, driven as agents drive them: through an in-process MCP client over the real server,
/// on Avalonia's headless platform. The chat's model is a scripted fake OpenAI-compatible server; no real model is
/// ever called.
/// </summary>
public class AgentTests
{
    /// <summary>Every tool the server offers. A new tool goes here, with descriptions on it and its arguments.</summary>
    internal static readonly string[] ToolNames =
    [
        "get_document", "new_drawing", "open_drawing", "save_drawing", "get_page", "add_page", "set_page", "delete_page",
        "list_stencils", "add_shapes", "connect", "set_shape", "delete_shapes", "arrange", "lay_out", "undo", "redo",
        "add_flowchart", "import_mermaid", "get_mermaid", "list_files", "read_file", "batch", "get_recording",
        "save_steps", "run_steps", "checkpoint", "diff", "render_checkpoint", "restore_checkpoint", "render_page", "export",
    ];

    internal const string Python = """
        def process_order(order):
            if not order.items:
                raise ValueError("empty order")
            total = 0
            for item in order.items:
                total += item.price * item.qty
            charge(order.customer, total)
            return total
        """;

    /// <summary>A server on a drawing of its own, in a temporary working folder, and a client connected to it.</summary>
    internal sealed class Rig : IDisposable
    {
        public HeadlessDesk Desk { get; } = new();
        public string Root { get; } = Directory.CreateTempSubdirectory("avasvg-agent-").FullName;
        public McpToolBridge Bridge { get; }
        public DiagramDocument Document => Desk.Session.Document;
        public UndoStack History => Desk.Session.History;

        public Rig()
        {
            Directory.CreateDirectory(Path.Combine(Root, "src"));
            File.WriteAllText(Path.Combine(Root, "src", "main.py"), Python);
            Bridge = Wait(McpToolBridge.StartAsync(new DiagramHost(new AvaloniaThread(), Desk, new EngineLibrary(), Root)));
        }

        public ToolOutcome Call(string tool, object? args = null) =>
            Wait(Bridge.CallAsync(tool, JsonSerializer.SerializeToElement(args ?? new { }).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone())));

        public JsonElement Ok(string tool, object? args = null)
        {
            var o = Call(tool, args);
            Assert.False(o.IsError, $"{tool} failed: {o.Text}");
            return o.Text.StartsWith('{') ? JsonDocument.Parse(o.Text).RootElement.Clone() : default;
        }

        public void Dispose() => Wait(Bridge.DisposeAsync().AsTask().ContinueWith(_ => true));
    }

    internal static T Wait<T>(Task<T> task)
    {
        for (var i = 0; i < 6000 && !task.IsCompleted; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(2);
        }
        Assert.True(task.IsCompleted, "It didn't finish in time.");
        Dispatcher.UIThread.RunJobs();
        return task.GetAwaiter().GetResult();
    }

    [AvaloniaFact]
    public void The_tools_are_the_ones_listed_and_each_says_what_it_and_its_arguments_are()
    {
        using var rig = new Rig();
        var tools = Wait(rig.Bridge.ListToolsAsync());
        Assert.Equal(ToolNames.Order(), tools.Select(t => t.Name).Order());
        foreach (var t in tools)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Description), $"{t.Name} has no description.");
            foreach (var p in t.InputSchema.GetProperty("properties").EnumerateObject())
                Assert.True(p.Value.TryGetProperty("description", out var d) && d.GetString()!.Length > 0, $"{t.Name}'s {p.Name} has no description.");
        }
    }

    [AvaloniaFact]
    public void Every_recipe_runs_call_by_call()
    {
        var recipes = typeof(Recipes).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.GetCustomAttribute<McpServerPromptAttribute>() != null).ToList();
        Assert.Equal(3, recipes.Count);
        foreach (var recipe in recipes)
        {
            using var rig = new Rig();
            var text = (string)recipe.Invoke(null, recipe.GetParameters().Select(_ => (object?)null).ToArray())!;
            var calls = text.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith(Recipes.CallMarker, StringComparison.Ordinal)).ToList();
            Assert.True(calls.Count >= 4, $"{recipe.Name} has few steps.");
            foreach (var call in calls)
            {
                var rest = call[Recipes.CallMarker.Length..];
                var space = rest.IndexOf(' ');
                var o = rig.Call(rest[..space], JsonDocument.Parse(rest[(space + 1)..]).RootElement);
                Assert.False(o.IsError, $"{recipe.Name}: {rest[..space]} failed: {o.Text}");
            }
            Assert.True(rig.Document.Pages.Sum(p => p.Shapes.Count) >= 5, $"{recipe.Name} drew little.");
        }
    }

    [AvaloniaFact]
    public void A_source_file_is_read_and_charted_in_one_undo_step()
    {
        using var rig = new Rig();
        var read = rig.Ok("read_file", new { path = "src/main.py" });
        Assert.Contains("    1  def process_order(order):", read.GetProperty("text").GetString());
        var chart = rig.Ok("add_flowchart", new
        {
            title = "process_order",
            nodes = new object[]
            {
                new { id = "s", label = "Start", kind = "start" },
                new { id = "has", label = "Order has items?", kind = "decision" },
                new { id = "err", label = "Raise: empty order", kind = "end" },
                new { id = "sum", label = "Add up the items", kind = "process" },
                new { id = "charge", label = "Charge the customer", kind = "subroutine" },
                new { id = "e", label = "Return the total", kind = "end" },
            },
            edges = new object[]
            {
                new { from = "s", to = "has" }, new { from = "has", to = "err", label = "no" }, new { from = "has", to = "sum", label = "yes" },
                new { from = "sum", to = "charge" }, new { from = "charge", to = "e" },
            },
        });
        Assert.Equal(2, chart.GetProperty("page").GetInt32());
        var page = rig.Document.Pages[1];
        Assert.Equal("process_order", page.Name);
        Assert.Equal(5, page.Shapes.OfType<ConnectorShape>().Count());
        var decision = page.Shapes.Single(x => x.Text == "Order has items?");
        Assert.Equal(ShapeKind.Diamond, decision.Kind);
        Assert.Equal(chart.GetProperty("ids").GetProperty("has").GetString(), decision.Uid);
        Assert.Contains(page.Shapes.OfType<ConnectorShape>(), c => c.Text == "no" && c.StartShape == decision);
        // Laid out down the page: the start above the decision above the end.
        Assert.True(page.Shapes.Single(x => x.Text == "Start").Bounds.Y < decision.Bounds.Y);
        Assert.True(decision.Bounds.Y < page.Shapes.Single(x => x.Text == "Return the total").Bounds.Y);

        rig.Ok("undo");
        Assert.Single(rig.Document.Pages);
    }

    [AvaloniaFact]
    public void Files_outside_the_working_folder_and_binary_ones_are_refused()
    {
        using var rig = new Rig();
        Assert.Contains("outside the working folder", rig.Call("read_file", new { path = "../../etc/passwd" }).Text);
        File.WriteAllBytes(Path.Combine(rig.Root, "blob.bin"), [1, 2, 0, 3]);
        Assert.Contains("isn't text", rig.Call("read_file", new { path = "blob.bin" }).Text);
        var listed = rig.Ok("list_files", new { recursive = true, pattern = "*.py" });
        Assert.Equal("src/main.py", listed.GetProperty("files")[0].GetProperty("path").GetString()!.Replace('\\', '/'));
        File.WriteAllText(Path.Combine(rig.Root, "long.txt"), string.Join("\n", Enumerable.Range(1, 50).Select(i => $"line {i}")));
        var piece = rig.Ok("read_file", new { path = "long.txt", start_line = 11, max_lines = 10 });
        Assert.Equal(20, piece.GetProperty("to").GetInt32());
        Assert.Contains("start_line 21", piece.GetProperty("more").GetString());
    }

    [AvaloniaFact]
    public void A_call_that_fails_changes_nothing_and_a_batch_is_one_step_or_none()
    {
        using var rig = new Rig();
        var made = rig.Ok("add_shapes", new { shapes = new object[] { new { kind = "Process", text = "A", x = 100, y = 100 }, new { kind = "Decision", text = "B?", x = 100, y = 250 } } });
        var (a, b) = (made.GetProperty("ids")[0].GetString()!, made.GetProperty("ids")[1].GetString()!);
        var before = DiagramFile.ToJson(rig.Document);
        var depth = rig.History.Depth;

        var bad = rig.Call("connect", new { links = new object[] { new { from = a, to = "s999" } } });
        Assert.True(bad.IsError);
        Assert.Equal(before, DiagramFile.ToJson(rig.Document));

        var failed = rig.Call("batch", new { steps = new object[] { new { tool = "set_shape", args = new { id = a, fill = "#FFEEAA" } }, new { tool = "set_shape", args = new { id = "nope", text = "x" } } } });
        Assert.Contains("Step 2 (set_shape) failed", failed.Text);
        Assert.Equal(before, DiagramFile.ToJson(rig.Document));
        Assert.Equal(depth, rig.History.Depth);

        var dry = rig.Ok("batch", new { dry_run = true, steps = new object[] { new { tool = "connect", args = new { links = new object[] { new { from = a, to = b } } } } } });
        Assert.Equal(1, dry.GetProperty("would_change").GetProperty("connectors").GetProperty("added").GetArrayLength());
        Assert.Equal(before, DiagramFile.ToJson(rig.Document));

        rig.Ok("batch", new
        {
            steps = new object[]
            {
                new { tool = "connect", args = new { links = new object[] { new { from = a, to = b, label = "go" } } } },
                new { tool = "set_shape", args = new { id = b, fill = "#FFEEAA" } },
                new { tool = "lay_out", args = new { } },
            },
        });
        Assert.Equal(depth + 1, rig.History.Depth);
        Assert.Single(rig.Document.Shapes.OfType<ConnectorShape>());
        rig.Ok("undo");
        Assert.Equal(before, DiagramFile.ToJson(rig.Document));
    }

    [AvaloniaFact]
    public void A_session_is_kept_as_steps_and_run_again_with_other_values()
    {
        using var rig = new Rig();
        rig.Ok("add_shapes", new { shapes = new object[] { new { kind = "Terminator", text = "Begin", x = 100, y = 100 } } });
        Assert.Equal("add_shapes", rig.Ok("get_recording").GetProperty("steps")[0].GetProperty("tool").GetString());
        rig.Ok("save_steps", new
        {
            path = "box",
            steps = new object[] { new { tool = "add_shapes", args = new { shapes = new object[] { new { kind = "Process", text = "${label}", x = "${left}", y = 300 } } } } },
            parameters = new { label = "Default", left = 40 },
        });
        Assert.True(File.Exists(Path.Combine(rig.Root, "box.avasteps.json")));
        rig.Ok("run_steps", new { path = "box.avasteps.json", values = new { label = "Ship it", left = 222 } });
        var made = rig.Document.Shapes.Single(x => x.Text == "Ship it");
        Assert.Equal(222, made.Bounds.X);
        Assert.Contains("no parameter", rig.Call("run_steps", new { path = "box.avasteps.json", values = new { colour = "red" } }).Text);
    }

    [AvaloniaFact]
    public void Ids_last_through_undo_saving_and_opening()
    {
        using var rig = new Rig();
        var id = rig.Ok("add_shapes", new { shapes = new object[] { new { kind = "Database", text = "Orders", x = 50, y = 50 } } }).GetProperty("ids")[0].GetString()!;
        rig.Ok("set_shape", new { id, text = "Orders DB" });
        rig.Ok("undo");
        Assert.Equal("Orders", ShapeIds.Find(rig.Document, id)!.Value.Shape.Text);
        rig.Ok("save_drawing", new { path = "kept" });
        rig.Ok("new_drawing");
        rig.Ok("open_drawing", new { path = "kept.avadiag" });
        Assert.Equal("Orders", ShapeIds.Find(rig.Document, id)!.Value.Shape.Text);
        Assert.Contains("\"uid\": \"" + id + "\"", File.ReadAllText(Path.Combine(rig.Root, "kept.avadiag")));
    }

    [AvaloniaFact]
    public void A_page_is_drawn_and_exported_in_every_format()
    {
        using var rig = new Rig();
        rig.Ok("import_mermaid", new { code = "flowchart TD\n  A[Start] --> B{Ready?}\n  B -->|yes| C[Go]\n  B -->|no| A" });
        var picture = rig.Call("render_page", new { scale = 0.5 });
        Assert.False(picture.IsError, picture.Text);
        Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, Assert.Single(picture.Images).Data[..4]);
        foreach (var f in new[] { "svg", "pdf", "png", "jpeg", "webp", "bmp", "mermaid", "vsdx" })
        {
            var o = rig.Ok("export", new { path = "out/chart", format = f, scale = 1 });
            Assert.True(new FileInfo(Path.Combine(rig.Root, o.GetProperty("exported").GetString()!)).Length > 50, f);
        }
        Assert.Contains("Ready?", File.ReadAllText(Path.Combine(rig.Root, "out", "chart.mmd")));
    }

    [AvaloniaFact]
    public void A_checkpoint_is_compared_with_and_gone_back_to()
    {
        using var rig = new Rig();
        rig.Ok("checkpoint", new { name = "before" });
        rig.Ok("add_shapes", new { shapes = new object[] { new { kind = "Process", text = "A", x = 10, y = 10 }, new { kind = "Process", text = "B", x = 10, y = 200 } } });
        rig.Ok("connect", new { links = new object[] { new { from = "s1", to = "s2" } } });
        var diff = rig.Ok("diff", new { since = "before" });
        Assert.Equal("+2 shapes, +1 connector", ChatSession.Summarise(System.Text.Json.Nodes.JsonNode.Parse(diff.GetRawText())!.AsObject()));
        Assert.Single(rig.Call("render_checkpoint", new { name = "before" }).Images);
        rig.Ok("restore_checkpoint", new { name = "before" });
        Assert.Empty(rig.Document.Shapes);
        rig.Ok("undo");
        Assert.Equal(3, rig.Document.Shapes.Count);
    }

    [AvaloniaFact]
    public void A_chat_on_a_local_model_charts_a_file_and_its_reply_can_be_undone()
    {
        using var rig = new Rig();
        var server = new FakeModelServer()
            .Reply(Say("I'll read it first."), Call("c1", "read_file", new { path = "src/main.py" }))
            .Reply(Call("c2", "add_flowchart", new
            {
                nodes = new object[] { new { id = "a", label = "Start", kind = "start" }, new { id = "b", label = "Items?", kind = "decision" }, new { id = "c", label = "End", kind = "end" } },
                edges = new object[] { new { from = "a", to = "b" }, new { from = "b", to = "c", label = "yes" } },
            }))
            .Reply(Say("Done: a flowchart of process_order on page 2."));
        var chat = new ChatSession(rig.Bridge) { Provider = ChatProvider.OpenAICompatible, Server = server.Client(), ServerModel = "qwen3" };
        Wait(chat.SendAsync("flowchart src/main.py for me").ContinueWith(t => { t.GetAwaiter().GetResult(); return true; }));

        Assert.Equal(2, rig.Document.Pages.Count);
        Assert.Equal(["read_file", "add_flowchart"], chat.Entries.OfType<ToolEntry>().Select(t => t.Name));
        Assert.All(chat.Entries.OfType<ToolEntry>(), t => Assert.False(t.IsError, t.Result));
        Assert.Contains("AVASvgMaker", (string?)server.Requests[0]["messages"]![0]!["content"]);
        Assert.Null(server.Authorization[0]);

        var undo = Assert.Single(chat.Entries.OfType<ReplyUndoEntry>());
        Assert.True(Wait(chat.UndoReplyAsync(undo)));
        Assert.Single(rig.Document.Pages);
    }

    [AvaloniaFact]
    public void Core_tools_for_small_models_are_all_real_tools()
    {
        foreach (var name in ChatTools.Core) Assert.Contains(name, ToolNames);
    }
}
