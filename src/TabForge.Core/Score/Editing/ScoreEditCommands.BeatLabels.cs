using TabForge.Models;

namespace TabForge.ScoreEditing;

// Owns: the chord name and the text of the cursor beat, read for the Note menu prompts and written as one undo step.
// Does not own: the prompts (Avalonia MainWindowNoteDialogs), the undo pipeline (RunEdit, DocumentEdits).
// Tests: TabForge.Avalonia.Tests.MainWindowMenuTests (the Note rows are handled).
public sealed partial class ScoreEditCommands
{
    /// <summary>Whether the cursor sits on a beat, so the chord and text prompts have one to write to.</summary>
    public bool HasCursorBeat => CurrentCell() is not null;

    /// <summary>The chord name of the cursor beat (the chord prompt's default); null when none is set.</summary>
    public string? CursorChordName => CurrentCell()?.ChordName;

    /// <summary>The text of the cursor beat (the text prompt's default); null when none is set.</summary>
    public string? CursorBeatText => CurrentCell()?.Text;

    /// <summary>Sets the chord name of the cursor beat as one undo step; false when there is no beat.</summary>
    public bool SetCursorChordName(string? name) => SetCursorBeatField(cell => cell.ChordName = name);

    /// <summary>Sets the text of the cursor beat as one undo step; false when there is no beat.</summary>
    public bool SetCursorBeatText(string? text) => SetCursorBeatField(cell => cell.Text = text);

    private bool SetCursorBeatField(Action<TabCell> write)
    {
        var cell = CurrentCell();
        return cell is not null && RunEdit(() => { write(cell); return true; });
    }
}
