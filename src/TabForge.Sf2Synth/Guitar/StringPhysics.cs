namespace TabForge.Sf2Synth.Guitar;

// Owns: the physical strings of a standard-tuned guitar (6, 10-46 set) and five-string bass (B0-E1-A1-D1-G1, 45-130 set): which string plays a note
//   (thinnest string at or below it), the fretted vibrating length, the stiffness (inharmonicity B = π³Ed⁴/64TL², core diameter)
//   and the dispersion all-pass coefficient that gives it.
// Does not own: the waveguide (PluckedString), sound values per program (Voicings).
// Tests: GuitarLayerTests.
internal static class StringPhysics
{
    private const double Steel = 2e11, Inch = 0.0254, MaxDispersion = 0.35, DispersionPerRootB = 12;
    public const double GuitarScale = 0.648, BassScale = 0.864;

    // Open MIDI note, core diameter (inch), tension (N).
    private static readonly (int Open, double Core, double Tension)[] Guitar =
        [(40, .017, 78), (45, .016, 85), (50, .014, 82), (55, .017, 72), (59, .013, 71), (64, .010, 72)];
    private static readonly (int Open, double Core, double Tension)[] Bass = [(23, .040, 165), (28, .035, 190), (33, .030, 190), (38, .025, 180), (43, .020, 170)];

    public readonly record struct Fretted(int String, double Length, double B)
    {
        public double Dispersion => -Math.Min(MaxDispersion, DispersionPerRootB * Math.Sqrt(B));
    }

    // str < 0: choose; else keep that string (legato).
    public static Fretted Of(int note, bool bass, int str = -1)
    {
        var set = bass ? Bass : Guitar;
        if (str < 0 || str >= set.Length)
        {
            str = 0;
            for (var i = 0; i < set.Length; i++) if (set[i].Open <= note) str = i;
        }
        var (open, core, tension) = set[str];
        var length = (bass ? BassScale : GuitarScale) * Math.Pow(2, -Math.Max(0, note - open) / 12.0);
        var d = core * Inch;
        return new(str, length, Math.Pow(Math.PI, 3) * Steel * Math.Pow(d, 4) / (64 * tension * length * length));
    }
}
