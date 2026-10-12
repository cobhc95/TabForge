using System.IO;
using TabForge.Services;

namespace TabForge.Plugins;

/// <summary>The user's saved presets of one kind, as the preset manage panel sees them: names in the order shown, and the changes it can make.</summary>
public interface IPresetStore
{
    /// <summary>The user's presets in the order the drop-down shows them.</summary>
    IReadOnlyList<string> Names();
    /// <summary>Renames a preset; a preset already named <paramref name="to"/> is replaced.</summary>
    void Rename(string from, string to);
    void Duplicate(string from, string to);
    void Delete(string name);
    /// <summary>Moves a preset to a position in the list (clamped).</summary>
    void Move(string name, int index);
    /// <summary>Raised after every change, so the drop-down that shows the presets can list them again.</summary>
    event Action? Changed;
}

/// <summary>The naming rules every preset kind shares.</summary>
public static class PresetRules
{
    public const int MaxNameLength = 64;

    /// <summary>Why the (trimmed) text cannot be a preset name; null when it can.</summary>
    public static string? Error(string text)
    {
        var name = text.Trim();
        if (name.Length == 0) return "Type a name.";
        if (name.Length > MaxNameLength) return $"Use at most {MaxNameLength} characters.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "A name cannot contain \\ / : * ? \" < > |";
        if (name.EndsWith('.')) return "A name cannot end with a dot.";
        return null;
    }

    /// <summary>"name copy", then "name copy 2"…: the first one no preset has yet.</summary>
    public static string CopyName(string name, IReadOnlyList<string> existing)
    {
        for (var n = 1; ; n++)
        {
            var suffix = n == 1 ? " copy" : $" copy {n}";
            var candidate = name[..Math.Min(name.Length, MaxNameLength - suffix.Length)].TrimEnd() + suffix;
            if (!existing.Contains(candidate, StringComparer.OrdinalIgnoreCase)) return candidate;
        }
    }
}

// Owns: one folder of user preset files of one extension as an IPresetStore: listing in the saved order, rename, duplicate, delete, and
//   the order file beside the presets (one name per line; a folder without it lists alphabetically, as before).
// Does not own: what a preset file holds or how it is loaded (PluginLibrary, the MIDI processing window), or any window.
// Tests: TestPresetStoresOnDisk, TestPresetManagePanel.
public sealed class PresetFolderStore : IPresetStore
{
    private const string OrderFile = "preset-order.txt";
    private const int MaxPresets = 1000;
    private const long MaxFileBytes = 32L * 1024 * 1024;
    private readonly string _folder, _extension;

    public PresetFolderStore(string folder, string extension) { _folder = folder; _extension = extension; }

    public static PresetFolderStore Plugin(PluginSlot slot) => new(PluginLibrary.PresetFolder(slot), ".tfpreset");
    /// <summary>The presets of the plug-in whose preset folder has that name (<see cref="PluginKey"/>).</summary>
    public static PresetFolderStore Plugin(string key) => new(Path.Combine(PluginLibrary.Root, "Presets", TabForge.Audio.Contracts.SafeFileNames.SafeFileName(key, "plugin", 64)), ".tfpreset");
    public static string PluginKey(PluginSlot slot) => Path.GetFileName(PluginLibrary.PresetFolder(slot));
    /// <summary>The plug-ins that have saved presets, as keys for <see cref="Plugin(string)"/>.</summary>
    public static IReadOnlyList<string> PluginKeys() => Folders(Path.Combine(PluginLibrary.Root, "Presets"), ".tfpreset");
    public static PresetFolderStore Containers() => new(Path.Combine(PluginLibrary.Root, "Containers"), ".tfcontainer");
    /// <summary>The processor types that have saved presets.</summary>
    public static IReadOnlyList<string> MidiProcessorTypes() => Folders(Path.Combine(PluginLibrary.Root, "MidiPresets", "Processors"), ".json");

    private static IReadOnlyList<string> Folders(string root, string extension)
    {
        try
        {
            return !Directory.Exists(root) ? Array.Empty<string>() : Directory.EnumerateDirectories(root).Take(MaxPresets).Where(d => Directory.EnumerateFiles(d, "*" + extension).Any())
                .Select(Path.GetFileName).OfType<string>().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Services.Trace.Error(Services.Trace.Ui, "presets: list folders: " + ex.Message); return Array.Empty<string>(); }
    }

    public static PresetFolderStore FxChains() => new(PluginLibrary.ChainsFolder, ".tfchain");
    public static PresetFolderStore MidiChains() => new(Path.Combine(PluginLibrary.Root, "MidiPresets"), ".json");
    public static PresetFolderStore MidiProcessor(string type) =>
        new(Path.Combine(PluginLibrary.Root, "MidiPresets", "Processors", TabForge.Audio.Contracts.SafeFileNames.SafeFileName(type, "processor", 64)), ".json");

    public event Action? Changed;

    /// <summary>Tells listeners to list again: the folder was changed through another store on the same folder.</summary>
    public void RaiseChanged() => Changed?.Invoke();

    /// <summary>The file of a preset inside this store's folder; a name that is no plain file name is refused.</summary>
    public string PathOf(string name)
    {
        if (PresetRules.Error(name) is { } error) throw new InvalidDataException(error);
        return Path.Combine(_folder, name.Trim() + _extension);
    }

    public IReadOnlyList<string> Names()
    {
        List<string> files;
        try
        {
            if (!Directory.Exists(_folder)) return Array.Empty<string>();
            files = Directory.EnumerateFiles(_folder, "*" + _extension).Where(f => f.EndsWith(_extension, StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileNameWithoutExtension).OfType<string>().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Take(MaxPresets).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Services.Trace.Error(Services.Trace.Ui, "presets: list: " + ex.Message); return Array.Empty<string>(); }
        var order = ReadOrder();
        if (order.Count == 0) return files;
        // The order file only ranks names that exist as files; it is never used as a path.
        var ordered = order.Select(o => files.FirstOrDefault(f => string.Equals(f, o, StringComparison.OrdinalIgnoreCase))).OfType<string>().Distinct().ToList();
        ordered.AddRange(files.Except(ordered));
        return ordered;
    }

    public void Rename(string from, string to)
    {
        to = to.Trim();
        var order = Names().ToList();
        File.Move(PathOf(from), PathOf(to), overwrite: true);
        order.RemoveAll(n => Same(n, to) && !Same(n, from));
        var at = order.FindIndex(n => Same(n, from));
        if (at >= 0) order[at] = to;
        Finish(order, force: false);
    }

    public void Duplicate(string from, string to)
    {
        var order = Names().ToList();
        var bytes = InputLimits.ReadBoundedBytes(PathOf(from), MaxFileBytes, "preset");
        FilePathPolicy.WriteAtomically(PathOf(to), s => s.Write(bytes));
        order.Insert(order.FindIndex(n => Same(n, from)) + 1, to.Trim());
        Finish(order, force: false);
    }

    public void Delete(string name)
    {
        File.Delete(PathOf(name));
        Finish(Names().ToList(), force: false);
    }

    public void Move(string name, int index)
    {
        var order = Names().ToList();
        var from = order.FindIndex(n => Same(n, name));
        if (from < 0) return;
        var moving = order[from];
        order.RemoveAt(from);
        order.Insert(Math.Clamp(index, 0, order.Count), moving);
        Finish(order, force: true);
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Keeps the order file in step (written only once the user has ordered this folder) and tells the drop-down.</summary>
    private void Finish(List<string> order, bool force)
    {
        try
        {
            var path = Path.Combine(_folder, OrderFile);
            if (force || File.Exists(path)) FilePathPolicy.WriteAtomically(path, s => s.Write(System.Text.Encoding.UTF8.GetBytes(string.Join('\n', order))));
        }
        finally { Changed?.Invoke(); }
    }

    private List<string> ReadOrder()
    {
        try
        {
            var path = Path.Combine(_folder, OrderFile);
            if (!File.Exists(path) || new FileInfo(path).Length > 256 * 1024) return new();
            return File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0).Take(MaxPresets).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Services.Trace.Error(Services.Trace.Ui, "presets: read order: " + ex.Message); return new(); }
    }
}
