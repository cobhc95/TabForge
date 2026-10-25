using TabForge.Models;

namespace TabForge.ScoreEditing;

// Owns: whether a Structure, Rhythm or Layout palette tool (a gp: id) is lit and whether it can run at the cursor, for both palettes.
// Does not own: the buttons that show it, the Tools tab's states (ScoreEditCommands.GetToolState) or the edits (ScoreEditCommands, PaletteBarCommands).
// Tests: no dedicated test class.
/// <summary>What the palette state rules read from the editor behind a palette.</summary>
public interface IPaletteToolContext
{
    SongProject Project { get; }
    /// <summary>The bar under the cursor on the shown track, or null.</summary>
    MeasureModel? CurrentBar { get; }
    ScoreEditCommands Edit { get; }
    int SelectedMeasure { get; }
    int SelectedString { get; }
    int ActiveVoiceIndex { get; }
    /// <summary>A track is shown (the voice and gray-voice tools need one).</summary>
    bool HasTrack { get; }
    int MaxMeasures { get; }
}

/// <summary>The lit and enabled rules of the gp: palette tools.</summary>
public static class PaletteToolStates
{
    /// <summary>True when the tool is on at the cursor, false when off, null when it has no state or the beats disagree; a non-gp id asks the editor's tool state.</summary>
    public static bool? Active(string id, IPaletteToolContext context)
    {
        var edit = context.Edit;
        if (!id.StartsWith("gp:", StringComparison.Ordinal)) return edit.GetToolState(id);
        var bar = context.CurrentBar;
        var project = context.Project;
        var cell = edit.CurrentCell();
        var selectedNote = cell?.Notes.FirstOrDefault(note => note.StringIndex == context.SelectedString);
        return id switch
        {
            "gp:key_signature" => bar is not null && ((bar.KeySignature ?? project.KeySignature) != 0 || (bar.KeySignatureMinor ?? project.KeySignatureMinor)),
            "gp:triplet_feel" => bar is not null && (bar.TripletFeel || bar.TripletFeelKind != "None"),
            "gp:free_time" => bar?.FreeTime,
            "gp:double_barline" => bar?.IsDoubleBar,
            "gp:repeat_one_bar" => bar?.SimileOneBar,
            "gp:repeat_two_bars" => bar?.SimileTwoBar,
            "gp:directions" => bar is not null && (!string.IsNullOrWhiteSpace(bar.Directions) || bar.AlternateEnding > 0),
            "gp:force_line_break" => bar?.ForceLineBreak,
            "gp:prevent_line_break" => bar?.PreventLineBreak,
            "gp:add_marker" or "gp:marker_list" or "gp:previous_marker" or "gp:next_marker" => false,
            "gp:custom_ntuplet" => edit.GetToolState("duration:tuplet"),
            "gp:tie_note" => selectedNote is not null && (selectedNote.Tied || cell!.IsTied),
            "gp:tie_beat" => cell is { Notes.Count: > 0 } && cell.Notes.All(note => note.Tied || cell.IsTied),
            "gp:sound_duration" => edit.GetNoteCellToolState(noteCell => noteCell.SoundDurationPercent != 100),
            "gp:octave_8va" => edit.GetNoteCellToolState(noteCell => noteCell.OctaveShiftSemitones == 12),
            "gp:octave_8vb" => edit.GetNoteCellToolState(noteCell => noteCell.OctaveShiftSemitones == -12),
            "gp:octave_15ma" => edit.GetNoteCellToolState(noteCell => noteCell.OctaveShiftSemitones == 24),
            "gp:octave_15mb" => edit.GetNoteCellToolState(noteCell => noteCell.OctaveShiftSemitones == -24),
            "gp:voice_1" => context.ActiveVoiceIndex == 0,
            "gp:voice_2" => context.ActiveVoiceIndex == 1,
            "gp:inactive_voice_gray" => project.GrayInactiveVoice,
            "gp:beam_auto" => edit.GetNoteCellToolState(noteCell => noteCell.BeamMode == BeamMode.Auto && !noteCell.BreakSecondaryBeamBefore),
            "gp:beam_force" => edit.GetNoteCellToolState(noteCell => noteCell.BeamMode == BeamMode.Force),
            "gp:beam_break" => edit.GetNoteCellToolState(noteCell => noteCell.BeamMode == BeamMode.Break),
            "gp:beam_break_secondary" => edit.GetNoteCellToolState(noteCell => noteCell.BreakSecondaryBeamBefore),
            "gp:stem_auto" => edit.GetNoteCellToolState(noteCell => noteCell.StemDirection == StemDirection.Auto),
            "gp:stem_invert" => edit.GetNoteCellToolState(noteCell => noteCell.StemDirection == StemDirection.Invert),
            _ => null
        };
    }

    /// <summary>Whether the tool can act at the cursor; every other tool is always enabled.</summary>
    public static bool Enabled(string id, IPaletteToolContext context)
    {
        var bar = context.CurrentBar;
        var edit = context.Edit;
        return id switch
        {
            "gp:key_signature" or "gp:triplet_feel" or "gp:free_time" or "gp:double_barline" or
            "gp:directions" or "gp:force_line_break" or "gp:prevent_line_break" => bar is not null,
            "gp:repeat_one_bar" => bar is not null && context.SelectedMeasure > 0,
            "gp:repeat_two_bars" => bar is not null && context.SelectedMeasure > 1,
            "gp:add_marker" => context.SelectedMeasure >= 0 && context.SelectedMeasure < context.MaxMeasures,
            "gp:marker_list" => true,
            "gp:previous_marker" => context.Project.Markers.Any(marker => marker.MeasureIndex < context.SelectedMeasure),
            "gp:next_marker" => context.Project.Markers.Any(marker => marker.MeasureIndex > context.SelectedMeasure),
            "gp:custom_ntuplet" => edit.CanSetTuplet((3, 2)),
            "gp:tie_note" => edit.CanTieSelectedNote(),
            "gp:tie_beat" => edit.CanTieSelectedBeat(),
            "gp:sound_duration" or "gp:octave_8va" or "gp:octave_8vb" or "gp:octave_15ma" or "gp:octave_15mb" => edit.HasEditableNotes,
            "gp:voice_1" or "gp:voice_2" or "gp:inactive_voice_gray" => context.HasTrack,
            "gp:beam_auto" or "gp:beam_force" or "gp:beam_break" or "gp:stem_auto" or "gp:stem_invert" => edit.HasEditableNotes,
            "gp:beam_break_secondary" => edit.HasSecondaryBeamEligibleNotes,
            _ => true
        };
    }
}
