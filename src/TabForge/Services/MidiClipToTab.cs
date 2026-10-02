using TabForge.Models;

namespace TabForge.Services;

/// <summary>
/// Writes a recorded MIDI clip into a track's notation (when the clip is dragged onto the track's row): onsets
/// are quantised to the 16th grid, notes starting together form one beat, each beat lasts until the next one
/// (the longest plain duration that fits), and pitches go to strings with the lowest playable fret. Drum tracks
/// take the note number as the drum. Only the bars the clip covers are touched.
/// </summary>
public static class MidiClipToTab
{
    /// <summary>Returns how many notes were written. <paramref name="barAt"/> maps song seconds to (bar, fraction).</summary>
    public static int Write(SongProject project, TrackModel track, AudioClip clip, Func<double, (int Bar, double Fraction)> barAt)
    {
        if (clip.Notes is not { Count: > 0 } notes) return 0;
        var bars = track.Measures.Count;
        var speed = Math.Clamp(clip.Speed, 0.25, 4);
        var drums = track.Kind == TrackKind.Drums || track.MidiChannel == 9 || track.StringTunings.Count == 0;

        // Onsets on the grid: (bar, slot) -> pitches (with velocity and length in slots).
        var beats = new SortedDictionary<(int Bar, int Slot), List<(int Pitch, int Velocity, double LengthSlots)>>();
        foreach (var n in notes)
        {
            if (n.StartSec < clip.OffsetSec - 1e-6 || n.StartSec >= clip.OffsetSec + clip.SourceLengthSec) continue;
            var songSec = clip.StartSec + (n.StartSec - clip.OffsetSec) / speed;
            var (bar, fraction) = barAt(songSec);
            if (bar < 0 || bar >= bars) continue;
            var slots = Math.Max(1, MusicTime.BarSlots(project, bar));
            var slot = (int)Math.Round(fraction * slots);
            if (slot >= slots) { bar++; slot = 0; if (bar >= bars) continue; }
            var (endBar, endFraction) = barAt(songSec + n.LengthSec / speed);
            var lengthSlots = endBar == bar ? (endFraction * slots - slot) : (slots - slot) + endFraction * MusicTime.BarSlots(project, Math.Min(endBar, bars - 1));
            var pitch = Math.Clamp(n.Pitch + (int)Math.Round(clip.Pitch), 0, 127);
            if (!beats.TryGetValue((bar, slot), out var list)) beats[(bar, slot)] = list = new();
            if (list.All(x => x.Pitch != pitch)) list.Add((pitch, n.Velocity, Math.Max(0.5, lengthSlots)));
        }

        var written = 0;
        var touched = new HashSet<int>();
        var keys = beats.Keys.ToList();
        for (var k = 0; k < keys.Count; k++)
        {
            var (bar, slot) = keys[k];
            var measure = track.Measures[bar];
            var slots = Math.Max(1, MusicTime.BarSlots(project, bar));
            // Until the next beat in this bar (or the bar end), and no longer than the longest note.
            var next = k + 1 < keys.Count && keys[k + 1].Bar == bar ? keys[k + 1].Slot : slots;
            var gap = Math.Max(1, next - slot);
            var longest = beats[keys[k]].Max(x => x.LengthSlots);
            var span = Math.Max(1, Math.Min(gap, (int)Math.Round(longest)));
            var cell = new TabCell { DurationDenominator = Denominator(span) };
            var usedStrings = new HashSet<int>();
            foreach (var (pitch, velocity, _) in beats[keys[k]].OrderByDescending(x => x.Pitch))
            {
                var note = drums ? DrumNote(pitch) : StringNote(track, pitch, usedStrings);
                if (note is null) continue;
                note.Velocity = Math.Clamp(velocity, 1, 127);
                usedStrings.Add(note.StringIndex);
                cell.Notes.Add(note);
            }
            if (cell.Notes.Count == 0) continue;
            while (measure.Cells.Count < slots) measure.Cells.Add(new TabCell());   // pad only a bar that receives notes, so a clip that fits nothing leaves the song untouched
            measure.Cells[slot] = cell;
            written += cell.Notes.Count;
            touched.Add(bar);
        }
        foreach (var bar in touched) FitBar(project, track.Measures[bar], bar);
        return written;
    }

    /// <summary>Largest plain duration of at most <paramref name="slots"/> sixteenths (16 = whole note).</summary>
    private static int Denominator(int slots) => slots >= 16 ? 1 : slots >= 8 ? 2 : slots >= 4 ? 4 : slots >= 2 ? 8 : 16;

    private static TabNote DrumNote(int pitch) =>
        new() { StringIndex = GuitarProImporter.DrumLine(pitch), Fret = pitch, MidiValue = pitch };

    /// <summary>The free string that plays the pitch at the lowest fret (null when no string can).</summary>
    private static TabNote? StringNote(TrackModel track, int pitch, HashSet<int> used)
    {
        var best = -1; var bestFret = int.MaxValue;
        for (var s = 0; s < track.StringTunings.Count; s++)
        {
            if (used.Contains(s)) continue;
            var fret = track.FretOf(s, pitch);
            if (fret < 0 || fret > Math.Max(1, track.NumberOfFrets)) continue;
            if (fret < bestFret) { best = s; bestFret = fret; }
        }
        return best < 0 ? null : new TabNote { StringIndex = best, Fret = bestFret, MidiValue = track.PitchOf(best, bestFret) };
    }

    /// <summary>Beats written earlier in a bar may now run into a new one: shorten each beat to the gap before the next.</summary>
    private static void FitBar(SongProject project, MeasureModel measure, int bar)
    {
        var slots = Math.Max(1, MusicTime.BarSlots(project, bar));
        var beats = MusicTime.BeatSlots(measure);
        for (var i = 0; i < beats.Count; i++)
        {
            var cell = measure.Cells[beats[i]];
            var gap = (i + 1 < beats.Count ? beats[i + 1] : slots) - beats[i];
            if (MusicTime.CellSlots(cell) > gap + 0.01 && cell.Tuplet.Numerator == 0)
            {
                cell.DurationDenominator = Denominator(Math.Max(1, gap));
                cell.Dots = 0;
            }
        }
    }
}
