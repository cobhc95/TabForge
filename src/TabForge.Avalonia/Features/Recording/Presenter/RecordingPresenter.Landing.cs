using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Features.Recording.Presenter;

/// <summary>One pass a re-record replaces: its track, group and pass number, and the loop range it records once.</summary>
public sealed record PassPunch(TrackModel Track, string GroupId, int Pass, double LoopStart, double LoopEnd)
{
    /// <summary>The one undo step's label (and the status text).</summary>
    public string UndoLabel => $"Re-record pass {Pass}";
}

// Owns: the Transport.Record start/stop toggle and the landing of its takes (MIDI now, audio as its files arrive) as one undo step.
// Does not own: the live take model (LiveTakes), the engine hooks (RecordingPresenter.EngineHooks) or the recording rules (TransportRecording).
// Tests: TabForge.Avalonia.Tests.TransportRecordingTests, CountInRecordingTests, RecordingLaneLifecycleTests, UndoRulesTests, RepeatTakeLandingTests.
internal sealed partial class RecordingPresenter
{
    /// <summary>
    /// Transport.Record: starts the song's playback and the recording of its armed tracks, or ends the recording, stops the playback and
    /// leaves the cursor and transport at the playhead where recording stopped. <paramref name="loop"/> is the loop range in song seconds
    /// while the song loops: the audio take lands as one take per lap and the MIDI capture keeps one take per lap; null records
    /// one continuous pass. A refused start (no armed track, no engine input, no MIDI device) leaves the recording off and rolls back a playback
    /// this call started (a paused song is left paused), so the transport is never left playing with no capture. The live takes
    /// (<paramref name="hooks"/>) follow the start, stop and refusal, and <paramref name="playback"/> is told the engine's song position once
    /// before the record command (null: no engine). The takes keep the song second (the playing timeline's second converted to the
    /// song's performed clock), and so does the engine's song position, which its clips follow. The MIDI capture follows the session's
    /// live song second from the start, so notes land where the song is when heard. The transport always rolls while recording: from a stop the song is seeked to the take's first bar and
    /// played there, from a pause it is resumed, and a song that already plays keeps its position (its schedule takes the record count-in
    /// pre-roll when the count-in switch is on).
    /// </summary>
    internal static void Toggle(TransportRecording recording, AvaloniaPlaybackSession session, SongDocument song, Action<string> showStatus, RecordStart start,
        EngineHooks? hooks = null, (double Start, double End)? loop = null, AvaloniaPlaybackSessions? playback = null,
        PassPunch? punch = null, RepeatPosition? position = null)
    {
        var tracks = song.Project.Tracks;
        if (recording.IsRecording)
        {
            StopAndLand(recording, session, song, showStatus, start, hooks, punch);
            return;
        }
        // A re-record captures only the pass's own track (other armed tracks stay armed and untouched); a normal take captures the armed tracks.
        IReadOnlyList<TrackModel>? capture = punch is { } target ? new[] { target.Track } : null;
        IReadOnlyList<TrackModel> captureTracks = capture ?? tracks;
        if (capture is null && !TransportRecording.AnyArmed(tracks))
        {
            WarnNothingArmed(showStatus);
            return;
        }
        // A recording still waiting for its count-in to end has captured nothing: a second Record press cancels it.
        if (session.CancelAfterCountIn())
        {
            session.Stop();
            hooks?.ClearLive();
            showStatus(TransportRecording.StoppedText);
            return;
        }
        var (wasPlaying, wasPaused) = ResolveStart(session, song, start, punch, position);
        // Record always rolls the transport: a stopped song starts playing at the take's first bar (the session's own start may be stale
        // after the cursor moved without a seek), and a paused one is resumed, because a paused transport captures nothing.
        if (wasPlaying)
        {
            if (wasPaused) session.PlayOrPause();   // a paused transport would capture nothing
        }
        else
        {
            // Play, and record, from the take's first bar; a later pass uses the S4a pass-aware seek so the transport
            // and the take both start at the same performed second. Pass 1 keeps the unchanged bar/cell seek.
            if (position is { Pass: > 1 } passStart) session.Seek(passStart);
            else session.Seek(start.Bar, start.Cell);
            session.PlayOrPause();
        }
        // Record while the song already plays: one bar of count-in is held at the playhead first, when count-in is on (the scheduler plays
        // it). From a stop the start's own count-in is the pre-roll (the play options carry it), so asking for a second one would double it;
        // it is asked for after the resume, since the scheduler drops the request while paused.
        if (wasPlaying && session.CountIn) (session.Engine as TabForge.Playback.PlaybackEngine)?.RequestRecordCountIn();
        // A timeline compiled mid-song drops every bar before its start, so its own seconds run behind the song clock by the
        // dropped span (the scheduler second is the timeline second; the engine song position is already the song second). Takes are placed on the song's
        // performed clock instead, so a take started mid-song converts the timeline second to the song second; the performed order
        // is captured once here (an edit mid-take keeps the press-time map, as the engine does until its next sync).
        var performed = SongExtent.Timing(song.Project).Performed;
        var loopRange = punch is { } onePass ? (onePass.LoopStart, onePass.LoopEnd) : loop;
        // Scheduler and engine seconds in, song seconds out (the clock maps timeline milliseconds).
        // A stopped record on a later pass of a repeat maps with that pass, as the take second does.
        double? passHint = !wasPlaying && position is { Pass: > 1 } ? TakeStartSec(song.Project, position, start.Bar, start.Cell) : null;
        Func<double, double> toSong = schedulerSec => TimelineSongClock.SongSecAt(performed, session.Transport.Timeline, schedulerSec * 1000.0, loopRange?.Start ?? passHint);
        var takeSec = punch is { } startAtPass
            ? startAtPass.LoopStart
            : wasPlaying
                ? toSong(session.SchedulerSec)
                : TakeStartSec(song.Project, position, start.Bar, start.Cell);
        var context = new CaptureStart(recording, session, song, showStatus, start, hooks, loop, playback, punch, capture, captureTracks, wasPlaying, wasPaused, toSong, takeSec, loopRange, performed);
        // From a stop with the count-in on, the start's count-in is a pre-roll: the take begins, and capture starts, only when the
        // scheduler has finished it at the take's first second. Capturing during it would write the clicks into the take's file (its
        // sample 0 would be the pre-roll) and offset it from playback by the count-in. A take that already plays (punch-in) and a start
        // without a count-in keep starting at once, as before.
        if (!wasPlaying && session.CountIn)
        {
            session.StartAfterCountIn(context.Begin);
            return;
        }
        context.Begin();
    }

    /// <summary>The second press of Record: ends the capture, lands the takes as one undo step, stops the playback and leaves the cursor and the transport at the playhead.</summary>
    private static void StopAndLand(TransportRecording recording, AvaloniaPlaybackSession session, SongDocument song, Action<string> showStatus, RecordStart start, EngineHooks? hooks, PassPunch? punch)
    {
        var tracks = song.Project.Tracks;
        // A transport Stop calls Toggle without the punch: the recording's own target is the one it must replace.
        var activePunch = punch ?? hooks?.RecordingPunch;
        hooks?.EndPunchAutoStop();
        var landingBase = song.Undo.Snapshot(song.Project, activePunch?.UndoLabel ?? "Record takes");
        hooks?.BeginLanding(landingBase, activePunch);
        var stopStatus = recording.Toggle(tracks, MediaFolders.ForSong(song.Path));
        showStatus(stopStatus);
        LandMidiTakes(recording, song, landingBase, activePunch, hooks?.MidiOptions?.Invoke());
        if (activePunch is not null && !AudioInputs.HasAudio(activePunch.Track.AudioInput)) hooks?.EndLandingPunch(activePunch);   // its file never arrives
        hooks?.ClipLanded?.Invoke(song);   // the landed MIDI clips are a clip edit: compact and sync as any clip edit does
        hooks?.RowsReload?.Invoke(song);   // always show the landed MIDI lanes/stacks
        // The drawn playhead at the stop is the take's end: keep it, so the cursor and the next Play continue from there.
        var drawn = session.Playhead;
        var stopBar = drawn.Bar >= 0 ? drawn.Bar : start.Bar;
        var stopCell = drawn.Bar >= 0 ? drawn.Cell : start.Cell;
        session.Stop();   // Stop ends the recorded song's playback too
        (song.CursorBar, song.CursorCell, song.CursorString) = (stopBar, stopCell, start.String);
        session.Seek(stopBar, stopCell);
        hooks?.EndLive();
        return;
    }

    /// <summary>Where a record starts and whether the song already plays or is paused. A pass re-record stops a playing transport (it starts from the loop start); the start bar and cell come from the playhead, the seek position, the cursor, the pass-aware position or the punch range.</summary>
    private static (bool WasPlaying, bool WasPaused) ResolveStart(AvaloniaPlaybackSession session, SongDocument song, RecordStart start, PassPunch? punch, RepeatPosition? position)
    {
        // Recording while the song already plays starts at the playhead, not at the cursor left behind by an earlier click: the take lines
        // up with what was heard, and Stop leaves the cursor and transport at the playhead where it stopped. The scheduler's second anchors it; the drawn playhead
        // runs up to a frame ahead. A recording this call starts the playback for begins where the song was last navigated to (every bar, section
        // and cursor click seeks the playback; the document's cursor is only written at a tab switch), else at the cursor, where that playback begins.
        var wasPlaying = session.IsPlaying;
        // A pass re-record always starts from the loop start: a playing transport is stopped and seeked there first (the normal
        // start path below then seeks and rolls, and the normal count-in gating applies unchanged).
        if (punch is not null && wasPlaying)
        {
            session.Stop();
            wasPlaying = false;
        }
        var wasPaused = wasPlaying && session.IsPaused;
        (start.Bar, start.Cell, start.String) = wasPlaying
            ? (Math.Max(0, session.Transport.PlayheadBar), Math.Max(0, session.Transport.PlayheadCell), song.CursorString)
            : session.Transport.SeekPosition is { } seek
                ? (seek.Bar, seek.Cell, song.CursorString)
                : (song.CursorBar, song.CursorCell, song.CursorString);
        // A record started from the playhead over a repeat takes its written bar and cell from the pass-aware position; the transport
        // is seeked through the same position below, so both start on the same performed second (RV13).
        if (!wasPlaying && position is { } stoppedAt)
        {
            start.Bar = Math.Max(0, stoppedAt.WrittenBar);
            var slots = Math.Max(1, MusicTime.BarSlots(song.Project, start.Bar));
            start.Cell = Math.Clamp((int)Math.Round(stoppedAt.Fraction * slots), 0, slots);
            start.String = song.CursorString;
        }
        if (punch is { } passRange)
        {
            // The punch range is in song seconds: seek to the bar and cell that hold its start (a loop usually starts on a bar line).
            var (bar, fraction) = AudioClipTimelineRules.BarAtSeconds(song.Project, passRange.LoopStart);
            var slots = Math.Max(1, MusicTime.BarSlots(song.Project, Math.Max(0, bar)));
            start.Bar = Math.Max(0, bar);
            start.Cell = Math.Clamp((int)Math.Round(fraction * slots), 0, slots);
            start.String = song.CursorString;
        }
        return (wasPlaying, wasPaused);
    }

    /// <summary>Everything the start of a capture needs, so it can run at once or when the count-in has ended.</summary>
    private sealed record CaptureStart(TransportRecording Recording, AvaloniaPlaybackSession Session, SongDocument Song, Action<string> ShowStatus, RecordStart Start, EngineHooks? Hooks,
        (double Start, double End)? Loop, AvaloniaPlaybackSessions? Playback, PassPunch? Punch, IReadOnlyList<TrackModel>? Capture, IReadOnlyList<TrackModel> CaptureTracks,
        bool WasPlaying, bool WasPaused, Func<double, double> ToSong, double TakeSec, (double Start, double End)? LoopRange, PerformedBar[] Performed)
    {
        public void Begin()
        {
            var (recording, session, song, showStatus, start, hooks, loop, playback, punch, capture, captureTracks, wasPlaying, wasPaused, toSong, takeSec, loopRange, performed) = this;
        if (hooks?.IsReleased == true) return;   // the window closed while the count-in played
        playback?.SetPosition(song, wasPlaying ? toSong(session.SchedulerSec) : takeSec);   // one-shot: the engine's song clock has no other source, so its audio take would anchor at 0
        // The MIDI capture follows the live song second from here, so notes land where the song is when heard, not where Record was pressed.
        showStatus(recording.Toggle(captureTracks, MediaFolders.ForSong(song.Path), takeSec, session.Speed, loopRange,
            _ => toSong(session.SchedulerSec), capture));
        if (recording.IsRecording)
        {
            // The live overlay folds a transport loop one lane per lap; through score repeats it stacks each play inside
            // the take's lane instead (the one owner, RepeatPasses, maps the plays; a transport loop wins over repeats).
            hooks?.BeginLive(session, captureTracks, toSong, wasPlaying, loopRange, punch,
                repeatPerformed: loopRange is null ? performed : null);
            if (punch is { } pass)
                hooks?.BeginPunchAutoStop(session, pass, toSong,
                    () => Toggle(recording, session, song, showStatus, start, hooks, loop, playback, pass));
            return;
        }
        hooks?.ClearLive();
        if (!wasPlaying)
        {
            // The capture was refused after playback started: put the transport back where Record was pressed.
            session.Stop();
            session.Seek(start.Bar, start.Cell);
        }
        else if (wasPaused) session.PlayOrPause();   // refused while paused: pause again, as Record found the transport
        }
    }

    /// <summary>
    /// The performed (song) second a take starts at: the playhead's pass-aware position when one was given (the single source,
    /// <see cref="RepeatMap.ToPerformed"/>), else the old bar+cell first-occurrence second. Used by the start and its take alike.
    /// </summary>
    internal static double TakeStartSec(SongProject project, RepeatPosition? position, int bar, int cell)
    {
        // Pass 1 (or no repeat position) keeps the old bar+cell seconds and never touches the repeat map.
        if (position is { Pass: > 1 } at)
        {
            var sec = RepeatMap.ToPerformed(SongExtent.Timing(project).Performed, at);
            if (!double.IsNaN(sec)) return sec;
        }
        var slots = Math.Max(1, MusicTime.BarSlots(project, Math.Max(0, bar)));
        return AudioClipTimelineRules.SecondsAtBar(project, Math.Max(0, bar), Math.Clamp(cell, 0, slots) / (double)slots);
    }

    /// <summary>
    /// Starts the one-pass re-record of <paramref name="clip"/>'s pass as one additive hook on the normal Record path: the loop is
    /// the group's whole span, only <paramref name="track"/> is captured, and the normal start/count-in gating runs unchanged. False
    /// when a recording already runs, the clip is not a pass, or the pass has no usable loop span.
    /// </summary>
    public static bool StartPunch(TransportRecording recording, AvaloniaPlaybackSession session, SongDocument song, TrackModel track, AudioClip clip,
        Action<string> showStatus, EngineHooks? hooks = null, AvaloniaPlaybackSessions? playback = null)
    {
        if (recording.IsRecording || !TakeGroups.IsGrouped(clip) || clip.TakeGroupId is not { } groupId) return false;
        var members = TakeGroups.Ordered(TakeGroups.Members(track, clip));
        var start = members.Min(member => member.StartSec);
        var end = members.Max(member => member.EndSec);
        if (end - start < RecordedTakes.MinimumLengthSec) return false;
        var punch = new PassPunch(track, groupId, clip.TakeGroupPass, start, end);
        Toggle(recording, session, song, showStatus, hooks?.Start ?? new RecordStart(), hooks, playback: playback, punch: punch);
        return true;
    }

    /// <summary>
    /// Lands the MIDI takes the last recording stop finished: each on its track's first free lane (a new lane when the track is busy there), the
    /// newest playing, as one undo step in the song that owns the tracks. <paramref name="before"/> is the state the recording stop began from,
    /// so every audio take that lands later joins the same step.  A recording through score repeats lands as one ordinary clip (the repeat view only folds its drawing).
    /// </summary>
    public static void LandMidiTakes(TransportRecording recording, SongDocument song, UndoSnapshot? before = null, PassPunch? punch = null, MidiRecordOptions? options = null)
    {
        var finished = recording.TakeFinishedMidi();
        if (punch is not null)
        {
            // A MIDI pass replaces its notes even when none were played (the pass becomes empty); an audio pass lands through Recorded.
            if (AudioInputs.HasAudio(punch.Track.AudioInput)) return;
            var takes = finished.Where(item => ReferenceEquals(item.Track, punch.Track)).SelectMany(item => item.Clips).ToList();
            DocumentEdits.Run(song, _ => RecordedTakes.ReplaceMidiPass(punch.Track, punch.GroupId, punch.Pass, takes), before, label: punch.UndoLabel);
            return;
        }
        if (finished.Count == 0) return;
        var count = finished.Sum(item => item.Clips.Count);
        DocumentEdits.Run(song, _ =>
        {
            var landing = options ?? new MidiRecordOptions();
            foreach (var (track, clips) in finished)
            {
                if (landing.QuantizeGrid is { Length: > 0 } grid) MidiTakeQuantize.Quantize(song.Project, clips, grid);   // input quantize first, then land
                MidiTakeLanding.Land(song.Project, track, clips, landing);
            }
            return true;
        }, before, label: count > 1 ? $"Record {count} takes" : "Record take");
    }

    /// <summary>
    /// One finished engine recording: lands its take on <paramref name="track"/> in the open song that owns it (one undo step) and returns the status
    /// text; a too-short take's file is deleted. A <paramref name="loop"/> range cuts the recording into one take per lap. <paramref name="before"/>
    /// is the state the stop began from, so takes of several tracks and files all land as one undo step. Null when nothing landed
    /// (too short, or the song was closed: the file stays in its media folder).
    /// </summary>
    public static string? LandTake(System.Collections.Generic.IEnumerable<SongDocument> songs, TabForge.Models.TrackModel track, string file, double startSec, double lengthSec,
        (double Start, double End)? loop = null, UndoSnapshot? before = null, PassPunch? punch = null)
    {
        if (lengthSec < RecordedTakes.MinimumLengthSec)
        {
            try { File.Delete(file); } catch (IOException) { } // swallow: best-effort removal of a discarded take.
            return null;
        }
        if (songs.FirstOrDefault(s => s.Project.Tracks.Contains(track)) is not { } owner)
        {
            // Never silently drop a take: its file stays in the media folder and the trace names it.
            TabForge.Services.Trace.Error(TabForge.Services.Trace.Ui, $"recorded take not landed: its track is in no open song ({file})");
            return null;
        }
        var takes = RecordedTakes.FromRecording(file, startSec, lengthSec, loop);
        if (punch is not null && ReferenceEquals(punch.Track, track))
        {
            // One loop length was recorded: its first lap replaces the target pass; a stray second partial take is ignored.
            var take = takes.FirstOrDefault();
            if (take is null) return null;
            var replaced = DocumentEdits.Run(owner, _ => RecordedTakes.ReplacePass(punch.Track, punch.GroupId, punch.Pass, take, midi: false),
                before, label: punch.UndoLabel).Changed;
            return replaced ? $"{punch.UndoLabel} on {track.Name}" : null;
        }
        var count = takes.Count;
        DocumentEdits.Run(owner, _ => { RecordedTakes.Place(track, takes, midi: false); return true; }, before,
            label: count > 1 ? $"Record {count} takes" : "Record take");
        // The status counts loop laps as takes.
        return RecordedTakes.StatusText(track, count, lengthSec);
    }
}
