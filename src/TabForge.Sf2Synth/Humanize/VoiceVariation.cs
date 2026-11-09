using static TabForge.Sf2Synth.Humanize.HumanTable;

namespace TabForge.Sf2Synth.Humanize;

// Owns: the synth side of the humanizer: per channel the program, whether the song sends CC1 or pitch bend (then no added vibrato)
//   and the note count, and each note-on's Nuance from them. Allocation-free (audio thread); Reset repeats a render bit for bit.
// Does not own: the variation itself (Nuance), the note pass (Humanizer, run by the host on the written notes).
// Tests: HumanizerTests.
internal sealed class VoiceVariation
{
    private readonly int[] _program = new int[16], _count = new int[16];
    private readonly bool[] _expressive = new bool[16];

    public void Reset() { Array.Clear(_program); Array.Clear(_count); Array.Clear(_expressive); }

    public void Hear(int channel, int command, int d1, int d2)
    {
        if (command == 0xC0) _program[channel] = d1 & 0x7F;
        if (command == 0xB0 && d1 == 1 && d2 > 0 || command == 0xE0 && ((d1 & 0x7F) | (d2 & 0x7F) << 7) != 8192) _expressive[channel] = true;
    }

    /// <summary>This note's voice variation (default when off, on the drum channel or for an untouched family).</summary>
    public Nuance Draw(int channel, int key, SynthOptions o)
    {
        if (!o.Humanize || channel == 9 || Of(_program[channel]) is not { } f) return default;
        var vib = o.HumanizeDrift && !_expressive[channel] && f.VibCents > 0;
        return o.HumanizeVoice || vib ? Nuance.Of(f, channel, key, _count[channel]++, o.HumanizeVoice, vib) : default;
    }
}
