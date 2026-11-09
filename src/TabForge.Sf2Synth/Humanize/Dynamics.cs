using static TabForge.Sf2Synth.Humanize.HumanTable;

namespace TabForge.Sf2Synth.Humanize;

// Owns: a stroke's level in dB as named parts, every one but the residue following from the music: meter position (strong and weak
//   beats from the real time signature, compound meters in threes), syncopation, note length, melodic peaks and rising lines,
//   phrase arch from the section start, energy (strokes per beat over the window), pick hand and string crossing, run grouping,
//   motif memory (the same beat content gets the same offsets), legato finger unevenness; and the slow bounded residue. Then the
//   feel's scale, a soft ceiling and the cap. Ghost and dead notes keep half of it, palm-mute chugs half the motif and residue.
// Does not own: the context (Humanizer), timing (Timing), the values (HumanTable).
// Tests: HumanizerTests.
internal static class Dynamics
{
    public static LevelParts Parts(in Ctx c, in HumanNote n, Family f, uint seed)
    {
        var meter = Meter(n.Beat, n.BeatsPerBar, n.BeatUnit) * (c.Fast ? RunMeterK : 1);
        var sync = Weak(n.Beat, n.BeatsPerBar, n.BeatUnit) && double.IsFinite(c.IoiNext) && n.Beat + c.IoiNext / n.BeatMs > NextPulse(n.Beat, n.BeatsPerBar, n.BeatUnit) + 0.05 ? SyncDb : 0;
        var held = double.IsFinite(c.IoiNext) ? c.IoiNext : n.OffMs - n.OnMs;
        var length = LenDbPerOct * Math.Clamp(Math.Log2(Math.Max(1, held) / n.BeatMs), -2, 2);
        double contour = 0;
        if (c.Single && c.PrevKey >= 0)
        {
            if (c.NextKey >= 0 && c.Key > c.PrevKey && c.Key >= c.NextKey) contour += PeakDb;
            contour += RiseDb * Math.Clamp((c.Key - (c.Prev2Key >= 0 ? c.Prev2Key : c.PrevKey)) / 12.0, -1, 1);
        }
        var x = (((n.Bar - n.Section) % ArchBars + ArchBars) % ArchBars + n.Beat / Math.Max(1, n.BeatsPerBar)) / ArchBars;
        var arch = ArchDb * (x < 0.75 ? x / 0.375 - 1 : 1 - (x - 0.75) / 0.125);
        var energy = EnergyDb * Math.Clamp(Math.Log2(Math.Max(0.01, c.Npb) / EnergyRef), -1.5, 1.5);
        var hand = c.Legato ? 0 : f.AltDb > 0 ? c.Down ? f.AltDb : -f.AltDb : c.Down ? 0 : UpDb;
        if (c.PrevString >= 0 && c.String >= 0 && c.String != c.PrevString) hand += CrossDb;
        var group = c.Fast && (int)Math.Round(n.Beat * c.Sub) % c.Group == 0 ? GroupDb * (c.Group == 2 ? 0.6 : 1) : 0;
        var motif = MotifDb * Hash.G(Hash.Of(c.Motif, c.MotifIndex, 1)) + (c.Legato ? LegatoDb * Hash.G(Hash.Of(c.Motif, c.MotifIndex, 2)) : 0);
        var residue = ResidueDb * Math.Tanh(Hash.Octaves(Hash.Of(seed, 0xD1), n.Bar + n.Beat / Math.Max(1, n.BeatsPerBar), 0.5, 4, 0.5));
        if (c.Chug) (motif, residue) = (motif * ChugK, residue * ChugK);
        var p = new LevelParts(meter, sync, length, contour, arch, energy, hand, group, motif, residue);
        return c.Soft ? new(meter * SoftK, sync * SoftK, length * SoftK, contour * SoftK, arch * SoftK, energy * SoftK, hand * SoftK, group * SoftK, motif * SoftK, residue * SoftK) : p;
    }

    /// <summary>The level change in dB: the parts scaled by the feel and family, a 3:1 ceiling above the knee, the cap.</summary>
    public static double Total(in LevelParts p, Style s, Family f)
    {
        var db = (p.Structural + p.Residue) * s.KDb * f.LevelK;
        if (db > KneeDb) db = KneeDb + (db - KneeDb) / Ratio;
        return Math.Clamp(db, -CapDb, CapDb);
    }

    // Pulse = beat, or three beats of a compound meter (6/8, 9/8, 12/8).
    private static (double Pulse, int Pulses) Pulse(int num, int den) => den >= 8 && num % 3 == 0 && num > 3 ? (3, num / 3) : (1, Math.Max(1, num));

    private static double Meter(double beat, int num, int den)
    {
        var (pulse, pulses) = Pulse(num, den);
        var p = beat / pulse;
        var i = (int)Math.Round(p);
        var frac = Math.Abs(p - i);
        if (frac < 0.01) return i == 0 ? DownDb : pulses % 2 == 0 && i == pulses / 2 ? MidDb : BeatDb;
        if (pulse == 3) return Math.Abs(beat - Math.Round(beat)) < 0.01 ? EighthDb : SixteenthDb;   // the inner beats of a compound pulse
        var t = p * 3;   // triplets sit with the 8ths
        return Math.Abs(frac - 0.5) < 0.01 || Math.Abs(t - Math.Round(t)) < 0.03 ? EighthDb : SixteenthDb;
    }

    private static bool Weak(double beat, int num, int den)
    {
        var p = beat / Pulse(num, den).Pulse;
        return Math.Abs(p - Math.Round(p)) >= 0.01;
    }

    private static double NextPulse(double beat, int num, int den)
    {
        var pulse = Pulse(num, den).Pulse;
        return (Math.Floor(beat / pulse + 1e-6) + 1) * pulse;
    }
}
