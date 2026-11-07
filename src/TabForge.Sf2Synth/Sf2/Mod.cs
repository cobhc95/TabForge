namespace TabForge.Sf2Synth.Sf2;

// Owns: one SF2 modulator, the source mapping (direction, polarity, linear/concave/convex/switch, §8.2.1) and the default list (§8.4).
// Does not own: reading controller values or summing results (Synth/Voice.Mods).
// Tests: Sf2SynthTests.
internal readonly record struct Mod(ushort Src, ushort Dest, short Amount, ushort AmtSrc, ushort Transform)
{
    public bool SameAs(in Mod o) => Src == o.Src && Dest == o.Dest && AmtSrc == o.AmtSrc && Transform == o.Transform;

    public static readonly Mod[] Defaults =
    [
        new(0x0502, 48, 960, 0, 0),      // velocity -> attenuation, concave
        new(0x0102, 8, -2400, 0, 0),     // velocity -> filter cutoff
        new(0x000D, 6, 50, 0, 0),        // channel pressure -> vibrato depth
        new(0x0081, 6, 50, 0, 0),        // CC1 -> vibrato depth
        new(0x0587, 48, 960, 0, 0),      // CC7 -> attenuation, concave
        new(0x028A, 17, 1000, 0, 0),     // CC10 -> pan
        new(0x058B, 48, 960, 0, 0),      // CC11 -> attenuation, concave
        new(0x00DB, 16, 200, 0, 0),      // CC91 -> reverb send
        new(0x00DD, 15, 200, 0, 0),      // CC93 -> chorus send
        new(0x020E, 59, 12700, 0x0010, 0), // pitch wheel × sensitivity -> pitch
    ];

    // x in 0..1 -> mapped value (unipolar 0..1 or bipolar -1..1).
    public static float Map(ushort src, float x)
    {
        if ((src & 0x100) != 0) x = 1 - x;
        var y = (src >> 10) switch
        {
            1 => x >= 1 ? 1 : MathF.Min(1, -(40f / 96) * MathF.Log10(1 - x)),
            2 => x <= 0 ? 0 : MathF.Max(0, 1 + (40f / 96) * MathF.Log10(x)),
            3 => x >= 0.5f ? 1 : 0,
            _ => x,
        };
        return (src & 0x200) != 0 ? 2 * y - 1 : y;
    }
}
