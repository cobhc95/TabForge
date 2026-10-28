using TabForge.Models;
using TabForge.Views;
using TabForge.Views.Score;

namespace TabForge.ScoreEditing;

// Owns: what the note-effect editor dialogs act on (selection or cursor note) and how their change enters the edit pipeline.
// Does not own: the dialogs (Views/EffectEditors) or the model writes (EffectEdits).
// Tests: no dedicated test class.
public sealed partial class ScoreEditCommands
{
    /// <summary>The selected notes, or the note on the cursor's string; empty when there is none.</summary>
    public List<TabNote> EffectNotes()
    {
        if (HasSelection) return ToolCells().SelectMany(cell => cell.Notes).ToList();
        var note = CurrentCell()?.Notes.FirstOrDefault(n => n.StringIndex == SelectedString);
        return note is null ? new List<TabNote>() : new List<TabNote> { note };
    }

    /// <summary>The beats of the selection (or the cursor beat) that hold notes.</summary>
    public List<TabCell> EffectCells()
    {
        var cells = HasSelection ? ToolCells() : CurrentCell() is { } current ? new List<TabCell> { current } : new List<TabCell>();
        return cells.Where(cell => cell.Notes.Count > 0).ToList();
    }

    /// <summary>The level (quarter tones) the bend of the note before the first effect note, on its string, ends on; 0 when there is no such bend.</summary>
    public double PreviousBendEnd()
    {
        if (EffectCells().FirstOrDefault() is not { } cell || PreviousCellOf(cell) is not { } previous) return 0;
        var note = previous.Notes.FirstOrDefault(n => n.StringIndex == (EffectNotes().FirstOrDefault()?.StringIndex ?? SelectedString));
        return LevelAtEnd(note?.BendPoints);
    }

    /// <summary>The level (quarter tones) the tremolo bar of the beat before the first effect beat ends on; 0 when there is none.</summary>
    public double PreviousWhammyEnd() =>
        EffectCells().FirstOrDefault() is { } cell && PreviousCellOf(cell) is { } previous ? LevelAtEnd(previous.WhammyPoints) : 0;

    private static double LevelAtEnd(IReadOnlyList<BendPointModel>? points) => points is { Count: > 0 } ? points.OrderBy(p => p.Offset).Last().Value : 0;

    /// <summary>The displayed track (string tunings for the fret <-> pitch conversion of the trill and grace editors).</summary>
    public TrackModel? EffectTrack => Track;

    /// <summary>Runs one effect-editor change as a single undo step (DocumentEdits.Run through the editor host).</summary>
    public bool EditEffect(Func<bool> mutate) => RunEdit(mutate);
}
