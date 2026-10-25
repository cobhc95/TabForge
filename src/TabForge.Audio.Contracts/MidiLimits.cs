namespace TabForge;

// Owns: the one MIDI data-byte maximum (127) shared by notes, velocities, controllers, programs and mixer levels.
// Does not own: other 127 meanings (fret caps, colours); those keep their own named limits.
// Tests: covered by every MIDI clamp that uses it (TabForge.Core.Tests, TabForge.AudioEngine.Tests).

/// <summary>The largest MIDI 7-bit data value.</summary>
public static class MidiLimits
{
    /// <summary>The largest MIDI note, velocity, controller value, program or mixer level.</summary>
    public const int Max = 127;
}
