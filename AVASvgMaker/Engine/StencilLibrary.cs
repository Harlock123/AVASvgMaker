using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AVASvgMaker.Engine;

/// <summary>One shape of your own: a name, and the drawing it stands for.</summary>
public sealed class CustomStencil
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The shapes themselves, as the same JSON the clipboard and the file format use. A
    /// custom stencil is a saved piece of drawing rather than an outline, so what comes back
    /// out of it is what went in - several shapes, their formatting, their glue and their
    /// grouping - and not a flattened silhouette of them.
    /// </summary>
    public string Fragment { get; set; } = string.Empty;
}

/// <summary>
/// The shapes you have saved yourself, kept beside the settings rather than beside a drawing:
/// they belong to the person, not to the file, which is the whole point of saving one.
/// </summary>
public static partial class StencilLibrary
{
    [JsonSourceGenerationOptions(
        WriteIndented = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(LibraryRecord))]
    private partial class LibraryRecords : JsonSerializerContext;

    private sealed class LibraryRecord
    {
        public int Version { get; set; } = 1;
        public List<CustomStencil> Stencils { get; set; } = [];
    }

    private static List<CustomStencil>? _stencils;

    /// <summary>Raised when a stencil is added, renamed or removed, so the toolbox can follow.</summary>
    public static event Action? Changed;

    /// <summary>Where the library lives. Overridable so a test need not touch the real one.</summary>
    public static string Path { get; set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AVASvgMaker",
        "stencils.json");

    public static IReadOnlyList<CustomStencil> All => _stencils ??= Read();

    public static CustomStencil? Find(string id) =>
        All.FirstOrDefault(stencil => string.Equals(stencil.Id, id, StringComparison.Ordinal));

    /// <summary>Saves a piece of drawing under a name. Returns null when there is nothing in it.</summary>
    public static CustomStencil? Add(string name, string fragment)
    {
        name = name.Trim();

        if (name.Length == 0 || string.IsNullOrWhiteSpace(fragment))
            return null;

        var stencil = new CustomStencil
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = Unused(name),
            Fragment = fragment
        };

        _stencils = [.. All, stencil];
        Write();

        return stencil;
    }

    public static bool Rename(string id, string name)
    {
        name = name.Trim();

        if (Find(id) is not { } stencil || name.Length == 0 ||
            string.Equals(stencil.Name, name, StringComparison.Ordinal))
            return false;

        stencil.Name = Unused(name, stencil);
        Write();

        return true;
    }

    public static bool Remove(string id)
    {
        if (Find(id) is not { } stencil)
            return false;

        _stencils = All.Where(other => !ReferenceEquals(other, stencil)).ToList();
        Write();

        return true;
    }

    /// <summary>Forgets what was read, so the next question goes back to the file.</summary>
    public static void Reload()
    {
        _stencils = null;
        Changed?.Invoke();
    }

    /// <summary>
    /// The name, or the name with a number after it. Two stencils with one name are not
    /// wrong, only impossible to tell apart in a list, which is worse.
    /// </summary>
    private static string Unused(string name, CustomStencil? except = null)
    {
        bool Taken(string candidate) => All.Any(stencil =>
            !ReferenceEquals(stencil, except) &&
            string.Equals(stencil.Name, candidate, StringComparison.CurrentCultureIgnoreCase));

        if (!Taken(name))
            return name;

        for (var n = 2; ; n++)
        {
            var candidate = $"{name} {n}";

            if (!Taken(candidate))
                return candidate;
        }
    }

    private static List<CustomStencil> Read()
    {
        try
        {
            if (!File.Exists(Path))
                return [];

            using var stream = File.OpenRead(Path);
            var record = JsonSerializer.Deserialize(stream, LibraryRecords.Default.LibraryRecord);

            // A stencil with nothing in it would be a row in the list that does nothing.
            return record?.Stencils
                       .Where(stencil => !string.IsNullOrWhiteSpace(stencil.Id)
                                         && !string.IsNullOrWhiteSpace(stencil.Fragment))
                       .ToList()
                   ?? [];
        }
        catch
        {
            // A library that will not parse is not worth taking the app down for; the shapes
            // in the drawing matter and these do not.
            return [];
        }
    }

    private static void Write()
    {
        try
        {
            var folder = System.IO.Path.GetDirectoryName(Path);

            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            using (var stream = File.Create(Path))
                JsonSerializer.Serialize(stream, new LibraryRecord { Stencils = All.ToList() },
                    LibraryRecords.Default.LibraryRecord);
        }
        catch
        {
            // Nothing to be done about a read-only home directory, and nothing worth stopping
            // for: the stencil is still in the list for as long as the app is open.
        }

        Changed?.Invoke();
    }
}
