using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AVASvgMaker.Engine;

/// <summary>
/// What the person, rather than the drawing, has settled on. Kept beside the saved shapes and
/// for the same reason: it belongs to whoever is at the keyboard, not to any one file.
///
/// Written whenever it changes rather than on the way out, because an application that loses
/// your settings when it is closed the wrong way has not really got any.
/// </summary>
public static partial class Preferences
{
    [JsonSourceGenerationOptions(
        WriteIndented = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(SettingsRecord))]
    private partial class SettingsRecords : JsonSerializerContext;

    private sealed class SettingsRecord
    {
        public int Version { get; set; } = 1;

        /// <summary>A theme's name, or "Desktop" to take whatever the desktop is doing.</summary>
        public string? Theme { get; set; }
    }

    private static SettingsRecord? _settings;

    /// <summary>Raised after any setting changes, so whatever shows it can follow.</summary>
    public static event Action? Changed;

    /// <summary>Where the settings live. Overridable so a test need not touch the real ones.</summary>
    public static string Path { get; set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AVASvgMaker",
        "settings.json");

    private static SettingsRecord Settings => _settings ??= Read();

    /// <summary>
    /// The chosen theme's name, or "Desktop". Never null: a fresh installation follows the
    /// desktop, which is what it did before there was anything to choose.
    /// </summary>
    public static string Theme
    {
        get => string.IsNullOrWhiteSpace(Settings.Theme) ? DesktopTheme : Settings.Theme;
        set
        {
            var wanted = string.IsNullOrWhiteSpace(value) ? DesktopTheme : value.Trim();

            if (string.Equals(Settings.Theme, wanted, StringComparison.Ordinal))
                return;

            Settings.Theme = wanted;
            Write();
            Changed?.Invoke();
        }
    }

    /// <summary>Whether the app should be taking its colours from the desktop.</summary>
    public static bool FollowsDesktop =>
        string.Equals(Theme, DesktopTheme, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Named here rather than taken from the view layer, so that reading the settings file
    /// does not depend on anything being on screen.
    /// </summary>
    public const string DesktopTheme = "Desktop";

    /// <summary>Forgets what was read, so the next question goes back to the file.</summary>
    public static void Reload()
    {
        _settings = null;
        Changed?.Invoke();
    }

    private static SettingsRecord Read()
    {
        try
        {
            if (!File.Exists(Path))
                return new SettingsRecord();

            using var stream = File.OpenRead(Path);

            return JsonSerializer.Deserialize(stream, SettingsRecords.Default.SettingsRecord)
                   ?? new SettingsRecord();
        }
        catch
        {
            // Settings that will not parse are not worth refusing to start over. Whatever is
            // in the file is replaced the next time something is changed.
            return new SettingsRecord();
        }
    }

    private static void Write()
    {
        try
        {
            var folder = System.IO.Path.GetDirectoryName(Path);

            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            using var stream = File.Create(Path);
            JsonSerializer.Serialize(stream, Settings, SettingsRecords.Default.SettingsRecord);
        }
        catch
        {
            // A settings file that cannot be written is a setting that does not outlast the
            // session. That is worth carrying on with; it is not worth an error box.
        }
    }
}
