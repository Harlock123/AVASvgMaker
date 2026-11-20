using System.IO;
using System.Runtime.CompilerServices;
using AVASvgMaker.Engine;

namespace AVASvgMaker.Tests;

/// <summary>
/// Before any test runs, the settings (and the AI side's agents.json, keys, token and saved chats beside them) move
/// to a temporary folder of the suite's own, so no test window reads or writes the person's: a window made by a test
/// must never open their agent pipe or network door, or use their API keys.
/// </summary>
internal static class SettingsIsolation
{
    [ModuleInitializer]
    internal static void MoveSettings() =>
        Preferences.Path = Path.Combine(Directory.CreateTempSubdirectory("avasvg-tests-").FullName, "settings.json");
}
