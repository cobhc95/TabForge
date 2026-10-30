using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Rendering;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// One-shot table exporter for the midi-verify resume job: writes
// C:/tfrel-tmp/midi-verify/tables_export.md and tables_import.md via
// File.WriteAllText (not stdout). Fixed song = already-chosen
// master_of_puppets_9.gpx, fallback enter_sandman_9.gp5.
public sealed class MidiVerifyTablesTests
{
    private static string OutDir => "C:/tfrel-tmp/midi-verify";
    private static readonly string Song =
        "samples/metallica/metallica/master_of_puppets_9.gpx";
    private static readonly string Fallback =
        "samples/metallica/metallica/enter_sandman_9.gp5";

    [Fact]
    public void MidiVerify_WriteTables()
    {
        Directory.CreateDirectory(OutDir);
        AlphaTabWarmUp.EnsureReady();
        var songPath = File.Exists(Song) ? Song : Fallback;
        var src = GuitarProImporter.Import(songPath);
        var counts = MidiArticulationVerifyTests.TechniqueCounts(src);
        var srcNotes = src.Tracks.Sum(t => t.Measures.Sum(m => m.Cells.Sum(c => c.Notes.Count)));

        var aPath = Path.Combine(OutDir, "verify_a_default.mid");
        MidiExportService.Export(src, aPath);
        var a = MidiArticulationVerifyTests.ParseMidi(File.ReadAllBytes(aPath));

        var ampleOpts = MidiArticulationVerifyTests.BuiltInOptions(src);
        var amplePath = Path.Combine(OutDir, "verify_b_ample.mid");
        MidiArticulationVerifyTests.ExportWhole(src, amplePath, ampleOpts);
        var ample = MidiArticulationVerifyTests.ParseMidi(File.ReadAllBytes(amplePath));

        var customOpts = MidiArticulationVerifyTests.CustomOptions(src, skip: false);
        var customPath = Path.Combine(OutDir, "verify_b_custom.mid");
        MidiArticulationVerifyTests.ExportWhole(src, customPath, customOpts);
        var custom = MidiArticulationVerifyTests.ParseMidi(File.ReadAllBytes(customPath));

        var copy = GuitarProImporter.Import(songPath);
        foreach (var t in copy.Tracks)
        {
            if (t.IsAudio) continue;
            t.MidiSound = false;
            t.SoundSource = SoundSources.Plugins;
        }
        var plugOpts = MidiArticulationVerifyTests.CustomOptions(copy, skip: false);
        var plugPath = Path.Combine(OutDir, "verify_b_custom_plugin.mid");
        MidiArticulationVerifyTests.ExportWhole(copy, plugPath, plugOpts);
        var plug = MidiArticulationVerifyTests.ParseMidi(File.ReadAllBytes(plugPath));

        var keepOpts = MidiArticulationVerifyTests.CustomOptions(src, skip: false);
        var skipOpts = MidiArticulationVerifyTests.CustomOptions(src, skip: true);
        var keepPath = Path.Combine(OutDir, "verify_b_keep.mid");
        var skipPath = Path.Combine(OutDir, "verify_b_skip.mid");
        MidiArticulationVerifyTests.ExportWhole(src, keepPath, keepOpts);
        MidiArticulationVerifyTests.ExportWhole(src, skipPath, skipOpts);
        var keep = MidiArticulationVerifyTests.ParseMidi(File.ReadAllBytes(keepPath));
        var skip = MidiArticulationVerifyTests.ParseMidi(File.ReadAllBytes(skipPath));

        bool WheelMoves(List<MidiArticulationVerifyTests.SMFBend> bends) =>
            bends.Any(e => Math.Abs(e.Value - 8192) > 200);
        var ksAmple = ample.Notes.Where(n => n.Pitch >= 24 && n.Pitch <= 29 && n.Velocity == 100 && n.End - n.Start <= 50).ToList();
        var ksPlug = plug.Notes.Where(n => n.Pitch >= 20 && n.Pitch <= 24).ToList();
        var ksByPitch = string.Join(",", new[] { 20, 21, 22, 23, 24 }.Select(p => $"{p}:{ksPlug.Count(n => n.Pitch == p)}"));
        var realStarts = plug.Notes.Where(n => n.Pitch > 30).Select(n => n.Start).OrderBy(t => t).ToList();
        var precedes = ksPlug.Any(k => realStarts.Any(r => r >= k.Start && r - k.Start <= 100));

        var exp = new List<string>
        {
            $"# Export tables song={Path.GetFileName(songPath)} tracks={src.Tracks.Count} notes={srcNotes}",
            $"# SOURCE_TECHNIQUES " + string.Join(",", counts.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}")),
            "| variant | bytes | notes | bends | wheelMoves | ccs | keyswitchNotes | mapped/kept/skipped |",
            "|---|---|---|---|---|---|---|---|",
            $"| (a) as-played | {new FileInfo(aPath).Length} | {a.Notes.Count} | {a.Bends.Count} | {WheelMoves(a.Bends)} | {a.Ccs.Count} | 0 | - |",
            $"| (b) ample preset (GM) | {new FileInfo(amplePath).Length} | {ample.Notes.Count} | {ample.Bends.Count} | {WheelMoves(ample.Bends)} | {ample.Ccs.Count} | {ksAmple.Count} | {ampleOpts.Mapped}/{ampleOpts.Kept}/{ampleOpts.Skipped} |",
            $"| (b) custom map (built-in) | {new FileInfo(customPath).Length} | {custom.Notes.Count} | {custom.Bends.Count} | {WheelMoves(custom.Bends)} | {custom.Ccs.Count} | {custom.Notes.Count(n => n.Pitch is 20 or 21 or 22 or 23 or 24)} | {customOpts.Mapped}/{customOpts.Kept}/{customOpts.Skipped} |",
            $"| (b) custom map (plugin) | {new FileInfo(plugPath).Length} | {plug.Notes.Count} | {plug.Bends.Count} | {WheelMoves(plug.Bends)} | {plug.Ccs.Count} | {ksPlug.Count} [{ksByPitch}] precedes={precedes} | {plugOpts.Mapped}/{plugOpts.Kept}/{plugOpts.Skipped} |",
            $"| (b) keep unmapped | {new FileInfo(keepPath).Length} | {keep.Notes.Count} | {keep.Bends.Count} | {WheelMoves(keep.Bends)} | {keep.Ccs.Count} | - | {keepOpts.Mapped}/{keepOpts.Kept}/{keepOpts.Skipped} |",
            $"| (b) skip unmapped | {new FileInfo(skipPath).Length} | {skip.Notes.Count} | {skip.Bends.Count} | {WheelMoves(skip.Bends)} | {skip.Ccs.Count} | - | {skipOpts.Mapped}/{skipOpts.Kept}/{skipOpts.Skipped} |",
            "| technique | srcCount | customKsPitch | ksInPlugin | ampleKsPresent |",
            "|---|---|---|---|---|",
        };
        var ksPitchFor = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [TechniqueNames.PalmMute] = 20,
            [TechniqueNames.LegatoSlide] = 21,
            [TechniqueNames.Harmonic] = 22,
            ["Slide"] = 23,
        };
        foreach (var kv in counts.OrderByDescending(kv => kv.Value))
        {
            var kp = ksPitchFor.TryGetValue(kv.Key, out var p) ? p.ToString() : "-";
            var kc = ksPitchFor.TryGetValue(kv.Key, out var p2) ? ksPlug.Count(n => n.Pitch == p2).ToString() : "-";
            exp.Add($"| {kv.Key} | {kv.Value} | {kp} | {kc} | {(ksAmple.Count > 0 ? "some" : "none")} |");
        }
        File.WriteAllText(Path.Combine(OutDir, "tables_export.md"), string.Join("\n", exp) + "\n");

        var backA = GuitarProImporter.Import(aPath);
        var backB = GuitarProImporter.Import(plugPath);
        string Summ(SongProject s)
        {
            var cells = s.Tracks.SelectMany(t => t.Measures).SelectMany(m => m.Cells.Concat(m.Voice2Cells ?? new List<TabCell>())).ToList();
            var vel = cells.SelectMany(c => c.Notes).Select(n => (double)n.Velocity).DefaultIfEmpty(0).Average();
            return $"tracks={s.Tracks.Count} notes={cells.Sum(c => c.Notes.Count)} tempo={s.Tempo} meter={s.TimeSignatureNumerator}/{s.TimeSignatureDenominator} velAvg={vel:F1} programs=[{string.Join(",", s.Tracks.Select(t => t.MidiProgram))}]";
        }
        var backACells = backA.Tracks.SelectMany(t => t.Measures).SelectMany(m => m.Cells.Concat(m.Voice2Cells ?? new List<TabCell>())).SelectMany(c => c.Notes).ToList();
        var backBCells = backB.Tracks.SelectMany(t => t.Measures).SelectMany(m => m.Cells.Concat(m.Voice2Cells ?? new List<TabCell>())).SelectMany(c => c.Notes).ToList();
        var backATech = MidiArticulationVerifyTests.TechniqueCounts(backA);
        var backBTech = MidiArticulationVerifyTests.TechniqueCounts(backB);
        var lowB = backBCells.Count(n => n.MidiValue is 20 or 21 or 22 or 23 or 24);
        var bendB = backBCells.Count(n => n.Techniques.Contains(TechniqueNames.Bend));
        var imp = new List<string>
        {
            $"# Import round-trip song={Path.GetFileName(songPath)}",
            $"# SRC {Summ(src)}",
            $"# BACK_A {Summ(backA)} midiFile ccs={a.Ccs.Count} bends={a.Bends.Count} programs={a.Programs.Count}",
            $"# BACK_Bplugin {Summ(backB)} midiKs={ksPlug.Count} lowNotesAfterImport={lowB} bendMarksAfterImport={bendB}",
            "| check | src | backA | backBplugin |",
            "|---|---|---|---|",
            $"| tracks/names | {src.Tracks.Count} [{string.Join(";", src.Tracks.Select(t => t.Name))}] | {backA.Tracks.Count} [{string.Join(";", backA.Tracks.Select(t => t.Name))}] | {backB.Tracks.Count} |",
            $"| notes | {srcNotes} | {backACells.Count} | {backBCells.Count} |",
            $"| tempo/meter | {src.Tempo} {src.TimeSignatureNumerator}/{src.TimeSignatureDenominator} | {backA.Tempo} {backA.TimeSignatureNumerator}/{backA.TimeSignatureDenominator} | {backB.Tempo} {backB.TimeSignatureNumerator}/{backB.TimeSignatureDenominator} |",
            $"| techniques lost | - | {string.Join(",", counts.Keys.Where(k => !backATech.ContainsKey(k)))} | {string.Join(",", counts.Keys.Where(k => !backBTech.ContainsKey(k)))} |",
            $"| techniques back | - | {string.Join(",", backATech.Select(kv => $"{kv.Key}={kv.Value}"))} | {string.Join(",", backBTech.Select(kv => $"{kv.Key}={kv.Value}"))} |",
            $"| bends/CC/keyswitch fate | bends={a.Bends.Count} cc={a.Ccs.Count} in file | bends->quantised pitches, CC dropped, no technique marks | keyswitches->{lowB} low notes (pitches 20-24), bends->{bendB} marks |",
        };
        File.WriteAllText(Path.Combine(OutDir, "tables_import.md"), string.Join("\n", imp) + "\n");

        Assert.True(File.Exists(Path.Combine(OutDir, "tables_export.md")));
        Assert.True(File.Exists(Path.Combine(OutDir, "tables_import.md")));
    }
}
