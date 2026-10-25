// Owns: the score editor's tool state that entry and edit commands read: the current duration (denominator, dots, triplet,
//   tuplet), the entry velocity, the entry flags, and the rule for when deletes merge rests.
// Does not own: the edits themselves (ScoreEditCommands), the undo step (DocumentEdits.Run through RunEdit) or the cursor.
// Tests: no dedicated test class.

using TabForge.ScoreEditing;

namespace TabForge.Presenters;

/// <summary>The duration, velocity and entry flags new notes are written with; one per score editor, read and written on the UI thread.</summary>
public sealed class ScoreEditToolState
{
    public int DurationDenominator { get; set; } = 4;
    public int Dots { get; set; }
    public bool Triplet { get; set; }
    public int TupletNumerator { get; set; }
    public int TupletDenominator { get; set; }
    public int Velocity { get; set; } = 100;

    public bool AutoAdvanceAfterEntry { get; set; }
    /// <summary>Alias of <see cref="AutoAdvanceAfterEntry"/> with the Preferences row's name.</summary>
    public bool MoveToNextNoteAfterWriting { get => AutoAdvanceAfterEntry; set => AutoAdvanceAfterEntry = value; }
    /// <summary>Default: + shortens the note, - lengthens it. True swaps them ("Plus key lengthens notes").</summary>
    public bool PlusKeyLengthensNotes { get; set; }
    /// <summary>Alias of <see cref="PlusKeyLengthensNotes"/> (the old name).</summary>
    public bool ReversePlusMinusDuration { get => PlusKeyLengthensNotes; set => PlusKeyLengthensNotes = value; }
    /// <summary>When true, duration/dot/tuplet changes that would overfill a bar are refused.</summary>
    public bool PreventBarOverflow { get; set; }
    /// <summary>Fill incomplete bars with rests (the setting): edits keep every edited bar complete.</summary>
    public bool FillBarsWithRests { get; set; }
    /// <summary>Deleting notes leaves merged rests (the fewest that fill the bar) instead of a rest of the same length.</summary>
    public bool MergeRestsOnDelete { get; set; } = true;

    /// <summary>Deletes merge rests only while bars are kept full of rests.</summary>
    public bool MergeRestsOnDeleteActive => MergeRestsOnDelete && FillBarsWithRests;
}
