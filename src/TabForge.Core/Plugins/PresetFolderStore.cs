using TabForge.Presenters;
using System.IO;
using TabForge.Services;

namespace TabForge.Plugins;

/// <summary>The user's saved presets of one kind, as the preset manage panel sees them: names in the order shown, and the changes it can make.</summary>
public interface IPresetStore
{
    /// <summary>The user's presets in the order the drop-down shows them.</summary>
    IReadOnlyList<string> Names();
    /// <summary>Renames a preset. A preset already named <paramref name="to"/> is replaced only when <paramref name="replace"/> is true (the user confirmed); otherwise it throws IOException.</summary>
    void Rename(string from, string to, bool replace = false);
    /// <summary>Copies a preset under a new name; throws IOException when a preset of that name exists.</summary>
    void Duplicate(string from, string to);
    void Delete(string name);
    /// <summary>Moves a preset to a position in the list (clamped).</summary>
    void Move(string name, int index);
    /// <summary>Raised after every change, so the drop-down that shows the presets can list them again.</summary>
    event Action? Changed;
}

// Owns: one folder of user preset files of one extension as an IPresetStore: listing in the saved order, rename, duplicate, delete, and
//   the order file beside the presets (one name per line; a folder without it lists alphabetically, as before).
// Does not own: what a preset file holds or how it is loaded (PluginLibrary, the MIDI processing window), or any window.
// Tests: TestPresetManagePanel.
public sealed class PresetFolderStore : IPresetRestorable
{
    /// <summary>The folder under <see cref="PluginLibrary.Root"/> that holds the plug-ins' presets.</summary>
    public const string PluginFolderName = "Presets";
    /// <summary>The file extension of a plug-in preset.</summary>
    public const string PluginFileExtension = ".tfpreset";
    private const string OrderFile = "preset-order.txt";
    private const int MaxPresets = 1000;
    private const long MaxFileBytes = 32L * 1024 * 1024;
    private readonly string _folder, _extension;
    private readonly Func<byte[], bool>? _accepts;

    /// <param name="accepts">Checks the bytes of an imported preset beyond the file form (a .json store checks JSON by default); null for none.</param>
    public PresetFolderStore(string folder, string extension, Func<byte[], bool>? accepts = null) { _folder = folder; _extension = extension; _accepts = accepts; }

    public string FileExtension => _extension;

    public bool Accepts(byte[] bytes) =>
        bytes.Length <= MaxFileBytes && (_accepts?.Invoke(bytes) ?? (!string.Equals(_extension, ".json", StringComparison.OrdinalIgnoreCase) || PresetBytes.IsJson(bytes)));

    public byte[]? Snapshot(string name)
    {
        try { return InputLimits.ReadBoundedBytes(PathOf(name), MaxFileBytes, "preset"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Services.Trace.Error(Services.Trace.Ui, "presets: read for undo: " + ex.Message);
            return null;
        }
    }

    public void Restore(string name, byte[] bytes, int index)
    {
        name = name.Trim();
        FilePathPolicy.WriteAtomically(PathOf(name), s => s.Write(bytes), createDirectory: true);
        var order = Names().ToList();
        order.RemoveAll(n => Same(n, name));
        order.Insert(Math.Clamp(index, 0, order.Count), name);
        Finish(order, force: File.Exists(Path.Combine(_folder, OrderFile)));
    }

    public static PresetFolderStore Plugin(PluginSlot slot) => new(PluginLibrary.PresetFolder(slot), PluginFileExtension);
    /// <summary>The presets of the plug-in whose preset folder has that name (<see cref="PluginKey"/>).</summary>
    public static PresetFolderStore Plugin(string key) => new(Path.Combine(PluginLibrary.Root, PluginFolderName, TabForge.Audio.Contracts.SafeFileNames.SafeFileName(key, "plugin", 64)), PluginFileExtension);
    public static string PluginKey(PluginSlot slot) => Path.GetFileName(PluginLibrary.PresetFolder(slot));
    /// <summary>The plug-ins that have saved presets, as keys for <see cref="Plugin(string)"/>.</summary>
    public static IReadOnlyList<string> PluginKeys() => Folders(Path.Combine(PluginLibrary.Root, PluginFolderName), PluginFileExtension);
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

    private const string TakenText = "A preset with that name already exists";

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

    public void Rename(string from, string to, bool replace = false)
    {
        to = to.Trim();
        var order = Names().ToList();
        if (!replace && !Same(from, to) && File.Exists(PathOf(to))) throw new IOException(TakenText);
        File.Move(PathOf(from), PathOf(to), overwrite: true);
        order.RemoveAll(n => Same(n, to) && !Same(n, from));
        var at = order.FindIndex(n => Same(n, from));
        if (at >= 0) order[at] = to;
        Finish(order, force: false);
    }

    public void Duplicate(string from, string to)
    {
        var order = Names().ToList();
        if (File.Exists(PathOf(to))) throw new IOException(TakenText);
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
