using TabForge.Models;
using TabForge.Services;

namespace TabForge.Presenters;

// Owns: a tab track's notes in the piano roll, both voices: reading the written notes (a tied note joins the note it continues) and writing an
//       edited list back changing only what the edit touched. Velocity and mute are written into the notes in place (mute is the note's Silent flag:
//       the tab stays as written, the note never plays); a pitch change with the same timing re-fingers the note in its beat; any other change is
//       a set of removed and added notes handed to PianoRollTabWriter, which leaves every other beat and its marks as they are.
// Does not own: placing the added notes in the beats (PianoRollTabWriter), the tab prompt (PianoRollPresenter) or the string rule (MidiClipToTab.StringNote).
// Tests: TabForge.Avalonia.Tests.PianoRollSourceTests, PianoRollTabWriterTests.
public sealed class PianoRollTabSource : IPianoRollSource
{
    private const double Epsilon = 1e-6;

    public bool IsTab => true;

    public PianoRollTimeMap Map(SongProject song) => PianoRollTimeMap.Written(song);

    /// <summary>One piano-roll note of the tab: its bar, its voice, and its written note with the tied notes that continue it, each with its beat.</summary>
    internal sealed record Entry(PianoRollNote Note, int Bar, List<(TabCell Cell, TabNote Note)> Parts);

    internal static bool Drums(TrackModel track) => track.IsDrumTrack || track.StringTunings.Count == 0;

    private static int PitchOf(TrackModel track, TabNote note) =>
        PianoRollEdits.ClampPitch(note.MidiValue > 0 ? note.MidiValue : track.NoteMidi(note.StringIndex, note.Fret));

    public List<PianoRollNote> Read(SongProject song, TrackModel track, PianoRollTimeMap map) => Entries(track, map).Select(e => e.Note).ToList();

    internal static List<Entry> Entries(TrackModel track, PianoRollTimeMap map)
    {
        var entries = new List<Entry>();
        for (var voice = 0; voice < 2; voice++)
        {
            var open = new Dictionary<int, int>();   // string (drums: pitch) -> index of the entry a tie continues
            foreach (var bar in map.Bars)
            {
                if (bar.Bar < 0 || bar.Bar >= track.Measures.Count) continue;
                var measure = track.Measures[bar.Bar];
                if (voice == 1 && measure.Voice2Cells.Count == 0) continue;
                var cells = measure.CellsForVoice(voice);
                var onsets = MusicTime.Onsets(cells);
                for (var i = 0; i < cells.Count; i++)
                {
                    var cell = cells[i];
                    if (cell.Notes.Count == 0 || !MusicTime.ConsumesTime(cell)) continue;
                    var start = bar.StartBeat + onsets[i] / SnapCore.SlotsPerBeat;
                    var length = MusicTime.CellSlots(cell) / SnapCore.SlotsPerBeat;
                    foreach (var note in cell.Notes)
                    {
                        if (note.IsGraceNote) continue;
                        var pitch = PitchOf(track, note);
                        var key = Drums(track) ? pitch : note.StringIndex;
                        if ((note.Tied || cell.IsTied) && open.TryGetValue(key, out var at) && entries[at].Note.Pitch == pitch)
                        {
                            var head = entries[at];
                            entries[at] = head with { Note = head.Note with { Length = start + length - head.Note.Start } };
                            head.Parts.Add((cell, note));
                            continue;
                        }
                        open[key] = entries.Count;
                        entries.Add(new Entry(new PianoRollNote(start, length, pitch, PianoRollEdits.ClampVelocity(note.Velocity), note.Silent, voice), bar.Bar,
                            new List<(TabCell, TabNote)> { (cell, note) }));
                    }
                }
            }
        }
        return entries.OrderBy(e => e.Note.Start).ThenBy(e => e.Note.Pitch).ThenBy(e => e.Note.Length).ThenBy(e => e.Note.Voice).ToList();
    }

    public bool Write(SongProject song, TrackModel track, PianoRollTimeMap map, IReadOnlyList<PianoRollNote> after)
    {
        var entries = Entries(track, map);
        if (InPlace(track, entries, after) is { } changed) return changed;
        return PianoRollTabWriter.Apply(song, track, map, entries, after);
    }

    private static bool SameTiming(PianoRollNote a, PianoRollNote b) =>
        Math.Abs(a.Start - b.Start) < Epsilon && Math.Abs(a.Length - b.Length) < Epsilon && a.Voice == b.Voice;

    /// <summary>Velocity, mute and same-timing pitch changes written into the existing notes; null when the edit adds, removes or moves notes.</summary>
    private static bool? InPlace(TrackModel track, List<Entry> entries, IReadOnlyList<PianoRollNote> after)
    {
        if (entries.Count != after.Count) return null;
        for (var i = 0; i < after.Count; i++)
        {
            if (!SameTiming(entries[i].Note, after[i])) return null;
            if (entries[i].Note.Pitch != after[i].Pitch && (entries[i].Parts.Count > 1 || Fingering(track, entries[i].Parts[0].Cell, entries[i].Parts[0].Note, after[i].Pitch) is null)) return null;
        }
        var changed = false;
        for (var i = 0; i < after.Count; i++)
        {
            var (entry, want) = (entries[i], after[i]);
            var head = entry.Parts[0];
            if (entry.Note.Pitch != want.Pitch && Fingering(track, head.Cell, head.Note, want.Pitch) is var (s, fret, midi))
            {
                (head.Note.StringIndex, head.Note.Fret, head.Note.MidiValue) = (s, fret, midi);
                changed = true;
            }
            foreach (var (_, part) in entry.Parts)
            {
                if (part.Velocity != want.Velocity) { part.Velocity = want.Velocity; changed = true; }
                if (part.Silent != want.Muted) { part.Silent = want.Muted; changed = true; }
            }
        }
        return changed;
    }

    /// <summary>Where <paramref name="pitch"/> goes in the note's beat: its own string when the fret fits, else the free string with the lowest fret.</summary>
    internal static (int String, int Fret, int Midi)? Fingering(TrackModel track, TabCell cell, TabNote? note, int pitch)
    {
        if (Drums(track))
        {
            var drum = MidiClipToTab.DrumNote(pitch);
            return (drum.StringIndex, drum.Fret, drum.MidiValue);
        }
        var used = cell.Notes.Where(other => !ReferenceEquals(other, note)).Select(other => other.StringIndex).ToHashSet();
        if (note is not null && note.StringIndex >= 0 && note.StringIndex < track.StringTunings.Count && !used.Contains(note.StringIndex))
        {
            var own = track.FretOf(note.StringIndex, pitch);
            if (own >= 0 && own <= track.NumberOfFrets) return (note.StringIndex, own, track.PitchOf(note.StringIndex, own));
        }
        return MidiClipToTab.StringNote(track, pitch, used) is { } placed ? (placed.StringIndex, placed.Fret, placed.MidiValue) : null;
    }
}
