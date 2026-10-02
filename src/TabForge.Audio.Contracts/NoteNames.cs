namespace TabForge.Audio.Contracts;

/// <summary>
/// The names of MIDI notes, shared by the application and the audio engine. Octaves are numbered so that MIDI note 0 is C-1 and
/// note 60 is C4. The sharp spelling is the default; the marker spelling (D#, G# and A# written as Eb, Ab and Bb) is what a
/// fretboard marker shows.
/// </summary>
public static class NoteNames
{
    private static readonly string[] Sharps = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    private static readonly string[] Marker = { "C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B" };

    /// <summary>A new array of the twelve sharp pitch-class names, C first.</summary>
    public static string[] SharpPitchClasses() => (string[])Sharps.Clone();

    /// <summary>Pitch class name (sharp spelling) of any MIDI note number, also below 0 and above 127.</summary>
    public static string PitchClass(int midi) => Sharps[PitchClassIndex(midi)];

    /// <summary>Pitch class name in the marker spelling.</summary>
    public static string MarkerPitchClass(int midi) => Marker[PitchClassIndex(midi)];

    /// <summary>Note name with octave in the sharp spelling: 60 is "C4", 69 is "A4", 0 is "C-1".</summary>
    public static string Name(int midi) => PitchClass(midi) + (midi / 12 - 1);

    private static int PitchClassIndex(int midi) => ((midi % 12) + 12) % 12;
}
