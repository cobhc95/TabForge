namespace TabForge.Services;

// Owns: one drum-map row (GM note, TAB line, label, staff step, notehead) and its copy.
// Does not own: the presets and default rows (DrumMaps, in the app) and drum playback.
// Tests: InstrumentChoiceTests.
/// <summary>
/// How one GM percussion sound is shown: TAB line and label, staff position and notehead.
/// StaffStep counts half-spaces down from the top staff line (0 = top line F5, 7 = F4 space, negative = above).
/// </summary>
public sealed class DrumMapEntry
{
    public int Midi { get; set; }
    public int TabLine { get; set; }
    public string Label { get; set; } = "";
    public int StaffStep { get; set; } = 3;
    /// <summary>"x" (cymbals, hi-hats), "normal" (drums), "circle" (open hi-hat), "diamond" (bells).</summary>
    public string Head { get; set; } = "normal";
    public DrumMapEntry Clone() => (DrumMapEntry)MemberwiseClone();
}
