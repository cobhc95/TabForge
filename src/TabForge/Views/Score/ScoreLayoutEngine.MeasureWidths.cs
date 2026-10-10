using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views.Score;

// Owns: the natural width of a measure: each beat's room for glyphs, accidentals, ghost brackets, beat text and grace notes.
// Does not own: the layout pass that places the measures (ScoreLayoutEngine.cs) and the drawing.
// Tests: listed in docs/feature-map/editing-and-notation.md.
internal sealed partial class ScoreLayoutEngine
{
    internal double NaturalMeasureWidth(TrackModel track, MeasureModel measure, int measureIndex,
        IReadOnlyList<PalmMutePassage> palmMutePassages)
    {
        var slots = SlotsFor(measureIndex);
        var numerator = measure.TimeSigNum ?? _host.Project?.TimeSignatureNumerator ?? 4;
        var denominator = measure.TimeSigDenom ?? _host.Project?.TimeSignatureDenominator ?? 4;
        var keySignature = measure.KeySignature ?? _host.Project?.KeySignature ?? 0;
        var notation = _staff.CreateLayout(track, measure, measureIndex, slots, 0, 0, 1,
            numerator, denominator, keySignature, _host.Appearance.ScoreSpacing);
        var notationBeats = notation.Beats.AsEnumerable();
        ScanMarkExtents(notation, measure);
        if (Voice2HasContent(measure))
        {
            var voice2 = _staff.CreateLayout(track, measure, measureIndex, slots, 0, 0, 1,
                numerator, denominator, keySignature, _host.Appearance.ScoreSpacing, measure.Voice2Cells);
            ScanMarkExtents(voice2, measure);
            notationBeats = notationBeats.Concat(voice2.Beats).OrderBy(beat => beat.StartSlots);
        }

        var events = new List<MeasureEventWidth>();
        foreach (var beat in notationBeats)
        {
            if (events.Count == 0 || Math.Abs(events[^1].StartSlots - beat.StartSlots) > 0.001)
                events.Add(new MeasureEventWidth(beat.StartSlots, beat.StartSlots + beat.DurationSlots, 7, 7));
            var eventIndex = events.Count - 1;
            var current = events[eventIndex];
            var left = current.Left;
            var right = current.Right;

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

            var ghostRoom = StaffNotationRenderer.GhostRoom(notation.StaffTop, beat);   // voice 2 shares the staff top
            foreach (var note in beat.Notes)
            {
                var offset = note.X - beat.CenterX;
                left = Math.Max(left, 6 - offset);
                right = Math.Max(right, 6 + offset);
                if (note.Accidental is not null)
                {
                    var accidentalWidth = ScoreText.MakeText(note.Accidental, 15, ScoreText.Brush(Colors.White)).Width;
                    left = Math.Max(left, 12.1 + note.AccidentalColumn * 10 + accidentalWidth / 2 - (beat.Notes.Min(n => n.X) - beat.CenterX) + ghostRoom); // accidentals hang off the chord's leftmost head, outside any ghost bracket
                }

                if (note.Source.Ghost) { left = Math.Max(left, 11 + ghostRoom - offset); right = Math.Max(right, 3 + ghostRoom + offset); }   // the ghost brackets
                var fret = note.Source.Dead ? "X"
                    : track.Kind == TrackKind.Drums ? DrumMaps.For(track, note.Source.MidiValue > 0 ? note.Source.MidiValue : note.Source.Fret).Label
                    : note.Source.Fret.ToString(CultureInfo.InvariantCulture);
                var fretWidth = ScoreText.MakeTextIn(ScoreTextArea.Fret, fret, _host.FretFontSize, ScoreText.Brush(Colors.White), FontWeights.Normal, "Consolas").Width;
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

            var techniqueLabel = ScoreMarkText.TechniqueLabel(beat.Cell.Notes, includeFade: false);
            if (techniqueLabel.Length > 0)
            {
                var width = ScoreText.MakeTextIn(ScoreTextArea.Technique, techniqueLabel, 9, ScoreText.Brush(Colors.White)).Width;
                left = Math.Max(left, width / 2 + 2);
                right = Math.Max(right, width / 2 + 2);
            }

            if (!string.IsNullOrWhiteSpace(beat.Cell.ChordName))
            {
                var width = ScoreText.MakeTextIn(ScoreTextArea.Chord, beat.Cell.ChordName!, 10, ScoreText.Brush(Colors.White), FontWeights.SemiBold).Width;
                left = Math.Max(left, width / 2 + 3);
                right = Math.Max(right, width / 2 + 3);
            }
            // Beat text (comments above the staff) runs over neighbouring beats; letting it
            // reserve its full width stretched a bar with a long comment across the whole line.
            if (!string.IsNullOrWhiteSpace(beat.Cell.Lyrics))
            {
                var width = beat.Cell.Lyrics.Split('\n').Max(text => ScoreText.MakeTextIn(ScoreTextArea.Lyrics, text, 10, ScoreText.Brush(Colors.White)).Width);
                left = Math.Max(left, width / 2 + 3);
                right = Math.Max(right, width / 2 + 3);
            }
            if (DynamicMarks.TryGetValue(beat.Cell, out var dynamicName))
            {
                // The marking is centred under the beat: keep neighbouring markings and notes apart.
                var width = ScoreText.DynamicText(dynamicName, Colors.White).Width;
                left = Math.Max(left, width / 2 + 2);
                right = Math.Max(right, width / 2 + 2);
            }
            if (beat.Cell.Fermata || beat.Cell.IsGrace) right = Math.Max(right, 13);
            if (beat.GraceNotes.Count > 0) left = Math.Max(left, StaffNotationRenderer.GraceOffset(beat, StaffNotationRenderer.GhostRoom(notation.StaffTop, beat)) + 9 * (Math.Min(3, beat.GraceNotes.Count) - 1) + 6 + (beat.Cell.Notes.Any(g => ScoreMarkText.GraceTransitionShown(track, beat.Cell, g)) ? 6 : 0));   // a line / arc between the grace fret and the main fret needs room
            if (beat.Cell.Dots > 0) right = Math.Max(right, 11 + beat.Cell.Dots * 4);

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

        // The extender is phrase-level, so reserve room only for the single P.M. label that starts
        // each contiguous effect run. Reserving it on every flagged note needlessly stretches bars.
        var labelWidth = ScoreText.MakeTextIn(ScoreTextArea.Technique, "P.M.", 9, ScoreText.Brush(Colors.White)).Width;
        foreach (var passage in palmMutePassages)
        {
            if (passage.FirstMeasure != measureIndex) continue;
            var eventIndex = events.FindIndex(item => Math.Abs(item.StartSlots - passage.FirstStartSlots) < 0.001);
            if (eventIndex < 0) continue;
            var item = events[eventIndex];
            events[eventIndex] = item with { Left = Math.Max(item.Left, labelWidth / 2 + 12) };
        }

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
            lead = Math.Max(lead, 22 + ScoreText.MakeTextIn(ScoreTextArea.BarInfo, tempoText, 9, ScoreText.Brush(Colors.White), FontWeights.Bold).Width + 6);
        var sectionTitle = MarkerForMeasure(measureIndex)?.Title ?? measure.SectionName;
        if (_host.Appearance.ShowSectionHeadings && !string.IsNullOrWhiteSpace(sectionTitle))
            lead = Math.Max(lead, ScoreText.MakeTextIn(ScoreTextArea.BarInfo, sectionTitle, 10, ScoreText.Brush(Colors.White), FontWeights.Bold).Width + 8);
        if (measure.RepeatStart) lead = Math.Max(lead, 22);
        if (measure.AlternateEnding > 0) lead = Math.Max(lead, 34);
        var tripletFeel = TripletFeels.Effective(measure);
        if (tripletFeel != TripletFeels.None)
            lead = Math.Max(lead, ScoreText.MakeTextIn(ScoreTextArea.BarInfo, ScoreMarkText.SwingSymbol(tripletFeel), 9, ScoreText.Brush(Colors.White)).Width + 8);
        var trail = measure.RepeatEnd
            ? ScoreText.MakeTextIn(ScoreTextArea.BarInfo, $"×{measure.RepeatCount}:|", 10, ScoreText.Brush(Colors.White), FontWeights.Bold).Width + 8
            : 10;
        if (measure.IsDoubleBar) trail += 4;
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

    internal readonly record struct ScoreFacts(IReadOnlyList<PalmMutePassage> PalmMutePassages,
        IReadOnlyList<FadePassage> FadePassages);

    private readonly record struct StaffLayoutCacheKey(TrackModel Track, MeasureModel Measure,
        IReadOnlyList<TabCell> Cells, int MeasureIndex, int Slots, int Numerator, int Denominator,
        int KeySignature, double X, double StaffTop, double SlotWidth, double StaffScale, MeasureWarp Warp);

    private readonly record struct MeasureEventWidth(
        double StartSlots, double EndSlots, double Left, double Right, double ComplexityPadding = 0);

}

