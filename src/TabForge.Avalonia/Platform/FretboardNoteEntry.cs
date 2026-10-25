using System;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views.Score;

namespace TabForge.Avalonia.Platform;

// Owns: the fretboard click that writes a note at the score cursor's cell, or removes the one already there (ScoreEditCommands.ToggleFretAtPosition),
//     and the drum-pad click that writes a drum sound on its drum-map line. A click edits while the song plays, as the WPF panel does.
// Does not own: the note rules and undo (Core ScoreEditCommands, DocumentEdits), the hover ghost (InstrumentPanelView), which clicks reach it (the strip).
// Tests: FretboardNoteEntryTests.
// Threads: UI thread only; each click is one edit through the editor's own RunEdit.

/// <summary>Writes or removes a fretboard note at the score cursor.</summary>
internal static class FretboardNoteEntry
{
    /// <summary>Toggles the note on <paramref name="stringIndex"/> at <paramref name="fret"/> in the cursor's cell. False, with no edit, while the song plays or when the cell cannot take the note.</summary>
    internal static bool Toggle(ScorePreviewEditor editor, int stringIndex, int fret) =>
        editor.Effects.ToggleFretAtPosition(stringIndex, fret);

    /// <summary>Toggles a drum sound at the cursor's cell, on the drum-map line the track's drum map gives it.</summary>
    internal static bool ToggleDrumSound(ScorePreviewEditor editor, TrackModel drumTrack, int percussion) =>
        editor.Effects.ToggleFretAtPosition(PercussionLine(drumTrack, percussion), percussion);

    /// <summary>The drum-map line a sound writes to: its tab line, kept inside the track's lines.</summary>
    internal static int PercussionLine(TrackModel drumTrack, int percussion) =>
        Math.Clamp(DrumMaps.For(drumTrack, percussion).TabLine, 0, Math.Max(0, drumTrack.StringTunings.Count - 1));
}
