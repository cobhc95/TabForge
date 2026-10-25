// Owns: the tuner's logic without UI types: which engine readings count as a note, the smoothing within one note, the nearest note's name,
//       the Hz and cents text, the cents needle position and its in-tune tone, the string of the track the reading is closest to, and the
//       string boxes' names.
// Does not own: the audio engine and its readings (AudioEngineClient), the update timer, the window, the gauge drawing and brushes.
// Tests: none dedicated(WPF characterization), TunerWindowTests (Avalonia headless).
using System.Globalization;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Presenters;

/// <summary>Colour rule of the cents needle: in tune (within 5 cents), close (within 20), far, or none when nothing is heard.</summary>
public enum TunerTone { None, InTune, Close, Far }

/// <summary>
/// What the tuner shows after one update. <see cref="Changed"/> is false when the reading is the one already shown, so the view does not
/// redraw. <see cref="StringsChanged"/> says the track's string boxes must be rebuilt from <see cref="TunerPresenter.StringNames"/>.
/// <see cref="NeedleCents"/> is NaN and <see cref="StringIndex"/> is -1 when nothing is heard or no string is close enough.
/// </summary>
public readonly record struct TunerFrame(bool Changed, bool StringsChanged, string Hint, string NoteText, string DetailText,
    double NeedleCents, int StringIndex);

/// <summary>
/// Tuner state. <see cref="Update"/> takes the shown track, whether the engine runs and the latest reading (Hz, clarity, sequence number,
/// and the time), and returns what to show. Thread: UI thread only (the tuner window drives it from its timer).
/// Caches (both live as long as the presenter): <see cref="_noteNames"/> holds the name of each MIDI note 0..127, filled on first use
/// (ceiling 128 strings); <see cref="_detailText"/> holds the last Hz and cents text, reused while the new text is the same.
/// </summary>
public sealed class TunerPresenter
{
    public const string EngineOffHint = "The audio engine is off. Turn it on in Preferences, then arm a track to record so the input opens.";
    public const string PlayNoteHint = "Play a note. The tuner listens to the armed input: arm a track (the red dot on its row) so the input is open.";

    public const double DefaultReferenceHz = 440.0;
    private const double MinimumClarity = 0.6;          // YIN clarity below this counts as no pitch
    private const double SameNoteSemitones = 0.8;       // a reading this close to the shown note is smoothed; a farther one jumps
    private const double SmoothingWeight = 0.4;
    private const double SteadyNoteMs = 500;            // smoothing needs a voiced reading this recent
    private const double SilenceMs = 1200;              // after this long with no voiced reading the display clears
    private const double StringMatchSemitones = 2;      // a string counts as "the one" within this distance
    private const double InTuneCents = 5, CloseCents = 20;
    private const double NeedleRangeCents = 50;
    private const double NeedleMarginPx = 10;

    private readonly string[] _noteNames = new string[128];
    private readonly List<string> _stringNames = new();
    private TrackModel? _shownTrack;
    private int[] _tunings = Array.Empty<int>();
    private int _lastSeq = -1;
    private double _smoothedMidi = double.NaN;
    private DateTime _lastVoiced = DateTime.MinValue;
    private string _detailText = "";

    /// <summary>The frequency of A4 the notes are named from (440 by default; the live input strip sets it from Preferences).</summary>
    public double ReferenceHz { get; set; } = DefaultReferenceHz;

    /// <summary>Starts with the strings of <paramref name="track"/>; the first <see cref="Update"/> reports them as changed.</summary>
    public TunerPresenter(TrackModel? track) => SetStrings(track);

    /// <summary>The names of the track's strings, string 1 first (the highest). Empty for a track without strings (audio, drums, none).</summary>
    public IReadOnlyList<string> StringNames => _stringNames;

    /// <summary>
    /// Applies one update. <paramref name="seq"/> numbers the engine's readings: an equal number is the reading already shown.
    /// </summary>
    public TunerFrame Update(TrackModel? track, bool engineRunning, float hz, float clarity, int seq, DateTime utcNow)
    {
        var stringsChanged = !ReferenceEquals(track, _shownTrack);
        if (stringsChanged) SetStrings(track);
        if (!engineRunning) return Cleared(stringsChanged, EngineOffHint);
        if (seq == _lastSeq) return Unchanged(stringsChanged);
        _lastSeq = seq;

        if (hz > 0 && clarity >= MinimumClarity)
        {
            var midi = 69 + 12 * Math.Log2(hz / ReferenceHz);
            var steady = !double.IsNaN(_smoothedMidi) && Math.Abs(midi - _smoothedMidi) < SameNoteSemitones
                && (utcNow - _lastVoiced).TotalMilliseconds < SteadyNoteMs;
            _smoothedMidi = steady ? _smoothedMidi + (midi - _smoothedMidi) * SmoothingWeight : midi;
            _lastVoiced = utcNow;
            return Reading(stringsChanged, _smoothedMidi, hz);
        }
        if ((utcNow - _lastVoiced).TotalMilliseconds > SilenceMs)
        {
            _smoothedMidi = double.NaN;
            return Cleared(stringsChanged, PlayNoteHint);
        }
        return Unchanged(stringsChanged);
    }

    /// <summary>Horizontal position of a cents value on a gauge <paramref name="width"/> pixels wide: -50 at the left margin, +50 at the right.</summary>
    public static double NeedleX(double cents, double width) =>
        NeedleMarginPx + (Math.Clamp(cents, -NeedleRangeCents, NeedleRangeCents) + NeedleRangeCents) / 100 * (width - 2 * NeedleMarginPx);

    /// <summary>The needle's colour rule: in tune within 5 cents, close within 20, far beyond; none for NaN (nothing heard).</summary>
    public static TunerTone ToneForCents(double cents)
    {
        if (double.IsNaN(cents)) return TunerTone.None;
        var off = Math.Abs(cents);
        return off <= InTuneCents ? TunerTone.InTune : off <= CloseCents ? TunerTone.Close : TunerTone.Far;
    }

    private void SetStrings(TrackModel? track)
    {
        _shownTrack = track;
        _tunings = track is null || track.Kind == TrackKind.Drums ? Array.Empty<int>() : track.StringTunings.ToArray();
        _stringNames.Clear();
        foreach (var midi in _tunings) _stringNames.Add(NoteName(midi));
        _lastSeq = -1;   // the next reading is shown even if the engine has not moved on
    }

    private TunerFrame Reading(bool stringsChanged, double midi, double hz)
    {
        var nearest = (int)Math.Round(midi);
        var cents = (midi - nearest) * 100;
        return new TunerFrame(true, stringsChanged, "", NoteName(nearest), DetailText(hz, cents), cents, StringAt(midi));
    }

    private int StringAt(double midi)
    {
        var best = -1;
        var bestDistance = StringMatchSemitones;
        for (var i = 0; i < _tunings.Length; i++)
        {
            var distance = Math.Abs(midi - _tunings[i]);
            if (distance < bestDistance) { bestDistance = distance; best = i; }
        }
        return best;
    }

    /// <summary>Name of a MIDI note; notes 0..127 come from the cache, others are built each time (they are not played notes).</summary>
    private string NoteName(int midi)
    {
        if (midi < 0 || midi >= _noteNames.Length) return MusicTheoryService.NoteName(midi);
        return _noteNames[midi] ??= MusicTheoryService.NoteName(midi);
    }

    /// <summary>"442.5 Hz   +12 cents". The text is written into a stack buffer and compared with the last one, so a steady display allocates nothing.</summary>
    private string DetailText(double hz, double cents)
    {
        Span<char> buffer = stackalloc char[64];
        if (!TryWriteDetail(buffer, hz, cents, out var length)) return _detailText = FormatDetail(hz, cents);
        if (!buffer[..length].SequenceEqual(_detailText.AsSpan())) _detailText = new string(buffer[..length]);
        return _detailText;
    }

    private static bool TryWriteDetail(Span<char> buffer, double hz, double cents, out int length)
    {
        length = 0;
        if (!hz.TryFormat(buffer, out var hzLength, "0.0", CultureInfo.InvariantCulture)) return false;
        length = hzLength;
        " Hz   ".AsSpan().CopyTo(buffer[length..]);
        length += 6;
        if (!cents.TryFormat(buffer[length..], out var centsLength, "+0;-0;0", CultureInfo.InvariantCulture)) return false;
        length += centsLength;
        " cents".AsSpan().CopyTo(buffer[length..]);
        length += 6;
        return true;
    }

    private static string FormatDetail(double hz, double cents) =>
        string.Create(CultureInfo.InvariantCulture, $"{hz:0.0} Hz   {cents:+0;-0;0} cents");

    private static TunerFrame Cleared(bool stringsChanged, string hint) =>
        new(true, stringsChanged, hint, "--", "", double.NaN, -1);

    private static TunerFrame Unchanged(bool stringsChanged) =>
        new(false, stringsChanged, "", "", "", double.NaN, -1);
}
