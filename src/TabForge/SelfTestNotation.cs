using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

public static partial class SelfTest
{
    private static void TestImportedNotationLayouts(string name, SongProject project)
    {
        var renderer = new StaffNotationRenderer();
        var selectedBars = new[]
        {
            0,
            1,
            2,
            project.Tracks.Count == 0 ? 0 : project.Tracks.Max(t => t.Measures.Count) / 4,
            project.Tracks.Count == 0 ? 0 : project.Tracks.Max(t => t.Measures.Count) / 2,
            project.Tracks.Count == 0 ? 0 : project.Tracks.Max(t => t.Measures.Count) * 3 / 4,
            project.Tracks.Count == 0 ? 0 : project.Tracks.Max(t => t.Measures.Count) - 1
        }.Distinct().ToArray();
        var layouts = new List<StaffNotationMeasureLayout>();

        foreach (var track in project.Tracks)
        foreach (var bar in selectedBars.Where(bar => bar >= 0 && bar < track.Measures.Count))
        {
            var measure = track.Measures[bar];
            var numerator = measure.TimeSigNum ?? project.TimeSignatureNumerator;
            var denominator = measure.TimeSigDenom ?? project.TimeSignatureDenominator;
            var slots = MusicTime.BarSlots(project, bar);
            layouts.Add(renderer.CreateLayout(track, measure, bar, slots, 300, 40, 180.0 / slots,
                numerator, denominator, measure.KeySignature ?? project.KeySignature));
        }

        var valid = layouts.Count > 0 && layouts.All(layout =>
            layout.Beats.All(beat => double.IsFinite(beat.StartSlots) && double.IsFinite(beat.CenterX) &&
                                     beat.Notes.All(note => double.IsFinite(note.X) && double.IsFinite(note.Y))) &&
            layout.BeamSegments.All(segment => double.IsFinite(segment.X1) && double.IsFinite(segment.Y1) &&
                                               double.IsFinite(segment.X2) && double.IsFinite(segment.Y2)) &&
            layout.BeamGroups.All(group => group.Beats.Count >= 2 &&
                group.Beats.All(beat => Math.Abs(beat.BeamGroupIndex - group.Index) < 0.001)) &&
            layout.Beats.All(beat => beat.Notes.All(note => Math.Abs(note.X - beat.CenterX) <= 12)));
        Check($"{name}: engraving geometry is valid across opening, middle, and ending sections",
            valid, $"checked {layouts.Count} track/bar layouts");

        if (LocalReferenceSongs.Matches(name, "reference-a") &&
            project.Tracks.Count > 0 && project.Tracks[0].Measures.Count > 20)
        {
            const int bar21Index = 20;
            var track = project.Tracks[0];
            var bar = track.Measures[bar21Index];
            var editor = new TabEditorControl { Project = project, SelectedTrackIndex = 0 };
            var passages = TabEditorControl.BuildPalmMutePassages(track, project);
            var bar21Mute = passages.FirstOrDefault(passage => passage.FirstMeasure == bar21Index &&
                passage.FirstStartSlots == 0);
            Check("Reference song A: bar 21: palm mute is one continuous span ending with the final muted note",
                bar21Mute.FirstMeasure == bar21Index && bar21Mute.LastMeasure == bar21Index &&
                Math.Abs(bar21Mute.LastEndSlots - 8) < 0.001);

            var slots = MusicTime.BarSlots(project, bar21Index);
            var naturalWidth = editor.NaturalMeasureWidth(track, bar, bar21Index, passages);
            var bar21Layout = renderer.CreateLayout(track, bar, bar21Index, slots, 0, 40,
                naturalWidth / slots,
                bar.TimeSigNum ?? project.TimeSignatureNumerator,
                bar.TimeSigDenom ?? project.TimeSignatureDenominator,
                bar.KeySignature ?? project.KeySignature, 1.0, null,
                start => editor.WarpFor(track, bar21Index).CenterFraction(start) * naturalWidth);
            var tightBeamGaps = bar21Layout.BeamGroups.SelectMany(group => group.Beats.Zip(group.Beats.Skip(1),
                    (left, right) => (left, right)))
                .Where(pair => pair.right.StartSlots - pair.left.StartSlots <= 1.001)
                .Select(pair => pair.right.CenterX - pair.left.CenterX).ToArray();
            Check("Reference song A: bar 21: dense beamed notes retain clear horizontal spacing",
                tightBeamGaps.Length > 0 && tightBeamGaps.Min() >= 16 - 0.001,
                $"closest beamed center gap={tightBeamGaps.DefaultIfEmpty(0).Min():0.##} px");
        }

        if (LocalReferenceSongs.Matches(name, "reference-a") &&
            project.Tracks.Count > 0 && project.Tracks[0].Measures.Count > 156)
        {
            var passages = TabEditorControl.BuildFadePassages(project.Tracks[0], project);
            var fade = passages.FirstOrDefault(passage => passage.FirstMeasure == 155 &&
                Math.Abs(passage.FirstStartSlots - 4) < 0.001 && !passage.IsFadeOut);
            Check("Reference song A: bars 156-157: fade-in hairpin spans the tied continuation",
                fade.FirstMeasure == 155 && fade.LastMeasure == 156 &&
                Math.Abs(fade.LastEndSlots - 8) < 0.001);
        }

        if (LocalReferenceSongs.Matches(name, "reference-a") && project.Tracks.Count > 0 &&
            project.Tracks[0].Measures.Count >= 6)
        {
            var responsiveEditor = new TabEditorControl
            {
                Project = project,
                SelectedTrackIndex = 0,
                PageWidthOverride = 1920
            };
            var composed = responsiveEditor.GetScoreLayout();
            var openingMutePassages = TabEditorControl.BuildPalmMutePassages(project.Tracks[0], project);
            var openingWidths = Enumerable.Range(0, Math.Min(12, project.Tracks[0].Measures.Count))
                .Select(index => $"{index + 1}:{responsiveEditor.NaturalMeasureWidth(project.Tracks[0], project.Tracks[0].Measures[index], index,
                    openingMutePassages):0}");
            Log.Add("  info  reference song A: opening intrinsic widths -> " + string.Join(" ", openingWidths));
            Log.Add("  info  reference song A: 1920-DIP system composition -> " +
                string.Join(" | ", composed.Systems.Select(system =>
                    $"bars {system.FirstMeasure + 1}-{system.LastMeasure + 1} ({system.Width:0}px)")));
            Check("Reference song A: reference layout fits the four ordinary opening bars at 1920-DIP working width",
                composed.Systems[0].LastMeasure >= 3,
                $"opening system ends at bar {composed.Systems[0].LastMeasure + 1}");
            var barsFourToSix = Enumerable.Range(3, 3).Select(composed.Measure).ToArray();
            Check("Reference song A: bars 4-6 keep annotation-driven width within the system expansion cap",
                barsFourToSix.All(position => position.Width <= position.NaturalWidth *
                    ScorePageLayout.MaximumExpansionJustified + 0.001));

            var screenshotWidthEditor = new TabEditorControl
            {
                Project = project,
                SelectedTrackIndex = 0,
                PageWidthOverride = TabEditorControl.BasePageWidth
            };
            var screenshotWidthLayout = screenshotWidthEditor.GetScoreLayout();
            Log.Add("  info  reference song A: 1280-DIP reference composition -> " +
                string.Join(" | ", screenshotWidthLayout.Systems.Select(system =>
                    $"bars {system.FirstMeasure + 1}-{system.LastMeasure + 1} ({system.Width:0}px)")));
            var secondOpeningSystem = screenshotWidthLayout.Systems.Count > 1
                ? screenshotWidthLayout.Systems[1]
                : null;
            Check("Reference song A: reference-width opening systems hold at least three bars and fill the page width",
                screenshotWidthLayout.Systems[0].FirstMeasure == 0 && screenshotWidthLayout.Systems[0].LastMeasure >= 2 &&
                secondOpeningSystem is not null &&
                secondOpeningSystem.FirstMeasure == screenshotWidthLayout.Systems[0].LastMeasure + 1 &&
                Math.Abs(screenshotWidthLayout.Systems[0].Width - screenshotWidthEditor.GridWidth) < 0.5);
        }
    }

    private static void TestNotationLayout()
    {
        var renderer = new StaffNotationRenderer();

        var values = NewMeasure();
        values.Cells[0] = NotationCell(1, 60);
        values.Cells[4] = NotationCell(2, 62);
        values.Cells[8] = NotationCell(4, 64, dots: 1);
        var valueLayout = Layout(renderer, values);
        Check("whole note has no stem", !valueLayout.BeatForCell(0)!.HasStem);
        Check("stemless whole-note heads remain on their rhythmic onset",
            Math.Abs(valueLayout.BeatForCell(0)!.Notes[0].X - valueLayout.BeatForCell(0)!.CenterX) < 0.001);
        Check("half note has a stem", valueLayout.BeatForCell(4)!.HasStem);
        Check("dotted quarter duration derives from value", Math.Abs(valueLayout.BeatForCell(8)!.DurationSlots - 6) < 0.001);
        Check("whole/half/quarter rests use distinct glyphs",
            StaffNotationRenderer.RestGlyph(new TabCell { DurationDenominator = 1 }) !=
            StaffNotationRenderer.RestGlyph(new TabCell { DurationDenominator = 2 }) &&
            StaffNotationRenderer.RestGlyph(new TabCell { DurationDenominator = 2 }) !=
            StaffNotationRenderer.RestGlyph(new TabCell { DurationDenominator = 4 }));
        Eq("whole rest hangs lower than half rest",
            StaffNotationRenderer.RestCenterY(new TabCell { DurationDenominator = 1 }, 20), 47.0);
        Check("written duration maps to the correct flag count",
            new[] { (4, 0), (8, 1), (16, 2), (32, 3) }.All(value =>
                Layout(renderer, SingleDurationMeasure(value.Item1)).BeatForCell(0)!.Flags == value.Item2));

        var darkInk = System.Windows.Media.Color.FromRgb(0xE7, 0xEA, 0xEF);
        var darkPaper = System.Windows.Media.Color.FromRgb(0x15, 0x18, 0x1D);
        var softenedDarkInk = StaffNotationRenderer.EngravingInkColor(darkInk, darkPaper);
        var lightInk = System.Windows.Media.Color.FromRgb(0x11, 0x11, 0x11);
        var lightPaper = System.Windows.Media.Colors.White;
        var softenedLightInk = StaffNotationRenderer.EngravingInkColor(lightInk, lightPaper);
        Check("engraving ink is softened against both dark and light paper",
            softenedDarkInk.R < darkInk.R && softenedDarkInk.R > darkPaper.R &&
            softenedLightInk.R > lightInk.R && softenedLightInk.R < lightPaper.R);

        var combinedEffectNote = new TabNote
        {
            Techniques = new HashSet<string>(new[] { "ArtificialHarmonic", "Vibrato", "Bend" },
                StringComparer.OrdinalIgnoreCase)
        };
        var combinedEffectLabel = TabEditorControl.TechniqueLabel(new[] { combinedEffectNote });
        Check("TAB engraving keeps simultaneous harmonic, vibrato and bend labels together",
            combinedEffectLabel.Contains("A.H.", StringComparison.Ordinal) &&
            !combinedEffectLabel.Contains("~", StringComparison.Ordinal) /* vibrato is a wavy line now */ &&
            combinedEffectLabel.Contains("b", StringComparison.Ordinal));
        Check("TAB engraving distinguishes prebend from a normal bend",
            TabEditorControl.BendLabel("Prebend") == "P.B." && TabEditorControl.BendLabel("Bend") == "b");

        var slideLabelNote = new TabNote
        {
            Techniques = new HashSet<string>(new[]
                { "ShiftSlide", "SlideInBelow", "SlideOutUp", "PickSlideUp" }, StringComparer.OrdinalIgnoreCase)
        };
        var slideLabel = TabEditorControl.TechniqueLabel(new[] { slideLabelNote });
        Check("TAB slide lines replace standalone slide labels while pick slides retain their label",
            !slideLabel.Contains("/", StringComparison.Ordinal) &&
            !slideLabel.Contains("↗", StringComparison.Ordinal) &&
            slideLabel.Contains("P.S.↑", StringComparison.Ordinal));

        var slideMeasure = NewMeasure();
        slideMeasure.Cells[0] = new TabCell
        {
            DurationDenominator = 8,
            Notes = new List<TabNote>
            {
                new() { StringIndex = 0, Fret = 3, MidiValue = 60, SlideTargetMidi = 62, Techniques = { "ShiftSlide" } },
                new() { StringIndex = 2, Fret = 2, MidiValue = 50, SlideTargetMidi = 52, Techniques = { "LegatoSlide" } }
            }
        };
        slideMeasure.Cells[1] = NotationCell(8, 70);
        slideMeasure.Cells[1].Notes[0].StringIndex = 3;
        slideMeasure.Cells[2] = new TabCell
        {
            DurationDenominator = 8,
            Notes = new List<TabNote>
            {
                new() { StringIndex = 0, Fret = 5, MidiValue = 62 },
                new() { StringIndex = 2, Fret = 4, MidiValue = 52 }
            }
        };
        slideMeasure.Cells[4] = NotationCell(8, 64);
        slideMeasure.Cells[4].Notes[0].Techniques.Add("SlideInBelow");
        slideMeasure.Cells[6] = NotationCell(8, 65);
        slideMeasure.Cells[6].Notes[0].Techniques.Add("SlideOutDown");
        var slideTrack = new TrackModel { Kind = TrackKind.Guitar, Measures = new List<MeasureModel> { slideMeasure } };
        var slideMarks = TabSlideNotation.ForMeasure(slideTrack, 0, 0);
        var connectedSlides = slideMarks.Where(mark => mark.Kind == TabSlideMarkKind.Connection).ToArray();
        Check("chord slides resolve to the next destination on each matching string",
            connectedSlides.Length == 2 && connectedSlides.All(mark => mark.Target is not null &&
                mark.Target.StringIndex == mark.Source.StringIndex && mark.TargetMeasureIndex == 0 &&
                mark.TargetCellIndex == 2 && mark.Target.MidiValue == mark.Source.SlideTargetMidi));
        Check("slide-in and slide-out marks resolve to compact directional glyphs",
            slideMarks.Any(mark => mark.Kind == TabSlideMarkKind.IncomingFromBelow) &&
            slideMarks.Any(mark => mark.Kind == TabSlideMarkKind.OutgoingDown));

        var simpleSpacing = NewMeasure();
        var chordSpacing = NewMeasure();
        for (var slot = 0; slot < 16; slot += 2)
        {
            simpleSpacing.Cells[slot] = NotationCell(8, 60);
            var chord = NotationCell(8, 61);
            chord.Notes[0].StringIndex = 0;
            chord.Notes.Add(new TabNote { StringIndex = 2, Fret = 12, MidiValue = 55, Dead = true });
            chord.Notes.Add(new TabNote { StringIndex = 4, Fret = 10, MidiValue = 48, Tied = slot > 0 });
            chord.Notes[0].Tied = slot > 0;
            chord.Notes[1].Tied = slot > 0;
            chordSpacing.Cells[slot] = chord;
        }
        var spacingTrack = new TrackModel
        {
            Kind = TrackKind.Guitar,
            Measures = new List<MeasureModel> { simpleSpacing, chordSpacing }
        };
        var spacingProject = new SongProject { Tracks = new List<TrackModel> { spacingTrack } };
        var spacingEditor = new TabEditorControl { Project = spacingProject, SelectedTrackIndex = 0 };
        var noMutePassages = Array.Empty<PalmMutePassage>();
        var simpleNaturalWidth = spacingEditor.NaturalMeasureWidth(spacingTrack, simpleSpacing, 0, noMutePassages);
        var chordNaturalWidth = spacingEditor.NaturalMeasureWidth(spacingTrack, chordSpacing, 1, noMutePassages);
        Check("complex chord runs receive local rhythmic space without widening simple bars globally",
            chordNaturalWidth > simpleNaturalWidth * 1.15 && simpleNaturalWidth < 260,
            $"simple={simpleNaturalWidth:0.##}px complex={chordNaturalWidth:0.##}px");

        var horizontalProject = new SongProject { Tracks = new List<TrackModel> { spacingTrack } };
        for (var extra = 0; extra < 30; extra++) spacingTrack.Measures.Add(NewMeasure());
        var horizontalEditor = new TabEditorControl { Project = horizontalProject, SelectedTrackIndex = 0, HorizontalScroll = true };
        var oneLine = horizontalEditor.GetScoreLayout(spacingTrack);
        horizontalEditor.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        Check("horizontal score scrolling lays every bar out on one line wider than a page",
            oneLine.SystemCount == 1 && horizontalEditor.DesiredSize.Width > TabEditorControl.BasePageWidth,
            $"systems={oneLine.SystemCount} width={horizontalEditor.DesiredSize.Width:0}");
        spacingTrack.Measures.RemoveRange(spacingTrack.Measures.Count - 30, 30);

        var annotatedMeasure = NewMeasure();
        annotatedMeasure.Cells[0] = NotationCell(4, 60);
        annotatedMeasure.Cells[0].Text = "If you listen closely, you can tell that...";
        var annotationWidth = spacingEditor.NaturalMeasureWidth(spacingTrack, annotatedMeasure, 0, noMutePassages);
        annotatedMeasure.Cells[0].Text = null;
        var unannotatedWidth = spacingEditor.NaturalMeasureWidth(spacingTrack, annotatedMeasure, 0, noMutePassages);
        Check("long beat text runs over neighbouring beats instead of widening its bar",
            Math.Abs(annotationWidth - unannotatedWidth) < 0.5,
            $"with text={annotationWidth:0.##}px without={unannotatedWidth:0.##}px");

        var eighths = NewMeasure();
        foreach (var slot in new[] { 0, 2, 4, 6 }) eighths.Cells[slot] = NotationCell(8, 60 + slot);
        var eighthLayout = Layout(renderer, eighths);
        Check("4/4 eighths form one beam per quarter beat",
            eighthLayout.BeamGroups.Count == 2 && eighthLayout.BeamGroups.All(g => g.Beats.Count == 2));
        Check("primary beam endpoints attach to group stems",
            eighthLayout.BeamSegments.Count == 2 && eighthLayout.BeamSegments.All(segment =>
                Math.Abs(segment.X1 - eighthLayout.BeamGroups[segment.GroupIndex].Beats[0].StemX) < 0.001 &&
                Math.Abs(segment.X2 - eighthLayout.BeamGroups[segment.GroupIndex].Beats[^1].StemX) < 0.001));

        var compound = NewMeasure();
        foreach (var slot in new[] { 0, 2, 4, 6, 8, 10 }) compound.Cells[slot] = NotationCell(8, 60 + slot);
        var compoundLayout = Layout(renderer, compound, numerator: 6, denominator: 8);
        Check("6/8 groups eighth notes by dotted-quarter pulse",
            compoundLayout.BeamGroups.Count == 2 && compoundLayout.BeamGroups.All(g => g.Beats.Count == 3));

        var additive = NewMeasure();
        for (var slot = 0; slot < 16; slot += 2) additive.Cells[slot] = NotationCell(8, 60 + slot);
        var additiveLayout = Layout(renderer, additive, numerator: 8, denominator: 8);
        Check("8/8 respects additive 3+3+2 beat grouping",
            additiveLayout.BeamGroups.Select(g => g.Beats.Count).SequenceEqual(new[] { 3, 3, 2 }));

        var sixteenths = NewMeasure();
        for (var slot = 0; slot < 4; slot++) sixteenths.Cells[slot] = NotationCell(16, 64 + slot);
        var sixteenthLayout = Layout(renderer, sixteenths);
        Check("sixteenths receive primary and subdivision-aware secondary beams",
            sixteenthLayout.BeamGroups.Count == 1 && sixteenthLayout.BeamSegments.Count(s => s.Level == 1) == 1 &&
            sixteenthLayout.BeamSegments.Count(s => s.Level == 2) == 1 &&
            sixteenthLayout.BeamSegments.Where(s => s.Level == 2).All(s => !s.IsPartial));

        var twoSixteenthBeats = NewMeasure();
        for (var slot = 0; slot < 8; slot++) twoSixteenthBeats.Cells[slot] = NotationCell(16, 60 + slot);
        var twoSixteenthBeatLayout = Layout(renderer, twoSixteenthBeats);
        Check("4/4 sixteenths group four notes per quarter beat",
            twoSixteenthBeatLayout.BeamGroups.Count == 2 &&
            twoSixteenthBeatLayout.BeamGroups.All(group => group.Beats.Count == 4) &&
            twoSixteenthBeatLayout.BeamSegments.Count(segment => segment.Level == 2) == 2);

        var thirtySeconds = NewMeasure();
        for (var i = 0; i < 8; i++)
        {
            thirtySeconds.Cells[i] = NotationCell(32, 60 + i);
            thirtySeconds.Cells[i].RhythmicPosition = i * 0.5;
        }
        var thirtySecondLayout = Layout(renderer, thirtySeconds);
        Check("32nds use three beam levels with the tertiary beam grouped by half-beat",
            thirtySecondLayout.BeamGroups.Count == 1 &&
            thirtySecondLayout.BeamSegments.Count(segment => segment.Level == 1) == 1 &&
            thirtySecondLayout.BeamSegments.Count(segment => segment.Level == 2 && !segment.IsPartial) == 1 &&
            thirtySecondLayout.BeamSegments.Count(segment => segment.Level == 3 && !segment.IsPartial) == 2);

        var chordedSixteenths = NewMeasure();
        chordedSixteenths.Cells[0] = NotationCell(16, 60);
        chordedSixteenths.Cells[0].Notes.Add(new TabNote { StringIndex = 1, MidiValue = 64 });
        chordedSixteenths.Cells[0].Notes.Add(new TabNote { StringIndex = 2, MidiValue = 67 });
        for (var slot = 1; slot < 4; slot++) chordedSixteenths.Cells[slot] = NotationCell(16, 60 + slot);
        var chordedLayout = Layout(renderer, chordedSixteenths);
        var chordBeat = chordedLayout.BeatForCell(0)!;
        var chordStemX = chordBeat.StemUp
            ? chordBeat.Notes.Max(note => note.X) + 5.04 * 0.9
            : chordBeat.Notes.Min(note => note.X) - 5.04 * 0.9;
        Check("chord tones share one stem/beam event within their voice",
            chordedLayout.BeamGroups.Count == 1 && chordedLayout.BeamGroups[0].Beats.Count == 4 &&
            chordBeat.Notes.Count == 3 && Math.Abs(chordBeat.StemX - chordStemX) < 0.001);

        var mixed = NewMeasure();
        mixed.Cells[0] = NotationCell(8, 60, dots: 1);
        mixed.Cells[3] = NotationCell(16, 62);
        var mixedLayout = Layout(renderer, mixed);
        Check("dotted eighth and sixteenth share one primary beam and a subdivision hook",
            mixedLayout.BeamGroups.Count == 1 && mixedLayout.BeamGroups[0].Beats.Count == 2 &&
            mixedLayout.BeamSegments.Count(s => s.Level == 1) == 1 &&
            mixedLayout.BeamSegments.Count(s => s.Level == 2 && s.IsPartial) == 1);

        var withRest = NewMeasure();
        for (var slot = 0; slot < 4; slot++) withRest.Cells[slot] = NotationCell(16, 60 + slot);
        withRest.Cells[4] = new TabCell { DurationDenominator = 16, IsRest = true };
        for (var slot = 5; slot < 8; slot++) withRest.Cells[slot] = NotationCell(16, 64 + slot);
        var restLayout = Layout(renderer, withRest);
        Check("explicit rests separate otherwise-beamable groups",
            restLayout.BeatForCell(4)!.IsRest && restLayout.BeamGroups.Count == 2 &&
            restLayout.BeamGroups.All(g => g.Beats.All(b => !b.IsRest)));

        var tuplets = NewMeasure();
        var tripletPositions = new[] { 0.0, 4.0 / 3.0, 8.0 / 3.0 };
        for (var i = 0; i < tripletPositions.Length; i++)
        {
            tuplets.Cells[i] = NotationCell(8, 60 + i, triplet: true);
            tuplets.Cells[i].RhythmicPosition = tripletPositions[i];
        }
        var tupletLayout = Layout(renderer, tuplets, scale: 1.3);
        Check("triplet ratios form a labeled group and one beam group",
            tupletLayout.TupletGroups.Count == 1 && tupletLayout.TupletGroups[0].Numerator == 3 &&
            tupletLayout.BeamGroups.Count == 1 && tupletLayout.BeamGroups[0].Beats.Count == 3);
        Check("fractional imported onsets align TAB and staff beat centers",
            tripletPositions.Select((position, index) =>
                Math.Abs(tupletLayout.BeatForCell(index)!.CenterX - (100 + (position + 0.5) * 10)) < 0.001).All(v => v));
        Check("staff layout keeps the configured vertical score scale", Math.Abs(tupletLayout.StaffScale - 1.3) < 0.001);
        var saved = ProjectService.Snapshot(new SongProject
        {
            Tracks = new List<TrackModel>
            {
                new() { Measures = new List<MeasureModel> { tuplets } }
            }
        });
        var restored = ProjectService.Restore(saved).Tracks[0].Measures[0].Cells[1].RhythmicPosition;
        Check("fractional imported onsets survive project save/load",
            restored is { } position && Math.Abs(position - tripletPositions[1]) < 0.0001);

        var accidentals = NewMeasure();
        accidentals.Cells[0] = NotationCell(16, 61); // C-sharp
        accidentals.Cells[1] = NotationCell(16, 61); // accidental carries through this measure
        accidentals.Cells[2] = NotationCell(16, 60); // natural cancels it
        var accidentalLayout = Layout(renderer, accidentals, kind: TrackKind.Other);
        Check("chromatic spelling and measure accidental state are coherent",
            accidentalLayout.BeatForCell(0)!.Notes[0].Accidental == "♯" &&
            accidentalLayout.BeatForCell(1)!.Notes[0].Accidental is null &&
            accidentalLayout.BeatForCell(2)!.Notes[0].Accidental == "♮");

        var inKey = NewMeasure();
        inKey.Cells[0] = NotationCell(4, 66); // F-sharp in G major
        var keyLayout = Layout(renderer, inKey, keySignature: 1, kind: TrackKind.Other);
        Check("key signature suppresses its expected accidental", keyLayout.BeatForCell(0)!.Notes[0].Accidental is null);

        var referenceClefs = new[]
        {
            (Clef: "G2", Midi: 64), // treble bottom line E4
            (Clef: "F4", Midi: 43), // bass bottom line G2
            (Clef: "C3", Midi: 53), // alto bottom line F3
            (Clef: "C4", Midi: 50)  // tenor bottom line D3
        };
        Check("treble, bass, alto, and tenor clefs place their reference notes on the bottom line",
            referenceClefs.All(reference =>
            {
                var clefMeasure = NewMeasure();
                clefMeasure.Clef = reference.Clef;
                clefMeasure.Cells[0] = NotationCell(4, reference.Midi);
                return Math.Abs(Layout(renderer, clefMeasure, kind: TrackKind.Other).BeatForCell(0)!.Notes[0].Y - 76) < 0.001;
            }));
        var octaveClef = NewMeasure();
        octaveClef.Clef = "G8vb";
        octaveClef.Cells[0] = NotationCell(4, 52);
        Check("octave-transposing clefs move sounding pitch into written register",
            Math.Abs(Layout(renderer, octaveClef, kind: TrackKind.Other).BeatForCell(0)!.Notes[0].Y - 76) < 0.001);
        var missingMidi = NewMeasure();
        missingMidi.Cells[0] = new TabCell
        {
            DurationDenominator = 4,
            Notes = new List<TabNote> { new() { StringIndex = 0, Fret = 3, MidiValue = 0 } }
        };
        Check("legacy fretted notes derive their pitch from tuning and fret",
            Layout(renderer, missingMidi, kind: TrackKind.Guitar).BeatForCell(0)!.Notes[0].WrittenMidi == 79);

        var ledger = NewMeasure();
        ledger.Cells[0] = NotationCell(4, 52); // E3 in treble clef, below the staff
        var ledgerLayout = Layout(renderer, ledger, kind: TrackKind.Other);
        var ledgerNoteY = ledgerLayout.BeatForCell(0)!.Notes[0].Y;
        var ledgerYs = StaffNotationRenderer.LedgerLinePositions(ledgerNoteY, 40);
        Check("ledger lines stop at the outside note and stay local",
            ledgerYs.Count == 3 && ledgerYs.All(y => y < ledgerNoteY && y >= 40 + 36));
        Check("notes inside the staff have no ledger lines",
            StaffNotationRenderer.LedgerLinePositions(58, 40).Count == 0);

        var chordLedger = NewMeasure();
        chordLedger.Cells[0] = NotationCell(4, 52);
        chordLedger.Cells[0].Notes.Add(new TabNote { StringIndex = 2, MidiValue = 52 });
        var chordLedgerLayout = Layout(renderer, chordLedger, kind: TrackKind.Other);
        var standardLedger = StaffNotationRenderer.LedgerLineSegments(chordLedgerLayout, LedgerLineMode.Standard);
        var minimalLedger = StaffNotationRenderer.LedgerLineSegments(chordLedgerLayout, LedgerLineMode.Minimal);
        var hiddenLedger = StaffNotationRenderer.LedgerLineSegments(chordLedgerLayout, LedgerLineMode.Hidden);
        Check("ledger modes draw complete, minimal, or no lines",
            standardLedger.Count == ledgerYs.Count && minimalLedger.Count == ledgerYs.Count && hiddenLedger.Count == 0 &&
            standardLedger.Zip(minimalLedger).All(pair => pair.First.X2 - pair.First.X1 > pair.Second.X2 - pair.Second.X1));
        Check("minimal ledger lines are only slightly wider than a notehead",
            minimalLedger.All(segment => segment.X2 - segment.X1 <= 15));
        var lightStaffLine = System.Windows.Media.Color.FromRgb(0xD5, 0xD5, 0xD5);
        var subduedLedger = StaffNotationRenderer.SubduedLedgerLineColor(lightStaffLine);
        Check("ledger lines use a translucent staff-line shade in both visible modes",
            subduedLedger.A < lightStaffLine.A && subduedLedger.R == lightStaffLine.R &&
            subduedLedger.G == lightStaffLine.G && subduedLedger.B == lightStaffLine.B);
        var unrelatedLedger = NewMeasure();
        unrelatedLedger.Cells[0] = NotationCell(4, 52);
        unrelatedLedger.Cells[8] = NotationCell(4, 52);
        Check("ledger lines from separate onsets stay separate",
            StaffNotationRenderer.LedgerLineSegments(Layout(renderer, unrelatedLedger, kind: TrackKind.Other),
                LedgerLineMode.Standard).Count == ledgerYs.Count * 2);

        var palmMuteBar = NewMeasure();
        palmMuteBar.Cells[0] = NotationCell(4, 60);
        palmMuteBar.Cells[0].Notes[0].Techniques.Add("PalmMute");
        palmMuteBar.Cells[4] = NotationCell(4, 62);
        palmMuteBar.Cells[4].Notes[0].Techniques.Add("PM");
        palmMuteBar.Cells[12] = NotationCell(8, 64);
        palmMuteBar.Cells[12].Notes[0].Techniques.Add("PalmMute");
        var palmMuteTrack = new TrackModel { Measures = new List<MeasureModel> { palmMuteBar } };
        var palmMuteProject = new SongProject { Tracks = new List<TrackModel> { palmMuteTrack } };
        var palmMutePassages = TabEditorControl.BuildPalmMutePassages(palmMuteTrack, palmMuteProject);
        Check("contiguous palm-muted notes form one ranged annotation and gaps start a new passage",
            palmMutePassages.Count == 2 && palmMutePassages[0].EventCount == 2 &&
             palmMutePassages[0].FirstMeasure == 0 && palmMutePassages[0].FirstStartSlots == 0 &&
             palmMutePassages[0].LastEndSlots == 8 && palmMutePassages[1].EventCount == 1 &&
             palmMutePassages[1].LastEndSlots == 14 &&
             palmMutePassages[1].FirstStartSlots == 12);

        var longPalmMuteMeasures = new List<MeasureModel> { NewMeasure(), NewMeasure() };
        for (var measureIndex = 0; measureIndex < longPalmMuteMeasures.Count; measureIndex++)
        for (var slot = 0; slot < 16; slot += 2)
        {
            longPalmMuteMeasures[measureIndex].Cells[slot] = NotationCell(8, 60 + slot % 7);
            longPalmMuteMeasures[measureIndex].Cells[slot].Notes[0].Techniques.Add("PalmMute");
        }
        longPalmMuteMeasures[0].Cells[0].Notes.Add(new TabNote
            { StringIndex = 1, MidiValue = 64, Techniques = { "PM" } });
        var longPalmMuteTrack = new TrackModel { Measures = longPalmMuteMeasures };
        var longPalmMuteProject = new SongProject { Tracks = new List<TrackModel> { longPalmMuteTrack } };
        var longPalmMutePassage = TabEditorControl.BuildPalmMutePassages(longPalmMuteTrack, longPalmMuteProject);
        Check("palm mute effects render as one phrase across bars and chord tones",
            longPalmMutePassage.Count == 1 && longPalmMutePassage[0].EventCount == 16 &&
            longPalmMutePassage[0].FirstMeasure == 0 && longPalmMutePassage[0].FirstStartSlots == 0 &&
            longPalmMutePassage[0].LastMeasure == 1 && longPalmMutePassage[0].LastEndSlots == 16);

        var pmLastBar = NewMeasure();
        pmLastBar.Cells[14] = NotationCell(8, 60);
        pmLastBar.Cells[14].Notes[0].Techniques.Add("PalmMute");
        var pmNextBar = NewMeasure();
        pmNextBar.Cells[0] = NotationCell(8, 62);
        pmNextBar.Cells[0].Notes[0].Techniques.Add("PalmMute");
        var crossBarTrack = new TrackModel { Measures = new List<MeasureModel> { pmLastBar, pmNextBar } };
        var crossBarProject = new SongProject { Tracks = new List<TrackModel> { crossBarTrack } };
        var crossBarPassages = TabEditorControl.BuildPalmMutePassages(crossBarTrack, crossBarProject);
         Check("palm-mute passages continue across barlines using musical duration",
             crossBarPassages.Count == 1 && crossBarPassages[0].EventCount == 2 &&
             crossBarPassages[0].FirstMeasure == 0 && crossBarPassages[0].LastMeasure == 1 &&
             crossBarPassages[0].LastEndSlots == 2);

        var systemPassageTrack = new TrackModel
        {
            Measures = new List<MeasureModel> { NewMeasure(), NewMeasure(), NewMeasure(), pmLastBar, pmNextBar }
        };
        var systemPassageProject = new SongProject { Tracks = new List<TrackModel> { systemPassageTrack } };
        var systemPassage = TabEditorControl.BuildPalmMutePassages(systemPassageTrack, systemPassageProject);
        Check("palm-mute ranges retain their endpoints across a system break",
            systemPassage.Count == 1 && systemPassage[0].FirstMeasure / 4 != systemPassage[0].LastMeasure / 4 &&
            systemPassage[0].EventCount == 2);

        var fractionalPm = NewMeasure();
        fractionalPm.Cells[0] = NotationCell(8, 60);
        fractionalPm.Cells[0].Notes[0].Techniques.Add("PalmMute");
        fractionalPm.Cells[1] = NotationCell(8, 62);
        fractionalPm.Cells[1].RhythmicPosition = 2.125;
        fractionalPm.Cells[1].Notes[0].Techniques.Add("PalmMute");
        var fractionalPmTrack = new TrackModel { Measures = new List<MeasureModel> { fractionalPm } };
        var fractionalPmProject = new SongProject { Tracks = new List<TrackModel> { fractionalPmTrack } };
        var fractionalPassages = TabEditorControl.BuildPalmMutePassages(fractionalPmTrack, fractionalPmProject);
        Check("palm-mute continuity respects exact fractional musical onsets",
            fractionalPassages.Count == 2 && Math.Abs(fractionalPassages[1].FirstStartSlots - 2.125) < 0.0001);

        var tiedMeasure = NewMeasure();
        tiedMeasure.Cells[0] = NotationCell(8, 60);
        tiedMeasure.Cells[2] = NotationCell(8, 60);
        tiedMeasure.Cells[2].Notes[0].Tied = true;
        var tiedLayout = Layout(renderer, tiedMeasure, kind: TrackKind.Other);
        Check("a same-pitch tie destination draws one connected tie",
            tiedLayout.Ties.Count == 1 && !tiedLayout.Ties[0].IsStub);

        var chordTie = NewMeasure();
        chordTie.Cells[0] = NotationCell(8, 60);
        chordTie.Cells[0].Notes.Add(new TabNote { StringIndex = 0, MidiValue = 64 });
        chordTie.Cells[2] = NotationCell(8, 64);
        chordTie.Cells[2].Notes[0].Tied = true;
        Check("chord ties match the same pitch instead of the first chord tone",
            Layout(renderer, chordTie, kind: TrackKind.Other).Ties.Count == 1);

        var notTied = NewMeasure();
        notTied.Cells[0] = NotationCell(8, 60);
        notTied.Cells[2] = NotationCell(8, 60);
        Check("equal adjacent pitches do not imply a tie",
            Layout(renderer, notTied, kind: TrackKind.Other).Ties.Count == 0);

        var firstBar = NewMeasure();
        firstBar.Cells[14] = NotationCell(8, 60);
        var secondBar = NewMeasure();
        secondBar.Cells[0] = NotationCell(8, 60);
        secondBar.Cells[0].Notes[0].Tied = true;
        var track = new TrackModel { Kind = TrackKind.Other, Measures = new List<MeasureModel> { firstBar, secondBar } };
        var outgoing = Layout(renderer, firstBar, measureIndex: 0, track: track);
        var incoming = Layout(renderer, secondBar, measureIndex: 1, track: track);
        Check("cross-bar ties stop at the barline on both systems",
            outgoing.Ties.Any(t => t.IsStub && !t.TowardLeft) && incoming.Ties.Any(t => t.IsStub && t.TowardLeft));

        var slurred = NewMeasure();
        slurred.Cells[0] = NotationCell(4, 60);
        slurred.Cells[0].Notes[0].Techniques.Add("HOPOOrigin");
        slurred.Cells[4] = NotationCell(4, 62);
        slurred.Cells[4].Notes[0].Techniques.Add("HOPODestination");
        Check("HOPO slur geometry only follows explicit origin/destination data",
            Layout(renderer, slurred, kind: TrackKind.Other).HopoSlurs.Count == 1);
    }

    private static MeasureModel NewMeasure() => new()
    {
        Clef = "G2",
        Cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList()
    };

    private static MeasureModel SingleDurationMeasure(int denominator)
    {
        var measure = NewMeasure();
        measure.Cells[0] = NotationCell(denominator, 60);
        return measure;
    }

    private static TabCell NotationCell(int denominator, int midi, int dots = 0, bool triplet = false) => new()
    {
        DurationDenominator = denominator,
        Dots = dots,
        IsTriplet = triplet,
        Notes = new List<TabNote> { new() { StringIndex = 0, Fret = 0, MidiValue = midi } }
    };

    private static StaffNotationMeasureLayout Layout(StaffNotationRenderer renderer, MeasureModel measure,
        int numerator = 4, int denominator = 4, int keySignature = 0, TrackKind kind = TrackKind.Other,
        TrackModel? track = null, int measureIndex = 0, double scale = 1.0)
    {
        track ??= new TrackModel { Kind = kind };
        if (!track.Measures.Contains(measure)) track.Measures.Add(measure);
        return renderer.CreateLayout(track, measure, measureIndex, MusicTime.BarSlots(numerator, denominator),
            100, 40, 10, numerator, denominator, keySignature, scale);
    }
}
