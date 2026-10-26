using System.Globalization;

namespace TabForge.Presenters;

// Owns: the info line's text for positions and lengths (bar.beat.16th.tick, 120 ticks to a sixteenth, as the big DAWs show them), the pitch
//       names (C4 = MIDI 60) and reading typed values back.
// Does not own: the info line itself (the view) or the clock (PianoRollTimeMap).
// Tests: TabForge.Avalonia.Tests.PianoRollEditTests.
public static class PianoRollPositionText
{
    /// <summary>Ticks in one sixteenth note.</summary>
    public const int TicksPerSixteenth = 120;

    private static readonly IReadOnlyList<string> Names = new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    /// <summary>The note name of a MIDI pitch: C4 is 60.</summary>
    public static string PitchName(int pitch) => Names[((pitch % 12) + 12) % 12] + (pitch / 12 - 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>A key strip row's name: the drum's General MIDI name on a drum track (where one exists), else the note name ("C#4").</summary>
    public static string KeyName(int pitch, bool drums) =>
        drums && pitch >= PercussionKeyMapRules.FirstNote && pitch - PercussionKeyMapRules.FirstNote < PercussionKeyMapRules.Names.Count
            ? PercussionKeyMapRules.Names[pitch - PercussionKeyMapRules.FirstNote] : PitchName(pitch);

    /// <summary>A short position as Reaper shows it: bar.beat.hundredths of the beat ("12.3.74").</summary>
    public static string Short(PianoRollTimeMap map, double beat)
    {
        var bar = map.BarAtBeat(beat);
        var inBar = Math.Max(0, beat - bar.StartBeat);
        var whole = (int)Math.Floor(inBar + 1e-9);
        var hundredths = Math.Clamp((int)Math.Floor((inBar - whole) * 100 + 1e-9), 0, 99);
        return string.Create(CultureInfo.InvariantCulture, $"{bar.Bar + 1}.{whole + 1}.{hundredths:00}");
    }

    /// <summary>The hover readout: the position and the note name with, over a note, its velocity ("12.3.74 | F#3 54").</summary>
    public static string Readout(string position, int pitch, int? velocity, bool drums) => position + " | " + Readout(pitch, velocity, drums);

    /// <summary>The hover readout: the note name and, over a note, its velocity ("C#5 73").</summary>
    public static string Readout(int pitch, int? velocity, bool drums) =>
        velocity is { } v ? KeyName(pitch, drums) + " " + v.ToString(CultureInfo.InvariantCulture) : KeyName(pitch, drums);

    /// <summary>The MIDI pitch of a name such as "C4", "F#3" or "Bb2", or a plain number; null when it is neither.</summary>
    public static int? ParsePitch(string text)
    {
        var t = text.Trim();
        if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) return PianoRollEdits.ClampPitch(number);
        if (t.Length < 2) return null;
        var index = IndexOfName(char.ToUpperInvariant(t[0]).ToString());
        if (index < 0) return null;
        var rest = t[1..];
        if (rest.StartsWith('#')) { index++; rest = rest[1..]; }
        else if (rest.StartsWith('b')) { index--; rest = rest[1..]; }
        if (!int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out var octave)) return null;
        return PianoRollEdits.ClampPitch((octave + 1) * 12 + index);
    }

    private static int IndexOfName(string name)
    {
        for (var i = 0; i < Names.Count; i++) if (Names[i] == name) return i;
        return -1;
    }

    /// <summary>A position as bar.beat.16th.tick (all from 1 but the tick), the bar numbered as the score numbers it.</summary>
    public static string Position(PianoRollTimeMap map, double beat)
    {
        var bar = map.BarAtBeat(beat);
        return (bar.Bar + 1).ToString(CultureInfo.InvariantCulture) + "." + Parts(beat - bar.StartBeat, 1);
    }

    /// <summary>A length as beats.16th.tick (beats and sixteenths from 0).</summary>
    public static string Length(double beats) => Parts(beats, 0);

    private static string Parts(double beats, int origin)
    {
        var ticks = (long)Math.Round(Math.Max(0, beats) * 4 * TicksPerSixteenth);
        var beat = ticks / (4 * TicksPerSixteenth);
        var sixteenth = ticks / TicksPerSixteenth % 4;
        var tick = ticks % TicksPerSixteenth;
        return string.Create(CultureInfo.InvariantCulture, $"{beat + origin}.{sixteenth + origin}.{tick}");
    }

    /// <summary>A typed relative value ("+12", "-5", "+1.2" for beats.16ths): its signed amount, or null when the text is not relative.</summary>
    public static double? ParseDelta(string text, bool beats)
    {
        var t = text.Trim();
        if (t.Length < 2 || t[0] is not ('+' or '-')) return null;
        var sign = t[0] == '-' ? -1 : 1;
        var rest = t[1..].Trim();
        if (beats) return ParseLength(rest) is { } amount ? sign * amount : null;
        return double.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? sign * value : null;
    }

    /// <summary>The beat of a typed position (bar.beat.16th.tick, later parts optional); null when it does not read or names no bar.</summary>
    public static double? ParsePosition(PianoRollTimeMap map, string text)
    {
        var parts = Numbers(text);
        if (parts is null || parts.Length == 0) return null;
        var barNumber = (int)parts[0] - 1;
        var bar = map.Bars.FirstOrDefault(b => b.Bar == barNumber);
        if (bar.Beats <= 0) return null;
        return bar.StartBeat + Offset(parts, 1, 1);
    }

    /// <summary>The beats of a typed length (beats.16th.tick, later parts optional); null when it does not read or is not above zero.</summary>
    public static double? ParseLength(string text)
    {
        var parts = Numbers(text);
        if (parts is null || parts.Length == 0) return null;
        var beats = Offset(parts, 0, 0);
        return beats > 0 ? beats : null;
    }

    private static double Offset(double[] parts, int first, int origin)
    {
        double Part(int i) => i < parts.Length ? parts[i] - (i - first < 2 ? origin : 0) : 0;
        return Math.Max(0, Part(first)) + Math.Max(0, Part(first + 1)) / 4 + Math.Max(0, Part(first + 2)) / (4.0 * TicksPerSixteenth);
    }

    private static double[]? Numbers(string text)
    {
        var pieces = text.Trim().Split('.', StringSplitOptions.RemoveEmptyEntries);
        var values = new double[pieces.Length];
        for (var i = 0; i < pieces.Length; i++)
            if (!double.TryParse(pieces[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])) return null;
        return values;
    }
}
