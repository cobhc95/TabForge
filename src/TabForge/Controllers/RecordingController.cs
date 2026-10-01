using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Views;

namespace TabForge.Controllers;

/// <summary>What the recording controller needs from its window (MainWindow implements it; A-01).</summary>
internal interface IRecordingHost
{
    /// <summary>The current tab's song (read on every use: switching tabs swaps it).</summary>
    SongProject Project { get; }
    /// <summary>The current tab's MIDI playback engine.</summary>
    PlaybackEngine Playback { get; }
    SongClock SongClock { get; }
    ArrangementPanel Arrangement { get; }
    Dispatcher Dispatcher { get; }
    /// <summary>The song file's path, if saved.</summary>
    string? CurrentPath { get; }
    bool LoopOn { get; }
    int LoopStartCell { get; }
    (int start, int end) GetLoopRange();
    (int Measure, int Cell, int String) Cursor { get; }
    /// <summary>Puts the editor cursor back and scrolls it into view.</summary>
    void RestoreCursor(int measure, int cell, int stringIndex);
    void SetStatus(string text);
    /// <summary>A message the user must read in full (the status bar is too narrow): a dialog.</summary>
    void ShowNotice(string text);
    void CaptureUndo();
    void SyncAudioEngine();
    void RefreshTracks();
    void RefreshArrangement();
    void UpdateTitle();
    void StartPlayback();
    void StopPlayback();
    void ClipsChanged(bool refreshRows);
    void SetRecordIcon(bool recording);
}

/// <summary>
/// Record-arm (live monitoring through the track), the input level meters, and recording armed tracks
/// (behaviour): audio is captured by the audio engine, MIDI by the editor; the take is drawn live as it comes in; it
/// lands on the first lane with room (a new lane when all are busy); with a loop, every pass becomes a take on its own
/// lane and the newest one plays (older takes grey out).
/// Per frame work while armed: a few meter values; while recording: the overlay layer only.
/// </summary>
internal sealed class RecordingController
{
    private readonly IRecordingHost _host;
    private bool _recording;
    private bool _recordingHooked;
    private bool _recordStartedPlayback;
    private int _recordStartMeasure, _recordStartCell, _recordStartString;
    private bool _frameHooked;
    private readonly MidiInputCapture _midiInput = new();
    /// <summary>Loop range in song seconds while recording with the loop on (every pass is a take).</summary>
    private (double Start, double End)? _recordLoop;
    /// <summary>How many times the loop has wrapped since recording began (each lap is a take).</summary>
    private int _recordLaps;
    private double _lastFrameSec = double.NaN;
    private readonly Dictionary<TrackModel, MidiTake> _midiTakes = new();
    private double _midiLevel;
    private long _lastMidiStamp;
    private TimeSpan _lastFrame;

    public RecordingController(IRecordingHost host) => _host = host;

    public bool IsRecording => _recording;

    /// <summary>Loop range in song seconds of the recording in progress (null: no loop, or not recording with one).</summary>
    public (double Start, double End)? RecordLoop => _recordLoop;
    /// <summary>Loop wraps seen since recording began (diagnostics / the recording probe).</summary>
    public int RecordLaps => _recordLaps;

    private SongProject Project => _host.Project;
    private ArrangementPanel Arrangement => _host.Arrangement;

    /// <summary>MIDI coming in for one armed track during a recording: its passes (takes) and held notes.</summary>
    private sealed class MidiTake
    {
        public readonly List<(double Start, double End, List<ClipNote> Notes)> Passes = new();
        public readonly Dictionary<int, (double Start, int Velocity)> Held = new();
        public double LastSec = double.NaN;
    }

    /// <summary>The red button on a track row: monitor the track's input (audio or MIDI) and record it.</summary>
    public void ToggleArm(TrackModel track)
    {
        HookRecording();
        _host.CaptureUndo();
        track.RecordArm = !track.RecordArm;
        if (!track.RecordArm) ClipLanes.Trim(track);
        Project.IsDirty = true;
        ArmChanged();
        _host.SetStatus(track.RecordArm
            ? $"{track.Name}: armed — {(AudioInputs.IsMidi(track.AudioInput) ? "MIDI input plays through the track" : "the audio input is monitored through the track")} (press Record or Ctrl+R to record)"
            : $"{track.Name}: disarmed");
    }

    /// <summary>Arm or input changed: engine monitoring, MIDI input, meters, rows.</summary>
    public void ArmChanged()
    {
        _host.SyncAudioEngine();
        UpdateMidiInput();
        UpdateFrameHook();
        _host.RefreshTracks();
        _host.RefreshArrangement();
        _host.UpdateTitle();
    }

    /// <summary>Transport Record (Ctrl+R): starts playback if needed and records every armed track; again to stop.</summary>
    public void ToggleRecording()
    {
        HookRecording();
        var engine = AudioEngineClient.Instance;
        if (_recording) { StopRecording(); return; }
        var armed = Project.Tracks.Where(t => t.RecordArm).ToList();
        if (armed.Count == 0)
        {
            _host.SetStatus("Arm a track first (the red button on its row), then press Record");
            return;
        }
        _host.SyncAudioEngine();
        (_recordStartMeasure, _recordStartCell, _recordStartString) = _host.Cursor;
        _recordStartedPlayback = !_host.Playback.IsPlaying;
        if (_recordStartedPlayback) _host.StartPlayback();
        var audioArmed = armed.Any(t => !AudioInputs.IsMidi(t.AudioInput));
        if (audioArmed && !engine.StartRecording(Project.Tracks, MediaFolder()))
        {
            _host.SetStatus("Recording could not start: the audio engine is not running (check Settings > Audio & Plug-ins)");
            if (!armed.Any(t => AudioInputs.IsMidi(t.AudioInput))) return;
        }
        _recordLoop = _host.LoopOn ? LoopSeconds() : null;
        _recordLaps = 0;
        _lastFrameSec = double.NaN;
        _midiTakes.Clear();
        Arrangement.LiveTakes.Clear();
        foreach (var track in armed) if (AudioInputs.IsMidi(track.AudioInput)) _midiTakes[track] = new MidiTake();
        _recording = true;
        _host.SetRecordIcon(true);
        UpdateFrameHook();
        _host.SetStatus("Recording… press Record (Ctrl+R) or Stop to finish");
    }

    private void StopRecording()
    {
        _recording = false;
        AudioEngineClient.Instance.StopRecording();   // audio takes arrive through Recorded
        _host.SetRecordIcon(false);
        FinishMidiTakes();
        // Audio live takes stay drawn until their files arrive; MIDI ones are replaced now.
        Arrangement.LiveTakes.RemoveAll(t => t.Midi);
        Arrangement.RefreshLiveTakes();
        UpdateFrameHook();
        // Stop ends playback too and returns the cursor to where recording began, so Space plays the take from its start.
        _host.StopPlayback();
        _host.RestoreCursor(_recordStartMeasure, _recordStartCell, _recordStartString);
        _host.SetStatus("Recording stopped");
    }

    /// <summary>The loop in song seconds (first performance of its bars).</summary>
    private (double, double) LoopSeconds()
    {
        var project = Project;
        var clock = _host.SongClock;
        var (start, end) = _host.GetLoopRange();
        var startSec = clock.BarStartSec(project, start);
        var endSec = clock.BarEndSec(project, end);
        var slots = Math.Max(1, Services.MusicTime.BarSlots(project, start));
        var loopStartCell = _host.LoopStartCell;
        if (loopStartCell > 0) startSec += (clock.BarEndSec(project, start) - clock.BarStartSec(project, start)) * loopStartCell / slots;
        return (startSec, Math.Max(startSec + 0.05, endSec));
    }

    /// <summary>Where recordings go: "&lt;song&gt; Media" beside the saved song, else Music\TabForge Recordings.</summary>
    private string MediaFolder() => Services.MediaFolders.ForSong(_host.CurrentPath);

    private void HookRecording()
    {
        if (_recordingHooked) return;
        _recordingHooked = true;
        var engine = AudioEngineClient.Instance;
        engine.Recorded += (track, file, startSec, lengthSec) =>
        {
            Arrangement.LiveTakes.RemoveAll(t => !t.Midi && ReferenceEquals(t.Track, track));
            Arrangement.RefreshLiveTakes();
            if (lengthSec < 0.05) { try { File.Delete(file); } catch (IOException) { } return; }
            if (!Project.Tracks.Contains(track)) return;
            _host.CaptureUndo();
            var name = Path.GetFileNameWithoutExtension(file);
            var passes = Services.RecordingPasses.Split(startSec, lengthSec, _recordLoop);
            var takes = passes.Select((pass, i) => new AudioClip
            {
                File = file, Name = passes.Count > 1 ? $"{name} take {i + 1}" : name, StartSec = pass.SongStart,
                OffsetSec = pass.FileOffset, SourceLengthSec = pass.Length, FileLengthSec = lengthSec,
            }).ToList();
            PlaceTakes(track, takes, midi: false);
            _host.ClipsChanged(true);
            _host.SetStatus(takes.Count > 1 ? $"Recorded {takes.Count} takes on {track.Name} (the newest plays)" : $"Recorded {lengthSec:0.0} s on {track.Name}");
        };
        engine.InputError += message => _host.SetStatus($"Audio input: {message}");
        engine.RecordingLoss += message => { _host.SetStatus("Recording lost input"); _host.ShowNotice(message); };
        _midiInput.Message += OnMidiInput;
    }

    /// <summary>Takes of one recording on one track: each on the first lane with room, the last one plays.</summary>
    private static void PlaceTakes(TrackModel track, List<AudioClip> takes, bool midi)
    {
        var newLane = false;
        foreach (var take in takes)
        {
            take.Lane = ClipLanes.FreeLane(track, take.StartSec, take.EndSec);
            newLane |= take.Lane > 0 || track.AudioClips.Any(c => c.IsMidi == midi && c.Overlaps(take.StartSec, take.EndSec));
            ClipLanes.Ensure(track, take.Lane + 1);
            track.AudioClips.Add(take);
        }
        if (takes.Count > 0 && newLane) ClipLanes.PlayNewTake(track, takes[^1].Lane, midi);
    }

    // ---------- MIDI input ----------
    private void UpdateMidiInput()
    {
        var project = Project;
        var wanted = project.Tracks.Any(t => t.RecordArm && AudioInputs.IsMidi(t.AudioInput));
        if (wanted && !_midiInput.IsOpen)
        {
            var opened = _midiInput.Open();
            if (opened == 0) _host.SetStatus("No MIDI input device found (or it is in use by another program)");
            // Each armed MIDI track plays with its own sound: set its program now (playback may not be running).
            var channels = ChannelAllocator.Assign(project);
            for (var i = 0; i < project.Tracks.Count; i++)
                if (project.Tracks[i].RecordArm && AudioInputs.IsMidi(project.Tracks[i].AudioInput))
                    _host.Playback.SendLive(project.Tracks[i].MidiOutputDeviceId, 0xC0 | (channels[i] & 0x0F), project.Tracks[i].MidiProgram, 0);
        }
        else if (!wanted && _midiInput.IsOpen) _midiInput.Close();
    }

    /// <summary>MIDI driver thread: play the message through every MIDI-armed track and, when recording, keep it.</summary>
    public void OnMidiInput(int status, int data1, int data2, long stamp)
    {
        _host.Dispatcher.BeginInvoke(() =>
        {
            var project = Project;
            var type = status & 0xF0;
            var channels = ChannelAllocator.Assign(project);
            // What the player heard at that moment (Windows MIDI is held back by the engine latency).
            var heardSec = _host.SongClock.SecAt(stamp) - HeardDelaySec();
            for (var i = 0; i < project.Tracks.Count; i++)
            {
                var track = project.Tracks[i];
                if (!track.RecordArm || !AudioInputs.IsMidi(track.AudioInput)) continue;
                if (track.MonitorInput) _host.Playback.SendLive(track.MidiOutputDeviceId, type | (channels[i] & 0x0F), Math.Clamp(data1 + (type is 0x80 or 0x90 ? track.Transpose : 0), 0, 127), data2);
                if (_recording && _midiTakes.TryGetValue(track, out var take) && !double.IsNaN(heardSec)) Keep(take, type, data1, data2, heardSec);
            }
            if (type == 0x90 && data2 > 0) { _midiLevel = Math.Max(_midiLevel, data2 / 127.0); _lastMidiStamp = stamp; }
        });
    }

    private static double HeardDelaySec()
    {
        var engine = AudioEngineClient.Instance;
        return engine.IsRunning ? engine.LatencyTicks / (double)Stopwatch.Frequency : 0;
    }

    /// <summary>Adds one message to a MIDI take, into the pass (loop lap) the song is in now.</summary>
    private void Keep(MidiTake take, int type, int pitch, int velocity, double sec)
    {
        var lap = _recordLoop is null ? 0 : _recordLaps;
        while (take.Passes.Count <= lap)
        {
            if (take.Passes.Count > 0)
            {
                // The previous lap ends at the loop end; notes still held end there.
                CloseHeld(take, _recordLoop?.End ?? take.LastSec);
                var previous = take.Passes[^1];
                take.Passes[^1] = (previous.Start, _recordLoop?.End ?? previous.End, previous.Notes);
            }
            var start = take.Passes.Count == 0 ? RecordStartSec(sec) : _recordLoop?.Start ?? sec;
            take.Passes.Add((start, start, new List<ClipNote>()));
        }
        take.LastSec = sec;
        var pass = take.Passes[lap];
        if (type == 0x90 && velocity > 0) take.Held[pitch] = (sec, velocity);
        else if (type is 0x80 or 0x90 && take.Held.Remove(pitch, out var held))
            pass.Notes.Add(new ClipNote(held.Start - pass.Start, Math.Max(0.01, sec - held.Start), pitch, held.Velocity));
        take.Passes[lap] = (pass.Start, Math.Max(pass.End, sec), pass.Notes);
    }

    /// <summary>The song time recording began (the first note may come later).</summary>
    private double RecordStartSec(double firstSec) =>
        Arrangement.LiveTakes.FirstOrDefault(t => t.Midi) is { } live ? Math.Min(live.StartSec, firstSec) : firstSec;

    private static void CloseHeld(MidiTake take, double at)
    {
        if (take.Passes.Count == 0) { take.Held.Clear(); return; }
        var pass = take.Passes[^1];
        foreach (var (pitch, held) in take.Held)
            pass.Notes.Add(new ClipNote(held.Start - pass.Start, Math.Max(0.01, at - held.Start), pitch, held.Velocity));
        take.Held.Clear();
    }

    /// <summary>Recording stopped: every MIDI pass with notes becomes a MIDI clip.</summary>
    private void FinishMidiTakes()
    {
        var endSec = _host.SongClock.SecAt(Stopwatch.GetTimestamp()) - HeardDelaySec();
        var any = false;
        foreach (var (track, take) in _midiTakes)
        {
            if (take.Passes.Count == 0) continue;
            CloseHeld(take, double.IsNaN(endSec) ? take.LastSec : endSec);
            var last = take.Passes[^1];
            take.Passes[^1] = (last.Start, Math.Max(last.End, double.IsNaN(endSec) ? last.End : endSec), last.Notes);
            var clips = take.Passes.Where(p => p.Notes.Count > 0).Select((p, i) => new AudioClip
            {
                Name = take.Passes.Count > 1 ? $"{track.Name} MIDI take {i + 1}" : $"{track.Name} MIDI",
                StartSec = Math.Max(0, p.Start), SourceLengthSec = Math.Max(0.1, p.End - p.Start),
                Notes = p.Notes.OrderBy(n => n.StartSec).ToList(),
            }).ToList();
            if (clips.Count == 0) continue;
            if (!any) _host.CaptureUndo();
            any = true;
            PlaceTakes(track, clips, midi: true);
        }
        _midiTakes.Clear();
        if (any) _host.ClipsChanged(true);
    }

    // ---------- per-frame: meters and the live takes ----------
    private void UpdateFrameHook()
    {
        var wanted = Project.Tracks.Any(t => t.RecordArm) || _recording;
        if (wanted == _frameHooked) return;
        _frameHooked = wanted;
        if (wanted) CompositionTarget.Rendering += OnRecordFrame;
        else CompositionTarget.Rendering -= OnRecordFrame;
    }

    /// <summary>Once per display frame while a track is armed: meter values; while recording: grow the live takes.</summary>
    private void OnRecordFrame(object? sender, EventArgs e)
    {
        if (e is RenderingEventArgs r) { if (r.RenderingTime == _lastFrame) return; _lastFrame = r.RenderingTime; }
        var engine = AudioEngineClient.Instance;
        var midiRecent = (Stopwatch.GetTimestamp() - _lastMidiStamp) < Stopwatch.Frequency / 8;
        _midiLevel *= midiRecent ? 1 : 0.85;
        var nowSec = _recording ? _host.SongClock.SecAt(Stopwatch.GetTimestamp()) - HeardDelaySec() : double.NaN;
        if (_recording && !double.IsNaN(nowSec))
        {
            if (!double.IsNaN(_lastFrameSec) && nowSec < _lastFrameSec - 0.25) _recordLaps++;   // the loop wrapped
            _lastFrameSec = nowSec;
        }
        foreach (var track in Project.Tracks)
        {
            if (!track.RecordArm) continue;
            var midi = AudioInputs.IsMidi(track.AudioInput);
            var peak = midi ? _midiLevel : engine.InputPeakOf(track);
            Arrangement.ShowInputLevel(track, peak, midi);
            if (_recording && !double.IsNaN(nowSec)) GrowLiveTake(track, midi, (float)peak, nowSec);
        }
        if (_recording) Arrangement.RefreshLiveTakes();
    }

    private void GrowLiveTake(TrackModel track, bool midi, float peak, double nowSec)
    {
        var live = Arrangement.LiveTakes.LastOrDefault(t => ReferenceEquals(t.Track, track));
        // A loop wrap (song time jumped back) starts the next take below.
        if (live is not null && nowSec < live.EndSec - 0.25) live = null;
        if (live is null)
        {
            var start = Arrangement.LiveTakes.Any(t => ReferenceEquals(t.Track, track)) && _recordLoop is { } loop ? loop.Start : nowSec;
            live = new LiveTake { Track = track, StartSec = start, EndSec = nowSec, Midi = midi };
            Arrangement.LiveTakes.Add(live);
        }
        live.EndSec = nowSec;
        var lap = _recordLoop is null ? 0 : _recordLaps;
        if (midi && _midiTakes.TryGetValue(track, out var take) && take.Passes.Count > lap)
        {
            var pass = take.Passes[lap];
            live.Notes.Clear();
            var offset = pass.Start - live.StartSec;
            foreach (var n in pass.Notes) live.Notes.Add(n with { StartSec = n.StartSec + offset });
            foreach (var (pitch, held) in take.Held) live.Notes.Add(new ClipNote(held.Start - live.StartSec, Math.Max(0.01, nowSec - held.Start), pitch, held.Velocity));
        }
        else if (!midi) live.Peaks.Add(peak);
        // Lane intelligence: the take sits on the first lane free over its whole span so far (older live takes count).
        var others = Arrangement.LiveTakes.Where(t => ReferenceEquals(t.Track, track) && !ReferenceEquals(t, live)).ToList();
        var lane = 0;
        while (track.AudioClips.Any(c => c.Lane == lane && c.Overlaps(live.StartSec, live.EndSec))
               || others.Any(t => t.Lane == lane && t.StartSec < live.EndSec && t.EndSec > live.StartSec)) lane++;
        if (lane != live.Lane || lane >= ClipLanes.Count(track))
        {
            live.Lane = lane;
            if (lane >= ClipLanes.Count(track)) { ClipLanes.Ensure(track, lane + 1); _host.RefreshTracks(); _host.RefreshArrangement(); }
        }
    }
}
