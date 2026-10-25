using System.Globalization;
using System.Linq;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge.Views.Score;

// ScoreRenderer: cell x positions, hammer-on/pull-off slurs and tab slides, and the next/previous-note searches they use.
// Owns: tab links between notes. Does not own: the bar drawing or the layout. Tests: render identity, TestTabEditorRenderInvariance.

internal sealed partial class ScoreRenderer
{
    private double CellCenterX(int measureIndex, double startSlots)
    {
        var track = _host.Track;
        if (track is null || measureIndex < 0 || measureIndex >= track.Measures.Count) return 0;
        var position = _layout.GetLayout(track).Measure(measureIndex);
        return position.X + _layout.WarpFor(track, measureIndex).CenterFraction(startSlots) * position.Width;
    }

    private double CellCenterX(int measureIndex, int cellIndex, int voice = 0)
    {
        var track = _host.Track;
        if (track is null || measureIndex < 0 || measureIndex >= track.Measures.Count) return 0;
        var measure = track.Measures[measureIndex];
        var voiceCells = VoiceCells(measure, voice);
        var position = _layout.GetLayout(track).Measure(measureIndex);
        return position.X + _layout.WarpFor(track, measureIndex).CenterFraction(CursorPositions.DrawnStart(voiceCells, cellIndex)) * position.Width;
    }

    private double CellBoundaryX(int measureIndex, double endSlots)
    {
        var track = _host.Track;
        if (track is null || measureIndex < 0 || measureIndex >= track.Measures.Count) return 0;
        var position = _layout.GetLayout(track).Measure(measureIndex);
        return position.X + _layout.WarpFor(track, measureIndex).Fraction(endSlots) * position.Width;
    }

    private void DrawTabHopoSlur(IDrawContext d, TabNote note, int measureIndex, int cellIndex,
        double startX, double y, UiColor ink, int voice = 0)
    {
        var isExplicitOrigin = note.Techniques.Contains("HOPOOrigin");
        var isLegacyHopo = note.Techniques.Contains("HOPO") &&
                           !note.Techniques.Contains("HOPODestination") && !isExplicitOrigin;
        if (!isExplicitOrigin && !isLegacyHopo) return;

        (int Measure, int Cell, TabNote Note)? next;
        if (isExplicitOrigin)
        {
            var previous = FindPreviousTabNoteOnString(measureIndex, cellIndex, note.StringIndex, voice);
            if (previous is { } prior && _layout.GetLayout().SystemForMeasure(prior.Measure) == _layout.GetLayout().SystemForMeasure(measureIndex) &&
                prior.Note.Techniques.Contains("HOPOOrigin")) return;

            next = FindHopoDestination(measureIndex, cellIndex, note.StringIndex, voice);
            while (next is { } endpoint && endpoint.Note.Techniques.Contains("HOPOOrigin"))
            {
                var chained = FindHopoDestination(endpoint.Measure, endpoint.Cell, note.StringIndex, voice);
                if (chained is null) break;
                next = chained;
            }
            // Some reference variants only encode the origin bit. Preserve a useful short slur for them.
            next ??= FindNextTabNoteOnString(measureIndex, cellIndex, note.StringIndex, voice);
        }
        else
        {
            // Older saved projects only have a generic HOPO bit. Treat each contiguous run as one
            // legato phrase: draw from its first note to its last instead of tiny adjacent arcs.
            var previous = FindPreviousTabNoteOnString(measureIndex, cellIndex, note.StringIndex, voice);
            if (previous is { } prior && _layout.GetLayout().SystemForMeasure(prior.Measure) == _layout.GetLayout().SystemForMeasure(measureIndex) &&
                prior.Note.Techniques.Contains("HOPO")) return;
            next = FindLegacyHopoPhraseEnd(measureIndex, cellIndex, note.StringIndex, voice);
        }
        if (next is null || _layout.GetLayout().SystemForMeasure(next.Value.Measure) != _layout.GetLayout().SystemForMeasure(measureIndex)) return;

        var endX = CellCenterX(next.Value.Measure, next.Value.Cell, voice);
        if (endX - startX < 18) return;

        // Hammer-on / pull-off groups as a legato slur in the tablature,
        // not as a repeated "H/P" text label over every note.
        // The reference draws the slur above the numbers on the upper strings and below them on the lower strings.
        var below = TabSlurBelow(note.StringIndex);
        var side = below ? 1 : -1;
        var start = new UiPoint(startX + 3, y + side * 7);
        var end = new UiPoint(endX - 3, y + side * 7);
        var arc = Math.Clamp((endX - startX) * 0.10, 5, 10);
        var geometry = TabHopoArc(d.Paths, start, end, startX, endX, y + side * (7 + arc));
        d.Path(null, new UiStroke(ink, 0.9), geometry);
    }

    /// <summary>The slur's path, built apart from <see cref="DrawTabHopoSlur"/> so the notes it skips allocate no closure.</summary>
    private static IUiPath TabHopoArc(IPathFactory paths, UiPoint start, UiPoint end, double startX, double endX, double peakY) => paths.BuildPath(c =>
    {
        c.BeginFigure(start, true, false);
        c.BezierTo(new UiPoint(startX + (endX - startX) * 0.28, peakY), new UiPoint(startX + (endX - startX) * 0.72, peakY), end);
    });

    /// <summary>A TAB slur on the lower half of the strings curves below the fret numbers (as in the reference).</summary>
    internal bool TabSlurBelow(int stringIndex) => stringIndex >= Math.Max(1, _host.Track?.StringTunings.Count ?? 6) / 2;

    private void DrawTabSlides(IDrawContext d, TrackModel track, int measureIndex,
        StaffNotationMeasureLayout layout, IReadOnlyList<TabCell> cells, int strings, double tabTop, UiColor ink)
    {
        var voiceIndex = ReferenceEquals(cells, track.Measures[measureIndex].Voice2Cells) ? 1 : 0;
        var scoreLayout = _layout.GetLayout(track);
        var systemIndex = scoreLayout.SystemForMeasure(measureIndex);
        var pen = new UiStroke(ink, 1.05);

        foreach (var mark in TabSlideNotation.ForMeasure(track, measureIndex, voiceIndex))
        {
            var sourceBeat = layout.BeatForCell(mark.SourceCellIndex);
            if (sourceBeat is null || mark.Source.StringIndex < 0 || mark.Source.StringIndex >= strings) continue;
            if (track.Kind != TrackKind.Drums && mark.Source.IsGraceNote && sourceBeat.Cell.Notes.Any(o => !o.IsGraceNote)) continue;   // a grace note's slide / hammer is drawn between it and its main note

            var sourceX = sourceBeat.CenterX;
            var y = tabTop + mark.Source.StringIndex * _host.StringGap;
            var sourceLabel = ScoreMarkText.FretLabelWidthText(mark.Source);
            var sourceWidth = ScoreText.MakeTextIn(d, ScoreTextArea.Fret, sourceLabel, _host.FretFontSize, ink, 400, "Consolas").Width;
            switch (mark.Kind)
            {
                case TabSlideMarkKind.IncomingFromBelow:
                    d.Line(pen, sourceX - sourceWidth / 2 - 7, y + 3.5, sourceX - sourceWidth / 2 - 2, y - 2.5);
                    break;
                case TabSlideMarkKind.IncomingFromAbove:
                    d.Line(pen, sourceX - sourceWidth / 2 - 7, y - 3.5, sourceX - sourceWidth / 2 - 2, y + 2.5);
                    break;
                case TabSlideMarkKind.OutgoingUp:
                case TabSlideMarkKind.OutgoingDown:
                    DrawTabSlideStub(d, pen, sourceX, sourceWidth, y,
                        mark.Kind == TabSlideMarkKind.OutgoingUp);
                    break;
                case TabSlideMarkKind.Connection:
                    if (mark.Target is null || mark.TargetMeasureIndex is not { } targetMeasure ||
                        mark.TargetCellIndex is null || mark.Target.StringIndex < 0 ||
                        mark.Target.StringIndex >= strings || scoreLayout.SystemForMeasure(targetMeasure) != systemIndex)
                    {
                        DrawTabSlideStub(d, pen, sourceX, sourceWidth, y, goesUp: true);
                        break;
                    }

                    var targetX = CellCenterX(targetMeasure, mark.TargetStartSlots);
                    var targetLabel = ScoreMarkText.FretLabelWidthText(mark.Target);
                    var targetWidth = ScoreText.MakeTextIn(d, ScoreTextArea.Fret, targetLabel, _host.FretFontSize, ink, 400, "Consolas").Width;
                    var startX = sourceX + sourceWidth / 2 + 1.5;
                    var endX = targetX - targetWidth / 2 - 1.5;
                    if (endX - startX < 4) break;
                    var goesUp = mark.Target.Fret >= mark.Source.Fret;
                    var endY = y + (goesUp ? -3 : 3);
                    d.Line(pen, startX, y, endX, endY);
                    if (mark.Source.Techniques.Contains("LegatoSlide"))
                    {
                        // Legato slide = the slide line plus a slur; a shift slide is the line alone.
                        var arcHeight = Math.Clamp((endX - startX) * 0.12, 4, 9);
                        var side = TabSlurBelow(mark.Source.StringIndex) ? 1 : -1;
                        // The lambda captures only these block locals, so slide marks without a slur allocate no closure.
                        var (from, peak, to) = (new UiPoint(startX, y + side * 8), new UiPoint((startX + endX) / 2, y + side * (8 + arcHeight * 2)), new UiPoint(endX, endY + side * 8));
                        var slur = d.Paths.BuildPath(c =>
                        {
                            c.BeginFigure(from, false, false);
                            c.QuadraticTo(peak, to);
                        });
                        d.Path(null, pen, slur);
                    }
                    break;
            }
        }
    }

    private static void DrawTabSlideStub(IDrawContext d, UiStroke pen, double centerX, double labelWidth,
        double y, bool goesUp)
    {
        var start = new UiPoint(centerX + labelWidth / 2 + 1.5, y);
        var end = new UiPoint(start.X + 6, y + (goesUp ? -5 : 5));
        d.Line(pen, start.X, start.Y, end.X, end.Y);
    }

    // The cell list of one voice (0 = voice 1, 1 = voice 2). The hammer-on/pull-off slur searches must walk the same
    // voice as the note they start from: the two lists can differ in length.
    private static List<TabCell> VoiceCells(MeasureModel measure, int voice) => voice == 1 ? measure.Voice2Cells : measure.Cells;

    private (int Measure, int Cell, TabNote Note)? FindNextTabNoteOnString(int measureIndex, int cellIndex, int stringIndex, int voice = 0)
    {
        var track = _host.Track;
        if (track is null) return null;
        for (var measure = measureIndex; measure < track.Measures.Count; measure++)
        {
            var firstCell = measure == measureIndex ? cellIndex + 1 : 0;
            var cells = VoiceCells(track.Measures[measure], voice);
            for (var cell = firstCell; cell < cells.Count; cell++)
            {
                var next = cells[cell].Notes.FirstOrDefault(candidate => candidate.StringIndex == stringIndex);
                if (next is not null) return (measure, cell, next);
            }
        }
        return null;
    }

    private (int Measure, int Cell, TabNote Note)? FindHopoDestination(int measureIndex, int cellIndex, int stringIndex, int voice = 0)
    {
        var track = _host.Track;
        if (track is null) return null;
        var system = _layout.GetLayout(track).SystemForMeasure(measureIndex);
        for (var measure = measureIndex; measure < track.Measures.Count && _layout.GetLayout(track).SystemForMeasure(measure) == system; measure++)
        {
            var firstCell = measure == measureIndex ? cellIndex + 1 : 0;
            var cells = VoiceCells(track.Measures[measure], voice);
            for (var cell = firstCell; cell < cells.Count; cell++)
            {
                var next = cells[cell].Notes.FirstOrDefault(candidate => candidate.StringIndex == stringIndex);
                if (next is null) continue;
                if (next.Techniques.Contains("HOPODestination")) return (measure, cell, next);
            }
        }
        return null;
    }

    private (int Measure, int Cell, TabNote Note)? FindLegacyHopoPhraseEnd(int measureIndex, int cellIndex, int stringIndex, int voice = 0)
    {
        var track = _host.Track;
        if (track is null) return null;
        var system = _layout.GetLayout(track).SystemForMeasure(measureIndex);
        (int Measure, int Cell, TabNote Note)? last = null;
        for (var measure = measureIndex; measure < track.Measures.Count && _layout.GetLayout(track).SystemForMeasure(measure) == system; measure++)
        {
            var firstCell = measure == measureIndex ? cellIndex + 1 : 0;
            var cells = VoiceCells(track.Measures[measure], voice);
            for (var cell = firstCell; cell < cells.Count; cell++)
            {
                var next = cells[cell].Notes.FirstOrDefault(candidate => candidate.StringIndex == stringIndex);
                if (next is null) continue;
                if (!next.Techniques.Contains("HOPO")) return last ?? (measure, cell, next);   // a single marked note slurs to the next note
                last = (measure, cell, next);
            }
        }
        return last;
    }

    private (int Measure, int Cell, TabNote Note)? FindPreviousTabNoteOnString(int measureIndex, int cellIndex, int stringIndex, int voice = 0)
    {
        var track = _host.Track;
        if (track is null) return null;
        for (var measure = Math.Min(measureIndex, track.Measures.Count - 1); measure >= 0; measure--)
        {
            var cells = VoiceCells(track.Measures[measure], voice);
            var firstCell = measure == measureIndex ? Math.Min(cellIndex - 1, cells.Count - 1) : cells.Count - 1;
            for (var cell = firstCell; cell >= 0; cell--)
            {
                var previous = cells[cell].Notes.FirstOrDefault(candidate => candidate.StringIndex == stringIndex);
                if (previous is not null) return (measure, cell, previous);
            }
        }
        return null;
    }

    internal static double CellStartSlots(MeasureModel measure, int cellIndex, IReadOnlyList<TabCell>? voiceCells = null)
    {
        return CursorPositions.DrawnStart(voiceCells ?? measure.Cells, cellIndex);   // the x the beat's notes are drawn at, also in an overfull bar
    }
}
