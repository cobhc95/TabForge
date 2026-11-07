namespace TabForge.Sf2Synth.Sf2;

// Owns: SoundFont 2.04 generator numbers (§8.1.2), their defaults and which ones a preset zone may not set (§8.1.3).
// Does not own: reading them (Sf2Reader) or applying them (Synth/Voice).
// Tests: Sf2SynthTests.
internal enum G
{
    StartOffset = 0, EndOffset = 1, LoopStartOffset = 2, LoopEndOffset = 3, StartCoarse = 4, ModLfoToPitch = 5, VibLfoToPitch = 6,
    ModEnvToPitch = 7, FilterFc = 8, FilterQ = 9, ModLfoToFc = 10, ModEnvToFc = 11, EndCoarse = 12, ModLfoToVolume = 13,
    Chorus = 15, Reverb = 16, Pan = 17, ModLfoDelay = 21, ModLfoFreq = 22, VibLfoDelay = 23, VibLfoFreq = 24,
    ModEnvDelay = 25, ModEnvAttack = 26, ModEnvHold = 27, ModEnvDecay = 28, ModEnvSustain = 29, ModEnvRelease = 30,
    KeyToModEnvHold = 31, KeyToModEnvDecay = 32, VolEnvDelay = 33, VolEnvAttack = 34, VolEnvHold = 35, VolEnvDecay = 36,
    VolEnvSustain = 37, VolEnvRelease = 38, KeyToVolEnvHold = 39, KeyToVolEnvDecay = 40, Instrument = 41, KeyRange = 43,
    VelRange = 44, LoopStartCoarse = 45, Keynum = 46, Velocity = 47, Attenuation = 48, LoopEndCoarse = 50, CoarseTune = 51,
    FineTune = 52, SampleId = 53, SampleModes = 54, ScaleTuning = 56, ExclusiveClass = 57, RootKey = 58,
    Pitch = 59,   // not in the file: the destination of the pitch-wheel modulator (cents), as most SF2 synths do
}

internal static class Gens
{
    public const int Count = 61;

    public static readonly short[] Defaults = Build();

    private static short[] Build()
    {
        var d = new short[Count];
        foreach (var g in new[] { G.ModLfoDelay, G.VibLfoDelay, G.ModEnvDelay, G.ModEnvAttack, G.ModEnvHold, G.ModEnvDecay, G.ModEnvRelease,
                     G.VolEnvDelay, G.VolEnvAttack, G.VolEnvHold, G.VolEnvDecay, G.VolEnvRelease }) d[(int)g] = -12000;
        d[(int)G.FilterFc] = 13500;
        d[(int)G.Keynum] = d[(int)G.Velocity] = d[(int)G.RootKey] = -1;
        d[(int)G.ScaleTuning] = 100;
        return d;
    }

    public static bool PresetIgnores(int g) => g is <= 4 or 12 or 45 or 46 or 47 or 50 or 54 or 57 or 58;
}
