using TabForge.Models;
using TabForge.Services;

namespace TabForge;

public static partial class SelfTest
{
    /// <summary>Rest fill on, drum track: a bar of sixteen sixteenths typed one by one (two cells get a second note) is a full bar, its beats sit on
    /// sixteenths 0..15 and all 18 notes play on time, whether the sixteenth is chosen before typing or on the first typed note.</summary>
    private static void TestSixteenthBarEntry()
    {
        foreach (var lengthOnNote in new[] { false, true })
        foreach (var snareWhileWriting in new[] { false, true })
        {
            var tag = $"sixteenth bar ({(lengthOnNote ? "length set on the first note" : "length set before typing")}, {(snareWhileWriting ? "second notes while writing" : "second notes afterwards")})";
            var project = Presets.TemplateFactory.Create("Rock Band");
            var trackIndex = project.Tracks.FindIndex(t => t.Kind == TrackKind.Drums);
            var track = project.Tracks[trackIndex];
            foreach (var m in track.Measures.Take(3)) BarFill.FillCells(m.Cells, 16, always: true);
            var kick = track.StringTunings.Count - 1;
            var ed = new Views.TabEditorControl { Project = project, SelectedTrackIndex = trackIndex, FillBarsWithRests = true, AutoAdvanceAfterEntry = false };
            ed.SetPosition(0, 0, kick, false);

            void Type(int stringIndex, int a, int b) { ed.SetPosition(ed.SelectedMeasure, ed.SelectedCell, stringIndex, false); ed.Effects.EnterFret(a); ed.Effects.EnterFret(b); }
            void Bar(int count, int denominator, int[] snares)
            {
                var bar = ed.SelectedMeasure;
                for (var i = 0; i < count; i++)
                {
                    if (i == 0 && !lengthOnNote) ed.Effects.SetDuration(denominator);
                    Type(kick, 3, 6);
                    if (i == 0 && lengthOnNote) ed.Effects.SetDuration(denominator);
                    if (snareWhileWriting && snares.Contains(i)) { Type(kick - 1, 4, 0); ed.SetPosition(ed.SelectedMeasure, ed.SelectedCell, kick, false); }
                    ed.MoveBeat(1);
                }
                if (snareWhileWriting) return;
                var (backBar, backCell) = (ed.SelectedMeasure, ed.SelectedCell);
                foreach (var s in snares)
                {
                    var beats = track.Measures[bar].Cells.Select((c, i) => (c, i)).Where(x => x.c.Notes.Count > 0).Select(x => x.i).ToList();
                    if (s >= beats.Count) continue;
                    ed.SetPosition(bar, beats[s], kick - 1, false);
                    Type(kick - 1, 4, 0);
                }
                ed.SetPosition(backBar, backCell, kick, false);
            }

            Bar(8, 8, new[] { 2, 5 });
            Bar(16, 16, new[] { 4, 11 });
            Bar(16, 16, new[] { 4, 11 });

            var timeline = Playback.MidiTimelineBuilder.Build(project, new Playback.PlaybackOptions());
            var sixteenthMs = 60000.0 / project.Tempo / 4;
            foreach (var bar in new[] { 1, 2 })
            {
                var cells = track.Measures[bar].Cells;
                var dump = string.Join(" ", cells.Select((c, i) => $"{i}:{(c.Notes.Count > 0 ? "n" + c.Notes.Count : c.IsRest ? "r" : "-")}/{c.DurationDenominator}{(c.RhythmicPosition is { } p ? "@" + p : "")}"));
                var onsets = BarGrid.Onsets(cells);
                var beatOnsets = cells.Select((c, i) => (c, i)).Where(x => x.c.Notes.Count > 0).Select(x => onsets[x.i]).ToList();
                Check($"{tag}: bar {bar + 1} is not marked", !MusicTime.AnalyzeBar(project, bar, track).Marked, dump);
                Check($"{tag}: bar {bar + 1} has sixteen sixteenth beats on slots 0..15 and no rests",
                    beatOnsets.SequenceEqual(Enumerable.Range(0, 16).Select(i => (double)i)) && cells.All(c => !c.IsRest && (c.Notes.Count == 0 || c.DurationDenominator == 16)), dump);
                var played = timeline.Notes.Where(n => n.TrackIndex == trackIndex && n.Bar == bar).Select(n => (int)Math.Round((n.OnsetMs - bar * 16 * sixteenthMs) / sixteenthMs)).OrderBy(x => x).ToList();
                var expected = Enumerable.Range(0, 16).Concat(new[] { 4, 11 }).OrderBy(x => x).ToList();
                Check($"{tag}: bar {bar + 1} plays 18 notes on their sixteenths", played.SequenceEqual(expected), string.Join(",", played));
            }
        }
    }
}
