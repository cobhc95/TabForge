using System.IO;
using System.Linq;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Rendering;
using TabForge.Services;

namespace TabForge;

// Regression tests for the clean .gp round-trip bugs found by the corpus sweep (`--roundtrip-diff`, work/archive/audit7/corpus-v2/fidelity).
// Each test builds a small synthetic song (no owner songs) that reproduces one cause and checks the reopened song.
public static partial class SelfTest
{
    private static void TestGpRoundTripFixes()
    {
        var folder = RtFolder();
        try
        {
            Guard(() => GpFixArpeggio(folder));
            Guard(() => GpFixShortBars(folder));
            Guard(() => GpFixClefs(folder));
            Guard(() => GpFixHarmonicTuning(folder));
            Guard(() => GpFixTempoRamps(folder));
            Guard(() => GpFixDrumSounds(folder));
            Guard(() => GpFixHighCapo(folder));
        }
        finally { RtCleanup(folder); }
    }

    private static TrackModel GpFixGuitar(int bars, string name = "Gtr") =>
        new() { Name = name, Kind = TrackKind.Guitar, MidiProgram = 29, MidiChannel = 0, Measures = TemplateFactory.Measures(bars) };

    // ---- 1. arpeggio strokes were written as brush strokes -------------------------------------------------------------
    // REGION:arpeggio
    private static void GpFixArpeggio(string folder)
    {
        var song = new SongProject { Title = "fix arpeggio", Tempo = 100 };
        var t = GpFixGuitar(3);
        song.Tracks.Add(t);
        // the importer tags an arpeggio stroke with its Arpeggio* name AND a Brush* name
        RtPut(t, 0, 0, 4, 0, RtNote(t, 0, 3, 95, "ArpeggioUp", "BrushUp"), RtNote(t, 1, 3, 95, "ArpeggioUp", "BrushUp"), RtNote(t, 2, 2, 95, "ArpeggioUp", "BrushUp"));
        RtPut(t, 1, 0, 4, 0, RtNote(t, 0, 5, 95, "ArpeggioDown", "BrushDown"), RtNote(t, 1, 5, 95, "ArpeggioDown", "BrushDown"));
        RtPut(t, 2, 0, 4, 0, RtNote(t, 0, 7, 95, "BrushDown"), RtNote(t, 1, 7, 95, "BrushDown"));
        var back = RtViaGp(song, folder, "arp", embed: false);
        bool Has(int bar, string tech) => back.Tracks[0].Measures[bar].Cells.Where(c => c.Notes.Count > 0).SelectMany(c => c.Notes).Any(n => n.Techniques.Contains(tech));
        Check("clean .gp keeps an arpeggio-up stroke (not a brush stroke)", Has(0, "ArpeggioUp"));
        Check("clean .gp keeps an arpeggio-down stroke", Has(1, "ArpeggioDown"));
        Check("clean .gp keeps a plain brush stroke a brush stroke", Has(2, "BrushDown") && !Has(2, "ArpeggioDown"));
        var before = RtNoteSig(RenderSpecBuilder.Compile(song)); var after = RtNoteSig(RenderSpecBuilder.Compile(back));
        Check("arpeggio song: same note onsets after a clean .gp round trip", before.Count == after.Count && before.Zip(after, (a, b) => Math.Abs(a.Onset - b.Onset) <= 1.0 && a.Midi == b.Midi).All(x => x),
            $"{before.Count} vs {after.Count} notes");
    }
    // ENDREGION

    // ---- 2. short bars (pickup, short last bar) were padded to full length -------------------------------------------------
    // REGION:shortbars
    private static void GpFixShortBars(string folder)
    {
        // an imported song: its incomplete bars play only as long as their content (ScoreToMidiCompiler.ContentSlots)
        var song = new SongProject { Title = "fix short bars", Tempo = 120, ImportedFrom = "synthetic.gp5" };
        var t = GpFixGuitar(6);
        var t2 = GpFixGuitar(6, "Gtr 2"); t2.MidiChannel = 1;
        song.Tracks.AddRange(new[] { t, t2 });
        RtPut(t, 0, 0, 4, 0, RtNote(t, 1, 3));                                   // bar 1: a one-beat pickup
        t.Measures[0].Anacrusis = true; t2.Measures[0].Anacrusis = true;
        RtPut(t2, 0, 0, 4, 0, RtNote(t2, 2, 2));
        for (var b = 1; b < 4; b++) { RtPut(t, b, 0, 2, 0, RtNote(t, 1, b)); RtPut(t, b, 8, 2, 0, RtNote(t, 1, b + 2)); RtPut(t2, b, 0, 1, 0, RtNote(t2, 3, b)); }
        RtPut(t, 4, 0, 4, 0, RtNote(t, 2, 5)); RtPut(t, 4, 4, 4, 0, RtNote(t, 2, 7)); RtPut(t2, 4, 0, 2, 0, RtNote(t2, 3, 5));   // bar 5: two beats (8 slots)
        RtPut(t, 5, 0, 4, 0, RtNote(t, 1, 0)); RtPut(t2, 5, 0, 4, 0, RtNote(t2, 3, 0));                                       // last bar: one beat
        var original = RenderSpecBuilder.Compile(song);
        var back = RtViaGp(song, folder, "short", embed: false);
        var reopened = RenderSpecBuilder.Compile(back);
        var o = string.Join(" ", original.Bars.Select(x => x.Slots)); var r = string.Join(" ", reopened.Bars.Select(x => x.Slots));
        Check("short bars: the pickup and the short bars keep their length after a clean .gp round trip", o == r && o.StartsWith("4 16 16 16 8 4", StringComparison.Ordinal), $"{o} vs {r}");
        Check("short bars: bar starts and song end are unchanged", original.Bars.Count == reopened.Bars.Count
            && original.Bars.Zip(reopened.Bars, (a, b) => Math.Abs(a.StartMs - b.StartMs) < 1.0).All(x => x) && Math.Abs(original.TotalMs - reopened.TotalMs) < 1.0,
            $"{original.TotalMs:0.0} vs {reopened.TotalMs:0.0} ms");
        Check("short bars: the first bar is still marked as a pickup (anacrusis)", back.Tracks[0].Measures[0].Anacrusis && !back.Tracks[0].Measures[1].Anacrusis);
        // a second pass changes nothing more
        var again = RenderSpecBuilder.Compile(RtViaGp(back, folder, "short2", embed: false));
        Check("short bars: a second round trip is stable", string.Join(" ", again.Bars.Select(x => x.Slots)) == r);

        // a pickup where another track (and the second voice) has nothing: the empty parts must not lengthen the pickup
        var lone = new SongProject { Title = "fix lone pickup", Tempo = 120, ImportedFrom = "synthetic.gp5" };
        var l1 = GpFixGuitar(3); var l2 = GpFixGuitar(3, "Gtr 2"); l2.MidiChannel = 1;
        lone.Tracks.AddRange(new[] { l1, l2 });
        l1.Measures[0].Anacrusis = true; l2.Measures[0].Anacrusis = true;
        RtPut(l1, 0, 0, 4, 0, RtNote(l1, 1, 3));                                   // only track 1 plays the one-beat pickup
        for (var b = 1; b < 3; b++) { RtPut(l1, b, 0, 1, 0, RtNote(l1, 1, b)); RtPut(l2, b, 0, 1, 0, RtNote(l2, 2, b)); }
        var v2 = l1.Measures[1].CellsForVoice(1, create: true); v2[0].DurationDenominator = 1; v2[0].Notes.Add(RtNote(l1, 4, 2));   // a second voice in bar 2 only
        var loneO = string.Join(" ", RenderSpecBuilder.Compile(lone).Bars.Select(x => x.Slots));
        var loneBack = RtViaGp(lone, folder, "lone", embed: false);
        var loneR = string.Join(" ", RenderSpecBuilder.Compile(loneBack).Bars.Select(x => x.Slots));
        Check("short bars: a pickup played by one track only (the other track and the second voice empty) keeps its length", loneO == loneR && loneO.StartsWith("4 16 16", StringComparison.Ordinal), $"{loneO} vs {loneR}");
        var loneR2 = string.Join(" ", RenderSpecBuilder.Compile(RtViaGp(loneBack, folder, "lone2", embed: false)).Bars.Select(x => x.Slots));
        Check("short bars: a one-track pickup is stable over a second round trip", loneR2 == loneR, $"{loneR} vs {loneR2}");

        // an empty bar in a 3/4 song stays a full bar (it used to be written as one quarter rest and reopened as a one-beat bar)
        var waltz = new SongProject { Title = "fix empty 3/4", Tempo = 90, TimeSignatureNumerator = 3, TimeSignatureDenominator = 4, ImportedFrom = "synthetic.gp5" };
        var w = GpFixGuitar(4);
        waltz.Tracks.Add(w);
        RtPut(w, 0, 0, 4, 0, RtNote(w, 1, 3)); RtPut(w, 0, 4, 4, 0, RtNote(w, 1, 5)); RtPut(w, 0, 8, 4, 0, RtNote(w, 1, 7));
        RtPut(w, 3, 0, 4, 0, RtNote(w, 1, 3)); RtPut(w, 3, 4, 4, 0, RtNote(w, 1, 5)); RtPut(w, 3, 8, 4, 0, RtNote(w, 1, 7));   // bars 2 and 3 are empty
        var waltzBars = RenderSpecBuilder.Compile(RtViaGp(waltz, folder, "waltz", embed: false)).Bars.Select(x => x.Slots).ToList();
        Check("empty bars in 3/4 stay full bars after a clean .gp round trip", waltzBars.SequenceEqual(new[] { 12, 12, 12, 12 }), string.Join(" ", waltzBars));

        // a song written in TabForge (not imported) keeps its silence: a bar with a short phrase is still a full bar
        var authored = new SongProject { Title = "fix authored", Tempo = 120 };
        var a = GpFixGuitar(3);
        authored.Tracks.Add(a);
        RtPut(a, 0, 0, 4, 0, RtNote(a, 1, 3)); RtPut(a, 1, 0, 4, 0, RtNote(a, 1, 5)); RtPut(a, 2, 0, 4, 0, RtNote(a, 1, 7));
        var authoredBars = RenderSpecBuilder.Compile(RtViaGp(authored, folder, "authored", embed: false)).Bars.Select(x => x.Slots).ToList();
        Check("a TabForge-written bar with a short phrase stays a full bar after a clean .gp round trip", authoredBars.SequenceEqual(new[] { 16, 16, 16 }), string.Join(" ", authoredBars));
    }
    // ENDREGION

    // ---- 3. the clef of a bar was never written (A7-V28) -----------------------------------------------------------------
    // REGION:clefs
    private static void GpFixClefs(string folder)
    {
        Check("clef mapping: bass F4, alto C3, tenor C4, treble and guitar G2 (guitar with the 8vb mark), neutral stays neutral",
            GuitarProExporter.GpClef(Clefs.Bass, true).Clef == AlphaTab.Model.Clef.F4 && GuitarProExporter.GpClef(Clefs.Alto, false).Clef == AlphaTab.Model.Clef.C3
            && GuitarProExporter.GpClef("C4", false).Clef == AlphaTab.Model.Clef.C4 && GuitarProExporter.GpClef(Clefs.Treble, false) == (AlphaTab.Model.Clef.G2, AlphaTab.Model.Ottavia.Regular)
            && GuitarProExporter.GpClef(Clefs.Guitar, false) == (AlphaTab.Model.Clef.G2, AlphaTab.Model.Ottavia._8vb) && GuitarProExporter.GpClef("Neutral", false).Clef == AlphaTab.Model.Clef.Neutral);
        var song = new SongProject { Title = "fix clefs", Tempo = 100 };
        var gtr = GpFixGuitar(6);
        var bass = new TrackModel { Name = "Bass", Kind = TrackKind.Bass, MidiProgram = 33, MidiChannel = 1, StringTunings = new() { 43, 38, 33, 28 }, Measures = TemplateFactory.Measures(6) };
        song.Tracks.AddRange(new[] { gtr, bass });
        string[] gtrClefs = { Clefs.Guitar, Clefs.Treble, Clefs.Bass, Clefs.Alto, "C4", Clefs.Guitar };
        for (var b = 0; b < 6; b++)
        {
            gtr.Measures[b].Clef = gtrClefs[b]; bass.Measures[b].Clef = Clefs.Bass;
            RtPut(gtr, b, 0, 4, 0, RtNote(gtr, 1, 3)); RtPut(bass, b, 0, 4, 0, RtNote(bass, 1, 3));
        }
        var back = RtViaGp(song, folder, "clef", embed: false);
        var expected = new[] { Clefs.Guitar, Clefs.Treble, Clefs.Bass, "C3", "C4", Clefs.Guitar };
        Check("clean .gp keeps the clef of every bar, including clef changes mid-track", back.Tracks[0].Measures.Select(m => m.Clef).SequenceEqual(expected), string.Join(",", back.Tracks[0].Measures.Select(m => m.Clef)));
        Check("clean .gp keeps the bass clef of a bass track", back.Tracks[1].Measures.All(m => m.Clef == Clefs.Bass), string.Join(",", back.Tracks[1].Measures.Select(m => m.Clef)));
        var again = RtViaGp(back, folder, "clef2", embed: false);
        Check("clefs are stable over a second round trip", again.Tracks[0].Measures.Select(m => m.Clef).SequenceEqual(expected) && again.Tracks[1].Measures.All(m => m.Clef == Clefs.Bass));
    }
    // ENDREGION

    // ---- 4. strings played in harmonics came back tuned an octave higher, a little more on every round trip ---------------------
    // REGION:harmonics
    private static void GpFixHarmonicTuning(string folder)
    {
        var song = new SongProject { Title = "fix harmonics", Tempo = 100 };
        var t = GpFixGuitar(3);
        song.Tracks.Add(t);
        // the B string is played only in 12th-fret artificial harmonics (sounding an octave above the fretted note), plus a plain low string note
        for (var b = 0; b < 3; b++)
        {
            var h = RtNote(t, 1, 12, 95, "ArtificialHarmonic"); h.HarmonicFret = 12; h.MidiValue = t.PitchOf(1, 12) + 12;
            var h2 = RtNote(t, 1, 12, 95, "ArtificialHarmonic"); h2.HarmonicFret = 12; h2.MidiValue = t.PitchOf(1, 12) + 12;
            RtPut(t, b, 0, 4, 0, h); RtPut(t, b, 4, 4, 0, h2); RtPut(t, b, 8, 4, 0, RtNote(t, 5, 3));
        }
        var tuning = string.Join(",", t.StringTunings);
        var back = RtViaGp(song, folder, "harm", embed: false);
        Check("harmonics: the track tuning is unchanged after a clean .gp round trip", string.Join(",", back.Tracks[0].StringTunings) == tuning, $"{tuning} -> {string.Join(",", back.Tracks[0].StringTunings)}");
        var again = RtViaGp(back, folder, "harm2", embed: false);
        Check("harmonics: the tuning stays put on a second round trip", string.Join(",", again.Tracks[0].StringTunings) == tuning, string.Join(",", again.Tracks[0].StringTunings));
        var before = RtNoteSig(RenderSpecBuilder.Compile(song)); var after = RtNoteSig(RenderSpecBuilder.Compile(again));
        Check("harmonics: the same pitches sound after two round trips", before.Count == after.Count && before.Zip(after, (a, b) => a.Midi == b.Midi).All(x => x),
            string.Join(",", before.Select(x => x.Midi).Distinct()) + " vs " + string.Join(",", after.Select(x => x.Midi).Distinct()));
    }
    // ENDREGION

    // ---- 5. tempo ramps cut short by the bar end or by the next point changed the song's timing --------------------------------
    // REGION:ramps
    private static void GpFixTempoRamps(string folder)
    {
        var song = new SongProject { Title = "fix ramps", Tempo = 100 };
        var t = GpFixGuitar(6);
        song.Tracks.Add(t);
        for (var b = 0; b < 6; b++) RtPut(t, b, 0, 1, 0, RtNote(t, 1, b));
        t.Measures[1].MidBarTempos = new List<TempoPoint> { new(10, 50, 64) };                         // a ramp far longer than the bar: only part of it plays in the bar
        t.Measures[3].MidBarTempos = new List<TempoPoint> { new(12, 160, 40), new(14, 85) };           // a ramp cut short by the next point
        t.Measures[4].MidBarTempos = new List<TempoPoint> { new(2, 140, 8), new(12, 70) };             // a ramp that fits, then a step
        var original = RenderSpecBuilder.Compile(song);
        var back = RtViaGp(song, folder, "ramp", embed: false);
        var reopened = RenderSpecBuilder.Compile(back);
        var worst = original.Bars.Count != reopened.Bars.Count ? double.NaN
            : original.Bars.Zip(reopened.Bars, (a, b) => Math.Abs(a.StartMs - b.StartMs)).DefaultIfEmpty(0).Max();
        Check("tempo ramps: every bar starts at the same time after a clean .gp round trip (ramps longer than a bar, cut short, and complete)", worst < 12.0, $"worst {worst:0.0} ms");
        Check("tempo ramps: the song lasts as long after a clean .gp round trip", Math.Abs(original.TotalMs - reopened.TotalMs) < 12.0, $"{original.TotalMs:0.0} vs {reopened.TotalMs:0.0} ms");
        var second = RenderSpecBuilder.Compile(RtViaGp(back, folder, "ramp2", embed: false));
        Check("tempo ramps: a second round trip does not drift further", Math.Abs(second.TotalMs - reopened.TotalMs) < 6.0, $"{reopened.TotalMs:0.0} vs {second.TotalMs:0.0} ms");
    }
    // ENDREGION

    // ---- 6 and 7. drum notes without a sound / tie chains, and a high capo ------------------------------------------------------
    // REGION:drums
    private static void GpFixDrumSounds(string folder)
    {
        // The importer-side cases need a Guitar Pro 3-5 file whose notes alphaTab reads without a sound (fret -1, value 0); the exporter side is
        // checked here: every drum sound in the model, including values outside the GM range and long tie chains, reopens as the same sound.
        var song = new SongProject { Title = "fix drums", Tempo = 100 };
        var kit = new TrackModel { Name = "Kit", Kind = TrackKind.Drums, MidiProgram = 0, MidiChannel = 9, InstrumentName = "Drum Kit (Standard)", StringTunings = new() { 49, 42, 48, 38, 43, 36 }, Measures = TemplateFactory.Measures(3) };
        song.Tracks.Add(kit);
        int[] sounds = { 36, 38, 42, 46, 48, 49, 57 };
        for (var b = 0; b < 3; b++)
            for (var k = 0; k < 8; k++)
            {
                var midi = sounds[(b * 8 + k) % sounds.Length];
                var note = new TabNote { StringIndex = GuitarProImporter.DrumLine(midi), Fret = midi, MidiValue = midi, Velocity = 95 };
                RtPut(kit, b, k * 2, 8, 0, note);
            }
        // a tie chain on the crash: the origin, then three tie destinations
        var crash = kit.Measures[2];
        for (var k = 0; k < 4; k++)
        {
            var cell = crash.Cells[10 + k]; cell.Notes.Clear();
            cell.DurationDenominator = 16;
            var tied = new TabNote { StringIndex = 0, Fret = 49, MidiValue = 49, Velocity = 95, Tied = k > 0 };
            if (k < 3) tied.Techniques.Add("Tie");
            cell.Notes.Add(tied);
        }
        var back = RtViaGp(song, folder, "drums", embed: false);
        var before = RtNoteSig(RenderSpecBuilder.Compile(song)); var after = RtNoteSig(RenderSpecBuilder.Compile(back));
        Check("drums: the same sounds at the same times after a clean .gp round trip", before.Count == after.Count && before.Zip(after, (a, b) => a.Midi == b.Midi && Math.Abs(a.Onset - b.Onset) <= 1.0 && Math.Abs(a.Dur - b.Dur) <= 30).All(x => x),
            $"{before.Count} vs {after.Count} notes");
        // a drum articulation outside the GM drum range keeps its sound (it used to come back as another number)
        var odd = new SongProject { Title = "fix odd drum", Tempo = 100 };
        var oddKit = new TrackModel { Name = "Kit", Kind = TrackKind.Drums, MidiProgram = 0, MidiChannel = 9, StringTunings = new() { 49, 42, 48, 38, 43, 36 }, Measures = TemplateFactory.Measures(1) };
        odd.Tracks.Add(oddKit);
        RtPut(oddKit, 0, 0, 4, 0, new TabNote { StringIndex = 2, Fret = 12, MidiValue = 12, Velocity = 95 });
        RtPut(oddKit, 0, 4, 4, 0, new TabNote { StringIndex = 2, Fret = 90, MidiValue = 90, Velocity = 95 });
        var oddBack = RtViaGp(odd, folder, "odd", embed: false);
        var oddSounds = oddBack.Tracks[0].Measures[0].Cells.SelectMany(c => c.Notes).Select(n => n.MidiValue).ToList();
        // a long tie chain as alphaTab reads a GP3-5 drum part: every destination has no sound of its own, only the first note does
        var chainOrigin = new AlphaTab.Model.Note { Fret = 49, String = 1 };
        var chainFirst = new AlphaTab.Model.Note { Fret = -1, String = -1, IsTieDestination = true, TieOrigin = chainOrigin };
        var chainSecond = new AlphaTab.Model.Note { Fret = -1, String = -1, IsTieDestination = true, TieOrigin = chainFirst };
        var chainThird = new AlphaTab.Model.Note { Fret = -1, String = -1, IsTieDestination = true, TieOrigin = chainSecond };
        Check("drums: a tie destination takes its sound from the end of the tie chain (it used to stop after one step)",
            GuitarProImporter.DrumPitch(chainFirst) == 49 && GuitarProImporter.DrumPitch(chainThird) == 49, $"{GuitarProImporter.DrumPitch(chainFirst)}, {GuitarProImporter.DrumPitch(chainThird)}");
        Check("drums: a sound outside the GM drum range keeps its number after a clean .gp round trip", oddSounds.SequenceEqual(new[] { 12, 90 }), string.Join(",", oddSounds));
    }

    // ENDREGION

    // REGION:capo
    private static void GpFixHighCapo(string folder)
    {
        var song = new SongProject { Title = "fix capo", Tempo = 100 };
        var synth = new TrackModel { Name = "Synth", Kind = TrackKind.Guitar, MidiProgram = 95, MidiChannel = 0, StringTunings = new() { 43, 38, 33, 28 }, Capo = 16, Measures = TemplateFactory.Measures(2) };
        song.Tracks.Add(synth);
        RtPut(synth, 0, 0, 4, 0, RtNote(synth, 0, 5)); RtPut(synth, 0, 4, 4, 0, RtNote(synth, 1, 7)); RtPut(synth, 1, 0, 4, 0, RtNote(synth, 2, 0));
        // a synth track whose notes carry their own sounding pitch (16 above tuning + fret): the tuning in the source file was not the real one
        var pitched = new TrackModel { Name = "Pitched", Kind = TrackKind.Guitar, MidiProgram = 90, MidiChannel = 1, StringTunings = new() { 43, 38, 33, 28 }, Measures = TemplateFactory.Measures(2) };
        song.Tracks.Add(pitched);
        foreach (var (slot, s, f) in new[] { (0, 0, 5), (4, 1, 7), (8, 2, 0) })
        {
            var n = RtNote(pitched, s, f); n.MidiValue += 16;
            RtPut(pitched, 0, slot, 4, 0, n);
        }
        // a tied note on that track: the tie destination carries its origin's (sounding) pitch and must move with it
        var tieFrom = RtNote(pitched, 1, 3); tieFrom.MidiValue += 16; tieFrom.Techniques.Add("Tie");
        var tieTo = RtNote(pitched, 1, 3); tieTo.MidiValue += 16; tieTo.Tied = true;
        RtPut(pitched, 1, 0, 2, 0, tieFrom); RtPut(pitched, 1, 8, 2, 0, tieTo);
        var before = RtNoteSig(RenderSpecBuilder.Compile(song));
        var back = RtViaGp(song, folder, "capo", embed: false);
        var after = RtNoteSig(RenderSpecBuilder.Compile(back));
        Check("capo and pitched notes: a track with a high capo, and a track whose notes sound above tuning + fret, keep their pitches after a clean .gp round trip", before.Count == after.Count && before.Zip(after, (a, b) => a.Midi == b.Midi).All(x => x),
            string.Join(",", before.Select(x => x.Midi)) + " vs " + string.Join(",", after.Select(x => x.Midi)));
    }
    // ENDREGION
}
