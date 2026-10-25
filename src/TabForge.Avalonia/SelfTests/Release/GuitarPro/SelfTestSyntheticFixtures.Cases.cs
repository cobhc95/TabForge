using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge;

// Owns: the synthetic fixture check bodies.
// Does not own: the rest of this type (see the main file).
// Tests: no dedicated test class.
public static partial class SelfTest
{
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

    // ------------------------------------------------------------------ the built-in demo song

    private static void SfDemoSong()
    {
        // The built-in demo song as the shared .gp file: written without the embedded project and read back.
        var song = GuitarProImporter.ImportBytes(GuitarProExporter.ToBytes(FullDemoSongFactory.Create(), embedProject: false), "demo.gp");
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

}
