using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AVASvgMaker.Agents.Mcp;

/// <summary>
/// Saved recipes, served as MCP prompts: short step-by-step plans for drawings agents are often asked for, written
/// with the real tool names and arguments. Each step that calls a tool shows the call on a line of its own,
/// "→ tool {arguments}", so an agent can follow it as it is (and the tests run every recipe through the server, call by
/// call, so a recipe that stops working fails the build).
/// </summary>
[McpServerPromptType]
public static class Recipes
{
    /// <summary>The marker that starts a step's tool call in a recipe's text.</summary>
    public const string CallMarker = "→ ";

    [McpServerPrompt(Name = "flowchart_source_file", Title = "Flowchart a source file")]
    [Description("Reads a source file in the working folder and draws its control flow as a flowchart: one page, steps in plain words, decisions with yes/no branches, loops back, and the exits.")]
    public static string FlowchartSourceFile(
        [Description("The source file, relative to the working folder (default \"src/main.py\").")] string? path = null,
        [Description("The function or method to chart, if the file has several. Optional.")] string? function = null)
    {
        var file = string.IsNullOrWhiteSpace(path) ? "src/main.py" : path.Trim();
        var what = string.IsNullOrWhiteSpace(function) ? "its main flow" : $"the {function.Trim()} function";
        var r = new Recipe($"Draw a flowchart of {what} in {file}.");
        r.Step("Find the file if the path isn't certain.", "list_files", new { folder = ".", pattern = "*", recursive = true });
        r.Step("Read the file. If it says more lines remain, read on with start_line until you have it all.", "read_file", new { path = file });
        r.Step("Work out the control flow: where it starts; each meaningful step (a call, an assignment that matters, a read or a write); each if, while, for or switch as a decision with labelled branches; loops back to the step they repeat; early returns and exceptions as ends. Say it in plain words, not code. Then draw it in one call. The call below shows its shape with an example: build your own nodes and edges from the code.", "add_flowchart", new
        {
            title = $"Flow of {Path(file)}",
            nodes = new object[]
            {
                new { id = "start", label = "Start", kind = "start" },
                new { id = "read", label = "Read the input file", kind = "input" },
                new { id = "valid", label = "Is the input valid?", kind = "decision" },
                new { id = "process", label = "Process each record", kind = "process" },
                new { id = "more", label = "More records?", kind = "decision" },
                new { id = "save", label = "Save the results", kind = "database" },
                new { id = "error", label = "Report the error", kind = "output" },
                new { id = "end", label = "End", kind = "end" },
            },
            edges = new object[]
            {
                new { from = "start", to = "read" }, new { from = "read", to = "valid" },
                new { from = "valid", to = "process", label = "yes" }, new { from = "valid", to = "error", label = "no" },
                new { from = "process", to = "more" }, new { from = "more", to = "process", label = "yes" },
                new { from = "more", to = "save", label = "no" }, new { from = "save", to = "end" }, new { from = "error", to = "end" },
            },
        });
        r.Step("Look at it: every branch labelled, nothing overlapping, the flow reading top to bottom.", "render_page", new { });
        r.Step("If lines tangle, lay it out again (across suits a long, shallow flow).", "lay_out", new { flow = "down" });
        return r.Text("Tell the person what the chart shows in a sentence or two. Offer to export it (export with svg, png or pdf) or to chart another function on a new page.");
    }

    [McpServerPrompt(Name = "approval_process", Title = "Approval process flowchart")]
    [Description("A request-and-approval process as a flowchart, with a decision, a rework loop and an end for each outcome, exported as SVG.")]
    public static string ApprovalProcess(
        [Description("What is being approved (default \"Purchase request\").")] string? subject = null)
    {
        var s = string.IsNullOrWhiteSpace(subject) ? "Purchase request" : subject.Trim();
        var r = new Recipe($"Draw the approval process for a {s.ToLowerInvariant()}.");
        r.Step("Start a new drawing (if it refuses because of unsaved changes, save first or ask the person).", "new_drawing", new { paper = "Letter", name = s });
        r.Step("The process in one call.", "add_flowchart", new
        {
            title = $"{s} approval",
            new_page = false,
            nodes = new object[]
            {
                new { id = "submit", label = $"{s} submitted", kind = "start" },
                new { id = "check", label = "Check details and budget", kind = "process" },
                new { id = "ok", label = "Complete and within budget?", kind = "decision" },
                new { id = "fix", label = "Return to requester to fix", kind = "manual" },
                new { id = "approve", label = "Manager approves?", kind = "decision" },
                new { id = "record", label = "Record the decision", kind = "database" },
                new { id = "approved", label = "Approved", kind = "end", fill = "#D9F2DD" },
                new { id = "rejected", label = "Rejected", kind = "end", fill = "#F9DADA" },
            },
            edges = new object[]
            {
                new { from = "submit", to = "check" }, new { from = "check", to = "ok" },
                new { from = "ok", to = "approve", label = "yes" }, new { from = "ok", to = "fix", label = "no" },
                new { from = "fix", to = "check", label = "resubmitted" },
                new { from = "approve", to = "record", label = "yes" }, new { from = "approve", to = "rejected", label = "no" },
                new { from = "record", to = "approved" },
            },
        });
        r.Step("Look at it.", "render_page", new { page = 1 });
        r.Step("Export it.", "export", new { path = "approval.svg", overwrite = true });
        return r.Text("Say where the SVG is. save_drawing keeps the drawing as .avadiag for changes later.");
    }

    [McpServerPrompt(Name = "network_diagram", Title = "Small network diagram")]
    [Description("An office network: the internet through a firewall and router to a switch, with servers, workstations and a printer, laid out across the page.")]
    public static string NetworkDiagram()
    {
        var r = new Recipe("Draw a small office network diagram.");
        r.Step("A new landscape page (if it refuses because of unsaved changes, save first or ask the person).", "new_drawing", new { paper = "Letter", landscape = true, name = "Office network" });
        r.Step("The devices (ids come back in this order: s1 to s8).", "add_shapes", new
        {
            shapes = new object[]
            {
                new { kind = "Cloud", text = "Internet", x = 40, y = 300, w = 130, h = 80 },
                new { kind = "Firewall", text = "Firewall", x = 220, y = 300, w = 80, h = 80 },
                new { kind = "Router", text = "Router", x = 360, y = 300, w = 80, h = 80 },
                new { kind = "Switch", text = "Core switch", x = 500, y = 300, w = 90, h = 70 },
                new { kind = "Server", text = "File server", x = 700, y = 120, w = 70, h = 90 },
                new { kind = "Server", text = "Mail server", x = 700, y = 260, w = 70, h = 90 },
                new { kind = "Workstation", text = "Workstations", x = 700, y = 400, w = 90, h = 80 },
                new { kind = "Printer", text = "Printer", x = 700, y = 540, w = 80, h = 70 },
            },
        });
        r.Step("Join them up, with no arrows (network links go both ways).", "connect", new
        {
            links = new object[]
            {
                new { from = "s1", to = "s2", end_cap = "none" }, new { from = "s2", to = "s3", end_cap = "none" },
                new { from = "s3", to = "s4", end_cap = "none" }, new { from = "s4", to = "s5", end_cap = "none" },
                new { from = "s4", to = "s6", end_cap = "none" }, new { from = "s4", to = "s7", end_cap = "none" },
                new { from = "s4", to = "s8", end_cap = "none" },
            },
        });
        r.Step("Lay it out across the page.", "lay_out", new { flow = "across" });
        r.Step("Look at it.", "render_page", new { });
        return r.Text("Offer to export it (export as svg, png, pdf or vsdx for Visio).");
    }

    private static string Path(string file) => System.IO.Path.GetFileName(file);

    private sealed class Recipe(string intro)
    {
        private readonly StringBuilder _text = new(intro + "\n\nFollow these steps in order. A line starting with \"" + CallMarker.Trim() + "\" is one tool call, with its arguments as JSON. If a step fails, read the error, fix the cause and carry on. The ids in these calls are the ones a fresh drawing gives; if a call returns different ids, use those.\n");
        private int _number;

        public void Step(string? says, string tool, object args)
        {
            _number++;
            _text.Append('\n').Append(_number).Append('.').Append(says != null ? " " : "");
            if (says != null) _text.Append(says);
            _text.Append("\n   ");
            _text.Append(CallMarker).Append(tool).Append(' ').Append(JsonSerializer.Serialize(args, Json.Options));
        }

        public string Text(string closing) => _text.Append("\n\n").Append(closing).ToString();
    }
}
