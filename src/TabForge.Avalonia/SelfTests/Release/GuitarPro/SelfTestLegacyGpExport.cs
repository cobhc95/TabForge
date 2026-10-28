using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge;

// Owns: the legacy-format copy check: a built song and the sample riff are written as .gp5, .gp4 and .gp3, read back with the ordinary importer, and compared with what the
// current-format round trip gives for the same song (bars, tempo, repeats, keys, tracks, tunings, durations, notes, frets and the marks each format keeps).
// Does not own: the song (SelfTestLegacyGpExport.Song.cs) or the writer (LegacyGpWriter).
// Tests: TestLegacyGpExport.
public static partial class SelfTest
{
    /// <summary>The marks each format can carry back; a mark outside the set is not compared for that format.</summary>
    private static HashSet<string> LgKept(LegacyGpFormat format, bool all) => all ? new HashSet<string>(GpEffects.All) : format == LegacyGpFormat.Gp3
        ? new HashSet<string> { "LetRing", "HOPO", "LegatoSlide", "ShiftSlide", "Bend" }
        : new HashSet<string> { "PalmMute", "LetRing", "HOPO", "LegatoSlide", "ShiftSlide", "Vibrato", "Harmonic", "Bend" };

    private static string LgCell(TabCell cell, bool drums, LegacyGpFormat format, bool all)
    {
        var kept = LgKept(format, all);
        var text = $"d{cell.DurationDenominator}.{cell.Dots}" + (cell.Tuplet.Numerator > 1 ? $"t{cell.Tuplet.Numerator}/{cell.Tuplet.Denominator}" : "") + (cell.IsRest ? "R" : "");
        if (format != LegacyGpFormat.Gp3 && cell.Staccato) text += "s";
        if (cell.Accent > 0) text += "a" + cell.Accent;
        if (!string.IsNullOrEmpty(cell.Text)) text += "'" + cell.Text + "'";
        if (!string.IsNullOrEmpty(cell.ChordName)) text += "<" + cell.ChordName + ">";
        if (format != LegacyGpFormat.Gp3 && !string.IsNullOrEmpty(cell.Lyrics)) text += "{" + cell.Lyrics + "}";
        foreach (var n in cell.Notes.Where(n => !n.IsGraceNote).GroupBy(n => (n.StringIndex, n.Fret)).Select(g => g.First()).OrderBy(n => n.StringIndex))
        {
            text += drums ? $" m{n.MidiValue}" : $" {n.StringIndex}:{n.Fret}";
            if (n.Dead) text += "x"; if (n.Ghost) text += "g"; if (n.Tied || cell.IsTied) text += "~";
            text += string.Concat(n.Techniques.Select(t => format == LegacyGpFormat.Gp3 && t == "LegatoSlide" ? "ShiftSlide" : t).Where(kept.Contains).OrderBy(t => t, StringComparer.Ordinal).Select(t => "," + t));
            if (n.BendPoints.Count >= 2) text += $",bend{n.BendPoints.Max(p => p.Value):0.#}";
        }
        var graces = cell.Notes.Count(n => n.IsGraceNote) + (cell.IsGrace ? 1 : 0);
        return graces > 0 ? text + $" grace{graces}" : text;
    }

    /// <summary>Techniques a format cannot carry back, with the words the loss list must use for them; the bars of the technique song named so are not compared.</summary>
    private static Dictionary<string, string> LgLost(LegacyGpFormat format)
    {
        var lost = new Dictionary<string, string>
        {
            ["FeedbackHarmonic"] = "harmonics", ["PickSlideUp"] = "Pick slides", ["PickSlideDown"] = "Pick slides", ["LeftTap"] = "Left-hand tapping", ["DeadSlapped"] = "Dead slapped",
            ["Legato"] = "Legato slurs", ["FadeOut"] = "Fade out", ["WahOpen"] = "Wah", ["WahClose"] = "Wah", ["ArpeggioDown"] = "Arpeggio strokes", ["ArpeggioUp"] = "Arpeggio strokes",
            ["Rasgueado"] = "Rasgueado", ["GraceBefore"] = "Grace note", ["GraceOnBeat"] = "Grace note", ["GraceBend"] = "Grace note",
        };
        if (format != LegacyGpFormat.Gp5) { lost["SlideInBelow"] = "Slides into"; lost["SlideInAbove"] = "Slides into"; }
        if (format != LegacyGpFormat.Gp3) return lost;
        foreach (var name in GpEffects.All.Where(n => n.StartsWith("TremBar", StringComparison.Ordinal))) lost[name] = "Tremolo bar";
        foreach (var name in new[] { "PinchHarmonic", "TapHarmonic", "SemiHarmonic" }) lost[name] = "harmonics";
        lost["PalmMute"] = "Palm mute"; lost["SlideOutUp"] = "Slides out"; lost["SlideOutDown"] = "Slides out"; lost["Trill"] = "Trills"; lost["TremoloPick"] = "Tremolo picking";
        lost["PickDown"] = "Pick direction"; lost["PickUp"] = "Pick direction";
        return lost;
    }

    private static List<string> LgSignature(SongProject p, LegacyGpFormat format, bool all, HashSet<int>? skipBars = null)
    {
        var lines = new List<string> { $"tempo {p.Tempo}", $"tracks {p.NotationTracks.Count()}" };
        var ti = 0;
        foreach (var t in p.NotationTracks)
        {
            var drums = t.IsDrumTrack;
            lines.Add($"track {t.Name} bars {t.Measures.Count}" + (drums ? "" : $" tune {string.Join(",", t.StringTunings)} capo {t.Capo}"));
            for (var b = 0; b < t.Measures.Count; b++)
            {
                if (skipBars is not null && skipBars.Contains(b)) continue;
                var m = t.Measures[b];
                lines.Add($"t{ti} b{b} slots {MusicTime.BarSlots(p, b)} tempo {MusicTime.TempoAt(p, b)} repeat {m.RepeatStart}/{m.RepeatEnd}" + (m.RepeatEnd ? $"x{m.RepeatCount}" : "")
                    + $" key {m.KeySignature ?? p.KeySignature} ending {m.EndingPasses} double {m.IsDoubleBar} mid {string.Join(",", (m.MidBarTempos ?? new()).Select(x => $"{x.Slot:0.#}@{x.Tempo}"))}");
                lines.Add($"t{ti} b{b} v1 " + string.Join(" | ", m.Cells.Where(c => c.Notes.Count > 0 || c.IsRest).Select(c => LgCell(c, drums, format, all))));
                if (format == LegacyGpFormat.Gp5) lines.Add($"t{ti} b{b} v2 " + string.Join(" | ", m.Voice2Cells.Where(c => c.Notes.Count > 0).Select(c => LgCell(c, drums, format, all))));
            }
            ti++;
        }
        lines.AddRange(p.Markers.Select(m => $"marker {m.MeasureIndex} {m.Title}"));
        return lines;
    }

    private static string LgDemoPath()
    {
        var root = FindRepositoryRoot();
        if (root is null) return "";
        foreach (var candidate in new[] { Path.Combine(root, "Source", "docs", "tutorial", "detailed", "downloads", "first-riff-11.gp"), Path.Combine(root, "docs", "tutorial", "detailed", "downloads", "first-riff-11.gp") })
            if (File.Exists(candidate)) return candidate;
        return "";
    }

    private static void TestLegacyGpExport()
    {
        var folder = RtFolder();
        try
        {
            var sources = new List<(string Name, SongProject Song)> { ("feature song", BuildLegacyGpSong()) };
            var techniques = TechniqueSong(out var barNames);
            sources.Add(("technique song", techniques));
            var wideVibrato = barNames.IndexOf("WideVibrato");
            var demo = LgDemoPath();
            if (demo.Length > 0) sources.Add(("sample riff", GuitarProImporter.Import(demo, new ImportContext())));
            else Skip("legacy export: the sample riff round trip", "docs/tutorial/detailed/downloads/first-riff-11.gp not found from the test's folder");
            foreach (var (name, song) in sources)
            {
                var baseline = RtViaGp(song, folder, name.Replace(' ', '-'), embed: false);
                foreach (var format in LegacyGpFormats.All)
                {
                    var ext = LegacyGpFormats.Extension(format);
                    var path = Path.Combine(folder, name.Replace(' ', '-') + ext);
                    var report = LegacyGpExport.Save(song, path, format);
                    Log.Add($"  info  legacy {name} {ext}: {new FileInfo(path).Length} bytes; the loss list: " + (report.Losses.Count == 0 ? "(nothing)" : string.Join("; ", report.Losses.Select(l => $"{l.Feature} x{l.Count} ({l.Where}) -> {l.Result}"))));
                    SongProject back;
                    try { back = GuitarProImporter.Import(path, new ImportContext()); }
                    catch (Exception ex) { Check($"legacy export {name} {ext}: the file re-imports", false, ex.Message); continue; }
                    var lost = name == "technique song" ? LgLost(format) : new Dictionary<string, string>();
                    var skip = barNames.Select((n, i) => (n, i)).Where(x => name == "technique song" && (lost.ContainsKey(x.n) || x.i == wideVibrato)).Select(x => x.i).ToHashSet();
                    var every = name == "technique song";
                    var want = LgSignature(baseline, format, every, skip); var got = LgSignature(back, format, every, skip);
                    var unlisted = lost.Where(kv => barNames.Contains(kv.Key) && !report.Losses.Any(l => l.Feature.Contains(kv.Value, StringComparison.OrdinalIgnoreCase))).Select(kv => kv.Key).ToList();
                    if (name == "technique song") Check($"legacy export {name} {ext}: every technique the format drops is named in the loss list", unlisted.Count == 0, "not named: " + string.Join(", ", unlisted));
                    var diffs = new List<string>();
                    for (var i = 0; i < Math.Max(want.Count, got.Count); i++)
                    {
                        var a = i < want.Count ? want[i] : "<none>"; var b = i < got.Count ? got[i] : "<none>";
                        if (a != b) diffs.Add($"want [{a}] got [{b}]");
                    }
                    if (name == "feature song")
                        Check($"legacy export {name} {ext}: the compared song really holds a mid-bar tempo, lyrics, a marker, repeats and a second voice",
                            want.Any(l => l.Contains("mid 8@110")) && (format == LegacyGpFormat.Gp3 || want.Any(l => l.Contains("{Hel-}"))) && want.Any(l => l.StartsWith("marker 4 Chorus"))
                            && want.Any(l => l.Contains("repeat False/True")) && (format != LegacyGpFormat.Gp5 || want.Any(l => l.Contains(" v2 d4.0 5:0"))), string.Join(" / ", want.Take(8)));
                    Check($"legacy export {name} {ext}: notes, frets, durations, bars, tempo, repeats and keys survive a re-import ({want.Count} lines)",
                        diffs.Count == 0, $"{diffs.Count} differ: " + string.Join(" || ", diffs.Take(4)));
                    if (name == "feature song" && format != LegacyGpFormat.Gp5)
                        Check($"legacy export {name} {ext}: the loss list names the second voice", report.Losses.Any(l => l.Feature == "Second voice"));
                }
            }
            CheckLegacyExtendedRange(folder);
            var wrong = Path.Combine(folder, "wrong.mid");
            Check("legacy export: a path with another extension is refused", ThrowsAny(() => LegacyGpExport.Save(sources[0].Song, wrong, LegacyGpFormat.Gp5)) && !File.Exists(wrong));
        }
        finally { RtCleanup(folder); }
    }

    /// <summary>An eight-string guitar and an audio track: the eighth string and the audio track cannot be written, the rest still re-imports, and the list says so.</summary>
    private static void CheckLegacyExtendedRange(string folder)
    {
        var song = new SongProject { Title = "Extended range", Tempo = 100 };
        var guitar = new TrackModel { Name = "Eight", Kind = TrackKind.Guitar, StringTunings = new List<int> { 64, 59, 55, 50, 45, 40, 35, 30 }, Measures = TemplateFactory.Measures(1) };
        LgPut(guitar.Measures[0], 0, 4, LgNote(guitar, 6, 3));
        LgPut(guitar.Measures[0], 4, 4, LgNote(guitar, 7, 0), LgNote(guitar, 0, 5));
        song.Tracks.Add(guitar);
        song.Tracks.Add(new TrackModel { Name = "Backing", Kind = TrackKind.Audio });
        foreach (var format in LegacyGpFormats.All)
        {
            var path = Path.Combine(folder, "range" + LegacyGpFormats.Extension(format));
            var report = LegacyGpExport.Save(song, path, format);
            var back = GuitarProImporter.Import(path, new ImportContext());
            var cells = back.Tracks[0].Measures[0].Cells.Where(c => c.Notes.Count > 0).ToList();
            Check($"legacy export extended range {LegacyGpFormats.Extension(format)}: the audio track and the eighth string are left out and named, the other notes stay",
                back.Tracks.Count == 1 && cells.Count == 2 && cells[0].Notes.Single().Fret == 3 && cells[1].Notes.Single().Fret == 5
                && report.Losses.Any(l => l.Feature == "More than seven strings") && report.Losses.Any(l => l.Feature.StartsWith("Notes on strings past the seventh", StringComparison.Ordinal))
                && report.Losses.Any(l => l.Feature == "Audio tracks"), string.Join("; ", report.Losses.Select(l => l.Feature)) + " | " + string.Join(" / ", cells.Select(c => string.Join(",", c.Notes.Select(n => n.StringIndex + ":" + n.Fret)))));
        }
    }

    private static bool ThrowsAny(Action action)
    {
        try { action(); return false; }
        catch (Exception) { return true; }   // swallow: the caller only asks whether it threw
    }
}
