namespace TabForge.Playback;

/// <summary>The short playing-technique tag shown above a fretboard marker.</summary>
public static class TechniqueTag
{
    /// <summary>The most telling technique on a note as a compact tag, in priority order.</summary>
    public static string? From(ICollection<string> t)
    {
        if (t.Count == 0) return null;
        if (t.Contains("Tapping") || t.Contains("LeftTap")) return "TAP";
        if (t.Contains("TapHarmonic")) return "T.H.";
        if (t.Contains("PinchHarmonic")) return "P.H.";
        if (t.Contains("Harmonic") || t.Contains("ArtificialHarmonic") || t.Contains("SemiHarmonic")) return "HARM";
        if (t.Contains("Slap")) return "SLAP";
        if (t.Contains("Pop")) return "POP";
        if (t.Contains("HOPO") || t.Contains("Legato")) return "H/P";
        if (t.Contains("Bend")) return "BEND";
        if (t.Contains("Slide") || t.Contains("LegatoSlide") || t.Contains("ShiftSlide")) return "SLIDE";
        if (t.Contains("Trill")) return "TRILL";
        if (t.Contains("WideVibrato") || t.Contains("Vibrato")) return "VIB";
        if (t.Contains("TremoloPick")) return "TREM";
        if (t.Contains("PalmMute")) return "P.M.";
        if (t.Contains("LetRing")) return "L.R.";
        return null;
    }

    /// <summary>A chord's stroke: 1 = down (brush, arpeggio, pick stroke or rasgueado), -1 = up, 0 = none or a single note.</summary>
    public static sbyte StrumOf(IReadOnlyCollection<TabForge.Models.TabNote> notes)
    {
        if (notes.Count < 2) return 0;
        foreach (var n in notes)
            if (n.Techniques.Contains("BrushUp") || n.Techniques.Contains("ArpeggioUp") || n.Techniques.Contains("PickUp")) return -1;
        foreach (var n in notes)
            if (n.Techniques.Contains("BrushDown") || n.Techniques.Contains("ArpeggioDown") || n.Techniques.Contains("PickDown") || n.Techniques.Contains("Rasgueado")) return 1;
        return 0;
    }
}
