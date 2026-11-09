namespace TabForge.Sf2Synth.Humanize;

// Owns: the humanizer's input (one written note: its time, key, velocity, string, place in the bar, meter, tempo, stroke and the
//   written techniques), its output (the played note) and the diagnostic split of a stroke's level into named parts.
// Does not own: building notes from a song (the host), the model (Humanizer, Dynamics, Timing).
// Tests: HumanizerTests.

/// <summary>Written techniques a note carries.</summary>
[Flags]
public enum Marks : ushort { None = 0, PalmMute = 1, Legato = 2, Slide = 4, Ghost = 8, Accent = 16, Dead = 32, Up = 64, Down = 128 }

/// <summary>
/// A written note. <see cref="Bar"/> is an absolute key (the performed bar: the passes of a repeat differ), <see cref="Section"/> the
/// bar its section starts on, <see cref="Beat"/> its place in the bar in beats of the meter (<see cref="BeatsPerBar"/> beats of
/// <see cref="BeatMs"/>, one beat = a 1/<see cref="BeatUnit"/> note), <see cref="Stroke"/> equal for the notes struck together,
/// <see cref="String"/> -1 when unknown.
/// </summary>
public record struct HumanNote(double OnMs, double OffMs, int Key, int Velocity, int String, int Bar, int Section,
    double Beat, int BeatsPerBar, int BeatUnit, double BeatMs, long Stroke, Marks Marks);

/// <summary>A played note: when it starts and ends, how hard.</summary>
public record struct HumanOut(double OnMs, double OffMs, int Velocity);

/// <summary>A stroke's level parts in dB before the feel's scale. All but <see cref="Residue"/> follow from the music.</summary>
public record struct LevelParts(double Meter, double Sync, double Length, double Contour, double Arch, double Energy,
    double Hand, double Group, double Motif, double Residue)
{
    public readonly double Structural => Meter + Sync + Length + Contour + Arch + Energy + Hand + Group + Motif;
}
