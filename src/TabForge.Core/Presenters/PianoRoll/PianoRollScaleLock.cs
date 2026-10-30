namespace TabForge.Presenters;

// Owns: the piano roll's scale lock: snapping a pitch to the nearest pitch of the current scale (drawn, moved, transposed and step-input
//       notes stay in key), and diatonic transposition (up / down by scale steps; semitones when there is no scale).
// Does not own: the scale table (PianoRollScale), the settings (EditingSettings) or the commit (PianoRollPresenter).
// Tests: TabForge.Avalonia.Tests.PianoRollScaleLockTests.
public static class PianoRollScaleLock
{
    public const string TransposeSemitones = "Semitones";
    public const string TransposeScaleSteps = "Scale steps";
    public static IReadOnlyList<string> TransposeModes { get; } = new[] { TransposeSemitones, TransposeScaleSteps };

    public const string StepOff = "Off", StepSnap = "Snap", StepOnlyScale = "Only scale notes";
    public static IReadOnlyList<string> StepFilters { get; } = new[] { StepOff, StepSnap, StepOnlyScale };

    /// <summary>Step input's own scale filter: the pitch a key writes (Off: as pressed; Snap: the nearest scale note; Only scale notes: null for a key outside the scale, which is ignored). No scale: as pressed.</summary>
    public static int? StepPitch(int pitch, IReadOnlySet<int>? scale, string? filter)
    {
        if (scale is null || scale.Count == 0 || filter is null or StepOff) return pitch;
        if (filter == StepOnlyScale) return scale.Contains(((pitch % 12) + 12) % 12) ? pitch : null;
        return Snap(pitch, scale);
    }

    /// <summary>
    /// Snaps <paramref name="pitch"/> to the nearest pitch whose class is in <paramref name="scale"/> (0 = C). A pitch already in the scale
    /// stays; a null or empty scale keeps the pitch. Ties go up with <paramref name="direction"/> >= 0, down below.
    /// </summary>
    public static int Snap(int pitch, IReadOnlySet<int>? scale, int direction = 0)
    {
        pitch = PianoRollEdits.ClampPitch(pitch);
        if (scale is null || scale.Count == 0 || scale.Contains(pitch % 12)) return pitch;
        for (var distance = 1; distance <= 12; distance++)
        {
            var up = pitch + distance <= MidiLimits.Max && scale.Contains((pitch + distance) % 12);
            var down = pitch - distance >= 0 && scale.Contains((pitch - distance) % 12);
            if (up && (!down || direction >= 0)) return pitch + distance;
            if (down) return pitch - distance;
        }
        return pitch;
    }

    /// <summary>
    /// Moves <paramref name="pitch"/> by <paramref name="steps"/> scale degrees of <paramref name="scale"/> (each step lands on the next
    /// in-scale pitch up or down); a null or empty scale moves by semitones. Running past either end of the MIDI range clamps.
    /// </summary>
    public static int Step(int pitch, IReadOnlySet<int>? scale, int steps)
    {
        pitch = PianoRollEdits.ClampPitch(pitch);
        if (scale is null || scale.Count == 0) return PianoRollEdits.ClampPitch(pitch + steps);
        var at = pitch;
        var sign = Math.Sign(steps);
        for (var s = 0; s < Math.Abs(steps); s++)
        {
            var next = at;
            do
            {
                next += sign;
                if (next < 0 || next > MidiLimits.Max) return at;
            } while (!scale.Contains(next % 12));
            at = next;
        }
        return at;
    }
}
