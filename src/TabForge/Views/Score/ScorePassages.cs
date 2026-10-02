using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views.Score;

/// <summary>Passages and marks derived once per score: palm-mute spans, fade spans and dynamic markings.</summary>
internal static class ScorePassages
{
    internal readonly record struct PalmMuteEvent(
        int Measure, double StartSlots, double EndSlots, double AbsoluteStart, double AbsoluteEnd);

    internal sealed record FadeNoteEvent(
        int Measure, int Cell, double StartSlots, double EndSlots,
        double AbsoluteStart, double AbsoluteEnd, TabNote Note, int Midi, bool IsTied);

    internal static IReadOnlyList<PalmMutePassage> BuildPalmMutePassages(TrackModel track, SongProject project)
    {
        var events = new List<PalmMuteEvent>();
        var measureStart = 0.0;
        for (var measureIndex = 0; measureIndex < track.Measures.Count; measureIndex++)
        {
            var measure = track.Measures[measureIndex];
            var slots = MusicTime.BarSlots(project, measureIndex);
            if (measure.SimileOneBar || measure.SimileTwoBar) { measureStart += slots; continue; }   // a simile bar shows only its sign: the P.M. line ends at the bar line before it
            for (var cellIndex = 0; cellIndex < measure.Cells.Count && cellIndex < slots; cellIndex++)
            {
                var cell = measure.Cells[cellIndex];
                if (!cell.Notes.Any(note => note.Techniques.Any(ScoreMarkText.IsPalmMute))) continue;
                var rawStart = cell.RhythmicPosition ?? cellIndex;
                var start = Math.Clamp(double.IsFinite(rawStart) ? rawStart : cellIndex, 0, slots);
                var end = Math.Min(slots, start + MusicTime.CellSlots(cell));
                events.Add(new PalmMuteEvent(measureIndex, start, end, measureStart + start,
                    measureStart + end));
            }
            measureStart += slots;
        }

        if (events.Count == 0) return Array.Empty<PalmMutePassage>();
        events.Sort((a, b) => a.AbsoluteStart.CompareTo(b.AbsoluteStart));

        // A chord or overlapping voice at the same onset is one annotation event, not multiple marks.
        var onsets = new List<PalmMuteEvent>();
        foreach (var item in events)
        {
            if (onsets.Count > 0 && Math.Abs(onsets[^1].AbsoluteStart - item.AbsoluteStart) < 0.001)
            {
                if (item.AbsoluteEnd > onsets[^1].AbsoluteEnd)
                    onsets[^1] = item;
            }
            else onsets.Add(item);
        }

        var passages = new List<PalmMutePassage>();
        var first = onsets[0];
        var endEvent = first;
        var passageEnd = first.AbsoluteEnd;
        var eventCount = 1;
        void FinishPassage() => passages.Add(new PalmMutePassage(
            first.Measure, first.StartSlots, endEvent.Measure, endEvent.EndSlots, eventCount));

        foreach (var item in onsets.Skip(1))
        {
            if (item.AbsoluteStart > passageEnd + 0.001)
            {
                FinishPassage();
                first = endEvent = item;
                passageEnd = item.AbsoluteEnd;
                eventCount = 1;
                continue;
            }

            if (item.AbsoluteEnd >= passageEnd)
            {
                passageEnd = item.AbsoluteEnd;
                endEvent = item;
            }
            eventCount++;
        }
        FinishPassage();
        return passages;
    }

    /// <summary>
    /// Resolves imported fade marks to musical spans. If the marked note is tied forward, its hairpin
    /// follows the same pitch/string through contiguous tie destinations, including across barlines.
    /// </summary>
    internal static IReadOnlyList<FadePassage> BuildFadePassages(TrackModel track, SongProject project)
    {
        var notes = new List<FadeNoteEvent>();
        var measureStart = 0.0;
        for (var measureIndex = 0; measureIndex < track.Measures.Count; measureIndex++)
        {
            var measure = track.Measures[measureIndex];
            var slots = MusicTime.BarSlots(project, measureIndex);
            for (var cellIndex = 0; cellIndex < measure.Cells.Count && cellIndex < slots; cellIndex++)
            {
                var cell = measure.Cells[cellIndex];
                if (cell.Notes.Count == 0) continue;
                var rawStart = cell.RhythmicPosition ?? cellIndex;
                var start = Math.Clamp(double.IsFinite(rawStart) ? rawStart : cellIndex, 0, slots);
                var end = Math.Min(slots, start + MusicTime.CellSlots(cell));
                foreach (var note in cell.Notes)
                {
                    var midi = note.MidiValue > 0 ? note.MidiValue
                        : note.StringIndex >= 0 && note.StringIndex < track.StringTunings.Count
                            ? track.PitchOf(note.StringIndex, note.Fret) : note.Fret;
                    notes.Add(new FadeNoteEvent(measureIndex, cellIndex, start, end,
                        measureStart + start, measureStart + end, note, midi,
                        cell.IsTied || note.Tied));
                }
            }
            measureStart += slots;
        }

        var passages = new List<FadePassage>();
        foreach (var origin in notes.Where(item => item.Note.Techniques.Contains("FadeIn") ||
                                                    item.Note.Techniques.Contains("FadeOut")))
        {
            var fadeOut = origin.Note.Techniques.Contains("FadeOut");
            var endMeasure = origin.Measure;
            var endSlots = origin.EndSlots;
            var endAbsolute = origin.AbsoluteEnd;
            while (true)
            {
                var destination = notes.Where(item => item.IsTied &&
                        item.AbsoluteStart > origin.AbsoluteStart + 0.001 &&
                        item.Note.StringIndex == origin.Note.StringIndex && item.Midi == origin.Midi &&
                        Math.Abs(item.AbsoluteStart - endAbsolute) < 0.001)
                    .OrderBy(item => item.AbsoluteStart).FirstOrDefault();
                if (destination is null) break;
                endMeasure = destination.Measure;
                endSlots = destination.EndSlots;
                endAbsolute = destination.AbsoluteEnd;
            }
            passages.Add(new FadePassage(origin.Measure, origin.StartSlots,
                endMeasure, endSlots, fadeOut));
        }

        return passages.Distinct().ToArray();
    }

    /// <summary>The beats that carry a dynamics marking: the first note of the track, then every change of dynamic.</summary>
    internal static Dictionary<TabCell, string> BuildDynamicMarks(TrackModel track)
    {
        var marks = new Dictionary<TabCell, string>(ReferenceEqualityComparer.Instance);
        var previous = -1;
        foreach (var measure in track.Measures)
            foreach (var cell in measure.Cells)
            {
                var principal = cell.Notes.FirstOrDefault(n => !n.IsGraceNote);
                if (principal is null) continue;
                var dynamic = Dynamics.NearestIndex(principal.Velocity);
                if (dynamic == previous) continue;
                previous = dynamic;
                marks[cell] = Dynamics.Names[dynamic];
            }
        return marks;
    }
}
