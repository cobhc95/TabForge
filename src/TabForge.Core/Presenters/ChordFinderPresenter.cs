using TabForge.Services;

namespace TabForge.Presenters;

/// <summary>
/// The logic of the Chord finder window: the root and type choices, the note list for a choice, and the status texts.
/// Owns: the choice lists, the chord name, the note lines and the insert status text.
/// Does not own: the window's layout (ChordFinderWindow), the chord tables (MusicTheoryService) or the score edit.
/// Tests: no dedicated test class.
/// </summary>
public static class ChordFinderPresenter
{
    public static IReadOnlyList<string> RootChoices => MusicTheoryService.NoteNames;
    public static List<string> TypeChoices() => MusicTheoryService.Chords.Keys.ToList();

    public static string RootOrDefault(object? selected) => selected as string ?? "C";
    public static string TypeOrDefault(object? selected) => selected as string ?? "Maj";
    public static string ChordName(string root, string type) => $"{root}{type}";

    /// <summary>One line per chord tone, "E  (tone 4)"; an unknown type falls back to a major triad.</summary>
    public static List<string> NoteLines(string root, string type)
    {
        if (!MusicTheoryService.Chords.TryGetValue(type, out var intervals)) intervals = new[] { 0, 4, 7 };
        var rootIndex = Array.FindIndex(MusicTheoryService.NoteNames, n => n == root);
        return intervals.Select(tone => $"{MusicTheoryService.NoteNames[(rootIndex + tone) % 12]}  (tone {tone})").ToList();
    }

    public static string InsertStatus(bool inserted, string chordName) => inserted
        ? $"Inserted {chordName} on the beat under the cursor."
        : "Put the score cursor on a beat first.";
}
