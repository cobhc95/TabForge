// Owns: the spoken texts of the score editor for screen readers: the cursor, one beat, the read-position line and the
//   read-bar line, with the duration, technique, section, signature, tempo and bar-time wording they use.
// Does not own: the automation peer or the cursor announcement's change tracking (Views/Score/EditorAutomation.cs in the app),
//   the cursor itself or the page layout.
// Tests: no dedicated test class.

using System.Globalization;
using System.Text.RegularExpressions;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Presenters;

/// <summary>Pure text builders over the song, the shown track and the cursor indices; stateless.</summary>
public static class ScoreCursorText
{
    public const string NoScore = "No score open";

    /// <summary>Splits "PalmMute" into words; compiled once, immutable, safe on any thread.</summary>
    private static readonly Regex CamelWords = new("(?<=[a-z0-9])(?=[A-Z])", RegexOptions.Compiled);

    /// <summary>Spoken description of the cursor: track, bar, beat, string, fret and note name, plus the ordered selection range.</summary>
    public static string Cursor(TrackModel? track, int bar, int cellIndex, int stringIndex, (int m1, int c1, int m2, int c2)? selection)
    {
        if (track is null || bar < 0 || bar >= track.Measures.Count) return NoScore;
        var measure = track.Measures[bar];
        var text = $"Track {track.Name}, bar {bar + 1}, beat {cellIndex + 1}, string {stringIndex + 1}";
        if (cellIndex >= 0 && cellIndex < measure.Cells.Count)
        {
            var cell = measure.Cells[cellIndex];
            text += ", " + DurationName(cell);
            var note = cell.Notes.FirstOrDefault(n => n.StringIndex == stringIndex);
            if (cell.IsRest) text += ", rest";
            else if (note is null) text += ", empty";
            else
            {
                var midi = note.MidiValue > 0 ? note.MidiValue
                    : stringIndex < track.StringTunings.Count ? track.PitchOf(stringIndex, note.Fret) : -1;
                text += note.Dead ? ", dead note" : $", fret {note.Fret}";
                if (midi >= 0 && !note.Dead) text += ", note " + MusicTheoryService.NoteName(midi).Replace("#", " sharp");
                if (note.Ghost) text += ", ghost";
                var techniques = TechniqueText(note);
                if (techniques.Length > 0) text += ", " + techniques;
            }
        }
        if (selection is var (m1, c1, m2, c2))
        {
            if (m2 < m1 || (m2 == m1 && c2 < c1)) (m1, c1, m2, c2) = (m2, c2, m1, c1);
            text += $"; selection from bar {m1 + 1} beat {c1 + 1} to bar {m2 + 1} beat {c2 + 1}";
        }
        return text;
    }

    /// <summary>"string 2 fret 7, palm mute" for one note of a beat.</summary>
    private static string NoteOfBeat(TabNote note)
    {
        var text = $"string {note.StringIndex + 1} " + (note.Dead ? "dead note" : $"fret {note.Fret}");
        if (note.Ghost) text += ", ghost";
        var techniques = TechniqueText(note);
        return techniques.Length > 0 ? text + ", " + techniques : text;
    }

    /// <summary>"Bar 5, beat 3, eighth note, string 2 fret 7, palm mute" - the name of one beat element.</summary>
    public static string Beat(TrackModel? track, int bar, int cellIndex)
    {
        if (track is null || bar < 0 || bar >= track.Measures.Count) return NoScore;
        var measure = track.Measures[bar];
        if (cellIndex < 0 || cellIndex >= measure.Cells.Count) return $"Bar {bar + 1}";
        var cell = measure.Cells[cellIndex];
        var text = $"Bar {bar + 1}, beat {cellIndex + 1}, {DurationName(cell)}";
        if (cell.Notes.Count > 0)
            text += ", " + string.Join("; ", cell.Notes.OrderBy(n => n.StringIndex).Select(NoteOfBeat));
        if (!string.IsNullOrWhiteSpace(cell.ChordName)) text += ", chord " + cell.ChordName;
        return text;
    }

    /// <summary>The nearest section name at or before the bar, or "".</summary>
    public static string SectionAt(TrackModel? track, int bar)
    {
        if (track is null) return "";
        for (var i = Math.Min(bar, track.Measures.Count - 1); i >= 0; i--)
            if (!string.IsNullOrWhiteSpace(track.Measures[i].SectionName)) return track.Measures[i].SectionName.Trim();
        return "";
    }

    /// <summary>The bar's time signature as "n/d" and its tempo.</summary>
    public static (string TimeSig, int Tempo) BarContext(SongProject? project, int bar)
    {
        if (project is null) return ("", 0);
        var m = MusicTime.BarOf(project, bar);
        return ($"{m?.TimeSigNum ?? project.TimeSignatureNumerator}/{m?.TimeSigDenom ?? project.TimeSignatureDenominator}", MusicTime.TempoAt(project, bar));
    }

    /// <summary>Approximate time of the start of a bar (repeats not followed), as m:ss.</summary>
    private static string BarTimeText(SongProject? project, int bar)
    {
        if (project is null) return "0:00";
        double ms = 0;
        for (var i = 0; i < bar; i++)
            ms += MusicTime.OffsetMs(MusicTime.BarOf(project, i), MusicTime.BarSlots(project, i), MusicTime.TempoAt(project, i));
        var seconds = (int)Math.Round(ms / 1000);
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
    }

    /// <summary>"Read position": track, bar, beat, string, section, signature, tempo and time.</summary>
    public static string Position(SongProject? project, TrackModel? track, int bar, int cellIndex, int stringIndex)
    {
        if (track is null || bar < 0 || bar >= track.Measures.Count) return NoScore;
        var (timeSig, tempo) = BarContext(project, bar);
        var section = SectionAt(track, bar);
        return $"Track {track.Name}, bar {bar + 1} of {track.Measures.Count}, beat {cellIndex + 1}, string {stringIndex + 1}" +
            (section.Length > 0 ? $", section {section}" : "") + $", time signature {timeSig}, tempo {tempo}, about {BarTimeText(project, bar)}";
    }

    /// <summary>"Read current bar": the bar's header and every beat in it.</summary>
    public static string Bar(SongProject? project, TrackModel? track, int bar)
    {
        if (track is null || bar < 0 || bar >= track.Measures.Count) return NoScore;
        var (timeSig, tempo) = BarContext(project, bar);
        var section = SectionAt(track, bar);
        var text = $"Bar {bar + 1}" + (section.Length > 0 ? $", section {section}" : "") + $", {timeSig}, tempo {tempo}";
        var beats = MusicTime.BeatSlots(track.Measures[bar]);
        if (beats.Count == 0) return text + ", empty";
        return text + ". " + string.Join(". ", beats.Select(i => Beat(track, bar, i)));
    }

    /// <summary>"PalmMute" -> "palm mute" (the origin/destination bookkeeping flags of hammer-ons are not spoken).</summary>
    public static string TechniqueText(TabNote note)
    {
        if (note.Techniques.Count == 0) return "";
        var names = new List<string>();
        foreach (var t in note.Techniques.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (t.Equals(TechniqueNames.HopoOrigin, StringComparison.OrdinalIgnoreCase) ||
                t.Equals(TechniqueNames.HopoDestination, StringComparison.OrdinalIgnoreCase)) continue;
            if (t.Equals(TechniqueNames.PalmMuteLegacy, StringComparison.OrdinalIgnoreCase)) { names.Add("palm mute"); continue; }
            if (t.Equals(TechniqueNames.Hopo, StringComparison.OrdinalIgnoreCase)) { names.Add("hammer-on or pull-off"); continue; }
            names.Add(CamelWords.Replace(t, " ").ToLowerInvariant());
        }
        return string.Join(", ", names.Distinct());
    }

    /// <summary>"quarter note", "dotted eighth note", "eighth triplet" ... for the beat's written duration.</summary>
    public static string DurationName(TabCell cell)
    {
        var name = cell.DurationDenominator switch
        {
            1 => "whole", 2 => "half", 4 => "quarter", 8 => "eighth", 16 => "sixteenth", 32 => "thirty-second", 64 => "sixty-fourth",
            var d => $"1/{d}"
        };
        var dots = cell.Dots == 1 ? "dotted " : cell.Dots >= 2 ? "double-dotted " : "";
        return dots + name + (cell.IsRest ? " rest" : " note") + (cell.Tuplet.Numerator > 0 ? $" in a {cell.Tuplet.Numerator}-tuplet" : "");
    }
}
