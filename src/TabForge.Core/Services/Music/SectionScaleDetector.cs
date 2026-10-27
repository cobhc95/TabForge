using TabForge.Models;
using TabForge.Presenters;

namespace TabForge.Services;

/// <summary>One scale (root pitch class and scale name) and how likely it is the one a passage is in.</summary>
/// <param name="Root">The root pitch class, 0 (C) to 11 (B).</param>
/// <param name="Scale">The scale name in the library (MusicTheoryService.Scales).</param>
/// <param name="Confidence">0 to 1: the share of the total likelihood over every candidate that was ranked.</param>
/// <param name="Score">The raw score (higher is better).</param>
public sealed record ScaleMatch(int Root, string Scale, double Confidence, double Score)
{
    /// <summary>The "Root Scale" value the fretboard scale highlight stores, e.g. "A Natural Minor".</summary>
    public string Highlight => $"{MusicTheoryService.NoteNames[Root]} {Scale}";

    /// <summary>The wording of the readout, e.g. "A minor".</summary>
    public string Display(bool flats = false, bool musical = false) => ScaleFamilies.Display(Root, Scale, flats, musical);
}

/// <summary>The pitch classes of a passage: how much each sounds (length times velocity), which came first and last.</summary>
public sealed class PitchHistogram
{
    public double[] Weights { get; } = new double[12];
    public int? First { get; set; }
    public int? Last { get; set; }
    public int NoteCount { get; set; }
    public double Total => Weights.Sum();

    public void Add(int pitch, double weight)
    {
        var pc = ((pitch % 12) + 12) % 12;
        Weights[pc] += weight;
        First ??= pc;
        Last = pc;
        NoteCount++;
    }
}

// Owns: the pitch-class histogram of a section for chosen tracks (tab notes and MIDI clip notes, each weighted by length times velocity, tie continuations counted once)
//   and the ranking of the enabled scales by fit: in-scale share, a penalty for out-of-scale notes and unplayed scale notes, a bonus for tonic and fifth weight and for a
//   first or last note on the root, and (when asked) a popularity prior that breaks close fits.
// Does not own: which section or tracks are read (ScaleReadoutEngine), the cache, the picks, or the settings (ScaleReadoutSettings).
// Tests: TabForge.Avalonia.Tests.SectionScaleDetectorTests.
// Threads: any thread (reads the song; the caller keeps it still).
public static class SectionScaleDetector
{
    private const double OutOfScalePenalty = 230;   // points per share of the weight outside the scale (a full fit is worth 100, so outside costs 130 plus the 100 not earned)
    private const double UnplayedPenalty = 1.0;     // per scale note that never sounds: a bigger scale fits anything
    private const double TonicBonus = 14, FifthBonus = 6, LastNoteBonus = 3, FirstNoteBonus = 1.5;
    private const double Temperature = 5;           // score points per e-fold of likelihood

    /// <summary>Collects the pitch classes of <paramref name="tracks"/> between <paramref name="firstBar"/> and <paramref name="lastBar"/> (inclusive). Drum tracks are skipped.</summary>
    public static PitchHistogram Collect(SongProject song, IEnumerable<TrackModel> tracks, int firstBar, int lastBar)
    {
        var histogram = new PitchHistogram();
        var list = tracks.Where(t => !t.IsDrumTrack).ToList();
        foreach (var track in list) CollectTabNotes(track, firstBar, lastBar, histogram);
        if (list.Any(t => t.AudioClips.Any(c => c.IsMidi))) CollectClipNotes(song, list, firstBar, lastBar, histogram);
        return histogram;
    }

    private static void CollectTabNotes(TrackModel track, int firstBar, int lastBar, PitchHistogram histogram)
    {
        for (var bar = Math.Max(0, firstBar); bar <= lastBar && bar < track.Measures.Count; bar++)
        {
            var measure = track.Measures[bar];
            AddCells(track, measure.Cells, histogram);
            AddCells(track, measure.Voice2Cells, histogram);
        }
    }

    private static void AddCells(TrackModel track, List<TabCell> cells, PitchHistogram histogram)
    {
        foreach (var cell in cells)
        {
            if (cell.IsRest || cell.IsGrace || cell.IsTied || cell.Notes.Count == 0) continue;
            var length = 1.0 / Math.Max(1, cell.DurationDenominator) * (cell.Dots == 2 ? 1.75 : cell.Dots == 1 ? 1.5 : 1);
            if (cell.Tuplet is { Numerator: > 0, Denominator: > 0 } tuplet) length *= tuplet.Denominator / (double)tuplet.Numerator;
            foreach (var note in cell.Notes)
            {
                if (note.Dead || note.Tied || note.IsGraceNote) continue;
                var pitch = note.StringIndex >= 0 && note.StringIndex < track.StringTunings.Count ? track.PitchOf(note.StringIndex, note.Fret) : note.MidiValue > 0 ? note.MidiValue : -1;
                if (pitch < 0) continue;
                histogram.Add(pitch, length * Math.Clamp(note.Velocity, 1, 127) / 127.0 * (note.Ghost ? 0.5 : 1));
            }
        }
    }

    // MIDI clip notes whose onset falls inside the bars; their lengths are turned into whole notes at the section tempo so they weigh like tab notes.
    private static void CollectClipNotes(SongProject song, List<TrackModel> tracks, int firstBar, int lastBar, PitchHistogram histogram)
    {
        var from = AudioClipTimelineRules.SecondsAtBar(song, Math.Max(0, firstBar), 0);
        var to = AudioClipTimelineRules.SecondsAtBar(song, lastBar + 1, 0);
        var wholeNoteSec = 240.0 / Math.Max(20, MusicTime.TempoAt(song, Math.Max(0, firstBar)));
        foreach (var track in tracks)
            foreach (var clip in track.AudioClips)
            {
                if (clip.Notes is not { } notes || clip.Muted) continue;
                var speed = Math.Clamp(clip.Speed, 0.25, 4);
                foreach (var note in notes.OrderBy(n => n.StartSec))
                {
                    if (note.Muted || note.StartSec < clip.OffsetSec) continue;
                    var onset = clip.StartSec + (note.StartSec - clip.OffsetSec) / speed;
                    if (onset < from - 1e-6 || onset >= to - 1e-6) continue;
                    histogram.Add(note.Pitch, Math.Max(0.0625, note.LengthSec / speed / wholeNoteSec) * Math.Clamp(note.Velocity, 1, 127) / 127.0);
                }
            }
    }

    /// <summary>Ranks every root of the given scales for the passage, best first; empty when the passage has no weight. <see cref="ScaleMatch.Confidence"/> sums to 1 over the list.</summary>
    public static IReadOnlyList<ScaleMatch> Rank(PitchHistogram histogram, IEnumerable<string> scales, bool preferCommon)
    {
        var total = histogram.Total;
        var scored = new List<(int Root, string Scale, double Score)>();
        if (total <= 0) return Array.Empty<ScaleMatch>();
        foreach (var name in scales)
        {
            var canonical = MusicTheoryService.Scales.Keys.FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (canonical is null) continue;
            var intervals = MusicTheoryService.Scales[canonical];
            for (var root = 0; root < 12; root++)
            {
                var inside = 0.0;
                var unplayed = 0;
                foreach (var interval in intervals)
                {
                    var weight = histogram.Weights[(root + interval) % 12];
                    inside += weight;
                    if (weight <= 0) unplayed++;
                }
                var fit = inside / total;
                var score = 100 - OutOfScalePenalty * (1 - fit) - UnplayedPenalty * unplayed
                    + TonicBonus * histogram.Weights[root] / total + FifthBonus * histogram.Weights[(root + 7) % 12] / total
                    + (histogram.Last == root ? LastNoteBonus : 0) + (histogram.First == root ? FirstNoteBonus : 0)
                    + (preferCommon ? ScaleFamilies.Prior(canonical) : 0);
                scored.Add((root, canonical, score));
            }
        }
        if (scored.Count == 0) return Array.Empty<ScaleMatch>();
        var best = scored.Max(s => s.Score);
        var sum = scored.Sum(s => Math.Exp((s.Score - best) / Temperature));
        return scored.OrderByDescending(s => s.Score).ThenBy(s => s.Root).ThenBy(s => s.Scale, StringComparer.Ordinal)
            .Select(s => new ScaleMatch(s.Root, s.Scale, Math.Exp((s.Score - best) / Temperature) / sum, s.Score)).ToList();
    }
}
