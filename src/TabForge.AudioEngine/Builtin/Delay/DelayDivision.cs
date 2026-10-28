namespace TabForge.AudioEngine.Builtin.Delay;

// Owns: the note lengths the delay can sync to (1/1 down to 1/32, each plain, dotted or triplet) as a number of quarter notes, and their names.
// Does not own: the tempo (the transport), turning a length into samples (DelayEffect.Process.cs).
// Tests: BuiltinDelayTests.
public static class DelayDivision
{
    /// <summary>The names in the order of the Division parameter.</summary>
    public static IReadOnlyList<string> Names { get; } =
        ["1/1", "1/2", "1/2.", "1/2T", "1/4", "1/4.", "1/4T", "1/8", "1/8.", "1/8T", "1/16", "1/16.", "1/16T", "1/32"];

    private static IReadOnlyList<double> Quarters { get; } =
        [4, 2, 3, 4.0 / 3, 1, 1.5, 2.0 / 3, 0.5, 0.75, 1.0 / 3, 0.25, 0.375, 1.0 / 6, 0.125];

    /// <summary>The index of 1/8, the default.</summary>
    public const int Eighth = 7;

    public static int Count => Names.Count;

    /// <summary>The length of a division in quarter notes (1/4 is 1, 1/8 is 0.5, 1/8. is 0.75, 1/8T is 1/3).</summary>
    public static double InQuarters(int index) => Quarters[Math.Clamp(index, 0, Quarters.Count - 1)];

    /// <summary>The length of a division in seconds at <paramref name="bpm"/> quarter notes per minute.</summary>
    public static double Seconds(int index, double bpm) => InQuarters(index) * 60.0 / bpm;
}
