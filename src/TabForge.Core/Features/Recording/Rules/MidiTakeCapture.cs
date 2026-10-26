using System.Diagnostics;
using TabForge.Audio;
using TabForge.Models;

namespace TabForge.Features.Recording.Rules;

// Owns: the MIDI takes of the armed MIDI tracks during one Transport Record: it listens on a MIDI input client and keeps every channel
//   message in each armed MIDI track's take (MidiTake) at the song second it was heard (the live scheduler second when one is given to Start,
//   else from the start second and speed given to Start; the engine latency removed), gives the takes back as MIDI clips (one per lap)
//   when the recording stops, and raises the note messages once for the live overlay.
// Does not own: the audio takes (AudioEngineClient, RecordedTakes), placing the clips on lanes and the undo step (the caller), the playback,
//   the MIDI input client (the caller creates it), the live overlay (the Avalonia TimelineLiveTakeLayer). An armed MIDI track with devices ticked hears only those
//   devices (none ticked: every device, as before).
// Threads: Start and Stop run on the UI thread; messages arrive on the MIDI driver thread. Both sides share one lock.
// Tests: TabForge.Avalonia.Tests.MidiTakeCaptureTests.
public sealed class MidiTakeCapture : IDisposable
{
    private readonly IMidiInputClient _input;
    private readonly object _gate = new();
    private readonly List<(TrackModel Track, MidiTake Take, string[] Devices)> _takes = new();   // Devices: the track's ticked MIDI devices when the capture started (empty: all)
    private readonly Dictionary<TrackModel, double> _endedAt = new();   // a track disarmed mid-recording: its take ends at this song second
    private bool _capturing;
    private double _startSec, _speed = 1;
    private long _startStamp, _latencyTicks;
    private (double Start, double End)? _loop;
    private Func<long, double>? _songSecAt;
    private int _laps;
    private double _lastLiveSec = double.NaN;

    public MidiTakeCapture(IMidiInputClient input) => _input = input;

    /// <summary>True from a successful <see cref="Start"/> until <see cref="Stop"/>.</summary>
    public bool IsCapturing { get { lock (_gate) return _capturing; } }

    /// <summary>
    /// A note message heard while capturing, for the live overlay: the track, the MIDI status byte and its two data bytes, and the song second
    /// it was heard at (the same numbers the take keeps). Raised on the MIDI driver thread, once per armed MIDI track; a handler must not block
    /// (the live overlay posts it to the UI thread).
    /// </summary>
    public event Action<TrackModel, int, int, int, double>? LiveNote;

    /// <summary>
    /// Opens the MIDI input and starts one take per armed MIDI track. <paramref name="startSec"/> is the song second the recording began at,
    /// heard at <paramref name="startStamp"/> (a Stopwatch timestamp), <paramref name="speed"/> the playback speed, and <paramref name="loop"/>
    /// the loop range a looped recording wraps within (one take per pass; null: one pass). <paramref name="songSecAt"/> is the live scheduler
    /// song second at a stamp (the Avalonia playback session's scheduler second): given, the notes follow the song as it plays instead of the
    /// press-time second carried forward by the wall clock. Returns how many MIDI devices are open; 0 (no device,
    /// or no armed MIDI track) starts no capture.
    /// </summary>
    public int Start(IReadOnlyList<TrackModel> tracks, double startSec, long startStamp, double speed, long latencyTicks, (double Start, double End)? loop = null,
        Func<long, double>? songSecAt = null, bool requireArm = true)
    {
        if (IsCapturing) throw new InvalidOperationException("A MIDI take is already being captured: stop it first");
        var armed = tracks.Where(t => (requireArm ? t.RecordArm : true) && AudioInputs.TakesMidi(t)).ToList();
        if (armed.Count == 0) return 0;
        var opened = _input.Open();
        if (opened == 0) return 0;
        lock (_gate)
        {
            _takes.Clear();
            _endedAt.Clear();
            foreach (var track in armed) _takes.Add((track, new MidiTake(), track.MidiInputs.ToArray()));
            (_startSec, _startStamp, _speed, _latencyTicks, _loop) = (startSec, startStamp, speed, latencyTicks, loop);
            _songSecAt = songSecAt;
            _laps = 0;
            _lastLiveSec = double.NaN;
            _capturing = true;
        }
        Subscribe();
        return opened;
    }

    /// <summary>
    /// Ends the capture at <paramref name="stopStamp"/>: the MIDI input closes, and each track whose take holds notes gets its clips (one per
    /// pass). Empty when nothing was captured.
    /// </summary>
    public List<(TrackModel Track, List<AudioClip> Clips)> Stop(long stopStamp)
    {
        var finished = new List<(TrackModel Track, List<AudioClip> Clips)>();
        lock (_gate)
        {
            if (!_capturing) return finished;
            _capturing = false;
            var endSec = SongPosAt(stopStamp).Sec;
            foreach (var (track, take, _) in _takes)
            {
                var trackEnd = _endedAt.TryGetValue(track, out var cut) ? cut : endSec;
                var clips = take.Finish(track.Name, trackEnd, looped: _loop is not null);
                if (clips.Count > 0) finished.Add((track, clips));
            }
            _takes.Clear();
            _endedAt.Clear();
        }
        Unsubscribe();
        if (_input.IsOpen) _input.Close();
        return finished;
    }

    /// <summary>
    /// A track disarmed while recording: its MIDI take ends at that moment (notes after are dropped; a held note closes there).
    /// The take still lands at the global stop, as one part of that stop's single undo step. False when that track was not captured.
    /// </summary>
    public bool Disarm(TrackModel track, long stamp)
    {
        lock (_gate)
        {
            if (!_capturing) return false;
            var index = _takes.FindIndex(entry => ReferenceEquals(entry.Track, track));
            if (index < 0 || _endedAt.ContainsKey(track)) return false;
            var endSec = SongPosAt(stamp).Sec;
            if (double.IsNaN(endSec)) return false;
            _endedAt[track] = endSec;
            _takes[index].Take.CloseHeld(endSec);
            return true;
        }
    }

    /// <summary>The window closed: the MIDI input is released (TransportRecording.Dispose stops the capture first and keeps its take).</summary>
    public void Dispose() => Stop(Stopwatch.GetTimestamp());

    // A client that names the device filters per track; a plain one is one unnamed device that every track hears.
    private void Subscribe() { if (_input is IMidiDeviceInputClient named) named.DeviceMessage += OnMessage; else _input.Message += OnUnnamed; }
    private void Unsubscribe() { if (_input is IMidiDeviceInputClient named) named.DeviceMessage -= OnMessage; else _input.Message -= OnUnnamed; }
    private void OnUnnamed(int status, int data1, int data2, long stamp) => OnMessage("", status, data1, data2, stamp);

    private static bool Hears(string[] devices, string device) => devices.Length == 0 || device.Length == 0 || Array.IndexOf(devices, device) >= 0;

    // MIDI driver thread: the song second comes from the message's own stamp, so UI latency does not move the notes.
    private void OnMessage(string device, int status, int data1, int data2, long stamp)
    {
        if (status is < 0x80 or >= 0xF0) return;   // channel messages only: clock and system messages carry no notes
        lock (_gate)
        {
            if (!_capturing) return;
            var (heardSec, lap) = SongPosAt(stamp);
            if (double.IsNaN(heardSec)) return;   // the scheduler has no position (stopped): such messages are dropped
            var type = status & 0xF0;
            foreach (var (track, take, devices) in _takes)
            {
                if (_endedAt.ContainsKey(track) || !Hears(devices, device)) continue;   // disarmed mid-recording: this track's capture ended there
                take.Keep(type, data1, data2, heardSec, lap, _loop, _ => _startSec);
            }
            // The live overlay only needs notes (a flood of controllers would post per message for nothing). Handlers post to the UI thread.
            if (LiveNote is { } live && type is 0x80 or 0x90 && (type == 0x80 || data2 > 0))
                foreach (var (track, _, devices) in _takes)
                {
                    if (_endedAt.ContainsKey(track) || !Hears(devices, device)) continue;
                    live(track, type, data1, data2, heardSec);
                }
        }
    }

    /// <summary>
    /// The song second and loop lap a stopwatch stamp was heard at: with a live scheduler, its song second now (the output latency
    /// removed), wrapped into passes by the engine itself so a jump back opens the next pass; otherwise the
    /// elapsed time at the speed from the start second, wrapped into the loop range when the recording loops (the first pass runs
    /// from the start to the loop end, every later pass covers the whole loop).
    /// </summary>
    private (double Sec, int Lap) SongPosAt(long stamp)
    {
        if (_songSecAt is { } live)
        {
            var at = live(stamp);
            if (double.IsNaN(at)) return (double.NaN, _laps);
            // The live scheduler already runs at the playback speed: only the output latency is removed, as in the press-anchored path.
            var heard = at - _latencyTicks * _speed / Stopwatch.Frequency;
            if (_loop is { } range && range.End - range.Start >= 0.05)
            {
                if (!double.IsNaN(_lastLiveSec) && heard < _lastLiveSec - 0.25) _laps++;
                _lastLiveSec = heard;
                return (heard, _laps);
            }
            return (Math.Max(_startSec, heard), 0);
        }
        var elapsed = Math.Max(0, (stamp - _latencyTicks - _startStamp) * _speed / Stopwatch.Frequency);
        var sec = _startSec + elapsed;
        if (_loop is not { } loop || loop.End - loop.Start < 0.05 || sec <= loop.End) return (Math.Max(_startSec, sec), 0);
        var loopLength = loop.End - loop.Start;
        var over = sec - loop.End;
        var lap = 1 + (int)(over / loopLength);
        return (loop.Start + over % loopLength, lap);
    }
}
