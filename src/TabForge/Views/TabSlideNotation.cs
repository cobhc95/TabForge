using TabForge.Models;

namespace TabForge.Views;

internal enum TabSlideMarkKind
{
    Connection,
    IncomingFromBelow,
    IncomingFromAbove,
    OutgoingUp,
    OutgoingDown
}

internal readonly record struct TabSlideMark(
    int SourceCellIndex,
    double SourceStartSlots,
    TabNote Source,
    TabSlideMarkKind Kind,
    int? TargetMeasureIndex = null,
    int? TargetCellIndex = null,
    double TargetStartSlots = 0,
    TabNote? Target = null);

/// <summary>Resolves compact TAB slide marks against the next note on the same string.</summary>
internal static class TabSlideNotation
{
    public static IReadOnlyList<TabSlideMark> ForMeasure(TrackModel track, int measureIndex, int voiceIndex)
    {
        if (measureIndex < 0 || measureIndex >= track.Measures.Count) return Array.Empty<TabSlideMark>();
        var measure = track.Measures[measureIndex];
        var cells = measure.CellsForVoice(voiceIndex);
        var marks = new List<TabSlideMark>();

        foreach (var (cell, cellIndex) in cells.Select((cell, index) => (cell, index)))
        {
            if (cell.Notes.Count == 0) continue;
            var startSlots = Math.Max(0, cell.RhythmicPosition ?? cellIndex);
            foreach (var note in cell.Notes)
            {
                if (HasIncomingBelow(note))
                    marks.Add(new TabSlideMark(cellIndex, startSlots, note, TabSlideMarkKind.IncomingFromBelow));
                else if (HasIncomingAbove(note))
                    marks.Add(new TabSlideMark(cellIndex, startSlots, note, TabSlideMarkKind.IncomingFromAbove));

                if (HasConnectedSlide(note))
                {
                    var target = FindNextOnString(track, measureIndex, voiceIndex, startSlots, note.StringIndex);
                    if (target is { } destination &&
                        (note.SlideTargetMidi <= 0 || PitchMidi(destination.Note, track) == note.SlideTargetMidi))
                    {
                        marks.Add(new TabSlideMark(cellIndex, startSlots, note, TabSlideMarkKind.Connection,
                            destination.MeasureIndex, destination.CellIndex, destination.StartSlots, destination.Note));
                    }
                    else
                    {
                        var goesUp = note.SlideTargetMidi > 0
                            ? note.SlideTargetMidi >= PitchMidi(note, track)
                            : !note.Techniques.Contains("SlideOutDown");
                        marks.Add(new TabSlideMark(cellIndex, startSlots, note,
                            goesUp ? TabSlideMarkKind.OutgoingUp : TabSlideMarkKind.OutgoingDown));
                    }
                }
                else if (HasOutgoingUp(note))
                    marks.Add(new TabSlideMark(cellIndex, startSlots, note, TabSlideMarkKind.OutgoingUp));
                else if (HasOutgoingDown(note))
                    marks.Add(new TabSlideMark(cellIndex, startSlots, note, TabSlideMarkKind.OutgoingDown));
            }
        }

        return marks;
    }

    public static bool HasIncoming(TabNote note) => HasIncomingBelow(note) || HasIncomingAbove(note);

    public static bool HasOutgoing(TabNote note) => HasConnectedSlide(note) || HasOutgoingUp(note) || HasOutgoingDown(note);

    public static bool IsRenderedAsGeometry(string technique) =>
        technique.Equals("Slide", StringComparison.OrdinalIgnoreCase) ||
        technique.Equals("LegatoSlide", StringComparison.OrdinalIgnoreCase) ||
        technique.Equals("ShiftSlide", StringComparison.OrdinalIgnoreCase) ||
        technique.Equals("SlideInBelow", StringComparison.OrdinalIgnoreCase) ||
        technique.Equals("SlideInAbove", StringComparison.OrdinalIgnoreCase) ||
        technique.Equals("SlideOutUp", StringComparison.OrdinalIgnoreCase) ||
        technique.Equals("SlideOutDown", StringComparison.OrdinalIgnoreCase);

    private static bool HasConnectedSlide(TabNote note) => note.Techniques.Contains("Slide") ||
        note.Techniques.Contains("LegatoSlide") || note.Techniques.Contains("ShiftSlide");

    private static bool HasIncomingBelow(TabNote note) => note.Techniques.Contains("SlideInBelow");
    private static bool HasIncomingAbove(TabNote note) => note.Techniques.Contains("SlideInAbove");
    private static bool HasOutgoingUp(TabNote note) => note.Techniques.Contains("SlideOutUp");
    private static bool HasOutgoingDown(TabNote note) => note.Techniques.Contains("SlideOutDown");

    private static (int MeasureIndex, int CellIndex, double StartSlots, TabNote Note)? FindNextOnString(
        TrackModel track, int measureIndex, int voiceIndex, double sourceStartSlots, int stringIndex)
    {
        for (var bar = measureIndex; bar < track.Measures.Count; bar++)
        {
            var cells = track.Measures[bar].CellsForVoice(voiceIndex);
            var candidates = cells.Select((cell, index) => (Cell: cell, Index: index,
                    Start: Math.Max(0, cell.RhythmicPosition ?? index)))
                .Where(item => item.Cell.Notes.Count > 0 &&
                               (bar > measureIndex || item.Start > sourceStartSlots + 0.001))
                .OrderBy(item => item.Start);

            foreach (var item in candidates)
            {
                var note = item.Cell.Notes.FirstOrDefault(candidate => candidate.StringIndex == stringIndex);
                if (note is not null)
                    return (bar, item.Index, item.Start, note);
            }
        }

        return null;
    }

    private static int PitchMidi(TabNote note, TrackModel track) => note.MidiValue > 0
        ? note.MidiValue
        : note.StringIndex >= 0 && note.StringIndex < track.StringTunings.Count
            ? track.PitchOf(note.StringIndex, note.Fret)
            : note.Fret;
}
