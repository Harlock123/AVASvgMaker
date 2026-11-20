using System.Net.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AVASvgMaker.Agents.Mcp;

/// <summary>
/// The MCP server: <see cref="DiagramTools"/>' methods as MCP tools (the SDK's attribute model, read by reflection),
/// with instructions that tell an agent AVASvgMaker's conventions. The same server runs over stdio headless, over a
/// pipe for agents attached to the open editor, and in-process for the program's own AI Chat.
/// </summary>
public static class DiagramServer
{
    public const string Name = "AVASvgMaker";

    /// <summary>What every agent is told on connecting: the conventions the tools' descriptions build on.</summary>
    public const string Instructions = """
        AVASvgMaker is a Visio-style diagram editor: flowcharts, BPMN, UML, network, electrical and logic diagrams on one or more pages, from a stencil catalogue of over 140 shapes joined by connectors that route themselves round what's in the way. You draw with the same shapes and tools a person uses. Its drawings export as SVG, PDF, PNG, JPEG, WebP, BMP, Mermaid and Visio (.vsdx).

        Conventions (they matter):
        - Positions and sizes are in page units: 96 to the inch, from the page's top-left corner, x to the right and y downward. A Letter page is 816 by 1056; a shape's x, y, w and h are its box.
        - Shapes are known by their ids ("s12"), given when they are made and kept for good (through undo and saving); get_page lists them. Pages are numbered from 1.
        - A shape's kind is a stencil name or kind (list_stencils): "Process", "Decision", "Terminator", "Data", "Database", "Document", "Actor", "Server", "Resistor", "AND gate"…
        - Connectors join two shapes by their ids (connect) and stay glued as the shapes move; their routing is straight, orthogonal (right angles round obstacles, the default) or curved, with an arrow at the end unless told otherwise, and an optional label.
        - Colours are "#RRGGBB".
        - Every call that changes the drawing is one undo step (undo takes it back; in the program Ctrl+Z does too), and a call that fails changes nothing.

        Flowcharts, the quickest way: add_flowchart takes nodes (each with an id of your own, a label and a kind: start, end, process, decision, input, output, data, database, document, subroutine, note) and edges (from, to, an optional label such as "yes"/"no"), makes the shapes, joins them and lays them out in layers, on a new page by default. import_mermaid does the same from Mermaid flowchart text. lay_out arranges an existing drawing by its connectors.

        Flowcharting source code or other files: list_files and read_file read text files in the working folder (in pieces for long ones). Read the code, decide its control flow (entry, steps, branches as decisions with yes/no or case labels, loops back to earlier steps, early returns, exits), then make the chart with add_flowchart, one node per meaningful step, labelled in plain words, not code. For a file with several functions, chart the one asked for, or one page each (new_page). Look at the result with render_page and tidy it.

        A good way to work: get_document; make or change shapes; render_page to look; fix what doesn't read well (crossing lines, cramped text, shapes off the page); export when asked. batch runs several changes as one undo step (dry_run to preview it), and save_steps keeps a sequence as a steps file with ${parameters} that run_steps runs again with other values.

        The server's prompts are saved recipes (a flowchart of a source file, a swimlane process, a network diagram): step-by-step plans with the exact tool calls, to follow or adapt.
        """;

    /// <summary>The MCP tools, bound to a host.</summary>
    public static IEnumerable<McpServerTool> Tools(IDiagramHost host) => Tools(new DiagramTools(host));

    /// <summary>The MCP tools of one set of handlers (one session's: its checkpoints and its recording).</summary>
    public static IEnumerable<McpServerTool> Tools(DiagramTools target)
    {
        foreach (var method in typeof(DiagramTools).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            if (method.GetCustomAttribute<McpServerToolAttribute>() != null)
                yield return McpServerTool.Create(method, target);
    }

    /// <summary>The saved recipes (<see cref="Recipes"/>) as MCP prompts.</summary>
    public static IEnumerable<McpServerPrompt> Prompts() =>
        typeof(Recipes).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.GetCustomAttribute<McpServerPromptAttribute>() != null)
            .Select(m => McpServerPrompt.Create(m, target: null));

    /// <summary>The tools' MCP definitions (names, descriptions, input schemas), without a host to run them.</summary>
    public static IList<Tool> Definitions() => [.. _definitions.Value];

    private static readonly Lazy<Tool[]> _definitions = new(() => Tools(new NoHost()).Select(t => t.ProtocolTool).ToArray());

    public static Implementation Info => new()
    {
        Name = Name,
        Title = "AVASvgMaker",
        Version = typeof(DiagramServer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.0",
    };

    public static McpServerOptions Options(IDiagramHost host) => Configure(new McpServerOptions(), host);

    /// <summary>Fills a new session's options (ones the network door's HTTP handler made) as <see cref="Options"/> makes them.</summary>
    public static McpServerOptions Configure(McpServerOptions options, IDiagramHost host)
    {
        options.ServerInfo = Info;
        options.ServerInstructions = Instructions;
        options.ToolCollection = [];
        var target = new DiagramTools(host);
        options.Filters.Request.CallToolFilters.Add(next => async (request, cancel) =>
        {
            var tool = request.Params is { } p && options.ToolCollection.TryGetPrimitive(p.Name, out var t) ? t.ProtocolTool : null;
            if (tool != null && Arguments.Check(tool, request.Params!.Arguments) is { } problem) return Arguments.Error(problem);
            try
            {
                var result = await next(request, cancel).ConfigureAwait(false);
                // The session's changes, as steps save_steps can keep: every call that worked and may change something.
                if (tool != null && result.IsError != true && tool.Annotations?.ReadOnlyHint != true)
                    target.Record(tool.Name, request.Params!.Arguments);
                return result;
            }
            catch (Exception e) when (e is not McpException and not OperationCanceledException)
            {
                // The SDK couldn't bind the arguments to the handler (a value of the wrong kind): say which, not just that it failed.
                return Arguments.Error(tool != null ? $"{tool.Name}: {e.Message} Check the arguments against the tool's input schema." : e.Message);
            }
        });
        foreach (var tool in Tools(target)) options.ToolCollection.Add(tool);
        options.PromptCollection = [];
        foreach (var prompt in Prompts()) options.PromptCollection.Add(prompt);
        return options;
    }

    /// <summary>Serves one MCP session over a transport until the client goes away or <paramref name="cancel"/> fires.</summary>
    public static async Task RunAsync(ITransport transport, IDiagramHost host, CancellationToken cancel = default)
    {
        await using var server = McpServer.Create(transport, Options(host));
        await server.RunAsync(cancel).ConfigureAwait(false);
    }

    /// <summary>Checks a call's arguments against its tool's input schema before the SDK binds them.</summary>
    internal static class Arguments
    {
        public static CallToolResult Error(string message) => new() { IsError = true, Content = [new TextContentBlock { Text = message }] };

        /// <summary>What's wrong with the arguments (a name the tool doesn't take, a value of the wrong kind), or null.</summary>
        public static string? Check(Tool tool, IDictionary<string, JsonElement>? args)
        {
            if (args == null || !tool.InputSchema.TryGetProperty("properties", out var props)) return null;
            foreach (var (name, value) in args)
            {
                if (!props.TryGetProperty(name, out var schema))
                {
                    var names = props.EnumerateObject().Select(x => x.Name).ToList();
                    return $"{tool.Name} has no argument \"{name}\". It takes: {(names.Count == 0 ? "none" : string.Join(", ", names))}.";
                }
                if (Kinds(schema) is { Count: > 0 } kinds && !kinds.Any(k => Fits(k, value)))
                    return $"{tool.Name}: {name} must be {string.Join(" or ", kinds.Where(k => k != "null").Select(Describe))}, not {Describe(value)}.";
            }
            return null;
        }

        private static List<string> Kinds(JsonElement schema) =>
            schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("type", out var type) ? []
            : type.ValueKind == JsonValueKind.String ? [type.GetString()!]
            : type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : [];

        private static bool Fits(string kind, JsonElement v) => kind switch
        {
            "string" => v.ValueKind == JsonValueKind.String,
            "number" => v.ValueKind == JsonValueKind.Number,
            "integer" => v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out _),
            "boolean" => v.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "array" => v.ValueKind == JsonValueKind.Array,
            "object" => v.ValueKind == JsonValueKind.Object,
            "null" => v.ValueKind == JsonValueKind.Null,
            _ => true,
        };

        private static string Describe(string kind) => kind switch
        {
            "string" => "text", "number" => "a number", "integer" => "a whole number", "boolean" => "true or false",
            "array" => "a list", "object" => "an object", _ => kind,
        };

        private static string Describe(JsonElement v) => v.ValueKind switch
        {
            JsonValueKind.String => $"the text \"{v.GetString()}\"",
            JsonValueKind.Number => $"the number {v.GetRawText()}",
            JsonValueKind.True or JsonValueKind.False => v.GetRawText(),
            JsonValueKind.Array => "a list",
            JsonValueKind.Object => "an object",
            _ => "null",
        };
    }

    /// <summary>A host for reading the tools' definitions only.</summary>
    private sealed class NoHost : IDiagramHost
    {
        public string Root => Path.GetTempPath();
        public IDiagramLibrary Library => throw new InvalidOperationException("Not connected to a document.");
        public Task<T> RunAsync<T>(string action, Func<IDiagramSession, Task<T>> work, CancellationToken cancel = default) =>
            throw new InvalidOperationException("Not connected to a document.");
    }
}
