using System.IO;
using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Rendering;
using TabForge.Services;

namespace TabForge;

/// <summary>
/// Fermata playback: a fermata beat is held +100%, on any track, and everything after it moves later in every track,
/// the playhead, the MIDI export and the render tempo map (all read the one compiled timeline). 120 BPM: a quarter is 500 ms.
/// </summary>
public static partial class SelfTest
{
    private static void TestFermataPlayback()
    {
        // Two tracks, two bars of quarters; only track 0 has a fermata (bar 1, beat 2).
        var p = SingleTrack(2);
        p.Tracks.Add(new TrackModel { Name = "Bass", Measures = TemplateFactory.Measures(2) });
        for (var track = 0; track < 2; track++)
            for (var bar = 0; bar < 2; bar++)
                for (var beat = 0; beat < 4; beat++)
                    Beat(p, track, bar, beat * 4, 4, 60 + track, track);
        var plain = MidiTimelineBuilder.Build(p, new PlaybackOptions());
        Eq("no fermata: 2 bars = 4000 ms", 4000.0, plain.TotalMs);

        p.Tracks[0].Measures[0].Cells[4].Fermata = true;
        var tl = MidiTimelineBuilder.Build(p, new PlaybackOptions());
        Eq("fermata bar lasts 500 ms longer (+100% of a quarter)", 2500.0, tl.Bars[0].EndMs - tl.Bars[0].StartMs);
        Eq("the next bar starts 500 ms later", 2500.0, tl.Bars[1].StartMs);
        Eq("song is 500 ms longer", 4500.0, tl.TotalMs);

        foreach (var track in new[] { 0, 1 })
        {
            var ons = tl.Events.Where(e => e.IsNoteOn && e.TrackIndex == track).Select(e => e.TimeMs).OrderBy(x => x).Take(5).ToList();
            var expect = new[] { 0.0, 500, 1500, 2000, 2500 };
            Check($"track {track}: notes before the fermata stay, notes after it move 500 ms later (a fermata on any track is a timing event)",
                ons.Count == 5 && ons.Zip(expect).All(pair => Math.Abs(pair.First - pair.Second) < 1), string.Join(",", ons.Select(x => x.ToString("0"))));
        }
        // Every form a fermata takes: a GP re-import also sets it on the silent tracks' whole-bar rests (must not out-hold the note beat);
        // the legacy note technique "Fermata" counts too.
        var rp = SingleTrack(1);
        rp.Tracks.Add(new TrackModel { Name = "Bass", Measures = TemplateFactory.Measures(1) });
        for (var beat = 0; beat < 4; beat++) Beat(rp, 0, 0, beat * 4, 4, 60, 0);
        rp.Tracks[0].Measures[0].Cells[0].Fermata = true;
        rp.Tracks[1].Measures[0].Cells[0] = new TabCell { IsRest = true, DurationDenominator = 1, Fermata = true };
        var rh = FermataTime.Holds(rp, 0);
        Check("a fermata on a silent track's whole-bar rest does not out-hold the sounding quarter at the same slot", rh.Count == 1 && rh[0].LengthSlots == 4, string.Join(";", rh));
        rp.Tracks[0].Measures[0].Cells[0].Fermata = false; rp.Tracks[1].Measures[0].Cells[0].Fermata = false;
        rp.Tracks[0].Measures[0].Cells[4].Notes[0].Techniques.Add("Fermata");
        Check("the legacy note technique \"Fermata\" holds the beat", FermataTime.Holds(rp, 0).Count == 1 && FermataTime.Holds(rp, 0)[0].Slot == 4);

        var held = tl.Events.Where(e => e.IsNoteOn && e.TrackIndex == 0).OrderBy(e => e.TimeMs).ElementAt(1);
        var heldOff = tl.Events.Where(e => e.IsNoteOff && e.TrackIndex == 0 && e.Data1 == held.Data1 && e.TimeMs > held.TimeMs).Min(e => e.TimeMs);
        Check("the held note sounds about twice as long", heldOff - held.TimeMs > 850, $"{heldOff - held.TimeMs:0} ms");

        // Playhead: before the hold unchanged, after it on the shifted slot.
        Eq("playhead at 2000 ms (after the hold) is on beat 4 of bar 1", 12, PlayheadMapper.Map(tl, 2000, 0, 0).Cell);
        Eq("playhead at 1500 ms is on beat 3 of bar 1", 8, PlayheadMapper.Map(tl, 1500, 0, 0).Cell);
        Eq("playhead at 250 ms is still on beat 1 (cell 2)", 2, PlayheadMapper.Map(tl, 250, 0, 0).Cell);
        Check("playhead inside the hold stays on the held beat", PlayheadMapper.Map(tl, 1000, 0, 0).Cell is >= 4 and < 8);

        // MIDI export: the file's ticks for the shifted notes are the written positions, and the tempo track slows the held beat.
        var map = new MidiExportService.TickMap(tl, p);
        Eq("export tick of the note after the hold is beat 3 (960)", 960, map.TickOf(1500));
        Eq("export tick of the next bar's first note is one bar (1920)", 1920, map.TickOf(2500));
        var path = Path.Combine(Path.GetTempPath(), "tf-fermata-" + Guid.NewGuid().ToString("N") + ".mid");
        try
        {
            MidiExportService.Export(p, path);
            var bytes = File.ReadAllBytes(path);
            var slowed = false;
            for (var i = 0; i + 5 < bytes.Length && !slowed; i++)
                slowed = bytes[i] == 0xFF && bytes[i + 1] == 0x51 && bytes[i + 2] == 3 && bytes[i + 3] == 0x0F && bytes[i + 4] == 0x42 && bytes[i + 5] == 0x40;
            Check("MIDI tempo track drops to 60 BPM for the held beat", slowed);
        }
        finally { try { File.Delete(path); } catch { } }

        // Offline render tempo map: slow over the held beat (quarter position 1 -> 2), back to 120 after it.
        var tempoMap = RenderSpecBuilder.TempoMap(tl, 48000, 120, p);
        var slowPoint = tempoMap.FirstOrDefault(t => Math.Abs(t.Ppq - 1.0) < 1e-6);
        var backPoint = tempoMap.FirstOrDefault(t => Math.Abs(t.Ppq - 2.0) < 1e-6);
        Check("render tempo map holds the fermata beat at 60 BPM from 500 ms",
            Math.Abs(slowPoint.Tempo - 60) < 0.01 && slowPoint.Frame == 24000, $"tempo {slowPoint.Tempo} frame {slowPoint.Frame}");
        Check("render tempo map returns to 120 BPM at 1500 ms", Math.Abs(backPoint.Tempo - 120) < 0.01 && backPoint.Frame == 72000, $"tempo {backPoint.Tempo} frame {backPoint.Frame}");

        // Repeats keep working: a repeated fermata bar is held on every pass.
        var repeat = SingleTrack(1);
        for (var beat = 0; beat < 4; beat++) Beat(repeat, 0, 0, beat * 4, 4, 60);
        repeat.Tracks[0].Measures[0].Cells[12].Fermata = true;
        repeat.Tracks[0].Measures[0].RepeatStart = true;
        repeat.Tracks[0].Measures[0].RepeatEnd = true;
        repeat.Tracks[0].Measures[0].RepeatCount = 2;
        var repeated = MidiTimelineBuilder.Build(repeat, new PlaybackOptions { RepeatExpansion = true });
        Check("a repeated fermata bar is held on every pass", repeated.Bars.Count == 2 && Math.Abs(repeated.TotalMs - 5000) < 1, $"bars {repeated.Bars.Count} total {repeated.TotalMs:0}");
    }
}
