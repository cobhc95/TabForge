using System.IO;
using System.Text;

namespace TabForge;

/// <summary>
/// Writes minimal, valid Guitar Pro 3.00, 4.00, 5.00 and 5.10 files for the long-import tests (generated here, never a real song):
/// 4/4, any number of six-string guitar tracks, alternating whole rests and whole notes (a note on the low E string whose fret is
/// the bar and track number modulo 12). The header can declare more bars or tracks than the file holds, to forge damaged files.
/// The layouts follow the Guitar Pro 3-5 reader of alphaTab 1.8.4 field by field.
/// </summary>
internal static class SyntheticGuitarPro35
{
    /// <summary>The file versions the importer supports through alphaTab's Guitar Pro 3-5 reader.</summary>
    internal static readonly int[] Versions = { 300, 400, 500, 510 };

    internal static string VersionName(int version) => $"GP{version / 100}.{version % 100:00}";

    /// <summary>The fret on the low E string of the whole note in bar <paramref name="bar"/> (zero-based) of <paramref name="track"/>; odd bars only (even bars are rests).</summary>
    internal static int FretAt(int bar, int track) => (bar + track) % 12;

    internal static byte[] Write(int version, int bars, int tracks = 1, int? declaredBars = null, int? declaredTracks = null, bool lyrics = false, int beatsPerBar = 0, bool secondVoiceRest = false, string? lyricsText = null)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        void IntByteString(string s) { var b = Encoding.ASCII.GetBytes(s); w.Write(b.Length + 1); w.Write((byte)b.Length); w.Write(b); }
        var versionText = Encoding.ASCII.GetBytes($"FICHIER GUITAR PRO v{version / 100}.{version % 100:00}");
        w.Write((byte)versionText.Length); w.Write(versionText); w.Write(new byte[30 - versionText.Length]);

        // Score information: title, subtitle, artist, album, words, (music from 5.00), copyright, tab, instructions; then notice lines.
        foreach (var s in version >= 500 ? new[] { "Synthetic long song", "", "", "", "", "", "", "", "" } : new[] { "Synthetic long song", "", "", "", "", "", "", "" })
            IntByteString(s);
        w.Write(0);
        if (version < 500) w.Write((byte)0);                                  // triplet feel (one flag for the whole song)
        if (version >= 400)
        {
            if (lyrics)
            {   // lyrics on track 1 from bar 2 (one-based), then 4 empty lines
                var words = Encoding.ASCII.GetBytes(lyricsText ?? "lalala la la");
                w.Write(1); w.Write(2); w.Write(words.Length); w.Write(words);
                for (var i = 0; i < 4; i++) { w.Write(0); w.Write(0); }
            }
            else { w.Write(0); for (var i = 0; i < 5; i++) { w.Write(0); w.Write(0); } } // lyrics: track, 5 x (start bar, empty text)
        }
        if (version >= 510) w.Write(new byte[19]);                            // master volume, effect, equaliser
        if (version >= 500)
        {
            w.Write(new byte[28]); w.Write((short)0);                         // page setup: size and margins, header/footer flags
            for (var i = 0; i < 10; i++) IntByteString("");                   // page setup texts
            IntByteString("");                                                // tempo text
        }
        w.Write(120);                                                         // tempo
        if (version >= 510) w.Write((byte)0);                                 // hide tempo
        w.Write(0);                                                           // key
        if (version >= 400) w.Write((byte)0);                                 // octave
        for (var i = 0; i < 64; i++) { w.Write(25); w.Write((byte)13); w.Write((byte)8); w.Write(new byte[6]); } // MIDI channels
        if (version >= 500)
        {
            for (var i = 0; i < 19; i++) w.Write((short)-1);                  // directions (none)
            w.Write(0);                                                       // master reverb
        }
        w.Write(declaredBars ?? bars); w.Write(declaredTracks ?? tracks);

        for (var b = 0; b < bars; b++)                                        // master bars
        {
            if (b == 0) { w.Write((byte)0x03); w.Write((byte)4); w.Write((byte)4); if (version >= 500) w.Write(new byte[] { 2, 2, 2, 2 }); } // 4/4 (+ beaming)
            else w.Write((byte)0);
            if (version >= 500) w.Write(new byte[3]);                         // alternate endings, triplet feel, unknown
        }

        for (var t = 0; t < tracks; t++)                                      // tracks
        {
            w.Write((byte)0);                                                 // track flags
            var name = Encoding.ASCII.GetBytes("Guitar");
            w.Write((byte)name.Length); w.Write(name); w.Write(new byte[40 - name.Length]);
            w.Write(6); foreach (var tuning in new[] { 64, 59, 55, 50, 45, 40, 0 }) w.Write(tuning); // strings and tuning
            w.Write(1); w.Write(1); w.Write(2);                               // port, channel, effect channel
            w.Write(24); w.Write(0);                                          // frets, capo
            w.Write(new byte[] { 255, 0, 0, 0 });                             // colour
            if (version >= 500)
            {
                w.Write((short)0); w.Write((byte)0); w.Write((byte)0);        // staff flags, MIDI automatic, RSE auto-accentuation, bank
                w.Write((byte)0); w.Write(new byte[12]); w.Write(new byte[12]); // RSE humanise, clef mode and two unknowns, 10 + 2 unknown bytes
                w.Write(new byte[16]);                                        // RSE instrument, style, sound bank, unknown
                if (version >= 510) { w.Write(new byte[4]); IntByteString(""); IntByteString(""); } // EQ, effect name, effect category
            }
        }

        for (var b = 0; b < bars; b++)                                        // bars: for each bar, each track
            for (var t = 0; t < tracks; t++)
            {
                if (version >= 500) w.Write((byte)0);
                if (beatsPerBar > 0)
                {   // dense: beatsPerBar quarter notes (all but the first beat of 4/4 are the same duration), a note on a different string each
                    w.Write(beatsPerBar);
                    for (var beat = 0; beat < beatsPerBar; beat++)
                    {
                        w.Write((byte)0); w.Write((byte)0); w.Write((byte)(1 << (1 + (b + beat + t) % 6))); // quarter note, string 1..6
                        w.Write((byte)0x20); w.Write((byte)1); w.Write((byte)((b + beat * 3 + t) % 20)); // normal note, fret 0..19
                        if (version >= 500) { w.Write((byte)0); w.Write((short)0); }                  // note flags 2, beat flags 2
                    }
                    if (version >= 500)
                    {
                        if (secondVoiceRest) { w.Write(1); w.Write((byte)0x40); w.Write((byte)0x02); w.Write(unchecked((byte)-2)); w.Write((byte)0); w.Write((short)0); } // voice 2: one whole rest, as most files hold
                        else w.Write(0);
                    }
                    continue;
                }
                w.Write(1);                                                   // voice 1: one beat
                if (b % 2 == 0)
                {
                    w.Write((byte)0x40); w.Write((byte)0x02); w.Write(unchecked((byte)-2)); w.Write((byte)0); // whole rest
                    if (version >= 500) w.Write((short)0);
                }
                else
                {
                    w.Write((byte)0); w.Write(unchecked((byte)-2)); w.Write((byte)0x02); // whole note on the low E string
                    w.Write((byte)0x20); w.Write((byte)1); w.Write((byte)FretAt(b, t));  // normal note, fret
                    if (version >= 500) w.Write((byte)0);                     // note flags 2
                    if (version >= 500) w.Write((short)0);                    // beat flags 2
                }
                if (version >= 500) w.Write(0);                               // voice 2: empty
            }
        return ms.ToArray();
    }
}
