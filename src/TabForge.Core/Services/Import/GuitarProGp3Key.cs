using System;
using System.Text;

namespace TabForge.Services;

// Owns: the song-wide key signature a Guitar Pro 3 file stores once in its header (the score reader drops it, so every bar read as C).
// Does not own: per-bar key changes (the score reader has those) or the other formats' keys.
// Tests: GuitarProImportParityTests (brave_new_world.gp3).
internal static class GuitarProGp3Key
{
    /// <summary>
    /// The header key (sharps positive, flats negative) and how many leading bars it covers: those before the first bar that sets its own key.
    /// (0, 0) when the file is not Guitar Pro 3, has no key, or is cut short.
    /// </summary>
    internal static (int Key, int Bars) Read(byte[] data)
    {
        try
        {
            if (data.Length < 40 || !Encoding.ASCII.GetString(data, 1, Math.Min(data[0], (byte)30)).Contains("v3.", StringComparison.Ordinal)) return (0, 0);
            var p = 31;
            for (var i = 0; i < 8; i++) p = SkipString(data, p);   // title .. instructions
            var notices = BitConverter.ToInt32(data, p);
            p += 4;
            if (notices is < 0 or > 1000) return (0, 0);
            for (var i = 0; i < notices; i++) p = SkipString(data, p);
            p += 1 + 4;   // triplet feel, tempo
            var key = BitConverter.ToInt32(data, p);
            p += 4 + 64 * 12;   // key, midi channels
            var bars = BitConverter.ToInt32(data, p);
            p += 8;   // bar count, track count
            if (key is < -7 or > 7 or 0 || bars is < 0 or > 100000) return (0, 0);
            for (var bar = 0; bar < bars; bar++)
            {
                var flags = data[p++];
                if ((flags & 0x40) != 0) return (key, bar);
                if ((flags & 0x01) != 0) p++;
                if ((flags & 0x02) != 0) p++;
                if ((flags & 0x08) != 0) p++;
                if ((flags & 0x10) != 0) p++;
                if ((flags & 0x20) != 0) p += 4 + BitConverter.ToInt32(data, p) + 4;
            }
            return (key, bars);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException) { return (0, 0); }
    }

    private static int SkipString(byte[] data, int p) => p + 4 + BitConverter.ToInt32(data, p);
}
