using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Rendering;
using TabForge.Services;

namespace TabForge;

// Synthetic score fixtures: original songs written in code, each with hand-specified expected facts. `TabForge.exe --write-gp-fixtures <dir>` writes
// clean .gp files (verified: no embedded project, no sidecar, in a fresh folder) plus MANIFEST.md listing every bar and beat. The self-test group
// gp-fidelity checks each fixture exactly (pitch, onset, duration, rests, signatures, tempo, repeats...) against the reopened clean file.
public static partial class SelfTest
{
    /// <summary>A fixture: the song, why it exists, and hand-written sounding pitches (MIDI) the model and the reopened file must both give.</summary>
    private sealed record GfFixture(string Id, string Purpose, SongProject Song, (string Where, int Midi)[] Anchors);

    private static TrackModel GfTrack(string name, int bars, int channel, int program = 29, List<int>? tuning = null, TrackKind kind = TrackKind.Guitar)
    {
        var t = new TrackModel { Name = name, Kind = kind, MidiProgram = program, MidiChannel = channel, Measures = TemplateFactory.Measures(bars) };
        if (tuning is not null) t.StringTunings = tuning;
        return t;
    }

    private static List<GfFixture> GfFixtures()
    {
        var list = new List<GfFixture>();

        // 01 standard guitar and bass
        var basic = BuildSyntheticGpSong();
        list.Add(new("01-guitar-bass-basic", "Standard guitar (EADGBE) and 4-string bass, quarter notes: pitch, count, onset, duration.", basic,
            new[] { ("guitar bar 1 beat 1 = string 1 (high E) fret 0", 0) }.Take(0).ToArray()));

        // 02 alternate tunings, capo, a pitched keyboard-programmed track
        var tun = new SongProject { Title = "gf tunings capo", Tempo = 100 };
        var dadgad = GfTrack("DADGAD", 2, 0, 25, new() { 62, 57, 55, 50, 45, 38 });
        var seven = GfTrack("Seven", 2, 1, 30, new() { 64, 59, 55, 50, 45, 40, 35 });
        var bass5 = GfTrack("Bass 5", 2, 2, 33, new() { 43, 38, 33, 28, 23 }, TrackKind.Bass);
        var capo = GfTrack("Capo 2", 2, 3, 25); capo.Capo = 2;
        var keys = GfTrack("Keys", 2, 4, 0, null, TrackKind.Keys);
        tun.Tracks.AddRange(new[] { dadgad, seven, bass5, capo, keys });
        GfPut(dadgad, 0, 0, 1, RtNote(dadgad, 5, 0)); GfPut(dadgad, 1, 0, 1, RtNote(dadgad, 0, 0));          // low D string open = D2 (38), high D open = D4 (62)
        GfPut(seven, 0, 0, 1, RtNote(seven, 6, 0)); GfPut(seven, 1, 0, 1, RtNote(seven, 0, 3));             // low B open = B0 (35), high E fret 3 = G4 (67)
        GfPut(bass5, 0, 0, 1, RtNote(bass5, 4, 0)); GfPut(bass5, 1, 0, 1, RtNote(bass5, 0, 2));            // low B (23), G string fret 2 = A2 (45)
        GfPut(capo, 0, 0, 1, RtNote(capo, 1, 3)); GfPut(capo, 1, 0, 1, RtNote(capo, 0, 0));                // capo 2: B string fret 3 = B3+2+3 = 64; high E open = 66
        GfPut(keys, 0, 0, 1, RtNote(keys, 2, 0)); GfPut(keys, 1, 0, 1, RtNote(keys, 1, 1));                // G3 (55); B string fret 1 = C4 (60)
        list.Add(new("02-tunings-capo-keys", "Alternate tunings (DADGAD, 7-string, 5-string bass), a capo track, a keyboard-programmed track.", tun,
            new[] { ("DADGAD bar 1 low string open", 38), ("DADGAD bar 2 high string open", 62), ("7-string bar 1 low B", 35), ("7-string bar 2 high E fret 3", 67), ("Bass 5 bar 1 low B", 23), ("Bass 5 bar 2 G fret 2", 45),
                ("capo 2, B string fret 3", 64), ("capo 2, high E open", 66), ("Keys bar 1 G string open", 55), ("Keys bar 2 B string fret 1", 60) }));

        // 03 transposed tracks (a .gp has no playback transposition: the sounding pitch is carried by the tuning or by string and fret)
        var tr = new SongProject { Title = "gf transposed", Tempo = 100 };
        var up = GfTrack("Up 2", 1, 0); up.Transpose = 2; var down = GfTrack("Bass down octave", 1, 1, 33, new() { 43, 38, 33, 28 }, TrackKind.Bass); down.Transpose = -12;
        tr.Tracks.AddRange(new[] { up, down });
        GfPut(up, 0, 0, 1, RtNote(up, 0, 0)); GfPut(down, 0, 0, 1, RtNote(down, 3, 0));                      // high E open +2 = F#4 (66); E1 open -12 = E0 (16)
        list.Add(new("03-transposed", "Track transpose +2 (guitar) and -12 (bass): the sounding pitch must stay, carried by the tuning.", tr, new[] { ("Up 2 bar 1 high E open, sounding", 66), ("Bass down octave bar 1 E open, sounding", 16) }));

        // 04 drums
        var dr = new SongProject { Title = "gf drums", Tempo = 100 };
        var kit = new TrackModel { Name = "Kit", Kind = TrackKind.Drums, MidiProgram = 0, MidiChannel = 9, InstrumentName = "Drum Kit (Standard)", StringTunings = new() { 49, 42, 48, 38, 43, 36 }, Measures = TemplateFactory.Measures(2) };
        dr.Tracks.Add(kit);
        var sounds = new[] { 36, 38, 42, 46, 49, 51 };
        for (var k = 0; k < 8; k++) GfPut(kit, 0, k * 2, 8, Drum(sounds[k % sounds.Length], k % 2 == 0 ? 110 : 80));
        GfPut(kit, 1, 0, 4, Drum(36, 112), Drum(42, 64), Drum(49, 127));
        list.Add(new("04-drums", "Drum kit: GM kick 36, snare 38, hats 42/46, crash 49, ride 51; a chord of three sounds.", dr, new[] { ("kit bar 1 beat 1 = kick", 36), ("kit bar 1 beat 2 = snare", 38) }));

        // 05 voices, rests, ties, simultaneous notes with different expression
        var vr = new SongProject { Title = "gf voices rests ties", Tempo = 90 };
        var g = GfTrack("Gtr", 4, 0); var g2 = GfTrack("Gtr 2", 4, 1);
        vr.Tracks.AddRange(new[] { g, g2 });
        GfPut(g, 0, 0, 4, RtNote(g, 1, 3)); GfPut(g, 0, 4, 8); GfPut(g, 0, 6, 8, RtNote(g, 1, 5)); GfPut(g, 0, 8, 2, RtNote(g, 1, 7));   // note, eighth rest, eighth note, half note
        GfPut(g, 1, 0, 1, RtNote(g, 2, 5)); GfPut(g, 2, 0, 4, RtNote(g, 2, 5)).Notes[0].Tied = true;                                         // a whole note tied over the bar line
        GfPut(g, 2, 4, 4, RtNote(g, 1, 3, 95, "Ghost"), RtNote(g, 2, 2)); g.Measures[2].Cells[4].Notes[0].Ghost = true; g.Measures[2].Cells[4].Accent = 1;
        GfPut(g, 3, 0, 4, RtNote(g, 1, 3)); GfPut(g, 3, 4, 4); GfPut(g, 3, 8, 4); GfPut(g, 3, 12, 4, RtNote(g, 1, 1));
        var v2 = g.Measures[0].CellsForVoice(1, create: true); v2[0].DurationDenominator = 2; v2[0].Notes.Add(RtNote(g, 4, 2)); v2[8].DurationDenominator = 2; v2[8].Notes.Add(RtNote(g, 4, 3));
        GfPut(g2, 0, 0, 1, RtNote(g2, 3, 0)); GfPut(g2, 1, 0, 2, RtNote(g2, 3, 2)); GfPut(g2, 1, 8, 2); GfPut(g2, 2, 0, 1); GfPut(g2, 3, 0, 1, RtNote(g2, 3, 0));
        GuitarProImporter.LinkTieOrigins(g);
        list.Add(new("05-voices-rests-ties", "Two tracks; voice 2 in bar 1; eighth/quarter/whole rests; a tie across a bar line; a ghost note beside a normal note in one chord.", vr,
            new[] { ("Gtr bar 1 beat 1 = B string fret 3", 62), ("Gtr bar 1 voice 2 beat 1 = D string fret 2", 52), ("Gtr bar 2 whole note = G string fret 5", 60) }));

        // 06 tuplets and graces
        var tg = new SongProject { Title = "gf tuplets graces", Tempo = 80 };
        var tgt = GfTrack("Gtr", 3, 0); tg.Tracks.Add(tgt);
        for (var k = 0; k < 3; k++) { var c = RtPut(tgt, 0, k * 4 / 3.0, 8, 0, RtNote(tgt, 1, 3 + k)); c.IsTriplet = true; }
        for (var k = 0; k < 5; k++) { var c = RtPut(tgt, 0, 4 + k * 0.8, 16, 0, RtNote(tgt, 1, 3 + k)); c.TupletNumerator = 5; c.TupletDenominator = 4; }
        GfPut(tgt, 0, 8, 2, RtNote(tgt, 2, 4));
        var gb = GfPut(tgt, 1, 0, 4, RtNote(tgt, 1, 5)); gb.Notes.Insert(0, new TabNote { StringIndex = 1, Fret = 3, MidiValue = tgt.PitchOf(1, 3), IsGraceNote = true, GraceBeforeBeat = true, GraceDurationSlots = 1, Velocity = 95 });
        var go = GfPut(tgt, 1, 4, 4, RtNote(tgt, 1, 7)); go.Notes.Insert(0, new TabNote { StringIndex = 1, Fret = 5, MidiValue = tgt.PitchOf(1, 5), IsGraceNote = true, GraceBeforeBeat = false, GraceDurationSlots = 1, Velocity = 95 });
        GfPut(tgt, 1, 8, 2, RtNote(tgt, 1, 3)); GfPut(tgt, 2, 0, 1, RtNote(tgt, 1, 0));
        list.Add(new("06-tuplets-graces", "Triplet eighths, a quintuplet of 16ths, a grace note before the beat and one on the beat.", tg, new[] { ("bar 1 triplet 1 = B string fret 3", 62), ("bar 2 beat 1 principal = B string fret 5", 64) }));

        // 07 signatures, key, pickup, fermata, double bar
        var sg = new SongProject { Title = "gf signatures", Tempo = 100, KeySignature = 2 };
        var st = GfTrack("Gtr", 8, 0); sg.Tracks.Add(st);
        st.Measures[0].Anacrusis = true; GfPut(st, 0, 0, 4, RtNote(st, 1, 3));
        st.Measures[1].TimeSigNum = 3; st.Measures[1].TimeSigDenom = 4; st.Measures[2].TimeSigNum = 6; st.Measures[2].TimeSigDenom = 8; st.Measures[3].TimeSigNum = 5; st.Measures[3].TimeSigDenom = 4;
        st.Measures[4].TimeSigNum = 7; st.Measures[4].TimeSigDenom = 8; st.Measures[5].TimeSigNum = 4; st.Measures[5].TimeSigDenom = 4;
        st.Measures[5].KeySignature = -3; st.Measures[5].KeySignatureMinor = true; st.Measures[6].IsDoubleBar = true;
        st.Measures[3].Cells.AddRange(Enumerable.Range(0, 4).Select(_ => new TabCell()));   // a 5/4 bar is 20 slots: the template bar has 16 cells
        for (var b = 1; b < 8; b++) { var slots = (int)MusicTime.BarSlots(sg, b); for (var k = 0; k < slots / 4; k++) GfPut(st, b, k * 4, 4, RtNote(st, 1, (b + k) % 8)); }
        st.Measures[6].Cells[0].Fermata = true;
        list.Add(new("07-signatures-pickup", "Pickup bar; 3/4, 6/8, 5/4, 7/8, 4/4; key change D major -> C minor; a double bar; a fermata.", sg, new[] { ("bar 1 pickup note = B string fret 3", 62) }));

        // 08 tempo steps and ramps
        var tp = new SongProject { Title = "gf tempo", Tempo = 60 };
        var tt = GfTrack("Gtr", 6, 0); tp.Tracks.Add(tt);
        for (var b = 0; b < 6; b++) GfPut(tt, b, 0, 1, RtNote(tt, 1, b));
        tt.Measures[1].TempoChange = 120; tt.Measures[2].TempoChange = 300; tt.Measures[3].MidBarTempos = new List<TempoPoint> { new(8, 90) }; tt.Measures[4].MidBarTempos = new List<TempoPoint> { new(0, 40, 16) };
        list.Add(new("08-tempo", "Tempo 60 -> 120 -> 300 (step), a mid-bar step to 90, a whole-bar ramp to 40.", tp, new[] { ("bar 1 note = B string open", 59) }));

        // 09 repeats, endings, jumps, sections
        var rp = new SongProject { Title = "gf repeats jumps", Tempo = 120 };
        var rt = GfTrack("Gtr", 10, 0); rp.Tracks.Add(rt);
        for (var b = 0; b < 10; b++) GfPut(rt, b, 0, 1, RtNote(rt, 1, b));
        rt.Measures[1].RepeatStart = true; rt.Measures[3].RepeatEnd = true; rt.Measures[3].RepeatCount = 3; rt.Measures[3].AlternateEndingMask = 0b011; rt.Measures[4].AlternateEnding = 3;
        rt.Measures[5].Directions = "Segno"; rt.Measures[7].Directions = "ToCoda"; rt.Measures[8].Directions = "DalSegnoAlCoda"; rt.Measures[9].Directions = "Coda,Fine";
        rp.Markers.Add(new MarkerModel { MeasureIndex = 0, Title = "Intro" }); rp.Markers.Add(new MarkerModel { MeasureIndex = 5, Title = "Bridge" });
        list.Add(new("09-repeats-jumps", "Repeat x3 with endings 1.2. / 3.; Segno, To Coda, D.S. al Coda, Coda, Fine; two section markers.", rp, new[] { ("bar 1 note = B string open", 59) }));

        // 10 / 11 every technique named by an allowance, and the same bars with none (spurious-addition detector)
        var present = RtTechniqueSong();
        list.Add(new("10-techniques-present", "One bar per technique of the technique song (bends, harmonics, slides, tremolo, brush, grace, ...), in the model's own conventions.", present, Array.Empty<(string, int)>()));
        var absent = RtTechniqueSong();
        foreach (var cell in absent.Tracks[0].Measures.SelectMany(m => m.Cells))
        {
            cell.Accent = 0; cell.Staccato = false; cell.Tenuto = false; cell.Fermata = false; cell.WhammyPoints.Clear(); cell.TremoloPickDenominator = 0; cell.BrushStepSlots = 0;
            cell.Notes.RemoveAll(n => n.IsGraceNote);
            foreach (var n in cell.Notes) { n.Techniques.Clear(); n.Ghost = false; n.Dead = false; n.BendPoints.Clear(); n.HarmonicFret = null; n.SlideTargetMidi = 0; n.TrillTargetMidi = 0; n.Tied = false; n.MidiValue = absent.Tracks[0].PitchOf(n.StringIndex, n.Fret); }
        }
        list.Add(new("11-techniques-absent", "The technique song's bars with every technique removed: a reopened file must not gain any.", absent, Array.Empty<(string, int)>()));

        // 12 curves and harmonics with known sounding pitches
        var bh = new SongProject { Title = "gf bends harmonics", Tempo = 100 };
        var bt = GfTrack("Gtr", 4, 0); bh.Tracks.Add(bt);
        GfPut(bt, 0, 0, 4, RtNote(bt, 1, 5, 95, "Bend")).Notes[0].BendPoints = new() { new() { Offset = 0, Value = 0 }, new() { Offset = 15, Value = 4 }, new() { Offset = 60, Value = 4 } };   // bend up one tone: ends at 66
        GfPut(bt, 0, 4, 4, RtNote(bt, 1, 7, 95, "Bend")).Notes[0].BendPoints = new() { new() { Offset = 0, Value = 4 }, new() { Offset = 60, Value = 4 } };                                   // pre-bend
        GfPut(bt, 1, 0, 4, new TabNote { StringIndex = 1, Fret = 12, MidiValue = GuitarProImporter.HarmonicMidi("Natural", bt.StringTunings[1], 12, 12), HarmonicFret = 12, Techniques = { "Harmonic" } });
        GfPut(bt, 1, 4, 4, new TabNote { StringIndex = 1, Fret = 7, MidiValue = GuitarProImporter.HarmonicMidi("Natural", bt.StringTunings[1], 7, 7), HarmonicFret = 7, Techniques = { "Harmonic" } });
        GfPut(bt, 1, 8, 4, new TabNote { StringIndex = 1, Fret = 5, MidiValue = GuitarProImporter.HarmonicMidi("Artificial", bt.StringTunings[1], 5, 12), HarmonicFret = 12, Techniques = { "ArtificialHarmonic" } });
        GfPut(bt, 2, 0, 4, RtNote(bt, 1, 3)).WhammyPoints = new() { new() { Offset = 0, Value = 0 }, new() { Offset = 30, Value = -8 }, new() { Offset = 60, Value = 0 } };
        list.Add(new("12-bends-whammy-harmonics", "Bend up a tone and a pre-bend; natural harmonics at 12 and 7; an artificial harmonic; a dip.", bh,
            new[] { ("bar 1 bend: fretted note B string fret 5", 64), ("bar 2 natural harmonic fret 12 on the B string sounds an octave up", 71), ("bar 2 natural harmonic fret 7 sounds a 12th above the open B", 78) }));

        // 13 extreme but valid dynamics and timing
        var ex = new SongProject { Title = "gf extremes", Tempo = 300 };
        var et = GfTrack("Gtr", 3, 0); ex.Tracks.Add(et);
        for (var k = 0; k < 8; k++) GfPut(et, 0, k * 2, 8, RtNote(et, 1, k, Dynamics.Velocities[k]));                                   // ppp .. fff
        for (var k = 0; k < 16; k++) GfPut(et, 1, k, 16, RtNote(et, 2, k % 5));                                                         // sixteen 16ths at 300 bpm
        for (var k = 0; k < 4; k++) { var c = GfPut(et, 2, k * 4, 32, RtNote(et, 2, k)); }                                              // 32nds
        ex.Tracks[0].Measures[2].TempoChange = 40;
        list.Add(new("13-dynamics-timing-extremes", "All eight dynamics ppp..fff, 16ths at 300 bpm, 32nds at 40 bpm.", ex, new[] { ("bar 1 beat 1 = B string fret 0 (ppp)", 59) }));

        // 14 marks that were repaired: fingering, tenuto, palm mute per note
        var mk = GfSong(1, t => { GfPut(t, 0, 0, 4, RtNote(t, 1, 3)).Notes[0].LeftHandFinger = 2; GfPut(t, 0, 4, 4, RtNote(t, 1, 5)).Tenuto = true; GfPut(t, 0, 8, 4, RtNote(t, 1, 8, 95, "PalmMute"), RtNote(t, 2, 6)); });
        mk.Title = "gf marks";
        list.Add(new("14-fingering-tenuto-mute", "Left-hand finger 2; tenuto; palm mute on one note of a chord.", mk, new[] { ("beat 1 = B string fret 3", 62) }));

        // 15 track mix
        var mx = new SongProject { Title = "gf mix", Tempo = 100 };
        var m1 = GfTrack("Loud left", 1, 0); m1.Volume = 120; m1.Pan = 0; var m2 = GfTrack("Quiet right", 1, 1); m2.Volume = 40; m2.Pan = 127; var m3 = GfTrack("Muted", 1, 2); m3.Mute = true; var m4 = GfTrack("Solo", 1, 3); m4.Solo = true;
        mx.Tracks.AddRange(new[] { m1, m2, m3, m4 });
        foreach (var t in mx.Tracks) GfPut(t, 0, 0, 1, RtNote(t, 1, 3));
        list.Add(new("15-track-mix", "Track volume/pan (quantised to the 0..16 scale), mute and solo.", mx, new[] { ("every track bar 1 = B string fret 3", 62) }));
        return list;
    }

    /// <summary>The expected facts of a fixture as text: song header, then per bar the signatures and per track voice every beat (onset, length, spelling, notes).</summary>
    private static string GfManifestText(GfFixture f)
    {
        var p = f.Song; var sb = new StringBuilder();
        sb.AppendLine($"### {f.Id}");
        sb.AppendLine();
        sb.AppendLine(f.Purpose);
        sb.AppendLine();
        sb.AppendLine($"Song: tempo {p.Tempo}, key {p.KeySignature}{(p.KeySignatureMinor ? "m" : "")}, {p.TimeSignatureNumerator}/{p.TimeSignatureDenominator}, {p.Tracks.Count} track(s).");
        if (f.Anchors.Length > 0)
        {
            sb.AppendLine(); sb.AppendLine("Hand-specified sounding pitches (asserted by the test, independent of the code that wrote the file):");
            foreach (var (where, midi) in f.Anchors) sb.AppendLine($"- {where}: MIDI {midi}");
        }
        for (var t = 0; t < p.Tracks.Count; t++)
        {
            var track = p.Tracks[t];
            sb.AppendLine();
            sb.AppendLine($"Track {t + 1} \"{track.Name}\": {(track.Kind == TrackKind.Drums || track.MidiChannel == 9 ? "drums" : "tuning " + string.Join(",", track.StringTunings))}, capo {track.Capo}, transpose {track.Transpose}, volume {track.Volume}, pan {track.Pan}{(track.Mute ? ", muted" : "")}{(track.Solo ? ", solo" : "")}");
            for (var b = 0; b < track.Measures.Count; b++)
            {
                var m = track.Measures[b]; var master = MusicTime.BarOf(p, b);
                var head = new List<string> { $"{master?.TimeSigNum ?? p.TimeSignatureNumerator}/{master?.TimeSigDenom ?? p.TimeSignatureDenominator}" };
                if (t == 0)
                {
                    head.Add($"tempo {MusicTime.TempoAt(p, b)}");
                    if (m.KeySignature is { } k) head.Add($"key {k}{(m.KeySignatureMinor == true ? "m" : "")}");
                    if (m.RepeatStart) head.Add("repeat start"); if (m.RepeatEnd) head.Add($"repeat end x{Math.Max(2, m.RepeatCount)}"); if (m.EndingLabel.Length > 0) head.Add($"ending {m.EndingLabel}");
                    if (m.Directions.Length > 0) head.Add("marks " + m.Directions); if (m.IsDoubleBar) head.Add("double bar"); if (m.Anacrusis) head.Add("pickup");
                    if (p.Markers.FirstOrDefault(x => x.MeasureIndex == b) is { } mark) head.Add($"section \"{mark.Title}\"");
                    if (m.MidBarTempos is { Count: > 0 } pts) head.Add("mid-bar tempo " + string.Join(" ", pts.Select(x => $"@{RtF(x.Slot)}->{x.Tempo}{(x.RampSlots > 0 ? "~" + RtF(x.RampSlots) : "")}")));
                }
                sb.AppendLine($"- bar {b + 1} [{string.Join(", ", head)}]");
                foreach (var (voiceCells, v) in new[] { (m.Cells, 1), (m.Voice2Cells, 2) })
                {
                    if (v == 2 && !voiceCells.Any(c => c.Notes.Count > 0)) continue;
                    foreach (var (cell, onset) in RtBeats(voiceCells))
                    {
                        var notes = cell.IsRest && cell.Notes.Count == 0 ? "rest" : string.Join(" + ", cell.Notes.Select(n => (n.IsGraceNote ? "grace " : "") + (RtIsDrums(track) ? "drum " + RtDrumMidi(n) : $"s{n.StringIndex + 1}f{n.Fret}=" + n.MidiValue) + (n.Velocity != 95 ? $" v{n.Velocity}" : "") + (n.Tied ? " tied" : "") + (n.Ghost ? " ghost" : "") + (n.Dead ? " dead" : "")
                            + (n.LeftHandFinger is { } lf ? $" lh{lf}" : "") + (n.BendPoints.Count > 0 ? " bend " + GfBend(n.BendPoints) : "") + (n.Techniques.Count > 0 ? " [" + GfTech(n) + "]" : "")));
                        var extra = (cell.Tenuto ? " tenuto" : "") + (cell.Fermata ? " fermata" : "") + (cell.Accent > 0 ? $" accent{cell.Accent}" : "") + (cell.WhammyPoints.Count > 0 ? " whammy " + GfBend(cell.WhammyPoints) : "");
                        sb.AppendLine($"  - t{t + 1} v{v} @{RtF(onset)} {cell.DurationDenominator}{new string('.', cell.Dots)}{(cell.Tuplet.Numerator > 0 ? $" {cell.Tuplet.Numerator}:{cell.Tuplet.Denominator}" : "")}: {notes}{extra}");
                    }
                }
            }
        }
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>`TabForge.exe --write-gp-fixtures &lt;dir&gt;`: clean .gp per fixture (each checked clean in place), the four file modes of one fixture, and MANIFEST.md.</summary>
    internal static int RunWriteGpFixtures(string dir)
    {
        Directory.CreateDirectory(dir);
        var manifest = new StringBuilder();
        manifest.AppendLine("# Guitar Pro fidelity fixtures (R5)");
        manifest.AppendLine();
        manifest.AppendLine("Original songs written in code (`SelfTestGpFidelityFixtures.cs`), exported by `TabForge.exe --write-gp-fixtures`. Every `.gp` here is CLEAN: no embedded TabForge project, no `.tfaudio`.");
        manifest.AppendLine("Open each in Guitar Pro 8, compare bar by bar with the facts below, save it from Guitar Pro and reopen it in TabForge. Slot = a sixteenth note; `@n` is the onset in slots from the bar start; `sNfM=P` is string N, fret M, sounding MIDI pitch P.");
        manifest.AppendLine();
        var failures = 0;
        foreach (var f in GfFixtures())
        {
            var path = Path.Combine(dir, f.Id + ".gp");
            try
            {
                GuitarProExporter.Save(f.Song, path, embedProject: false);
                var clean = GuitarProExporter.TryReadEmbedded(path) is null && !File.Exists(AudioDataFile.PathFor(path));
                if (!clean) { failures++; Console.Error.WriteLine($"not clean: {f.Id}"); }
                manifest.AppendLine(GfManifestText(f));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { failures++; Console.Error.WriteLine($"{f.Id}: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}"); }
        }
        var modes = Path.Combine(dir, "modes"); Directory.CreateDirectory(modes);
        var modeSong = GfFixtures().First(f => f.Id.StartsWith("05", StringComparison.Ordinal)).Song;
        GuitarProExporter.Save(modeSong, Path.Combine(modes, "mode-pure-clean.gp"), embedProject: false);
        GuitarProExporter.Save(modeSong, Path.Combine(modes, "mode-embedded.gp"), embedProject: true);
        new DocumentController().SaveCleanGuitarProWithAudioData(DocumentSession.FromProject(RtCopy(modeSong), null), Path.Combine(modes, "mode-clean-plus-sidecar.gp"), "");
        ProjectService.Save(Path.Combine(modes, "mode-native.tforge"), modeSong);
        manifest.AppendLine("### modes/");
        manifest.AppendLine();
        manifest.AppendLine("The song of fixture 05 in the four file modes: `mode-pure-clean.gp` (nothing of TabForge), `mode-embedded.gp` (full project inside), `mode-clean-plus-sidecar.gp` + `.tfaudio`, `mode-native.tforge`. Each test reads only the data of its own mode.");
        File.WriteAllText(Path.Combine(dir, "MANIFEST.md"), manifest.ToString(), new UTF8Encoding(false));
        Console.Out.WriteLine($"Wrote {GfFixtures().Count} fixtures and MANIFEST.md to {dir}" + (failures > 0 ? $"; {failures} NOT CLEAN" : ""));
        return failures == 0 ? 0 : 1;
    }

    // ---- family: the fixture set, checked exactly ----
    // REGION:fidelity-fixtures
    /// <summary>Per voice: every explicit rest of the original must reopen as a rest at the same onset and length.</summary>
    private static List<string> GfRestFacts(SongProject p, int track, int bar) =>
        new[] { p.Tracks[track].Measures[bar].Cells, p.Tracks[track].Measures[bar].Voice2Cells }.SelectMany((cells, v) => RtBeats(cells).Where(x => x.Cell.IsRest && x.Cell.Notes.Count == 0).Select(x => $"v{v}@{RtF(x.Onset)}:{RtF(MusicTime.CellSlots(x.Cell))}")).ToList();

    private static void GpFidelityFixtureSet(string folder)
    {
        var fresh = Path.Combine(folder, "fixtures"); Directory.CreateDirectory(fresh);
        foreach (var f in GfFixtures())
        {
            var original = RtCopy(f.Song);
            var back = GfClean(f.Song, fresh, f.Id);
            // the pitches written by hand, in the fixture and in the reopened file (through playback's own compile, which every reader of the file shares)
            foreach (var (where, midi) in f.Anchors)
                Check($"fixture {f.Id}: hand-specified pitch {midi} ({where}) sounds in the model and in the reopened file", GfSoundingPitches(f.Song).Contains(midi) && GfSoundingPitches(back).Contains(midi), $"{string.Join(",", GfSoundingPitches(back).Distinct().Take(12))}");
            var profile = RtGpClean();
            var ef = RtScoreFacts(RtBakedTranspose(original), out var n1, out _); var af = RtScoreFacts(back, out var n2, out _);
            var unexpected = RtCompare(ef, af).Where(d => profile.ReasonFor(d.Category, d) is null).ToList();
            Check($"fixture {f.Id}: every score fact (pitch, onset, duration, voice, tuning, capo, ties, bars, signatures, tempo, repeats, jumps) reopens exactly; only the named allowances differ ({n1} notes)", unexpected.Count == 0,
                string.Join(" | ", unexpected.GroupBy(d => d.Category).Take(4).Select(g => $"{g.Key} x{g.Count()} e.g. {g.First().Where} '{g.First().Expected}' -> '{g.First().Actual}'")));
            var restDiff = new List<string>();
            for (var t = 0; t < original.Tracks.Count; t++)
                for (var b = 0; b < original.Tracks[t].Measures.Count; b++)
                {
                    var want = GfRestFacts(original, t, b); var got = GfRestFacts(back, t, b);
                    foreach (var r in want) if (!got.Contains(r)) restDiff.Add($"t{t + 1} bar {b + 1} {r}");
                }
            Check($"fixture {f.Id}: every explicit rest reopens at the same onset and length", restDiff.Count == 0, string.Join("; ", restDiff.Take(5)));
        }
        var modes = Path.Combine(folder, "modes"); Directory.CreateDirectory(modes);
        var song = GfFixtures().First(f => f.Id.StartsWith("05", StringComparison.Ordinal)).Song;
        song.Tracks[0].Performer = "native only"; song.Tracks[0].Reverb = 77;
        var embeddedPath = Path.Combine(modes, "e.gp"); GuitarProExporter.Save(song, embeddedPath, embedProject: true);
        var tforgePath = Path.Combine(modes, "n.tforge"); ProjectService.Save(tforgePath, song);
        Check("modes: the embedded .gp restores TabForge-only data (native claim)", GuitarProImporter.Import(embeddedPath).Tracks[0].Reverb == 77);
        Check("modes: the .tforge restores TabForge-only data (native claim)", ProjectService.Load(tforgePath).Tracks[0].Performer == "native only");
        var cleanPath = Path.Combine(modes, "c.gp"); GuitarProExporter.Save(song, cleanPath, embedProject: false);
        var cleanBack = GuitarProImporter.Import(cleanPath);
        Check("modes: a clean .gp has none of it, and the test did not read a sidecar or an embedded copy (clean-score claim)", cleanBack.Tracks[0].Reverb == 24 && cleanBack.Tracks[0].Performer == "" && !File.Exists(AudioDataFile.PathFor(cleanPath)));
        var pairPath = Path.Combine(modes, "p.gp"); new DocumentController().SaveCleanGuitarProWithAudioData(DocumentSession.FromProject(RtCopy(song), null), pairPath, "");
        Check("modes: the sidecar restores the audio data it stores but not notation or the fields it does not hold (sidecar claim)", new DocumentController().Open(pairPath).Project.Tracks[0].Performer == "" && File.Exists(AudioDataFile.PathFor(pairPath)));
    }

    private static List<int> GfSoundingPitches(SongProject p) => RtNoteSig(RenderSpecBuilder.Compile(p)).Select(n => n.Midi).Distinct().ToList();
    // ENDREGION
}
