using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Playback;

// Owns: re-scheduling a playing (or paused, or still compiling) song after a score edit, so the engine plays the edited notes from the position it is at.
// Does not own: the engine's transport (PlaybackEngine.Seek), what counts as a sound change (PlaybackEditDiff), the debounce (the app's PlaybackEditWatch) or the track list refresh (RefreshArrangementLive).
// Tests: SelfTestPlaybackEditRefresh.
// Threads: owner (UI) thread only, like the engine's transport calls; it holds the live song only for the length of a call.

/// <summary>Live score edits reach the running schedule: it is recompiled from a copy of the edited song and resumed at the current position.</summary>
public sealed class PlaybackEditRefresh
{
    private readonly PlaybackEngine _engine;
    private readonly ProjectStateEncoder _encoder = new(ProjectStateEncoder.PlaybackParallelEncodeBars);   // keeps the last copy, so an edit encodes only the bars it changed

    public PlaybackEditRefresh(PlaybackEngine engine) => _engine = engine;

    /// <summary>
    /// Call after an edit of <paramref name="project"/>, the song the engine plays. A stopped engine does nothing (the next start copies the edited song).
    /// A start still compiling begins again from the edited song. A playing or paused song is silenced, recompiled on a worker from a copy of the song and
    /// resumed at the position it was at, so notes deleted or changed are heard from the next unplayed event and nothing already queued ahead survives.
    /// Returns true when the schedule was replaced; a mark that changed nothing audible (a mixer level) returns false.
    /// </summary>
    public bool Run(SongProject project)
    {
        if (_engine.SnapshotForEdit(out var startPending) is not { } playing) return false;
        if (startPending)
        {
            // The compile began from the song before the edit: begin the start again at its bar and cell (a seek on a pending start does exactly that).
            if (playing.ContentRevision == project.ContentRevision) return false;
            _engine.Seek(project, _engine.CurrentBar, _engine.CurrentCell);
            return true;
        }
        if (!PlaybackEditDiff.SoundChanged(playing, SongSnapshot.Take(project, _encoder), project)) return false;
        var position = _engine.Playhead();
        var lastBar = Math.Max(0, BarRangeEditor.MaxMeasures(project) - 1);   // an edit may have removed the bars the playhead was in
        var bar = Math.Clamp(position.Bar, 0, lastBar);
        var occurrence = _engine.LiveOccurrence(project);
        _engine.MarkEditSeek();
        _engine.Seek(project, bar, bar == position.Bar ? Math.Max(0, position.Cell) : 0, occurrence);
        return true;
    }
}
