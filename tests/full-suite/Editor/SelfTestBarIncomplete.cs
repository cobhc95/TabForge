using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge;

public static partial class SelfTest
{
    /// <summary>Feature 6a: a bar with less music than its time signature is marked like an error bar; an empty bar, a full bar and a pickup bar are not.</summary>
    private static void TestBarIncompleteMarking()
    {
        static void Quarter(MeasureModel bar, int cell) { bar.Cells[cell].DurationDenominator = 4; bar.Cells[cell].Notes.Add(new TabNote { StringIndex = 0, Fret = 3, MidiValue = 67 }); }
        var p = TemplateFactory.Blank();
        var bar = p.Tracks[0].Measures[0];

        Check("empty bar is not marked", !MusicTime.AnalyzeBar(p, 0).Marked);
        Quarter(bar, 0); Quarter(bar, 4);
        var half = MusicTime.AnalyzeBar(p, 0);
        Check("half-empty bar (2 of 4 quarters) is marked, not an error", half.Marked && half.Short && !half.Error && !half.Complete, half.ToString());
        Quarter(bar, 8); Quarter(bar, 12);
        Check("full bar is not marked", !MusicTime.AnalyzeBar(p, 0).Marked);
        bar.Cells[12] = new TabCell { DurationDenominator = 2, Notes = { new TabNote { StringIndex = 0, Fret = 5 } } };
        var over = MusicTime.AnalyzeBar(p, 0);
        Check("overfull bar is still an error and marked", over.Error && over.Marked && !over.Short, over.ToString());

        bar.Cells[12] = new TabCell(); bar.Cells[8] = new TabCell();
        bar.Anacrusis = true;
        Check("pickup (anacrusis) bar with short content is not marked", !MusicTime.AnalyzeBar(p, 0).Marked);
        bar.Anacrusis = false;
        bar.Cells[8] = new TabCell { IsRest = true, DurationDenominator = 2 };
        Check("a half rest fills the bar: not marked", !MusicTime.AnalyzeBar(p, 0).Marked);
    }

    /// <summary>
    /// Bars are judged per track on voice 1: seven eighths in 4/4 are short even beside a full track; grace notes take no time;
    /// ties, palm mutes and tuplets at their real length fill a bar; a partly filled voice 2 is optional; any overfull voice is an
    /// error; the shipped demo song has no marked bar.
    /// </summary>
    private static void TestBarFillPerTrackVoice()
    {
        static TabCell Beat(int den, int fret = 3) => new() { DurationDenominator = den, Notes = { new TabNote { StringIndex = 0, Fret = fret, MidiValue = 64 + fret } } };
        static SongProject TwoTracks()
        {
            var p = TemplateFactory.Blank();
            if (p.Tracks.Count < 2) p.Tracks.Add(new TrackModel { Measures = { new MeasureModel() } });
            var other = p.Tracks[1].Measures[0];
            while (other.Cells.Count < 16) other.Cells.Add(new TabCell());
            other.Cells[0] = new TabCell { IsRest = true, DurationDenominator = 1 };
            return p;
        }

        var p = TwoTracks();
        var bar = p.Tracks[0].Measures[0];
        for (var i = 0; i < 14; i += 2) bar.Cells[i] = Beat(8);
        Check("seven eighths in 4/4 are short beside a full track", MusicTime.AnalyzeBar(p, 0).Short);
        Check("seven eighths: red in their own track's view", MusicTime.AnalyzeBar(p, 0, p.Tracks[0]).Short);
        Check("seven eighths: the full track's own view is not red", !MusicTime.AnalyzeBar(p, 0, p.Tracks[1]).Marked);

        // A normal bar: grace note, palm-muted eighths, a tie and an eighth-note triplet (3 x 4/3 slots).
        p = TwoTracks(); bar = p.Tracks[0].Measures[0];
        bar.Cells[0] = new TabCell { IsGrace = true, DurationDenominator = 32, RhythmicPosition = 0, Notes = { new TabNote { Fret = 5, IsGraceNote = true } } };
        bar.Cells[1] = Beat(4); bar.Cells[1].RhythmicPosition = 0;
        for (var i = 4; i < 8; i += 2) { bar.Cells[i] = Beat(8); bar.Cells[i].Notes[0].Techniques.Add("PalmMute"); }
        bar.Cells[8] = Beat(4); bar.Cells[8].IsTied = true; bar.Cells[8].Notes[0].Tied = true;
        for (var k = 0; k < 3; k++)
        {
            var cell = Beat(8); cell.TupletNumerator = 3; cell.TupletDenominator = 2; cell.RhythmicPosition = 12 + k * 4.0 / 3; bar.Cells[12 + k] = cell;
        }
        var normal = MusicTime.AnalyzeBar(p, 0);
        Check("grace, palm mute, tie and triplet bar is not red", !normal.Marked, normal.ToString());

        // An imported grace is merged into its main beat (the cell is flagged IsGrace): that beat keeps its length.
        p = TwoTracks(); bar = p.Tracks[0].Measures[0];
        for (var i = 0; i < 16; i += 4) bar.Cells[i] = Beat(4);
        bar.Cells[12].IsGrace = true; bar.Cells[12].Notes.Add(new TabNote { StringIndex = 1, Fret = 2, IsGraceNote = true });
        Check("a main beat carrying a merged grace note still counts", !MusicTime.AnalyzeBar(p, 0, p.Tracks[0]).Marked);
        // A lone quarter rest is how an imported empty bar arrives: silence, not a short bar.
        p = TwoTracks(); bar = p.Tracks[0].Measures[0];
        bar.Cells[0] = new TabCell { IsRest = true, DurationDenominator = 4 };
        Check("a rest-only voice 1 is not red", !MusicTime.AnalyzeBar(p, 0, p.Tracks[0]).Marked);

        // Full voice 1, voice 2 with one quarter and a quarter rest: voice 2 is optional and not judged short.
        p = TwoTracks(); bar = p.Tracks[0].Measures[0];
        for (var i = 0; i < 16; i += 4) bar.Cells[i] = Beat(4);
        bar.Voice2Cells.AddRange(Enumerable.Range(0, 16).Select(_ => new TabCell()));
        bar.Voice2Cells[0] = Beat(4, 0); bar.Voice2Cells[4] = new TabCell { IsRest = true, DurationDenominator = 4 };
        Check("a partly filled voice 2 is not red", !MusicTime.AnalyzeBar(p, 0).Marked && !MusicTime.AnalyzeBar(p, 0, p.Tracks[0]).Marked);
        bar.Voice2Cells[12] = Beat(2, 0);
        Check("an overfull voice 2 is red", MusicTime.AnalyzeBar(p, 0, p.Tracks[0]).Error);

        p = TwoTracks(); bar = p.Tracks[0].Measures[0];
        for (var i = 0; i < 12; i += 4) bar.Cells[i] = Beat(4);
        bar.Cells[12] = Beat(2);
        Check("an overfull voice 1 is red", MusicTime.AnalyzeBar(p, 0).Error && MusicTime.AnalyzeBar(p, 0, p.Tracks[0]).Marked);

        foreach (var name in new[] { "TabForge Demo - Ashen Meridian.gp5", "TabForge Demo - Ashen Meridian.gp" })
        {
            var bytes = FuzzFindSample(name);
            if (bytes is null) { Check($"bar fill: {name} is present", false); continue; }
            var song = GuitarProImporter.ImportBytes(bytes, name);
            var red = new List<string>();
            foreach (var track in song.Tracks)
                for (var b = 0; b < track.Measures.Count; b++)
                    if (MusicTime.AnalyzeBar(song, b, track) is { Marked: true } st) red.Add($"{track.Name} bar {b + 1} {st} [" + string.Join(" ", track.Measures[b].Cells.Select((c, i) => c.Notes.Count > 0 || c.IsRest ? $"{i}:{c.DurationDenominator}{new string('.', c.Dots)}{(c.Tuplet.Numerator > 0 ? "t" + c.Tuplet.Numerator + "/" + c.Tuplet.Denominator : "")}{(c.IsGrace ? "g" : "")}{(c.IsRest ? "r" : "")}@{c.RhythmicPosition:0.###}" : "")) + "]");
            Check($"bar fill: {name} has no red bar in any track's view", red.Count == 0, string.Join(", ", red.Take(12)));
        }
    }
}
