namespace TabForge.Sf2Synth.Sf2;

// Owns: the parsed SF2 objects: zone (generators set, modulators, key/velocity range, link), instrument, preset, sample header.
// Does not own: parsing (Sf2Reader) or how zones combine into a voice (Synth/Voice.Setup).
// Tests: Sf2SynthTests.
internal sealed class Zone
{
    public readonly short[] Gen = new short[Gens.Count];
    public ulong Set;
    public Mod[] Mods = [];
    public int KeyLo, KeyHi = 127, VelLo, VelHi = 127;
    public int Link = -1;   // instrument (preset zone) or sample (instrument zone)

    public bool Has(int g) => ((Set >> g) & 1) != 0;
    public bool Covers(int key, int vel) => key >= KeyLo && key <= KeyHi && vel >= VelLo && vel <= VelHi;
}

internal sealed record SampleHeader(string Name, int Start, int End, int LoopStart, int LoopEnd, int Rate, int Root, int Correction);

internal sealed record Instrument(string Name, Zone? Global, Zone[] Zones);

public sealed record Preset(string Name, int Bank, int Program)
{
    internal Zone? Global { get; init; }
    internal Zone[] Zones { get; init; } = [];
}
