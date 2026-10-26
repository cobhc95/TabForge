using TabForge.Models;
using TabForge.Services;

namespace TabForge.Presenters;

// Owns: the piano roll's clock: the bars it lays out in quarter-note beats, each with its place in song seconds, and the mapping both ways
//       (seconds to beats, beats to seconds, beats to bar), the grid lines of a SnapSettings grid value in beats, and the bars past the end.
//       A MIDI clip's notes live on the performed clock (repeats played out, as clips are), a tab track's on the written bars.
// Does not own: the performed bar order and the song timing (SongExtent.Timing, AudioClipTimelineRules), the snap candidates (PianoRollSnap).
// Tests: TabForge.Avalonia.Tests.PianoRollSnapTests.

/// <summary>One bar of the piano roll: the song bar it shows, where it starts in beats and how many beats long it is, and its song seconds.</summary>
public readonly record struct PianoRollBar(int Bar, double StartBeat, double Beats, double StartSec, double EndSec)
{
    public double EndBeat => StartBeat + Beats;
}

/// <summary>The piano roll's beats-to-seconds map (a tempo map: each bar is linear inside, like the timeline's bar fraction).</summary>
public sealed class PianoRollTimeMap
{
    private readonly PianoRollBar[] _bars;
    private readonly double _tailBeats;
    private readonly double _tailSec;

    /// <param name="bars">The bars in order, back to back in beats and in seconds.</param>
    /// <param name="tailBeats">Beats of one more bar past the end; <paramref name="tailSec"/> its seconds (the grid and the clock go on in bars this long).</param>
    /// <param name="tailSec">Seconds of one more bar past the end.</param>
    public PianoRollTimeMap(IReadOnlyList<PianoRollBar> bars, double tailBeats = 4, double tailSec = 2)
    {
        _bars = bars.ToArray();
        _tailBeats = tailBeats > 0 ? tailBeats : 4;
        _tailSec = tailSec > 0 ? tailSec : 2;
    }

    public IReadOnlyList<PianoRollBar> Bars => _bars;

    /// <summary>Where the last bar ends, in beats.</summary>
    public double EndBeat => _bars.Length == 0 ? 0 : _bars[^1].EndBeat;

    /// <summary>The performed clock (repeats played out), for MIDI clips.</summary>
    public static PianoRollTimeMap Performed(SongProject song)
    {
        var timing = SongExtent.Timing(song);
        var bars = new List<PianoRollBar>(timing.Performed.Length);
        var beat = 0.0;
        foreach (var performed in timing.Performed)
        {
            var beats = BeatsOf(song, performed.Bar);
            bars.Add(new PianoRollBar(performed.Bar, beat, beats, performed.StartMs / 1000, performed.EndMs / 1000));
            beat += beats;
        }
        return new PianoRollTimeMap(bars, TailBeats(song), timing.BarSec);
    }

    /// <summary>The written bars in order (each at its first performance in seconds), for a tab track.</summary>
    public static PianoRollTimeMap Written(SongProject song)
    {
        var count = AudioClipTimelineRules.BarCount(song);
        var bars = new List<PianoRollBar>(count);
        var beat = 0.0;
        for (var bar = 0; bar < count; bar++)
        {
            var beats = BeatsOf(song, bar);
            var start = AudioClipTimelineRules.SecondsAtBar(song, bar, 0);
            var end = AudioClipTimelineRules.SecondsAtBar(song, bar, 1);
            bars.Add(new PianoRollBar(bar, beat, beats, start, Math.Max(start, end)));
            beat += beats;
        }
        return new PianoRollTimeMap(bars, TailBeats(song), SongExtent.Timing(song).BarSec);
    }

    private static double BeatsOf(SongProject song, int bar) => Math.Max(1, MusicTime.BarSlots(song, Math.Max(0, bar))) / SnapCore.SlotsPerBeat;

    private static double TailBeats(SongProject song) => BeatsOf(song, Math.Max(0, AudioClipTimelineRules.BarCount(song) - 1));

    /// <summary>The index of the bar holding <paramref name="beat"/> (clamped to the bars; -1 when there are none).</summary>
    public int IndexAtBeat(double beat)
    {
        if (_bars.Length == 0) return -1;
        int low = 0, high = _bars.Length - 1, found = 0;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (_bars[mid].StartBeat <= beat + 1e-9) { found = mid; low = mid + 1; }
            else high = mid - 1;
        }
        return found;
    }

    /// <summary>The bar holding <paramref name="beat"/>: a real bar, or a virtual one past the end in bars as long as the last.</summary>
    public PianoRollBar BarAtBeat(double beat)
    {
        if (_bars.Length > 0 && beat < EndBeat) return _bars[IndexAtBeat(beat)];
        var end = EndBeat;
        var endSec = _bars.Length == 0 ? 0 : _bars[^1].EndSec;
        var index = Math.Max(0, Math.Floor((beat - end) / _tailBeats));
        var lastBar = _bars.Length == 0 ? -1 : _bars[^1].Bar;
        return new PianoRollBar(lastBar + 1 + (int)index, end + index * _tailBeats, _tailBeats, endSec + index * _tailSec, endSec + (index + 1) * _tailSec);
    }

    /// <summary>Song seconds at <paramref name="beat"/> (linear inside each bar).</summary>
    public double SecondsAt(double beat)
    {
        var at = Math.Max(0, beat);
        var bar = BarAtBeat(at);
        var fraction = bar.Beats > 0 ? (at - bar.StartBeat) / bar.Beats : 0;
        return bar.StartSec + (bar.EndSec - bar.StartSec) * fraction;
    }

    /// <summary>Beats at song second <paramref name="seconds"/>: the first bar that holds it (the inverse of <see cref="SecondsAt"/>).</summary>
    public double BeatsAt(double seconds)
    {
        if (_bars.Length == 0 || seconds >= _bars[^1].EndSec)
        {
            var endSec = _bars.Length == 0 ? 0 : _bars[^1].EndSec;
            return EndBeat + Math.Max(0, seconds - endSec) / _tailSec * _tailBeats;
        }
        int low = 0, high = _bars.Length - 1, found = 0;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (_bars[mid].StartSec <= seconds + 1e-9) { found = mid; low = mid + 1; }
            else high = mid - 1;
        }
        var bar = _bars[found];
        var length = bar.EndSec - bar.StartSec;
        return bar.StartBeat + (length > 0 ? Math.Clamp((seconds - bar.StartSec) / length, 0, 1) : 0) * bar.Beats;
    }

    /// <summary>The grid step of <paramref name="grid"/> in beats in the bar holding <paramref name="beat"/>.</summary>
    public double GridStep(string grid, double beat) => SnapCore.GridStepSlots(grid, SlotsOf(BarAtBeat(beat))) / SnapCore.SlotsPerBeat;

    /// <summary>The grid line of <paramref name="grid"/> nearest to <paramref name="beat"/>, counted from the start of the bar holding it.</summary>
    public double NearestGrid(double beat, string grid)
    {
        var bar = BarAtBeat(beat);
        return Math.Max(0, SnapCore.NearestInBar(beat, bar.StartBeat, bar.Beats, SlotsOf(bar), grid));
    }

    /// <summary>
    /// The grid line nearest to <paramref name="beat"/> with <paramref name="swing"/> percent applied: every second line (counted from the bar start)
    /// moves later by up to a third of a step (100 = the triplet feel, as DAW swing).
    /// </summary>
    public double NearestGrid(double beat, string grid, double swing)
    {
        if (swing <= 0) return NearestGrid(beat, grid);
        var bar = BarAtBeat(beat);
        var step = GridStep(grid, beat);
        if (step <= 0) return NearestGrid(beat, grid);
        var shift = step / 3 * Math.Clamp(swing, 0, 100) / 100;
        var index = Math.Floor((beat - bar.StartBeat) / step);
        var best = beat;
        var distance = double.MaxValue;
        for (var k = index - 1; k <= index + 2; k++)
        {
            var line = bar.StartBeat + k * step + (((long)k & 1) == 1 ? shift : 0);
            if (Math.Abs(line - beat) < distance) { distance = Math.Abs(line - beat); best = line; }
        }
        return Math.Max(0, best);
    }

    private static int SlotsOf(PianoRollBar bar) => Math.Max(1, (int)Math.Round(bar.Beats * SnapCore.SlotsPerBeat));
}
