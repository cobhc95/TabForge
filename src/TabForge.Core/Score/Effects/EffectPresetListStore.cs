using System.Text.Json;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Presenters;

namespace TabForge.Views.EffectEditors;

// Owns: the user presets of one note-effect editor kind (AppSettings.EffectPresets filtered by kind) as an IPresetStore the preset manager and
//   the editors' preset lists share: names in the saved order, rename, duplicate, delete, move, and the bytes a delete can be undone from.
// Does not own: the built-in lists or saving a curve (EffectPresetStore), writing the settings file (the callback it is given), or any window.
// Tests: PresetUndoTests.
public sealed class EffectPresetListStore : IPresetRestorable
{
    private const string TakenText = "A preset with that name already exists";
    private const string ReservedText = "A preset that comes with the app has that name";
    private const string FullText = "There is no room for another preset of this kind";
    private const string MissingText = "The preset is not there any more";

    private readonly AppSettings _settings;
    private readonly EffectEditorKind _kind;
    private readonly Action? _save;

    /// <param name="save">Writes the settings after each change; null when the caller saves.</param>
    public EffectPresetListStore(AppSettings settings, EffectEditorKind kind, Action? save = null) => (_settings, _kind, _save) = (settings, kind, save);

    public event Action? Changed;

    /// <summary>Tells listeners to list again: the settings were changed through another store on the same kind.</summary>
    public void RaiseChanged() => Changed?.Invoke();

    public string FileExtension => ".json";

    private string Key => _kind.ToString();

    private List<EffectPresetEntry> Mine() => _settings.EffectPresets.Where(p => p.Kind == Key).ToList();

    private EffectPresetEntry? Find(string name) => Mine().FirstOrDefault(p => Same(p.Name, name));

    public IReadOnlyList<string> Names() => Mine().Select(p => p.Name).ToList();

    public void Rename(string from, string to, bool replace = false)
    {
        to = to.Trim();
        var entry = Find(from) ?? throw new IOException(MissingText);
        RefuseBuiltIn(to);
        var clash = Find(to);
        if (clash is not null && !ReferenceEquals(clash, entry))
        {
            if (!replace) throw new IOException(TakenText);
            _settings.EffectPresets.Remove(clash);
        }
        entry.Name = to;
        Finish();
    }

    public void Duplicate(string from, string to)
    {
        to = to.Trim();
        var entry = Find(from) ?? throw new IOException(MissingText);
        RefuseBuiltIn(to);
        if (Find(to) is not null) throw new IOException(TakenText);
        RefuseWhenFull();
        var copy = Clone(entry, to);
        _settings.EffectPresets.Insert(_settings.EffectPresets.IndexOf(entry) + 1, copy);
        Finish();
    }

    public void Delete(string name)
    {
        _settings.EffectPresets.RemoveAll(p => p.Kind == Key && Same(p.Name, name));
        Finish();
    }

    public void Move(string name, int index)
    {
        var entry = Find(name);
        if (entry is null) return;
        _settings.EffectPresets.Remove(entry);
        PlaceAt(entry, index);
        Finish();
    }

    public byte[]? Snapshot(string name) => Find(name) is { } entry ? JsonSerializer.SerializeToUtf8Bytes(entry) : null;

    public bool Accepts(byte[] bytes) => Read(bytes) is not null;

    public void Restore(string name, byte[] bytes, int index)
    {
        name = name.Trim();
        if (PresetRules.Error(name) is { } error) throw new InvalidDataException(error);
        RefuseBuiltIn(name);
        var entry = Read(bytes) ?? throw new InvalidDataException("not an effect preset");
        entry.Name = name;
        entry.Kind = Key;
        if (Find(name) is { } old) _settings.EffectPresets.Remove(old);
        else RefuseWhenFull();
        PlaceAt(entry, index);
        Finish();
    }

    private static EffectPresetEntry? Read(byte[] bytes)
    {
        try { return bytes.Length > MaxEntryBytes ? null : JsonSerializer.Deserialize<EffectPresetEntry>(bytes); }
        catch (JsonException) { return null; }
    }

    private const int MaxEntryBytes = 256 * 1024;

    private static EffectPresetEntry Clone(EffectPresetEntry entry, string name) => new()
    {
        Kind = entry.Kind, Name = name, Points = entry.Points.Select(p => p.Clone()).ToList(), Values = new Dictionary<string, double>(entry.Values),
    };

    // Puts the entry at a place among this kind's presets (the other kinds keep their places).
    private void PlaceAt(EffectPresetEntry entry, int index)
    {
        var mine = Mine();
        var at = index >= mine.Count ? _settings.EffectPresets.Count : _settings.EffectPresets.IndexOf(mine[Math.Max(index, 0)]);
        _settings.EffectPresets.Insert(at, entry);
    }

    private void RefuseBuiltIn(string name)
    {
        if (EffectPresetStore.BuiltIn(_kind).Any(p => Same(p.Name, name))) throw new InvalidDataException(ReservedText);
    }

    private void RefuseWhenFull()
    {
        if (Mine().Count >= EffectPresetStore.MaxPerKind) throw new IOException(FullText);
    }

    private void Finish()
    {
        try { _save?.Invoke(); }
        finally { Changed?.Invoke(); }
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
