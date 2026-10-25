using TabForge.Presenters;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using TabForge.Services;

namespace TabForge.Plugins;

// Owns: the user's drum map files as the map editor and the manage panel change them: a checked draft of one map (pieces and the
//   note-to-note table) built from the editor's text rows and written atomically, and the drum maps folder as an IPresetStore
//   (rename, duplicate and delete keep the name inside the file in step with the file name).
// Does not own: reading maps and building the note table (DrumMapLibrary), or any window (Views/NoteMaps).
// Tests: no dedicated test class.
public sealed class DrumMapDraft
{
    public const int MaxPieceLength = DrumMapDraftPresenter.MaxPieceLength;

    public string Name { get; private init; } = "";
    public SortedDictionary<int, string> Pieces { get; private init; } = new();
    /// <summary>Incoming note to the note sent (-1: not sent), for the notes that pairing by piece name does not already send there.</summary>
    public SortedDictionary<int, int> Remap { get; private init; } = new();

    /// <summary>The note a source note becomes through piece names alone: the note of the same-named piece, or the note itself.</summary>
    public static int Paired(DrumMap? source, IReadOnlyDictionary<int, string> pieces, int note) =>
        DrumMapDraftPresenter.Paired(source?.Notes, pieces, note);

    /// <summary>A draft from the editor's rows, or why they cannot be saved (the checks are in <see cref="DrumMapDraftPresenter.Check"/>).</summary>
    public static (DrumMapDraft? Draft, string? Error) Build(string name, IEnumerable<(string Note, string Piece)> pieces,
        IEnumerable<(int Source, string SendAs)> sends, DrumMap? source, IReadOnlyDictionary<int, int>? keep = null)
    {
        var (plan, error) = DrumMapDraftPresenter.Check(name, pieces, sends, source?.Notes, keep, DrumMapLibrary.BuiltInNames());
        return plan is null ? (null, error) : (new DrumMapDraft { Name = plan.Name, Pieces = plan.Pieces, Remap = plan.Remap }, null);
    }

    /// <summary>Writes the map into the user's drum maps folder (a failed write leaves an older file intact) and has the library read the maps again.</summary>
    public void Save()
    {
        var notes = new JsonObject();
        foreach (var (note, piece) in Pieces) notes[note.ToString(CultureInfo.InvariantCulture)] = piece;
        var root = new JsonObject { ["Name"] = Name, ["Verified"] = false, ["Notes"] = notes };
        if (Remap.Count > 0)
        {
            var remap = new JsonObject();
            foreach (var (from, sent) in Remap) remap[from.ToString(CultureInfo.InvariantCulture)] = sent;
            root["Remap"] = remap;
        }
        var bytes = System.Text.Encoding.UTF8.GetBytes(root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        var store = new DrumMapStore();
        FilePathPolicy.WriteAtomically(store.FileOfMap(Name) ?? store.PathOf(Name), s => s.Write(bytes), createDirectory: true);
        DrumMapLibrary.Reload();
    }
}

/// <summary>The user's drum maps folder as the preset manage panel sees it: one row per map file.</summary>
public sealed class DrumMapStore : IPresetStore
{
    private readonly PresetFolderStore _files = new(DrumMapLibrary.UserFolder, ".json");

    public event Action? Changed;

    /// <summary>Reads the maps again and tells listeners: the folder was changed through another store.</summary>
    public void RaiseChanged() { DrumMapLibrary.Reload(); Changed?.Invoke(); }

    public string PathOf(string name) => _files.PathOf(name);
    public IReadOnlyList<string> Names() => _files.Names();

    /// <summary>The user's map files that cannot be read as a map (file names without the extension).</summary>
    public IReadOnlyList<string> Unreadable() => Names().Where(n => DrumMapLibrary.Parse(PathOf(n), user: true) is null).ToList();

    /// <summary>The file that holds the user's map of that name (the name inside the file, which a hand-written file may not carry in its file name); null when there is none.</summary>
    public string? FileOfMap(string name) =>
        Names().Select(PathOf).FirstOrDefault(f => DrumMapLibrary.Parse(f, user: true) is { } map && string.Equals(map.Name, name, StringComparison.OrdinalIgnoreCase));

    public void Rename(string from, string to, bool replace = false)
    {
        RefuseShipped(to);
        _files.Rename(from, to, replace);
        Finish(to);
    }

    public void Duplicate(string from, string to)
    {
        RefuseShipped(to);
        _files.Duplicate(from, to);
        Finish(to);
    }

    /// <summary>A user's copy of a map that comes with the app, under a new name.</summary>
    public void DuplicateShipped(string map, string to)
    {
        RefuseShipped(to);
        var file = Directory.Exists(DrumMapLibrary.ShippedFolder) ? Directory.EnumerateFiles(DrumMapLibrary.ShippedFolder, "*.json").Take(200)
            .FirstOrDefault(f => DrumMapLibrary.Parse(f, user: false) is { } m && string.Equals(m.Name, map, StringComparison.OrdinalIgnoreCase)) : null;
        if (file is null) throw new InvalidDataException("the map's file is missing");
        var bytes = InputLimits.ReadBoundedBytes(file, 256 * 1024, "drum map");
        FilePathPolicy.WriteAtomically(PathOf(to), s => s.Write(bytes), createDirectory: true);
        Finish(to);
    }

    public void Delete(string name) { _files.Delete(name); Finish(null); }
    public void Move(string name, int index) { _files.Move(name, index); Finish(null); }

    private static void RefuseShipped(string name)
    {
        if (DrumMapLibrary.All().Any(m => !m.IsUser && string.Equals(m.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("a map that comes with the app has that name");
    }

    /// <summary>Puts the file's name inside the file too (the library lists a map by the name inside), then has the library read again.</summary>
    private void Finish(string? named)
    {
        try
        {
            if (named is null) return;
            var path = PathOf(named);
            if (JsonNode.Parse(InputLimits.ReadBoundedBytes(path, 256 * 1024, "drum map")) is not JsonObject root) return;
            root["Name"] = named.Trim();
            var bytes = System.Text.Encoding.UTF8.GetBytes(root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            FilePathPolicy.WriteAtomically(path, s => s.Write(bytes));
        }
        catch (System.Text.Json.JsonException ex) { Services.Trace.Error(Services.Trace.Ui, "drum maps: name inside an unreadable file: " + ex.Message); }
        finally { DrumMapLibrary.Reload(); Changed?.Invoke(); }
    }
}
