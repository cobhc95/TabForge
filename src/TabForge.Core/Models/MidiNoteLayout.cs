namespace TabForge.Models;

// Owns: the pure maths of the mini piano roll drawn inside a MIDI clip (and the live MIDI take): the pitch rows over the clip height and the velocity opacity.
// Does not own: the drawing (the app's MidiNotePainter) or the note data (AudioClip.Notes).
// Tests: TabForge.Avalonia.Tests.MidiNoteLayoutTests.

/// <summary>The pitch range of a clip's notes, lowest to highest, both included.</summary>
public readonly record struct MidiPitchRange(int Low, int High)
{
    /// <summary>How many semitone rows the range spans (C3..G3 = 8).</summary>
    public int Rows => High - Low + 1;
}

/// <summary>Maps notes to rectangles: pitch to a row of the clip height, velocity to opacity.</summary>
public static class MidiNoteLayout
{
    /// <summary>The faintest note opacity (velocity 0).</summary>
    public const double MinOpacity = 0.35;
    public const int MaxVelocity = MidiLimits.Max;
    /// <summary>The narrowest a note is drawn, in pixels.</summary>
    public const double MinNoteWidth = 2;

    /// <summary>Velocity 0..127 to opacity 0.35..1.0.</summary>
    public static double OpacityOf(int velocity) => MinOpacity + (1 - MinOpacity) * Math.Clamp(velocity, 0, MaxVelocity) / MaxVelocity;

    /// <summary>The top of the row of <paramref name="pitch"/> inside <paramref name="height"/> (high pitches on top) and the row height; a row is at least 1 px tall.</summary>
    public static (double Y, double Height) RowOf(int pitch, MidiPitchRange range, double height)
    {
        var row = height / Math.Max(1, range.Rows);
        return ((range.High - Math.Clamp(pitch, range.Low, range.High)) * row, Math.Max(1, row));
    }

    /// <summary>The width of a note from <paramref name="x1"/> to <paramref name="x2"/>, never under <see cref="MinNoteWidth"/>.</summary>
    public static double WidthOf(double x1, double x2) => Math.Max(MinNoteWidth, x2 - x1);
}
