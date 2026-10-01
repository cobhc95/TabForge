using System.Globalization;
using System.Text;

namespace TabForge.Diagnostics;

// Report model of `--audio-audit` (serialised as audio-report.json; rendered as audio-report.md).

public sealed record AuditTrackInfo(int Index, string Name, string Kind, int Program, string AttackClass, bool InMaster, string StemFile, int NoteOns);

public sealed record TimingTrackResult(string Track, string AttackClass, int Expected, int Detected, int Matched, int Missing, int Extra,
    double MedianErrorMs, double MeanAbsErrorMs, double P95AbsErrorMs, double MaxAbsErrorMs, int Within10Ms, int Within20Ms, int Within50Ms);

public sealed record TimingBarResult(int Bar, string Track, int Expected, int Missing, int Extra, int Late30Ms, double MaxAbsErrorMs);

public sealed record ArtifactFileResult(string File, double PeakDbfs, long ClippedSamples, int ClipRegions, double DcLeft, double DcRight, double MaxBlockDc,
    int Clicks, double WorstClickJump, double TailRmsDbfs, double DecayToMinus70Sec, int SilentNotes, int ExpectedNotes);

public sealed record ArtifactEvent(string File, string Kind, double TimeSec, int Bar, double Value, string Detail);

public sealed record SectionLevel(string Section, int FirstBar, int LastBar, double StartSec, double EndSec, double MixLufsish, double MixRmsDbfs, double MixPeakDbfs);

public sealed record TrackSectionLevel(string Section, string Track, int Notes, double RmsDbfs, double ActiveRmsDbfs, double PeakDbfs, double RelativeToMixDb, string Flag);

public sealed record DrumSectionStats(string Section, int Hits, int VelMin, int VelMax, double VelMean, double VelStdDev, int DistinctVelocities,
    double PeakDbStdDevWithinPiece, int IsolatedHits);

public sealed record DrumPieceStats(int Note, int Hits, double VelStdDev, int DistinctVelocities, int LongestSameVelocityRun);

public sealed record HumanisationResult(string Track, List<DrumSectionStats> Sections, List<DrumPieceStats> Pieces, double VelocityToPeakCorrelation, int CorrelationSamples, List<string> Flags);

public sealed record ConsistencyResult(double PlaybackTotalMs, double MidiFileTotalMs, double RenderTotalMs, double RenderFileSeconds,
    Dictionary<string, int> NotesPlayback, Dictionary<string, int> NotesMidiFile, Dictionary<string, int> NotesRender,
    int Bars, double MaxBarStartErrorMidiMs, double MaxBarStartErrorTempoMapMs, double MaxNoteTimeErrorMidiMs, int NoteTimeErrorsOver5Ms, List<string> Issues);

public sealed record ListenMoment(double TimeSec, string Clock, int Bar, string Track, string Reason, double Severity);

public sealed class AuditReport
{
    public string Song { get; set; } = "";
    public string Generated { get; set; } = "";
    public int SampleRate { get; set; }
    public double SongSeconds { get; set; }
    public double RenderSeconds { get; set; }
    public double RenderElapsedSeconds { get; set; }
    public List<AuditTrackInfo> Tracks { get; set; } = new();
    public List<TimingTrackResult> Timing { get; set; } = new();
    public List<TimingBarResult> WorstBars { get; set; } = new();
    public List<ArtifactFileResult> Artifacts { get; set; } = new();
    public List<ArtifactEvent> ArtifactEvents { get; set; } = new();
    public List<SectionLevel> Sections { get; set; } = new();
    public List<TrackSectionLevel> TrackLevels { get; set; } = new();
    public string DynamicsArc { get; set; } = "";
    public double DynamicsRangeLu { get; set; }
    public List<HumanisationResult> Humanisation { get; set; } = new();
    public ConsistencyResult? Consistency { get; set; }
    public List<ListenMoment> MomentsToListenTo { get; set; } = new();
    public List<string> Problems { get; set; } = new();
    public List<string> Notes { get; set; } = new();

    internal static string Clock(double sec) => $"{(int)(sec / 60)}:{sec % 60:00.00}";

    public string ToMarkdown()
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        string F(double v, string f = "0.0") => v.ToString(f, c);
        sb.AppendLine($"# Audio audit: {Song}").AppendLine();
        sb.AppendLine($"Generated {Generated}. {SampleRate} Hz, song {F(SongSeconds)} s, rendered {F(RenderSeconds)} s of audio in {F(RenderElapsedSeconds)} s (offline, built-in General MIDI synth, no audio device).").AppendLine();
        sb.AppendLine("## Summary").AppendLine();
        if (Problems.Count == 0) sb.AppendLine("No problems flagged."); else foreach (var p in Problems) sb.AppendLine("- " + p);
        foreach (var n in Notes) sb.AppendLine("- note: " + n);
        sb.AppendLine().AppendLine("## Moments to listen to").AppendLine();
        sb.AppendLine("| Time | Bar | Track | Why |").AppendLine("|---|---|---|---|");
        foreach (var m in MomentsToListenTo) sb.AppendLine($"| {m.Clock} | {m.Bar} | {m.Track} | {m.Reason} |");
        sb.AppendLine().AppendLine("## Timing (detected onsets in each stem against the compiled timeline)").AppendLine();
        sb.AppendLine("Error = detected minus expected (positive = late). Sharp tracks are matched within 50 ms, slow-attack tracks (strings, pads, organ, choir) within 200 ms and reported separately.").AppendLine();
        sb.AppendLine("| Track | Class | Expected | Detected | Missing | Extra | Median ms | Mean abs ms | P95 abs ms | Max abs ms | within 10 / 20 / 50 ms |").AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var t in Timing)
            sb.AppendLine($"| {t.Track} | {t.AttackClass} | {t.Expected} | {t.Detected} | {t.Missing} | {t.Extra} | {F(t.MedianErrorMs)} | {F(t.MeanAbsErrorMs)} | {F(t.P95AbsErrorMs)} | {F(t.MaxAbsErrorMs)} | {t.Within10Ms} / {t.Within20Ms} / {t.Within50Ms} |");
        if (WorstBars.Count > 0)
        {
            sb.AppendLine().AppendLine("Worst bars (sharp tracks):").AppendLine();
            sb.AppendLine("| Bar | Track | Expected | Missing | Extra | Late over 30 ms | Max abs ms |").AppendLine("|---|---|---|---|---|---|---|");
            foreach (var b in WorstBars) sb.AppendLine($"| {b.Bar} | {b.Track} | {b.Expected} | {b.Missing} | {b.Extra} | {b.Late30Ms} | {F(b.MaxAbsErrorMs)} |");
        }
        sb.AppendLine().AppendLine("## Artifacts").AppendLine();
        sb.AppendLine("Clipping = samples at or above -0.1 dBFS; click = sample jump more than 8 times the local jump level (away from note attacks); tail = RMS after the last note-off plus 1.5 s; silent = expected note with nothing audible in its first 45 ms.").AppendLine();
        sb.AppendLine("| File | Peak dBFS | Clipped samples | Clip regions | DC L / R | Clicks | Tail RMS dBFS | Decay to -70 dB (s) | Silent notes |").AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var a in Artifacts)
            sb.AppendLine($"| {a.File} | {F(a.PeakDbfs)} | {a.ClippedSamples} | {a.ClipRegions} | {F(a.DcLeft, "0.0000")} / {F(a.DcRight, "0.0000")} | {a.Clicks} | {F(a.TailRmsDbfs)} | {F(a.DecayToMinus70Sec, "0.00")} | {a.SilentNotes} of {a.ExpectedNotes} |");
        sb.AppendLine().AppendLine("## Levels").AppendLine();
        sb.AppendLine($"Dynamics arc (mix, LUFS-ish per section, range {F(DynamicsRangeLu)} LU):").AppendLine().AppendLine("```").AppendLine(DynamicsArc).AppendLine("```").AppendLine();
        sb.AppendLine("| Section | Bars | Time | Mix LUFS-ish | Mix RMS dBFS | Mix peak dBFS |").AppendLine("|---|---|---|---|---|---|");
        foreach (var s in Sections) sb.AppendLine($"| {s.Section} | {s.FirstBar}-{s.LastBar} | {Clock(s.StartSec)} | {F(s.MixLufsish)} | {F(s.MixRmsDbfs)} | {F(s.MixPeakDbfs)} |");
        var flagged = TrackLevels.Where(t => t.Flag.Length > 0).ToList();
        sb.AppendLine().AppendLine(flagged.Count == 0 ? "No track is inaudible relative to the mix in any section." : "Tracks flagged against the mix:").AppendLine();
        foreach (var t in flagged) sb.AppendLine($"- {t.Section} / {t.Track}: {t.Flag} (active RMS {F(t.ActiveRmsDbfs)} dBFS, {F(t.RelativeToMixDb)} dB against the mix, {t.Notes} notes)");
        sb.AppendLine().AppendLine("## Humanisation (drums)").AppendLine();
        foreach (var h in Humanisation)
        {
            sb.AppendLine($"Track {h.Track}: velocity to stem-peak correlation {F(h.VelocityToPeakCorrelation, "0.00")} over {h.CorrelationSamples} isolated hits.").AppendLine();
            sb.AppendLine("| Section | Hits | Velocity min-max | Mean | Std dev | Distinct | Peak dB std dev (same piece) |").AppendLine("|---|---|---|---|---|---|---|");
            foreach (var s in h.Sections) sb.AppendLine($"| {s.Section} | {s.Hits} | {s.VelMin}-{s.VelMax} | {F(s.VelMean)} | {F(s.VelStdDev)} | {s.DistinctVelocities} | {F(s.PeakDbStdDevWithinPiece)} ({s.IsolatedHits} isolated) |");
            sb.AppendLine().AppendLine("| GM note | Hits | Velocity std dev | Distinct | Longest same-velocity run |").AppendLine("|---|---|---|---|---|");
            foreach (var p in h.Pieces) sb.AppendLine($"| {p.Note} | {p.Hits} | {F(p.VelStdDev)} | {p.DistinctVelocities} | {p.LongestSameVelocityRun} |");
            foreach (var f in h.Flags) sb.AppendLine().AppendLine("- " + f);
        }
        if (Humanisation.Count == 0) sb.AppendLine("No drum track.");
        sb.AppendLine().AppendLine("## Consistency (playback compile, MIDI export, offline render)").AppendLine();
        if (Consistency is { } k)
        {
            sb.AppendLine($"- Total duration: playback {F(k.PlaybackTotalMs)} ms, MIDI file {F(k.MidiFileTotalMs)} ms, render tempo map {F(k.RenderTotalMs)} ms, rendered file {F(k.RenderFileSeconds, "0.000")} s.");
            sb.AppendLine($"- {k.Bars} performed bars; largest bar-start difference: MIDI file {F(k.MaxBarStartErrorMidiMs, "0.00")} ms, render tempo map {F(k.MaxBarStartErrorTempoMapMs, "0.00")} ms.");
            sb.AppendLine($"- Note-on times MIDI file vs playback: largest {F(k.MaxNoteTimeErrorMidiMs, "0.00")} ms, {k.NoteTimeErrorsOver5Ms} over 5 ms.");
            sb.AppendLine("- Note-on counts per track (playback / MIDI file / render compile): " + string.Join(", ", k.NotesPlayback.Keys.Select(n => $"{n} {k.NotesPlayback[n]}/{k.NotesMidiFile.GetValueOrDefault(n)}/{k.NotesRender.GetValueOrDefault(n)}")));
            sb.AppendLine(k.Issues.Count == 0 ? "- All three agree." : "").AppendLine();
            foreach (var i in k.Issues) sb.AppendLine("- MISMATCH: " + i);
        }
        return sb.ToString();
    }
}
