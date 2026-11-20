using Avalonia;
using System;

namespace AVASvgMaker;

class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        // AVASvgMaker --mcp: the MCP server for AI agents, with no window (see Agents/AgentMain).
        Agents.Mcp.McpCommandLine.Wanted(args) ? Agents.AgentMain.Run(args) : BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
