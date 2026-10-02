using System.IO;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// Time / key signatures that carry forward to the next change (score, playback, bar check, exports, undo). Synthetic songs only.
public static partial class SelfTest
{
    private static string SigRow(SongProject song, int from = 0, int to = int.MaxValue) => string.Join(" ",
        Enumerable.Range(from, Math.Min(to, BarRangeEditor.MaxMeasures(song) - 1) - from + 1)
            .Select(bar => BarSignatures.TimeAt(song, bar) is var (n, d) ? $"{n}/{d}" : ""));

    private static string KeyRow(SongProject song) => string.Join(" ",
        Enumerable.Range(0, BarRangeEditor.MaxMeasures(song)).Select(bar => BarSignatures.KeyAt(song, bar) is var (k, m) ? $"{k}{(m ? "m" : "")}" : ""));

    private static void TestSignaturesCarryForward()
    {
        // 1. A change applies from its bar up to the next change, on every track.
        var song = SignatureSong(2, 12);
        Check("a new song starts with its signature on every bar", SigRow(song) == string.Join(" ", Enumerable.Repeat("4/4", 12)));
        Check("time signature: bar 5 = 3/4 carries to the end of the song and not before it",
            BarSignatures.SetTime(song, 4, 3, 4) == 11 && SigRow(song) == "4/4 4/4 4/4 4/4 3/4 3/4 3/4 3/4 3/4 3/4 3/4 3/4", SigRow(song));
        Check("every track gets the same bars (shared master bars)",
            Enumerable.Range(0, 12).All(b => song.Tracks[0].Measures[b].TimeSigNum == song.Tracks[1].Measures[b].TimeSigNum &&
                                              song.Tracks[0].Measures[b].TimeSigDenom == song.Tracks[1].Measures[b].TimeSigDenom));

        // 2. A later change stops the earlier one; changing the earlier one again only reaches the next change.
        BarSignatures.SetTime(song, 8, 6, 8);
        Check("a second change takes over from its bar", SigRow(song) == "4/4 4/4 4/4 4/4 3/4 3/4 3/4 3/4 6/8 6/8 6/8 6/8", SigRow(song));
        var lastChanged = BarSignatures.SetTime(song, 4, 2, 4);
        Check("changing the first of two changes stops at the next one",
            lastChanged == 7 && SigRow(song) == "4/4 4/4 4/4 4/4 2/4 2/4 2/4 2/4 6/8 6/8 6/8 6/8", SigRow(song));
        BarSignatures.SetTime(song, 1, 5, 4, untilNextChange: false);
        Check("'only this bar' changes that bar alone", SigRow(song) == "4/4 5/4 4/4 4/4 2/4 2/4 2/4 2/4 6/8 6/8 6/8 6/8", SigRow(song));
        BarSignatures.SetTime(song, 8, 4, 4);
        Check("going back to the song's signature carries forward too",
            SigRow(song) == "4/4 5/4 4/4 4/4 2/4 2/4 2/4 2/4 4/4 4/4 4/4 4/4" && song.Tracks[0].Measures[10].TimeSigNum is null && song.Tracks[0].Measures[8].TimeSigNum == 4,
            SigRow(song));
        Check("an impossible bar number changes nothing", BarSignatures.SetTime(song, 40, 7, 8) < 0 && BarSignatures.SetTime(song, -1, 7, 8) < 0 && SigRow(song).EndsWith("4/4"));

        // 3. Bar 1 is the song's own signature: later bars that relied on it keep their meter.
        var first = SignatureSong(2, 10);
        BarSignatures.SetTime(first, 5, 6, 8, untilNextChange: false);   // bar 6 is 6/8, bars 7-10 are the song's 4/4 again
        BarSignatures.SetTime(first, 0, 3, 4);
        Check("changing bar 1 reaches only up to the next change and keeps the bars after it as they were",
            SigRow(first) == "3/4 3/4 3/4 3/4 3/4 6/8 4/4 4/4 4/4 4/4" && first.TimeSignatureNumerator == 3 && first.TimeSignatureDenominator == 4, SigRow(first));
        BarSignatures.SetSongTime(first, 7, 8);
        Check("the project's song signature still moves every bar that has no signature of its own",
            first.Tracks.All(t => t.Measures[0].TimeSigNum == 7) && BarSignatures.TimeAt(first, 3) == (7, 8) && BarSignatures.TimeAt(first, 5) == (6, 8) && BarSignatures.TimeAt(first, 7) == (4, 4), SigRow(first));

        // 4. A bar that grows gets the beat slots to hold the new length; written beats are never dropped.
        var grow = SignatureSong(1, 3);
        BarSignatures.SetTime(grow, 0, 3, 4);
        grow.Tracks[0].Measures[1].Cells.RemoveRange(12, 4);
        BarSignatures.SetTime(grow, 0, 4, 4);
        Beat(grow, 0, 1, 14, 16, 60);
        Check("a bar that becomes longer is topped up with empty beat slots", grow.Tracks[0].Measures.All(m => m.Cells.Count >= 16) && grow.Tracks[0].Measures[1].Cells[14].Notes.Count == 1);

        // 5. Playback follows the carried signature: bar lengths and where each bar starts.
        var play = SingleTrack(8, 120);
        for (var bar = 0; bar < 8; bar++) Beat(play, 0, bar, 0, 4, 60);
        BarSignatures.SetTime(play, 2, 3, 4);
        BarSignatures.SetTime(play, 6, 6, 8);
        var timeline = MidiTimelineBuilder.Build(play, new PlaybackOptions());
        var onsets = timeline.Events.Where(e => e.IsNoteOn).Select(e => e.TimeMs).OrderBy(x => x).ToList();
        var expectedStarts = new[] { 0.0, 2000, 4000, 5500, 7000, 8500, 10000, 11500 };   // 4/4 = 2000 ms, 3/4 = 1500 ms, 6/8 = 1500 ms at 120 bpm
        Check("playback: every bar starts where the carried signatures put it",
            onsets.Count == 8 && onsets.Select((t, i) => Math.Abs(t - expectedStarts[i]) < 0.6).All(v => v), string.Join(" ", onsets.Select(t => t.ToString("0"))));
        Near("playback: the song is as long as its signatures say", 13000, timeline.TotalMs, 1.0);
        Check("bar length for the timeline and the score agree (slots per bar)",
            Enumerable.Range(0, 8).Select(b => MusicTime.BarSlots(play, b)).SequenceEqual(new[] { 16, 16, 12, 12, 12, 12, 12, 12 }));

        // 6. The red "incomplete / too long" check uses the carried signature.
        var check = SingleTrack(6, 120);
        for (var bar = 0; bar < 6; bar++) for (var k = 0; k < 4; k++) Beat(check, 0, bar, k * 4, 4, 60);   // four quarters in every bar
        Check("a bar of four quarters is complete in 4/4", MusicTime.FindBarProblems(check).Count == 0);
        BarSignatures.SetTime(check, 3, 3, 4);
        var problems = MusicTime.FindBarProblems(check).Select(p => p.BarIndex).ToArray();
        Check("after a change to 3/4 the bars that no longer add up are flagged, the earlier ones are not",
            problems.SequenceEqual(new[] { 3, 4, 5 }) && MusicTime.AnalyzeBar(check, 2).Complete && MusicTime.AnalyzeBar(check, 3).Error, string.Join(",", problems));

        // 7. Beaming follows the carried signature.
        var renderer = new StaffNotationRenderer();
        var beams = SignatureSong(1, 6);
        BarSignatures.SetTime(beams, 2, 6, 8);
        for (var k = 0; k < 6; k++) beams.Tracks[0].Measures[3].Cells[k * 2] = NotationCell(8, 67);
        var (beamNum, beamDen) = BarSignatures.TimeAt(beams, 3);
        var beamLayout = Layout(renderer, beams.Tracks[0].Measures[3], beamNum, beamDen, BarSignatures.KeyAt(beams, 3).Key);
        Check("score beaming sees the carried 6/8 on the bar after the change (3 + 3)",
            (beamNum, beamDen) == (6, 8) && beamLayout.BeamGroups.Count == 2 && beamLayout.BeamGroups.All(g => g.Beats.Count == 3), $"{beamNum}/{beamDen}, {beamLayout.BeamGroups.Count} beam groups");
    }

    private static void TestKeySignaturesCarryForward()
    {
        var song = SignatureSong(2, 12);
        Check("key signature: bar 4 = D major carries to the end of the song",
            BarSignatures.SetKey(song, 3, 2, false) == 11 && KeyRow(song) == "0 0 0 2 2 2 2 2 2 2 2 2", KeyRow(song));
        BarSignatures.SetKey(song, 6, -2, true);
        Check("a later key change takes over from its bar", KeyRow(song) == "0 0 0 2 2 2 -2m -2m -2m -2m -2m -2m", KeyRow(song));
        var last = BarSignatures.SetKey(song, 3, 4, false);
        Check("changing the earlier key stops at the next one", last == 5 && KeyRow(song) == "0 0 0 4 4 4 -2m -2m -2m -2m -2m -2m", KeyRow(song));
        BarSignatures.SetKey(song, 8, 0, false);
        Check("returning to the song's key carries forward",
            KeyRow(song) == "0 0 0 4 4 4 -2m -2m 0 0 0 0" && song.Tracks[0].Measures[10].KeySignature == 0 && song.Tracks[1].Measures[11].KeySignature == 0, KeyRow(song));
        BarSignatures.SetKey(song, 1, 1, false, untilNextChange: false);
        Check("'only this bar' changes that bar alone", KeyRow(song) == "0 1 0 4 4 4 -2m -2m 0 0 0 0", KeyRow(song));
        Check("both tracks hold the same keys",
            Enumerable.Range(0, 12).All(b => song.Tracks[0].Measures[b].KeySignature == song.Tracks[1].Measures[b].KeySignature &&
                                              song.Tracks[0].Measures[b].KeySignatureMinor == song.Tracks[1].Measures[b].KeySignatureMinor));

        // Accidentals follow the carried key: F-sharp is written in C major, silent in G / D major.
        var renderer = new StaffNotationRenderer();
        var keyed = SignatureSong(1, 6);
        BarSignatures.SetKey(keyed, 2, 2, false);
        string? Accidental(int bar)
        {
            var measure = keyed.Tracks[0].Measures[bar];
            measure.Cells[0] = NotationCell(4, 66);   // F-sharp
            var layout = Layout(renderer, measure, 4, 4, BarSignatures.KeyAt(keyed, bar).Key);
            return layout.BeatForCell(0)!.Notes[0].Accidental;
        }
        Check("accidentals: F-sharp needs a sharp before the key change and none after it, on every later bar",
            Accidental(1) == "♯" && Accidental(2) is null && Accidental(3) is null && Accidental(5) is null);

        // Bar 1 changes the song's own key; bars after the run that used the old key keep it.
        var opening = SignatureSong(1, 8);
        BarSignatures.SetKey(opening, 4, 3, false, untilNextChange: false);
        BarSignatures.SetKey(opening, 0, 1, false);
        Check("changing the key of bar 1 stops at the next change and leaves the later bars as they were",
            KeyRow(opening) == "1 1 1 1 3 0 0 0" && opening.KeySignature == 1, KeyRow(opening));
        var plain = SignatureSong(1, 6);
        BarSignatures.SetKey(plain, 3, 2, false, untilNextChange: false);
        BarSignatures.SetSongKey(plain, 5, false);
        Check("the project's song key still moves every bar without a key of its own", KeyRow(plain) == "5 5 5 2 5 5" && plain.KeySignature == 5, KeyRow(plain));
        // A selected bar range: the change applies to exactly those bars.
        var ranged = SignatureSong(2, 8);
        BarSignatures.SetTime(ranged, 6, 6, 8);
        Check("a selected range gets the time signature on exactly its bars",
            BarSignatures.SetTimeRange(ranged, 2, 4, 3, 4) == 4 && SigRow(ranged) == "4/4 4/4 3/4 3/4 3/4 4/4 6/8 6/8", SigRow(ranged));
        Check("a selected range from bar 1 changes the song's signature but not the bars after the range",
            BarSignatures.SetTimeRange(ranged, 0, 1, 2, 4) == 1 && SigRow(ranged) == "2/4 2/4 3/4 3/4 3/4 4/4 6/8 6/8", SigRow(ranged));
        Check("a selected range gets the key on exactly its bars",
            BarSignatures.SetKeyRange(ranged, 3, 5, -3, true) == 5 && KeyRow(ranged) == "0 0 0 -3m -3m -3m 0 0", KeyRow(ranged));
        // A bar without a key of its own (a one-bar change, or a song saved before keys carried forward) exports in the song's key, as the
        // score shows it: the clean .gp used to carry the one-bar key on to the end of the song.
        var keyFolder = RtFolder();
        try
        {
            var exported = RtViaGp(plain, keyFolder, "onebarkey", embed: false);
            Check("clean .gp: a one-bar key change stays on that bar (later bars in the song's key, as the score shows)", KeyRow(exported) == KeyRow(plain), $"{KeyRow(plain)} -> {KeyRow(exported)}");
        }
        finally { RtCleanup(keyFolder); }

        // One undo step for a signature change.
        var undoSong = SignatureSong(2, 8);
        var before = ProjectService.Snapshot(undoSong);
        var history = new UndoController();
        var transaction = history.BeginTransaction(undoSong);
        BarSignatures.SetTime(undoSong, 2, 7, 8);
        BarSignatures.SetKey(undoSong, 2, 3, true);
        history.Commit(transaction);
        var undone = history.TryUndo(history.Snapshot(undoSong), out var target) ? history.Restore(target, undoSong) : null;
        Check("a signature change that reaches many bars is one undo step",
            undone is not null && history.UndoCount == 0 && history.RedoCount == 1 && ProjectService.Snapshot(undone) == before &&
            SigRow(undone) == string.Join(" ", Enumerable.Repeat("4/4", 8)) && KeyRow(undone) == string.Join(" ", Enumerable.Repeat("0", 8)));
    }

    private static void TestSignatureRoundTrips()
    {
        var song = SignatureSong(2, 10);
        song.Title = "Signature round trip";
        for (var t = 0; t < 2; t++) for (var bar = 0; bar < 10; bar++) Beat(song, t, bar, 0, 4, 60 + t);
        BarSignatures.SetTime(song, 3, 3, 4);
        BarSignatures.SetTime(song, 6, 6, 8);
        BarSignatures.SetTime(song, 8, 4, 4);
        BarSignatures.SetKey(song, 2, 2, false);
        BarSignatures.SetKey(song, 5, 0, false);   // back to the song's key: the bars after it carry no key of their own
        BarSignatures.SetKey(song, 7, -1, true);
        var times = SigRow(song);
        var keys = KeyRow(song);

        var path = Path.Combine(Path.GetTempPath(), "tabforge-selftest-signatures.gp");
        try
        {
            GuitarProExporter.Save(song, path, embedProject: false);
            var imported = GuitarProImporter.Import(path);
            Check("guitar pro round trip keeps the time signature of every bar", SigRow(imported) == times, $"{SigRow(imported)} vs {times}");
            Check("guitar pro round trip keeps the key of every bar, including the return to the song's key", KeyRow(imported) == keys, $"{KeyRow(imported)} vs {keys}");
        }
        finally { try { File.Delete(path); } catch { } }

        var restored = ProjectService.Restore(ProjectService.Snapshot(song));
        Check("a saved project keeps every bar's signatures", SigRow(restored) == times && KeyRow(restored) == keys, $"{SigRow(restored)} | {KeyRow(restored)}");

        var xml = System.Text.Encoding.UTF8.GetString(MusicXmlExportService.ToBytes(song));
        var fifths = System.Text.RegularExpressions.Regex.Matches(xml, "<fifths>(-?\\d+)</fifths>").Select(m => m.Groups[1].Value).ToArray();
        Check("MusicXML writes the key where it changes, including the return to the song's key",
            fifths.Length >= 4 && fifths.Take(4).SequenceEqual(new[] { "0", "2", "0", "-1" }), string.Join(",", fifths));
    }
}
