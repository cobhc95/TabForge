using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

// Owns: stems and beams of a percussion staff. A bar written in one voice stems every beat up, all its sounds on one shared stem
//   (a kick and a snare in one cell are a chord). A bar written in two voices splits by instrument: hands (snare, toms, cymbals,
//   hi-hat) stem up, feet (kick, pedal hi-hat) stem down, each direction beamed on its own per beat group. Every stem is anchored
//   to its own noteheads and keeps the written length of its cell (the same value always draws the same shape).
// Does not own: the heads (StaffNotationDrawing.DrawDrumHeads), the beam segment geometry (StaffNotationLayoutBuilder.BuildBeamSegments).
// Tests: no dedicated test class.
internal static class StaffDrumStems
{
    /// <summary>Staff step (half-spaces down from the top line) from which a sound is a foot sound: kick (F4) and pedal hi-hat.</summary>
    internal const int FootStep = 7;

    /// <summary>
    /// Builds one up-stem and/or one down-stem "shadow" beat per drum beat, beams each direction, and mirrors the primary stem
    /// onto the real beat (up stem when it has hands, else the down stem; the other direction goes to LowerStem*).
    /// Returns the shadows (drawn as the stems); the beam groups and segments of both directions come out in order.
    /// </summary>
    internal static List<StaffNotationBeat> Build(IReadOnlyList<StaffNotationBeat> beats, IReadOnlyList<StaffNotationLayoutBuilder.MetricGroup> metricGroups,
        out List<StaffNotationBeamGroup> groups, out List<StaffNotationBeamSegment> segments)
    {
        var up = new List<StaffNotationBeat>();
        var down = new List<StaffNotationBeat>();
        var pairs = new List<(StaffNotationBeat Real, StaffNotationBeat? Up, StaffNotationBeat? Down)>();
        foreach (var beat in beats)
        {
            var ys = beat.Cell.Notes.Select(n => (Y: HeadY(beat, n), Foot: StepOf(beat, n) >= FootStep)).ToList();
            beat.MinY = ys.Count > 0 ? ys.Min(h => h.Y) : beat.StaffTop + StaffNotationGeometry.MiddleLineOffset;
            beat.MaxY = ys.Count > 0 ? ys.Max(h => h.Y) : beat.MinY;
            if (!beat.HasStem || ys.Count == 0) continue;
            StaffNotationBeat? u = null, d = null;
            // One written voice: one up stem through every head of the cell. Two voices: hands up, feet down.
            var hands = ys.Where(h => !beat.TwoVoices || !h.Foot).Select(h => h.Y).ToList();
            var feet = ys.Where(h => beat.TwoVoices && h.Foot).Select(h => h.Y).ToList();
            if (hands.Count > 0) { u = Shadow(beat, true, hands); up.Add(u); }
            if (feet.Count > 0) { d = Shadow(beat, false, feet); down.Add(d); }
            pairs.Add((beat, u, d));
        }
        groups = StaffNotationLayoutBuilder.BuildBeamGroups(up, metricGroups);
        foreach (var g in groups) g.StemUp = true;
        var downGroups = StaffNotationLayoutBuilder.BuildBeamGroups(down, metricGroups, groups.Count);
        groups.AddRange(downGroups);
        segments = StaffNotationLayoutBuilder.BuildBeamSegments(groups);

        foreach (var (real, u, d) in pairs)
        {
            var primary = u ?? d!;
            real.StemUp = primary.StemUp;
            real.StemX = primary.StemX;
            real.StemStartY = primary.StemStartY;
            real.StemEndY = primary.StemEndY;
            real.BeamGroupIndex = primary.BeamGroupIndex;
            if (u is not null && d is not null)
            {
                real.LowerStemTopY = d.StemStartY;
                real.LowerStemEndY = d.StemEndY;
            }
        }
        return up.Concat(down).ToList();
    }

    /// <summary>Onsets (in slots) of the cells of one voice that carry notes.</summary>
    internal static List<double> NoteOnsets(IReadOnlyList<TabCell> cells)
        => MusicTime.BeatOnsets(cells).Where(b => b.Cell.Notes.Count > 0).Select(b => b.Start).ToList();

    private static StaffNotationBeat Shadow(StaffNotationBeat real, bool up, List<double> ys)
    {
        var minY = ys.Min();
        var maxY = ys.Max();
        return new StaffNotationBeat
        {
            CellIndex = real.CellIndex, Cell = real.Cell, StartSlots = real.StartSlots, DurationSlots = real.DurationSlots,
            CenterX = real.CenterX, Flags = real.Flags, Notes = new List<StaffNotationNote>(), IsDrum = true, StaffTop = real.StaffTop,
            StemUp = up, MinY = minY, MaxY = maxY,
            StemX = real.CenterX + (up ? 5 : -5),
            StemStartY = up ? maxY : minY,
            StemEndY = up ? minY - StaffNotationGeometry.StemLength : maxY + StaffNotationGeometry.StemLength,
        };
    }

    private static int StepOf(StaffNotationBeat beat, TabNote note) => beat.DrumMap?.Invoke(note.MidiValue > 0 ? note.MidiValue : note.Fret).StaffStep ?? 4;

    private static double HeadY(StaffNotationBeat beat, TabNote note) => beat.StaffTop + StepOf(beat, note) * StaffNotationGeometry.StaffGap / 2;
}
