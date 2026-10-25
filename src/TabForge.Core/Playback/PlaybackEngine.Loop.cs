using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using TabForge.Models;
using TabForge.Services;
using TempoMath = TabForge.Audio.Contracts.TempoMath;

namespace TabForge.Playback;

// Owns: loop range, option updates and rebuild.
// Does not own: the rest of this type (see the main file).
// Tests: no dedicated test class.
public sealed partial class PlaybackEngine
{
    /// <summary>Updates the loop bars of a running playback; the scheduler picks the new bounds up on its next tick.</summary>
    public void SetLoopRange(int startBar, int endBar, int startCell = 0, int endCell = -1)
    {
        if (ChangePendingStart(null, options =>
            {
                options.LoopStartBar = startBar;
                options.LoopEndBar = endBar;
                options.LoopStartCell = startCell;
                options.LoopEndCell = endCell;
            })) return;
        if (_options is null) return;
        _options.LoopStartBar = startBar;
        _options.LoopEndBar = endBar;
        _options.LoopStartCell = startCell;
        _options.LoopEndCell = endCell;
        // A loop that now begins before the compiled timeline needs a recompile from its start.
        var s = Volatile.Read(ref _song);
        var timeline = s?.Timeline;
        if (_running && _options.Loop && LiveProject is { } project && timeline is not null &&
            !timeline.Bars.Any(b => b.Bar == Math.Min(startBar, endBar)))
        {
            var pos = Playhead();
            _options.StartBar = pos.Bar;
            _options.StartCell = pos.Cell;
            _options.StartOccurrence = null;   // a loop restart keeps today's bar/cell start
            RestartKeepingState(project, _options, _paused);
            return;
        }
        Interlocked.Increment(ref _loopRangeVersion);
    }

    /// <summary>Applies metronome controls directly to the scheduler without rebuilding or stopping playback.</summary>
    public void SetMetronomeSettings(bool enabled, int volume, int accentVolume, int clickVolume,
        int accentNote, int clickNote, int subdivisions)
    {
        Volatile.Write(ref _metronomeEnabled, enabled ? 1 : 0);
        Volatile.Write(ref _metronomeVolume, Math.Clamp(volume, 0, 100));
        Volatile.Write(ref _metronomeAccentVolume, Math.Clamp(accentVolume, 0, 100));
        Volatile.Write(ref _metronomeClickVolume, Math.Clamp(clickVolume, 0, 100));
        Volatile.Write(ref _metronomeAccentNote, Math.Clamp(accentNote, 0, MidiLimits.Max));
        Volatile.Write(ref _metronomeClickNote, Math.Clamp(clickNote, 0, MidiLimits.Max));
        Volatile.Write(ref _metronomeSubdivision, subdivisions is 1 or 2 or 3 or 4 ? subdivisions : 1);

        if (_options is not { } options) return;
        options.Metronome = enabled;
        options.MetronomeVolume = Math.Clamp(volume, 0, 100);
        options.MetronomeAccentVolume = Math.Clamp(accentVolume, 0, 100);
        options.MetronomeClickVolume = Math.Clamp(clickVolume, 0, 100);
        options.MetronomeAccentNote = Math.Clamp(accentNote, 0, MidiLimits.Max);
        options.MetronomeClickNote = Math.Clamp(clickNote, 0, MidiLimits.Max);
        options.MetronomeSubdivision = subdivisions is 1 or 2 or 3 or 4 ? subdivisions : 1;
    }

    /// <summary>
    /// Applies a live option change (metronome / count-in). If the transport is running the timeline
    /// is recompiled from the current musical position so the change is heard immediately instead of
    /// being silently ignored until the next Play. The click voice itself (volumes, notes, subdivision,
    /// on/off) applies live at dispatch without any restart, so slider drags never recompile and never
    /// cut a count-in that is playing.
    /// </summary>
    public void UpdateOptions(SongProject project, Action<PlaybackOptions> mutate)
    {
        if (ChangePendingStart(project, mutate)) return;
        var opts = _options;
        if (opts is null) return;
        var before = opts.Clone();
        mutate(opts);
        // Push the click voice at once: the scheduler reads it live, so a volume, note or subdivision change
        // (or a toggle) takes effect even when the timeline below is unchanged.
        SetMetronomeSettings(opts.Metronome, opts.MetronomeVolume, opts.MetronomeAccentVolume,
            opts.MetronomeClickVolume, opts.MetronomeAccentNote, opts.MetronomeClickNote, opts.MetronomeSubdivision);
        if (!_running || opts.CompilesSameAs(before)) return; // unchanged: never restart
        var pos = Playhead();
        var occurrence = RestartOccurrence(project, opts, pos);   // before the start moves: it anchors the timeline match
        opts.StartBar = pos.Bar;
        opts.StartCell = pos.Cell;
        opts.StartOccurrence = occurrence;   // an option restart resumes the same performance
        RestartKeepingState(project, opts, _paused);
    }

    /// <summary>
    /// Recompiles the timeline from the current musical position. Used when a mixer change (mute,
    /// solo, volume, pan, program) must be heard immediately rather than at the next Play.
    /// </summary>
    public void Rebuild(SongProject project)
    {
        var opts = _options;
        // Stopped: nothing restarts, so push the current instruments / levels now (an instrument change must
        // register before the next play, not keep the stale programs).
        if (!_running) { SendSetupFromProject(project); return; }
        if (opts is null) return;
        var pos = Playhead();
        if (TryRestartInPlace(project, opts)) return;
        var occurrence = RestartOccurrence(project, opts, pos);   // before the start moves: it anchors the timeline match
        opts.StartBar = pos.Bar;
        opts.StartCell = pos.Cell;
        opts.StartOccurrence = occurrence;   // a rebuild resumes the same performance
        RestartKeepingState(project, opts, _paused);
    }

    // ---------- scheduler ----------

    /// <summary>
    /// RT-10 / D10: the 1 ms Windows timer resolution (shared, reference-counted <see cref="Audio.Contracts.NativeTimer"/>) is held only
    /// while this engine plays: raised on start and resume, released on pause, stop and dispose (an idle document costs no battery).
    /// </summary>
    private readonly Audio.Contracts.NativeTimer.Hold _timer = new();

}
