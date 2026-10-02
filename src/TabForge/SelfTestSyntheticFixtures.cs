using System.IO;
using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge;

// Audit 5 A5-14: synthetic fixtures for the checks that used to need the user's own songs (whole-folder import, reference songs,
// tuplets.gp5, the round-trip local extra) and therefore skipped on CI. Every song here is built in code at test time, saved and
// re-imported through .tforge and .gp (embedded and clean), and compared with the semantic comparer of SelfTestRoundTripSemantics.
// The PERFORMED bar order and the bar-to-ms timeline are asserted as behaviour (what plays, when), not as implementation detail,
// so a rewrite of PlaybackOrder can be checked against the same fixtures. Runs on every machine; CI requires it (--require synthetic-fixtures).
public static partial class SelfTest
{
    // ------------------------------------------------------------------ song building

    private static SongProject SfSong(string title, int bars, int tempo = 120)
    {
        var song = new SongProject { Title = title, Artist = "TabForge self-test", Tempo = tempo };
        var guitar = new TrackModel { Name = "Synthetic Guitar", Kind = TrackKind.Guitar, MidiProgram = 29, MidiChannel = 0, Measures = TemplateFactory.Measures(bars) };
        var bass = new TrackModel
        {
            Name = "Synthetic Bass", Kind = TrackKind.Bass, MidiProgram = 33, MidiChannel = 1,
            StringTunings = new List<int> { 43, 38, 33, 28 }, Measures = TemplateFactory.Measures(bars)
        };
        // Every bar carries its own number as the guitar fret (quarter notes) and a whole-note bass note, so what is performed is audible in the notes.
        for (var b = 0; b < bars; b++)
        {
            for (var k = 0; k < 4; k++) SfNote(guitar, b, k * 4, 4, 0, 0, b % 18);
            SfNote(bass, b, 0, 1, 0, 2, b % 5);
        }
        song.Tracks.Add(guitar); song.Tracks.Add(bass);
        song.IsDirty = false;
        return song;
    }

    private static TabCell SfNote(TrackModel t, int bar, double slot, int den, int dots, int str, int fret, params string[] tech)
    {
        var m = t.Measures[bar];
        var idx = (int)slot;
        while (idx < m.Cells.Count - 1 && (m.Cells[idx].Notes.Count > 0 || m.Cells[idx].IsRest)) idx++;   // tuplet onsets share a grid cell: take the next free one
        var c = m.Cells[idx];
        c.DurationDenominator = den; c.Dots = dots;
        if (Math.Abs(slot - Math.Round(slot)) > 0.01 || idx != (int)slot) c.RhythmicPosition = slot;
        c.Notes.Add(RtNote(t, str, fret, 95, tech));
        return c;
    }

    private static void SfClearBar(TrackModel t, int bar)
    {
        t.Measures[bar].Cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList();
        t.Measures[bar].Voice2Cells = new();
    }

    private static void SfBar(SongProject s, int bar, Action<MeasureModel> apply) { foreach (var t in s.Tracks) apply(t.Measures[bar]); }

    /// <summary>Milliseconds of a run of slots at a constant tempo (a slot is a sixteenth note).</summary>
    private static double SfMs(double slots, double bpm) => slots / 4.0 * 60000.0 / bpm;

    // ------------------------------------------------------------------ behaviour assertions

    private static string SfList(IEnumerable<int> bars) => string.Join(",", bars.Select(b => b + 1));

    /// <summary>The performed bar order (source bars, 1-based in messages) and the timeline: the same bars, back to back, each of the expected length.</summary>
    private static ScoreTimeline SfAssertPerformance(string label, SongProject song, int[] order, double[]? barMs, double toleranceMs = 1.5)
    {
        var got = PlaybackOrder.Build(song, new PlaybackOptions());
        Check($"{label}: performed bar order is {SfList(order)}", got.SequenceEqual(order), $"got {SfList(got)}");
        var tl = MidiTimelineBuilder.Build(song, new PlaybackOptions());
        var played = tl.Bars.Select(b => b.Bar).ToList();
        Check($"{label}: the playback timeline plays the same bars in the same order", played.SequenceEqual(order), $"got {SfList(played)}");
        if (barMs is null || played.Count != barMs.Length) { if (barMs is not null) Check($"{label}: timeline has {barMs.Length} bars", false, $"got {played.Count}"); return tl; }
        var worst = 0.0; var gap = 0.0;
        for (var i = 0; i < barMs.Length; i++)
        {
            var bar = tl.Bars[i];
            worst = Math.Max(worst, Math.Abs(bar.EndMs - bar.StartMs - barMs[i]));
            if (i > 0) gap = Math.Max(gap, Math.Abs(bar.StartMs - tl.Bars[i - 1].EndMs));
        }
        Check($"{label}: every performed bar lasts its expected time (worst {worst:0.00} ms)", worst <= toleranceMs, $"expected {string.Join(", ", barMs.Select(x => x.ToString("0.0")))}; got {string.Join(", ", tl.Bars.Select(b => (b.EndMs - b.StartMs).ToString("0.0")))}");
        Check($"{label}: bars follow each other without a gap (bar-to-ms timeline)", gap <= toleranceMs, $"largest gap {gap:0.00} ms");
        return tl;
    }

    /// <summary>One song through .tforge, .gp with the embedded project and a clean .gp: the semantic comparer on each, and the same performance after reopening.</summary>
    private static void SfRoundTrip(string label, SongProject song, int[] order, double[]? barMs, RtProfile? clean = null, bool cleanPerformance = true, bool embeddedPerformance = true)
    {
        var folder = RtFolder();
        try
        {
            SfAssertPerformance($"{label} [source]", song, order, barMs);
            var expected = RtCopy(song);
            var tforge = RtViaTforge(song, folder, "s");
            RtVerify(label, RtTforge, expected, tforge);
            SfAssertPerformance($"{label} [.tforge]", tforge, order, barMs);
            var embedded = RtViaGp(song, folder, "s", embed: true);
            RtVerify(label, RtGpEmbedded, expected, embedded);
            if (embeddedPerformance) SfAssertPerformance($"{label} [.gp with project]", embedded, order, barMs);
            var cleaned = RtViaGp(song, folder, "s", embed: false);
            RtVerify(label, clean ?? RtGpClean(), expected, cleaned);
            if (cleanPerformance) SfAssertPerformance($"{label} [clean .gp]", cleaned, order, barMs);
        }
        finally { RtCleanup(folder); }
    }

    // ------------------------------------------------------------------ 1. repeats: several closes, alternate endings

    private static void SfRepeatEndings()
    {
        // Bars 1-7 (index 0-6): bar 2 opens; bar 4 is ending 1 and closes; bar 5 is ending 2 and closes; bar 6 is ending 3; bar 7 is the outro.
        var a = SfSong("Repeat with 1. 2. 3. endings", 7);
        SfBar(a, 1, m => m.RepeatStart = true);
        SfBar(a, 3, m => { m.AlternateEnding = 1; m.RepeatEnd = true; m.RepeatCount = 2; });
        SfBar(a, 4, m => { m.AlternateEnding = 2; m.RepeatEnd = true; m.RepeatCount = 2; });
        SfBar(a, 5, m => m.AlternateEnding = 3);
        SfRoundTrip("repeat, endings 1. 2. 3., each with its own close", a, new[] { 0, 1, 2, 3, 1, 2, 4, 1, 2, 5, 6 }, Enumerable.Repeat(2000.0, 11).ToArray());

        // One shared ending bar for passes 1 and 3 ("1.3."), then "2." and "4.": a four-pass repeat (logged, see below).
        var b = SfSong("Repeat with shared ending 1.3.", 6);
        SfBar(b, 0, m => m.RepeatStart = true);
        SfBar(b, 2, m => { m.AlternateEndingMask = 0b0101; m.RepeatEnd = true; m.RepeatCount = 4; });
        SfBar(b, 3, m => { m.AlternateEnding = 2; m.RepeatEnd = true; m.RepeatCount = 2; });
        SfBar(b, 4, m => m.AlternateEnding = 4);
        // KNOWN (not asserted): this shape is ambiguous in a file (a close on the "1.3." bar is used up on pass 1, so pass 2 jumps back again and the
        // "2." bar is never reached). The order is logged so the clean-room PlaybackOrder rewrite can be compared; the 1.2./3. shape below is asserted.
        var known = PlaybackOrder.Build(b, new PlaybackOptions());
        Log.Add($"  KNOWN synthetic: shared ending 1.3. + 2. + 4. (closes x4 and x2) performs bars {SfList(known)}; the musically expected order is 1,2,3,1,2,4,1,2,3,1,2,5,6 (unconfirmed against Guitar Pro)");

        // Two separate repeats (x2 and x3) one after the other, the second with a "1.2." / "3." ending pair.
        var c = SfSong("Two repeats in a row", 8);
        SfBar(c, 0, m => m.RepeatStart = true);
        SfBar(c, 1, m => { m.RepeatEnd = true; m.RepeatCount = 2; });
        SfBar(c, 2, m => m.RepeatStart = true);
        SfBar(c, 4, m => { m.AlternateEndingMask = 0b011; m.RepeatEnd = true; m.RepeatCount = 3; });
        SfBar(c, 5, m => m.AlternateEnding = 3);
        SfRoundTrip("two repeats in a row, the second x3 with endings 1.2. and 3.", c, new[] { 0, 1, 0, 1, 2, 3, 4, 2, 3, 4, 2, 3, 5, 6, 7 }, Enumerable.Repeat(2000.0, 15).ToArray());
    }

    // ------------------------------------------------------------------ 2. D.C. / D.S. / al Coda / al Fine

    private static void SfNavigation()
    {
        var dcFine = SfSong("D.C. al Fine", 5);
        SfBar(dcFine, 2, m => m.Directions = "Fine"); SfBar(dcFine, 4, m => m.Directions = "DaCapo");
        SfNavigationCase("D.C. al Fine", dcFine, new[] { 0, 1, 2, 3, 4, 0, 1, 2 });

        var dsFine = SfSong("D.S. al Fine", 5);
        SfBar(dsFine, 1, m => m.Directions = "Segno"); SfBar(dsFine, 3, m => m.Directions = "Fine"); SfBar(dsFine, 4, m => m.Directions = "DalSegno");
        SfNavigationCase("D.S. al Fine", dsFine, new[] { 0, 1, 2, 3, 4, 1, 2, 3 });

        var dsCoda = SfSong("D.S. al Coda", 8);
        SfBar(dsCoda, 1, m => m.Directions = "Segno"); SfBar(dsCoda, 3, m => m.Directions = "ToCoda");
        SfBar(dsCoda, 5, m => m.Directions = "DalSegnoAlCoda"); SfBar(dsCoda, 7, m => m.Directions = "Coda");
        SfNavigationCase("D.S. al Coda", dsCoda, new[] { 0, 1, 2, 3, 4, 5, 1, 2, 3, 7 });

        var dcCoda = SfSong("D.C. al Coda", 7);
        SfBar(dcCoda, 2, m => m.Directions = "ToCoda"); SfBar(dcCoda, 4, m => m.Directions = "DaCapoAlCoda"); SfBar(dcCoda, 5, m => m.Directions = "Coda");
        SfNavigationCase("D.C. al Coda", dcCoda, new[] { 0, 1, 2, 3, 4, 0, 1, 2, 5, 6 });

        // A repeat inside the D.S. region: the repeat plays on the first way through only, after the jump playback goes straight through.
        var mixed = SfSong("D.S. al Coda with a repeat", 5);
        SfBar(mixed, 1, m => { m.Directions = "Segno"; m.RepeatStart = true; });
        SfBar(mixed, 2, m => { m.RepeatEnd = true; m.Directions = "ToCoda"; });
        SfBar(mixed, 3, m => m.Directions = "DalSegnoAlCoda"); SfBar(mixed, 4, m => m.Directions = "Coda");
        SfNavigationCase("D.S. al Coda with a repeat", mixed, new[] { 0, 1, 2, 1, 2, 3, 1, 2, 4 });

        // The same jumps written the way a Guitar Pro file names them (what the importer stores).
        var native = SfSong("D.S. al Coda, Guitar Pro direction names", 8);
        SfBar(native, 1, m => m.Directions = "TargetSegno"); SfBar(native, 3, m => m.Directions = "JumpDaCoda");
        SfBar(native, 5, m => m.Directions = "JumpDalSegnoAlCoda"); SfBar(native, 7, m => m.Directions = "TargetCoda");
        SfNavigationCase("D.S. al Coda (Guitar Pro names)", native, new[] { 0, 1, 2, 3, 4, 5, 1, 2, 3, 7 });
    }

    private static void SfNavigationCase(string label, SongProject song, int[] order)
    {
        var folder = RtFolder();
        try
        {
            var ms = Enumerable.Repeat(2000.0, order.Length).ToArray();
            SfAssertPerformance($"{label} [source]", song, order, ms);
            var expected = RtCopy(song);
            var tforge = RtViaTforge(song, folder, "n");
            RtVerify(label, RtTforge, expected, tforge);
            SfAssertPerformance($"{label} [.tforge]", tforge, order, ms);
            var embedded = RtViaGp(song, folder, "n", embed: true);
            RtVerify(label, RtGpEmbedded, expected, embedded);
            SfAssertPerformance($"{label} [.gp with project]", embedded, order, ms);
            // The clean .gp: the jumps are written as Guitar Pro directions, so the reopened song performs the same bars.
            var cleaned = RtViaGp(song, folder, "n", embed: false);
            RtVerify(label, RtGpClean(("bar.directions", "the clean .gp stores directions under Guitar Pro's own names (TargetSegno, JumpDaCoda...); the performed order is compared strictly below")), expected, cleaned);
            SfAssertPerformance($"{label} [clean .gp]", cleaned, order, ms);
        }
        finally { RtCleanup(folder); }
    }

    // ------------------------------------------------------------------ 3. tempo changes mid-bar and tempo ramps

    private static void SfTempo()
    {
        // 100 bpm; bar 2 at 140; bar 3 switches to 80 halfway; bar 4 stays at 80; bar 5 returns to 100 on the first beat, bar 6 switches halfway again.
        var steps = SfSong("Tempo steps, mid-bar", 6, 100);
        SfBar(steps, 1, m => m.TempoChange = 140);
        SfBar(steps, 2, m => m.MidBarTempos = new List<TempoPoint> { new(8, 80) });
        SfBar(steps, 4, m => m.TempoChange = 100);
        SfBar(steps, 5, m => m.MidBarTempos = new List<TempoPoint> { new(4, 160) });
        var stepMs = new[] { SfMs(16, 100), SfMs(16, 140), SfMs(8, 140) + SfMs(8, 80), SfMs(16, 80), SfMs(16, 100), SfMs(4, 100) + SfMs(12, 160) };
        SfRoundTrip("tempo changes mid-bar", steps, new[] { 0, 1, 2, 3, 4, 5 }, stepMs);

        // Ramps: a whole-bar ramp from 120 down to 60 (bar 2), and a ramp starting on beat 2 of bar 4 up to 180, which then holds.
        var ramps = SfSong("Tempo ramps", 5, 120);
        SfBar(ramps, 1, m => m.MidBarTempos = new List<TempoPoint> { new(0, 60, 16) });
        SfBar(ramps, 3, m => m.MidBarTempos = new List<TempoPoint> { new(4, 180, 8) });
        // Linear-in-beat ramp from t0 to t1 over L slots: the integral of 60000 / (4 * t(x)) dx is 15000 * L * ln(t1 / t0) / (t1 - t0).
        static double Ramp(double l, double t0, double t1) => 15000.0 * l * Math.Log(t1 / t0) / (t1 - t0);
        var rampMs = new[] { SfMs(16, 120), Ramp(16, 120, 60), SfMs(16, 60), SfMs(4, 60) + Ramp(8, 60, 180) + SfMs(4, 180), SfMs(16, 180) };
        // A7-E02: the clean .gp writes each ramp as Guitar Pro's linear tempo automation (a linear point gliding to the next point), so the ramps, their lengths and the timing all survive.
        SfRoundTrip("tempo ramps", ramps, new[] { 0, 1, 2, 3, 4 }, rampMs);
        var cleanRamp = SfViaCleanGp(ramps);
        var rampPoint = cleanRamp.Tracks[0].Measures[1].MidBarTempos;
        var rampPoint2 = cleanRamp.Tracks[0].Measures[3].MidBarTempos;
        Check("tempo ramps: the clean .gp reopens with both ramps, same target tempo and length", rampPoint is [{ Slot: 0, Tempo: 60, RampSlots: 16 }] && rampPoint2 is [{ Slot: 4, Tempo: 180, RampSlots: 8 }],
            $"bar 2: {string.Join(";", rampPoint ?? new())}; bar 4: {string.Join(";", rampPoint2 ?? new())}");
    }

    private static SongProject SfViaCleanGp(SongProject s)
    {
        var folder = RtFolder();
        try { return RtViaGp(s, folder, "c", embed: false); }
        finally { RtCleanup(folder); }
    }

    // ------------------------------------------------------------------ 4. mix-table changes (volume / pan automation)

    private static List<(double Ms, int Value)> SfController(ScoreTimeline tl, int track, int controller) =>
        tl.Events.Where(e => !e.IsMetronome && !e.IsSetup && e.TrackIndex == track && (e.Status & 0xF0) == 0xB0 && e.Data1 == controller && e.TimeMs > 0.5)
            .Select(e => (Math.Round(e.TimeMs, 1), e.Data2)).Distinct().OrderBy(e => e.Item1).ToList();

    private static void SfMix()
    {
        var song = SfSong("Mix-table changes", 5);
        // Bar 2: volume 12 (of 16) and pan -4 on the guitar at once; bar 3: volume falls to 4 over two beats; bar 4: pan +4 on every track.
        song.Tracks[0].Measures[1].Cells[0].Mix = new MixChange { Volume = 12, Pan = -4 };
        song.Tracks[0].Measures[2].Cells[0].Mix = new MixChange { Volume = 4, TransitionBeats = 2 };
        song.Tracks[0].Measures[3].Cells[0].Mix = new MixChange { Pan = 4, AllTracks = true };
        var order = new[] { 0, 1, 2, 3, 4 };
        var folder = RtFolder();
        try
        {
            var expected = RtCopy(song);
            var tforge = RtViaTforge(song, folder, "m");
            RtVerify("mix-table volume and pan changes", RtTforge, expected, tforge);
            var embedded = RtViaGp(song, folder, "m", embed: true);
            RtVerify("mix-table volume and pan changes", RtGpEmbedded, expected, embedded);
            RtVerify("mix-table volume and pan changes", RtGpClean(), expected, RtViaGp(song, folder, "m", embed: false));
            foreach (var (name, s) in new[] { ("source", song), (".tforge", tforge), (".gp with project", embedded) })
            {
                var tl = SfAssertPerformance($"mix-table [{name}]", s, order, Enumerable.Repeat(2000.0, 5).ToArray());
                var vol = SfController(tl, 0, 7); var pan = SfController(tl, 0, 10);
                Check($"mix-table [{name}]: volume 12/16 is set on the guitar at the start of bar 2 (CC7 = 96)", vol.Contains((2000.0, 96)), string.Join(" ", vol.Take(6)));
                Check($"mix-table [{name}]: pan -4 is set on the guitar at the start of bar 2 (CC10 = 32)", pan.Contains((2000.0, 32)), string.Join(" ", pan.Take(6)));
                var ramp = vol.Where(e => e.Ms > 4000 && e.Ms <= 5000).ToList();
                Check($"mix-table [{name}]: the volume ramp over two beats steps down from 96 to 32 and arrives one second later",
                    ramp.Count >= 4 && ramp[^1] == (5000.0, 32) && ramp.Zip(ramp.Skip(1), (a, b) => a.Value >= b.Value).All(x => x) && ramp[0].Value < 96,
                    string.Join(" ", ramp));
                var other = SfController(tl, 1, 10);
                Check($"mix-table [{name}]: an all-tracks pan change reaches the bass too (CC10 = 96 at bar 4)", other.Contains((6000.0, 96)), string.Join(" ", other.Take(6)));
            }
        }
        finally { RtCleanup(folder); }
    }

    // ------------------------------------------------------------------ 5. tuplets and dotted values

    private static List<double> SfOnsets(ScoreTimeline tl, int track, int voice, int bar)
    {
        var start = tl.Bars.First(b => b.Bar == bar).StartMs;
        return tl.Notes.Where(n => n.TrackIndex == track && n.VoiceIndex == voice && n.Bar == bar).Select(n => Math.Round(n.OnsetMs - start, 1)).Distinct().OrderBy(x => x).ToList();
    }

    private static void SfTuplets()
    {
        var song = SfSong("Tuplets and dotted values", 4);
        var g = song.Tracks[0];
        for (var b = 0; b < 4; b++) SfClearBar(g, b);
        // Bar 1: eighth triplet, a 5:4 sixteenth run, a dotted eighth + sixteenth, then a double-dotted eighth + a thirty-second.
        for (var k = 0; k < 3; k++) { var c = SfNote(g, 0, k * 4 / 3.0, 8, 0, 1, 3 + k); c.IsTriplet = true; }
        for (var k = 0; k < 5; k++) { var c = SfNote(g, 0, 4 + k * 0.8, 16, 0, 1, 5 + k % 3); c.TupletNumerator = 5; c.TupletDenominator = 4; }
        SfNote(g, 0, 8, 8, 1, 2, 5); SfNote(g, 0, 11, 16, 0, 2, 7);
        SfNote(g, 0, 12, 8, 2, 2, 5); SfNote(g, 0, 15.5, 32, 0, 2, 3);
        // Bar 2: a 6:4 sixteenth sextuplet, a quarter-note triplet across beats 2-3, a quarter.
        for (var k = 0; k < 6; k++) { var c = SfNote(g, 1, k * 4 / 6.0, 16, 0, 1, 3 + k % 4); c.TupletNumerator = 6; c.TupletDenominator = 4; }
        for (var k = 0; k < 3; k++) { var c = SfNote(g, 1, 4 + k * 8 / 3.0, 4, 0, 2, 4 + k); c.IsTriplet = true; }
        SfNote(g, 1, 12, 4, 0, 1, 2);
        // Bar 3: dotted half + quarter. Bar 4: double-dotted half + eighth.
        SfNote(g, 2, 0, 2, 1, 1, 5); SfNote(g, 2, 12, 4, 0, 1, 7);
        SfNote(g, 3, 0, 2, 2, 1, 5); SfNote(g, 3, 14, 8, 0, 1, 7);
        var order = new[] { 0, 1, 2, 3 };
        var tl = SfAssertPerformance("tuplets [source]", song, order, Enumerable.Repeat(2000.0, 4).ToArray());
        static bool Near(List<double> got, double[] want) => got.Count == want.Length && got.Zip(want, (a, b) => Math.Abs(a - b) <= 1.0).All(x => x);
        var want1 = new[] { 0, 500 / 3.0, 1000 / 3.0, 500, 600, 700, 800, 900, 1000, 1375, 1500, 1937.5 };
        var want2 = new[] { 0, 500 / 6.0, 1000 / 6.0, 250, 1000 / 3.0, 1250 / 3.0, 500, 500 + 1000 / 3.0, 500 + 2000 / 3.0, 1500 };
        var got1 = SfOnsets(tl, 0, 0, 0); var got2 = SfOnsets(tl, 0, 0, 1);
        Check("tuplets: triplet, 5:4, dotted and double-dotted notes sound at their exact times in bar 1", Near(got1, want1), $"got {string.Join(" ", got1)}");
        Check("tuplets: a 6:4 sextuplet and a quarter-note triplet sound at their exact times in bar 2", Near(got2, want2), $"got {string.Join(" ", got2)}");
        Check("tuplets: dotted half then quarter (bar 3), double-dotted half then eighth (bar 4)",
            Near(SfOnsets(tl, 0, 0, 2), new[] { 0, 1500.0 }) && Near(SfOnsets(tl, 0, 0, 3), new[] { 0, 1750.0 }));
        SfRoundTrip("tuplets and dotted values", song, order, Enumerable.Repeat(2000.0, 4).ToArray());
        foreach (var (name, s) in new[] { (".tforge", SfReopen(song, "t")), ("clean .gp", SfViaCleanGp(song)) })
        {
            var t2 = MidiTimelineBuilder.Build(s, new PlaybackOptions());
            Check($"tuplets [{name}]: the same exact onsets after reopening",
                Near(SfOnsets(t2, 0, 0, 0), want1) && Near(SfOnsets(t2, 0, 0, 1), want2) && Near(SfOnsets(t2, 0, 0, 3), new[] { 0, 1750.0 }),
                $"bar 1 {string.Join(" ", SfOnsets(t2, 0, 0, 0))}; bar 2 {string.Join(" ", SfOnsets(t2, 0, 0, 1))}");
        }
    }

    private static SongProject SfReopen(SongProject s, string name)
    {
        var folder = RtFolder();
        try { return RtViaTforge(s, folder, name); }
        finally { RtCleanup(folder); }
    }

    // ------------------------------------------------------------------ 6. two voices, one of them partial

    private static void SfVoices()
    {
        var song = SfSong("Two voices, partial second voice", 4);
        var g = song.Tracks[0];
        foreach (var b in new[] { 0, 1, 3 }) g.Measures[b].Voice2Cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList();
        void Voice2(int bar, int slot, int den, int fret) { var c = g.Measures[bar].Voice2Cells[slot]; c.DurationDenominator = den; c.Notes.Add(RtNote(g, 4, fret)); }
        Voice2(0, 0, 2, 0);                                    // bar 1: only the first half of the bar
        Voice2(1, 8, 2, 2);                                    // bar 2: only the second half
        for (var k = 0; k < 4; k++) Voice2(3, k * 4, 4, k);    // bar 4: a full bar; bar 3 has no second voice at all
        var order = new[] { 0, 1, 2, 3 };
        var ms = Enumerable.Repeat(2000.0, 4).ToArray();
        SfRoundTrip("two voices with a partial second voice", song, order, ms);
        foreach (var (name, s) in new[] { ("source", song), (".tforge", SfReopen(song, "v")), ("clean .gp", SfViaCleanGp(song)) })
        {
            var tl = MidiTimelineBuilder.Build(s, new PlaybackOptions());
            var v1 = tl.Notes.Count(n => n.TrackIndex == 0 && n.VoiceIndex == 0); var v2 = tl.Notes.Count(n => n.TrackIndex == 0 && n.VoiceIndex == 1);
            Check($"voices [{name}]: 16 first-voice notes and 6 second-voice notes play", v1 == 16 && v2 == 6, $"voice 1: {v1}, voice 2: {v2}");
            bool Same(int bar, params double[] want) { var got = SfOnsets(tl, 0, 1, bar); return got.Count == want.Length && got.Zip(want, (a, b) => Math.Abs(a - b) <= 1.0).All(x => x); }
            Check($"voices [{name}]: the second voice sounds only where it is written (first half, second half, nothing, every beat)",
                Same(0, 0) && Same(1, 1000) && Same(2) && Same(3, 0, 500, 1000, 1500));
        }
    }

    // ------------------------------------------------------------------ 7. fade-in / fade-out and palm-mute spans across bars

    private static void SfSpans()
    {
        var song = SfSong("Fades and palm-mute spans", 7);
        var g = song.Tracks[0];
        for (var b = 0; b < 7; b++) SfClearBar(g, b);
        // Bars 1-2: palm-muted half notes; bar 3: the same notes unmuted (the control); bars 4-5: a whole note tied across the bar line,
        // fading in; bars 6-7: a whole note tied across the bar line, fading out.
        foreach (var (bar, tech) in new[] { (0, "PalmMute"), (1, "PalmMute"), (2, "") })
            foreach (var slot in new[] { 0, 8 }) SfNote(g, bar, slot, 2, 0, 1, 3, tech);
        SfNote(g, 3, 0, 1, 0, 2, 5, "FadeIn"); SfNote(g, 4, 0, 1, 0, 2, 5).Notes[0].Tied = true;
        SfNote(g, 5, 0, 1, 0, 2, 7, "FadeOut"); SfNote(g, 6, 0, 1, 0, 2, 7).Notes[0].Tied = true;
        GuitarProImporter.LinkTieOrigins(g);
        var order = Enumerable.Range(0, 7).ToArray();
        var ms = Enumerable.Repeat(2000.0, 7).ToArray();
        SfRoundTrip("fade-in, fade-out and palm-mute spans across bars", song, order, ms);
        foreach (var (name, s) in new[] { ("source", song), (".tforge", SfReopen(song, "f")), ("clean .gp", SfViaCleanGp(song)) })
        {
            var tl = MidiTimelineBuilder.Build(s, new PlaybackOptions());
            var muted = tl.Notes.Where(n => n.TrackIndex == 0 && n.Bar <= 1).ToList(); var control = tl.Notes.Where(n => n.TrackIndex == 0 && n.Bar == 2).ToList();
            Check($"spans [{name}]: palm-muted notes across bars 1-2 are shorter and softer than the same notes unmuted",
                muted.Count == 4 && control.Count == 2 && muted.All(n => n.DurationMs < control.Min(c => c.DurationMs) - 50 && n.Velocity < control.Min(c => c.Velocity)),
                $"muted {string.Join(" ", muted.Select(n => $"{n.DurationMs:0}ms/v{n.Velocity}"))}; control {string.Join(" ", control.Select(n => $"{n.DurationMs:0}ms/v{n.Velocity}"))}");
            var fadeIn = tl.Notes.FirstOrDefault(n => n.TrackIndex == 0 && n.FadeIn);
            Check($"spans [{name}]: the fade-in note is flagged and lasts across both tied bars (over 3.5 s)", fadeIn is not null && fadeIn.DurationMs > 3500, fadeIn is null ? "no fade-in note" : $"{fadeIn.DurationMs:0} ms");
            if (fadeIn is not null)
            {
                var cc = SfController(tl, 0, 11).Where(e => e.Ms >= fadeIn.OnsetMs - 0.5 && e.Ms <= fadeIn.EndMs + 0.5).ToList();
                Check($"spans [{name}]: the fade-in opens from silence, only ever rises, and ends at full level",
                    cc.Count > 10 && cc[0].Value == 0 && cc[^1].Value == 127 && cc.Zip(cc.Skip(1), (a, b) => a.Value <= b.Value).All(x => x), string.Join(" ", cc.Take(8)));
            }
            var fadeStart = tl.Bars[5].StartMs;
            var down = SfController(tl, 0, 11).Where(e => e.Ms >= fadeStart - 0.5 && e.Ms < fadeStart + 2000).ToList();
            Check($"spans [{name}]: the fade-out starts at full level and only ever falls until it is reset", down.Count > 10 && down[0].Value >= 120 && down.Min(e => e.Value) <= 20, string.Join(" ", down.Take(8)));
        }
    }

    // ------------------------------------------------------------------ the demo song as a required fixture

    private static string? SfDemoSongPath()
    {
        const string name = "TabForge Demo - Ashen Meridian.gp";
        var beside = Path.Combine(AppContext.BaseDirectory, "Samples", name);
        if (File.Exists(beside)) return beside;
        var root = FindRepositoryRoot();
        var inRepo = root is null ? null : Path.Combine(root, "samples", name);
        return inRepo is not null && File.Exists(inRepo) ? inRepo : null;
    }

    private static void SfDemoSong()
    {
        var path = SfDemoSongPath();
        Check("demo song: the shipped sample file is present (required fixture, never skipped)", path is not null);
        if (path is null) return;
        var song = GuitarProImporter.Import(path);
        var bars = song.Tracks.Count == 0 ? 0 : song.Tracks.Max(t => t.Measures.Count);
        Eq("demo song: 10 tracks", 10, song.Tracks.Count);
        Check("demo song: every track has 144 bars", song.Tracks.All(t => t.Measures.Count == 144), string.Join("/", song.Tracks.Select(t => t.Measures.Count)));
        var sections = string.Join(", ", song.Markers.OrderBy(m => m.MeasureIndex).Select(m => $"{m.MeasureIndex + 1}:{m.Title}"));
        Check("demo song: it has a drum track", song.Tracks.Any(t => t.Kind == TrackKind.Drums));
        Check("demo song: named sections from the intro to the ending (at least 15)", song.Markers.Count >= 15 && song.Markers.OrderBy(m => m.MeasureIndex).First().MeasureIndex == 0, sections);
        Check("demo song: title and artist are set", !string.IsNullOrWhiteSpace(song.Title) && !string.IsNullOrWhiteSpace(song.Artist));
        Log.Add($"  info  demo song invariants: {song.Tracks.Count} tracks, {bars} bars, sections {string.Join(", ", song.Markers.OrderBy(m => m.MeasureIndex).Select(m => $"{m.MeasureIndex + 1}:{m.Title}"))}; tempo {song.Tempo}");
        var order = PlaybackOrder.Build(song, new PlaybackOptions());
        var tl = MidiTimelineBuilder.Build(song, new PlaybackOptions());
        Check("demo song: the performed bar order covers the song and the timeline plays it in that order",
            order.Count >= bars && tl.Bars.Select(b => b.Bar).SequenceEqual(order) && tl.TotalMs > 10_000 && tl.Notes.Count > 100, $"{order.Count} performed bars, {tl.TotalMs:0} ms, {tl.Notes.Count} notes");
        var folder = RtFolder();
        try
        {
            var expected = RtCopy(song);
            var tforge = RtViaTforge(song, folder, "demo");
            RtVerify("demo song", RtTforge, expected, tforge);
            RtVerify("demo song", RtGpEmbedded, expected, RtViaGp(song, folder, "demo", embed: true));
            var cleaned = RtViaGp(song, folder, "demo", embed: false);
            RtVerify("demo song", RtGpClean(), expected, cleaned);
            // A7-E03: alphaTab's GP7 reader drops a double bar on the last bar; the importer takes it from the file.
            Check("demo song: the double bar on the last bar survives the clean .gp round trip",
                expected.Tracks[0].Measures[bars - 1].IsDoubleBar && cleaned.Tracks.All(t => t.Measures[bars - 1].IsDoubleBar));
            Check("demo song: the performed bar order is the same after .tforge and clean .gp round trips",
                PlaybackOrder.Build(tforge, new PlaybackOptions()).SequenceEqual(order) && PlaybackOrder.Build(cleaned, new PlaybackOptions()).SequenceEqual(order));
            RtCheckTimelineSame("demo song", ".tforge", expected, tforge, exactVelocity: true);
            // Allow-listed for the clean .gp: a cell's sound length (SoundDurationPercent, a TabForge note-length setting) has no Guitar Pro field,
            // so the reference timeline plays those cells at full length; every other note event must match.
            var fullLength = RtCopy(expected);
            foreach (var cell in fullLength.Tracks.SelectMany(t => t.Measures).SelectMany(m => m.Cells.Concat(m.Voice2Cells))) cell.SoundDurationPercent = 100;
            // and the techniques the clean profile allow-lists as missing (no GP7 writer support) are not played by the reference either
            var lostTechniques = RtGpClean().Losses.Keys.Where(k => k.StartsWith("note.technique:", StringComparison.Ordinal) && k.EndsWith(".missing", StringComparison.Ordinal))
                .Select(k => k["note.technique:".Length..^".missing".Length]).ToHashSet();
            foreach (var n in fullLength.Tracks.SelectMany(t => t.Measures).SelectMany(m => m.Cells.Concat(m.Voice2Cells)).SelectMany(c => c.Notes))
                n.Techniques.RemoveWhere(lostTechniques.Contains);
            RtCheckTimelineSame("demo song", "clean .gp", fullLength, cleaned, exactVelocity: false);
        }
        finally { RtCleanup(folder); }
    }

    // ------------------------------------------------------------------ clean .gp fidelity (losses found by the demo song)

    private static void SfCleanGpFidelity()
    {
        var song = SfSong("Clean .gp fidelity", 7);
        var g = song.Tracks[0]; var bass = song.Tracks[1];
        // bar 1: tremolo picking 1/8, 1/16, 1/32 on beats of different lengths (the speed used to come back halved or quartered)
        SfClearBar(g, 0);
        foreach (var (slot, den, speed) in new[] { (0.0, 2, 8), (8.0, 4, 16), (12.0, 4, 32) })
            SfNote(g, 0, slot, den, 0, 1, 5, "TremoloPick").TremoloPickDenominator = speed;
        // bar 2: one-bar simile; bars 3-4: two-bar simile (both lost: the importer read the mark from the master bar)
        SfClearBar(g, 1); g.Measures[1].SimileOneBar = true;
        SfClearBar(g, 2); SfClearBar(g, 3); g.Measures[2].SimileTwoBar = true; g.Measures[3].SimileTwoBar = true;
        // bar 5: triplet feel (not written); a legato slide into an artificial harmonic (target read as the harmonic's pitch);
        // a bend grace before the last beat (alphaTab's GP7 writer dropped BendGrace, so the grace became a 32nd that pushed the beat late)
        SfBar(song, 4, m => m.TripletFeelKind = TripletFeels.Eighth);
        SfClearBar(g, 4);
        SfNote(g, 4, 0, 4, 0, 2, 3, "LegatoSlide").Notes[0].SlideTargetMidi = g.PitchOf(2, 5);
        var harmonic = SfNote(g, 4, 4, 4, 0, 2, 5, "ArtificialHarmonic").Notes[0];
        harmonic.HarmonicFret = 17; harmonic.MidiValue = GuitarProImporter.HarmonicMidi("Artificial", g.StringTunings[2], 5, 17);
        var principal = SfNote(g, 4, 12, 4, 0, 1, 7);
        var grace = RtNote(g, 1, 5, 95, "GraceBend", "Bend");
        grace.IsGraceNote = true; grace.GraceBeforeBeat = true; grace.GraceDurationSlots = 0.5;
        grace.BendPoints = new() { new() { Offset = 0, Value = 0 }, new() { Offset = 60, Value = 4 } };
        principal.Notes.Insert(0, grace);
        // bar 6: a 7-tuplet and a 13-tuplet run (whole-tick positions drifted early), and a dead-slapped bass note (not written)
        SfClearBar(g, 5);
        g.Measures[5].Cells = Enumerable.Range(0, 32).Select(_ => new TabCell()).ToList();   // 21 beats: more cells than sixteenths, as the importer builds them
        for (var k = 0; k < 7; k++) { var c = SfNote(g, 5, k * 4 / 7.0, 16, 0, 0, 3 + k % 3); c.TupletNumerator = 7; c.TupletDenominator = 4; }
        for (var k = 0; k < 13; k++) { var c = SfNote(g, 5, 4 + k * 8 / 13.0, 16, 0, 1, 2 + k % 4); c.TupletNumerator = 13; c.TupletDenominator = 8; }
        SfNote(g, 5, 12, 4, 0, 0, 7);
        SfClearBar(bass, 5);
        SfNote(bass, 5, 0, 2, 0, 1, 0, "DeadSlapped", "Slap", "Dead").Notes[0].Dead = true;
        SfNote(bass, 5, 8, 2, 0, 2, 3);
        // bar 7: a brushed chord with its own stroke speed (the export always wrote the default spread)
        SfClearBar(g, 6);
        var strum = SfNote(g, 6, 0, 2, 0, 0, 0, "BrushDown");
        strum.Notes.Add(RtNote(g, 1, 1, 95, "BrushDown")); strum.Notes.Add(RtNote(g, 2, 0, 95, "BrushDown")); strum.Notes.Add(RtNote(g, 3, 2, 95, "BrushDown"));
        strum.BrushStepSlots = 0.5;
        var folder = RtFolder();
        try
        {
            var expected = RtCopy(song);
            var cleaned = RtViaGp(song, folder, "fidelity", embed: false);
            RtVerify("clean .gp fidelity", RtGpClean(), expected, cleaned);
            var cg = cleaned.Tracks[0].Measures;
            Check("clean .gp: simile marks survive (one-bar on bar 2, two-bar on bars 3-4)", cg[1].SimileOneBar && cg[2].SimileTwoBar && cg[3].SimileTwoBar && !cg[0].SimileOneBar && !cg[4].SimileTwoBar,
                $"{cg[1].SimileOneBar}/{cg[2].SimileTwoBar}/{cg[3].SimileTwoBar}");
            Check("clean .gp: a triplet feel survives", cleaned.Tracks.All(t => t.Measures[4].TripletFeelKind == TripletFeels.Eighth), cg[4].TripletFeelKind);
            RtCheckTimelineSame("clean .gp fidelity", "clean .gp", expected, cleaned, exactVelocity: false);
        }
        finally { RtCleanup(folder); }
    }

    // ------------------------------------------------------------------ the group

    private static void TestSyntheticFixtures()
    {
        SfRepeatEndings();
        SfNavigation();
        SfTempo();
        SfMix();
        SfTuplets();
        SfVoices();
        SfSpans();
        SfCleanGpFidelity();
        SfDemoSong();
    }
}
