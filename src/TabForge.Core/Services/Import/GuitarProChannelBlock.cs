using System;
using System.Linq;

namespace TabForge.Services;

// Owns: the 64-channel mixer block of a Guitar Pro 3-5 file (program, volume, balance, chorus, reverb), read from the raw bytes because the score reader drops chorus and reverb.
// Does not own: the track mixer values themselves (GuitarProTrackConverter).
// Tests: GuitarProImportParityTests (brave_new_world.gp3, aces_high_3.gp5).
internal static class GuitarProChannelBlock
{
    private const int Channels = 64;
    private const int EntrySize = 12;

    internal readonly record struct Entry(int Program, int Volume, int Balance, int Chorus, int Reverb);

    /// <summary>
    /// The 64 channel entries, or null when the file is not Guitar Pro 3-5 or the block is not found. The block sits in the file header
    /// (after the key, in every version), is 64 entries of a program int and six 0..16 steps, and is found by that shape.
    /// </summary>
    internal static Entry[]? Read(byte[] data)
    {
        if (data.Length < 40 + Channels * EntrySize || data[0] is 0 or > 30) return null;
        if (!System.Text.Encoding.ASCII.GetString(data, 1, data[0]).StartsWith("FICHIER GUITAR PRO v", StringComparison.Ordinal)) return null;
        var last = Math.Min(data.Length - Channels * EntrySize, 1 << 20);
        for (var start = 31; start <= last; start++)
            if (LooksLikeBlock(data, start))
                return Enumerable.Range(0, Channels).Select(i => EntryAt(data, start + i * EntrySize)).ToArray();
        return null;
    }

    private static Entry EntryAt(byte[] data, int at) => new(BitConverter.ToInt32(data, at), data[at + 4], data[at + 5], data[at + 6], data[at + 7]);

    private static bool LooksLikeBlock(byte[] data, int start)
    {
        for (var i = 0; i < Channels; i++)
        {
            var at = start + i * EntrySize;
            var program = BitConverter.ToInt32(data, at);   // -1 marks an unused channel
            if (program is < -1 or > MidiLimits.Max) return false;
            for (var b = 4; b < 10; b++)
                if (data[at + b] > 16) return false;
        }
        return true;
    }

    /// <summary>A 0..16 mixer step as a 0..127 value, rounded down the way Guitar Pro and TuxGuitar do (step 8 is 63, 16 is 127).</summary>
    internal static int ToMidi(int step) => Math.Clamp(step * 127 / 16, 0, MidiLimits.Max);

    /// <summary>
    /// The entry of a track's channel. The score reader's channel numbers do not index the 64 entries, so the entry is the first whose
    /// program (when it has one), volume and balance equal what the score reader gave for the track; null when none does.
    /// </summary>
    internal static Entry? For(Entry[]? block, int program, int volume, int balance)
    {
        if (block is null) return null;
        foreach (var entry in block)
            if (entry.Volume == volume && entry.Balance == balance && (entry.Program < 0 || entry.Program == program)) return entry;
        return null;
    }
}
