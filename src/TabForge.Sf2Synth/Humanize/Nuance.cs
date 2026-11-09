using static TabForge.Sf2Synth.Humanize.HumanTable;

namespace TabForge.Sf2Synth.Humanize;

// Owns: one note's voice variation, drawn from a seed of channel, key and the channel's note count (so a render repeats bit for
//   bit): excitation seed, decaying detune, brightness, pick position, release, late vibrato; and the pitch offset over the note's age.
// Does not own: when it is drawn (Humanizer), where it is applied (Voice, Guitar/PluckedString). default = no variation.
// Tests: HumanizerTests.
public readonly record struct Nuance(uint Seed, float Cents, float Bright, float Pick, float Release, float VibHz, float VibCents)
{
    public bool Moves => Cents != 0 || VibCents != 0;

    public static Nuance Of(Family p, int channel, int key, int count, bool voice, bool vibrato)
    {
        var h = (uint)(channel * 131 + key + 1) * 0x9E3779B9u ^ (uint)count * 0x85EBCA6Bu;
        h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12;
        float U() { h ^= h << 13; h ^= h >> 17; h ^= h << 5; return (h & 0xFFFFFF) / (float)0x800000 - 1; }   // -1..1
        var sign = U() < 0 ? -1 : 1;
        var cents = sign * (float)(p.DetuneCents * (0.17 + 0.83 * (0.5 + 0.5 * U())));   // 0.5..3 cents
        float bright = (float)p.Bright * U(), pick = (float)p.Pick * U(), release = (float)p.Release * U();
        float hz = (float)(VibLo + (VibHi - VibLo) * (0.5 + 0.5 * U())), depth = (float)(p.VibCents * (0.85 + 0.15 * U()));
        return voice
            ? new(h | 1, cents, bright, pick, release, vibrato ? hz : 0, vibrato ? depth : 0)
            : new(0, 0, 0, 0, 0, vibrato ? hz : 0, vibrato ? depth : 0);
    }

    /// <summary>Cents to add at this age: the detune falling away, the vibrato fading in after the onset while the note is held.</summary>
    public double Offset(double age, bool released)
    {
        var c = age < DetuneUntil ? Cents * Math.Exp(-age / DetuneSeconds) : 0;
        if (VibCents == 0 || released || age < VibOnset) return c;
        return c + VibCents * Math.Min(1, (age - VibOnset) / VibRamp) * Math.Sin(2 * Math.PI * VibHz * (age - VibOnset));
    }
}
