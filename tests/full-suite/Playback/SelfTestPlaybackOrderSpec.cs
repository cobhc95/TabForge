using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;

namespace TabForge;

/// <summary>
/// The worked examples of the playback-order specification (docs/PLAYBACK_ORDER_SPEC.md, section 7), one check
/// per example, named by its id. Structures are written the way the specification writes them:
/// <c>"0 |:; 1 [1.] :|; 2 [2.]; 3 :|x3; 4 DaCapo"</c> (bar index, then its marks).
/// </summary>
public static partial class SelfTest
{
    private sealed record OrderExample(string Id, string Summary, int Bars, string Structure, int[] Expected,
        int StartBar = 0, bool RepeatExpansion = true, int SecondTrackBars = 0);

    private static void TestPlaybackOrderSpec()
    {
        foreach (var example in OrderExamples())
        {
            var project = OrderExampleProject(example);
            var order = PlaybackOrder.Build(project, new PlaybackOptions { StartBar = example.StartBar, RepeatExpansion = example.RepeatExpansion });
            Check($"{example.Id}: {example.Summary}", order.SequenceEqual(example.Expected),
                $"expected {string.Join(",", example.Expected)}, got {string.Join(",", order)}");
        }

        // Loop range and bounds (4/4 at 120 bpm: 2000 ms a bar, 16 slots).
        var song = SingleTrack(4);
        var loop = new PlaybackOptions { Loop = true, LoopStartBar = 1, LoopEndBar = 2 };
        var timeline = MidiTimelineBuilder.Build(song, loop);
        var bounds = PlaybackOrder.LoopBounds(timeline, song, loop);
        Check("E64: loop bars 1 to 2 span 2000 to 6000 ms", bounds.available &&
            Math.Abs(bounds.startMs - 2000) < 0.5 && Math.Abs(bounds.endMs - 6000) < 0.5, bounds.ToString());

        loop.LoopStartCell = 4;
        loop.LoopEndCell = 7;
        bounds = PlaybackOrder.LoopBounds(timeline, song, loop);
        Check("E65: loop cells 4 to 7 span 2500 to 5000 ms", bounds.available &&
            Math.Abs(bounds.startMs - 2500) < 0.5 && Math.Abs(bounds.endMs - 5000) < 0.5, bounds.ToString());

        var reversed = PlaybackOrder.LoopRange(song, new PlaybackOptions { LoopStartBar = 3, LoopEndBar = 1 });
        Check("E66: loop bars 3 to 1 give the range 1 to 3", reversed == (1, 3), reversed.ToString());

        var outside = PlaybackOrder.LoopRange(song, new PlaybackOptions { LoopStartBar = -2, LoopEndBar = 9 });
        Check("E67: loop bars -2 to 9 are clamped to 0 to 3", outside == (0, 3), outside.ToString());

        var missing = PlaybackOrder.LoopBounds(new ScoreTimeline(), song, new PlaybackOptions { LoopStartBar = 1, LoopEndBar = 2 });
        Check("E68: a loop bar missing from the timeline gives no bounds", missing == (0, 0, false), missing.ToString());
    }

    private static IEnumerable<OrderExample> OrderExamples()
    {
        static int[] Seq(params int[] bars) => bars;
        static int[] Times(int count, params int[] bars) => Enumerable.Repeat(bars, count).SelectMany(b => b).ToArray();
        static int[] Cat(params int[][] parts) => parts.SelectMany(p => p).ToArray();

        // Repeats
        yield return new("E1", "a repeat plays its bars twice", 4, "0 |:; 2 :|", Seq(0, 1, 2, 0, 1, 2, 3));
        yield return new("E2", "a close without an open repeats from the song start", 3, "1 :|", Seq(0, 1, 0, 1, 2));
        yield return new("E3", "a repeat count below 2 counts as 2", 3, "0 :|x1", Seq(0, 0, 1, 2));
        yield return new("E4", "a close plays its repeat count", 3, "0 |:; 1 :|x3", Seq(0, 1, 0, 1, 0, 1, 2));
        yield return new("E5", "a repeat count of 0 counts as 2", 3, "0 |:; 1 :|x0", Seq(0, 1, 0, 1, 2));
        yield return new("E6", "a repeat count above 99 counts as 99", 5, "0 |:; 1 :|x100", Cat(Times(99, 0, 1), Seq(2, 3, 4)));
        yield return new("E7", "a one-bar repeat plays its bar on every pass", 4, "0 |: :|x4; 1 |:; 2 :|", Seq(0, 0, 0, 0, 1, 2, 1, 2, 3));
        yield return new("E8", "a close returns to the most recent open", 5, "1 |:; 2 |:; 3 :|x12", Cat(Seq(0, 1), Times(12, 2, 3), Seq(4)));
        yield return new("E9", "two repeats in a row", 5, "0 |:; 1 :|; 2 |:; 3 :|", Seq(0, 1, 0, 1, 2, 3, 2, 3, 4));
        yield return new("E10", "a close without an open returns to the bar after the previous close", 5, "0 |:; 1 :|; 3 :|", Seq(0, 1, 0, 1, 2, 3, 2, 3, 4));
        yield return new("E11", "two closes without opens", 3, "0 :|; 2 :|", Seq(0, 0, 1, 2, 1, 2));
        yield return new("E12", "a close after a one-bar repeat returns to the bar after it", 4, "0 |:; 1 |: :|; 2 :|", Seq(0, 1, 1, 2, 2, 3));

        // Alternate endings
        yield return new("E13", "first pass uses ending 1, second ending 2 (bracket runs to the close)", 4, "0 |:; 1 [1.]; 2 [2.]; 3 :|", Seq(0, 1, 0, 2, 3));
        yield return new("E14", "a first-ending bracket spans every bar up to the close", 6, "0 |:; 2 [1.]; 3 :|; 4 [2.]", Seq(0, 1, 2, 3, 0, 1, 4, 5));
        yield return new("E15", "a 1.2. ending with a close and no open, then the 3. ending", 4, "1 [1.2.] :|x3; 2 [3.]", Seq(0, 1, 0, 1, 0, 2, 3));
        yield return new("E16", "the repeat count belongs to the close, not the ending", 4, "0 |:; 1 [1.2.3.] :|x2; 2 [4.]", Seq(0, 1, 0, 1, 2, 3));
        yield return new("E17", "each close of a repeat with several endings sends playback back once", 6, "0 |:; 1 [1.] :|; 2 [2.] :|; 3 [3.]", Seq(0, 1, 0, 2, 0, 3, 4, 5));
        yield return new("E18", "an ending bar that opens the next repeat is repeated with it", 5, "0 |:; 1 [1.] :|; 2 [2.] |:; 3 :|", Seq(0, 1, 0, 2, 3, 2, 3, 4));
        yield return new("E19", "two consecutive first-ending bars", 6, "0 |:; 1 [1.]; 2 [1.]; 3 :|; 4 [2.]", Seq(0, 1, 2, 3, 0, 4, 5));
        yield return new("E20", "an unmarked bar inside the bracket takes its passes", 6, "0 |:; 1 [1.]; 3 :|; 4 [2.]", Seq(0, 1, 2, 3, 0, 4, 5));
        yield return new("E21", "a two-bar ending without a close", 5, "0 |:; 1 [1.] :|; 2 [2.]; 3 [2.]", Seq(0, 1, 0, 2, 3, 4));
        yield return new("E22", "a skipped close with jumps left still sends playback back", 5, "0 |:; 1 [1.2.] :|x4; 2 [3.]", Seq(0, 1, 0, 1, 0, 0, 2, 3, 4));
        yield return new("E23", "a first ending with ten passes", 4, "0 |:; 1 [1.] :|x10; 2 [2.]", Cat(Seq(0, 1), Times(9, 0), Seq(2, 3)));
        yield return new("E24", "endings name passes 1 to 8 only", 4, "0 |:; 1 [1.2.3.4.5.6.7.8.] :|x10", Cat(Times(8, 0, 1), Seq(0, 0, 2, 3)));
        yield return new("E25", "an ending on the first bar that also opens the repeat", 3, "0 |: [1.]; 1 :|; 2 [2.]", Seq(0, 1, 0, 1, 2));
        yield return new("E26", "a stray second ending without repeats is skipped", 4, "1 [2.]", Seq(0, 2, 3));
        yield return new("E27", "a stray first ending without repeats plays", 4, "1 [1.]", Seq(0, 1, 2, 3));
        yield return new("E28", "a repeat after a three-ending repeat", 7, "0 |:; 1 [1.] :|; 2 [2.] :|; 3 [3.]; 4 |:; 5 :|", Seq(0, 1, 0, 2, 0, 3, 4, 5, 4, 5, 6));

        // Directions
        yield return new("E29", "Dal Segno repeats from the Segno through Fine", 4, "0 Segno; 2 Fine; 3 DalSegno", Seq(0, 1, 2, 3, 0, 1, 2));
        yield return new("E30", "D.S. al Fine: the D.S. bar is played, then back to the Segno and stop at Fine", 6, "1 Segno; 3 Fine; 4 DalSegno", Seq(0, 1, 2, 3, 4, 1, 2, 3));
        yield return new("E31", "Fine before any jump does not end playback", 4, "1 Fine", Seq(0, 1, 2, 3));
        yield return new("E32", "D.C. al Coda finds the coda from the song start", 5, "1 Coda; 3 ToCoda; 4 DaCapoAlCoda", Seq(0, 1, 2, 3, 4, 0, 1, 2, 3, 1, 2, 3, 4));
        yield return new("E33", "D.C. al Coda: To Coda is ignored before the jump", 6, "2 ToCoda; 3 DaCapoAlCoda; 4 Coda", Seq(0, 1, 2, 3, 0, 1, 2, 4, 5));
        yield return new("E34", "D.S.S. jumps to the double segno", 6, "0 Segno; 1 SegnoSegno; 3 Fine; 4 DalSegnoSegno", Seq(0, 1, 2, 3, 4, 1, 2, 3));
        yield return new("E35", "D.S.S. without a double segno jumps to the segno", 6, "0 Segno; 1 DalSegnoSegno", Seq(0, 1, 0, 1, 2, 3, 4, 5));
        yield return new("E36", "D.S. without a segno is ignored", 5, "0 DalSegno", Seq(0, 1, 2, 3, 4));
        yield return new("E37", "a later D.C. is taken after an ignored D.S.", 6, "1 DalSegno; 3 DaCapo", Seq(0, 1, 2, 3, 0, 1, 2, 3, 4, 5));
        yield return new("E38", "only one D.C. is taken", 6, "1 DaCapo; 3 DaCapo", Seq(0, 1, 0, 1, 2, 3, 4, 5));
        yield return new("E39", "Fine on the first bar ends playback after the D.C.", 6, "0 Fine; 2 DaCapo", Seq(0, 1, 2, 0));
        yield return new("E40", "Fine wins over To Coda after the jump", 6, "1 ToCoda Fine; 2 DaCapoAlCoda; 4 Coda", Seq(0, 1, 2, 0, 1));
        yield return new("E41", "To Coda is ignored after a plain D.C.", 6, "1 ToCoda; 2 DaCapo; 3 Coda", Seq(0, 1, 2, 0, 1, 2, 3, 4, 5));
        yield return new("E42", "To Coda leads to a double coda", 6, "1 ToCoda; 2 DaCapoAlCoda; 4 DoubleCoda", Seq(0, 1, 2, 0, 1, 4, 5));
        yield return new("E43", "To Double Coda leads to a coda", 6, "0 Segno; 1 ToDoubleCoda; 2 DalSegnoAlCoda; 4 Coda", Seq(0, 1, 2, 0, 1, 4, 5));
        yield return new("E44", "stored Target/Jump spellings", 5, "0 TargetSegno; 1 TargetSegnoSegno; 2 JumpDaDoubleCoda; 3 JumpDalSegnoSegnoAlCoda; 4 TargetDoubleCoda", Seq(0, 1, 2, 3, 1, 2, 4));
        yield return new("E45", "stored D.S. al Fine spelling", 5, "1 TargetSegno; 2 TargetFine; 3 JumpDalSegnoAlFine", Seq(0, 1, 2, 3, 1, 2));
        yield return new("E46", "stored D.C. al Fine spelling", 5, "1 Fine; 3 JumpDaCapoAlFine", Seq(0, 1, 2, 3, 0, 1));
        yield return new("E47", "an unknown spelling is ignored", 5, "2 DaCapoAlFine; 3 Fine", Seq(0, 1, 2, 3, 4));
        yield return new("E48", "direction names are compared case-insensitively", 6, "2 daCapo", Seq(0, 1, 2, 0, 1, 2, 3, 4, 5));

        // Directions with repeats
        yield return new("E49", "after a D.C., only the last ending plays", 6, "0 |:; 2 [1.]; 3 :|; 4 [2.]; 5 DaCapo", Seq(0, 1, 2, 3, 0, 1, 4, 5, 0, 1, 4, 5));
        yield return new("E50", "after a D.C., only the third ending plays", 6, "0 |:; 1 [1.] :|; 2 [2.] :|; 3 [3.]; 4 DaCapo", Seq(0, 1, 0, 2, 0, 3, 4, 0, 3, 4, 5));
        yield return new("E51", "after a D.S., the first ending is left out", 6, "0 |: Segno; 1 [1.] :|; 2 [2.]; 3 DalSegno", Seq(0, 1, 0, 2, 3, 0, 2, 3, 4, 5));
        yield return new("E52", "a D.S. on a close is taken after the repeat plays out", 4, "0 |: Segno; 1 :| DalSegno", Seq(0, 1, 0, 1, 0, 1, 2, 3));
        yield return new("E53", "a D.S. inside a later repeat is taken on its last pass", 6, "0 |:; 1 :|; 2 Segno; 3 |:; 4 :| DalSegno", Seq(0, 1, 0, 1, 2, 3, 4, 3, 4, 2, 3, 4, 5));
        yield return new("E54", "after a D.C., the first ending is left out when the second opens a repeat", 6, "0 |:; 1 [1.] :|; 2 [2.] |:; 3 :|; 4 DaCapo", Seq(0, 1, 0, 2, 3, 2, 3, 4, 0, 2, 3, 4, 5));

        // Options and edge cases
        yield return new("E55", "start bar 1 keeps later passes from bar 1", 4, "0 |:; 2 :|", Seq(1, 2, 1, 2, 3), StartBar: 1);
        yield return new("E56", "start bar 3 plays bar 3", 4, "0 |:; 2 :|", Seq(3), StartBar: 3);
        yield return new("E57", "a start bar past the song gives nothing", 4, "0 |:; 2 :|", Seq(), StartBar: 9);
        yield return new("E58", "repeat expansion off plays each bar once", 4, "0 |:; 2 :|", Seq(0, 1, 2, 3), RepeatExpansion: false);
        yield return new("E59", "start bar 2 filters the D.S. order", 6, "1 Segno; 3 Fine; 4 DalSegno", Seq(2, 3, 4, 2, 3), StartBar: 2);
        yield return new("E60", "a start bar the filter leaves out plays alone", 6, "0 Fine; 2 DaCapo", Seq(4), StartBar: 4);
        yield return new("E61", "a score without bars gives nothing", 0, "", Seq());
        yield return new("E62", "repeat expansion off still keeps only the last ending after a D.C.", 6, "0 |:; 2 [1.]; 3 :|; 4 [2.]; 5 DaCapo", Seq(0, 1, 2, 3, 4, 5, 0, 1, 4, 5), RepeatExpansion: false);
        yield return new("E63", "only the first track carries structure; longer tracks add plain bars", 2, "0 |:; 1 :|", Seq(0, 1, 0, 1, 2, 3), SecondTrackBars: 4);
    }

    /// <summary>Builds the example's song from its bar count and structure text.</summary>
    private static SongProject OrderExampleProject(OrderExample example)
    {
        var project = new SongProject { Tempo = 120 };
        var track = new TrackModel { Name = "Gtr", Measures = TemplateFactory.Measures(example.Bars) };
        project.Tracks.Add(track);
        if (example.SecondTrackBars > 0)
            project.Tracks.Add(new TrackModel { Name = "Bass", Measures = TemplateFactory.Measures(example.SecondTrackBars) });

        foreach (var entry in example.Structure.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var bar = track.Measures[int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture)];
            var directions = new List<string>();
            foreach (var mark in parts.Skip(1))
            {
                if (mark == "|:") bar.RepeatStart = true;
                else if (mark.StartsWith(":|", StringComparison.Ordinal))
                {
                    bar.RepeatEnd = true;
                    bar.RepeatCount = mark.Length > 3 ? int.Parse(mark[3..], System.Globalization.CultureInfo.InvariantCulture) : 2;
                }
                else if (mark.StartsWith('[') && mark.EndsWith(']'))
                {
                    var passes = mark[1..^1].Split('.', StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => int.Parse(p, System.Globalization.CultureInfo.InvariantCulture));
                    bar.AlternateEndingMask = passes.Aggregate(0, (mask, pass) => mask | (1 << (pass - 1)));
                }
                else directions.Add(mark);
            }
            bar.Directions = string.Join(",", directions);
        }
        return project;
    }
}
