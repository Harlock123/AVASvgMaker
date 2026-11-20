using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AVASvgMaker.Engine;

namespace AVASvgMaker.Agents;

/// <summary>
/// The AI side's settings: outside agents and the network door, and the AI Chat's model. Kept in <c>agents.json</c>
/// beside the program's <c>settings.json</c> (<see cref="Preferences.Path"/>), so tests that move one move both. API
/// keys and the network token are never here: they live in owner-only files of their own beside it.
/// </summary>
public sealed class AgentSettings
{
    /// <summary>Whether AI agents outside the program may work in it (AI › Allow AI Agents). Off until turned on.</summary>
    public bool AllowAgents { get; set; }

    /// <summary>Whether agents on other computers may, over the network (AI › Connect an Agent). Off until turned on.</summary>
    public bool AllowNetworkAgents { get; set; }
    public string NetworkAgentsAddress { get; set; } = "0.0.0.0";
    public int NetworkAgentsPort { get; set; } = 8769;
    /// <summary>The network door's working folder (empty: the Documents folder).</summary>
    public string NetworkAgentsFolder { get; set; } = "";
    public string NetworkAgentsCertificate { get; set; } = "";
    public string NetworkAgentsKey { get; set; } = "";

    /// <summary>The folder the chat (and agents attached to the window) read, open, save and export in; empty: Documents.</summary>
    public string WorkingFolder { get; set; } = "";

    public bool ChatOpen { get; set; }
    public bool ChatAutoContinue { get; set; } = true;
    /// <summary>"Anthropic", or "OpenAICompatible" for Ollama, LM Studio, another computer's server or a cloud service.</summary>
    public string ChatProvider { get; set; } = "Anthropic";
    public string ChatServerUrl { get; set; } = "http://localhost:11434/v1";
    public string ChatServerModel { get; set; } = "";
    public bool ChatCoreTools { get; set; } = true;
    public bool ChatModelSeesPictures { get; set; }
    public bool ChatKeepHistory { get; set; }
    public string ChatModel { get; set; } = "";
    public Dictionary<string, double[]> ChatPrices { get; set; } = [];
    public double? ChatBudget { get; set; }

    /// <summary>The folder the settings, keys, token and saved chats are in.</summary>
    public static string Folder => System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Preferences.Path))!;

    public static string Path => System.IO.Path.Combine(Folder, "agents.json");

    public static AgentSettings Load()
    {
        try
        {
            return File.Exists(Path) ? JsonSerializer.Deserialize(File.ReadAllText(Path), AgentSettingsJson.Default.AgentSettings) ?? new() : new();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, AgentSettingsJson.Default.AgentSettings));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>Generated at build time, so the settings still read and write in an ahead-of-time build.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AgentSettings))]
internal partial class AgentSettingsJson : JsonSerializerContext;
