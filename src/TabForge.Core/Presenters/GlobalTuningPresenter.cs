// Owns: the six-string tuning edited in the global tuning window, without UI types: nudging one string or all strings, shifting by typed semitones, resetting to standard E, applying a preset, the preset the tuning matches, and each string's note, thickness and offset text.
// Does not own: the window and its controls, the event suppression while the window refreshes, the dialog result, or the preset catalogue (the window passes the presets in).
// Tests: no dedicated test class.
namespace TabForge.Presenters;

/// <summary>
/// Global tuning state. The tuning is six MIDI note numbers, high to low; values stay within 0 to 127 (a nudge stops at the
/// ends). <see cref="PresetChoices"/> lists the preset names followed by "Custom"; a tuning that matches no preset selects "Custom".
/// Thread: UI thread only (it is driven by the global tuning window).
/// </summary>
public sealed class GlobalTuningPresenter
{
    /// <summary>Standard E tuning, high to low: the reset target.</summary>
    public static readonly int[] StandardE = { 64, 59, 55, 50, 45, 40 };

    /// <summary>Number of strings the reference tuning has.</summary>
    private const int StringCount = 6;

    private const int MaxSemitoneShift = 24;
    private const string CustomChoice = "Custom";

    private readonly List<(string Name, int[] HighToLow)> _presets;
    private int[] _tuning;

    public GlobalTuningPresenter(int[] current, IEnumerable<(string Name, int[] HighToLow)> presets)
    {
        _tuning = (int[])current.Clone();
        _presets = presets.ToList();
    }

    /// <summary>The current tuning, high to low (a copy).</summary>
    public int[] Tuning => (int[])_tuning.Clone();

    /// <summary>The preset names, then "Custom" as the last choice.</summary>
    public IReadOnlyList<string> PresetChoices => _presets.Select(p => p.Name).Append(CustomChoice).ToList();

    /// <summary>The index in <see cref="PresetChoices"/> of the preset the tuning matches, or the "Custom" index when none does.</summary>
    public int MatchingPresetIndex
    {
        get
        {
            var match = _presets.FindIndex(p => p.HighToLow.SequenceEqual(_tuning));
            return match >= 0 ? match : _presets.Count;
        }
    }

    /// <summary>Moves one string by <paramref name="delta"/> (a single step), stopping at 0 and 127.</summary>
    public void NudgeString(int index, int delta) => _tuning[index] = Nudge(_tuning[index], delta);

    /// <summary>Moves every string by <paramref name="delta"/> (a single step), stopping at 0 and 127.</summary>
    public void NudgeAll(int delta)
    {
        for (var s = 0; s < StringCount; s++) _tuning[s] = Nudge(_tuning[s], delta);
    }

    /// <summary>
    /// Shifts every string by the semitones typed in <paramref name="text"/> (an optional leading "+", clamped to -24 to 24).
    /// Returns false, and changes nothing, when the text is not a whole number.
    /// </summary>
    public bool TryShiftAll(string text)
    {
        if (!int.TryParse(text.Trim().TrimStart('+'), out var semitones)) return false;
        semitones = Math.Clamp(semitones, -MaxSemitoneShift, MaxSemitoneShift);
        for (var s = 0; s < StringCount; s++) _tuning[s] = Math.Clamp(_tuning[s] + semitones, 0, MidiLimits.Max);
        return true;
    }

    /// <summary>Sets the tuning to standard E.</summary>
    public void ResetToStandard() => _tuning = (int[])StandardE.Clone();

    /// <summary>Sets the tuning to the preset at <paramref name="index"/>. Returns false, and changes nothing, for an index that is not a preset (such as "Custom").</summary>
    public bool ApplyPreset(int index)
    {
        if (index < 0 || index >= _presets.Count) return false;
        _tuning = (int[])_presets[index].HighToLow.Clone();
        return true;
    }

    /// <summary>The note name of a string, such as "E2".</summary>
    public string NoteOf(int index) => Services.MusicTheoryService.NoteName(_tuning[index]);

    /// <summary>The drawn thickness of a string: thicker for lower pitches, between 1 and 5.</summary>
    public double StringThickness(int index) => Math.Clamp(1 + (70 - _tuning[index]) / 9.0, 1, 5);

    /// <summary>How far a string sits from standard E: "standard", "+2 from standard" or "-1 from standard".</summary>
    public string OffsetText(int index)
    {
        var offset = _tuning[index] - StandardE[index];
        return offset == 0 ? "standard" : offset > 0 ? $"+{offset} from standard" : $"{offset} from standard";
    }

    private static int Nudge(int value, int delta) => delta < 0 ? Math.Max(0, value + delta) : Math.Min(MidiLimits.Max, value + delta);
}
