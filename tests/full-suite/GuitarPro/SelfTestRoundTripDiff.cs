using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Rendering;
using TabForge.Services;

namespace TabForge;

// `--roundtrip-diff`: the "playback timeline ... same note events after reopen" self-test check (RtCheckTimelineSame) and the semantic
// round-trip comparison (RtVerify / RtScoreFacts) for ANY song, with every difference listed instead of a pass/fail line. Per song:
//   open -> compile the playback timeline -> clean .gp export -> reopen -> compile -> diff,
//   open -> .tforge save -> reopen -> compile -> diff,
//   open -> MIDI export -> read back (worst bar-start and note-on drift).
// The timeline diff pairs notes by (track, pitch, onset within 1 ms) so one lost note does not shift every later comparison; unpaired notes
// become missing / extra, and are then re-paired as pitch changes (same onset, other pitch) or onset shifts (same pitch, within 300 ms).
public static partial class SelfTest
{
    private sealed record TlDiff(string Format, string Kind, int Track, int Bar, double Ms, string Detail, string Traits);

    /// <summary>`--roundtrip-diff &lt;song | @list.txt&gt; &lt;out.txt&gt; [gp,tforge,midi]` (list lines: path, or id TAB path). Writes the report plus out.txt.diffs.tsv and out.txt.summary.tsv.</summary>
    internal static int RunRoundTripDiff(string input, string reportPath, string what)
    {
        var report = FilePathPolicy.OutputFile(reportPath, "round-trip diff report");
        var modes = (string.IsNullOrWhiteSpace(what) ? "gp,tforge,midi" : what).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.ToLowerInvariant()).ToHashSet();
        var songs = new List<(string Id, string Path)>();
        if (input.StartsWith('@'))
        {
            foreach (var line in File.ReadAllLines(input[1..]))
            {
                var l = line.Trim(); if (l.Length == 0 || l.StartsWith('#')) continue;
                var tab = l.IndexOf('\t');
                songs.Add(tab > 0 ? (l[..tab], l[(tab + 1)..]) : (System.IO.Path.GetFileNameWithoutExtension(l), l));
            }
        }
        else songs.Add((System.IO.Path.GetFileNameWithoutExtension(input), input));

        var folder = RtFolder();
        var failures = 0;
        using var text = new StreamWriter(report, false, new UTF8Encoding(false)) { AutoFlush = true };
        using var diffs = new StreamWriter(report + ".diffs.tsv", false, new UTF8Encoding(false)) { AutoFlush = true };
        using var summary = new StreamWriter(report + ".summary.tsv", false, new UTF8Encoding(false)) { AutoFlush = true };
        diffs.WriteLine("id\tformat\tkind\ttrack\tbar\tms\tdetail\tbarTraits");
        summary.WriteLine("id\tstatus\ttracks\tbars\tnotes\tgp_tl_diffs\tgp_fact_unexpected\tgp_fact_allowlisted\ttforge_tl_diffs\ttforge_fact_unexpected\tmidi_max_bar_ms\tmidi_max_note_ms\tmidi_notes_pb\tmidi_notes_file\tmidi_end_err_ms\tseconds\terror");
        try
        {
            foreach (var (id, path) in songs)
            {
                var watch = Stopwatch.StartNew();
                string status = "ok", error = "";
                int tracks = 0, bars = 0, notes = 0;
                int gpTl = -1, gpUn = -1, gpAl = -1, tfTl = -1, tfUn = -1, midiNotesPb = -1, midiNotesFile = -1;
                double midiBar = double.NaN, midiNote = double.NaN, midiEnd = double.NaN;
                text.WriteLine($"=== {id}");
                try
                {
                    var song = path.Equals("@technique", StringComparison.OrdinalIgnoreCase) ? GmSongAudit.TechniqueSong()
                        : System.IO.Path.GetExtension(path).Equals(".tforge", StringComparison.OrdinalIgnoreCase) ? ProjectService.Load(path) : GuitarProImporter.Import(path);
                    tracks = song.Tracks.Count; bars = song.Tracks.Count == 0 ? 0 : song.Tracks.Max(t => t.Measures.Count);
                    var original = RenderSpecBuilder.Compile(song);
                    notes = original.Notes.Count;
                    text.WriteLine($"  imported: {tracks} tracks, {bars} bars, {notes} timeline notes, {original.TotalMs / 1000.0:0.0} s, timeline hash {RtTimelineHash(original)}");

                    if (modes.Contains("gp"))
                        try
                        {
                            var gpPath = System.IO.Path.Combine(folder, "rt-clean.gp");
                            GuitarProExporter.Save(song, gpPath, embedProject: false);
                            var back = GuitarProImporter.Import(gpPath);
                            var tl = RtTimelineDiff(".gp", id, song, back, original, exactVelocity: false, diffs);
                            gpTl = tl.Count;
                            text.WriteLine($"  clean .gp: {original.Notes.Count} -> {RenderSpecBuilder.Compile(back).Notes.Count} note events; timeline differences: {tl.Count}{RtKinds(tl)}");
                            foreach (var d in tl.Take(5)) text.WriteLine($"    {d.Kind} track {d.Track} bar {d.Bar + 1} @{d.Ms:0}ms {d.Detail} [{d.Traits}]");
                            (gpUn, gpAl) = RtFactDiff(".gp", id, RtGpClean(), RtBakedTranspose(song), back, text, diffs);
                        }
                        catch (Exception ex) when (ex is not OutOfMemoryException) { status = "error"; error += $"gp: {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}; "; text.WriteLine($"  clean .gp FAILED: {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}"); }

                    if (modes.Contains("tforge"))
                        try
                        {
                            var back = RtViaTforge(song, folder, "rt");
                            var tl = RtTimelineDiff(".tforge", id, song, back, original, exactVelocity: true, diffs);
                            tfTl = tl.Count;
                            text.WriteLine($"  .tforge: {original.Notes.Count} -> {RenderSpecBuilder.Compile(back).Notes.Count} note events; timeline differences: {tl.Count}{RtKinds(tl)}");
                            foreach (var d in tl.Take(5)) text.WriteLine($"    {d.Kind} track {d.Track} bar {d.Bar + 1} @{d.Ms:0}ms {d.Detail} [{d.Traits}]");
                            (tfUn, _) = RtFactDiff(".tforge", id, RtTforge, RtCopy(song), back, text, diffs);
                        }
                        catch (Exception ex) when (ex is not OutOfMemoryException) { status = "error"; error += $"tforge: {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}; "; text.WriteLine($"  .tforge FAILED: {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}"); }

                    if (modes.Contains("midi"))
                        try
                        {
                            var r = MidiTimingAudit.Measure(song, System.IO.Path.Combine(folder, "rt.mid"));
                            midiBar = r.MaxBarStartErrMs; midiNote = r.MaxNoteErrMs; midiNotesPb = r.NoteCountPlayback; midiNotesFile = r.NoteCountFile; midiEnd = r.EndErrMs;
                            text.WriteLine($"  MIDI: {r}; worst note-on: {r.WorstNoteAt}");
                        }
                        catch (Exception ex) when (ex is not OutOfMemoryException) { status = "error"; error += $"midi: {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}; "; text.WriteLine($"  MIDI FAILED: {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}"); }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    status = "open-failed"; error = $"{ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}";
                    text.WriteLine($"  OPEN FAILED: {error}");
                }
                if (status != "ok") failures++;
                else if (gpTl > 0 || tfTl > 0 || gpUn > 0 || tfUn > 0 || midiNotesPb != midiNotesFile) failures++;
                var inv = CultureInfo.InvariantCulture;
                summary.WriteLine(string.Join('\t', id, status, tracks, bars, notes, gpTl, gpUn, gpAl, tfTl, tfUn,
                    midiBar.ToString("0.###", inv), midiNote.ToString("0.###", inv), midiNotesPb, midiNotesFile, midiEnd.ToString("0.###", inv),
                    (watch.ElapsedMilliseconds / 1000.0).ToString("0.0", inv), error.Replace('\t', ' ')));
                GC.Collect();
            }
        }
        finally { RtCleanup(folder); }
        text.WriteLine($"{songs.Count} song(s), {failures} with differences or errors");
        Console.Out.WriteLine($"{songs.Count} song(s), {failures} with differences or errors; report {report}");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>A short hash of the compiled note events (track, pitch, onset, duration, velocity), to tell whether two builds import a song to the same playback.</summary>
    private static string RtTimelineHash(ScoreTimeline timeline)
    {
        var text = string.Join(";", timeline.Notes.OrderBy(n => n.TrackIndex).ThenBy(n => n.OnsetMs).ThenBy(n => n.Midi)
            .Select(n => string.Create(CultureInfo.InvariantCulture, $"{n.TrackIndex},{n.Midi},{n.OnsetMs:0.0},{n.DurationMs:0.0},{n.Velocity}")));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12];
    }

    private static string RtKinds(List<TlDiff> tl) =>
        tl.Count == 0 ? "" : " (" + string.Join(", ", tl.GroupBy(d => d.Kind).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}")) + ")";

    private sealed record TlNote(int Track, int Midi, double Onset, double Dur, int Velocity, int Bar);

    private static List<TlNote> RtTlNotes(ScoreTimeline tl) =>
        tl.Notes.Select(n => new TlNote(n.TrackIndex, n.Midi, n.OnsetMs, n.DurationMs, n.Velocity, n.Bar)).ToList();

    /// <summary>The note events of the original song against the reopened one (see the file comment for how they are paired).</summary>
    private static List<TlDiff> RtTimelineDiff(string format, string id, SongProject original, SongProject reopened, ScoreTimeline originalTimeline, bool exactVelocity, StreamWriter detail)
    {
        var reopenedTimeline = RenderSpecBuilder.Compile(reopened);
        var a = RtTlNotes(originalTimeline); var b = RtTlNotes(reopenedTimeline);
        var result = new List<TlDiff>();
        var missing = new List<TlNote>(); var extra = new List<TlNote>();
        foreach (var group in a.Select(n => (n.Track, n.Midi)).Concat(b.Select(n => (n.Track, n.Midi))).Distinct())
        {
            var ga = a.Where(n => n.Track == group.Track && n.Midi == group.Midi).OrderBy(n => n.Onset).ToList();
            var gb = b.Where(n => n.Track == group.Track && n.Midi == group.Midi).OrderBy(n => n.Onset).ToList();
            int i = 0, j = 0;
            while (i < ga.Count && j < gb.Count)
            {
                var d = gb[j].Onset - ga[i].Onset;
                if (Math.Abs(d) <= 1.0)
                {
                    var x = ga[i]; var y = gb[j];
                    if (Math.Abs(x.Dur - y.Dur) > Math.Max(30, x.Dur * 0.15)) result.Add(new TlDiff(format, "duration", x.Track, x.Bar, x.Onset, $"midi {x.Midi} {x.Dur:0} -> {y.Dur:0} ms", RtBarTraits(original, x.Track, x.Bar)));
                    else if (exactVelocity && x.Velocity != y.Velocity) result.Add(new TlDiff(format, "velocity", x.Track, x.Bar, x.Onset, $"midi {x.Midi} {x.Velocity} -> {y.Velocity}", RtBarTraits(original, x.Track, x.Bar)));
                    i++; j++;
                }
                else if (d > 0) missing.Add(ga[i++]); else extra.Add(gb[j++]);
            }
            while (i < ga.Count) missing.Add(ga[i++]);
            while (j < gb.Count) extra.Add(gb[j++]);
        }
        // pitch change: the same track and onset (within 1 ms) but another pitch
        foreach (var m in missing.OrderBy(n => n.Track).ThenBy(n => n.Onset).ToList())
        {
            var e = extra.FirstOrDefault(x => x.Track == m.Track && Math.Abs(x.Onset - m.Onset) <= 1.0);
            if (e is null) continue;
            missing.Remove(m); extra.Remove(e);
            result.Add(new TlDiff(format, "pitch", m.Track, m.Bar, m.Onset, $"midi {m.Midi} -> {e.Midi}", RtBarTraits(original, m.Track, m.Bar)));
        }
        // onset shift: the same pitch, within 300 ms
        foreach (var m in missing.OrderBy(n => n.Track).ThenBy(n => n.Onset).ToList())
        {
            var e = extra.Where(x => x.Track == m.Track && x.Midi == m.Midi && Math.Abs(x.Onset - m.Onset) <= 300).OrderBy(x => Math.Abs(x.Onset - m.Onset)).FirstOrDefault();
            if (e is null) continue;
            missing.Remove(m); extra.Remove(e);
            result.Add(new TlDiff(format, "onset", m.Track, m.Bar, m.Onset, $"midi {m.Midi} moved {e.Onset - m.Onset:+0.0;-0.0} ms (bar {e.Bar + 1})", RtBarTraits(original, m.Track, m.Bar)));
        }
        foreach (var m in missing) result.Add(new TlDiff(format, "missing", m.Track, m.Bar, m.Onset, $"midi {m.Midi} dur {m.Dur:0} ms", RtBarTraits(original, m.Track, m.Bar)));
        foreach (var e in extra) result.Add(new TlDiff(format, "extra", e.Track, e.Bar, e.Onset, $"midi {e.Midi} dur {e.Dur:0} ms", RtBarTraits(reopened, e.Track, e.Bar)));
        // performed-bar map: the bars played, in order, with their start time, length and tempo (a tempo or repeat difference shows here first)
        var ba = originalTimeline.Bars; var bb = reopenedTimeline.Bars;
        var barDiffs = new List<string>();
        if (ba.Count != bb.Count) barDiffs.Add($"performed bars {ba.Count} -> {bb.Count}");
        for (var k = 0; k < Math.Min(ba.Count, bb.Count) && barDiffs.Count < 6; k++)
        {
            var x = ba[k]; var y = bb[k];
            if (x.Bar != y.Bar || x.Slots != y.Slots || x.Tempo != y.Tempo || Math.Abs(x.StartMs - y.StartMs) > 1.0)
                barDiffs.Add($"performed #{k + 1}: source bar {x.Bar + 1}/{y.Bar + 1}, slots {x.Slots}/{y.Slots}, tempo {x.Tempo}/{y.Tempo}, start {x.StartMs:0.0}/{y.StartMs:0.0} ms");
        }
        foreach (var bd in barDiffs) result.Add(new TlDiff(format, "barmap", -1, -1, 0, bd, ""));
        result = result.OrderBy(d => d.Track).ThenBy(d => d.Ms).ToList();
        foreach (var d in result.Take(400)) detail.WriteLine(string.Join('\t', id, d.Format, d.Kind, d.Track, d.Bar + 1, d.Ms.ToString("0", CultureInfo.InvariantCulture), d.Detail, d.Traits));
        return result;
    }

    /// <summary>What the bar holds that tends to explain a loss: tuplets, ties, grace notes, second voice, repeats, tempo, and every technique name present.</summary>
    private static string RtBarTraits(SongProject p, int track, int bar)
    {
        if (track < 0 || track >= p.Tracks.Count || bar < 0 || bar >= p.Tracks[track].Measures.Count) return "?";
        var m = p.Tracks[track].Measures[bar];
        var t = new SortedSet<string>(StringComparer.Ordinal);
        var cells = m.Cells.Concat(m.Voice2Cells).ToList();
        if (m.Voice2Cells.Any(c => c.Notes.Count > 0)) t.Add("voice2");
        if (m.RepeatStart) t.Add("repeatStart");
        if (m.RepeatEnd) t.Add("repeatEnd");
        if (m.AlternateEnding > 0 || m.AlternateEndingMask != 0) t.Add("ending");
        if (m.TempoChange is not null) t.Add("tempoChange");
        if (m.MidBarTempos is { Count: > 0 }) t.Add("midTempo");
        if (!string.IsNullOrEmpty(m.Directions)) t.Add("directions");
        if (m.TimeSigNum is not null) t.Add("timeSig");
        if (m.FreeTime) t.Add("freeTime");
        if (m.Anacrusis) t.Add("pickup");
        if (!string.IsNullOrEmpty(m.TripletFeelKind) && m.TripletFeelKind != TripletFeels.None) t.Add("tripletFeel");
        foreach (var c in cells)
        {
            if (c.Tuplet.Numerator > 0) t.Add($"tuplet{c.Tuplet.Numerator}:{c.Tuplet.Denominator}");
            if (c.Dots > 0) t.Add("dotted");
            if (c.IsTied) t.Add("cellTie");
            if (c.IsGrace) t.Add("graceCell");
            if (c.Fermata) t.Add("fermata");
            if (c.Staccato) t.Add("staccato");
            if (c.Accent > 0) t.Add("accent");
            if (c.WhammyPoints.Count > 0) t.Add("whammy");
            if (c.SoundDurationPercent != 100) t.Add("soundPct");
            if (c.OctaveShiftSemitones != 0) t.Add("ottava");
            if (c.RhythmicPosition is not null && c.Notes.Count > 0 && Math.Abs(c.RhythmicPosition.Value - Math.Round(c.RhythmicPosition.Value)) > 1e-6) t.Add("offGrid");
            foreach (var n in c.Notes)
            {
                if (n.Tied) t.Add("noteTie");
                if (n.IsGraceNote) t.Add("grace");
                if (n.Dead) t.Add("dead");
                if (n.Ghost) t.Add("ghost");
                if (n.BendPoints.Count > 0) t.Add("bend");
                foreach (var tech in n.Techniques) t.Add(tech);
            }
        }
        return string.Join(",", t);
    }

    /// <summary>The semantic comparison of RtVerify for one format, reported instead of asserted. Returns (unexpected, allow-listed) difference counts.</summary>
    private static (int Unexpected, int Allowed) RtFactDiff(string format, string id, RtProfile profile, SongProject expected, SongProject actual, StreamWriter text, StreamWriter detail)
    {
        var ef = RtScoreFacts(expected, out _, out _); var af = RtScoreFacts(actual, out _, out _);
        var all = RtCompare(ef, af);
        var unexpected = new List<RtDiff>(); var allowed = new List<RtDiff>();
        foreach (var d in all) (profile.ReasonFor(d.Category, d) is null ? unexpected : allowed).Add(d);
        text.WriteLine($"  {format} semantic facts: {unexpected.Count} unexpected, {allowed.Count} on the known-loss allow-list");
        foreach (var g in unexpected.GroupBy(d => d.Category).OrderByDescending(g => g.Count()).Take(8))
            text.WriteLine($"    UNEXPECTED {g.Key} x{g.Count()} e.g. {g.First().Where} expected '{Short(g.First().Expected)}' got '{Short(g.First().Actual)}'");
        foreach (var g in allowed.GroupBy(d => d.Category).OrderByDescending(g => g.Count()).Take(12))
            text.WriteLine($"    allowed    {g.Key} x{g.Count()} e.g. {g.First().Where} expected '{Short(g.First().Expected)}' got '{Short(g.First().Actual)}'");
        foreach (var g in unexpected.GroupBy(d => d.Category))
            detail.WriteLine(string.Join('\t', id, format, "FACT-UNEXPECTED", "", "", "", $"{g.Key} x{g.Count()} e.g. {g.First().Where} expected '{Short(g.First().Expected)}' got '{Short(g.First().Actual)}'", ""));
        foreach (var g in allowed.GroupBy(d => d.Category))
            detail.WriteLine(string.Join('\t', id, format, "FACT-ALLOWED", "", "", "", $"{g.Key} x{g.Count()} e.g. {g.First().Where} expected '{Short(g.First().Expected)}' got '{Short(g.First().Actual)}'", ""));
        return (unexpected.Count, allowed.Count);
        static string Short(string s) => (s.Length > 70 ? s[..70] + "..." : s).Replace('\t', ' ').Replace('\n', ' ');
    }
}
