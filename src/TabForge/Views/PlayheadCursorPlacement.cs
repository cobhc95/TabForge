namespace TabForge.Views;

// Owns: moving the edit cursor to the playhead when playback pauses or stops (same string, no seek, no scroll); a selection is left alone.
// Does not own: the playhead (PlaybackOverlay), the transport (MainWindow.Playback) or the cursor itself (TabEditorControl).
// Tests: TestPlayheadCursorPlacement.
/// <summary>Puts the edit cursor on the beat the playhead is on when the user pauses or stops.</summary>
internal static class PlayheadCursorPlacement
{
    /// <summary>Moves the cursor to the playhead's bar and cell. Does nothing when no playhead is shown.</summary>
    public static void MoveCursorToPlayhead(TabEditorControl editor)
    {
        var playhead = editor.Playback;
        if (playhead.Measure < 0 || playhead.Cell < 0) return;
        if (editor.HasSelection) return;   // a selection (the loop being played) stays: only Esc or a new click ends it
        editor.SetPosition(playhead.Measure, playhead.Cell, editor.SelectedString, seekPlayback: false);
    }
}
