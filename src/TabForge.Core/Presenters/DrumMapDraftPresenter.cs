using System.Globalization;

// Owns: the drum map editor's checks without UI types: the draft of one map from the editor's rows (the note-to-piece table and the
//       remap rows) or the message that says why it cannot be saved, and the pairing of a note by piece name.
// Does not own: the editor's rows and messages (Views/NoteMaps/DrumMapEditorWindow), the file (DrumMapDraft.Save), or the built-in maps
//       (the caller passes their names).
// Tests: no dedicated test class.
namespace TabForge.Presenters;

/// <summary>A checked draft of one drum map: the name, the pieces by note and the remap of notes that are not sent as they arrive.</summary>
public sealed record DrumMapDraftPlan(string Name, SortedDictionary<int, string> Pieces, SortedDictionary<int, int> Remap);

public static class DrumMapDraftPresenter
{
    public const int MaxPieceLength = 64;

    /// <summary>The note a source note becomes through piece names alone: the note of the same-named piece, or the note itself.</summary>
    public static int Paired(IReadOnlyDictionary<int, string>? sourceNotes, IReadOnlyDictionary<int, string> pieces, int note)
    {
        if (sourceNotes is null || !sourceNotes.TryGetValue(note, out var piece)) return note;
        foreach (var (target, name) in pieces.OrderBy(p => p.Key))
            if (string.Equals(name, piece, StringComparison.OrdinalIgnoreCase)) return target;
        return note;
    }

    /// <summary>
    /// A draft from the editor's rows, or why they cannot be saved. <paramref name="pieces"/> are the grid rows (note number text, piece
    /// name; a wholly empty row is skipped); <paramref name="sends"/> are the remap rows (source note, "send as" text: a note number,
    /// "x" for not sent, empty for unchanged) relative to <paramref name="sourceNotes"/>; <paramref name="keep"/> is the edited map's
    /// remap, kept for the notes that have no remap row; <paramref name="builtInNames"/> are the names the app's own maps carry.
    /// </summary>
    public static (DrumMapDraftPlan? Plan, string? Error) Check(string name, IEnumerable<(string Note, string Piece)> pieces,
        IEnumerable<(int Source, string SendAs)> sends, IReadOnlyDictionary<int, string>? sourceNotes, IReadOnlyDictionary<int, int>? keep,
        IReadOnlyCollection<string> builtInNames)
    {
        name = name.Trim();
        if (PresetRules.Error(name) is { } nameError) return (null, nameError);
        if (builtInNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
            return (null, "A map that comes with the app has that name. Choose another name.");
        var draftPieces = new SortedDictionary<int, string>();
        foreach (var (noteText, pieceText) in pieces)
        {
            var piece = pieceText.Trim();
            if (noteText.Trim().Length == 0 && piece.Length == 0) continue;
            if (Note(noteText) is not { } note) return (null, $"\"{noteText.Trim()}\" is not a note number from 0 to 127.");
            if (piece.Length == 0) return (null, $"Note {note} needs a piece name.");
            if (piece.Length > MaxPieceLength) return (null, $"The piece name of note {note} is longer than {MaxPieceLength} characters.");
            if (!draftPieces.TryAdd(note, piece)) return (null, $"Note {note} is listed twice.");
        }
        var draftRemap = new SortedDictionary<int, int>();
        var rows = new HashSet<int>();
        foreach (var (from, text) in sends)
        {
            if (from is < 0 or > MidiLimits.Max || !rows.Add(from)) return (null, $"Note {from} has two remap rows.");
            var sendAs = text.Trim();
            var sent = sendAs.Length == 0 ? Paired(sourceNotes, draftPieces, from) : sendAs is "x" or "X" or "-" ? -1 : Note(sendAs) ?? -2;
            if (sent == -2) return (null, $"Send note {from} as a note number from 0 to 127 (or x for not sent); \"{sendAs}\" is neither.");
            if (sent != Paired(sourceNotes, draftPieces, from)) draftRemap[from] = sent;
        }
        foreach (var (from, sent) in keep ?? new Dictionary<int, int>())
            if (!rows.Contains(from) && from is >= 0 and <= MidiLimits.Max && sent is >= -1 and <= MidiLimits.Max) draftRemap[from] = sent;
        return (new DrumMapDraftPlan(name, draftPieces, draftRemap), null);
    }

    private static int? Note(string text) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n <= MidiLimits.Max ? n : null;
}
