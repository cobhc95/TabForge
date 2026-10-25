using TabForge.Models;

namespace TabForge.ScoreEditing;

// Owns: the Rhythm and Layout palette tools that edit the notes at the cursor or in the selection: tie, octave marking, beaming and stem direction.
// Does not own: the edits themselves (ScoreEditCommands.Marks.cs), the tool buttons or the tools that open a dialog or change a bar (PaletteBarCommands).
// Tests: no dedicated test class.
public sealed partial class ScoreEditCommands
{
    /// <summary>Runs the note-level palette tool with this id; false for every other id.</summary>
    public bool TryRunPaletteTool(string id)
    {
        switch (id)
        {
            case "gp:tie_note": TieSelectedNote(); break;
            case "gp:tie_beat": TieSelectedBeat(); break;
            case "gp:octave_8va": SetOctaveShift(12); break;
            case "gp:octave_8vb": SetOctaveShift(-12); break;
            case "gp:octave_15ma": SetOctaveShift(24); break;
            case "gp:octave_15mb": SetOctaveShift(-24); break;
            case "gp:beam_auto": SetBeamMode(BeamMode.Auto); SetSecondaryBeamBreak(false); break;
            case "gp:beam_force": SetBeamMode(BeamMode.Force); break;
            case "gp:beam_break": SetBeamMode(BeamMode.Break); break;
            case "gp:beam_break_secondary": SetSecondaryBeamBreak(GetNoteCellToolState(cell => cell.BreakSecondaryBeamBefore) != true); break;
            case "gp:stem_auto": SetStemDirection(StemDirection.Auto); break;
            case "gp:stem_invert": ToggleStemInvert(); break;
            default: return false;
        }
        return true;
    }

    private void ToggleStemInvert()
    {
        var cell = CurrentCell();
        if (cell is null || !HasEditableNotes) return;
        SetStemDirection(cell.StemDirection == StemDirection.Invert ? StemDirection.Auto : StemDirection.Invert);
    }
}
