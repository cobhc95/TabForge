using System.Linq;
using System.Windows;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge.Visualization;

public enum VisualRole { Past, Current, Selected, Next, Upcoming }

public sealed class VisualNote
{
    public int StringIndex;
    public int Fret;
    public int Midi;
    public double OnsetMs;
    public double DurationMs;
    public VisualRole Role;
    /// <summary>1 = full emphasis, 0 = invisible. Falls off with musical distance.</summary>
    public double Emphasis = 1;
    public bool Dead;
    public bool Ghost;
    public bool Held;          // sounding right now (onset passed, duration not finished)
    /// <summary>Shown as the current position but no longer sounding (kept readable during rests).</summary>
    public bool Released;
    public string? ChordName;
    /// <summary>Short playing-technique tag drawn above the marker (e.g. "TAP"), or null.</summary>
    public string? Technique;
}

/// <summary>What an instrument visualisation needs to draw one frame.</summary>
public sealed class InstrumentVisualState
{
    public InstrumentKind Kind = InstrumentKind.Guitar;
    public IReadOnlyList<int> Tuning = new List<int>();   // high -> low, MIDI numbers
    public int FretCount = 24;
    /// <summary>How many frets to show at once: 12 or 24 (default 24).</summary>
    public int DisplayFrets = 24;
    public bool LeftHanded;
    public bool ShowNoteNames;
    public bool ShowStringLabels = true;
    public int PreviewHorizon = 6;                        // how many upcoming movements to preview
    public IReadOnlyList<VisualNote> Notes = Array.Empty<VisualNote>();
    public VisualNote? Current;
    public VisualNote? Next;
    public double NowMs;
    public bool IsPlaying;
    public bool IsPaused;
    /// <summary>Score-following layout: no movement line between beats (markers keep the TabForge look).</summary>
    public bool FollowScoreStyle;
    public string? ChordName;
    public string? ScaleName;
    public IReadOnlyCollection<int> ScalePitchClasses = Array.Empty<int>();
    public double LoopStartMs = double.NaN;
    public double LoopEndMs = double.NaN;
    /// <summary>0..1 presentation pulse used to breathe the "next" marker (never affects timing).</summary>
    public double Pulse;
    /// <summary>How long a note stays visible after it stops sounding, in ms.</summary>
    public double PastHoldMs = 2600;
    /// <summary>Keys drawn by the keyboard view (88 = A0..C8).</summary>
    public int KeyboardKeys = 88;
    /// <summary>Keyboard view: soft grey "white" keys instead of pure white.</summary>
    public bool GreyKeys;
    /// <summary>Scale notes: "Shaded", "Circles" or "Rings" (see ScaleHighlightStyles).</summary>
    public string ScaleStyle = "Shaded";
    /// <summary>Scale highlight colour.</summary>
    public System.Windows.Media.Color ScaleColour = System.Windows.Media.Color.FromRgb(0x4C, 0x8A, 0xE0);
    /// <summary>Scale highlight strength multiplier (0.1 .. 1.5; 1.0 = the standard look).</summary>
    public double ScaleStrength = 1.0;
    /// <summary>Fret position dots: colour (null = theme) and brightness 0 (original) .. 1.</summary>
    public System.Windows.Media.Color? MarkerColour;
    public double MarkerBrightness = 0.3;
    /// <summary>Scale of the fret numbers, note bubbles, technique tags and string labels (Large = 1.0, the original size).</summary>
    public double NumberScale = 0.85;
    /// <summary>Multiplier (0.75 .. 1.5) of the natural string-gap cap; see <see cref="FretboardGeometry.MaxGapToFretWidth"/>.</summary>
    public double StringSpacing = 1.0;
}

public enum InstrumentKind { Guitar, Bass, Drums, Keyboard, Unknown }

/// <summary>
/// Builds the fretboard/instrument view model from the canonical playback timeline.
/// This never re-reads the score: it consumes the same NoteEvent stream the MIDI engine plays.
/// </summary>
public enum Gp5FretboardMode { Beat, BeatAndNextBeat, BeatAndBar, Bar }

public static class InstrumentVisualizer
{
    private static readonly object ScaleCacheGate = new();
    private static readonly Dictionary<string, int[]> ScaleCache = new(StringComparer.Ordinal);
    private static readonly Queue<string> ScaleCacheOrder = new();
    private const int ScaleCacheCapacity = 64;

    private static readonly string[] StringedNames =
    {
        "guitar", "bass", "oud", "bouzouki", "mandolin", "ukulele", "lute", "banjo", "sitar", "violin", "viola", "cello",
        "contrabass", "double bass", "fiddle", "harp", "koto", "shamisen", "baglama", "saz", "balalaika", "charango", "dobro", "pedal steel"
    };

    /// <summary>
    /// The view that suits the track's instrument: drum pads for drums; a fretboard (with the track's own
    /// strings) for stringed instruments, recognised by name or by GM program; a keyboard for the rest.
    /// </summary>
    public static InstrumentKind NaturalKind(TrackModel? track)
    {
        if (track is null) return InstrumentKind.Guitar;
        if (track.Kind == TrackKind.Drums || track.MidiChannel == 9) return InstrumentKind.Drums;
        // Word starts only: "flute" must not match "lute", "contrabass" is listed on its own.
        var name = " " + System.Text.RegularExpressions.Regex.Replace((track.InstrumentName ?? "").ToLowerInvariant(), "[^a-z]+", " ");
        if (name.Contains(" piano") || name.Contains(" organ") || name.Contains(" synth") || name.Contains(" keys")) return InstrumentKind.Keyboard;
        if (StringedNames.Any(word => name.Contains(" " + word)) || track.MidiProgram is >= 24 and <= 47 or >= 104 and <= 107)
            return track.Kind == TrackKind.Bass || name.Contains(" bass") || track.MidiProgram is >= 32 and <= 39 ? InstrumentKind.Bass : InstrumentKind.Guitar;
        return InstrumentKind.Keyboard;
    }

    public static InstrumentKind KindOf(TrackModel? track) => track?.Kind switch
    {
        TrackKind.Guitar => InstrumentKind.Guitar,
        TrackKind.Bass => InstrumentKind.Bass,
        TrackKind.Drums => InstrumentKind.Drums,
        TrackKind.Keys => InstrumentKind.Keyboard,
        _ => InstrumentKind.Unknown
    };

    public static InstrumentVisualState Build(
        SongProject project,
        TrackModel? track,
        ScoreTimeline? timeline,
        double nowMs,
        bool isPlaying,
        bool isPaused,
        int previewHorizon,
        bool leftHanded,
        bool showNoteNames,
        string? scaleName,
        int displayFrets = 24,
        double pulse = 0,
        VisualOptions? options = null)
    {
        var gp5Mode = options?.Gp5Mode;
        var state = new InstrumentVisualState
        {
            Kind = KindOf(track),
            Tuning = (IReadOnlyList<int>?)track?.StringTunings ?? Array.Empty<int>(),
            FretCount = track?.NumberOfFrets ?? 24,
            DisplayFrets = displayFrets is 12 or 24 ? displayFrets : 24,
            LeftHanded = leftHanded,
            ShowNoteNames = showNoteNames,
            PreviewHorizon = Math.Clamp(previewHorizon, 0, 10),
            NowMs = nowMs,
            IsPlaying = isPlaying,
            IsPaused = isPaused,
            ScaleName = scaleName,
            FollowScoreStyle = gp5Mode is not null,
            Pulse = pulse
        };

        SetScalePitchClasses(state, scaleName);

        if (track is null || timeline is null) return state;
        var trackIndex = project.Tracks.IndexOf(track);
        if (trackIndex < 0) return state;

        var notes = timeline.NotesFor(trackIndex);
        if (notes.Length == 0) return state;

        // Index of the first note that starts after now (shared binary search).
        var lo = NoteTimeline.FirstIndexAfter(notes, nowMs);

        var list = new List<VisualNote>();
        var hold = state.PastHoldMs;

        // 1. Still sounding (may be several notes of a chord). This is the accurate note length.
        var soundingAny = false;
        for (var index = lo - 1; index >= 0; index--)
        {
            var n = notes[index];
            if (n.OnsetMs < nowMs - 6000) break;
            if (n.EndMs <= nowMs) continue;
            var v = Convert(n, VisualRole.Current, 1.0);
            v.Held = true;
            list.Add(v);
            soundingAny = true;
        }

        // 2. Recently struck: fade fast first (so a note never *looks* longer than it sounds) and
        //    then keep a faint trace while it is still recent enough to be useful.
        var endedCount = 0;
        for (var index = lo - 1; index >= 0 && endedCount < 4; index--)
        {
            var n = notes[index];
            if (n.EndMs > nowMs) continue;
            if (nowMs - n.EndMs > hold) break;
            var age = nowMs - n.EndMs;
            var fast = Math.Exp(-age / 320.0);                   // quick release
            var slow = Math.Max(0, 1.0 - age / hold);            // position trace
            var emphasis = Math.Clamp(0.55 * fast + 0.16 * slow, 0.06, 0.7);
            list.Add(Convert(n, VisualRole.Past, emphasis));
            endedCount++;
        }

        // 3. If nothing is sounding (a rest), keep the last struck note as a clearly *released*
        //    position indicator: readable, but visually distinct from a sounding note.
        if (!soundingAny && lo > 0)
        {
            var last = notes[lo - 1];
            var v = Convert(last, VisualRole.Current, 0.7);
            v.Released = true;
            list.Add(v);
        }

        // score-following layout: the preview follows the score (this beat / next beat / this bar) instead
        // of a fixed number of upcoming movements. Markers keep TabForge's own look.
        if (gp5Mode is { } gp5)
        {
            list.RemoveAll(v => v.Role == VisualRole.Past);
            var bar = lo > 0 ? notes[lo - 1].Bar : notes[0].Bar;
            if (gp5 is Gp5FretboardMode.BeatAndNextBeat && lo < notes.Length)
            {
                var nextOnset = notes[lo].OnsetMs;
                for (var i = lo; i < notes.Length && Math.Abs(notes[i].OnsetMs - nextOnset) <= 0.5; i++)
                    list.Add(Convert(notes[i], VisualRole.Next, 1.0));
            }
            else if (gp5 is Gp5FretboardMode.BeatAndBar or Gp5FretboardMode.Bar)
            {
                // Every other note of the bar the cursor is in: earlier ones faint, later ones as preview.
                for (var i = lo - 1; i >= 0 && notes[i].Bar == bar; i--)
                {
                    var n = notes[i];
                    if (!list.Any(v => v.StringIndex == n.StringIndex && Math.Abs(v.OnsetMs - n.OnsetMs) < 0.5))
                        list.Add(Convert(n, VisualRole.Past, 0.42));
                }
                var first = true;
                for (var i = lo; i < notes.Length && notes[i].Bar == bar; i++)
                {
                    var isNext = first || Math.Abs(notes[i].OnsetMs - notes[lo].OnsetMs) <= 0.5;
                    first = false;
                    list.Add(Convert(notes[i], isNext ? VisualRole.Next : VisualRole.Upcoming, isNext ? 1.0 : 0.62));
                }
                if (gp5 is Gp5FretboardMode.Bar)
                    foreach (var v in list) if (v.Role == VisualRole.Current) { v.Role = VisualRole.Upcoming; v.Emphasis = 0.8; }
            }
            state.Notes = list;
            state.Current = list.FirstOrDefault(n => n.Role == VisualRole.Current);
            state.Next = list.FirstOrDefault(n => n.Role == VisualRole.Next);
            state.ChordName = state.Current?.ChordName ?? (list.Count > 0 ? list[0].ChordName : null);
            return state;
        }

        // 4. Upcoming movements, grouped by onset: a chord is ONE movement, not a sequence of notes.
        var horizons = new List<double>();
        for (var i = lo; i < notes.Length && horizons.Count < state.PreviewHorizon + 2; i++)
        {
            var onset = notes[i].OnsetMs;
            if (horizons.Count == 0 || onset - horizons[^1] > 0.5) horizons.Add(onset);
        }
        if (horizons.Count > 0)
        {
            var nextOnset = horizons[0];
            var horizonIndex = 0;
            for (var i = lo; i < notes.Length; i++)
            {
                var n = notes[i];
                if (n.OnsetMs - horizons[^1] > 0.5) break;
                while (horizonIndex + 1 < horizons.Count &&
                       Math.Abs(horizons[horizonIndex + 1] - n.OnsetMs) <= 0.5)
                    horizonIndex++;
                var depth = horizonIndex;
                var isNext = Math.Abs(n.OnsetMs - nextOnset) <= 0.5;
                var emphasis = isNext ? 1.0 : Math.Max(0.16, 0.78 - depth * 0.17);
                list.Add(Convert(n, isNext ? VisualRole.Next : VisualRole.Upcoming, emphasis));
            }
        }

        state.Notes = list;
        foreach (var note in list)
        {
            if (state.Current is null && note.Role == VisualRole.Current) state.Current = note;
            if (state.Next is null && note.Role == VisualRole.Next) state.Next = note;
            if (state.Current is not null && state.Next is not null) break;
        }
        state.ChordName = state.Current?.ChordName ?? (list.Count > 0 ? list[0].ChordName : null);
        return state;
    }

    /// <summary>Builds the persistent editor-cursor highlight directly from the selected score beat.</summary>
    public static InstrumentVisualState BuildEditingSelection(
        TrackModel? track,
        TabCell? cell,
        bool leftHanded,
        bool showNoteNames,
        string? scaleName,
        int displayFrets = 24,
        VisualOptions? options = null)
    {
        var state = new InstrumentVisualState
        {
            Kind = KindOf(track),
            Tuning = (IReadOnlyList<int>?)track?.StringTunings ?? Array.Empty<int>(),
            FretCount = track?.NumberOfFrets ?? 24,
            DisplayFrets = displayFrets is 12 or 24 ? displayFrets : 24,
            LeftHanded = leftHanded,
            ShowNoteNames = showNoteNames,
            IsPlaying = false,
            IsPaused = false,
            FollowScoreStyle = options?.Gp5Mode is not null,
            ScaleName = scaleName
        };
        SetScalePitchClasses(state, scaleName);

        if (track is null || cell is null) return state;
        var selected = cell.Notes.Select(note => new VisualNote
        {
            StringIndex = note.StringIndex,
            Fret = note.Fret,
            Midi = note.MidiValue > 0
                ? note.MidiValue
                : note.StringIndex >= 0 && note.StringIndex < track.StringTunings.Count
                    ? track.PitchOf(note.StringIndex, note.Fret)
                    : note.Fret,
            Role = VisualRole.Selected,
            Emphasis = 1,
            Dead = note.Dead,
            Ghost = note.Ghost,
            ChordName = cell.ChordName,
            Technique = TechniqueTag.From(note.Techniques)
        }).ToList();
        state.Notes = selected;
        state.Current = selected.Count > 0 ? selected[0] : null;
        state.ChordName = cell.ChordName;
        return state;
    }

    private static void SetScalePitchClasses(InstrumentVisualState state, string? scaleName)
    {
        if (scaleName is null) return;
        lock (ScaleCacheGate)
        {
            if (ScaleCache.TryGetValue(scaleName, out var cached))
            {
                state.ScalePitchClasses = cached;
                return;
            }
        }

        var separator = scaleName.IndexOf(' ');
        var root = separator < 0 ? scaleName : scaleName[..separator];
        var mode = separator < 0 ? "Major" : scaleName[(separator + 1)..];
        var pitchClasses = new List<int>(7);
        foreach (var note in MusicTheoryService.ScaleNotes(root, mode))
        {
            var pitchClass = Array.IndexOf(MusicTheoryService.NoteNames, note);
            if (pitchClass >= 0) pitchClasses.Add(pitchClass);
        }
        var result = pitchClasses.ToArray();
        lock (ScaleCacheGate)
        {
            if (ScaleCache.TryGetValue(scaleName, out var cached)) result = cached;
            else
            {
                if (ScaleCache.Count >= ScaleCacheCapacity)
                    ScaleCache.Remove(ScaleCacheOrder.Dequeue());
                ScaleCache.Add(scaleName, result);
                ScaleCacheOrder.Enqueue(scaleName);
            }
        }
        state.ScalePitchClasses = result;
    }

    private static VisualNote Convert(NoteEvent n, VisualRole role, double emphasis) => new()
    {
        StringIndex = n.StringIndex,
        Fret = n.Fret,
        Midi = n.Midi,
        OnsetMs = n.OnsetMs,
        DurationMs = n.DurationMs,
        Role = role,
        Emphasis = emphasis,
        Dead = n.Dead,
        Ghost = n.Ghost,
        ChordName = n.ChordName,
        Technique = n.Technique
    };
}
