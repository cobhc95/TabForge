using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views;
using TabForge.Views.Score;

namespace TabForge;

// Owns: the drum-staff engraving checks (percussion clef, stems, beams, head heights) on a kick/snare bar and the demo song's drum bars.
// Does not own: the engraving itself (Views/Score).
// Tests: TestDrumStaff.
public static partial class SelfTest
{
    /// <summary>One 4/4 bar of sixteen sixteenth kicks (MIDI 36) and snares (40) on slots 0, 8, 10, 12: in one voice (chords) or kick voice 1 / snare voice 2.</summary>
    internal static TrackModel DrumBar(bool twoVoices)
    {
        var track = new TrackModel { Name = "Drums", Kind = TrackKind.Drums, MidiChannel = 9, StringTunings = new() { 49, 46, 42, 38, 36 }, Measures = TemplateFactory.Measures(1) };
        var bar = track.Measures[0];
        TabCell Cell(params int[] midis) => new() { DurationDenominator = 16, Notes = midis.Select(m => new TabNote { MidiValue = m, Fret = m }).ToList() };
        var snares = new[] { 0, 8, 10, 12 };
        for (var i = 0; i < 16; i++)
            bar.Cells[i] = twoVoices || !snares.Contains(i) ? Cell(36) : Cell(36, 40);
        if (twoVoices)
            bar.Voice2Cells = Enumerable.Range(0, 16).Select(i => snares.Contains(i) ? Cell(40) : new TabCell { IsRest = true, DurationDenominator = 16 }).ToList();
        DrumMaps.Apply(track, DrumMaps.GuitarPro5);
        return track;
    }

    /// <summary>One 4/4 bar of eight eighth kicks with a snare on the 3rd and 6th eighth (same cells).</summary>
    internal static TrackModel DrumEighthBar()
    {
        var track = DrumBar(false);
        track.Measures[0].Cells = Enumerable.Range(0, 8).Select(i => new TabCell { DurationDenominator = 8, Notes = (i is 2 or 5 ? new[] { 36, 40 } : new[] { 36 }).Select(m => new TabNote { MidiValue = m, Fret = m }).ToList() }).ToList();
        return track;
    }

    private static void TestDrumStaff()
    {
        var renderer = new StaffNotationRenderer();
        if (Environment.GetEnvironmentVariable("TF_D1_DUMP") is { Length: > 0 } dump)
        {
            foreach (var voices in new[] { false, true })
                ProjectService.Save(System.IO.Path.Combine(dump, voices ? "drums-two.tforge" : "drums-one.tforge"), new SongProject { Tracks = { DrumBar(voices) } });
            ProjectService.Save(System.IO.Path.Combine(dump, "drums-eighths.tforge"), new SongProject { Tracks = { DrumEighthBar() } });
            ProjectService.Save(System.IO.Path.Combine(dump, "demo.tforge"),FullDemoSongFactory.Create());
        }

        Check("drum staff: the percussion clef replaces the guitar clef on a drum track and only there",
            ScoreClefKey.ClefShapeOf(ScoreClefKey.SystemClef(new TrackModel { Kind = TrackKind.Drums }, Clefs.Guitar)).Shape == ClefShape.Percussion &&
            ScoreClefKey.SystemClef(new TrackModel { Kind = TrackKind.Guitar }, Clefs.Guitar) == Clefs.Guitar);

        // Staff step of each sound: kick F4 space (7), snare C5 space (3).
        var one = DrumBar(false);
        var layout = renderer.CreateLayout(one, one.Measures[0], 0, 16, 100, 40, 10, 4, 4, 0, 1.0);
        var stems = layout.DrumStems;
        var top = layout.StaffTop;
        // One written voice: every cell has one up stem from its lowest head (the kick, F4 space), the snare is a second head on it.
        Check("drum staff: one voice gives every cell one up stem and no down stem (kick and snare in a cell share the stem)",
            stems.Count == 16 && stems.All(s => s.StemUp) && stems.Select(s => s.CellIndex).Distinct().Count() == 16 && layout.Beats.All(b => b.LowerStemTopY is null),
            $"{stems.Count(s => s.StemUp)} up, {stems.Count(s => !s.StemUp)} down");
        Check("drum staff: the shared stem starts at the kick head and ends a stem length above the top head or on its beam",
            stems.All(s => Math.Abs(s.StemStartY - (top + 3.5 * StaffNotationRenderer.StaffGap)) < 0.01 && s.StemEndY < s.MinY - 10));
        Check("drum staff: sixteenths in one voice are beamed in fours above the staff (4 up groups), all with two beams",
            layout.BeamGroups.Count == 4 && layout.BeamGroups.All(g => g.StemUp && g.Beats.Count == 4 && g.MaxFlags == 2) && stems.All(s => s.Flags == 2),
            string.Join(",", layout.BeamGroups.Select(g => $"{(g.StemUp ? "u" : "d")}{g.Beats.Count}")));
        Check("drum staff: each beam ends its stems exactly on the beam line",
            layout.BeamGroups.All(g => g.Beats.All(b => Math.Abs(b.StemEndY - g.BaseYAt(b.StemX)) < 0.01)));

        // Eight eighth kicks with a snare on the 3rd and 6th: four beamed pairs, the snare cells shaped like the kick-only cells.
        var eighths = DrumEighthBar();
        var eighthLayout = renderer.CreateLayout(eighths, eighths.Measures[0], 0, 16, 100, 40, 10, 4, 4, 0, 1.0);
        Check("drum staff: eight eighths with two snares are four beamed up pairs: no flag, no quarter shape, no second stem on the snare cells",
            eighthLayout.DrumStems.Count == 8 && eighthLayout.DrumStems.All(s => s.StemUp && s.Flags == 1 && s.BeamGroupIndex >= 0 && Math.Abs(s.DurationSlots - 2) < 0.01) &&
            eighthLayout.BeamGroups.Count == 4 && eighthLayout.BeamGroups.All(g => g.StemUp && g.Beats.Count == 2),
            string.Join(",", eighthLayout.DrumStems.Select(s => $"{s.CellIndex}:{(s.StemUp ? "u" : "d")}{s.Flags}")));
        Check("drum staff: the snare head of a shared stem is above the kick head (C5 space over F4 space)",
            eighthLayout.DrumStems.Where(s => s.CellIndex is 2 or 5).All(s => Math.Abs(s.MinY - (top + 1.5 * StaffNotationRenderer.StaffGap)) < 0.01 && Math.Abs(s.MaxY - (top + 3.5 * StaffNotationRenderer.StaffGap)) < 0.01));

        var two = DrumBar(true);
        var kickVoice = renderer.CreateLayout(two, two.Measures[0], 0, 16, 100, 40, 10, 4, 4, 0, 1.0);
        var snareVoice = renderer.CreateLayout(two, two.Measures[0], 0, 16, 100, 40, 10, 4, 4, 0, 1.0, two.Measures[0].Voice2Cells);
        Check("drum staff: two voices split by instrument: kick in voice 1 stems down (beamed in fours), snare in voice 2 stems up as flagged sixteenths",
            kickVoice.DrumStems.Count == 16 && kickVoice.DrumStems.All(s => !s.StemUp) && kickVoice.BeamGroups.Count == 4 &&
            snareVoice.DrumStems.Count == 4 && snareVoice.DrumStems.All(s => s.StemUp && s.Flags == 2 && s.BeamGroupIndex < 0));

        var demo = FullDemoSongFactory.Create();
        var drums = demo.Tracks.First(t => t.Kind == TrackKind.Drums);
        var problems = new List<string>();
        for (var bar = 0; bar < drums.Measures.Count; bar++)
        {
            var l = renderer.CreateLayout(drums, drums.Measures[bar], bar, 16, 100, 40, 10, 4, 4, 0, 1.0);
            var cells = l.Beats.Where(b => b.HasStem).Select(b => b.CellIndex).Distinct().Count();
            var anchored = l.DrumStems.Select(s => s.CellIndex).Distinct().Count();
            if (anchored != cells) problems.Add($"bar {bar + 1}: {cells} stemmed beats, {anchored} with a stem");
            if (l.DrumStems.Any(s => s.StemUp ? s.StemEndY >= s.StemStartY : s.StemEndY <= s.StemStartY)) problems.Add($"bar {bar + 1}: stem direction");
        }
        Check("drum staff: every stemmed beat of the demo song's drum track has its stems, in the right direction", problems.Count == 0, string.Join("; ", problems.Take(4)));
    }
}
