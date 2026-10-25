using TabForge.Models;

namespace TabForge.Playback;

// Owns: the transport and playhead surface a playback view needs from an engine, with no toolkit or audio types.
// Does not own: scheduling, timeline compilation, MIDI output (the implementing engine).
// Tests: no dedicated test class.
// Threads: transport calls (Start, Stop, Pause, Resume, Seek) and AudiblePlayhead run on the owning UI thread; IsPlaying, IsPaused and
//     Playhead read volatile state and are safe on any thread; onPosition and onFinished run on the scheduler thread; TimelineChanged
//     and TimelineRevised may be raised on either thread (a handler posts to the UI thread itself).

/// <summary>A playback engine as the playback view sees it.</summary>
public interface IPlaybackEngine : IDisposable
{
    bool IsPlaying { get; }
    bool IsPaused { get; }

    /// <summary>Raised with the compiled timeline at a start, a seek or a speed change.</summary>
    event Action<ScoreTimeline>? TimelineChanged;
    /// <summary>Raised with a live-reordered future timeline; the playhead does not move.</summary>
    event Action<ScoreTimeline>? TimelineRevised;

    void Start(SongProject project, PlaybackOptions options, Action<PlaybackPosition> onPosition, Action onFinished, bool startPaused = false);
    /// <summary>Starts like Start but returns at once, compiling off the calling thread; an engine without a worker start starts at once.</summary>
    void BeginStart(SongProject project, PlaybackOptions options, Action<PlaybackPosition> onPosition, Action onFinished, bool startPaused = false) =>
        Start(project, options, onPosition, onFinished, startPaused);
    void Stop();
    void Pause();
    void Resume();
    void Seek(SongProject project, int bar, int cell);
    /// <summary>Seeks to the <paramref name="occurrence"/>-th performance of the bar (the pass-aware seek); an engine that only knows bars ignores it.</summary>
    void Seek(SongProject project, int bar, int cell, int? occurrence) => Seek(project, bar, cell);
    /// <summary>The bars that start a section (the song's markers), which a start counts in before when count-in sections is on.</summary>
    void SetSectionStarts(IEnumerable<int> bars) { }
    /// <summary>Turns looping on or off for the running playback and the next start.</summary>
    void SetLoop(bool loop);

    /// <summary>Moves the loop's bar and cell bounds under a running playback without restarting it; an engine without loop bounds ignores the change.</summary>
    void SetLoopRange(int startBar, int endBar, int startCell, int endCell) { }

    /// <summary>Changes the options a running song or the next start uses (metronome, count-in); a running song is recompiled from its position. An engine without options ignores the change.</summary>
    void UpdateOptions(SongProject project, Action<PlaybackOptions> mutate) { }

    /// <summary>The MIDI output the engine plays through, when it routes channels to the audio engine; null for an engine without one.</summary>
    TabForge.Audio.RoutedMidiOutput? Routing => null;

    /// <summary>Applies mute and solo to the running playback without a restart; an engine without live mute and solo ignores the change.</summary>
    void SetMuteSolo(SongProject project, bool liveSolo = false) { }

    /// <summary>Sets the master percent (0-100) the running playback plays at; an engine without a master level ignores the change.</summary>
    void SetMasterVolume(SongProject project, int percent) { }

    /// <summary>Writes the settings-driven values the scheduler reads live (count-in volume and sound, metronome boost, section count-in, loop behaviour) into the engine's preferences; an engine without them ignores the change.</summary>
    void SetPreferences(Action<PlaybackPreferences> write) { }

    /// <summary>Plays one short test note on a MIDI output (the Sound menu's test row); an engine without MIDI output does nothing.</summary>
    System.Threading.Tasks.Task PreviewNoteAsync(int deviceId, int channel, int program, int note, int ms = 350) => System.Threading.Tasks.Task.CompletedTask;

    /// <summary>Which performance (1-based, counted in the full performed order) of its bar the playing position is in, for an internal restart to resume that pass; null when unknown or the first (an engine without passes).</summary>
    int? LiveOccurrence(SongProject project) => null;

    /// <summary>Sends every channel's program, volume and pan to the output again (its synths were just created); an engine without MIDI output ignores it.</summary>
    void RearmChannelSetup() { }

    /// <summary>Switches the channel to <paramref name="program"/> now and keeps it through loop restarts and seeks until the next timeline is compiled or <see cref="EndProgramPreview"/>; an engine without MIDI output ignores it.</summary>
    void BeginProgramPreview(SongProject project, int deviceId, int channel, int program) { }

    /// <summary>Drops the switch and sends the song's own program again (unless <paramref name="resend"/> is false: the caller re-arms the whole setup right after); an engine without MIDI output ignores it.</summary>
    void EndProgramPreview(SongProject project, int deviceId, int channel, bool resend = true) { }

    /// <summary>The scheduler's last reported position (a new instance).</summary>
    PlaybackPosition Playhead();
    /// <summary>The position being heard now, for drawing one frame: while it plays it fills and returns the caller's <paramref name="frame"/> (no allocation).</summary>
    PlaybackPosition AudiblePlayhead(PlaybackPosition frame);
}
