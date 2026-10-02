using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>Copy/paste chunk C6: Paste Special (repeat, mode, octave shift, keep string and fret, bar settings) and its dialog.</summary>
public static partial class SelfTest
{
    private static void TestPasteSpecial()
    {
        static SongProject Song(int bars, params TrackModel[] tracks)
        {
            var p = new SongProject();
            foreach (var t in tracks.Length == 0 ? new[] { new TrackModel { Name = "Guitar" } } : tracks)
            {
                t.Measures = Enumerable.Range(0, bars).Select(i => new MeasureModel { Number = i + 1 }).ToList();
                p.Tracks.Add(t);
            }
            return p;
        }
        static TabCell Q(int fret, int s = 0)
        {
            var cell = new TabCell { DurationDenominator = 4 };
            cell.Notes.Add(new TabNote { StringIndex = s, Fret = fret, MidiValue = new[] { 64, 59, 55, 50, 45, 40 }[s] + fret });
            return cell;
        }
        static List<int> Frets(SongProject p, int bar, int track = 0) =>
            p.Tracks[track].Measures[bar].Cells.Where(c => c.Notes.Count > 0).Select(c => c.Notes[0].Fret).ToList();
        static string Snap(SongProject p) => ProjectService.Snapshot(p);

        var src = Song(2);
        for (var i = 0; i < 4; i++) src.Tracks[0].Measures[0].Cells[i * 4] = Q(i + 1);
        var twoBeats = ClipboardService.CaptureBeats(src, 0, 0, 0, 4, 0, 8);          // frets 2, 3
        var wholeBar = ClipboardService.CaptureSelection(src, 0, 0, 0, 0, 0, -1);      // frets 1, 2, 3, 4

        // Beats: repeat 3 (Replace) runs end to end across the bar line; nothing is asked or remembered; one undo step.
        var settings = new EditingSettings();
        var p = Song(2);
        var before = Snap(p);
        var doc = DocumentSession.FromProject(p, null);
        var undo = doc.Undo;
        var pasted = DocumentEdits.Run<PasteOutcome>(doc, sp => EditCommands.PasteSpecial(sp, twoBeats, new PasteTarget(0, 0, 0, 0), new PasteSpecialOptions(Repeat: 3), settings) is { Changed: true } o ? o : null);
        var r = pasted.Value ?? new PasteOutcome();
        var capture = pasted.Capture;
        Check("paste special: beats repeated 3 times land end to end across the bar line",
            r.Changed && r.Asked.Count == 0 && Frets(p, 0).SequenceEqual(new[] { 2, 3, 2, 3 }) && Frets(p, 1).SequenceEqual(new[] { 2, 3 }));
        Check("paste special: no answer is stored in the settings", PasteQuestionInfo.All.All(q => PasteQuestionInfo.Get(settings, q) == PasteQuestionInfo.Ask));
        Check("paste special: the status names the copies", r.Status.Contains("3 copies"), r.Status);
        Check("paste special: repeated beats are one undo step",
            capture.Stored && undo.TryUndo(undo.Snapshot(p), out var target) && Snap(undo.Restore(target)) == before && undo.UndoCount == 0);

        // Beats: Insert pushes the existing notes by the whole repeated length.
        p = Song(2);
        for (var i = 0; i < 4; i++) p.Tracks[0].Measures[0].Cells[i * 4] = Q(10 + i);
        r = EditCommands.PasteSpecial(p, twoBeats, new PasteTarget(0, 0, 0, 0), new PasteSpecialOptions(Repeat: 2, BeatMode: BeatPasteMode.Insert), settings);
        Check("paste special: beats Insert x2 shifts the notes after the cursor by four beats",
            r.Changed && Frets(p, 0).SequenceEqual(new[] { 2, 3, 2, 3 }) && Frets(p, 1).SequenceEqual(new[] { 10, 11, 12, 13 }));

        // Bars: repeat 3 + Insert before = three new bars on every track, old bar pushed behind; one undo step.
        var two = Song(2, new TrackModel { Name = "Lead" }, new TrackModel { Name = "Rhythm" });
        two.Tracks[0].Measures[1].Cells[0] = Q(7);
        two.Tracks[1].Measures[1].Cells[0] = Q(9);
        before = Snap(two);
        doc = DocumentSession.FromProject(two, null);
        undo = doc.Undo;
        pasted = DocumentEdits.Run<PasteOutcome>(doc, sp => EditCommands.PasteSpecial(sp, wholeBar, new PasteTarget(0, 0, 1, 0),
            new PasteSpecialOptions(Repeat: 3, BarsMode: BarsOntoNotesAnswer.InsertBefore), settings) is { Changed: true } o ? o : null);
        r = pasted.Value ?? new PasteOutcome();
        capture = pasted.Capture;
        Check("paste special: bars repeat 3 + insert before gives three new bars on every track and keeps the old bar",
            r.Changed && r.Asked.Count == 0 && r.BarMap is not null && two.Tracks.All(t => t.Measures.Count == 5) &&
            Frets(two, 1).SequenceEqual(new[] { 1, 2, 3, 4 }) && Frets(two, 2).SequenceEqual(new[] { 1, 2, 3, 4 }) && Frets(two, 3).SequenceEqual(new[] { 1, 2, 3, 4 }) &&
            Frets(two, 4).SequenceEqual(new[] { 7 }) && Frets(two, 4, 1).SequenceEqual(new[] { 9 }) && Frets(two, 1, 1).Count == 0);
        Check("paste special: bars repeated and inserted are one undo step",
            capture.Stored && undo.TryUndo(undo.Snapshot(two), out var t2) && Snap(undo.Restore(t2)) == before && undo.UndoCount == 0);

        // Bars: insert after, overwrite and copy-settings off.
        two = Song(2, new TrackModel { Name = "Lead" });
        two.Tracks[0].Measures[0].Cells[0] = Q(7);
        r = EditCommands.PasteSpecial(two, wholeBar, new PasteTarget(0, 0, 0, 0), new PasteSpecialOptions(Repeat: 2, BarsMode: BarsOntoNotesAnswer.InsertAfter), settings);
        Check("paste special: bars x2 insert after puts the copies behind the target bar",
            r.Changed && two.Tracks[0].Measures.Count == 4 && Frets(two, 0).SequenceEqual(new[] { 7 }) && Frets(two, 1).Count == 4 && Frets(two, 2).Count == 4);
        var threeFour = ClipboardService.CaptureSelection(src, 0, 0, 0, 0, 0, -1);
        threeFour.Tracks[0].Bars[0].TimeSigNum = 3; threeFour.Tracks[0].Bars[0].TimeSigDenom = 4;
        foreach (var copy in new[] { true, false })
        {
            two = Song(2);
            r = EditCommands.PasteSpecial(two, threeFour, new PasteTarget(0, 0, 0, 0), new PasteSpecialOptions(CopyBarSettings: copy), settings);
            Check($"paste special: bars with copy bar settings {(copy ? "on" : "off")} {(copy ? "take" : "keep")} the time signature",
                r.Changed && (two.Tracks[0].Measures[0].TimeSigNum == 3) == copy);
        }

        // Repeat is limited to 1..99.
        var norm = new PasteSpecialOptions(Repeat: 500, OctaveShift: 9).Normalized();
        Check("paste special: repeat and octave shift are clamped",
            norm.Repeat == 99 && norm.OctaveShift == 2 && new PasteSpecialOptions(Repeat: 0, OctaveShift: -9).Normalized() is { Repeat: 1, OctaveShift: -2 });
        p = Song(2);
        r = EditCommands.PasteSpecial(p, twoBeats, new PasteTarget(0, 0, 0, 0), new PasteSpecialOptions(Repeat: 0), settings);
        Check("paste special: a repeat of 0 pastes once", r.Changed && Frets(p, 0).SequenceEqual(new[] { 2, 3 }));

        // Octave shift on the same instrument: pitch moves by whole octaves and is re-fingered.
        p = Song(1);
        r = EditCommands.PasteSpecial(p, twoBeats, new PasteTarget(0, 0, 0, 0), new PasteSpecialOptions(OctaveShift: 1), settings);
        var up = p.Tracks[0].Measures[0].Cells.Where(c => c.Notes.Count > 0).Select(c => (c.Notes[0].MidiValue, c.Notes[0].Fret)).ToList();
        Check("paste special: octave shift +1 raises the pitch by 12 and re-frets it", r.Changed && up.SequenceEqual(new[] { (78, 14), (79, 15) }), string.Join(",", up));
        var high = Song(2);
        high.Tracks[0].Measures[0].Cells[0] = Q(14);
        var highClip = ClipboardService.CaptureBeats(high, 0, 0, 0, 0, 0, 0);
        p = Song(1);
        r = EditCommands.PasteSpecial(p, highClip, new PasteTarget(0, 0, 0, 0), new PasteSpecialOptions(OctaveShift: -1), settings);
        Check("paste special: octave shift -1 lowers the pitch by 12",
            r.Changed && p.Tracks[0].Measures[0].Cells[0].Notes[0] is { MidiValue: 66 });
        p = Song(1);
        r = EditCommands.PasteSpecial(p, highClip, new PasteTarget(0, 0, 0, 0), new PasteSpecialOptions(OctaveShift: 2), settings);
        Check("paste special: notes shifted out of the instrument's range are left out and reported",
            !r.Changed || p.Tracks[0].Measures[0].Cells.All(c => c.Notes.Count == 0) || r.Mapping?.LeftOut.Count > 0, r.Status);

        // Keep string and fret (no re-fingering) versus keep pitch, guitar to bass.
        TrackModel Bass() => new() { Name = "Bass", Kind = TrackKind.Bass, StringTunings = new() { 43, 38, 33, 28 } };
        var bass = Song(1, new TrackModel { Name = "Guitar" }, Bass());
        r = EditCommands.PasteSpecial(bass, twoBeats, new PasteTarget(1, 0, 0, 0), new PasteSpecialOptions(KeepStringAndFret: true), settings);
        var kept = bass.Tracks[1].Measures[0].Cells.Where(c => c.Notes.Count > 0).Select(c => (c.Notes[0].StringIndex, c.Notes[0].Fret, c.Notes[0].MidiValue)).ToList();
        Check("paste special: keep string and fret leaves the fingering and follows the bass tuning",
            r.Changed && r.Asked.Count == 0 && kept.SequenceEqual(new[] { (0, 2, 45), (0, 3, 46) }), string.Join(",", kept));
        bass = Song(1, new TrackModel { Name = "Guitar" }, Bass());
        r = EditCommands.PasteSpecial(bass, twoBeats, new PasteTarget(1, 0, 0, 0), new PasteSpecialOptions(), settings);
        var pitches = bass.Tracks[1].Measures[0].Cells.Where(c => c.Notes.Count > 0).Select(c => c.Notes[0].MidiValue).ToList();
        Check("paste special: the default keeps the exact pitch instead", r.Changed && pitches.SequenceEqual(new[] { 66, 67 }));
        bass = Song(1, new TrackModel { Name = "Guitar" }, Bass());
        r = EditCommands.PasteSpecial(bass, twoBeats, new PasteTarget(1, 0, 0, 0), new PasteSpecialOptions(KeepStringAndFret: true, OctaveShift: 2), settings);
        Check("paste special: octave shift is not used together with keep string and fret",
            r.Changed && bass.Tracks[1].Measures[0].Cells[0].Notes[0] is { StringIndex: 0, Fret: 2, MidiValue: 45 });

        // The dialog: choices, limits, sizing, wording.
        var beatsDialog = new PasteSpecialDialog(ScoreClipKind.Beats);
        var barsDialog = new PasteSpecialDialog(ScoreClipKind.Bars);
        Check("paste special dialog: beats offer Replace / Insert, bars offer Overwrite / Insert before / Insert after",
            beatsDialog.ModeCount == 2 && barsDialog.ModeCount == 3 && !beatsDialog.HasBarSettings && barsDialog.HasBarSettings);
        Check("paste special dialog: defaults are a single replacing paste", beatsDialog.Current() == new PasteSpecialOptions(CopyBarSettings: true)
            && barsDialog.Current() == new PasteSpecialOptions(CopyBarSettings: true));
        beatsDialog.Set(250, 1, -1, false, true);
        Check("paste special dialog: repeat is limited to 99 and the mode and octave come back",
            beatsDialog.Current() == new PasteSpecialOptions(99, BeatPasteMode.Insert, BarsOntoNotesAnswer.Overwrite, -1, false, true));
        barsDialog.Set(3, 2, 2, false, false);
        Check("paste special dialog: bars mode and bar settings come back",
            barsDialog.Current() == new PasteSpecialOptions(3, BeatPasteMode.Replace, BarsOntoNotesAnswer.InsertAfter, 2, false, false));
        barsDialog.Set(3, 1, 2, true, true);
        Check("paste special dialog: keep string and fret turns the octave shift off",
            !barsDialog.OctaveEnabled && barsDialog.Current() is { KeepStringAndFret: true, OctaveShift: 0, BarsMode: BarsOntoNotesAnswer.InsertBefore });
        Check("paste special dialog: no result before Paste is pressed", barsDialog.Result is null);
        barsDialog.Finish(true);
        Check("paste special dialog: Paste returns the choices", barsDialog.Confirmed && barsDialog.Result is { Repeat: 3 });
        beatsDialog.Finish(false);
        Check("paste special dialog: Cancel returns nothing", !beatsDialog.Confirmed && beatsDialog.Result is null);
        var hb = beatsDialog.MeasureContentHeight();
        var hr = barsDialog.MeasureContentHeight();
        Check("paste special dialog: sizes to its content and stays compact", hb > 120 && hb < hr && hr < 480, $"{hb:0} / {hr:0}");
    }
}
