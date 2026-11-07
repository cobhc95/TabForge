namespace TabForge.Sf2Synth;

// Owns: one MIDI channel's state: controllers, program and bank, pitch bend, channel pressure, RPN 0/1/2 (bend range, fine and
//   coarse tune), and the controller values modulators read (§8.2.1 sources). Version bumps whenever a modulator input changes.
// Does not own: voices (Synthesizer, Voice).
// Tests: Sf2SynthTests.
internal sealed class Channel(bool drum)
{
    public readonly byte[] Cc = new byte[128];
    public int Program, Bank, Bend, Pressure, RpnMsb, RpnLsb, Version;
    public float BendRange, TuneCents;
    public bool Drum = drum;
    private int _fine, _coarse;

    public bool Sustain => Cc[64] >= 64;

    public void Reset()
    {
        Array.Clear(Cc);
        (Program, Bank, _fine, _coarse, TuneCents) = (0, 0, 8192, 0, 0);
        Cc[7] = 100; Cc[10] = 64;
        ResetControllers();
    }

    // RP-015: controllers back to default, volume, pan and program kept.
    public void ResetControllers()
    {
        Cc[1] = 0; Cc[11] = 127; Cc[64] = Cc[65] = Cc[66] = Cc[67] = 0;
        (Bend, Pressure, RpnMsb, RpnLsb, BendRange) = (8192, 0, 127, 127, 2);
        Version++;
    }

    public void Controller(int cc, int v)
    {
        Cc[cc & 127] = (byte)(v & 127);
        switch (cc)
        {
            case 0: Bank = v; break;
            case 101: RpnMsb = v; break;
            case 100: RpnLsb = v; break;
            case 6 or 38 when RpnMsb == 0: DataEntry(cc == 6, v); break;
            case 121: ResetControllers(); break;
        }
        Version++;
    }

    private void DataEntry(bool msb, int v)
    {
        switch (RpnLsb)
        {
            case 0 when msb: BendRange = v + BendRange % 1; break;
            case 0: BendRange = MathF.Floor(BendRange) + v / 100f; break;
            case 1: _fine = msb ? (v << 7) | (_fine & 127) : (_fine & ~127) | v; break;
            case 2 when msb: _coarse = v - 64; break;
        }
        TuneCents = _coarse * 100 + (_fine - 8192) / 8192f * 100;
    }

    // Non-CC sources of §8.2.1 for a voice's key and velocity, normalised to 0..1.
    public float Source(int index, bool cc, int key, int vel) => cc ? Cc[index] / 127f : index switch
    {
        0 => 1,
        2 => vel / 127f,
        3 => key / 127f,
        13 => Pressure / 127f,
        14 => Bend / 16384f,
        16 => BendRange / 127f,
        _ => 0,
    };
}
