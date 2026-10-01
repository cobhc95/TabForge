using System.IO;
using System.Text;
using TabForge.Services;

namespace TabForge;

/// <summary>
/// Guitar Pro 3-5 songs over 1,000 bars: alphaTab 1.8.4 refuses them with a fixed "'bar count' ... internal safety
/// threshold of 1000"; <see cref="Gp3To5LongSongReader"/> reads them with TabForge's own limit (20,000 bars) instead.
/// Uses a synthetic GP 5.00 file written here (one guitar track, whole rests and whole notes), never a real song.
/// </summary>
public static partial class SelfTest
{
    private static void TestLongGuitarPro35Import()
    {
        Check("the long-song reader matches the installed alphaTab build", Gp3To5LongSongReader.IsAvailable);

        // Below alphaTab's threshold the replay must give exactly what alphaTab's own ReadScore gives.
        var small = SyntheticGp5(40);
        var viaAlphaTab = ScoreFingerprint(AlphaTab.Importer.ScoreLoader.LoadScoreFromBytes(small, new AlphaTab.Settings()));
        var viaReplay = ScoreFingerprint(Gp3To5LongSongReader.Read(small, new AlphaTab.Settings(), InputLimits.MaxMeasuresPerTrack));
        Check("the long-song reader reads a short GP5 exactly like alphaTab", viaAlphaTab == viaReplay && viaAlphaTab.Length > 200,
            $"alphaTab {viaAlphaTab.Length} chars, replay {viaReplay.Length} chars");
        // Full model (alphaTab's own serialised object tree, every property): the shipped demo GP5 and a file with lyrics.
        foreach (var (label, bytes) in new[] { ("the demo GP5", FuzzFindSample("TabForge Demo - Ashen Meridian.gp5")), ("a GP5 with lyrics", SyntheticGp5(12, lyrics: true)) })
        {
            if (bytes is null) { Check($"long-song reader fidelity: {label} is present", false); continue; }
            var full = FullScoreDump(AlphaTab.Importer.ScoreLoader.LoadScoreFromBytes(bytes, new AlphaTab.Settings()));
            var fullReplay = FullScoreDump(Gp3To5LongSongReader.Read(bytes, new AlphaTab.Settings(), InputLimits.MaxMeasuresPerTrack));
            var firstDiff = Enumerable.Range(0, Math.Min(full.Length, fullReplay.Length)).FirstOrDefault(i => full[i] != fullReplay[i], -1);
            Check($"the long-song reader reads {label} exactly like alphaTab (full model)", full == fullReplay && full.Length > 1_000,
                $"alphaTab {full.Length} chars, replay {fullReplay.Length} chars, first difference at {firstDiff}");
            if (label == "a GP5 with lyrics")
                Check("the lyrics fixture really carries lyrics (both paths)", full.Contains("lalala", StringComparison.Ordinal) && fullReplay.Contains("lalala", StringComparison.Ordinal));
        }

        // Over the threshold: alphaTab alone refuses it, TabForge opens it with every bar.
        var longSong = SyntheticGp5(1_200);
        var alphaTabRefuses = false;
        try { AlphaTab.Importer.ScoreLoader.LoadScoreFromBytes(longSong, new AlphaTab.Settings()); }
        catch (Exception ex) { alphaTabRefuses = Gp3To5LongSongReader.IsBarCountRefusal(ex); }
        Check("alphaTab alone still refuses a 1,200-bar GP5 (the case this covers)", alphaTabRefuses);
        string? error = null;
        Models.SongProject? song = null;
        try { song = GuitarProImporter.ImportBytes(longSong, "long.gp5"); }
        catch (Exception ex) { error = ex.Message; }
        Check("a 1,200-bar GP5 opens with all of its bars", song is not null && song.Tracks.Count == 1 && song.Tracks[0].Measures.Count == 1_200,
            error ?? $"{song?.Tracks.Count} tracks, {song?.Tracks.FirstOrDefault()?.Measures.Count} bars");
        Check("the last bar of the long GP5 keeps its note", song?.Tracks[0].Measures[^1].Cells.Any(cell => cell.Notes.Any(note => note.Fret == 1_199 % 12)) == true);

        // TabForge's own limit still applies: a header claiming more bars than InputLimits allows is refused up front.
        var tooLong = SyntheticGp5(3, declaredBars: InputLimits.MaxMeasuresPerTrack + 1);
        Check("a GP5 declaring more than 20,000 bars is refused", ThrowsInvalidData(() => GuitarProImporter.ImportBytes(tooLong, "too-long.gp5"), out var tooLongError)
            && tooLongError.Contains("too many measures", StringComparison.Ordinal), tooLongError);
        // A forged bar count with no data behind it ends at the end of the file, not in a huge allocation.
        var forged = SyntheticGp5(3, declaredBars: 15_000);
        Check("a GP5 declaring 15,000 bars but holding 3 is refused as damaged", ThrowsInvalidData(() => GuitarProImporter.ImportBytes(forged, "forged.gp5"), out var forgedError), forgedError);
    }

    /// <summary>Every property of the score as alphaTab serialises it (JsonConverter.ScoreToJsObject), as text.</summary>
    private static string FullScoreDump(AlphaTab.Model.Score score)
    {
        var text = new StringBuilder();
        void Dump(object? value, int depth)
        {
            if (depth > 64) { text.Append("<deep>"); return; }
            switch (value)
            {
                case null: text.Append("null"); return;
                case string s: text.Append('"').Append(s).Append('"'); return;
                case IFormattable f when value.GetType().IsPrimitive || value is decimal || value.GetType().IsEnum:
                    text.Append(f.ToString(null, System.Globalization.CultureInfo.InvariantCulture)); return;
                case bool b: text.Append(b ? "true" : "false"); return;
                case System.Collections.IEnumerable items:
                    text.Append('[');
                    foreach (var item in items) { Dump(item, depth + 1); text.Append(','); }
                    text.Append(']'); return;
            }
            var type = value.GetType();
            if (type.GetProperty("Key") is { } key && type.GetProperty("Value") is { } val)
            {
                Dump(key.GetValue(value), depth + 1); text.Append(':'); Dump(val.GetValue(value), depth + 1); return;
            }
            text.Append(type.Name).Append(':').Append(value);
        }
        Dump(AlphaTab.Model.JsonConverter.ScoreToJsObject(score), 0);
        return text.ToString();
    }

    private static string ScoreFingerprint(AlphaTab.Model.Score score)
    {
        var text = new StringBuilder();
        text.Append(score.Title).Append('|').Append(score.Tempo).Append('|').Append(score.MasterBars.Count).Append('\n');
        foreach (var master in score.MasterBars)
            text.Append(master.TimeSignatureNumerator).Append('/').Append(master.TimeSignatureDenominator)
                .Append(' ').Append(master.TempoAutomations.Count).Append(' ').Append(master.Start).Append(';');
        foreach (var track in score.Tracks)
        {
            text.Append('\n').Append(track.Name).Append(':');
            foreach (var bar in track.Staves[0].Bars)
                foreach (var voice in bar.Voices)
                    foreach (var beat in voice.Beats)
                    {
                        text.Append(beat.Duration).Append(beat.IsRest ? "r" : "").Append(beat.PlaybackStart);
                        foreach (var note in beat.Notes) text.Append('(').Append(note.String).Append(',').Append(note.Fret).Append(')');
                        text.Append(' ');
                    }
        }
        return text.ToString();
    }

    /// <summary>
    /// A minimal Guitar Pro 5.00 file: 4/4, one six-string guitar track, alternating whole rests and whole notes.
    /// <paramref name="declaredBars"/> overrides the bar count in the header (to forge a damaged file).
    /// </summary>
    internal static byte[] SyntheticGp5(int bars, int? declaredBars = null, bool lyrics = false)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        void IntByteString(string s) { var b = Encoding.ASCII.GetBytes(s); w.Write(b.Length + 1); w.Write((byte)b.Length); w.Write(b); }
        var version = Encoding.ASCII.GetBytes("FICHIER GUITAR PRO v5.00");
        w.Write((byte)version.Length); w.Write(version); w.Write(new byte[30 - version.Length]);
        foreach (var s in new[] { "Synthetic long song", "", "", "", "", "", "", "", "" }) IntByteString(s); // title .. instructions
        w.Write(0);                                                         // notice lines
        if (lyrics)
        {   // lyrics on track 1 from bar 2 (one-based), then 4 empty lines
            var words = Encoding.ASCII.GetBytes("lalala la la");
            w.Write(1); w.Write(2); w.Write(words.Length); w.Write(words);
            for (var i = 0; i < 4; i++) { w.Write(0); w.Write(0); }
        }
        else { w.Write(0); for (var i = 0; i < 5; i++) { w.Write(0); w.Write(0); } } // lyrics: track, 5 x (start bar, empty text)
        w.Write(new byte[28]); w.Write((short)0);                           // page setup: size and margins, header/footer flags
        for (var i = 0; i < 10; i++) IntByteString("");                     // page setup texts
        IntByteString("");                                                  // tempo text
        w.Write(120);                                                       // tempo
        w.Write(0); w.Write((byte)0);                                       // key, octave
        for (var i = 0; i < 64; i++) { w.Write(25); w.Write((byte)13); w.Write((byte)8); w.Write(new byte[6]); } // MIDI channels
        for (var i = 0; i < 19; i++) w.Write((short)-1);                    // directions (none)
        w.Write(0);                                                         // master reverb
        w.Write(declaredBars ?? bars); w.Write(1);                          // bar count, track count
        for (var b = 0; b < bars; b++)
        {
            if (b > 0) w.Write((byte)0);                                    // blank before every bar header but the first
            if (b == 0) { w.Write((byte)0x03); w.Write((byte)4); w.Write((byte)4); w.Write(new byte[] { 2, 2, 2, 2 }); } // 4/4 + beaming
            else w.Write((byte)0);
            w.Write((byte)0);                                               // alternate endings
            w.Write((byte)0);                                               // triplet feel
        }
        w.Write((byte)0);                                                   // blank before the track (5.00)
        w.Write((byte)0);                                                   // track flags
        var name = Encoding.ASCII.GetBytes("Guitar");
        w.Write((byte)name.Length); w.Write(name); w.Write(new byte[40 - name.Length]);
        w.Write(6); foreach (var t in new[] { 64, 59, 55, 50, 45, 40, 0 }) w.Write(t); // strings and tuning
        w.Write(1); w.Write(1); w.Write(2);                                 // port, channel, effect channel
        w.Write(24); w.Write(0);                                            // frets, capo
        w.Write(new byte[] { 255, 0, 0, 0 });                               // colour
        w.Write((short)0); w.Write((byte)0); w.Write((byte)0);              // flags, auto accentuation, bank
        w.Write((byte)0); w.Write(new byte[12]); w.Write(new byte[12]);     // RSE humanize, three values, reserved
        w.Write(0); w.Write(0); w.Write(0); w.Write((short)0); w.Write((byte)0); // RSE instrument, unknown, sound bank, effect
        w.Write((short)0);                                                  // after the tracks (5.00)
        for (var b = 0; b < bars; b++)
        {
            w.Write(1);                                                     // voice 1: one beat
            if (b % 2 == 0) { w.Write((byte)0x40); w.Write((byte)0x02); w.Write(unchecked((byte)-2)); w.Write((byte)0); w.Write((short)0); } // whole rest
            else
            {
                w.Write((byte)0); w.Write(unchecked((byte)-2)); w.Write((byte)0x02); // whole note on the low E string
                w.Write((byte)0x20); w.Write((byte)1); w.Write((byte)(b % 12)); w.Write((byte)0); // normal note, fret, flags
                w.Write((short)0);
            }
            w.Write(0);                                                     // voice 2: empty
            w.Write((byte)0);                                               // line break
        }
        return ms.ToArray();
    }
}
