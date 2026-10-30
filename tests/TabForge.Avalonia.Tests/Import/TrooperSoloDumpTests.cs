using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Trooper solo audit dump (tests only, no src edits).
// Reads C:/tfrel-tmp/trooper/trooper.gp5 (skips when missing) and writes
// C:/tfrel-tmp/trooper-dump/dump.md + summary.md via File.WriteAllText (no stdout).
// Dumps bars 89-96 of the solo track exactly as GuitarProImporter produced them
// plus the compiled MIDI from MidiTimelineBuilder with default options.
public sealed class TrooperSoloDumpTests
{
    private const string InPath = "C:/tfrel-tmp/trooper/trooper.gp5";
    private const string OutDir = "C:/tfrel-tmp/trooper-dump";
    private const double TicksPerSlot = 240.0; // 960 ticks/quarter -> 240 per sixteenth slot
    private const double BendRangeSemitones = 12.0; // ScoreToMidiCompiler.PitchBendRangeSemitones

    [Fact]
    public void TrooperSoloDump()
    {
        Directory.CreateDirectory(OutDir);
        if (!File.Exists(InPath))
        {
            File.WriteAllText(OutDir + "/dump.md", "# Trooper solo dump\n\nSKIPPED: input file missing: " + InPath + "\n");
            return;
        }

        AlphaTabWarmUp.EnsureReady();
        var context = new ImportContext();
        var song = GuitarProImporter.Import(InPath, context);
        var opt = new PlaybackOptions();
        var timeline = MidiTimelineBuilder.Build(song, opt);
        var order = MidiTimelineBuilder.BuildPlaybackOrder(song, opt);

        // Solo track = most notes in source bars 89-95 (0-based 88..94).
        const int soloFirst = 88;
        const int soloLast = 94; // inclusive; dump runs to bar 96 (index 95)
        const int dumpLast = 95;
        var counts = song.Tracks.Select((t, i) => new
        {
            Index = i,
            Count = CountNotes(t, soloFirst, soloLast),
            Total = t.Measures.Sum(m => m.Cells.Sum(c => c.Notes.Count) + m.Voice2Cells.Sum(c => c.Notes.Count)),
            SoloFx = CountSoloFx(t, soloFirst, soloLast)
        }).ToList();
        // The solo is a lead-guitar part: never drums/bass. Tie-break equal note counts by
        // solo techniques (bend/vibrato/HOPO/slide) in range.
        var solo = counts.Where(c => song.Tracks[c.Index].Kind != TrackKind.Drums)
            .OrderByDescending(c => song.Tracks[c.Index].Kind == TrackKind.Guitar)
            .ThenByDescending(c => c.Count).ThenByDescending(c => c.SoloFx).First();
        Assert.True(song.Tracks.Count > 0);
        Assert.True(solo.Count > 0);
        var track = song.Tracks[solo.Index];

        var dump = new StringBuilder();
        WriteHeader(dump, song, counts, solo.Index, soloFirst, soloLast, dumpLast);
        WriteImported(dump, song, track, solo.Index, soloFirst, dumpLast);
        WriteRaw(dump, track, solo.Index, soloFirst, dumpLast);
        WriteCompiled(dump, song, timeline, order, track, solo.Index, soloFirst, dumpLast);
        File.WriteAllText(OutDir + "/dump.md", dump.ToString());

        var summary = new StringBuilder();
        WriteSummary(summary, song, timeline, order, track, solo.Index, soloFirst, soloLast);
        File.WriteAllText(OutDir + "/summary.md", summary.ToString());

        Assert.True(File.Exists(OutDir + "/dump.md"));
        Assert.True(File.Exists(OutDir + "/summary.md"));
    }

    private static int CountNotes(TrackModel t, int first, int last)
    {
        var n = 0;
        for (var b = first; b <= last && b < t.Measures.Count; b++)
            n += t.Measures[b].Cells.Sum(c => c.Notes.Count) + t.Measures[b].Voice2Cells.Sum(c => c.Notes.Count);
        return n;
    }

    private static int CountSoloFx(TrackModel t, int first, int last)
    {
        var n = 0;
        for (var b = first; b <= last && b < t.Measures.Count; b++)
            foreach (var c in t.Measures[b].Cells.Concat(t.Measures[b].Voice2Cells))
                foreach (var note in c.Notes)
                    if (note.BendPoints.Count > 0 || note.Techniques.Contains("Bend") || note.Techniques.Contains("Vibrato") || note.Techniques.Contains("WideVibrato")
                        || note.Techniques.Contains("HOPO") || note.Techniques.Contains("Slide") || note.Techniques.Contains("ShiftSlide") || note.Techniques.Contains("LegatoSlide"))
                        n++;
        return n;
    }

    private static void WriteHeader(StringBuilder sb, SongProject song, IEnumerable<dynamic> counts, int solo, int first, int last, int dumpLast)
    {
        sb.AppendLine("# Trooper solo dump (bars 89-96)");
        sb.AppendLine();
        sb.AppendLine("- input: `C:/tfrel-tmp/trooper/trooper.gp5` (" + new FileInfo(InPath).Length + " bytes)");
        sb.AppendLine("- importer: `TabForge.Services.GuitarProImporter` (legacy GP3-5 reader: alphaTab `Gp3To5Importer` via `AlphaTabBoundary`, patched `TabForge.AlphaTab` build; embeds nothing, converts every voice/staff of every track)");
        sb.AppendLine("- title: " + Esc(song.Title) + " | artist: " + Esc(song.Artist) + " | tempo: " + song.Tempo + " | bars: " + (song.MasterBarTrack?.Measures.Count ?? 0));
        sb.AppendLine("- compile: `MidiTimelineBuilder.Build(song, new PlaybackOptions())` (defaults; pitch-bend range +/-12 semitones; wheel 8192 = centre, semitones = (value-8192)*12/8192)");
        sb.AppendLine("- MIDI ticks below are approximate: 960 ticks/quarter derived from the performed tempo map (`ScoreTimeline.Bars`: Tempo per performed bar). Imported-model ticks = slots*240 (sixteenth slot = 240 ticks).");
        sb.AppendLine("- bar numbers are 1-based song bars; beat = cell index in the bar grid (voice 1 = `Cells`, voice 2 = `Voice2Cells`); pos = `RhythmicPosition ?? cell` in sixteenth slots.");
        sb.AppendLine();
        sb.AppendLine("## Tracks (notes in bars 89-95)");
        sb.AppendLine();
        sb.AppendLine("| idx | name | kind | bars | notes 89-95 | soloFx 89-95 | notes total |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var c in counts)
            sb.AppendLine("| " + c.Index + " | " + Esc(song.Tracks[c.Index].Name) + " | " + song.Tracks[c.Index].Kind + " | " + song.Tracks[c.Index].Measures.Count + " | " + c.Count + " | " + c.SoloFx + " | " + c.Total + " |");
        sb.AppendLine();
        sb.AppendLine("solo track (most notes in 89-95): **#" + solo + " " + Esc(song.Tracks[solo].Name) + "**; dump covers bars 89-96 (0-based " + first + ".." + dumpLast + ").");
        sb.AppendLine();
        sb.AppendLine("## Bar headers 89-96 (master track)");
        sb.AppendLine();
        sb.AppendLine("| bar | time sig | tempo change | section | directions |");
        sb.AppendLine("|---|---|---|---|---|");
        var master = song.MasterBarTrack;
        for (var b = first; b <= dumpLast && master is not null && b < master.Measures.Count; b++)
        {
            var m = master.Measures[b];
            sb.AppendLine("| " + (b + 1) + " | " + (m.TimeSigNum?.ToString() ?? song.TimeSignatureNumerator.ToString()) + "/" + (m.TimeSigDenom?.ToString() ?? song.TimeSignatureDenominator.ToString())
                + " | " + (m.TempoChange?.ToString() ?? "-") + (m.MidBarTempos is { Count: > 0 } ? " mid:" + string.Join(";", m.MidBarTempos.Select(p => p.Slot + "=" + p.Tempo)) : "")
                + " | " + Esc(m.SectionName) + " | " + Esc(m.Directions) + " |");
        }
        sb.AppendLine();
    }

    private static void WriteImported(StringBuilder sb, SongProject song, TrackModel track, int ti, int first, int last)
    {
        sb.AppendLine("## Imported model, track #" + ti + " " + Esc(track.Name) + " (bars 89-96)");
        sb.AppendLine();
        sb.AppendLine("tuning(high->low): " + string.Join(",", track.StringTunings) + " | capo: " + track.Capo + " | midiCh: " + track.MidiChannel + " | program: " + track.MidiProgram);
        sb.AppendLine();
        sb.AppendLine("bend points as stored: Offset 0..60 (fraction x60 of the note), Value in quarter-tones (4 = whole tone; compiler halves to semitones).");
        sb.AppendLine();
        sb.AppendLine("| bar | v | beat | pos | ticks | dur | str | fret | midi | vel | tied | techniques (note level) | bend [type/style: (off,val)...] | slide type->target | vib | misc | grace |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        for (var b = first; b <= last && b < track.Measures.Count; b++)
        {
            var m = track.Measures[b];
            for (var v = 0; v < 2; v++)
            {
                var cells = v == 0 ? m.Cells : m.Voice2Cells;
                for (var c = 0; c < cells.Count; c++)
                {
                    var cell = cells[c];
                    foreach (var n in cell.Notes)
                    {
                        var pos = cell.RhythmicPosition ?? c;
                        sb.Append("| " + (b + 1) + " | " + (v + 1) + " | " + c + " | " + F(pos) + " | " + (int)Math.Round(pos * TicksPerSlot)
                            + " | " + Dur(cell) + " | " + n.StringIndex + " | " + n.Fret + " | " + n.MidiValue + " | " + n.Velocity
                            + " | " + Tie(n, cell) + " | " + Esc(Sorted(n.Techniques))
                            + " | " + Bend(n) + " | " + Slide(n) + " | " + Vib(n) + " | " + Misc(n) + " | " + Grace(n, cell) + " |");
                        sb.AppendLine();
                    }
                }
            }
        }
        sb.AppendLine();
        sb.AppendLine("### Beat-level (cell) effects in 89-96 (only non-default cells)");
        sb.AppendLine();
        sb.AppendLine("| bar | v | beat | pos | cell-level |");
        sb.AppendLine("|---|---|---|---|---|");
        for (var b = first; b <= last && b < track.Measures.Count; b++)
        {
            var m = track.Measures[b];
            for (var v = 0; v < 2; v++)
            {
                var cells = v == 0 ? m.Cells : m.Voice2Cells;
                for (var c = 0; c < cells.Count; c++)
                {
                    var fx = CellFx(cells[c]);
                    if (fx.Length > 0)
                        sb.AppendLine("| " + (b + 1) + " | " + (v + 1) + " | " + c + " | " + F(cells[c].RhythmicPosition ?? c) + " | " + Esc(fx) + " |");
                }
            }
        }
        sb.AppendLine();
        sb.AppendLine("note-level = `TabNote` fields/techniques above; beat-level = `TabCell` fields here. Note: the importer copies beat-level vibrato (`Vibrato`/`Wide`) into the note's technique set, and beat-level accent/staccato live on the cell.");
        sb.AppendLine();
    }

    private static string Dur(TabCell c)
    {
        var s = "1/" + c.DurationDenominator + new string('.', c.Dots);
        if (c.TupletNumerator > 0) s += " " + c.TupletNumerator + ":" + c.TupletDenominator;
        else if (c.IsTriplet) s += " 3:2";
        if (c.SoundDurationPercent != 100) s += " snd" + c.SoundDurationPercent + "%";
        if (c.IsRest) s += " rest";
        return s;
    }

    private static string Tie(TabNote n, TabCell c)
    {
        var s = "";
        if (n.Tied) s += "note";
        if (c.IsTied) s += (s.Length > 0 ? "+cell" : "cell");
        if (n.Techniques.Contains("Tie")) s += (s.Length > 0 ? "+origin" : "origin");
        return s.Length > 0 ? s : "-";
    }

    private static string Bend(TabNote n)
    {
        if (n.BendPoints.Count == 0) return n.Techniques.Contains("Bend") ? "flag-only" : "-";
        return "[" + n.BendTypeName + "/" + n.BendStyleName + ": " + string.Join(" ", n.BendPoints.Select(p => "(" + F(p.Offset) + "," + F(p.Value) + ")")) + "]";
    }

    private static string Slide(TabNote n)
    {
        var kinds = n.Techniques.Where(t => t.Contains("Slide")).ToList();
        if (kinds.Count == 0 && n.SlideTargetMidi <= 0) return "-";
        return string.Join("+", kinds) + (n.SlideTargetMidi > 0 ? "->" + n.SlideTargetMidi : "");
    }

    private static string Vib(TabNote n)
    {
        var w = n.Techniques.Contains("WideVibrato");
        var v = n.Techniques.Contains("Vibrato");
        if (!v && !w) return "-";
        return (w ? "wide" : "norm") + (n.VibratoDepth is { } d ? " d=" + d : "") + (n.VibratoSpeed is { } s ? " s=" + s : "");
    }

    private static string Misc(TabNote n)
    {
        var l = new List<string>();
        if (n.Techniques.Contains("PalmMute")) l.Add("PM");
        if (n.Techniques.Contains("LetRing")) l.Add("LR");
        if (n.HarmonicFret is { } h) l.Add("harm@" + h + ":" + string.Join("+", n.Techniques.Where(t => t.Contains("armonic"))));
        if (n.Techniques.Contains("Trill")) l.Add("trill->" + n.TrillTargetMidi + "/1/" + n.TrillDurationDenominator);
        if (n.Techniques.Contains("TremoloPick")) l.Add("tremPick");
        if (n.Techniques.Contains("Tapping") || n.Techniques.Contains("LeftTap")) l.Add("tap");
        if (n.Dead) l.Add("dead");
        if (n.Ghost) l.Add("ghost");
        if (n.Silent) l.Add("silent");
        if (n.LeftHandFinger is { } lf) l.Add("LH" + lf);
        if (n.RightHandFinger is { } rf) l.Add("RH" + rf);
        var rest = n.Techniques.Where(t => !Known(t)).ToList();
        if (rest.Count > 0) l.Add("other:" + string.Join("+", rest));
        return l.Count > 0 ? string.Join(" ", l) : "-";
    }

    private static bool Known(string t) => t.Contains("Slide") || t.Contains("Vibrato") || t is "Bend" or "HOPO" or "HOPOOrigin" or "HOPODestination"
        or "PalmMute" or "LetRing" or "Trill" or "TremoloPick" or "Tapping" or "LeftTap" or "Dead" or "Ghost" or "Tie"
        || t.Contains("armonic");

    private static string Grace(TabNote n, TabCell c)
    {
        if (!n.IsGraceNote && !c.IsGrace) return "-";
        return (n.IsGraceNote ? "note" + (n.GraceBeforeBeat ? "<" : ">") + " off" + F(n.GraceOnsetOffsetSlots) + " len" + F(n.GraceDurationSlots) : "cell") + (c.IsGrace ? "+cell" : "");
    }

    private static string CellFx(TabCell c)
    {
        var l = new List<string>();
        if (c.WhammyPoints.Count > 0) l.Add("whammy[" + string.Join(" ", c.WhammyPoints.Select(p => "(" + F(p.Offset) + "," + F(p.Value) + ")")) + "]");
        if (c.TremoloPickDenominator > 0) l.Add("tremPick/1/" + c.TremoloPickDenominator);
        if (c.Staccato) l.Add("stacc");
        if (c.Tenuto) l.Add("ten");
        if (c.Accent == 1) l.Add("acc");
        if (c.Accent == 2) l.Add("heavyAcc");
        if (c.IsTied) l.Add("tied");
        if (c.IsGrace) l.Add("grace" + (c.GraceBeforeBeat ? "<" : ">"));
        if (c.Fermata) l.Add("fermata");
        if (c.OctaveShiftSemitones != 0) l.Add("8va" + c.OctaveShiftSemitones);
        if (c.SoundDurationPercent != 100) l.Add("snd" + c.SoundDurationPercent + "%");
        if (c.BrushStepSlots != 0) l.Add("brush" + F(c.BrushStepSlots));
        if (c.Mix is { IsEmpty: false }) l.Add("mix");
        if (!string.IsNullOrWhiteSpace(c.ChordName)) l.Add("chord=" + c.ChordName);
        if (!string.IsNullOrWhiteSpace(c.Text)) l.Add("txt=" + c.Text);
        return string.Join(" ", l);
    }

    // ---------- raw alphaTab (file-level) ----------

    private static void WriteRaw(StringBuilder sb, TrackModel track, int ti, int first, int last)
    {
        sb.AppendLine("## Raw file data (alphaTab reader, same track index)");
        sb.AppendLine();
        try
        {
            var bytes = File.ReadAllBytes(InPath);
            var score = AlphaTabBoundary.LoadScore(bytes);
            var tracks = RList(RProp(score, "Tracks"));
            sb.AppendLine("alphaTab tracks: " + tracks.Count + "; names: " + string.Join(" | ", tracks.Select(t => Esc(Str(RProp(t, "Name"))))) + ". Assumes song track #" + ti + " == alphaTab track #" + ti + " (import converts every source track in order).");
            if (ti >= tracks.Count) { sb.AppendLine("track index out of range in raw score."); sb.AppendLine(); return; }
            var staves = RList(RProp(tracks[ti], "Staves"));
            if (staves.Count == 0) { sb.AppendLine("no staves."); sb.AppendLine(); return; }
            var bars = RList(RProp(staves[0], "Bars"));
            sb.AppendLine("bars in raw staff: " + bars.Count + ". BendPoint props dumped verbatim (Offset/Value + any extra props such as a vibrato flag, if the reader exposes them).");
            sb.AppendLine();
            sb.AppendLine("| bar | v | beat | playStart | dispStart | beatVib | whammy | notes (str/fret/real bend{type/style/pts} vib hopo slide tie) |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            for (var b = first; b <= last && b < bars.Count; b++)
            {
                var voices = RList(RProp(bars[b], "Voices"));
                for (var v = 0; v < voices.Count; v++)
                {
                    var beats = RList(RProp(voices[v], "Beats"));
                    for (var bi = 0; bi < beats.Count; bi++)
                    {
                        var beat = beats[bi];
                        var notes = RList(RProp(beat, "Notes"));
                        if (notes.Count == 0 && !Has(RProp(beat, "HasWhammyBar"))) continue;
                        var wpts = RList(RProp(beat, "WhammyBarPoints"));
                        sb.Append("| " + (b + 1) + " | " + (v + 1) + " | " + bi
                            + " | " + Str(RProp(beat, "PlaybackStart")) + " | " + Str(RProp(beat, "DisplayStart"))
                            + " | " + Str(RProp(beat, "Vibrato")) + " | " + Str(RProp(beat, "WhammyBarType"))
                            + (wpts.Count > 0 ? "[" + string.Join(" ", wpts.Select(p => Props(p))) + "]" : "")
                            + " | " + string.Join("; ", notes.Select(RawNote)) + " |");
                        sb.AppendLine();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine("raw read failed: " + ex.GetType().Name + ": " + Esc(ex.Message));
        }
        sb.AppendLine();
    }

    private static string RawNote(object n)
    {
        var l = "s" + Str(RProp(n, "String")) + "/f" + Str(RProp(n, "Fret")) + "/r" + Str(RProp(n, "RealValue"));
        var bends = RList(RProp(n, "BendPoints"));
        if (bends.Count > 0) l += " bend{" + Str(RProp(n, "BendType")) + "/" + Str(RProp(n, "BendStyle")) + " " + string.Join(" ", bends.Select(p => "(" + Props(p) + ")")) + "}";
        var vib = Str(RProp(n, "Vibrato"));
        if (vib.Length > 0 && vib != "None") l += " vib=" + vib;
        foreach (var f in new[] { "IsHammerPullOrigin", "IsHammerPullDestination", "IsTieOrigin", "IsTieDestination", "IsPalmMute", "IsLetRing", "IsDead", "IsGhost", "IsTrill", "IsLeftHandTapped" })
            if (IsTrue(RProp(n, f))) l += " " + f.Substring(2);
        foreach (var f in new[] { "SlideInType", "SlideOutType", "UnlinkedSlideOutType", "HarmonicType" })
        {
            var s = Str(RProp(n, f));
            if (s.Length > 0 && s != "None") l += " " + f + "=" + s;
        }
        var tv = Str(RProp(n, "TrillValue"));
        if (tv.Length > 0 && tv != "0") l += " trillVal=" + tv;
        var st = RProp(n, "SlideTarget");
        if (st is not null) l += " slideTgt fret=" + Str(RProp(st, "Fret")) + " real=" + Str(RProp(st, "RealValue"));
        return l;
    }

    private static object? RProp(object? o, string name) => o?.GetType().GetProperty(name)?.GetValue(o);
    private static List<object> RList(object? o) => (o as IEnumerable)?.Cast<object>().ToList() ?? new List<object>();
    private static string Str(object? o) => o?.ToString() ?? "";
    private static bool Has(object? o) => o is bool b && b;
    private static bool IsTrue(object? o) => o is bool b && b;
    private static string Props(object p) => string.Join(",", p.GetType().GetProperties().Where(pr => pr.CanRead && pr.GetIndexParameters().Length == 0).Select(pr =>
    {
        try { return pr.Name + "=" + (pr.GetValue(p)?.ToString() ?? ""); } catch { return pr.Name + "=?"; }
    }));

    // ---------- compiled MIDI ----------

    private static void WriteCompiled(StringBuilder sb, SongProject song, ScoreTimeline tl, List<int> order, TrackModel track, int ti, int first, int last)
    {
        sb.AppendLine("## Compiled MIDI, track #" + ti + " (bars 89-96)");
        sb.AppendLine();
        var perfBars = order.Select((src, perf) => (src, perf)).Where(x => x.src >= first && x.src <= last).ToList();
        sb.AppendLine("performed bars mapping to source 89-96 (perf idx -> src bar): " + string.Join(" ", perfBars.Select(x => x.perf + "->" + (x.src + 1))) + ". Repeats " + (perfBars.Count > (last - first + 1) ? "expanded (a source bar sounds more than once)" : "not expanded in range") + ".");
        sb.AppendLine("tick base: cumulative performed tempo map below; wheel semitones use +/-12.");
        sb.AppendLine();
        sb.AppendLine("| perfBar | srcBar | startMs | endMs | tempo | baseTick |");
        sb.AppendLine("|---|---|---|---|---|---|");
        var baseTicks = new double[tl.Bars.Count];
        var acc = 0.0;
        for (var p = 0; p < tl.Bars.Count; p++)
        {
            baseTicks[p] = acc;
            var b = tl.Bars[p];
            sb.AppendLine("| " + p + " | " + (b.Bar + 1) + " | " + F(b.StartMs) + " | " + F(b.EndMs) + " | " + b.Tempo + " | " + (int)Math.Round(acc) + " |");
            acc += Math.Max(0, b.EndMs - b.StartMs) * b.Tempo / 60000.0 * 960.0;
        }
        double TickAt(int perf, double ms)
        {
            if (perf < 0 || perf >= tl.Bars.Count) return double.NaN;
            var b = tl.Bars[perf];
            return baseTicks[perf] + (ms - b.StartMs) * b.Tempo / 60000.0 * 960.0;
        }
        string Semi(int wheel) => F((wheel - 8192) * BendRangeSemitones / 8192.0);
        sb.AppendLine();
        sb.AppendLine("### Per-note compile (imported bar.beat v str/fret -> NoteEvent + events)");
        sb.AppendLine();
        sb.AppendLine("| note (bar.beat v s/f) | imp midi/vel | NoteEvent onsetMs/durMs midi/vel ch fxCh | on (tick/ms p/ch/vel) | off (tick/ms) | wheel (tick=value(semi) ...) | CC (tick ctrl=val ...) | cont. |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        for (var b = first; b <= last && b < track.Measures.Count; b++)
        {
            var m = track.Measures[b];
            for (var v = 0; v < 2; v++)
            {
                var cells = v == 0 ? m.Cells : m.Voice2Cells;
                for (var c = 0; c < cells.Count; c++)
                {
                    foreach (var n in cells[c].Notes)
                    {
                        var label = (b + 1) + "." + c + " v" + (v + 1) + " s" + n.StringIndex + "/f" + n.Fret;
                        var perfIdx = perfBars.Where(x => x.src == b).Select(x => x.perf).ToList();
                        var matches = tl.Notes.Where(e => e.TrackIndex == ti && perfIdx.Contains(e.Bar) && e.Cell == c && e.VoiceIndex == v && e.StringIndex == n.StringIndex && e.Fret == n.Fret).ToList();
                        if (matches.Count == 0)
                        {
                            sb.AppendLine("| " + label + " | " + n.MidiValue + "/" + n.Velocity + " | NO NoteEvent (tied merge? dead? muted?) | - | - | - | - | - |");
                            continue;
                        }
                        foreach (var e in matches)
                        {
                            var on = tl.Events.Where(ev => ev.TrackIndex == ti && (ev.Status & 0xF0) == 0x90 && ev.Data2 > 0 && ev.Channel == e.Channel && ev.Data1 == e.Midi && Math.Abs(ev.TimeMs - e.OnsetMs) < 1.5).ToList();
                            var offs = e.OffEventIndices.Where(i => i >= 0 && i < tl.Events.Count).Select(i => tl.Events[i]).ToList();
                            var wheels = tl.Events.Where(ev => ev.TrackIndex == ti && (ev.Status & 0xF0) == 0xE0 && ev.Channel == e.Channel && ev.TimeMs >= e.OnsetMs - 0.01 && ev.TimeMs <= e.OnsetMs + e.DurationMs + 0.01).OrderBy(ev => ev.TimeMs).ToList();
                            var ccs = tl.Events.Where(ev => ev.TrackIndex == ti && (ev.Status & 0xF0) == 0xB0 && ev.Channel == e.Channel && ev.TimeMs >= e.OnsetMs - 0.01 && ev.TimeMs <= e.OnsetMs + e.DurationMs + 0.01 && !ev.IsSetup).OrderBy(ev => ev.TimeMs).ToList();
                            sb.Append("| " + label + " | " + n.MidiValue + "/" + n.Velocity
                                + " | " + F(e.OnsetMs) + "/" + F(e.DurationMs) + " m" + e.Midi + "/v" + e.Velocity + " ch" + e.Channel + (e.UsesEffectChannel ? " FX" : "")
                                + " | " + string.Join(";", on.Select(ev => (int)Math.Round(TickAt(PerfOf(order, e), ev.TimeMs)) + "/" + F(ev.TimeMs) + " p" + ev.Data1 + "/ch" + ev.Channel + "/v" + ev.Data2))
                                + " | " + string.Join(";", offs.Select(ev => (int)Math.Round(TickAt(PerfOf(order, e), ev.TimeMs)) + "/" + F(ev.TimeMs)))
                                + " | " + (wheels.Count > 0 ? string.Join(" ", wheels.Select(ev => (int)Math.Round(TickAt(PerfOf(order, e), ev.TimeMs)) + "=" + ((ev.Data2 << 7) | ev.Data1) + "(" + Semi((ev.Data2 << 7) | ev.Data1) + ")")) : "-")
                                + " | " + (ccs.Count > 0 ? string.Join(" ", ccs.Select(ev => (int)Math.Round(TickAt(PerfOf(order, e), ev.TimeMs)) + " " + ev.Data1 + "=" + ev.Data2)) : "-")
                                + " | " + (e.Continuations is { Count: > 0 } ? string.Join(";", e.Continuations.Select(k => (k.Bar + 1) + "." + k.Cell)) : "-") + " |");
                            sb.AppendLine();
                        }
                    }
                }
            }
        }
        sb.AppendLine();
    }

    private static int PerfOf(List<int> order, NoteEvent e) => e.Bar;

    // ---------- summary ----------

    private sealed record BendRow(int Bar, int Cell, int Voice, TabNote Note, List<(int Perf, NoteEvent Ev)> Compiled);

    private static void WriteSummary(StringBuilder sb, SongProject song, ScoreTimeline tl, List<int> order, TrackModel track, int ti, int first, int last)
    {
        sb.AppendLine("# Trooper solo summary: bends in bars 89-95 + HOPO/slides");
        sb.AppendLine();
        var perfOf = order.Select((src, perf) => (src, perf)).Where(x => x.src >= first && x.src <= last).ToList();
        var rows = new List<BendRow>();
        for (var b = first; b <= last && b < track.Measures.Count; b++)
        {
            var m = track.Measures[b];
            for (var v = 0; v < 2; v++)
            {
                var cells = v == 0 ? m.Cells : m.Voice2Cells;
                for (var c = 0; c < cells.Count; c++)
                    foreach (var n in cells[c].Notes)
                    {
                        if (n.BendPoints.Count == 0 && !n.Techniques.Contains("Bend")) continue;
                        var perfIdx = perfOf.Where(x => x.src == b).Select(x => x.perf).ToList();
                        var comp = tl.Notes.Where(e => e.TrackIndex == ti && perfIdx.Contains(e.Bar) && e.Cell == c && e.VoiceIndex == v && e.StringIndex == n.StringIndex && e.Fret == n.Fret).Select(e => (e.Bar, e)).ToList();
                        rows.Add(new BendRow(b, c, v, n, comp));
                    }
            }
        }
        sb.AppendLine("## Every bend in bars 89-95 (" + rows.Count + ")");
        sb.AppendLine();
        sb.AppendLine("| note (bar.beat v s/f) | file type/style | imported pts (off,val qt) | class | vibrato? | wheel (beat,semi) |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var r in rows)
        {
            var wheels = new List<(int Perf, double Ms, double Semi)>();
            foreach (var (_, e) in r.Compiled)
                foreach (var ev in tl.Events.Where(ev => ev.TrackIndex == ti && (ev.Status & 0xF0) == 0xE0 && ev.Channel == e.Channel && ev.TimeMs >= e.OnsetMs - 0.01 && ev.TimeMs <= e.OnsetMs + e.DurationMs + 0.01).OrderBy(ev => ev.TimeMs))
                    wheels.Add((e.Bar, ev.TimeMs, (((ev.Data2 << 7) | ev.Data1) - 8192) * BendRangeSemitones / 8192.0));
            var wheelStr = wheels.Count == 0 ? (r.Compiled.Count == 0 ? "no NoteEvent" : "no wheel")
                : string.Join(" ", wheels.Select(w => "(" + (order[w.Perf] + 1) + "." + BeatOf(tl, w.Perf, w.Ms) + "," + (w.Semi >= 0 ? "+" : "") + F(w.Semi) + ")"));
            sb.AppendLine("| " + (r.Bar + 1) + "." + r.Cell + " v" + (r.Voice + 1) + " s" + r.Note.StringIndex + "/f" + r.Note.Fret
                + " | " + r.Note.BendTypeName + "/" + r.Note.BendStyleName
                + " | " + (r.Note.BendPoints.Count > 0 ? string.Join(" ", r.Note.BendPoints.Select(p => "(" + F(p.Offset) + "," + F(p.Value) + ")")) : "none")
                + " | " + Classify(r.Note) + " | " + VibFlag(track, r.Bar, r.Cell, r.Voice, r.Note) + " | " + wheelStr + " |");
        }
        sb.AppendLine();
        sb.AppendLine("class: bend = 0->peak hold; bend-release = starts bent, ends 0; pre-bend = starts bent, holds; bend then release = 0->peak->0; hold = flat high (often a tie carry); custom/other = as stored.");
        sb.AppendLine("vibrato: WITH = same note has Vibrato/WideVibrato; FOLLOWED(bar.beat) = next note on the same string carries vibrato.");
        sb.AppendLine();
        sb.AppendLine("## Every hammer-on/pull-off/slide in bars 89-95 (compiled pitch + timing)");
        sb.AppendLine();
        sb.AppendLine("| note (bar.beat v s/f) | kind | imp midi | compiled onsetMs midi/vel ch |");
        sb.AppendLine("|---|---|---|---|");
        for (var b = first; b <= last && b < track.Measures.Count; b++)
        {
            var m = track.Measures[b];
            for (var v = 0; v < 2; v++)
            {
                var cells = v == 0 ? m.Cells : m.Voice2Cells;
                for (var c = 0; c < cells.Count; c++)
                    foreach (var n in cells[c].Notes)
                    {
                        var kinds = n.Techniques.Where(t => t is "HOPO" or "HOPOOrigin" or "HOPODestination" || t.Contains("Slide")).ToList();
                        if (kinds.Count == 0) continue;
                        var perfIdx = perfOf.Where(x => x.src == b).Select(x => x.perf).ToList();
                        var comp = tl.Notes.Where(e => e.TrackIndex == ti && perfIdx.Contains(e.Bar) && e.Cell == c && e.VoiceIndex == v && e.StringIndex == n.StringIndex && e.Fret == n.Fret)
                            .Select(e => F(e.OnsetMs) + " m" + e.Midi + "/v" + e.Velocity + " ch" + e.Channel).ToList();
                        sb.AppendLine("| " + (b + 1) + "." + c + " v" + (v + 1) + " s" + n.StringIndex + "/f" + n.Fret + " | " + string.Join("+", kinds)
                            + (n.SlideTargetMidi > 0 ? "->" + n.SlideTargetMidi : "") + " | " + n.MidiValue + " | " + (comp.Count > 0 ? string.Join("; ", comp) : "no NoteEvent") + " |");
                    }
            }
        }
        sb.AppendLine();
    }

    private static int BeatOf(ScoreTimeline tl, int perf, double ms)
    {
        var match = tl.Notes.Where(n => n.Bar == perf && n.OnsetMs <= ms + 0.01).OrderByDescending(n => n.OnsetMs).FirstOrDefault();
        return match?.Cell ?? -1;
    }

    private static string Classify(TabNote n)
    {
        var pts = n.BendPoints.Select(p => p.Value / 2.0).ToList();
        string shape;
        if (pts.Count == 0) shape = "flag-only";
        else
        {
            var f = pts[0]; var l = pts[^1]; var peak = pts.Max(Math.Abs);
            var up = Math.Abs(f) < 0.01; var down = Math.Abs(l) < 0.01;
            shape = (up, down, peak) switch
            {
                (_, _, 0) => "flat",
                (true, false, _) => "0->" + F(pts.Max()),
                (false, true, _) => F(f) + "->0",
                (false, false, _) => Math.Abs(f - l) < 0.01 ? "hold@" + F(f) : F(f) + "->" + F(l),
                (true, true, _) => "0->pk" + F(peak) + "->0",
            };
        }
        var cls = n.BendTypeName switch
        {
            "Bend" => "bend",
            "Release" => "bend-release",
            "BendRelease" => pts.Count > 0 && Math.Abs(pts[0]) < 0.01 && Math.Abs(pts[^1]) < 0.01 ? "bend then release" : "bend-release",
            "Prebend" => "pre-bend",
            "PrebendBend" => "pre-bend",
            "PrebendRelease" => "bend-release",
            "Hold" => "hold",
            "Custom" => "custom",
            "" => pts.Count == 0 ? "bend?" : Math.Abs(pts[0]) < 0.01 && Math.Abs(pts[^1]) < 0.01 && pts.Max(Math.Abs) > 0.01 ? "bend then release"
                : Math.Abs(pts[0]) < 0.01 ? "bend" : Math.Abs(pts[^1]) < 0.01 ? "bend-release" : "hold?",
            _ => "other:" + n.BendTypeName,
        };
        return cls + " [" + shape + (n.BendStyleName.Length > 0 ? " " + n.BendStyleName : "") + "]";
    }

    private static string VibFlag(TrackModel track, int bar, int cell, int voice, TabNote n)
    {
        if (n.Techniques.Contains("WideVibrato") || n.Techniques.Contains("Vibrato"))
            return "WITH " + Vib(n);
        for (var b = bar; b < track.Measures.Count; b++)
        {
            var m = track.Measures[b];
            var grids = voice == 0 ? new[] { m.Cells } : new[] { m.Voice2Cells };
            foreach (var grid in grids)
                for (var c = (b == bar ? cell + 1 : 0); c < grid.Count; c++)
                {
                    var hit = grid[c].Notes.FirstOrDefault(x => x.StringIndex == n.StringIndex && (x.Techniques.Contains("Vibrato") || x.Techniques.Contains("WideVibrato")));
                    if (hit is not null) return "FOLLOWED(" + (b + 1) + "." + c + ")";
                    if (grid[c].Notes.Any(x => x.StringIndex == n.StringIndex)) return "-";
                }
            if (b > bar + 1) break;
        }
        return "-";
    }

    private static string Sorted(HashSet<string> t) => string.Join(",", t.OrderBy(x => x, StringComparer.Ordinal));
    private static string F(double d) => d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    private static string Esc(string? s) => (s ?? "").Replace("|", "/").Replace("\r", " ").Replace("\n", " ").Trim();
}
