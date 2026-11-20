using System;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVASvgMaker.Agents.Chat;
using Xunit;
using static AVASvgMaker.Tests.Agents.FakeModelServer;

namespace AVASvgMaker.Tests.Agents;

/// <summary>
/// The README's and guide's picture of the AI Chat flow-charting a source file: the real window, a local model's side
/// replayed from a script, the tool calls run for real. Writes Images/AVASvgMaker-ai-chat.png only with AVASVG_SHOOT set;
/// checks what it shows either way.
/// </summary>
public class ChatScreenshot
{
    [AvaloniaFact]
    public void The_ai_chat_flowcharts_a_source_file()
    {
        var root = Directory.CreateTempSubdirectory("avasvg-shot-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(Path.Combine(root, "src", "orders.py"), AgentTests.Python);

        var (window, canvas) = Harness.Editor(816, 1056);
        window.Width = 1440;
        window.Height = 900;
        window.ChatKeys = (new ApiKeyStore(null, null), new ApiKeyStore(null, null));
        window.AgentSettings.WorkingFolder = root;
        window.AgentSettings.ChatProvider = "OpenAICompatible";
        window.AgentSettings.ChatServerModel = "qwen3";

        var server = new FakeModelServer()
            .Reply(Say("I'll read the file first."), Call("c1", "read_file", new { path = "src/orders.py" }))
            .Reply(Say("process_order checks the order has items, adds up each item, charges the customer and returns the total. Drawing it now."),
                Call("c2", "add_flowchart", new
                {
                    title = "process_order",
                    nodes = new object[]
                    {
                        new { id = "s", label = "Start", kind = "start" },
                        new { id = "has", label = "Order has items?", kind = "decision" },
                        new { id = "err", label = "Raise ValueError: empty order", kind = "end" },
                        new { id = "zero", label = "Total = 0", kind = "preparation" },
                        new { id = "each", label = "Add price x qty for each item", kind = "process" },
                        new { id = "charge", label = "Charge the customer", kind = "subroutine" },
                        new { id = "e", label = "Return the total", kind = "end" },
                    },
                    edges = new object[]
                    {
                        new { from = "s", to = "has" }, new { from = "has", to = "err", label = "no" }, new { from = "has", to = "zero", label = "yes" },
                        new { from = "zero", to = "each" }, new { from = "each", to = "charge" }, new { from = "charge", to = "e" },
                    },
                }))
            .Reply(Call("c3", "render_page", new { scale = 0.4 }))
            .Reply(Say("Done: the flowchart of process_order is on page 2. The empty-order check branches off to its error; Ctrl+Z or Undo this reply takes it back."));

        window.ToggleChat();
        var chat = window.Chat!;
        chat.ServerFactory = (_, _) => server.Client();
        chat.LoadSettings();
        chat.Input.Text = "Examine src/orders.py and flowchart it for me";
        chat.Send();
        for (var i = 0; i < 3000 && (chat.Session.Working || server.Left > 0); i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(2);
        }

        Assert.Equal(0, server.Left);
        Assert.All(chat.Session.Entries.OfType<ToolEntry>(), t => Assert.False(t.IsError, t.Result));
        Assert.Equal(2, canvas.Document.Pages.Count);
        Assert.Contains(canvas.Document.Shapes, x => x.Text == "Order has items?");
        Assert.Contains(chat.Session.Entries.OfType<ReplyUndoEntry>(), e => e.CanUndo);

        if (Environment.GetEnvironmentVariable("AVASVG_SHOOT") is null) return;
        canvas.ZoomToFit();
        canvas.InvalidateVisual();
        Harness.Settle(window);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Harness.Settle(window);
        var folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Images"));
        using var file = File.Create(Path.Combine(folder, "AVASvgMaker-ai-chat.png"));
        window.CaptureRenderedFrame()!.Save(file);
    }
}
