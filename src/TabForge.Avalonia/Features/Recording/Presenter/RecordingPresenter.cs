using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TabForge.Audio;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Features.Recording.Presenter;

// Owns: the Transport.Record command (the Record button and its hotkey): it starts the shown song's playback and records its armed tracks
//   through the engine (TransportRecording in Core) into the song's Media folder (MediaFolders); pressed again it ends the recording and stops the playback, as the WPF app does,
//   and leaves the cursor and transport at the playhead where recording stopped. The same stop is offered to the transport (Stop, Space/PlayPause, Esc) through the returned
//   RecordingSession, and every arrangement's Record button shows the recording state. Closing the window releases its engine hooks and ends a
//   recording in progress. While recording, each write passes the live takes to every arrangement's overlay; each finished take lands on its
//   track's free lane in the open song that owns the track (RecordedTakes in Core); all takes of one stop are one undo step.
// Does not own: the recording logic (TransportRecording), the loop laps (one take per lap at landing; the live box stacks one lane
//   per lap once the playhead wraps), the button (Platform/TransportBarView), the overlay drawing (Platform/TimelineLiveTakeLayer), the take model
//   (LiveTakes), the playback sessions and engine (AvaloniaPlaybackSessions), the commands table (MainWindowCommands).
// Tests: TabForge.Avalonia.Tests.TransportRecordingTests, MidiTakeCaptureTests, CountInRecordingTests, RecordingBatchOneTests, RecordingLiveTakeTests.
internal sealed partial class RecordingPresenter(MainWindowCommands commands, MainWindowDocumentPanes panes, AvaloniaPlaybackSessions? playback,
    MainWindowTabs songs, Action<string> showStatus, Func<PluginSettings> plugins, Func<double, double> meterFraction)
{
    /// <summary>The command id of Transport.Record (the Record button runs it too).</summary>
    public const string CommandId = "Transport.Record";

    /// <summary>The window's recording state: the transport ends a recording through StopRecording, and the arrangement's
    /// Record button reads IsRecording. Disposing it (the window closed) ends a recording in progress.</summary>
    internal sealed class RecordingSession : IDisposable
    {
        private readonly EngineHooks _hooks;
        private readonly Action _stop;
        private readonly Action<SongDocument, TrackModel, AudioClip>? _reRecord;

        internal RecordingSession(EngineHooks hooks, Action stop, Action<SongDocument, TrackModel, AudioClip>? reRecord = null)
        {
            _hooks = hooks;
            _stop = stop;
            _reRecord = reRecord;
        }

        /// <summary>True from a successful start until the recording is stopped (by Record again, the transport, or the window closing).</summary>
        public bool IsRecording => _hooks.IsRecording;

        /// <summary>Ends a recording in progress the same way Record again does (the takes are kept); nothing when not recording. True when one ended.</summary>
        public bool StopRecording()
        {
            if (!IsRecording) return false;
            _stop();
            return true;
        }

        /// <summary>True while <paramref name="track"/> is one of the tracks this recording still captures (not one disarmed mid-take).</summary>
        public bool IsTrackRecording(TrackModel track) => _hooks.IsCapturing(track);

        /// <summary>A record arm/disarm changed: a disarm while recording ends that track's capture at that moment (its lane stays until the stop).</summary>
        public void ArmChanged(SongDocument song) => _hooks.ArmChanged(song);

        /// <summary>
        /// Starts the one-pass re-record of <paramref name="clip"/>'s pass on <paramref name="song"/>: seeks to the group's loop start,
        /// runs the normal count-in when enabled, captures only that track for one loop length and replaces only that pass on landing.
        /// False while another recording runs or when the clip is not a loop pass.
        /// </summary>
        public bool ReRecordPass(SongDocument song, TrackModel track, AudioClip clip)
        {
            if (IsRecording || _reRecord is null || !TakeGroups.IsGrouped(clip)) return false;
            _reRecord(song, track, clip);
            return true;
        }

        public void Dispose() => _hooks.Dispose();
    }

    /// <returns>The window's recording state; disposing it (the window closed) ends a recording in progress and detaches the hooks.</returns>
    internal RecordingSession Register()
    {
        var hooks = new EngineHooks(playback?.Engine, songs, showStatus)
        {
            InputPeakOf = track => playback?.Engine is { } engine ? engine.InputPeakOf(track) : 0,
            LiveChanged = () => panes.Arrangement?.Timeline.RefreshLiveTakes(),
            // A landed take is a clip edit too: compact its empty lanes and sync the engine down the normal path (WPF ClipsChanged).
            ClipLanded = song => panes.Arrangement?.Timeline.ClipEdited?.Invoke(song),
            // The rows must always show a landed take's lanes and pass stacks, even if no clip-refresh hook is attached.
            RowsReload = song =>
            {
                if (panes.Arrangement is not { } arrangement) return;
                arrangement.Timeline.Reload(song.Project);
                arrangement.Header.Reload(song.Project);
            },
            // A lane the growing take needs: only the overlay grows it; lay the rows out once so the take's band is current (never every frame).
            LiveLaneCreated = _ =>
            {
                if (panes.Arrangement is { } arrangement)
                {
                    arrangement.Timeline.Reload(arrangement.Document.Project);
                    arrangement.Header.Reload(arrangement.Document.Project);
                }
            },
        };
        // The clip menu's "Re-record this part" runs a one-part punch.
        void ReRecord(SongDocument song, TrackModel track, AudioClip clip)
        {
            if (playback?.For(song) is not { } session || hooks.Recording is null) return;
            playback.SyncEngine(song, plugins());
            StartPunch(hooks.Recording, session, song, track, clip, showStatus, hooks, playback);
            ShowRecording(panes, hooks.IsRecording);
        }
        if (playback is not null)
        {
            // The dock builds the first arrangement before the window registers this, so that one is attached here too; later ones as they are made.
            void Attach(ArrangementView arrangement)
            {
                arrangement.Input.TrackArmToggled += _ =>
                {
                    playback.SyncEngine(songs.Active, plugins());
                    hooks.ArmChanged(songs.Active);   // a disarm while recording ends that track's capture now
                };
                // The armed sub-rows' monitor and input edits reach the engine, and their meters read the engine's input peaks.
                arrangement.ArmedRows.SettingChanged += () => playback.SyncEngine(songs.Active, plugins());
                arrangement.ArmedRows.InputLevelOf = track => playback.Engine is { } engine ? meterFraction(engine.InputPeakOf(track)) : 0;
                arrangement.Transport.ShowRecording(hooks.IsRecording);
                arrangement.Timeline.UseLiveTakes(hooks.Live);
            }
            if (panes.Arrangement is { } existing) Attach(existing);
            panes.ArrangementCreated += Attach;
        }
        // The transport (Stop, Space/PlayPause, Esc) ends a recording with this, then shows the restored Record button.
        void StopFromTransport()
        {
            if (hooks.Recording is not { IsRecording: true } recording || playback?.For(songs.Active) is not { } session) return;
            Toggle(recording, session, songs.Active, showStatus, hooks.Start, hooks);
            ShowRecording(panes, hooks.IsRecording);
        }
        commands.Add(CommandId, () =>
        {
            if (hooks.Recording is null || playback?.For(songs.Active) is not { } session) return;
            playback.SyncEngine(songs.Active, plugins());
            // A stopped playhead on a later repeat pass records from that performed second; pass 1 leaves the old start path untouched.
            var position = panes.Arrangement?.Timeline is { PlayheadPass: > 1 } timeline
                ? new RepeatPosition(timeline.PlayheadBar, timeline.PlayheadFraction, timeline.PlayheadPass)
                : (RepeatPosition?)null;
            Toggle(hooks.Recording, session, songs.Active, showStatus, hooks.Start, hooks,
                session.Loop ? panes.Selection.LoopSeconds : null, playback, position: position);
            ShowRecording(panes, hooks.IsRecording);
        });
        return new RecordingSession(hooks, StopFromTransport, ReRecord);
    }

    /// <summary>The Record button of the shown arrangement shows whether the window is capturing.</summary>
    internal static void ShowRecording(MainWindowDocumentPanes panes, bool on) => panes.Arrangement?.Transport.ShowRecording(on);

    /// <summary>Where the song's cursor was when recording began; Stop puts it back there, so Play plays the take from its start.</summary>
    // One window's handlers on the shared engine client and its recording; Dispose (the window closed) ends a recording and removes exactly them.
    internal sealed class EngineHooks : IDisposable
    {
        private readonly AudioEngineClient? _engine;
        private readonly Action<TrackModel, string, double, double>? _onRecorded;
        private readonly Action<TrackModel, int, int, int, double> _onLiveNote;
        private readonly Action<string>? _onInputError, _onLoss;
        private AvaloniaPlaybackSession? _liveSession;
        private TrackModel[] _liveTracks = Array.Empty<TrackModel>();
        private Func<double, double>? _liveToSong;
        private Func<double, double>? _recordToSong;
        private Func<double, double>? _landingToSong;   // a stopped recording's mapping, kept until its takes land (they arrive asynchronously)
        private UndoSnapshot? _landingBase;              // the state the stop began from: every take of that stop joins this one undo step
        private (double Start, double End)? _landingLoop; // the stopped recording's loop, kept for its later take files
        private double _landingSpeed = 1;                // the stopped recording's playback speed (the file length times it is song time)
        private bool _landingPending;
        private PassPunch? _recordPunch;                 // the pass this recording replaces (null: a normal take); kept until its stop
        private readonly Dictionary<TrackModel, PassPunch> _landingPunches = new();   // stopped punches awaiting their async take, by target track
        private AvaloniaPlaybackSession? _punchSession;  // the session whose frame at the punch end stops the one-pass take
        private Func<double, double>? _punchToSong;
        private Action? _punchStop;
        private double _punchEndSec;
        private double _punchLastSec = double.NaN;
        private bool _punchFired;
        private bool _liveAnchored;
        private double _liveAnchorSec;
        private double _recordSpeed = 1;                 // the speed the current recording captures at
        private readonly Dictionary<TrackModel, double> _audioCutSec = new();   // a track disarmed mid-recording: its clip ends here
        private readonly HashSet<TrackModel> _disarmed = new();                 // tracks whose capture ended early (a mid-take disarm)
        private readonly HashSet<TrackModel> _explicitCapture = new();          // a pass punch's one track, captured without needing an arm
        private bool _released;

        public EngineHooks(AudioEngineClient? engine, MainWindowTabs songs, Action<string> showStatus)
        {
            _engine = engine;
            _onLiveNote = (track, type, pitch, velocity, sec) => AvaloniaUiThread.ForApplication().Post(() =>
            {
                if (_released || _liveSession is null || !Live.Note(track, type, pitch, velocity, sec)) return;
                LiveChanged?.Invoke();
            });
            if (engine is null) return;
            Recording = new TransportRecording(engine, engine.MidiInput.CreateClient());
            _onRecorded = (track, file, start, length) => AvaloniaUiThread.ForApplication().Post(() =>
            {
                if (_released) return;
                RemoveLiveTake(track);   // the real clip replaces the live box as it lands
                // A take recorded while the song played is stamped on the playing timeline: place it on the song clock.
                // The mapping, loop and speed are those of the recording that produced the take (its files arrive after the stop).
                var toSong = _landingPending ? _landingToSong : _recordToSong;
                var loop = _landingPending ? _landingLoop : Recording?.RecordLoop;
                var speed = _landingPending ? _landingSpeed : _recordSpeed;
                var songStart = toSong?.Invoke(start) ?? start;
                // The engine's length is wall time; song time advances by the session speed. A track disarmed mid-recording is cut there.
                var songLength = Math.Max(0, length) * Math.Max(0.01, speed);
                if (_audioCutSec.Remove(track, out var cut))
                    songLength = cut > songStart ? Math.Min(songLength, cut - songStart) : 0;
                // A one-pass re-record lands against the target it started with, never by "the last recording": the file can arrive
                // after another recording has stopped. Its target is keyed by track and consumed by its one take.
                _landingPunches.Remove(track, out var punch);
                showStatus(LandTake(songs.Songs, track, file, songStart, songLength, loop, _landingPending ? _landingBase : null, punch) ?? "");
                if (songs.Songs.FirstOrDefault(song => song.Project.Tracks.Contains(track)) is { } owner)
                {
                    ClipLanded?.Invoke(owner);
                    RowsReload?.Invoke(owner);   // the new lanes/stacks are visible even when no clip-refresh hook is attached
                }
            });
            _onInputError = message => showStatus($"Audio input: {message}");
            _onLoss = message => showStatus($"Recording: {message}");
            engine.Recorded += _onRecorded;
            engine.InputError += _onInputError;
            engine.RecordingLoss += _onLoss;
        }

        public TransportRecording? Recording { get; private set; }

        /// <summary>True while this window's recording is capturing.</summary>
        public bool IsRecording => Recording?.IsRecording == true;

        /// <summary>True once the window closed and released these hooks: a count-in start still waiting must not capture.</summary>
        public bool IsReleased => _released;

        /// <summary>The live takes of the recording in progress, drawn by the arrangements' overlay layers.</summary>
        public LiveTakes Live { get; } = new();

        /// <summary>Reads an armed track's input peak for the live take's envelope; null: 0.</summary>
        public Func<TrackModel, double>? InputPeakOf { get; init; }

        /// <summary>Redraws the live-take overlay of the shown arrangement (called on a recording frame and when a take stops).</summary>
        public Action? LiveChanged { get; init; }

        /// <summary>A take landed as clips: the window's clip-edit refresh (lane compaction and the engine sync).</summary>
        public Action<SongDocument>? ClipLanded { get; init; }

        /// <summary>Lays the rows out again after a landing; called unconditionally, so a take is never invisible for a missing clip hook.</summary>
        public Action<SongDocument>? RowsReload { get; init; }

        /// <summary>Called once when a growing take needs a lane the track does not have yet.</summary>
        public Action<TrackModel>? LiveLaneCreated { get; init; }

        public RecordStart Start { get; } = new();

        /// <summary>Starts feeding the live takes for a recording that just started: one take per armed track, one frame per playhead frame.</summary>
        public void BeginLive(AvaloniaPlaybackSession session, IReadOnlyList<TrackModel> tracks, Func<double, double>? toSong = null, bool startedWhilePlaying = false,
            (double Start, double End)? loop = null, PassPunch? punch = null, PerformedBar[]? repeatPerformed = null)
        {
            EndLive(stopping: false);
            EndPunchAutoStop();
            _recordPunch = punch;
            _explicitCapture.Clear();
            if (punch is not null) _explicitCapture.Add(punch.Track);
            // A pass punch's one track is captured without a record arm; a normal recording captures the armed tracks only.
            // The punch live box is the punch target only (the capture list at Landing ~69), never the armed tracks.
            _liveTracks = punch is not null ? new[] { punch.Track } : tracks.Where(track => track.RecordArm).ToArray();
            _liveToSong = toSong;
            _liveAnchored = false;   // the first frame with a song second anchors the takes at the recording's start
            _recordSpeed = session.Speed < 0.01 ? 1 : session.Speed;
            _audioCutSec.Clear();
            _disarmed.Clear();
            Live.Loop = loop;        // a looped recording's takes stack one live lane per lap inside this range
            // Through score repeats (no transport loop) each play stacks inside the take's own lane instead; a transport loop wins.
            Live.RepeatPerformed = loop is null ? repeatPerformed : null;
            // The audio take the engine reports is stamped on the playing timeline: it is converted to the song clock when it
            // lands. A recording started from a stop is already on the song clock (the engine holds the anchored start).
            _recordToSong = startedWhilePlaying ? toSong : null;
            Live.Clear();
            Live.LaneCreated = track => LiveLaneCreated?.Invoke(track);
            if (_liveTracks.Length == 0) return;
            if (punch is not null)
                foreach (var track in _liveTracks)   // the target shows a live box from its loop start even before the first frame maps
                    Live.Frame(track, punch.LoopStart, 0f, AudioInputs.IsMidi(track.AudioInput));
            _liveSession = session;
            session.PlayheadFrame += OnLiveFrame;
            if (Recording is not null) Recording.LiveMidiNote += _onLiveNote;
            LiveChanged?.Invoke();
        }

        /// <summary>The recording stopped: every take that stop lands (MIDI now, audio when its files arrive) uses <paramref name="before"/> as its one undo step.</summary>
        public void BeginLanding(UndoSnapshot before, PassPunch? punch = null)
        {
            _landingBase = before;
            _landingToSong = _recordToSong;
            _landingLoop = Recording?.RecordLoop;
            _landingSpeed = _recordSpeed;
            _landingPending = true;
            if (punch is not null) _landingPunches[punch.Track] = punch;
            _recordPunch = null;
        }

        /// <summary>The pass the current recording replaces (null: a normal recording); read when the transport stops it.</summary>
        public PassPunch? RecordingPunch => _recordPunch;

        /// <summary>A MIDI punch landed with the stop: no audio file will arrive for it, so no later take can match it.</summary>
        public void EndLandingPunch(PassPunch punch)
        {
            if (_landingPunches.TryGetValue(punch.Track, out var pending) && ReferenceEquals(pending, punch)) _landingPunches.Remove(punch.Track);
        }

        /// <summary>
        /// Stops a one-pass re-record exactly at its loop end: one playhead frame at or after <paramref name="punch"/>'s end runs
        /// <paramref name="stop"/> once and unsubscribes. A transport Stop before that ends it early through the normal stop path.
        /// </summary>
        public void BeginPunchAutoStop(AvaloniaPlaybackSession session, PassPunch punch, Func<double, double> toSong, Action stop)
        {
            EndPunchAutoStop();
            _punchSession = session;
            _punchToSong = toSong;
            _punchStop = stop;
            _punchEndSec = punch.LoopEnd;
            _punchLastSec = double.NaN;
            session.PlayheadFrame += OnPunchFrame;
        }

        /// <summary>Ends the punch auto-stop monitor; safe to call when none runs (a stop, a new recording, the window closing).</summary>
        public void EndPunchAutoStop()
        {
            if (_punchSession is not null) _punchSession.PlayheadFrame -= OnPunchFrame;
            _punchSession = null;
            _punchToSong = null;
            _punchStop = null;
            _punchLastSec = double.NaN;
            _punchFired = false;
        }

        private void OnPunchFrame()
        {
            if (_punchFired || _punchSession is null || _punchStop is null) return;
            var sec = _punchToSong?.Invoke(_punchSession.SchedulerSec) ?? _punchSession.SchedulerSec;
            if (double.IsNaN(sec)) return;
            // The loop's own wrap can skip the exact end frame: a frame that wrapped just after the end stops the take too.
            var wrapped = !double.IsNaN(_punchLastSec) && sec + 0.05 < _punchLastSec && _punchLastSec >= _punchEndSec - 0.1;
            var reachedEnd = sec + 1e-6 >= _punchEndSec;
            _punchLastSec = sec;
            if (!reachedEnd && !wrapped) return;
            _punchFired = true;   // one stop only: the frame event may fire again while the stop lands
            var stop = _punchStop;
            EndPunchAutoStop();
            stop();
        }

        /// <summary>Ends the live feed: the MIDI takes' boxes go at once, the audio takes stay until their WAVs arrive.</summary>
        /// <param name="stopping">True (the recording stopped): keep its timeline-to-song mapping for the take files that arrive later. False (a new recording starts): the previous landing mapping stays.</param>
        public void EndLive(bool stopping = true)
        {
            if (_liveSession is not null) _liveSession.PlayheadFrame -= OnLiveFrame;
            if (Recording is not null) Recording.LiveMidiNote -= _onLiveNote;
            _liveSession = null;
            _liveToSong = null;
            // The take files arrive asynchronously after the stop: keep this recording's mapping for their landing, so a recording
            // started meanwhile (or the field being reassigned) cannot convert them.
            if (stopping)
            {
                _landingToSong = _recordToSong;
                _landingLoop = Recording?.RecordLoop;
                _landingSpeed = _recordSpeed;
                _landingPending = true;
            }
            _recordToSong = null;
            _liveAnchored = false;
            _liveTracks = Array.Empty<TrackModel>();
            _explicitCapture.Clear();
            Live.LaneCreated = null;
            if (Live.Stop()) LiveChanged?.Invoke();
        }

        /// <summary>A refused start (or a new recording): nothing is captured, so nothing stays drawn.</summary>
        public void ClearLive()
        {
            if (Live.Clear()) LiveChanged?.Invoke();
        }

        /// <summary>A finished audio take's file arrived: its live box goes before the real clip is placed.</summary>
        public void RemoveLiveTake(TrackModel track)
        {
            if (Live.Remove(track)) LiveChanged?.Invoke();
        }

        /// <summary>True while this recording still captures <paramref name="track"/> (a track disarmed mid-take is no longer captured).</summary>
        public bool IsCapturing(TrackModel track)
        {
            if (Recording?.IsRecording != true || _disarmed.Contains(track)) return false;
            for (var index = 0; index < _liveTracks.Length; index++)
                if (ReferenceEquals(_liveTracks[index], track)) return true;
            return false;
        }

        /// <summary>
        /// A record arm changed on the shown song: a track disarmed while this recording captures ends at that moment. Its live box is
        /// frozen (and keeps its lane) and an audio take is cut on landing; the MIDI capture drops every later message. Nothing when the
        /// track is still armed or was never captured.
        /// </summary>
        public void ArmChanged(SongDocument song)
        {
            _ = song;   // the capture's own tracks decide; the shown song is the same document the arm toggled
            if (Recording?.IsRecording != true) return;
            var changed = false;
            foreach (var track in _liveTracks)
            {
                if (track.RecordArm || _disarmed.Contains(track) || _explicitCapture.Contains(track)) continue;
                var midi = AudioInputs.IsMidi(track.AudioInput);
                var cut = CaptureNowSec();
                if (Live.Of(track) is null && cut is { } at) changed |= Live.Frame(track, at, 0f, midi);   // open the lane the take keeps
                if (Live.Of(track) is { } take)
                {
                    if (!midi) _audioCutSec[track] = take.EndSec;   // the landed clip ends here
                    changed |= Live.Freeze(track);
                }
                Recording.DisarmTrack(track, Stopwatch.GetTimestamp());
                _disarmed.Add(track);
            }
            if (changed) LiveChanged?.Invoke();
        }

        // The current capture point on the song clock, used when a track is disarmed before its first live frame.
        private double? CaptureNowSec()
        {
            if (_liveSession is null) return null;
            var schedulerSec = _liveSession.SchedulerSec;
            if (!double.IsNaN(schedulerSec))
            {
                var songSec = _liveToSong?.Invoke(schedulerSec) ?? schedulerSec;
                if (!double.IsNaN(songSec)) return songSec;
            }
            return Live.Count > 0 ? Live[0].EndSec : null;
        }

        // One display frame while recording: the take grows from the record start to the live scheduler's song second, keeps one peak, and its lane
        // is recomputed. The playhead is read only to anchor the first frame: a later playhead move (a seek) must not freeze or stretch the box.
        private void OnLiveFrame()
        {
            if (_liveSession is null || _liveSession.Transport.StartPending) return;   // a start still compiling has no timeline to map with yet
            if (!_liveAnchored)
            {
                // The first frame's song second is the recording's start (the engine's take is stamped on it too): the same mapping as the MIDI capture.
                var schedSec = _liveSession.SchedulerSec;
                if (double.IsNaN(schedSec)) return;   // the scheduler has no position (stopped): WPF drops such frames too
                var startSec = _liveToSong?.Invoke(schedSec) ?? schedSec;
                if (double.IsNaN(startSec)) return;
                // While a start compiles, the clock has not reported the start second yet: a bar after the first means the 0 is not the take's start.
                if (startSec <= 0 && _liveSession.Playhead.Bar > 0) return;
                _liveAnchorSec = startSec;
                _liveAnchored = true;
            }
            // The live scheduler's song second (the clock the engine's take runs on), never before the record start: the box tracks the song exactly.
            var nowSec = Math.Max(_liveAnchorSec, CaptureNowSec() ?? _liveAnchorSec);
            var changed = false;
            foreach (var track in _liveTracks)
            {
                if (_disarmed.Contains(track)) continue;   // a mid-take disarm froze (or never opened) its box
                var midi = AudioInputs.IsMidi(track.AudioInput);
                var peak = midi ? 0f : (float)Math.Clamp(InputPeakOf?.Invoke(track) ?? 0, 0, 1);
                changed |= Live.Frame(track, nowSec, peak, midi);
            }
            if (changed) LiveChanged?.Invoke();
        }

        public void Dispose()
        {
            if (_released || _engine is null) return;
            _released = true;
            EndPunchAutoStop();
            EndLive();
            _landingPunches.Clear();
            Live.Clear();
            _audioCutSec.Clear();
            if (Recording?.IsRecording == true) _engine.StopRecording();   // the take file is kept in the media folder
            Recording?.Dispose();   // a MIDI take still being captured is dropped
            Recording = null;
            _engine.Recorded -= _onRecorded;
            _engine.InputError -= _onInputError;
            _engine.RecordingLoss -= _onLoss;
        }
    }
}
