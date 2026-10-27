namespace TabForge.Services;

// Owns: the short name of a key signature for the toolbar: its tonic, with "m" for minor.
// Does not own: the bar's key data (BarSignatures) or the full key names in the status bar.
// Tests: no dedicated test class.
public static class KeyNames
{
    // Tonic by signature, index 0 = 7 flats (C♭) to index 14 = 7 sharps (C♯).
    private static readonly string[] MajorTonics = { "C♭", "G♭", "D♭", "A♭", "E♭", "B♭", "F", "C", "G", "D", "A", "E", "B", "F♯", "C♯" };
    // Relative minor tonic by signature, same indexing.
    private static readonly string[] MinorTonics = { "A♭", "E♭", "B♭", "F", "C", "G", "D", "A", "E", "B", "F♯", "C♯", "G♯", "D♯", "A♯" };

    /// <summary>The short key name: the tonic ("C", "B♭") for major, the tonic and "m" for minor ("Am", "F♯m").
    /// The signature is the number of sharps (negative: flats), clamped to 7 either way.</summary>
    public static string Short(int signature, bool minor)
    {
        var i = Math.Clamp(signature, -7, 7) + 7;
        return minor ? MinorTonics[i] + "m" : MajorTonics[i];
    }
}
