using TabForge.Models;
using TabForge.Services;

namespace TabForge;

/// <summary>Copy/paste chunk C2: BarGrid beat placement (Replace/Insert, bar-line ties, meters, tuplets, appended bars)
/// and the bar-level Overwrite/Insert operations with and without bar settings.</summary>
public static partial class SelfTest
{
    private static void TestBarGridPlacement()
    {
        static SongProject Song(int bars, int tracks = 1)
        {
            var p = new SongProject();
            for (var t = 0; t < tracks; t++)
                p.Tracks.Add(new TrackModel { Name = $"T{t}", Measures = Enumerable.Range(0, bars).Select(i => new MeasureModel { Number = i + 1 }).ToList() });
            return p;
        }
        static TabCell Beat(int fret, int denominator = 4, int tuplet = 0)
        {
            var cell = new TabCell { DurationDenominator = denominator };
            if (tuplet > 0) { cell.TupletNumerator = tuplet; cell.TupletDenominator = 2; }
            cell.Notes.Add(new TabNote { StringIndex = 0, Fret = fret, MidiValue = 64 + fret });
            return cell;
        }
        static void Put(MeasureModel m, int slot, TabCell cell) => m.Cells[slot] = cell;
        static BeatRun Run(params (double Offset, TabCell Cell)[] beats) =>
            new(beats.Select(b => new ClipBeat(b.Offset, b.Cell)).ToList(), 0);
        // "onset:fret[t]/den" per beat of a bar's voice.
        static string Bar(SongProject p, int bar, int track = 0, int voice = 0)
        {
            var cells = p.Tracks[track].Measures[bar].CellsForVoice(voice);
            var onsets = BarGrid.Onsets(cells);
            return string.Join(" ", cells.Select((c, i) => (c, i)).Where(x => x.c.Notes.Count > 0 || x.c.IsRest)
                .Select(x => FormattableString.Invariant($"{onsets[x.i]:0.###}:{(x.c.IsRest ? "r" : x.c.Notes[0].Fret.ToString())}{(x.c.Notes.Any(n => n.Tied) ? "t" : "")}/{x.c.DurationDenominator}{(x.c.Dots > 0 ? "." : "")}")));
        }
        static string Json(SongProject p) => System.Text.Json.JsonSerializer.Serialize(p.Tracks.Select(t => t.Measures).ToList());

        SongProject Four()
        {
            var p = Song(2);
            for (var i = 0; i < 4; i++) Put(p.Tracks[0].Measures[0], i * 4, Beat(i + 1));
            return p;
        }

        // Replace at beat 2: two eighths overwrite the second quarter only.
        var p = Four();
        var anchor = BarGrid.AnchorAt(p, 0, 0, 0, 4);
        var revision = p.TimelineRevision;
        var r = BarGrid.PlaceBeats(p, 0, 0, anchor, Run((0, Beat(7, 8)), (2, Beat(8, 8))), BeatPasteMode.Replace);
        Check("C2: Replace overwrites the notes at the cursor only", r.Ok && Bar(p, 0) == "0:1/4 4:7/8 6:8/8 8:3/4 12:4/4", Bar(p, 0));
        Check("C2: a paste bumps the timeline revision", p.TimelineRevision > revision);

        // Insert pushes the following notes along, through the bar line.
        p = Four();
        r = BarGrid.PlaceBeats(p, 0, 0, 4, Run((0, Beat(7, 8)), (2, Beat(8, 8))), BeatPasteMode.Insert);
        Check("C2: Insert pushes the following notes along into the next bar",
            r.Ok && Bar(p, 0) == "0:1/4 4:7/8 6:8/8 8:2/4 12:3/4" && Bar(p, 1) == "0:4/4", $"{Bar(p, 0)} | {Bar(p, 1)}");

        // Overflow: 3 quarters at beat 4 -> 1 in bar 1, 2 in bar 2; a half note at beat 4 -> quarter + tied quarter.
        p = Song(2);
        r = BarGrid.PlaceBeats(p, 0, 0, 12, Run((0, Beat(1)), (4, Beat(2)), (8, Beat(3))), BeatPasteMode.Replace);
        Check("C2: beats flow across the bar line at their onsets", r.Ok && Bar(p, 0) == "12:1/4" && Bar(p, 1) == "0:2/4 4:3/4", $"{Bar(p, 0)} | {Bar(p, 1)}");
        p = Song(2);
        r = BarGrid.PlaceBeats(p, 0, 0, 12, Run((0, Beat(5, 2))), BeatPasteMode.Replace);
        Check("C2: a half note crossing the bar line becomes quarter + tied quarter",
            r.Ok && r.TiesAdded == 1 && Bar(p, 0) == "12:5/4" && Bar(p, 1) == "0:5t/4", $"{Bar(p, 0)} | {Bar(p, 1)}");

        // 3/8 target bars: the target meter wins.
        p = Song(3);
        foreach (var m in p.Tracks[0].Measures) { m.TimeSigNum = 3; m.TimeSigDenom = 8; m.Cells = m.Cells.Take(6).ToList(); }
        r = BarGrid.PlaceBeats(p, 0, 0, 0, Run((0, Beat(1)), (4, Beat(2)), (8, Beat(3)), (12, Beat(4))), BeatPasteMode.Replace);
        Check("C2: a 4/4 run flows through 3/8 bars (split with a tie at each bar line)",
            r.Ok && Bar(p, 0) == "0:1/4 4:2/8" && Bar(p, 1) == "0:2t/8 2:3/4" && Bar(p, 2) == "0:4/4",
            $"{Bar(p, 0)} | {Bar(p, 1)} | {Bar(p, 2)}");

        // Tuplets: placed intact; a group crossing a bar line refuses the paste with the project unchanged.
        p = Song(2);
        r = BarGrid.PlaceBeats(p, 0, 0, 4, Run((0, Beat(1, 8, 3)), (4 / 3.0, Beat(2, 8, 3)), (8 / 3.0, Beat(3, 8, 3))), BeatPasteMode.Replace);
        var tupletCells = p.Tracks[0].Measures[0].Cells.Where(c => c.Notes.Count > 0).ToList();
        Check("C2: a triplet group keeps its exact onsets", r.Ok && Bar(p, 0) == "4:1/8 5.333:2/8 6.667:3/8" &&
            tupletCells.All(c => c.TupletNumerator == 3), Bar(p, 0));
        var before = Json(p);
        r = BarGrid.PlaceBeats(p, 0, 0, 14, Run((0, Beat(1, 8, 3)), (4 / 3.0, Beat(2, 8, 3)), (8 / 3.0, Beat(3, 8, 3))), BeatPasteMode.Replace);
        Check("C2: a tuplet group crossing a bar line is refused and nothing changes",
            !r.Ok && r.Error!.Contains("bar 2") && Json(p) == before, r.Error);

        // Bars appended at the end, on all tracks.
        p = Song(2, 2);
        r = BarGrid.PlaceBeats(p, 1, 0, 16, Run((0, Beat(1, 1)), (16, Beat(2, 1)), (32, Beat(3, 1))), BeatPasteMode.Replace);
        Check("C2: a paste past the song appends bars to every track",
            r.Ok && r.BarsAppended == 2 && p.Tracks.All(t => t.Measures.Count == 4) && Bar(p, 3, 1) == "0:3/1" &&
            p.Tracks[0].Measures[3].Number == 4, $"{r.BarsAppended} {Bar(p, 3, 1)}");

        // Voice 2 gets its own grid.
        p = Four();
        r = BarGrid.PlaceBeats(p, 0, 1, 8, Run((0, Beat(9))), BeatPasteMode.Replace);
        Check("C2: a voice-2 paste leaves voice 1 alone", r.Ok && Bar(p, 0, 0, 1) == "8:9/4" && Bar(p, 0) == "0:1/4 4:2/4 8:3/4 12:4/4", Bar(p, 0, 0, 1));

        // Tie at the edge: a pasted tied note with no matching note before it loses the tie.
        p = Four();
        var tied = Beat(11); tied.Notes[0].Tied = true;
        r = BarGrid.PlaceBeats(p, 0, 0, 4, Run((0, tied)), BeatPasteMode.Replace);
        Check("C2: a pasted tie without a matching previous note is dropped", r.Ok && Bar(p, 0) == "0:1/4 4:11/4 8:3/4 12:4/4", Bar(p, 0));

        // Bar level: Insert before / after on all tracks, markers follow.
        p = Song(3, 2);
        Put(p.Tracks[0].Measures[1], 0, Beat(2, 1));
        p.Markers.Add(new MarkerModel { MeasureIndex = 2, Title = "B" });
        var clipBar = new MeasureModel(); Put(clipBar, 0, Beat(9, 1));
        r = BarGrid.InsertBars(p, 1, new[] { new TrackBars(0, new[] { clipBar }) }, copySettings: true);
        Check("C2: Insert before bar 2 adds the bar on all tracks and keeps them aligned",
            r.Ok && p.Tracks.All(t => t.Measures.Count == 4) && Bar(p, 1) == "0:9/1" && Bar(p, 2) == "0:2/1" &&
            Bar(p, 1, 1) == "" && p.Markers[0].MeasureIndex == 3 && r.BarMap![1] == 2, $"{Bar(p, 1)} {Bar(p, 2)} m{p.Markers[0].MeasureIndex}");
        r = BarGrid.InsertBars(p, 3 + 1, new[] { new TrackBars(1, new[] { clipBar }) }, copySettings: true);
        Check("C2: Insert after the last bar appends on all tracks",
            r.Ok && p.Tracks.All(t => t.Measures.Count == 5) && Bar(p, 4, 1) == "0:9/1" && Bar(p, 4) == "", Bar(p, 4, 1));

        // Bar settings copy on/off.
        var waltz = new MeasureModel { TimeSigNum = 3, TimeSigDenom = 4, TempoChange = 90, RepeatStart = true, Cells = Enumerable.Range(0, 12).Select(_ => new TabCell()).ToList() };
        for (var i = 0; i < 3; i++) Put(waltz, i * 4, Beat(i + 1));
        p = Song(2, 2);
        r = BarGrid.OverwriteBars(p, 0, new[] { new TrackBars(0, new[] { waltz }) }, copySettings: true);
        Check("C2: Overwrite with bar settings copies time signature and tempo to every track, not repeats",
            r.Ok && p.Tracks.All(t => t.Measures[0].TimeSigNum == 3 && t.Measures[0].TempoChange == 90 && !t.Measures[0].RepeatStart) &&
            Bar(p, 0) == "0:1/4 4:2/4 8:3/4");
        p = Song(2, 2);
        r = BarGrid.OverwriteBars(p, 1, new[] { new TrackBars(1, new[] { waltz, waltz }) }, copySettings: false);
        Check("C2: Overwrite keeping the target's settings leaves the meter and appends bars",
            r.Ok && r.BarsAppended == 1 && p.Tracks.All(t => t.Measures.Count == 3 && t.Measures[1].TimeSigNum is null) &&
            Bar(p, 2, 1) == "0:1/4 4:2/4 8:3/4", Bar(p, 2, 1));
        var common = new MeasureModel();
        for (var i = 0; i < 4; i++) Put(common, i * 4, Beat(i + 1));
        p = Song(1);
        p.Tracks[0].Measures[0].TimeSigNum = 3; p.Tracks[0].Measures[0].TimeSigDenom = 4;
        r = BarGrid.OverwriteBars(p, 0, new[] { new TrackBars(0, new[] { common }) }, copySettings: false);
        Check("C2: Overwrite into a shorter bar drops and counts what does not fit", r.Ok && r.DroppedBeats == 1 && Bar(p, 0) == "0:1/4 4:2/4 8:3/4", Bar(p, 0));
        p = Song(2);
        r = BarGrid.InsertBars(p, 1, new[] { new TrackBars(0, new[] { waltz }) }, copySettings: false);
        Check("C2: Insert keeping the target's settings gives the new bar the meter in force", r.Ok && p.Tracks[0].Measures[1].TimeSigNum is null && r.DroppedBeats == 0);
        p = Song(2);
        r = BarGrid.InsertBars(p, 1, new[] { new TrackBars(0, new[] { waltz }) }, copySettings: true);
        Check("C2: Insert with bar settings gives the new bar the clip's meter", r.Ok && p.Tracks[0].Measures[1].TimeSigNum == 3 &&
            MusicTime.BarSlots(p, 1) == 12 && MusicTime.BarSlots(p, 2) == 16);

        Check("C2: Decompose splits into representable tied values", BarGrid.Decompose(5).SequenceEqual(new[] { (4, 0), (16, 0) }) &&
            BarGrid.Decompose(6).SequenceEqual(new[] { (4, 1) }) && BarGrid.Decompose(20).SequenceEqual(new[] { (1, 0), (4, 0) }));
    }
}
