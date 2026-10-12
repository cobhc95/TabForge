using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using TabForge.Services;

namespace TabForge.Plugins;

// Owns: the user's drum map files as the map editor and the manage panel change them: a checked draft of one map (pieces and the
//   note-to-note table) built from the editor's text rows and written atomically, and the drum maps folder as an IPresetStore
//   (rename, duplicate and delete keep the name inside the file in step with the file name).
// Does not own: reading maps and building the note table (DrumMapLibrary), or any window (Views/NoteMaps).
// Tests: TestDrumMapRemap, TestDrumMapEditorStore.
public sealed class DrumMapDraft
{
    public const int MaxPieceLength = 64;

    public string Name { get; private init; } = "";
    public SortedDictionary<int, string> Pieces { get; } = new();
    /// <summary>Incoming note to the note sent (-1: not sent), for the notes that pairing by piece name does not already send there.</summary>
    public SortedDictionary<int, int> Remap { get; } = new();

    /// <summary>The note a source note becomes through piece names alone: the note of the same-named piece, or the note itself.</summary>
    public static int Paired(DrumMap? source, IReadOnlyDictionary<int, string> pieces, int note)
    {
        if (source is null || !source.Notes.TryGetValue(note, out var piece)) return note;
        foreach (var (target, name) in pieces.OrderBy(p => p.Key))
            if (string.Equals(name, piece, StringComparison.OrdinalIgnoreCase)) return target;
        return note;
    }

    /// <summary>
    /// A draft from the editor's rows, or why they cannot be saved. <paramref name="pieces"/> are the grid rows (note number text, piece
    /// name; a wholly empty row is skipped); <paramref name="sends"/> are the remap rows (source note, "send as" text: a note number,
    /// "x" for not sent, empty for unchanged) relative to <paramref name="source"/>; <paramref name="keep"/> is the edited map's note
    /// table, kept for the notes that have no remap row.
    /// </summary>
    public static (DrumMapDraft? Draft, string? Error) Build(string name, IEnumerable<(string Note, string Piece)> pieces,
        IEnumerable<(int Source, string SendAs)> sends, DrumMap? source, IReadOnlyDictionary<int, int>? keep = null)
    {
        name = name.Trim();
        if (PresetRules.Error(name) is { } nameError) return (null, nameError);
        if (DrumMapLibrary.All().Any(m => !m.IsUser && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
            return (null, "A map that comes with the app has that name. Choose another name.");
        var draft = new DrumMapDraft { Name = name };
        foreach (var (noteText, pieceText) in pieces)
        {
            var piece = pieceText.Trim();
            if (noteText.Trim().Length == 0 && piece.Length == 0) continue;
            if (Note(noteText) is not { } note) return (null, $"\"{noteText.Trim()}\" is not a note number from 0 to 127.");
            if (piece.Length == 0) return (null, $"Note {note} needs a piece name.");
            if (piece.Length > MaxPieceLength) return (null, $"The piece name of note {note} is longer than {MaxPieceLength} characters.");
            if (!draft.Pieces.TryAdd(note, piece)) return (null, $"Note {note} is listed twice.");
        }
        var rows = new HashSet<int>();
        foreach (var (from, text) in sends)
        {
            if (from is < 0 or > 127 || !rows.Add(from)) return (null, $"Note {from} has two remap rows.");
            var sendAs = text.Trim();
            var sent = sendAs.Length == 0 ? Paired(source, draft.Pieces, from) : sendAs is "x" or "X" or "-" ? -1 : Note(sendAs) ?? -2;
            if (sent == -2) return (null, $"Send note {from} as a note number from 0 to 127 (or x for not sent); \"{sendAs}\" is neither.");
            if (sent != Paired(source, draft.Pieces, from)) draft.Remap[from] = sent;
        }
        foreach (var (from, sent) in keep ?? new Dictionary<int, int>())
            if (!rows.Contains(from) && from is >= 0 and <= 127 && sent is >= -1 and <= 127) draft.Remap[from] = sent;
        return (draft, null);
    }

    private static int? Note(string text) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n <= 127 ? n : null;

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

    public void Rename(string from, string to)
    {
        RefuseShipped(to);
        _files.Rename(from, to);
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
