using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Avalonia.Media;
using AVASvgMaker.Views;

namespace AVASvgMaker.Engine;

/// <summary>
/// Follows the desktop theme on Omarchy.
///
/// The palette is read through <c>omarchy-theme-color --all</c>, which is the resolver every
/// other consumer on the system uses - waybar, alacritty, the terminal escape sequences. Going
/// through it rather than parsing the file directly means this app gets the same alias and
/// fallback cascade as everything else, and cannot drift from the rest of the desktop.
/// Parsing the file is kept only as a fallback for when the command is not on the path.
/// </summary>
public static class OmarchyTheme
{
    private static readonly string StateDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".local", "state", "omarchy", "current");

    /// <summary>Rewritten on every theme change, which is what makes it worth watching.</summary>
    private static string MarkerFile => Path.Combine(StateDirectory, "theme.name");

    private static string ColoursFile => Path.Combine(StateDirectory, "theme", "colors.toml");

    public static bool IsPresent => File.Exists(MarkerFile);

    public static string? CurrentName =>
        IsPresent ? File.ReadAllText(MarkerFile).Trim() : null;

    /// <summary>What was read last, and the stamp on the file that said so.</summary>
    private static (DateTime Stamp, AppPalette? Palette)? _last;

    /// <summary>
    /// The desktop palette mapped onto the app's chrome, or null if there is none.
    ///
    /// Remembered against the marker file's timestamp, because reading is not free - it runs
    /// the desktop's own resolver in a subprocess - and the answer cannot have changed unless
    /// that file has. It is the same file the watcher keys on, so nothing can go stale that
    /// would not also go unnoticed.
    /// </summary>
    public static AppPalette? Read()
    {
        var stamp = Stamped();

        if (_last is { } remembered && remembered.Stamp == stamp)
            return remembered.Palette;

        var palette = Sample();
        _last = (stamp, palette);

        return palette;
    }

    /// <summary>
    /// What the current theme is stamped with. Both files, because the marker says which
    /// theme is on and the colours file is what is actually read: a change that touched only
    /// one of them would otherwise go unnoticed until the app was restarted.
    /// </summary>
    private static DateTime Stamped()
    {
        try
        {
            var marker = File.Exists(MarkerFile) ? File.GetLastWriteTimeUtc(MarkerFile) : DateTime.MinValue;
            var colours = File.Exists(ColoursFile) ? File.GetLastWriteTimeUtc(ColoursFile) : DateTime.MinValue;

            return marker > colours ? marker : colours;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static AppPalette? Sample()
    {
        var colours = Resolve();

        if (colours is null || !colours.TryGetValue("background", out var background))
            return null;

        var light = colours.GetValueOrDefault("mode", "dark")
            .Equals("light", StringComparison.OrdinalIgnoreCase);

        // dark_background is the recessed surface in either mode, which is what the page
        // should sit on; the panels take the plain background a step in front of it.
        return new AppPalette(
            Workspace: Parse(colours, "dark_background", background),
            Panel: Parse(colours, "background", background),
            PanelItem: Parse(colours, "lighter_background", background),
            Border: Parse(colours, "selection", colours.GetValueOrDefault("muted", background)),
            Text: Parse(colours, "foreground", light ? "#101010" : "#E4E4E8"),
            Accent: Parse(colours, "accent", "#FF8C00"),
            IsLight: light);
    }

    private static Color Parse(IReadOnlyDictionary<string, string> colours, string key, string fallback) =>
        Color.TryParse(colours.GetValueOrDefault(key, fallback), out var colour)
            ? colour
            : Color.TryParse(fallback, out var spare) ? spare : Colors.Gray;

    private static Dictionary<string, string>? Resolve()
    {
        if (!IsPresent)
            return null;

        return FromResolver() ?? FromFile();
    }

    /// <summary>Asks Omarchy's own resolver, so the palette matches the rest of the desktop.</summary>
    private static Dictionary<string, string>? FromResolver()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("omarchy-theme-color", "--all")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });

            if (process is null)
                return null;

            var output = process.StandardOutput.ReadToEnd();

            if (!process.WaitForExit(2000) || process.ExitCode != 0)
                return null;

            var colours = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in output.Split('\n'))
            {
                var parts = line.Split('\t');

                if (parts.Length == 2)
                    colours[parts[0].Trim()] = parts[1].Trim();
            }

            return colours.Count > 0 ? colours : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The bare key = "value" lines, for when the resolver is not on the path.</summary>
    private static Dictionary<string, string>? FromFile()
    {
        try
        {
            if (!File.Exists(ColoursFile))
                return null;

            var colours = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in File.ReadAllLines(ColoursFile))
            {
                var text = line.Trim();

                if (text.StartsWith('#') || !text.Contains('='))
                    continue;

                var split = text.Split('=', 2);
                colours[split[0].Trim()] = split[1].Trim().Trim('"');
            }

            return colours.Count > 0 ? colours : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Calls back when the desktop theme changes. Watching the marker file works in-process
    /// and needs nothing installed, unlike Omarchy's theme-set hook directory.
    /// </summary>
    public static IDisposable? Watch(Action onChanged)
    {
        if (!IsPresent)
            return null;

        try
        {
            var watcher = new FileSystemWatcher(StateDirectory)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };

            // A single theme change writes several times; the callback is expected to settle.
            watcher.Changed += (_, _) => onChanged();
            watcher.Created += (_, _) => onChanged();
            watcher.Renamed += (_, _) => onChanged();

            return watcher;
        }
        catch
        {
            return null;
        }
    }
}
