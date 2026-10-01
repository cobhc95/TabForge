using System.Linq;
using TabForge.Services;

namespace TabForge.Playback;

/// <summary>
/// One MIDI message at an absolute musical time (milliseconds) on one output device.
/// Status is the full status byte including the channel nibble.
/// </summary>
public sealed class ScoreEvent
{
    public double TimeMs;
    public int DeviceId;
    public int Channel;
    public int Status;
    public int Data1;
    public int Data2;
    /// <summary>Track that produced the event, or -1 for metronome/global events.</summary>
    public int TrackIndex = -1;
    /// <summary>
    /// Channel setup (program / volume / pan / reverb / chorus). Setup events are re-sent after a
    /// panic or a loop wrap, because <c>midiOutReset</c> clears device state.
    /// </summary>
    public bool IsSetup;
    /// <summary>Marks a metronome click whose note/velocity/subdivision can be selected at dispatch time.</summary>
    public bool IsMetronome;
    /// <summary>Count-in clicks remain enabled even when the playback metronome is toggled off.</summary>
    public bool IsCountInClick;
    public bool IsMetronomeAccent;
    /// <summary>Tick within the beat on a 12-tick grid, allowing 1, 2, 3 or 4 subdivisions per beat.</summary>
    public int MetronomeTick;
    /// <summary>Matching note-on/off identifier, unique within this timeline.</summary>
    public int MetronomePairId;
    /// <summary>
    /// A compiled attack that was cut away (a repeated tremolo/trill attack after its note was shortened): kept while
    /// event indices are still in use, removed by the final sort so it is never sent.
    /// </summary>
    internal bool Dropped;

    public bool IsNoteOn => (Status & 0xF0) == 0x90 && Data2 > 0;
    public bool IsNoteOff => (Status & 0xF0) == 0x80 || ((Status & 0xF0) == 0x90 && Data2 == 0);
}

/// <summary>
/// A single sounding note in absolute time, produced by the same compiler that generates MIDI.
/// This is the canonical stream consumed by the fretboard/score visualisation - views never
/// re-read the score.
/// </summary>
public sealed class NoteEvent
{
    public double OnsetMs;
    public double DurationMs;
    public int TrackIndex;
    public int Bar;
    public int Cell;
    public int VoiceIndex;
    public int StringIndex;
    public int Fret;
    public int Midi;
    public int Velocity = 100;
    public bool Dead;
    public bool Ghost;
    public bool LetRing;
    /// <summary>Bent or whammied note: these play on a second (effect) channel so a bend does not detune other notes.</summary>
    public bool UsesEffectChannel;
    /// <summary>The MIDI channel the note really plays on (its track's effect channel when <see cref="UsesEffectChannel"/> and one was free), or -1 when unknown.</summary>
    public int Channel = -1;
    /// <summary>Fade-in expression belongs to the merged sounding note, including tied continuations.</summary>
    public bool FadeIn;
    public string? ChordName;
    /// <summary>Short playing-technique tag shown on the fretboard (e.g. "TAP", "H/P"), or null.</summary>
    public string? Technique;

    /// <summary>Indices into <see cref="ScoreTimeline.Events"/> of every note-off for this note.</summary>
    public List<int> OffEventIndices { get; } = new();

    /// <summary>
    /// Note-off indices that represent the end of a sustained articulation. Repeated attacks such as
    /// tremolo picking keep all releases in <see cref="OffEventIndices"/>, but only the final release
    /// should be moved by let-ring.
    /// </summary>
    public List<int> SustainOffEventIndices { get; } = new();

    public double EndMs => OnsetMs + DurationMs;
}

/// <summary>A performed bar in the playback order. <see cref="Bar"/> is the source bar index.</summary>
public readonly record struct ScoreBar(int Bar, double StartMs, double EndMs, int Slots, int Tempo, FermataSpan[]? Fermatas = null)
{
    /// <summary>Fraction (0..1) of the bar's slots reached at an absolute time, skipping the time a fermata holds the music.</summary>
    public double SlotFraction(double ms)
    {
        var length = Math.Max(1.0, EndMs - StartMs);
        if (Fermatas is null) return Math.Clamp((ms - StartMs) / length, 0, 1);
        var baseLength = Math.Max(1.0, length - FermataSpan.TotalExtraMs(Fermatas));
        return Math.Clamp(FermataSpan.Unwarp(Fermatas, ms - StartMs) / baseLength, 0, 1);
    }

    /// <summary>Absolute time at a fraction of the bar's slots (inverse of <see cref="SlotFraction"/>).</summary>
    public double MsAtFraction(double fraction)
    {
        var length = Math.Max(1.0, EndMs - StartMs);
        if (Fermatas is null) return StartMs + length * fraction;
        var baseLength = Math.Max(1.0, length - FermataSpan.TotalExtraMs(Fermatas));
        return StartMs + FermataSpan.Warp(Fermatas, baseLength * fraction);
    }
}

/// <summary>
/// Absolute-time MIDI event list compiled from a score: the single source of truth for playback,
/// visualisation and MIDI export.
/// </summary>
public sealed class ScoreTimeline
{
    public readonly List<ScoreEvent> Events = new();
    public readonly List<ScoreBar> Bars = new();
    public readonly List<NoteEvent> Notes = new();

    /// <summary>Per-channel program/controller messages; re-sent after a device reset.</summary>
    public readonly List<ScoreEvent> ChannelSetup = new();

    public double TotalMs;
    /// <summary>Length of the count-in (0 when disabled); music begins at this time when there is one.</summary>
    public double CountInMs;
    /// <summary>Absolute time of the first *sounding* event (start cell offset included).</summary>
    public double PlayFromMs;

    /// <summary>
    /// Number of same-pitch releases that still land on the next attack after gap correction.
    /// Non-zero means a synth may swallow those retriggers. Computed before the event sort.
    /// </summary>
    public int RetriggerCollisions { get; set; }

    // Diagnostics for the sustain logic (tie/let-ring merging).
    public int TieMerges { get; set; }
    public int TieOrphans { get; set; }
    public int LetRingExtensions { get; set; }
    public double MaxLetRingExtensionMs { get; set; }
    public double LongestSoundingNoteMs { get; set; }
    public double LongestSoundingNoteAtBar { get; set; }

    private readonly Dictionary<int, NoteEvent[]> _byTrack = new();

    /// <summary>Per-track note array sorted by onset, cached for the visualiser and editor.</summary>
    public NoteEvent[] NotesFor(int trackIndex)
    {
        if (_byTrack.TryGetValue(trackIndex, out var cached)) return cached;
        var array = Notes.Where(n => n.TrackIndex == trackIndex).OrderBy(n => n.OnsetMs).ToArray();
        _byTrack[trackIndex] = array;
        return array;
    }

    /// <summary>The performed bar containing <paramref name="ms"/> (binary search).</summary>
    public ScoreBar BarAt(double ms)
    {
        if (Bars.Count == 0) return default;
        var lo = 0;
        var hi = Bars.Count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (Bars[mid].StartMs <= ms + 1e-6) lo = mid; else hi = mid - 1;
        }
        return Bars[lo];
    }
}
