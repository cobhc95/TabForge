using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Rendering;
using TabForge.Services;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

// Verifies articulation MIDI export + MIDI import round trip on one real technique-rich song.
// Files: C:/tfrel-tmp/midi-verify. Read-only input under samples/.
public sealed class MidiArticulationVerifyTests : IDisposable
{
    private static string OutDir => "C:/tfrel-tmp/midi-verify";
    private readonly ITestOutputHelper _out;
    private readonly string _scratch;

    public MidiArticulationVerifyTests(ITestOutputHelper output)
    {
        _out = output;
        _scratch = Path.Combine(Path.GetTempPath(), "tf-midi-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratch);
        Directory.CreateDirectory(OutDir);
        AlphaTabWarmUp.EnsureReady();
    }

    public void Dispose()
    {
        try { Directory.Delete(_scratch, true); } catch (IOException) { }
    }

    internal static readonly string[] Candidates = new[]
    {
        "samples/metallica/metallica/enter_sandman_9.gp5",
        "samples/metallica/metallica/kirk_solos.gp5",
        "samples/metallica/metallica/master_of_puppets_9.gpx",
        "samples/children_of_bodom/all_for_nothing.gp5",
    };

    internal static string BestSong()
    {
        string? best = null;
        var bestDistinct = -1;
        var bestNotes = -1;
        foreach (var c in Candidates)
        {
            if (!File.Exists(c)) continue;
            var song = GuitarProImporter.Import(c);
            var counts = TechniqueCounts(song);
            var notes = song.Tracks.Sum(t => t.Measures.Sum(m => m.Cells.Sum(x => x.Notes.Count)));
            if (counts.Count > bestDistinct || (counts.Count == bestDistinct && notes > bestNotes))
            {
                best = c;
                bestDistinct = counts.Count;
                bestNotes = notes;
            }
        }
        return best ?? throw new FileNotFoundException("No candidate sample found.");
    }

    internal static string PickSong() => BestSong();

    internal static Dictionary<string, int> TechniqueCounts(SongProject song)
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in song.Tracks)
            foreach (var m in t.Measures)
                foreach (var c in m.Cells.Concat(m.Voice2Cells ?? Enumerable.Empty<TabCell>()))
                    foreach (var n in c.Notes)
                        foreach (var tech in n.Techniques)
                        {
                            d.TryGetValue(tech, out var v);
                            d[tech] = v + 1;
                        }
        return d;
    }

    // ---------- minimal SMF reader ----------
    internal sealed record SMFNote(int Pitch, int Start, int End, int Channel, int Velocity);
    internal sealed record SMFBend(int Tick, int Channel, int Value);
    internal sealed record SMFCc(int Tick, int Channel, int Controller, int Value);
    internal sealed record SMFData(int Division, List<string> TrackNames, List<SMFNote> Notes, List<SMFBend> Bends, List<SMFCc> Ccs, List<(int Tick, int Mpq)> Tempos, List<(int Tick, int Num, int Den)> Sigs, List<(int Tick, int Channel, int Program)> Programs, int TrackChunks);

    internal static int ReadBe16(byte[] b, int at) => (b[at] << 8) | b[at + 1];
    internal static int ReadBe32(byte[] b, int at) => (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];
    internal static int ReadVarLen(byte[] b, ref int p)
    {
        var v = 0;
        for (var i = 0; i < 4; i++) { var x = b[p++]; v = (v << 7) | (x & 0x7F); if ((x & 0x80) == 0) return v; }
        return v;
    }

    internal static SMFData ParseMidi(byte[] b)
    {
        Assert.True(b.Length > 14 && b[0] == (byte)'M' && b[1] == (byte)'T' && b[2] == (byte)'h' && b[3] == (byte)'d');
        var trackCount = ReadBe16(b, 10);
        var division = ReadBe16(b, 12);
        var names = new List<string>();
        var notes = new List<SMFNote>();
        var bends = new List<SMFBend>();
        var ccs = new List<SMFCc>();
        var tempos = new List<(int, int)>();
        var sigs = new List<(int, int, int)>();
        var programs = new List<(int, int, int)>();
        var pending = new Dictionary<(int, int), Queue<(int Start, int Vel)>>();
        var p = 14;
        for (var chunk = 0; chunk < trackCount; chunk++)
        {
            var length = ReadBe32(b, p + 4);
            var end = p + 8 + length;
            var q = p + 8;
            var tick = 0;
            var name = "";
            var running = -1;
            while (q < end)
            {
                tick += ReadVarLen(b, ref q);
                var s = b[q];
                if (s == 0xFF)
                {
                    q++;
                    var type = b[q++];
                    var len = ReadVarLen(b, ref q);
                    if (type == 0x51 && len == 3) tempos.Add((tick, (b[q] << 16) | (b[q + 1] << 8) | b[q + 2]));
                    else if (type == 0x58 && len >= 2) sigs.Add((tick, b[q], 1 << b[q + 1]));
                    else if (type == 0x03 && len > 0) name += System.Text.Encoding.Latin1.GetString(b, q, len);
                    q += len;
                    running = -1;
                    continue;
                }
                if (s == 0xF0 || s == 0xF7) { q++; var len = ReadVarLen(b, ref q); q += len; running = -1; continue; }
                int status;
                int d1;
                if (s >= 0x80) { status = s; q++; d1 = b[q++]; }
                else { if (running < 0) throw new InvalidDataException("bad running status"); status = running; d1 = s; q++; }
                running = status;
                var kind = status & 0xF0;
                var ch = status & 0x0F;
                if (kind == 0xC0) { programs.Add((tick, ch, d1)); continue; }
                if (kind == 0xD0) { q++; continue; }
                var d2 = b[q++];
                if (kind == 0x90 && d2 > 0)
                {
                    var key = (ch, d1);
                    if (!pending.TryGetValue(key, out var qu)) pending[key] = qu = new Queue<(int, int)>();
                    qu.Enqueue((tick, d2));
                }
                else if (kind == 0x80 || kind == 0x90)
                {
                    var key = (ch, d1);
                    if (pending.TryGetValue(key, out var qu) && qu.Count > 0)
                    {
                        var (st, vel) = qu.Dequeue();
                        notes.Add(new SMFNote(d1, st, tick, ch, vel));
                    }
                }
                else if (kind == 0xE0)
                {
                    bends.Add(new SMFBend(tick, ch, (d2 << 7) | d1));
                }
                else if (kind == 0xB0)
                {
                    ccs.Add(new SMFCc(tick, ch, d1, d2));
                }
            }
            names.Add(name);
            p = end;
        }
        return new SMFData(division, names, notes, bends, ccs, tempos, sigs, programs, trackCount);
    }

    internal static ArticulationMap CustomMap()
    {
        var map = new ArticulationMap();
        map.Rules[TechniqueNames.PalmMute] = new ArticulationRule(ArticulationAction.Keyswitch, 20, 0, ArticulationMode.Latched);
        map.Rules[TechniqueNames.LegatoSlide] = new ArticulationRule(ArticulationAction.Keyswitch, 21, 0, ArticulationMode.PerNote);
        map.Rules[TechniqueNames.Harmonic] = new ArticulationRule(ArticulationAction.Keyswitch, 22, 0, ArticulationMode.PerNote);
        map.Rules["Slide"] = new ArticulationRule(ArticulationAction.Keyswitch, 23, 0, ArticulationMode.PerNote);
        map.Rules[ArticulationTechniques.Normal] = new ArticulationRule(ArticulationAction.Keyswitch, 24, 0, ArticulationMode.Latched);
        return map;
    }

    internal static ArticulationExportOptions BuiltInOptions(SongProject song)
    {
        var preset = ArticulationPresets.FindBuiltIn(ArticulationPresets.AmpleName) ?? ArticulationPresets.BuiltIn[0];
        var options = new ArticulationExportOptions();
        for (var i = 0; i < song.Tracks.Count; i++)
            if (!song.Tracks[i].IsAudio) options.Overrides[i] = preset.ToMap();
        return options;
    }

    internal static ArticulationExportOptions CustomOptions(SongProject song, bool skip)
    {
        var options = new ArticulationExportOptions { SkipUnmapped = skip };
        var map = CustomMap();
        for (var i = 0; i < song.Tracks.Count; i++)
            if (!song.Tracks[i].IsAudio) options.Overrides[i] = map;
        return options;
    }

    internal static string ExportWhole(SongProject song, string path, ArticulationExportOptions? options)
    {
        var (_, slice) = MidiExportRange.Resolve(song, new MidiExportSettings(), null, null);
        MidiExportService.Export(song, path, slice, options);
        return path;
    }

    private void Log(string s) { _out.WriteLine(s); }

    [Fact]
    public void MidiArticulationVerify_ScanCandidates()
    {
        var rows = new List<(string File, int Tracks, int Notes, int Distinct, string Top, string All)>();
        foreach (var f in Candidates)
        {
            if (!File.Exists(f)) { rows.Add((f, -1, -1, -1, "missing", "missing")); continue; }
            var song = GuitarProImporter.Import(f);
            var counts = TechniqueCounts(song);
            var notes = song.Tracks.Sum(t => t.Measures.Sum(m => m.Cells.Sum(c => c.Notes.Count)));
            var top = string.Join(",", counts.OrderByDescending(kv => kv.Value).Take(8).Select(kv => $"{kv.Key}={kv.Value}"));
            var all = string.Join(",", counts.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}"));
            rows.Add((Path.GetFileName(f), song.Tracks.Count, notes, counts.Count, top, all));
            Log($"CANDIDATE {Path.GetFileName(f)} tracks={song.Tracks.Count} notes={notes} distinctTech={counts.Count} :: {all}");
        }
        var best = rows.Where(r => r.Distinct >= 0).OrderByDescending(r => r.Distinct).ThenByDescending(r => r.Notes).First();
        Log($"SELECTED {best.File}");
        File.WriteAllText(Path.Combine(OutDir, "selection.txt"), best.File + "\n" + string.Join("\n", rows.Select(r => $"{r.File} tracks={r.Tracks} notes={r.Notes} distinct={r.Distinct} {r.All}")));
        Assert.True(best.Distinct >= 4, "Need a technique-rich song, got: " + best.Top);
    }

    [Fact]
    public void MidiArticulationVerify_ExportA_ByteIdentical()
    {
        var song = GuitarProImporter.Import(PickSong());
        var oldPath = Path.Combine(OutDir, "verify_a_default.mid");
        var newPath = Path.Combine(_scratch, "verify_a_slice.mid");
        MidiExportService.Export(song, oldPath);
        ExportWhole(song, newPath, null);
        var a = File.ReadAllBytes(oldPath);
        var b = File.ReadAllBytes(newPath);
        Log($"EXPORT_A bytes old={a.Length} new={b.Length} identical={a.SequenceEqual(b)}");
        Assert.Equal(a, b);
        var midi = ParseMidi(a);
        Assert.Equal(480, midi.Division);
        Assert.NotEmpty(midi.Notes);
        Log($"EXPORT_A notes={midi.Notes.Count} bends={midi.Bends.Count} ccs={midi.Ccs.Count} tempos={midi.Tempos.Count} sigs={midi.Sigs.Count} programs={midi.Programs.Count} chunks={midi.TrackChunks}");
    }

    [Fact]
    public void MidiArticulationVerify_ExportB_AmplePreset()
    {
        var song = GuitarProImporter.Import(PickSong());
        var counts = TechniqueCounts(song);
        Log("SOURCE_TECHNIQUES " + string.Join(",", counts.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}")));
        Log("GM_FLAGS " + string.Join(",", song.Tracks.Select((t, i) => $"{i}:{t.Name}:MidiSound={t.MidiSound}:src={t.SoundSource}")));
        var options = BuiltInOptions(song);
        var path = Path.Combine(OutDir, "verify_b_ample.mid");
        ExportWhole(song, path, options);
        var midi = ParseMidi(File.ReadAllBytes(path));
        Log($"AMPLE mapped={options.Mapped} kept={options.Kept} skipped={options.Skipped} notes={midi.Notes.Count} bends={midi.Bends.Count}");
        var ksPitches = new[] { 24, 25, 26, 27, 28, 29 };
        // Real bass notes can sit on 28/29 (open low strings), so keyswitches are identified by the
        // compiler's signature: velocity 100 and a ~lead-length duration (real notes are longer/louder).
        var ksNotes = midi.Notes.Where(n => ksPitches.Contains(n.Pitch) && n.Velocity == 100 && n.End - n.Start <= 50).ToList();
        Log($"AMPLE keyswitchNotes={ksNotes.Count} byPitch=" + string.Join(",", ksPitches.Select(p => $"{p}:{ksNotes.Count(n => n.Pitch == p)}")));
        var plainPath = Path.Combine(_scratch, "plain.mid");
        MidiExportService.Export(song, plainPath);
        var plain = ParseMidi(File.ReadAllBytes(plainPath));
        bool WheelMoves(List<SMFBend> bends) => bends.Any(e => Math.Abs(e.Value - 8192) > 200);
        Log($"WHEEL plainMoves={WheelMoves(plain.Bends)} ampleMoves={WheelMoves(midi.Bends)} plainBends={plain.Bends.Count} ampleBends={midi.Bends.Count}");
        // The export explicitly picked the Ample preset for every track, so built-in-sound tracks use it too.
        Assert.True(options.Mapped > 0, "Ample preset should map something on this song");
        Assert.NotEmpty(ksNotes);
        if (counts.ContainsKey(TechniqueNames.Bend))
            Assert.True(midi.Bends.Count <= plain.Bends.Count, $"mapped bend should reduce wheel events ({plain.Bends.Count} -> {midi.Bends.Count})");
    }

    [Fact]
    public void MidiArticulationVerify_ExportB_CustomMap()
    {
        var song = GuitarProImporter.Import(PickSong());
        var counts = TechniqueCounts(song);
        var options = CustomOptions(song, skip: false);
        var path = Path.Combine(OutDir, "verify_b_custom.mid");
        ExportWhole(song, path, options);
        var midi = ParseMidi(File.ReadAllBytes(path));
        Log($"CUSTOM_GM mapped={options.Mapped} kept={options.Kept} skipped={options.Skipped} notes={midi.Notes.Count}");
        var ksNotes = midi.Notes.Where(n => n.Pitch is 20 or 21 or 22 or 23 or 24 && n.Velocity == 100 && n.End - n.Start <= 50).ToList();
        Log("CUSTOM_GM keyswitch byPitch=" + string.Join(",", new[] { 20, 21, 22, 23, 24 }.Select(p => $"{p}:{ksNotes.Count(n => n.Pitch == p)}")));
        Log("GM_FLAGS " + string.Join(",", song.Tracks.Select((t, i) => $"{i}:{t.Name}:MidiSound={t.MidiSound}")));
        // The export explicitly picked the custom map, so built-in-sound tracks write its keyswitches too.
        Assert.NotEmpty(ksNotes);

        // Same custom map on a plug-in copy (MidiSound=false) proves the keyswitch mechanics on the real material.
        var copy = GuitarProImporter.Import(PickSong());
        foreach (var t in copy.Tracks)
        {
            if (t.IsAudio) continue;
            t.MidiSound = false;
            t.SoundSource = TabForge.Models.SoundSources.Plugins;
            t.Rig.Plugins.Add(new TabForge.Plugins.PluginSlot { Name = "Guitar", Type = TabForge.Plugins.PluginSlotType.Instrument });
        }
        var options2 = CustomOptions(copy, skip: false);
        var path2 = Path.Combine(OutDir, "verify_b_custom_plugin.mid");
        ExportWhole(copy, path2, options2);
        var midi2 = ParseMidi(File.ReadAllBytes(path2));
        var ks2 = midi2.Notes.Where(n => n.Pitch is 20 or 21 or 22 or 23 or 24).ToList();
        Log($"CUSTOM_PLUGIN mapped={options2.Mapped} kept={options2.Kept} skipped={options2.Skipped} notes={midi2.Notes.Count}");
        Log("CUSTOM_PLUGIN keyswitch byPitch=" + string.Join(",", new[] { 20, 21, 22, 23, 24 }.Select(p => $"{p}:{ks2.Count(n => n.Pitch == p)}")));
        if (counts.ContainsKey(TechniqueNames.PalmMute)) Assert.Contains(ks2, n => n.Pitch == 20);
        if (counts.ContainsKey(TechniqueNames.LegatoSlide)) Assert.Contains(ks2, n => n.Pitch == 21);
        if (counts.ContainsKey(TechniqueNames.Harmonic)) Assert.Contains(ks2, n => n.Pitch == 22);
        Assert.NotEmpty(ks2);
        var realStarts = midi2.Notes.Where(n => n.Pitch > 30).Select(n => n.Start).OrderBy(t => t).ToList();
        var precedes = ks2.Any(k => realStarts.Any(r => r >= k.Start && r - k.Start <= 100));
        Log($"CUSTOM_PLUGIN keyswitchPrecedesNote={precedes}");
        Assert.True(precedes, "keyswitch should be at/just before its note");
        var plain = ParseMidi(File.ReadAllBytes(Path.Combine(OutDir, "verify_a_default.mid")));
        Log($"LEN plainNotes={plain.Notes.Count} customPluginNotes={midi2.Notes.Count} (custom adds keyswitch attacks)");
        Assert.True(midi2.Notes.Count >= plain.Notes.Count, "custom map adds keyswitch notes so count should grow");
    }

    [Fact]
    public void MidiArticulationVerify_ExportB_KeepVsSkip()
    {
        var song = GuitarProImporter.Import(PickSong());
        foreach (var t in song.Tracks)
        {
            if (t.IsAudio) continue;
            t.MidiSound = false;
            t.SoundSource = TabForge.Models.SoundSources.Plugins;
            t.Rig.Plugins.Add(new TabForge.Plugins.PluginSlot { Name = "Guitar", Type = TabForge.Plugins.PluginSlotType.Instrument });
        }
        var keep = CustomOptions(song, skip: false);
        var skip = CustomOptions(song, skip: true);
        var keepPath = Path.Combine(OutDir, "verify_b_keep.mid");
        var skipPath = Path.Combine(OutDir, "verify_b_skip.mid");
        ExportWhole(song, keepPath, keep);
        ExportWhole(song, skipPath, skip);
        var keepMidi = ParseMidi(File.ReadAllBytes(keepPath));
        var skipMidi = ParseMidi(File.ReadAllBytes(skipPath));
        Log($"KEEP mapped={keep.Mapped} kept={keep.Kept} skipped={keep.Skipped} bends={keepMidi.Bends.Count} notes={keepMidi.Notes.Count}");
        Log($"SKIP mapped={skip.Mapped} kept={skip.Kept} skipped={skip.Skipped} bends={skipMidi.Bends.Count} notes={skipMidi.Notes.Count}");
        Assert.Equal(0, keep.Skipped);
        Assert.True(skip.Skipped > 0, "skip should record skipped techniques");
        Assert.True(skipMidi.Bends.Count <= keepMidi.Bends.Count, "skip must not add wheel events");
    }

    [Fact]
    public void MidiArticulationVerify_Import_AsPlayed()
    {
        var src = GuitarProImporter.Import(PickSong());
        var midPath = Path.Combine(OutDir, "verify_a_default.mid");
        if (!File.Exists(midPath)) ExportWhole(src, midPath, null);
        var back = GuitarProImporter.Import(midPath);
        var srcCells = src.Tracks.SelectMany(t => t.Measures).SelectMany(m => m.Cells.Concat(m.Voice2Cells ?? new List<TabCell>())).ToList();
        var backCells = back.Tracks.SelectMany(t => t.Measures).SelectMany(m => m.Cells.Concat(m.Voice2Cells ?? new List<TabCell>())).ToList();
        var srcNotes = srcCells.Sum(c => c.Notes.Count);
        var backNotes = backCells.Sum(c => c.Notes.Count);
        var srcSet = srcCells.SelectMany(c => c.Notes).Select(n => n.MidiValue).ToHashSet();
        var backSet = backCells.SelectMany(c => c.Notes).Select(n => n.MidiValue).ToHashSet();
        var recall = srcSet.Count == 0 ? 1.0 : (double)srcSet.Intersect(backSet).Count() / srcSet.Count;
        var srcVel = srcCells.SelectMany(c => c.Notes).Select(n => n.Velocity).DefaultIfEmpty(0).Average();
        var backVel = backCells.SelectMany(c => c.Notes).Select(n => n.Velocity).DefaultIfEmpty(0).Average();
        Log($"IMPORT_A srcTracks={src.Tracks.Count} backTracks={back.Tracks.Count} srcNotes={srcNotes} backNotes={backNotes} pitchDistinctRecall={recall:F2} srcDistinct={srcSet.Count} backDistinct={backSet.Count}");
        Log($"IMPORT_A tempo src={src.Tempo} back={back.Tempo} meter src={src.TimeSignatureNumerator}/{src.TimeSignatureDenominator} back={back.TimeSignatureNumerator}/{back.TimeSignatureDenominator}");
        Log($"IMPORT_A srcNames=[{string.Join(";", src.Tracks.Select(t => t.Name))}] backNames=[{string.Join(";", back.Tracks.Select(t => t.Name))}]");
        Log($"IMPORT_A programs src=[{string.Join(",", src.Tracks.Select(t => t.MidiProgram))}] back=[{string.Join(",", back.Tracks.Select(t => t.MidiProgram))}] velAvg src={srcVel:F1} back={backVel:F1}");
        var midi = ParseMidi(File.ReadAllBytes(midPath));
        Log($"IMPORT_A midiFile ccs={midi.Ccs.Count} bends={midi.Bends.Count} programs={midi.Programs.Count} (import keeps programs/velocity/tempo/meter; drops CC + pitch-bend into quantised pitches)");
        var srcTech = TechniqueCounts(src);
        var backTech = TechniqueCounts(back);
        Log("IMPORT_A techniquesLost=" + string.Join(",", srcTech.Keys.Where(k => !backTech.ContainsKey(k))));
        Log("IMPORT_A techniquesBack=" + string.Join(",", backTech.Select(kv => $"{kv.Key}={kv.Value}")));
        Assert.Equal(src.Tracks.Count, back.Tracks.Count);
        Assert.Equal(src.Tempo, back.Tempo);
        Assert.Equal(src.TimeSignatureNumerator, back.TimeSignatureNumerator);
        Assert.Equal(src.TimeSignatureDenominator, back.TimeSignatureDenominator);
        Assert.True(recall >= 0.8, $"distinct pitch recall too low: {recall:F2}");
        Assert.InRange(backNotes, (int)(srcNotes * 0.5), (int)(srcNotes * 2.0));
        Assert.True(backTech.Count < srcTech.Count, "techniques as marks are expected lost (only Tie may survive)");
    }

    [Fact]
    public void MidiArticulationVerify_Import_WithMap_Keyswitches()
    {
        var src = GuitarProImporter.Import(PickSong());
        var mapPath = Path.Combine(OutDir, "verify_b_custom_plugin.mid");
        if (!File.Exists(mapPath)) ExportWhole(src, mapPath, CustomOptions(src, false));
        var back = GuitarProImporter.Import(mapPath);
        var midi = ParseMidi(File.ReadAllBytes(mapPath));
        var ksCount = midi.Notes.Count(n => n.Pitch is 20 or 21 or 22 or 23 or 24);
        var context = new ImportContext();
        var direct = MidiToSong.Convert(SmfReader.Read(File.ReadAllBytes(mapPath), context), mapPath, context);
        var backLow = direct.Tracks.SelectMany(t => t.Measures).SelectMany(m => m.Cells.Concat(m.Voice2Cells ?? new List<TabCell>())).SelectMany(c => c.Notes).Count(n => n.MidiValue is 20 or 21 or 22 or 23 or 24);
        var backBends = back.Tracks.SelectMany(t => t.Measures).SelectMany(m => m.Cells).SelectMany(c => c.Notes).Count(n => n.Techniques.Contains(TechniqueNames.Bend));
        Log($"IMPORT_B keyswitchNotesInMidi={ksCount} lowNotesAfterImport={backLow} keyswitchMoved={context.KeyswitchNotesMoved} notice={context.KeyswitchNotice} bendMarksAfterImport={backBends} backTracks={back.Tracks.Count}");
        // Keyswitch notes are articulation switches, not music: the import leaves them out and reports how many.
        var gmPath = Path.Combine(OutDir, "verify_b_custom.mid");
        var gm = ParseMidi(File.ReadAllBytes(gmPath));
        var gmKs = gm.Notes.Count(n => n.Pitch is 20 or 21 or 22 or 23 or 24);
        Log($"IMPORT_B_GM keyswitchNotesInMidi={gmKs} (the export picked the map for the built-in track too)");
        if (ksCount > 0)
        {
            Assert.True(context.KeyswitchNotesMoved > 0, "keyswitch notes should be reported as left out");
            Assert.True(backLow < ksCount, $"keyswitch notes should not survive as low notes ({backLow} of {ksCount})");
            Assert.NotNull(context.KeyswitchNotice);
        }
        else Assert.Equal(0, backLow);
    }
}
