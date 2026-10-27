using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the TabForge half of the TuxGuitar parity audit: dumps every Guitar Pro file of a folder as one JSON line per file, in the same schema as
//   the TuxGuitar dumper (TgDump.java), so compare.py can diff the two. Runs only when TABFORGE_PARITY_DIR is set (it is not a regression test).
// Does not own: the TuxGuitar side or the comparison. The import runs in this process (GuitarProImporter, which ImportWorker also calls inside its
//   child process); the worker exe is the app, not the test host, so it is not used here.
// Env: TABFORGE_PARITY_DIR = samples folder (scanned fresh, recursively); TABFORGE_PARITY_OUT = output .jsonl (default: tabforge.jsonl beside the test dll).
public class ImportParityDump
{
    private static readonly string[] Extensions = { ".gp3", ".gp4", ".gp5", ".gpx", ".gp", ".gtp" };
    private static readonly TimeSpan PerFileTimeout = TimeSpan.FromSeconds(60);

    public ImportParityDump() => AlphaTabWarmUp.EnsureReady();

    [Fact]
    public void DumpEveryFile()
    {
        var dir = Environment.GetEnvironmentVariable("TABFORGE_PARITY_DIR");
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;   // skipped: not a parity run
        var outPath = Environment.GetEnvironmentVariable("TABFORGE_PARITY_OUT") ?? Path.Combine(AppContext.BaseDirectory, "tabforge.jsonl");
        var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal).ToList();
        using var writer = new StreamWriter(outPath, false, new System.Text.UTF8Encoding(false));
        foreach (var file in files)
            writer.WriteLine(DumpOne(file, Path.GetRelativePath(dir, file).Replace("\\", "/")));
    }

    /// <summary>One file's JSON line; a failed or timed-out import yields ok=false with the error text.</summary>
    private static string DumpOne(string path, string name)
    {
        var clock = Stopwatch.StartNew();
        SongProject? project = null;
        string? error = null;
        var task = Task.Run(() =>
        {
            try { project = GuitarProImporter.Import(path, new ImportContext()); }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; }
        });
        if (!task.Wait(PerFileTimeout)) error = "timeout";
        var ms = clock.ElapsedMilliseconds;

        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("file", name);
            w.WriteBoolean("ok", error is null && project is not null);
            if (error is not null) w.WriteString("error", error);
            w.WriteNumber("ms", ms);
            if (error is null && project is not null)
            {
                try { WriteSong(w, project); }
                catch (Exception ex) { w.WriteString("dumpError", ex.Message); }
            }
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteSong(Utf8JsonWriter w, SongProject p)
    {
        var tracks = p.Tracks.Where(t => t.HasNotation).ToList();
        w.WriteString("title", p.Title);
        w.WriteString("artist", p.Artist);
        w.WriteStartArray("tracks");
        foreach (var t in tracks)
        {
            w.WriteStartObject();
            w.WriteString("name", t.Name);
            w.WriteStartArray("strings"); foreach (var s in t.StringTunings) w.WriteNumberValue(s); w.WriteEndArray();
            w.WriteNumber("capo", t.Capo);
            w.WriteBoolean("isPercussion", t.Kind == TrackKind.Drums);
            w.WriteNumber("channel", t.MidiChannel);
            w.WriteNumber("program", t.MidiProgram);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        // Effective time signature of each bar exactly as the red-bar check reads it (ScoreLayoutEngine.BarStateFor).
        var bars = p.MasterBarTrack?.Measures ?? new List<MeasureModel>();
        var meters = bars.Select(b => (Num: b.TimeSigNum ?? p.TimeSignatureNumerator, Den: b.TimeSigDenom ?? p.TimeSignatureDenominator)).ToList();
        w.WriteStartArray("measures");
        var tempo = p.Tempo;
        for (var i = 0; i < bars.Count; i++)
        {
            var b = bars[i];
            if (b.TempoChange is { } change) tempo = change;
            w.WriteStartObject();
            w.WriteNumber("num", meters[i].Num);
            w.WriteNumber("den", meters[i].Den);
            w.WriteNumber("tempo", tempo);   // the tempo in effect at the bar's START; mid-bar changes are in tempoEvents (tick > 0) and tempoLast
            if (b.MidBarTempos is { Count: > 0 } mid) { w.WriteNumber("tempoLast", mid[^1].Tempo); tempo = mid[^1].Tempo; }
            else w.WriteNumber("tempoLast", tempo);
            w.WriteBoolean("repeatOpen", b.RepeatStart);
            w.WriteNumber("repeatClose", b.RepeatEnd ? b.RepeatCount : 0);
            w.WriteNumber("alternate", b.EndingPasses);
            w.WriteString("marker", p.Markers.FirstOrDefault(m => m.MeasureIndex == i)?.Title ?? "");
            w.WriteNumber("tripletFeel", b.TripletFeelKind == TripletFeels.Eighth ? 1 : b.TripletFeelKind == TripletFeels.Sixteenth ? 2 : 0);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartArray("tracksMeasures");
        var flagged = new List<(List<int> All, List<int> Over)>();
        foreach (var t in tracks)
        {
            var all = new List<int>(); var over = new List<int>();
            w.WriteStartArray();
            for (var i = 0; i < t.Measures.Count; i++)
            {
                var m = t.Measures[i];
                w.WriteStartObject();
                w.WriteStartArray("voices");
                WriteVoice(w, m, 1);
                WriteVoice(w, m, 2);
                w.WriteEndArray();
                w.WriteNumber("tiesIn", m.Cells.Concat(m.Voice2Cells).Sum(c => c.Notes.Count(n => n.Tied)));
                WriteEffects(w, m);
                w.WriteEndObject();
                if (i < meters.Count)
                {
                    var status = BarTiming.Status(m, meters[i].Num, meters[i].Den);
                    if (status != BarTimingStatus.Complete) all.Add(i);
                    if (status == BarTimingStatus.Overfull) over.Add(i);
                }
            }
            w.WriteEndArray();
            flagged.Add((all, over));
        }
        w.WriteEndArray();
        w.WriteStartArray("flaggedBars");
        foreach (var f in flagged) { w.WriteStartArray(); foreach (var i in f.All) w.WriteNumberValue(i); w.WriteEndArray(); }
        w.WriteEndArray();
        w.WriteStartArray("overfullBars");
        foreach (var f in flagged) { w.WriteStartArray(); foreach (var i in f.Over) w.WriteNumberValue(i); w.WriteEndArray(); }
        w.WriteEndArray();
        WriteExtra(w, p, tracks, bars);
    }

    private const double TicksPerSlot = 240;   // a sixteenth slot at 960 ticks per quarter

    /// <summary>Per-bar effect counts (notes carrying each effect, grace notes left out) and velocity [min, max, sum]; omitted when the bar has no notes.</summary>
    private static void WriteEffects(Utf8JsonWriter w, MeasureModel m)
    {
        var fx = new SortedDictionary<string, int>(StringComparer.Ordinal);
        void Inc(string k) => fx[k] = fx.GetValueOrDefault(k) + 1;
        int vmin = int.MaxValue, vmax = int.MinValue; long vsum = 0;
        foreach (var c in m.Cells.Concat(m.Voice2Cells))
        {
            foreach (var n in c.Notes)
            {
                if (n.IsGraceNote) continue;
                var t = n.Techniques;
                vmin = Math.Min(vmin, n.Velocity); vmax = Math.Max(vmax, n.Velocity); vsum += n.Velocity;
                if (n.BendPoints.Count > 0 || t.Contains("Bend")) Inc("bend");
                if (t.Contains("TremBar")) Inc("tremBar");
                if (t.Contains("Slide") || t.Contains("LegatoSlide") || t.Contains("ShiftSlide")) Inc("slide");
                if (t.Contains("SlideInBelow") || t.Contains("SlideInAbove") || t.Contains("SlideOutUp") || t.Contains("SlideOutDown") || t.Contains("PickSlideUp") || t.Contains("PickSlideDown")) Inc("slideIO");
                if (t.Contains("HOPOOrigin")) Inc("hopo");
                if (t.Contains("PalmMute")) Inc("palm");
                if (t.Contains("LetRing")) Inc("ring");
                if (t.Contains("Vibrato") || t.Contains("WideVibrato")) Inc("vib");
                if (t.Contains("TremoloPick")) Inc("tremPick");
                // The cell's flags count per note, as TuxGuitar counts them (counting once per cell made accent and staccato worse, 4276 to 9932).
                if (c.Accent == 1) Inc("accent");
                if (c.Accent == 2) Inc("heavyAccent");
                if (c.Staccato) Inc("staccato");
                if (c.Tenuto) Inc("tenuto");
                if (n.Dead || t.Contains("Dead")) Inc("dead");
                if (n.Ghost || t.Contains("Ghost")) Inc("ghost");
                if (t.Contains("Tapping")) Inc("tap");
                if (t.Contains("Slap")) Inc("slap");
                if (t.Contains("Pop")) Inc("pop");
                if (t.Contains("FadeIn")) Inc("fadeIn");
                if (t.Contains("FadeOut")) Inc("fadeOut");
                if (t.Contains("Trill")) Inc("trill");
                if (t.Contains("PinchHarmonic")) Inc("harmPinch");
                else if (t.Contains("TapHarmonic")) Inc("harmTap");
                else if (t.Contains("SemiHarmonic")) Inc("harmSemi");
                else if (t.Contains("FeedbackHarmonic")) Inc("harmOther");
                else if (t.Contains("ArtificialHarmonic")) Inc("harmArt");
                else if (t.Contains("Harmonic")) Inc("harmNat");
            }
        }
        if (fx.Count > 0)
        {
            w.WriteStartObject("fx");
            foreach (var kv in fx) w.WriteNumber(kv.Key, kv.Value);
            w.WriteEndObject();
        }
        if (vmin != int.MaxValue)
        {
            w.WriteStartArray("vel"); w.WriteNumberValue(vmin); w.WriteNumberValue(vmax); w.WriteNumberValue(vsum); w.WriteEndArray();
        }
    }

    private static int To127(int gp16) => (int)Math.Round(gp16 * 127.0 / 16.0);

    /// <summary>TGP2 additions: capabilities, tempo events (bar, tick in bar, BPM, ramp ticks), key signatures, track mix start values, mix automation and the performed bar order.</summary>
    private static void WriteExtra(Utf8JsonWriter w, SongProject p, List<TrackModel> tracks, List<MeasureModel> bars)
    {
        w.WriteStartObject("caps");
        w.WriteBoolean("tempoMidBar", true); w.WriteBoolean("tempoRamp", true); w.WriteBoolean("mixAutomation", true);
        w.WriteBoolean("slideKind", true); w.WriteBoolean("playOrder", true); w.WriteBoolean("directions", true);
        w.WriteEndObject();

        w.WriteStartArray("tempoEvents");
        var running = -1;
        for (var i = 0; i < bars.Count; i++)
        {
            var start = bars[i].TempoChange ?? (i == 0 ? p.Tempo : running);
            if (i == 0 || start != running)
            {
                w.WriteStartArray(); w.WriteNumberValue(i); w.WriteNumberValue(0); w.WriteNumberValue(start); w.WriteNumberValue(0); w.WriteEndArray();
            }
            running = start;
            if (bars[i].MidBarTempos is { Count: > 0 } points)
                foreach (var pt in points)
                {
                    w.WriteStartArray(); w.WriteNumberValue(i); w.WriteNumberValue(Math.Round(pt.Slot * TicksPerSlot, 1)); w.WriteNumberValue(pt.Tempo);
                    w.WriteNumberValue(Math.Round(pt.RampSlots * TicksPerSlot, 1)); w.WriteEndArray();
                    running = pt.Tempo;
                }
        }
        w.WriteEndArray();

        w.WriteStartArray("keySigs");
        foreach (var b in bars) w.WriteNumberValue(b.KeySignature ?? p.KeySignature);
        w.WriteEndArray();

        w.WriteStartArray("trackMix");
        foreach (var t in tracks)
        {
            w.WriteStartObject();
            w.WriteNumber("program", t.MidiProgram); w.WriteNumber("volume", t.Volume); w.WriteNumber("pan", t.Pan);
            w.WriteNumber("chorus", t.Chorus); w.WriteNumber("reverb", t.Reverb);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        // Mix automation: [track, bar, tick in bar, {program, volume, pan, chorus, reverb, tempo, transitionBeats}], volume/pan/chorus/reverb on 0..127.
        w.WriteStartArray("mixEvents");
        for (var ti = 0; ti < tracks.Count; ti++)
            for (var bi = 0; bi < tracks[ti].Measures.Count; bi++)
            {
                var cells = tracks[ti].Measures[bi].Cells;
                for (var ci = 0; ci < cells.Count; ci++)
                {
                    if (cells[ci].Mix is not { IsEmpty: false } mx) continue;
                    w.WriteStartArray(); w.WriteNumberValue(ti); w.WriteNumberValue(bi); w.WriteNumberValue(Math.Round((cells[ci].RhythmicPosition ?? ci) * TicksPerSlot, 1));
                    w.WriteStartObject();
                    if (mx.Program is { } pr) w.WriteNumber("program", pr);
                    if (mx.Volume is { } v) w.WriteNumber("volume", To127(v));
                    if (mx.Pan is { } pa) w.WriteNumber("pan", To127(pa + 8));
                    if (mx.Chorus is { } ch) w.WriteNumber("chorus", To127(ch));
                    if (mx.Reverb is { } re) w.WriteNumber("reverb", To127(re));
                    if (mx.Tempo is { } te) w.WriteNumber("tempo", te);
                    if (mx.TransitionBeats > 0) w.WriteNumber("transitionBeats", mx.TransitionBeats);
                    w.WriteEndObject(); w.WriteEndArray();
                }
            }
        w.WriteEndArray();

        w.WriteStartArray("directions");   // [bar, text] for bars that carry D.C./D.S./Coda/Fine style directions
        for (var i = 0; i < bars.Count; i++)
            if (!string.IsNullOrEmpty(bars[i].Directions)) { w.WriteStartArray(); w.WriteNumberValue(i); w.WriteStringValue(bars[i].Directions); w.WriteEndArray(); }
        w.WriteEndArray();

        w.WriteStartArray("playOrder");
        try { foreach (var b in PlaybackOrder.Build(p, new PlaybackOptions()).Take(20000)) w.WriteNumberValue(b); }
        catch (Exception) { /* left short: compare reports the length mismatch */ }
        w.WriteEndArray();
    }

    /// <summary>One voice of a bar: beats that take time, their notes and rests, the exact tick sum (BarTiming, 960 per quarter) and the grace notes.</summary>
    private static void WriteVoice(Utf8JsonWriter w, MeasureModel m, int voice)
    {
        var cells = voice >= 2 ? m.Voice2Cells : m.Cells;
        int beats = 0, notes = 0, rests = 0, graces = 0;
        foreach (var c in cells)
        {
            graces += c.Notes.Count(n => n.IsGraceNote);
            var graceOnly = c.IsGrace && c.Notes.Count > 0 && c.Notes.TrueForAll(n => n.IsGraceNote);
            if (graceOnly || !(c.Notes.Count > 0 || c.IsRest || c.IsTied || c.HasAnnotation)) continue;
            beats++;
            notes += c.Notes.Count(n => !n.IsGraceNote);
            if (c.Notes.Count == 0 && c.IsRest) rests++;
        }
        w.WriteStartObject();
        w.WriteNumber("beats", beats);
        w.WriteNumber("notes", notes);
        w.WriteNumber("rests", rests);
        w.WriteNumber("ticks", Math.Round(BarTiming.VoiceUsedTicks(m, voice).Value, 3));
        w.WriteNumber("graces", graces);
        w.WriteEndObject();
    }
}
