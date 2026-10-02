using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge;

/// <summary>Copy/paste chunk C4: the paste command path (questions, remembered answers, cancel, mapping, placement, one undo step) and cut.</summary>
public static partial class SelfTest
{
    private sealed class FakePasteAsker : IPasteQuestionAsker
    {
        public Func<IReadOnlyCollection<PasteQuestion>, PasteAnswers?> Reply = _ => new PasteAnswers();
        public List<List<PasteQuestion>> Calls { get; } = new();
        public PasteAnswers? Ask(IReadOnlyCollection<PasteQuestion> questions) { Calls.Add(questions.ToList()); return Reply(questions); }
        public List<PasteQuestion> Last => Calls.Count == 0 ? new() : Calls[^1];
    }

    private static void TestPasteCommands()
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
        static TabCell Q(int fret, int s = 0, int midi = -1)
        {
            var cell = new TabCell { DurationDenominator = 4 };
            cell.Notes.Add(new TabNote { StringIndex = s, Fret = fret, MidiValue = midi >= 0 ? midi : new[] { 64, 59, 55, 50, 45, 40 }[s] + fret });
            return cell;
        }
        static List<int> Frets(SongProject p, int bar, int track = 0) =>
            p.Tracks[track].Measures[bar].Cells.Where(c => c.Notes.Count > 0).Select(c => c.Notes[0].Fret).ToList();
        static string Snap(SongProject p) => ProjectService.Snapshot(p);
        TrackModel Bass() => new() { Name = "Bass", Kind = TrackKind.Bass, StringTunings = new() { 43, 38, 33, 28 } };

        // Source: bar 0 = quarters fret 1,2,3,4 on the top string.
        var src = Song(2);
        for (var i = 0; i < 4; i++) src.Tracks[0].Measures[0].Cells[i * 4] = Q(i + 1);
        var twoBeats = ClipboardService.CaptureBeats(src, 0, 0, 0, 4, 0, 8);   // frets 2, 3
        var wholeBar = ClipboardService.CaptureSelection(src, 0, 0, 0, 0, 0, -1);

        // Beats onto an empty bar: no question; lands at the cursor (cell 5 = old bug: only cell 0 worked).
        var p = Song(2);
        var asker = new FakePasteAsker();
        var r = EditCommands.Paste(p, twoBeats, new PasteTarget(0, 0, 0, 4), new EditingSettings(), asker);
        Check("paste: beats onto an empty bar ask nothing and land at the cursor",
            r.Changed && asker.Calls.Count == 0 && Frets(p, 0).SequenceEqual(new[] { 2, 3 }) && p.Tracks[0].Measures[0].Cells[4].Notes.Count == 1);

        // Q1 Replace vs Insert, cancel, remembered answers.
        SongProject Full() { var s = Song(2); for (var i = 0; i < 4; i++) s.Tracks[0].Measures[0].Cells[i * 4] = Q(10 + i); return s; }
        p = Full();
        var before = Snap(p);
        asker = new FakePasteAsker { Reply = _ => null };
        r = EditCommands.Paste(p, twoBeats, new PasteTarget(0, 0, 0, 0), new EditingSettings(), asker);
        Check("paste: Q1 asked for beats onto notes; cancel changes nothing",
            asker.Last.SequenceEqual(new[] { PasteQuestion.BeatsOntoNotes }) && r.Cancelled && !r.Changed && Snap(p) == before);
        asker = new FakePasteAsker { Reply = _ => new PasteAnswers(BeatsOntoNotes: BeatsOntoNotesAnswer.Replace) };
        EditCommands.Paste(p, twoBeats, new PasteTarget(0, 0, 0, 0), new EditingSettings(), asker);
        Check("paste: Replace overwrites the notes at the cursor", Frets(p, 0).SequenceEqual(new[] { 2, 3, 12, 13 }));
        p = Full();
        var settings = new EditingSettings();
        asker = new FakePasteAsker { Reply = _ => new PasteAnswers(BeatsOntoNotes: BeatsOntoNotesAnswer.Insert, RememberBeatsOntoNotes: true) };
        EditCommands.Paste(p, twoBeats, new PasteTarget(0, 0, 0, 0), settings, asker);
        Check("paste: Insert pushes the following notes along (into the next bar)",
            Frets(p, 0).SequenceEqual(new[] { 2, 3, 10, 11 }) && Frets(p, 1).SequenceEqual(new[] { 12, 13 }));
        Check("paste: a ticked 'Remember my choice' is stored", settings.PasteBeatsOntoNotes == nameof(BeatsOntoNotesAnswer.Insert));
        p = Full();
        asker = new FakePasteAsker { Reply = _ => null };
        r = EditCommands.Paste(p, twoBeats, new PasteTarget(0, 0, 0, 0), settings, asker);
        Check("paste: a remembered answer skips the asker", r.Changed && asker.Calls.Count == 0 && Frets(p, 1).SequenceEqual(new[] { 12, 13 }));

        // Q2 guitar -> bass (keep pitch / shift octave), every combination with Q1.
        foreach (var beatsAnswer in new[] { BeatsOntoNotesAnswer.Replace, BeatsOntoNotesAnswer.Insert })
            foreach (var octave in new[] { OctaveAnswer.KeepPitch, OctaveAnswer.ShiftOctave })
            {
                var song = Song(2, new TrackModel { Name = "Guitar" }, Bass());
                song.Tracks[1].Measures[0].Cells[0] = new TabCell { DurationDenominator = 4, Notes = { new TabNote { StringIndex = 3, Fret = 0, MidiValue = 28 } } };
                asker = new FakePasteAsker { Reply = _ => new PasteAnswers(BeatsOntoNotes: beatsAnswer, Octave: octave) };
                r = EditCommands.Paste(song, twoBeats, new PasteTarget(1, 0, 0, 0), new EditingSettings(), asker);
                var pitches = song.Tracks[1].Measures[0].Cells.Where(c => c.Notes.Count > 0).Select(c => c.Notes[0].MidiValue).ToList();
                var want = octave == OctaveAnswer.KeepPitch ? new[] { 66, 67 } : new[] { 54, 55 };
                Check($"paste: guitar to bass {beatsAnswer}/{octave} asks Q1+Q2 and maps the pitch",
                    r.Changed && asker.Last.SequenceEqual(new[] { PasteQuestion.BeatsOntoNotes, PasteQuestion.Octave }) &&
                    pitches.Take(2).SequenceEqual(want) && song.Tracks[1].Measures[0].Cells.SelectMany(c => c.Notes).All(n => n.StringIndex < 4) &&
                    (beatsAnswer == BeatsOntoNotesAnswer.Insert) == pitches.Contains(28));
            }

        // Q5 guitar -> drums.
        foreach (var drums in new[] { DrumsAnswer.RhythmOntoOneSound, DrumsAnswer.DontPaste })
        {
            var song = Song(1, new TrackModel { Name = "Drums", Kind = TrackKind.Drums, MidiChannel = 9 });
            before = Snap(song);
            asker = new FakePasteAsker { Reply = _ => new PasteAnswers(Drums: drums) };
            r = EditCommands.Paste(song, twoBeats, new PasteTarget(0, 0, 0, 0), new EditingSettings(), asker);
            Check($"paste: guitar onto drums asks Q5 ({drums})", asker.Last.SequenceEqual(new[] { PasteQuestion.Drums }) &&
                (drums == DrumsAnswer.DontPaste
                    ? !r.Changed && Snap(song) == before
                    : r.Changed && song.Tracks[0].Measures[0].Cells.Where(c => c.Notes.Count > 0).All(c => c.Notes[0].MidiValue == NoteMapper.DefaultDrumSound)));
        }

        // Beats into 3/8 bars: flow by the target meter with a tie across the bar line.
        var eighths = Song(3);
        foreach (var m in eighths.Tracks[0].Measures) { m.TimeSigNum = 3; m.TimeSigDenom = 8; m.Cells = Enumerable.Range(0, 6).Select(_ => new TabCell()).ToList(); }
        var threeQuarters = ClipboardService.CaptureBeats(src, 0, 0, 0, 0, 0, 8);
        r = EditCommands.Paste(eighths, threeQuarters, new PasteTarget(0, 0, 0, 0), new EditingSettings(), new FakePasteAsker());
        Check("paste: quarters into 3/8 bars flow by the target meter and tie over the bar line",
            r.Changed && Frets(eighths, 0).SequenceEqual(new[] { 1, 2 }) && eighths.Tracks[0].Measures[1].Cells[0].Notes.Any(n => n.Tied) &&
            Frets(eighths, 1).Contains(3) && BarGrid.Onsets(eighths.Tracks[0].Measures[1].Cells).Length == 6);

        // Bars: Q3 every answer x Q4 every answer, on two tracks; one undo reverts each.
        TrackModel Guitar2() => new() { Name = "Rhythm" };
        foreach (var bars in new[] { BarsOntoNotesAnswer.Overwrite, BarsOntoNotesAnswer.InsertBefore, BarsOntoNotesAnswer.InsertAfter })
            foreach (var keep in new[] { BarSettingsAnswer.CopySettings, BarSettingsAnswer.KeepTarget })
            {
                var song = Song(3, new TrackModel { Name = "Lead" }, Guitar2());
                song.Tracks[0].Measures[1].Cells[0] = Q(7);
                song.Tracks[1].Measures[1].Cells[0] = Q(9);
                var clip = ClipboardService.CaptureSelection(src, 0, 0, 0, 0, 0, -1);
                clip.Tracks[0].Bars[0].TimeSigNum = 3; clip.Tracks[0].Bars[0].TimeSigDenom = 4;
                before = Snap(song);
                var doc = DocumentSession.FromProject(song, null);
                var undo = doc.Undo;
                asker = new FakePasteAsker { Reply = _ => new PasteAnswers(BarsOntoNotes: bars, BarSettings: keep) };
                var pasted = DocumentEdits.Run<PasteOutcome>(doc, sp => EditCommands.Paste(sp, clip, new PasteTarget(0, 0, 1, 0), new EditingSettings(), asker) is { Changed: true } o ? o : null);
                r = pasted.Value ?? new PasteOutcome();
                var capture = pasted.Capture;
                var insert = bars != BarsOntoNotesAnswer.Overwrite;
                var landed = bars == BarsOntoNotesAnswer.InsertAfter ? 2 : 1;
                var ok = r.Changed && asker.Last.SequenceEqual(new[] { PasteQuestion.BarsOntoNotes, PasteQuestion.BarSettings }) &&
                    song.Tracks.All(t => t.Measures.Count == (insert ? 4 : 3)) &&
                    Frets(song, landed).Take(3).SequenceEqual(new[] { 1, 2, 3 }) &&
                    Frets(song, landed, 1).SequenceEqual(insert ? Array.Empty<int>() : new[] { 9 }) &&
                    (insert ? r.BarMap is not null : r.BarMap is null) &&
                    (song.Tracks[0].Measures[landed].TimeSigNum == 3) == (keep == BarSettingsAnswer.CopySettings);
                var undone = capture.Stored && undo.TryUndo(undo.Snapshot(song), out var target) && Snap(undo.Restore(target)) == before;
                Check($"paste: bars {bars}/{keep} on all tracks, one undo step reverts it", ok && undone && undo.UndoCount == 0);
            }

        // Rests-only target bars and bars past the end: no Q3.
        var rests = Song(1);
        rests.Tracks[0].Measures[0].Cells[0] = new TabCell { IsRest = true, DurationDenominator = 1 };
        asker = new FakePasteAsker();
        r = EditCommands.Paste(rests, wholeBar, new PasteTarget(0, 0, 0, 0), new EditingSettings(), asker);
        var r2 = EditCommands.Paste(rests, wholeBar, new PasteTarget(0, 0, 1, 0), new EditingSettings(), asker);
        Check("paste: a rests-only bar and bars past the end paste without a question",
            r.Changed && r2.Changed && asker.Calls.Count == 0 && rests.Tracks[0].Measures.Count == 2 && Frets(rests, 1).SequenceEqual(new[] { 1, 2, 3, 4 }));

        // Cut: whole bars are emptied (not deleted); beats become rests and the following beats keep their onsets.
        var cut = Song(2);
        for (var i = 0; i < 4; i++) cut.Tracks[0].Measures[0].Cells[i] = Q(i + 1);   // packed: onsets 0, 4, 8, 12 by the grid rule
        cut.Tracks[0].Measures[1].Cells[0] = Q(5);
        cut.Tracks[0].Measures[1].Voice2Cells.Add(Q(6));
        var onsetsBefore = BarGrid.Onsets(cut.Tracks[0].Measures[0].Cells);
        var beatsCut = EditCommands.CutClear(cut, ScoreClipKind.Beats, 0, 0, 0, 1, 0, 2);
        var onsetsAfter = BarGrid.Onsets(cut.Tracks[0].Measures[0].Cells);
        Check("cut: beats are cleared and the following beat keeps its onset",
            beatsCut && Frets(cut, 0).SequenceEqual(new[] { 1, 4 }) && Math.Abs(onsetsAfter[3] - onsetsBefore[3]) < 1e-6);
        var barsCut = EditCommands.CutClear(cut, ScoreClipKind.Bars, 0, 0, 1, 0, 1, -1);
        Check("cut: whole bars are emptied in both voices, not deleted",
            barsCut && cut.Tracks[0].Measures.Count == 2 && cut.Tracks[0].Measures[1].Cells.All(c => c.Notes.Count == 0) &&
            cut.Tracks[0].Measures[1].Voice2Cells.All(c => c.Notes.Count == 0));
    }
}
