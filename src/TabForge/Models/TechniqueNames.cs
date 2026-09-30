namespace TabForge.Models;

/// <summary>
/// Identifiers stored in <see cref="TabNote.Techniques"/>. They are persisted as text in .tforge files, so
/// the values must never change; use these constants instead of retyping the strings.
/// </summary>
public static class TechniqueNames
{
    public const string PalmMute = "PalmMute";
    /// <summary>Legacy short form of <see cref="PalmMute"/> still accepted on read.</summary>
    public const string PalmMuteLegacy = "PM";
    public const string LetRing = "LetRing";
    public const string Hopo = "HOPO";
    public const string HopoOrigin = "HOPOOrigin";
    public const string HopoDestination = "HOPODestination";
    public const string Bend = "Bend";
    public const string LegatoSlide = "LegatoSlide";
    public const string ShiftSlide = "ShiftSlide";
    public const string Vibrato = "Vibrato";
    public const string WideVibrato = "WideVibrato";
    public const string TremoloBar = "TremBar";
    public const string Harmonic = "Harmonic";
    public const string ArtificialHarmonic = "ArtificialHarmonic";
    public const string Tapping = "Tapping";
    public const string Slap = "Slap";
    public const string Pop = "Pop";
    public const string Trill = "Trill";
    public const string TremoloPick = "TremoloPick";
    public const string FadeIn = "FadeIn";
    public const string FadeOut = "FadeOut";
    public const string WahOpen = "WahOpen";
    public const string WahClose = "WahClose";
    public const string BrushDown = "BrushDown";
    public const string BrushUp = "BrushUp";
    public const string ArpeggioDown = "ArpeggioDown";
    public const string ArpeggioUp = "ArpeggioUp";

    public static bool IsPalmMute(string technique) =>
        technique.Equals(PalmMute, StringComparison.OrdinalIgnoreCase) ||
        technique.Equals(PalmMuteLegacy, StringComparison.OrdinalIgnoreCase);

    /// <summary>The note set is case-insensitive (see <see cref="TabNote.Techniques"/>).</summary>
    public static bool HasPalmMute(ISet<string> techniques) =>
        techniques.Contains(PalmMute) || techniques.Contains(PalmMuteLegacy);
}
