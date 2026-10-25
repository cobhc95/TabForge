using System.Globalization;
using System.Linq;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge.Views.Score;

// Owns: the drawing of one bar (DrawMeasure) with its staff notation call, rest beats, vibrato lines and the fret numbers on the TAB.
// Does not own: the system frame and per-bar overlays (ScoreRenderer.cs), the bar furniture (ScoreRenderer.Bars.cs) or the marks (ScoreRenderer.Marks.cs).
// Tests: no dedicated test class.

internal sealed partial class ScoreRenderer
{
    private void DrawMeasure(IDrawContext d, TrackModel track, MeasureModel measure, int measureIndex, double x, double measureWidth, double staffTop, double tabTop, double slotWidth, int slots, int strings, UiColor ink, UiColor faint, UiColor staffLine, UiColor accent, UiColor playColor, bool showStaff, bool showTab,
        IReadOnlyList<TabCell> cells, bool inactiveVoice)
    {
        // A simile bar shows only its sign (the notes behind it are a copy kept for playback and export).
        if (SimileHidesNotes(measure, measureIndex)) cells = Array.Empty<TabCell>();
        cells = DrawnCells(measure, cells, slots);
        if (inactiveVoice)
        {
            ink = Palette.Get("ScoreInactiveVoiceInk", light: !_host.Appearance.DarkPaper);
            faint = Palette.Get("ScoreInactiveVoiceFaint", light: !_host.Appearance.DarkPaper);
        }
        _bendLabelBoxes.Clear();
        _drawnBendLabels.Clear();
        _pendingBendLabels.Clear();
        var bg = _host.Appearance.PaperColor;
        var numerator = measure.TimeSigNum ?? _host.Project?.TimeSignatureNumerator ?? 4;
        var denominator = measure.TimeSigDenom ?? _host.Project?.TimeSignatureDenominator ?? 4;
        var keySignature = measure.KeySignature ?? _host.Project?.KeySignature ?? 0;
        var layout = _layout.StaffLayoutFor(track, measure, measureIndex, slots, x, staffTop, slotWidth,
            numerator, denominator, keySignature, cells);
        layout.Skyline = _sky;
        layout.HarmonicCaptionsOnTab = showTab;   // the reference prints "Harm." just above the TAB fret, not under the staff
        layout.FirstVoice = layout.IsSecondVoice ? _layout.CachedStaffLayout(measureIndex, 0) : null;
        layout.ContentLeft = SignatureRight(track, measure, measureIndex, x);
        if (showStaff) DrawDynamics(d, track, measure, measureIndex, layout, staffTop, tabTop, strings, ink, showStaff);   // nearest the staff: claims its row first
        if (showStaff) ScoreCursorBox.DrawStaff(d, _host, layout, measureIndex, ink);
        if (showStaff)
            _staff.DrawMeasure(d, layout, measureIndex, ink, faint, accent, playColor, bg, staffLine, _host.Appearance.LedgerLines,
                _host.PlaybackActive ? _host.SoundingNotes : _noNotes, _host.PlaybackActive ? _host.StruckNotes : _noNotes);   // the green note glow is a playback mark only
        if (showTab)
            DrawTabSlides(d, track, measureIndex, layout, cells, strings, tabTop, ink);

        foreach (var beat in layout.Beats)
        {
            var cell = beat.Cell;
            if (cell.IsRest && cell.Notes.Count == 0)
            {
                DrawRestBeat(d, measure, cells, layout, beat, x, measureWidth, staffTop, tabTop, strings, ink, faint, accent, showStaff, showTab);
                continue;
            }

            DrawNotedBeat(d, track, measure, measureIndex, cells, layout, beat, x, measureWidth, slots, staffTop, tabTop, strings, ink, faint, accent, bg, playColor, showStaff, showTab);
        }
        if (showTab) DrawLetRingSpans(d, layout, x + measureWidth, tabTop, ink, showStaff);
        if (!showStaff) DrawDynamics(d, track, measure, measureIndex, layout, staffTop, tabTop, strings, ink, showStaff);
        FlushBendLabels(d);
    }

    /// <summary>The x the bar content may start at, right of the clef, key and time signature of the bar; negative infinity when no notation is shown.</summary>
    private double SignatureRight(TrackModel track, MeasureModel measure, int measureIndex, double x)
    {
        var signatureRight = double.NegativeInfinity;
        if (_host.Notation != NotationMode.TabOnly && _host.Project is not null)
        {
            var afterSignatures = x + 28 + (ScoreClefKey.ClefChanges(track, measureIndex) ? ScoreClefKey.ClefChangeWidth : 0) + (_layout.KeySignatureChanges(track, measureIndex) ? _layout.KeySignatureWidth(track, measureIndex) : 0);
            if (_layout.TimeSignatureShown(track, measureIndex)) afterSignatures += _layout.TimeSignatureWidth(measure) + 4;
            if (afterSignatures > x + 28) signatureRight = afterSignatures;
        }
        return signatureRight;
    }

    /// <summary>A beat with notes: chord name, vibrato, mix point, texts, fermata, accent marks, lyrics, the technique label and the fret numbers.</summary>
    private void DrawNotedBeat(IDrawContext d, TrackModel track, MeasureModel measure, int measureIndex, IReadOnlyList<TabCell> cells, StaffNotationMeasureLayout layout, StaffNotationBeat beat, double x, double measureWidth, int slots, double staffTop, double tabTop, int strings, UiColor ink, UiColor faint, UiColor accent, UiColor bg, UiColor playColor, bool showStaff, bool showTab)
    {
        var cell = beat.Cell;
        var cx = beat.CenterX;
        var techniqueLabel = showTab ? ScoreMarkText.DrawnTechniqueLabel(cell.Notes) : "";
        if (!string.IsNullOrWhiteSpace(cell.ChordName))
        {
            var chordFt = ScoreText.MakeTextIn(d, ScoreTextArea.Chord, cell.ChordName!, 10, accent, 600);
            StackTextAbove(d, chordFt, 10, cx - chordFt.Width / 2, staffTop, 14);
        }
        // The reference vibrato: a wavy line along the note's duration, above the staff and above the TAB.
        DrawBeatVibrato(d, measure, measureIndex, cells, layout, beat, x, measureWidth, slots, staffTop, tabTop, showStaff, showTab, ink);
        // Mix Table point (F10): a red marker with a white core above the beat dot.
        if (cell.Mix is not null)
        {
            var mixY = _sky.PlaceAbove(cx - 4.2, cx + 4.2, 8.4, staffTop - 24) + 4.2;
            d.Ellipse(Palette.Get("ScoreMixMarker", light: !_host.Appearance.DarkPaper), null, cx, mixY, 4.2, 4.2);
            d.Ellipse(Palette.Get("White"), null, cx, mixY, 1.5, 1.5);
        }
        if (!string.IsNullOrWhiteSpace(cell.Text))
        {
            // Beat text stacks above the staff's marks like every other text; the bar's title and tempo stack above it.
            var textFt = ScoreText.MakeTextIn(d, ScoreTextArea.Lyrics, cell.Text!, ScoreMarkText.BeatTextSize, ScoreText.BeatTextColor(ink, faint));
            StackTextAbove(d, textFt, ScoreMarkText.BeatTextSize, Math.Clamp(cx - textFt.Width / 2, x + 2, Math.Max(x + 2, x + measureWidth - textFt.Width - 2)), staffTop, 14);
        }
        if (cell.Fermata && !showStaff && !layout.FermataSharedWithFirstVoice(beat)) ScoreText.DrawCenteredIn(ScoreTextArea.Technique, d, "𝄐", cx, _sky.PlaceAbove(cx - 6, cx + 6, 14, tabTop - 10), 12, ink);   // tab only: one fermata per onset
        if (cell.Accent != 0 && !showStaff)
        {
            // With a notation staff the accent is engraved above the note there; tab-only shows it here.
            var accentY = _sky.PlaceAbove(cx - 5, cx + 5, 12, tabTop - 10);
            ScoreText.DrawCenteredIn(ScoreTextArea.Technique, d, cell.Accent == 2 ? "^" : ">", cx, accentY, 11, ink, 700);
        }
        // With a notation staff the renderer engraves staccato / tenuto beside the note head; tab-only shows them here.
        if (cell.Staccato && !showStaff) ScoreText.DrawCenteredIn(ScoreTextArea.Technique, d, "•", cx, _sky.PlaceAbove(cx - 4, cx + 4, 10, tabTop - 10), 10, ink);
        if (cell.Tenuto && !showStaff) ScoreText.DrawCenteredIn(ScoreTextArea.Technique, d, "—", cx, _sky.PlaceAbove(cx - 5, cx + 5, 10, tabTop - 10), 10, ink);
        if (cell.IsGrace && !cell.Notes.Any(n => n.IsGraceNote))
        {
            var grFt = ScoreText.MakeTextIn(d, ScoreTextArea.Technique, "gr", 8, faint);
            StackTextAbove(d, grFt, 8, cx - grFt.Width / 2, staffTop, 14);
        }

        // Lyrics sit under the TAB staff, one line per row (the reference layout).
        if (!string.IsNullOrWhiteSpace(cell.Lyrics))
        {
            var lines = cell.Lyrics.Split('\n');
            // The row starts under the fingering of every beat the text runs across, not only its own.
            var lyricWidth = lines.Max(l => ScoreText.MakeTextIn(d, ScoreTextArea.Lyrics, l, 10, ink).Width);
            var fingeringUnder = ScoreMarkText.FingeringHeight(cell);
            foreach (var other in layout.Beats)
                if (Math.Abs(other.CenterX - cx) < lyricWidth / 2 + 8) fingeringUnder = Math.Max(fingeringUnder, ScoreMarkText.FingeringHeight(other.Cell));
            for (var li = 0; li < Math.Min(lines.Length, 3); li++)
                ScoreText.DrawCenteredIn(ScoreTextArea.Lyrics, d, lines[li], cx, tabTop + (strings - 1) * _host.StringGap + 13 + fingeringUnder + li * 14.5, 10, ink);
        }

        DrawBeatTabFrets(d, track, measure, measureIndex, cells, beat, tabTop, strings, ink, bg, playColor, showTab);
        if (techniqueLabel.Length > 0)
        {
            // One complete, width-reserved annotation per beat prevents chord techniques
            // from being overprinted and keeps simultaneous marks together.
            var hasPalmMute = cell.Notes.Any(ScoreMarkText.ShowsPalmMute);
            var techniqueY = tabTop - (hasPalmMute && !showStaff ? 34 : 20);
            var techniqueFt = ScoreText.MakeTextIn(d, ScoreTextArea.Technique, techniqueLabel, 9, ink,
                techniqueLabel.Split(' ').Contains("T") ? 600 : 400);
            // Between the staff and the TAB the label stacks upward from the TAB, clear of whatever the staff side claimed.
            if (showStaff) techniqueY = _sky.PlaceAbove(cx - techniqueFt.Width / 2, cx + techniqueFt.Width / 2, 11, tabTop - 9) - 1;
            else techniqueY = _sky.PlaceAbove(cx - techniqueFt.Width / 2, cx + techniqueFt.Width / 2, 11, tabTop - 9);
            ScoreText.DrawFormatted(d, techniqueFt, cx - techniqueFt.Width / 2, techniqueY);
        }
        if (showTab)
        {
            // Voice 2's marks under the TAB go below voice 1's at the same beat.
            var voiceOneUnder = 0.0;
            if (layout.IsSecondVoice && _layout.CachedStaffLayout(measureIndex, 0) is { } voiceOne)
                foreach (var other in voiceOne.Beats)
                    if (Math.Abs(other.CenterX - cx) < 14) voiceOneUnder = Math.Max(voiceOneUnder, ScoreMarkText.FingeringExtent(other.Cell) > 0 ? ScoreMarkText.FingeringExtent(other.Cell) - 8 : 0);
            DrawTabBeatMarks(d, track, layout, beat, x + measureWidth, tabTop, strings, ink, showStaff, voiceOneUnder);
        }
    }

    /// <summary>A rest beat: its rest sign (tab only), chord name, beat text and fermata.</summary>
    private void DrawRestBeat(IDrawContext d, MeasureModel measure, IReadOnlyList<TabCell> cells, StaffNotationMeasureLayout layout, StaffNotationBeat beat, double x, double measureWidth, double staffTop, double tabTop, int strings, UiColor ink, UiColor faint, UiColor accent, bool showStaff, bool showTab)
    {
        var cell = beat.Cell;
        var i = beat.CellIndex;
        var cx = beat.CenterX;
        if (showTab && !showStaff && !layout.IsSecondVoice) // The reference shows the rest only on the staff when notation is visible
        {
            // A fret of the other voice at the same beat sits at the rest's height: the rest moves up, clear of the topmost such fret.
            var restY = tabTop + (strings - 1) * _host.StringGap / 2.0 - 9;
            var otherCells = ReferenceEquals(cells, measure.Voice2Cells) ? measure.Cells : measure.Voice2Cells;
            var restAt = cell.RhythmicPosition ?? i;
            var sameBeat = otherCells.Where((o, k) => o.Notes.Count > 0 && Math.Abs((o.RhythmicPosition ?? k) - restAt) < 0.01).SelectMany(o => o.Notes).ToList();
            if (sameBeat.Count > 0)
                restY = Math.Min(restY, tabTop + sameBeat.Min(n => n.StringIndex) * _host.StringGap - 27);
            ScoreText.DrawCentered(d, StaffNotationRenderer.RestGlyph(cell), cx, restY, 14, faint);
        }
        // A rest can still carry a chord name, beat text and a fermata (a held rest).
        if (!string.IsNullOrWhiteSpace(cell.ChordName))
        {
            var restChordFt = ScoreText.MakeTextIn(d, ScoreTextArea.Chord, cell.ChordName!, 10, accent, 600);
            StackTextAbove(d, restChordFt, 10, cx - restChordFt.Width / 2, staffTop, 14);
        }
        if (!string.IsNullOrWhiteSpace(cell.Text))
        {
            var restTextFt = ScoreText.MakeTextIn(d, ScoreTextArea.Lyrics, cell.Text!, ScoreMarkText.BeatTextSize, ScoreText.BeatTextColor(ink, faint));
            StackTextAbove(d, restTextFt, ScoreMarkText.BeatTextSize, Math.Clamp(cx - restTextFt.Width / 2, x + 2, Math.Max(x + 2, x + measureWidth - restTextFt.Width - 2)), staffTop, 14);
        }
        if (cell.Fermata && !showStaff && !layout.FermataSharedWithFirstVoice(beat)) ScoreText.DrawCenteredIn(ScoreTextArea.Technique, d, "𝄐", cx, _sky.PlaceAbove(cx - 6, cx + 6, 14, tabTop - 10), 12, ink);   // tab only: one fermata per onset
    }

    /// <summary>The wavy vibrato line along the beat's duration, above the staff and above the TAB.</summary>
    private void DrawBeatVibrato(IDrawContext d, MeasureModel measure, int measureIndex, IReadOnlyList<TabCell> cells, StaffNotationMeasureLayout layout, StaffNotationBeat beat, double x, double measureWidth, int slots, double staffTop, double tabTop, bool showStaff, bool showTab, UiColor ink)
    {
        var cell = beat.Cell;
        var cx = beat.CenterX;
    var vibratoWide = cell.Notes.Any(n => n.Techniques.Contains("WideVibrato"));
    if (HasVibrato(cell))
    {
        var right = beat.CenterX + Math.Max(18, beat.DurationSlots * (measureWidth / Math.Max(1, slots)) * 0.8);
        right = Math.Max(Math.Min(right, x + measureWidth - 3), beat.CenterX + 8);   // the line ends inside its bar (and so inside the page)
        // Consecutive vibrato notes share one unbroken line, as in the reference: this piece runs on to where the next one starts.
        // The skyline claim stops short of the join so the next piece is placed on the same row.
        var claimRight = right;
        if (VibratoRunsOnTo(measureIndex, layout, beat, ReferenceEquals(cells, measure.Voice2Cells) ? 1 : 0) is { } joined) (right, claimRight) = (joined, Math.Max(cx, joined - 6));
        // Stack above whatever already sits over the note instead of drawing across it:
        // staff: above the chord-name / text lanes when present; TAB: above the technique label
        // and the P.M. lane.
        // Above the staff the next bar's marks (its number, high notes) set its own row: the staff piece stops at the barline.
        var staffRight = Math.Min(right, x + measureWidth - 3);
        var staffLane = staffTop - 16;
        if (showStaff)
        {
            // Stacked above the staff's marks for this column (the wavy line is about 8 px tall).
            var vibTop = _sky.PlaceAbove(cx - 6, Math.Min(claimRight, staffRight), vibratoWide ? 9 : 7, staffTop - 8);
            staffLane = vibTop + (vibratoWide ? 4.5 : 3.5);
        }
        // TAB: its own row above the P.M. lane and anything else already stacked over the beat.
        var tabLane = showTab ? _sky.PlaceAbove(cx - 6, claimRight, vibratoWide ? 9 : 7, tabTop - 8) + (vibratoWide ? 4.5 : 3.5) : 0;
        if (showStaff) DrawVibratoLine(d, cx - 6, staffRight, staffLane, vibratoWide, ink);
        if (showTab) DrawVibratoLine(d, cx - 6, right, tabLane, vibratoWide, ink);
    }
    }

    /// <summary>The fret numbers of one beat on the TAB: the played / selected chip behind each, grace frets, hammer-on slurs.</summary>
    private void DrawBeatTabFrets(IDrawContext d, TrackModel track, MeasureModel measure, int measureIndex, IReadOnlyList<TabCell> cells, StaffNotationBeat beat, double tabTop, int strings, UiColor ink, UiColor bg, UiColor playColor, bool showTab)
    {
        var cell = beat.Cell;
        var i = beat.CellIndex;
        var cx = beat.CenterX;
        foreach (var note in cell.Notes)
        {
            if (note.StringIndex < 0 || note.StringIndex >= strings) continue;
            var isSounding = _host.PlaybackActive && _host.SoundingNotes.Contains((measureIndex, i, note.StringIndex));
            var isStruck = _host.PlaybackActive && _host.StruckNotes.Contains((measureIndex, i, note.StringIndex));
            // The reference prints no fret number for a tied-to note in the TAB (the tie is in the notation), not even under the cursor.
            // Tab only: there is no notation to show the tie, so the number stays.
            var tiedTo = (note.Tied || cell.IsTied) && !note.IsGraceNote && track.Kind != TrackKind.Drums && _host.Notation != NotationMode.TabOnly;
            if (showTab && !tiedTo)
            {
                var sy = tabTop + note.StringIndex * _host.StringGap;
                var label = note.Dead ? "X"
                    : track.Kind == TrackKind.Drums ? DrumMaps.For(track, note.MidiValue > 0 ? note.MidiValue : note.Fret).Label
                    : note.Fret.ToString(CultureInfo.InvariantCulture);
                if (track.Kind == TrackKind.Drums)
                {
                    // Several drum sounds on one line at one beat (e.g. two cymbals) share one label instead of overprinting.
                    var sameLine = cell.Notes.Where(o => o.StringIndex == note.StringIndex).ToList();
                    if (sameLine[0] != note) continue;
                    if (sameLine.Count > 1)
                        label = string.Join("\n", sameLine.Select(o => DrumMaps.For(track, o.MidiValue > 0 ? o.MidiValue : o.Fret).Label));
                }
                if (note.Ghost && !note.Dead) label = "(" + label + ")"; // The reference: a ghost note is the fret in brackets
                var isGraceNote = note.IsGraceNote && cell.Notes.Any(other => !other.IsGraceNote);
                var brush = ink;
                var ft = ScoreText.MakeTextIn(d, ScoreTextArea.Fret, label, isGraceNote ? _host.FretFontSize - 3 : label.Contains('\n') ? _host.FretFontSize - 4 : _host.FretFontSize, brush, 400, "Consolas");
                // a grace fret sits small, just before the main fret: clear of the widest main fret number (two digits, brackets) by a gap of at least 2.4 px
                var mainChars = isGraceNote ? cell.Notes.Where(o => !o.IsGraceNote).Max(o => Math.Max(1, o.Fret.ToString().Length) + (o.Ghost && !o.Dead ? 2 : 0)) : 0;
                var gx = isGraceNote ? cx - Math.Max(13, mainChars * _host.FretFontSize * 0.55 / 2 + ft.Width / 2 + 3.6 + (ScoreMarkText.GraceTransitionShown(track, cell, note) ? 6 : 0)) : cx;   // room for the line / arc to the main note
                var chipWidth = ft.Width + 2;
                var chipHeight = Math.Max(14, ft.Height + 2);

                if (!isGraceNote) DrawTabHopoSlur(d, note, measureIndex, i, cx, sy, ink, ReferenceEquals(cells, measure.Voice2Cells) ? 1 : 0);

                if (isSounding)
                {
                    // A chord stacks one pill per string; each pill is trimmed to its string's band (the midpoint to a
                    // sounding neighbour) so stacked pills tile at the same width instead of overprinting. A lone note keeps its whole pill.
                    var halfGap = _host.StringGap / 2;
                    double? clipTop = _host.SoundingNotes.Contains((measureIndex, i, note.StringIndex - 1)) ? sy - halfGap : null;
                    double? clipBottom = _host.SoundingNotes.Contains((measureIndex, i, note.StringIndex + 1)) ? sy + halfGap : null;
                    ScorePlayedChip.Draw(d, playColor, isStruck, gx, sy, chipWidth, chipHeight, clipTop, clipBottom, ScorePlayedChip.IsLight(bg));
                }
                else
                {
                    var chip = new UiRect(gx - chipWidth / 2, sy - chipHeight / 2, chipWidth, chipHeight);
                    d.Rect(bg, null, chip.X, chip.Y, chip.Width, chip.Height);
                    if (ScoreCursorBox.Selected(_host, measureIndex, i, note.StringIndex)) d.Rect(ScoreCursorBox.Highlight(_host), null, chip.X, chip.Y, chip.Width, chip.Height);
                }
                ScoreText.DrawFormatted(d, ft, gx - ft.Width / 2, sy - ft.Height / 2);
                if (isGraceNote && ScoreMarkText.GraceTransitionShown(track, cell, note)) DrawTabGraceTransition(d, cell, note, gx, ft.Width, cx, sy, tabTop, ink);
            }
        }
    }
}
