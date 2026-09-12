using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Media;
using AVASvgMaker.Models;

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
        public int Version { get; set; } = 2;

        /// <summary>A theme's name, or "Desktop" to take whatever the desktop is doing.</summary>
        public string? Theme { get; set; }

        /// <summary>
        /// The formatting a new shape is given. Version 2 onwards, and absent in a file
        /// written before there was anywhere to say it - such a file gets what the app
        /// always drew with.
        /// </summary>
        public StyleRecord? Style { get; set; }

        /// <summary>The grid a new window starts with. Version 2 onwards.</summary>
        public double? GridSize { get; set; }

        public bool? SnapToGrid { get; set; }

        public bool? ShowGrid { get; set; }

        /// <summary>The paper a new drawing starts on, in pixels. Version 2 onwards.</summary>
        public double? PageWidth { get; set; }

        public double? PageHeight { get; set; }
    }

    /// <summary>
    /// A <see cref="ShapeStyle"/> as it is written down. Colours go as strings rather than as
    /// numbers so the file can be read by a person, which is the same choice the drawing
    /// format makes.
    /// </summary>
    private sealed class StyleRecord
    {
        public string? Fill { get; set; }
        public string? FillTo { get; set; }
        public double? FillAngle { get; set; }
        public string? Stroke { get; set; }
        public string? TextColor { get; set; }
        public double? StrokeThickness { get; set; }
        public string? StrokeStyle { get; set; }
        public double? FontSize { get; set; }
        public string? FontName { get; set; }
        public bool? Bold { get; set; }
        public bool? Italic { get; set; }
        public string? TextAlign { get; set; }
        public string? TextVerticalAlign { get; set; }
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

    /// <summary>
    /// The formatting a new shape is given, and what a new window's properties panel starts
    /// out showing.
    ///
    /// Deliberately not the same thing as the panel's live value. Setting a colour with
    /// nothing selected arms the next shape, which is something people do while trying
    /// colours out; if that also wrote here, experimenting would quietly rewrite what the
    /// app starts with every time. The dialog writes this, and nothing else does.
    /// </summary>
    public static ShapeStyle Style
    {
        get => ToStyle(Settings.Style);
        set
        {
            var record = ToRecord(value);

            if (Same(Settings.Style, record))
                return;

            Settings.Style = record;
            Write();
            Changed?.Invoke();
        }
    }

    /// <summary>The grid a new window starts with.</summary>
    public static double GridSize
    {
        get => Settings.GridSize is > 0 and var size ? size : 10;
        set => Set(() => Math.Abs(GridSize - value) < 1e-9, () => Settings.GridSize = value);
    }

    public static bool SnapToGrid
    {
        get => Settings.SnapToGrid ?? true;
        set => Set(() => SnapToGrid == value, () => Settings.SnapToGrid = value);
    }

    public static bool ShowGrid
    {
        get => Settings.ShowGrid ?? true;
        set => Set(() => ShowGrid == value, () => Settings.ShowGrid = value);
    }

    /// <summary>The paper a new drawing starts on. Letter, unless told otherwise.</summary>
    public static Size Paper
    {
        get => new(
            Settings.PageWidth is > 0 and var width ? width : 816,
            Settings.PageHeight is > 0 and var height ? height : 1056);
        set => Set(
            () => Paper == value,
            () =>
            {
                Settings.PageWidth = value.Width;
                Settings.PageHeight = value.Height;
            });
    }

    /// <summary>Writes and announces, unless nothing actually changed.</summary>
    private static void Set(Func<bool> unchanged, Action apply)
    {
        if (unchanged())
            return;

        apply();
        Write();
        Changed?.Invoke();
    }

    /// <summary>Forgets what was read, so the next question goes back to the file.</summary>
    public static void Reload()
    {
        _settings = null;
        Changed?.Invoke();
    }

    #region Styles on disk

    private static ShapeStyle ToStyle(StyleRecord? record)
    {
        if (record is null)
            return ShapeStyle.Default;

        var fallback = ShapeStyle.Default;

        return new ShapeStyle(
            Colour(record.Fill, fallback.Fill),
            string.IsNullOrWhiteSpace(record.FillTo) ? null : Colour(record.FillTo, fallback.Fill),
            record.FillAngle ?? fallback.FillAngle,
            Colour(record.Stroke, fallback.Stroke),
            Colour(record.TextColor, fallback.TextColor),
            record.StrokeThickness is > 0 and var weight ? weight : fallback.StrokeThickness,
            Parse(record.StrokeStyle, fallback.StrokeStyle),
            record.FontSize is > 0 and var size ? size : fallback.FontSize,
            record.FontName ?? fallback.FontName,
            record.Bold ?? fallback.Bold,
            record.Italic ?? fallback.Italic,
            Parse(record.TextAlign, fallback.TextAlign),
            Parse(record.TextVerticalAlign, fallback.TextVerticalAlign));
    }

    private static StyleRecord ToRecord(ShapeStyle style) => new()
    {
        Fill = style.Fill.ToString(),
        FillTo = style.FillTo?.ToString(),
        FillAngle = style.FillAngle,
        Stroke = style.Stroke.ToString(),
        TextColor = style.TextColor.ToString(),
        StrokeThickness = style.StrokeThickness,
        StrokeStyle = style.StrokeStyle.ToString(),
        FontSize = style.FontSize,
        FontName = string.IsNullOrEmpty(style.FontName) ? null : style.FontName,
        Bold = style.Bold,
        Italic = style.Italic,
        TextAlign = style.TextAlign.ToString(),
        TextVerticalAlign = style.TextVerticalAlign.ToString()
    };

    /// <summary>Compared as styles rather than field by field, so a no-op write stays a no-op.</summary>
    private static bool Same(StyleRecord? a, StyleRecord? b) => ToStyle(a) == ToStyle(b);

    private static Color Colour(string? text, Color fallback) =>
        Color.TryParse(text, out var colour) ? colour : fallback;

    private static T Parse<T>(string? text, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(text, out var value) ? value : fallback;

    #endregion

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
