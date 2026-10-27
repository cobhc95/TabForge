using System.Diagnostics;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Features.Recording.Rules;

// Owns: the UI-free part of Transport Record: which tracks are armed, starting and ending the
//   engine's recording of the armed audio tracks, the MIDI takes of the armed MIDI tracks (MidiTakeCapture), and the status text each outcome shows.
// Does not own: the playback that Record starts (the caller's session), landing the takes on lanes (the caller: audio takes arrive through the
//   engine's Recorded event, MIDI takes through TakeFinishedMidi), the window and its status line (the caller shows the text).
// Tests: TabForge.Avalonia.Tests.TransportRecordingTests, TabForge.Avalonia.Tests.MidiTakeCaptureTests (offline: the engine client is not started).
public sealed class TransportRecording : IDisposable
{
    public const string NothingArmedText = "Arm a track first (the red button on its row), then press Record";
    public const string CouldNotStartText = "Recording could not start: the audio engine is not running (check Settings > Audio & Plug-ins)";
    /// <summary>The folder prefix when the recording folder itself cannot hold a take (validation or write probe, not the engine).</summary>
    public const string FolderNotUsableText = "Recording folder could not be used: ";
    public const string NoMidiDeviceText = "No MIDI input device found (or it is in use by another program)";
    public const string RecordingText = "Recording… press Record or Stop to finish";
    public const string StoppedText = "Recording stopped";

    private readonly AudioEngineClient _engine;
    private readonly MidiTakeCapture _midi;
    private List<(TrackModel Track, List<AudioClip> Clips)> _finishedMidi = new();

    /// <param name="midiInput">The client the MIDI takes listen on (the caller's; the window's engine client hands out its own).</param>
    public TransportRecording(AudioEngineClient engine, IMidiInputClient midiInput)
    {
        _engine = engine;
        _midi = new MidiTakeCapture(midiInput);
    }

    /// <summary>True from a successful start until the next toggle.</summary>
    public bool IsRecording { get; private set; }

    /// <summary>The note messages heard while capturing, for the live overlay (MIDI driver thread; see <see cref="MidiTakeCapture.LiveNote"/>).</summary>
    public event Action<TrackModel, int, int, int, double>? LiveMidiNote
    {
        add => _midi.LiveNote += value;
        remove => _midi.LiveNote -= value;
    }

    /// <summary>True when at least one track is armed (the red button on its row).</summary>
    public static bool AnyArmed(IReadOnlyList<TrackModel> tracks) => tracks.Any(t => t.RecordArm);

    /// <summary>
    /// The loop range of the recording in progress, in song seconds (null: no loop): the caller cuts the audio take at it and the MIDI capture
    /// opens one take per pass. Kept after the stop so the engine's later Recorded event can split on the same range.
    /// </summary>
    public (double Start, double End)? RecordLoop { get; private set; }

    /// <summary>
    /// A track disarmed while recording: its capture ends at that moment (MIDI notes after are dropped; the audio engine keeps writing
    /// its file and the caller trims the landed clip). The take still lands at the global stop, in that stop's one undo step.
    /// </summary>
    public bool DisarmTrack(TrackModel track, long stamp) => _midi.Disarm(track, stamp);

    /// <summary>The MIDI takes the last recording stop finished, per track; handed over once, so the caller lands each on its track.</summary>
    public List<(TrackModel Track, List<AudioClip> Clips)> TakeFinishedMidi()
    {
        var taken = _finishedMidi;
        _finishedMidi = new();
        return taken;
    }

    /// <summary>
    /// Ends the recording in progress, or starts recording the armed audio tracks into <paramref name="mediaFolder"/> and the armed MIDI tracks
    /// from the song second <paramref name="startSec"/> at <paramref name="speed"/> (both used when a recording starts); returns the status
    /// text to show. <paramref name="loop"/> cuts a looped recording into one take per pass. <paramref name="songSecAt"/> is the live scheduler
    /// song second at a stamp: given, the MIDI notes follow the song as it plays instead of the press-time second. A start that captures nothing (no engine input for
    /// audio, no MIDI device for a MIDI-only set) leaves <see cref="IsRecording"/> false, so the transport is never left in a recording state that
    /// records nothing. <paramref name="capture"/> (a re-record of one pass) is the exact track set to capture: other armed tracks are
    /// excluded and the target needs no record arm. Null keeps the normal armed-track behaviour. UI thread only.
    /// </summary>
    public string Toggle(IReadOnlyList<TrackModel> tracks, string mediaFolder, double startSec = 0, double speed = 1, (double Start, double End)? loop = null,
        Func<long, double>? songSecAt = null, IReadOnlyList<TrackModel>? capture = null)
    {
        if (IsRecording)
        {
            IsRecording = false;
            _engine.StopRecording();   // audio takes arrive through the engine's Recorded event
            _finishedMidi.AddRange(_midi.Stop(Stopwatch.GetTimestamp()));
            return StoppedText;
        }
        var requested = capture ?? tracks;
        if (capture is null && !AnyArmed(tracks)) return NothingArmedText;
        if (!requested.Any(t => (capture is not null || t.RecordArm) && AudioInputs.HasInput(t))) return AudioInputs.NoInputText;   // armed tracks with no audio and no MIDI input have nothing to record
        var audioArmed = requested.Any(t => (capture is not null || t.RecordArm) && AudioInputs.HasAudio(t.AudioInput));
        // The engine being down is reported without touching the disk; otherwise the folder is validated (device/link)
        // and write-probed before the Record command is sent, so a denied folder reports its own message, not the engine's.
        if (audioArmed && !_engine.IsRunning) return CouldNotStartText;
        var fellBack = false;
        var fellBackDenied = true;
        if (audioArmed && TryValidateRecordingFolder(mediaFolder, out var validatedFolder) is { } failure)
        {
            var denied = FilePathPolicy.IsAccessDenied(failure);
            var full = !denied && FilePathPolicy.IsDiskFull(failure);
            TabForge.Services.Trace.Error(TabForge.Services.Trace.Ui, $"recording folder: validate: {failure.Message} denied={denied} hresult=0x{failure.HResult:X8}");
            // Level 3 is the song's own folder (the takes folder's parent): takes stay discoverable when both
            // system fallbacks fail. Video skips it (no song folder in that flow).
            string? songAdjacent = null;
            try
            {
                if (Path.IsPathFullyQualified(mediaFolder)) songAdjacent = Path.GetDirectoryName(mediaFolder);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { songAdjacent = null; }
            string? win = null;
            if (denied || full) win = FilePathPolicy.FallbackWritableChain("Recordings", songAdjacent, out _);
            if (win is null) return failure is InvalidDataException
                ? FolderNotUsableText + failure.Message
                : FolderNotUsableText + failure.Message
                    + (denied ? FilePathPolicy.AccessDeniedHint : "")
                    + $" (folder: {validatedFolder})";
            mediaFolder = win; fellBack = true; fellBackDenied = denied;
        }
        if (audioArmed && !_engine.StartRecording(requested, mediaFolder, requireArm: capture is null)) return CouldNotStartText;
        var midiArmed = requested.Any(t => (capture is not null || t.RecordArm) && AudioInputs.TakesMidi(t));
        RecordLoop = loop;
        var midiDevices = midiArmed
            ? _midi.Start(requested, startSec, Stopwatch.GetTimestamp(), speed, _engine.LatencyTicks, loop, songSecAt, requireArm: capture is null)
            : 0;
        // A MIDI-only recording with no device opens no device and would capture nothing: refuse it instead of entering a recording state.
        if (midiArmed && midiDevices == 0 && !audioArmed) return NoMidiDeviceText;
        IsRecording = true;
        if (midiArmed && midiDevices == 0) return NoMidiDeviceText;
        return fellBack
            ? (fellBackDenied
                ? $"{RecordingText} Saved to {mediaFolder} because the usual folder was blocked by Windows"
                : $"{RecordingText} Saved to {mediaFolder} because the usual folder is full")
            : RecordingText;
    }

    /// <summary>
    /// Null when <paramref name="mediaFolder"/> can hold a recording (device path or link refused, then a real write probe);
    /// otherwise the structured failure (validate-stage <see cref="InvalidDataException"/>, else the probe failure) for the
    /// caller to report and to gate the fallback on with <see cref="FilePathPolicy.IsAccessDenied"/>. <paramref name="validated"/>
    /// is the validated full path (probe-stage reports name it); on a validate-stage failure it is the input unchanged.
    /// </summary>
    internal static Exception? TryValidateRecordingFolder(string mediaFolder, out string validated)
    {
        validated = mediaFolder;
        try { validated = FilePathPolicy.ValidateWritableFolder(mediaFolder, "recording folder"); }
        catch (InvalidDataException ex) { return ex; }
        if (!FilePathPolicy.TryProbeWritableFolder(validated, out var failure) && failure is not null)
            return failure;
        return null;
    }

    /// <summary>The window closed: a MIDI take still being captured is kept (finished into <see cref="TakeFinishedMidi"/>), not dropped.</summary>
    public void Dispose()
    {
        if (IsRecording) { IsRecording = false; _finishedMidi.AddRange(_midi.Stop(Stopwatch.GetTimestamp())); }
        _midi.Dispose();
    }
}
