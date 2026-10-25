using System.Globalization;
using System.Linq;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views.Score;

// Owns: the natural width of a measure: each beat's room for glyphs, accidentals, ghost brackets, beat text and grace notes.
// Does not own: the layout pass that places the measures (ScoreLayoutEngine.cs) and the drawing.
// Tests: no dedicated test class.
internal sealed partial class ScoreLayoutEngine
{
    internal double NaturalMeasureWidth(TrackModel track, MeasureModel measure, int measureIndex,
        IReadOnlyList<PalmMutePassage> palmMutePassages)
    {
        var slots = SlotsFor(measureIndex);
        var notationBeats = NotationBeats(track, measure, measureIndex, slots, out var notation);
        var events = EventWidths(track, notation, notationBeats);
        ReservePalmMuteLabels(events, palmMutePassages, measureIndex);
        var lead = LeadWidth(track, measure, measureIndex);
        var trail = TrailWidth(measure);
        // Duration-based spacing (standard): gaps are weighted by duration^0.62, see MeasureWarp.
        var warp = WarpFor(track, measureIndex);
        var rhythmicSpacing = RhythmicPixelsPerSlot * _host.Appearance.ScoreSpacing;
        // Floor: sparse bars (long notes, rests, empty bars) keep at least 70% of their old width.
        var temporalWidth = (events.Count == 0 ? slots : Math.Max(warp.TotalWeight, slots * 0.7)) * rhythmicSpacing;
        var required = lead + trail;
        var gaps = new List<(double Start, double Clearance)>();
        if (events.Count > 0)
        {
            var first = events[0];
            required += MeasureWarp.Weight(first.StartSlots) * rhythmicSpacing + first.Left;
            for (var i = 1; i < events.Count; i++)
            {
                var previous = events[i - 1];
                var current = events[i];
                var onsetGap = Math.Max(0, current.StartSlots - previous.StartSlots);
                var rhythmicGap = MeasureWarp.Weight(onsetGap) * rhythmicSpacing;
                var clearanceGap = previous.Right + current.Left + 2.5;
                required += Math.Max(rhythmicGap, clearanceGap);
                if (onsetGap > 0.001) gaps.Add((previous.StartSlots, clearanceGap));
            }
            var last = events[^1];
            required += last.Right + MeasureWarp.Weight(Math.Max(0, slots - last.EndSlots)) * rhythmicSpacing;
        }

        // Note positions are mapped back to a uniform slot grid when the measure is drawn. The
        // pairwise event clearances above can enlarge a measure's natural width without guaranteeing
        // that a one-slot onset gap receives that same clearance. Keep the grid itself wide enough
        // for the densest adjacent events so beamed noteheads and their annotations cannot collapse.
        var minimumGridWidth = warp.WidthFor(gaps);
        return Math.Max(70, Math.Max(Math.Max(lead + trail + temporalWidth, required), minimumGridWidth));
    }

    /// <summary>The beats of both voices in time order, laid out at unit scale; the extents of their marks are noted on the way.</summary>
    private IEnumerable<StaffNotationBeat> NotationBeats(TrackModel track, MeasureModel measure, int measureIndex, int slots, out StaffNotationMeasureLayout notation)
    {
        var numerator = measure.TimeSigNum ?? _host.Project?.TimeSignatureNumerator ?? 4;
        var denominator = measure.TimeSigDenom ?? _host.Project?.TimeSignatureDenominator ?? 4;
        var keySignature = measure.KeySignature ?? _host.Project?.KeySignature ?? 0;
        notation = StaffNotationLayoutBuilder.CreateLayout(track, measure, measureIndex, slots, 0, 0, 1,
            numerator, denominator, keySignature, _host.Appearance.ScoreSpacing);
        var notationBeats = notation.Beats.AsEnumerable();
        ScanMarkExtents(notation, measure);
        if (Voice2HasContent(measure))
        {
            var voice2 = StaffNotationLayoutBuilder.CreateLayout(track, measure, measureIndex, slots, 0, 0, 1,
                numerator, denominator, keySignature, _host.Appearance.ScoreSpacing, measure.Voice2Cells);
            ScanMarkExtents(voice2, measure);
            notationBeats = notationBeats.Concat(voice2.Beats).OrderBy(beat => beat.StartSlots);
        }
        return notationBeats;
    }

    /// <summary>The room each onset needs left and right of its centre, from the glyphs, labels and padding of every beat that starts there.</summary>
    private List<MeasureEventWidth> EventWidths(TrackModel track, StaffNotationMeasureLayout notation, IEnumerable<StaffNotationBeat> notationBeats)
    {
        var events = new List<MeasureEventWidth>();
        foreach (var beat in notationBeats)
        {
            if (events.Count == 0 || Math.Abs(events[^1].StartSlots - beat.StartSlots) > 0.001)
                events.Add(new MeasureEventWidth(beat.StartSlots, beat.StartSlots + beat.DurationSlots, 7, 7));
            var eventIndex = events.Count - 1;
            var current = events[eventIndex];
            var left = current.Left;
            var right = current.Right;
            BeatGlyphExtents(track, notation, beat, ref left, ref right);

            var complexityPadding = ComplexityPaddingOf(beat);

            BeatLabelExtents(track, notation, beat, ref left, ref right);

            events[eventIndex] = current with
            {
                EndSlots = Math.Max(current.EndSlots, beat.StartSlots + beat.DurationSlots),
                Left = left,
                Right = right,
                ComplexityPadding = Math.Max(current.ComplexityPadding, complexityPadding)
            };
        }

        for (var i = 0; i < events.Count; i++)
        {
            var item = events[i];
            events[i] = item with
            {
                Left = item.Left + item.ComplexityPadding,
                Right = item.Right + item.ComplexityPadding
            };
        }
        return events;
    }

    /// <summary>The room the rest sign, the stem and every note head of a beat need: accidentals, ghost brackets, fret text, ties, slides, bends.</summary>
    private void BeatGlyphExtents(TrackModel track, StaffNotationMeasureLayout notation, StaffNotationBeat beat, ref double left, ref double right)
    {
        if (beat.IsRest)
        {
            left = Math.Max(left, 10);
            right = Math.Max(right, 10);
        }

        if (beat.HasStem)
        {
            var stemOffset = beat.StemX - beat.CenterX;
            left = Math.Max(left, 1 - stemOffset);
            right = Math.Max(right, 1 + stemOffset);
        }

        var ghostRoom = StaffNotationGeometry.GhostRoom(notation.StaffTop, beat);   // voice 2 shares the staff top
        foreach (var note in beat.Notes)
        {
            var offset = note.X - beat.CenterX;
            left = Math.Max(left, 6 - offset);
            right = Math.Max(right, 6 + offset);
            if (note.Accidental is not null)
            {
                var accidentalWidth = _text.Width(ScoreTextArea.General, note.Accidental, 15);
                left = Math.Max(left, 12.1 + note.AccidentalColumn * 10 + accidentalWidth / 2 - (beat.Notes.Min(n => n.X) - beat.CenterX) + ghostRoom); // accidentals hang off the chord's leftmost head, outside any ghost bracket
            }

            if (note.Source.Ghost) { left = Math.Max(left, 11 + ghostRoom - offset); right = Math.Max(right, 3 + ghostRoom + offset); }   // the ghost brackets
            var fret = note.Source.Dead ? "X"
                : track.Kind == TrackKind.Drums ? DrumMaps.For(track, note.Source.MidiValue > 0 ? note.Source.MidiValue : note.Source.Fret).Label
                : note.Source.Fret.ToString(CultureInfo.InvariantCulture);
            var fretWidth = _text.Width(ScoreTextArea.Fret, fret, _host.FretFontSize, 400, "Consolas");
            left = Math.Max(left, (fretWidth + 4) / 2);
            right = Math.Max(right, (fretWidth + 4) / 2);

            if (note.Source.Tied || beat.Cell.IsTied)
            {
                left = Math.Max(left, 12);
                right = Math.Max(right, 12);
            }
            if (TabSlideNotation.HasOutgoing(note.Source)) right = Math.Max(right, 14);
            if (TabSlideNotation.HasIncoming(note.Source)) left = Math.Max(left, 14);
            if (note.Source.Techniques.Contains("Bend") ||
                note.Source.Techniques.Contains("Harmonic") || note.Source.Techniques.Contains("ArtificialHarmonic"))
                right = Math.Max(right, 14);
        }
    }

    /// <summary>Extra air around an onset of a chord or layered notation, applied after the voices are combined so a shared beat is charged once.</summary>
    private static double ComplexityPaddingOf(StaffNotationBeat beat)
    {
        // Chords and layered notation need additional local air around an onset. This padding is
        // applied after combining voices so a shared beat is charged once, and simple notes keep
        // the original rhythmic baseline. The actual glyph extents above still dominate for wide
        // accidentals, text, and fret labels.
        var complexityPadding = Math.Min(5.0, Math.Max(0, beat.Cell.Notes.Count - 1) * 1.5);
        if (beat.Cell.Notes.Any(note => note.Dead)) complexityPadding += 0.5;
        if (beat.Cell.Notes.Any(note => note.Tied) || beat.Cell.IsTied) complexityPadding += 0.75;
        if (beat.Notes.Any(note => note.Accidental is not null)) complexityPadding += 0.5;
        if (beat.Cell.Notes.Any(note => note.Ghost)) complexityPadding += 0.5;
        if (beat.Cell.Notes.Count > 1 && beat.HasStem) complexityPadding += 0.25;
        if (beat.Cell.Notes.Count > 1 && beat.BeamGroupIndex >= 0) complexityPadding += 0.25;
        if (beat.Cell.IsTriplet || beat.Cell.TupletNumerator > 0) complexityPadding += 0.75;
        complexityPadding = Math.Min(5.0, complexityPadding);
        return complexityPadding;
    }

    /// <summary>The room the labels of a beat need: technique, chord name, text, dynamic, fermata, grace notes and dots.</summary>
    private void BeatLabelExtents(TrackModel track, StaffNotationMeasureLayout notation, StaffNotationBeat beat, ref double left, ref double right)
    {
        var techniqueLabel = ScoreMarkText.TechniqueLabel(beat.Cell.Notes, includeFade: false);
        if (techniqueLabel.Length > 0)
        {
            var width = _text.Width(ScoreTextArea.Technique, techniqueLabel, 9);
            left = Math.Max(left, width / 2 + 2);
            right = Math.Max(right, width / 2 + 2);
        }

        if (!string.IsNullOrWhiteSpace(beat.Cell.ChordName))
        {
            var width = _text.Width(ScoreTextArea.Chord, beat.Cell.ChordName!, 10, 600);
            left = Math.Max(left, width / 2 + 3);
            right = Math.Max(right, width / 2 + 3);
        }
        // Beat text (comments above the staff) runs over neighbouring beats; letting it
        // reserve its full width stretched a bar with a long comment across the whole line.
        if (!string.IsNullOrWhiteSpace(beat.Cell.Lyrics))
        {
            var width = beat.Cell.Lyrics.Split('\n').Max(text => _text.Width(ScoreTextArea.Lyrics, text, 10));
            left = Math.Max(left, width / 2 + 3);
            right = Math.Max(right, width / 2 + 3);
        }
        if (DynamicMarks.TryGetValue(beat.Cell, out var dynamicName))
        {
            // The marking is centred under the beat: keep neighbouring markings and notes apart.
            var width = _text.DynamicWidth(dynamicName);
            left = Math.Max(left, width / 2 + 2);
            right = Math.Max(right, width / 2 + 2);
        }
        if (beat.Cell.Fermata || beat.Cell.IsGrace) right = Math.Max(right, 13);
        if (beat.GraceNotes.Count > 0) left = Math.Max(left, StaffNotationGeometry.GraceOffset(beat, StaffNotationGeometry.GhostRoom(notation.StaffTop, beat)) + 9 * (Math.Min(3, beat.GraceNotes.Count) - 1) + 6 + (beat.Cell.Notes.Any(g => ScoreMarkText.GraceTransitionShown(track, beat.Cell, g)) ? 6 : 0));   // a line / arc between the grace fret and the main fret needs room
        if (beat.Cell.Dots > 0) right = Math.Max(right, 11 + beat.Cell.Dots * 4);
    }

    private void ReservePalmMuteLabels(List<MeasureEventWidth> events, IReadOnlyList<PalmMutePassage> palmMutePassages, int measureIndex)
    {
        // The extender is phrase-level, so reserve room only for the single P.M. label that starts
        // each contiguous effect run. Reserving it on every flagged note needlessly stretches bars.
        var labelWidth = _text.Width(ScoreTextArea.Technique, "P.M.", 9);
        foreach (var passage in palmMutePassages)
        {
            if (passage.FirstMeasure != measureIndex) continue;
            var eventIndex = events.FindIndex(item => Math.Abs(item.StartSlots - passage.FirstStartSlots) < 0.001);
            // Only a label at the bar start needs room (it must clear the lead); a mid-bar label sits in its own lane over the
            // previous note's slot, and the uniform slot grid would multiply any extra left room across the whole bar.
            if (eventIndex != 0) continue;
            var item = events[eventIndex];
            events[eventIndex] = item with { Left = Math.Max(item.Left, labelWidth / 2 + 12) };
        }
    }

    /// <summary>The room before the first onset: signatures, tempo, section title, repeat start, ending and swing mark.</summary>
    private double LeadWidth(TrackModel track, MeasureModel measure, int measureIndex)
    {
        var lead = 16.0;
        var keyChanges = KeySignatureChanges(track, measureIndex);
        var timeShown = TimeSignatureShown(track, measureIndex);
        var clefChanged = ScoreClefKey.ClefChanges(track, measureIndex);
        if (keyChanges || timeShown || clefChanged)
        {
            lead = 28;
            if (clefChanged) lead += ScoreClefKey.ClefChangeWidth;
            if (keyChanges) lead += KeySignatureWidth(track, measureIndex);
            if (timeShown) lead += TimeSignatureWidth(measure) + 6;
        }
        var tempoText = TempoText(measure, measureIndex);
        if (tempoText is not null)
            lead = Math.Max(lead, 22 + _text.Width(ScoreTextArea.BarInfo, tempoText, ScoreMarkText.TempoSize, 700) + 6);
        var sectionTitle = MarkerForMeasure(measureIndex)?.Title ?? measure.SectionName;
        if (_host.Appearance.ShowSectionHeadings && !string.IsNullOrWhiteSpace(sectionTitle))
            lead = Math.Max(lead, _text.Width(ScoreTextArea.BarInfo, sectionTitle, 10, 700) + 8);
        if (measure.RepeatStart) lead = Math.Max(lead, 22);
        if (measure.AlternateEnding > 0) lead = Math.Max(lead, 34);
        var tripletFeel = TripletFeels.Effective(measure);
        if (tripletFeel != TripletFeels.None)
            lead = Math.Max(lead, _text.Width(ScoreTextArea.BarInfo, ScoreMarkText.SwingSymbol(tripletFeel), 9) + 8);
            return lead;
    }

    private double TrailWidth(MeasureModel measure)
    {
        var trail = measure.RepeatEnd
            ? _text.Width(ScoreTextArea.BarInfo, $"×{measure.RepeatCount}:|", 10, 700) + 8
            : 10;
        if (measure.IsDoubleBar) trail += 4;
            return trail;
    }

    internal readonly record struct ScoreFacts(IReadOnlyList<PalmMutePassage> PalmMutePassages,
        IReadOnlyList<FadePassage> FadePassages);

    private readonly record struct StaffLayoutCacheKey(TrackModel Track, MeasureModel Measure,
        IReadOnlyList<TabCell> Cells, int MeasureIndex, int Slots, int Numerator, int Denominator,
        int KeySignature, double X, double StaffTop, double SlotWidth, double StaffScale, MeasureWarp Warp);

    private readonly record struct MeasureEventWidth(
        double StartSlots, double EndSlots, double Left, double Right, double ComplexityPadding = 0);

}

