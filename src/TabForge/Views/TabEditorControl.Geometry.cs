using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using RenderDraw = TabForge.Visualization.Draw;

namespace TabForge.Views;

// TabEditorControl: page geometry, score layout, duration-based spacing and cursor positioning.
public sealed partial class TabEditorControl
{
    // ---------- geometry ----------

    // Horizontal scroll mode: the page is exactly as wide as the single line of bars.
    private double PageWidth => HorizontalScroll && _horizontalPageWidth > 1 ? _horizontalPageWidth
        : PageWidthOverride > 1 ? PageWidthOverride : BasePageWidth;
    private double _horizontalPageWidth;
    // Title block centre: over the first page width in one-line mode (the page there is the whole song).
    private double HeaderCentreX => HorizontalScroll ? BasePageWidth / 2 : PageWidth / 2;
    private int TuningRows => HasStringTuning(Track) ? Math.Max(1, (Track!.StringTunings.Count + 1) / 2) : 0;
    private double HeaderHeight => 82 + TuningRows * 11;
    private double SystemTop(int system) => HeaderHeight + system * SystemHeight;
    private double StaffTop(int system) => HeaderHeight + system * SystemHeight + StaffMarginTop;
    private double TabTop(int system) => StaffTop(system) + StaffHeight + StaveGap;
    private double GridLeft => PagePad + 46;
    internal double GridWidth => Math.Max(200, PageWidth - PagePad * 2 - 46);

    /// <summary>
    /// Playback caret geometry in the editor's own coordinates, for the lightweight overlay. Null when
    /// there is no playback position.
    /// </summary>
    public (double X, double Top, double Bottom)? PlayheadGeometry()
    {
        var track = Track;
        if (!PlaybackActive || track is null || PlaybackMeasure < 0 || PlaybackCell < 0) return null;
        if (PlaybackMeasure >= track.Measures.Count) return null;
        var measurePosition = GetScoreLayout(track).Measure(PlaybackMeasure);
        var system = measurePosition.SystemIndex;
        var caretWarp = WarpFor(track, PlaybackMeasure);
        var x = measurePosition.X + caretWarp.Fraction(Math.Clamp(PlaybackFraction, 0, 1) * caretWarp.Slots) * measurePosition.Width;
        var staffTop = StaffTop(system);
        var tabTop = TabTop(system);
        var strings = Math.Max(1, track.StringTunings.Count);
        var showTab = Notation != NotationMode.TabOnly;
        var showStaff = Notation != NotationMode.StaffOnly;
        var bottom = showTab ? tabTop + (strings - 1) * StringGap + 14 : staffTop + 4 * StaffGap + 14;
        var top = showStaff ? staffTop - 12 : tabTop - 12;
        return (x * _zoom, top * _zoom, bottom * _zoom);
    }

    /// <summary>
    /// Horizontal scroll mode: the whole score is one continuous line that runs to the right (no page
    /// wrapping); the view scrolls and follows playback horizontally.
    /// </summary>
    public bool HorizontalScroll
    {
        get => _horizontalScroll;
        set
        {
            if (_horizontalScroll == value) return;
            _horizontalScroll = value;
            _horizontalPageWidth = 0;
            InvalidateScoreLayout();
            InvalidateMeasure();
        }
    }
    private bool _horizontalScroll;

    /// <summary>Centres each engraved system in continuous, viewport-reflowing score mode.</summary>
    public bool CenterSystems
    {
        get => _centerSystems;
        set
        {
            if (_centerSystems == value) return;
            _centerSystems = value;
            InvalidateScoreLayout();
        }
    }

    /// <summary>
    /// Full score geometry for the active note/chord event. Its bounds are derived from the canonical
    /// event's fixed onset/end times, not the moving playhead, and are split across score systems when
    /// a tied or sustained event crosses bar boundaries.
    /// </summary>
    public IReadOnlyList<(double X, double EndX, double Top, double Bottom)> PlaybackDurationGeometries()
    {
        var track = Track;
        var timeline = Timeline;
        if (!PlaybackActive || track is null || timeline is null)
            return Array.Empty<(double, double, double, double)>();

        var notes = timeline.NotesFor(PlaybackTrackIndex);
        if (notes.Length == 0) return Array.Empty<(double, double, double, double)>();
        var lookback = Math.Max(6000, timeline.LongestSoundingNoteMs + 1);
        var sounding = NoteTimeline.SoundingAt(notes, PlaybackMs, lookback);
        if (sounding.Count == 0) return Array.Empty<(double, double, double, double)>();

        // Chord notes may have staggered attacks. Keep the block anchored to the newest active score
        // event, while including every string in that cell/voice and performed bar in its full span.
        var active = sounding.OrderByDescending(note => note.OnsetMs).First();
        var playedBarStart = timeline.BarAt(active.OnsetMs).StartMs;
        // Notes are sorted by onset, so the chord's notes sit next to the active one: scan only its
        // neighbourhood instead of the whole track every frame.
        var activeIndex = Array.IndexOf(notes, active);
        var chordList = new List<NoteEvent>(8);
        for (var i = Math.Max(0, activeIndex < 0 ? 0 : activeIndex - 64); i < notes.Length; i++)
        {
            var note = notes[i];
            if (activeIndex >= 0 && note.OnsetMs > active.OnsetMs + 1) break;
            if (note.Bar == active.Bar && note.Cell == active.Cell && note.VoiceIndex == active.VoiceIndex &&
                Math.Abs(timeline.BarAt(note.OnsetMs).StartMs - playedBarStart) < 0.5)
                chordList.Add(note);
        }
        var chord = chordList.ToArray();
        if (chord.Length == 0) chord = new[] { active };

        var startMs = chord.Min(note => note.OnsetMs);
        var endMs = chord.Max(note => note.EndMs);
        if (endMs <= startMs || timeline.Bars.Count == 0)
            return Array.Empty<(double, double, double, double)>();

        var layout = GetScoreLayout(track);
        var strings = Math.Max(1, track.StringTunings.Count);
        var showTab = Notation != NotationMode.TabOnly;
        var showStaff = Notation != NotationMode.StaffOnly;
        var geometries = new List<(double X, double EndX, double Top, double Bottom)>();
        foreach (var bar in timeline.Bars)
        {
            var segmentStart = Math.Max(startMs, bar.StartMs);
            var segmentEnd = Math.Min(endMs, bar.EndMs);
            if (segmentEnd <= segmentStart + 0.001 || bar.Bar < 0 || bar.Bar >= track.Measures.Count || bar.Slots <= 0)
                continue;

            var measurePosition = layout.Measure(bar.Bar);
            var barDuration = Math.Max(1, bar.EndMs - bar.StartMs);
            var startSlot = Math.Clamp((segmentStart - bar.StartMs) / barDuration, 0, 1) * bar.Slots;
            var endSlot = Math.Clamp((segmentEnd - bar.StartMs) / barDuration, 0, 1) * bar.Slots;
            if (endSlot <= startSlot + 0.001) continue;

            var system = measurePosition.SystemIndex;
            var staffTop = StaffTop(system);
            var tabTop = TabTop(system);
            var bottom = showTab ? tabTop + (strings - 1) * StringGap + 14 : staffTop + 4 * StaffGap + 14;
            var top = showStaff ? staffTop - 12 : tabTop - 12;
            var durationWarp = WarpFor(track, bar.Bar);
            var left = measurePosition.X + durationWarp.Fraction(startSlot / bar.Slots * durationWarp.Slots) * measurePosition.Width;
            var right = measurePosition.X + durationWarp.Fraction(endSlot / bar.Slots * durationWarp.Slots) * measurePosition.Width;
            geometries.Add((left * _zoom, right * _zoom, top * _zoom, bottom * _zoom));
        }
        return geometries;
    }

    /// <summary>
    /// Vertical scroll offset that brings the given measure's system near the top of the viewport.
    /// Exposed so the window never hardcodes the score layout geometry (it used to scroll by a
    /// different system height than the renderer draws, so auto-scroll drifted during playback).
    /// </summary>
    public double ScrollOffsetForMeasure(int measure)
    {
        var track = Track;
        if (track is null || track.Measures.Count == 0) return 0;
        var system = GetScoreLayout(track).SystemForMeasure(Math.Clamp(measure, 0, track.Measures.Count - 1));
        return Math.Max(0, SystemTop(system) - 20) * _zoom;
    }

    /// <summary>Left edge (rendered pixels) of a measure, for horizontal scrolling to the cursor.</summary>
    public double HorizontalOffsetForMeasure(int measure)
    {
        var track = Track;
        if (track is null || track.Measures.Count == 0) return 0;
        var layout = GetScoreLayout(track);
        var index = Math.Clamp(measure, 0, track.Measures.Count - 1);
        return Math.Max(0, layout.Measure(index).X - 60) * _zoom;
    }

    /// <summary>Top edge (page coordinates) of the system that contains the given measure.</summary>
    public double SystemTopForMeasure(int measure)
    {
        var track = Track;
        if (track is null || track.Measures.Count == 0) return 0;
        var system = GetScoreLayout(track).SystemForMeasure(Math.Clamp(measure, 0, track.Measures.Count - 1));
        return SystemTop(system) * _zoom;
    }

    /// <summary>Horizontal playback geometry in the scaled score-page coordinate space.</summary>
    public (int SystemIndex, double SystemLeft, double SystemRight, double PlayheadX, double BarWidth)?
        PlaybackHorizontalGeometry(int measure, double fraction)
    {
        var track = Track;
        if (track is null || track.Measures.Count == 0) return null;
        measure = Math.Clamp(measure, 0, track.Measures.Count - 1);
        var layout = GetScoreLayout(track);
        var bar = layout.Measure(measure);
        var system = layout.Systems[bar.SystemIndex];
        fraction = double.IsFinite(fraction) ? Math.Clamp(fraction, 0, 1) : 0;
        // Time fraction -> spaced position (notes are spaced by duration, not uniformly).
        var warp = WarpFor(track, measure);
        var spaced = warp.Fraction(fraction * warp.Slots);
        return (system.Index, system.X * _zoom, (system.X + system.Width) * _zoom,
            (bar.X + spaced * bar.Width) * _zoom, bar.Width * _zoom);
    }

    private int SlotsFor(int measure)
    {
        var p = _project;
        return p is null ? 16 : MusicTime.BarSlots(p, measure);
    }

    /// <summary>Invalidate the natural-width and system-break cache after score content changes.</summary>
    public void InvalidateScoreLayout()
    {
        _scoreLayout = null;
        _scoreGeneration++;
        _scoreFactsGeneration = -1;
        _scoreFactsTrack = null;
        _scoreFactsProject = null;
        _palmMutePassages = Array.Empty<PalmMutePassage>();
        _fadePassages = Array.Empty<FadePassage>();
        _barStateCache = null;
        _barStateComputed = null;
        _markerByMeasure = null;
        _markerCacheProject = null;
        _markerCacheGeneration = -1;
        _staffLayoutCache = null;
        _staffLayoutKeys = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void SetActiveVoice(int voiceIndex)
    {
        var next = Math.Clamp(voiceIndex, 0, 1);
        if (_activeVoiceIndex == next) return;
        _activeVoiceIndex = next;
        SelectionChangedNow(seekPlayback: false);
    }

    private List<TabCell> CellsFor(MeasureModel measure, bool create = false)
        => measure.CellsForVoice(_activeVoiceIndex, create);

    internal ScorePageLayout GetScoreLayout(TrackModel? track = null)
    {
        track ??= Track;
        if (track is null) return ScorePageLayout.Create(GridLeft, GridWidth, Array.Empty<double>());
        if (_scoreLayout is not null && ReferenceEquals(_scoreLayoutTrack, track) &&
            ReferenceEquals(_scoreLayoutProject, _project) && _scoreLayoutHorizontal == HorizontalScroll &&
            (HorizontalScroll || Math.Abs(_scoreLayoutGridWidth - GridWidth) < 0.1) &&
            Math.Abs(_scoreLayoutSpacing - _scoreSpacing) < 0.001 &&
            Math.Abs(_scoreLayoutSystemSpacing - _systemVerticalSpacing) < 0.001 &&
            Math.Abs(_scoreLayoutMeasureSpacing - _measureHorizontalSpacing) < 0.001)
            return _scoreLayout;

#if DEBUG
        using var performance = RenderPerformance.Measure(RenderPerformance.PerformanceCategory.ScoreLayout);
#endif
        var widths = new double[track.Measures.Count];
        var palmMutePassages = _project is null
            ? Array.Empty<PalmMutePassage>()
            : EnsureScoreFacts(track, _project).PalmMutePassages;
        for (var i = 0; i < widths.Length; i++)
            widths[i] = NaturalMeasureWidth(track, track.Measures[i], i, palmMutePassages) * _measureHorizontalSpacing;
        var forceLineBreaks = new bool[track.Measures.Count];
        var preventLineBreaks = new bool[track.Measures.Count];
        for (var measure = 0; measure < track.Measures.Count; measure++)
        {
            forceLineBreaks[measure] = track.Measures[measure].ForceLineBreak;
            preventLineBreaks[measure] = track.Measures[measure].PreventLineBreak;
        }
        if (HorizontalScroll)
        {
            // One line: no width limit and no line breaks; the page is then sized to fit that line.
            _scoreLayout = ScorePageLayout.Create(GridLeft, 1e7, widths);
            var line = _scoreLayout.Systems.FirstOrDefault();
            var lineRight = line is null || line.Measures.Count == 0 ? GridLeft + 200
                : line.Measures[^1].X + line.Measures[^1].Width;
            _horizontalPageWidth = lineRight + PagePad;
        }
        else
            _scoreLayout = ScorePageLayout.Create(GridLeft, GridWidth, widths,
                forceLineBreaks, preventLineBreaks, centerRows: CenterSystems);
        _scoreLayoutHorizontal = HorizontalScroll;
        _scoreLayoutTrack = track;
        _scoreLayoutProject = _project;
        // (horizontal mode sizes the page from the line, so it never compares the grid width)
        _scoreLayoutGridWidth = GridWidth;
        _scoreLayoutSpacing = _scoreSpacing;
        _scoreLayoutSystemSpacing = _systemVerticalSpacing;
        _scoreLayoutMeasureSpacing = _measureHorizontalSpacing;
        return _scoreLayout;
    }

    /// <summary>Voice 2 is engraved (notes and rests) only when it holds real content in the bar.</summary>
    internal static bool Voice2HasContent(MeasureModel measure)
    {
        var cells = measure.Voice2Cells;
        for (var i = 0; i < cells.Count; i++)
            if (cells[i].Notes.Count > 0 || cells[i].HasAnnotation) return true;
        return false;
    }

    internal double NaturalMeasureWidth(TrackModel track, MeasureModel measure, int measureIndex,
        IReadOnlyList<PalmMutePassage> palmMutePassages)
    {
        var slots = SlotsFor(measureIndex);
        var numerator = measure.TimeSigNum ?? _project?.TimeSignatureNumerator ?? 4;
        var denominator = measure.TimeSigDenom ?? _project?.TimeSignatureDenominator ?? 4;
        var keySignature = measure.KeySignature ?? _project?.KeySignature ?? 0;
        var notation = _staff.CreateLayout(track, measure, measureIndex, slots, 0, 0, 1,
            numerator, denominator, keySignature, _scoreSpacing);
        var notationBeats = notation.Beats.AsEnumerable();
        if (Voice2HasContent(measure))
        {
            var voice2 = _staff.CreateLayout(track, measure, measureIndex, slots, 0, 0, 1,
                numerator, denominator, keySignature, _scoreSpacing, measure.Voice2Cells);
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

            foreach (var note in beat.Notes)
            {
                var offset = note.X - beat.CenterX;
                left = Math.Max(left, 6 - offset);
                right = Math.Max(right, 6 + offset);
                if (note.Accidental is not null)
                {
                    var accidentalWidth = MakeText(note.Accidental, 15, Brush(Colors.White)).Width;
                    left = Math.Max(left, 10.1 + note.AccidentalColumn * 10 + accidentalWidth / 2 - offset);
                }

                var fret = note.Source.Dead ? "X"
                    : track.Kind == TrackKind.Drums ? DrumMaps.For(track, note.Source.MidiValue > 0 ? note.Source.MidiValue : note.Source.Fret).Label
                    : note.Source.Fret.ToString(CultureInfo.InvariantCulture);
                var fretWidth = MakeTextIn(ScoreTextArea.Fret, fret, FretFontSize, Brush(Colors.White), FontWeights.Normal, "Consolas").Width;
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

            var techniqueLabel = TechniqueLabel(beat.Cell.Notes, includeFade: false);
            if (techniqueLabel.Length > 0)
            {
                var width = MakeTextIn(ScoreTextArea.Technique, techniqueLabel, 9, Brush(Colors.White)).Width;
                left = Math.Max(left, width / 2 + 2);
                right = Math.Max(right, width / 2 + 2);
            }

            if (!string.IsNullOrWhiteSpace(beat.Cell.ChordName))
            {
                var width = MakeTextIn(ScoreTextArea.Chord, beat.Cell.ChordName!, 10, Brush(Colors.White), FontWeights.SemiBold).Width;
                left = Math.Max(left, width / 2 + 3);
                right = Math.Max(right, width / 2 + 3);
            }
            // Beat text (comments above the staff) runs over neighbouring beats; letting it
            // reserve its full width stretched a bar with a long comment across the whole line.
            if (!string.IsNullOrWhiteSpace(beat.Cell.Lyrics))
            {
                var width = beat.Cell.Lyrics.Split('\n').Max(text => MakeTextIn(ScoreTextArea.Lyrics, text, 10, Brush(Colors.White)).Width);
                left = Math.Max(left, width / 2 + 3);
                right = Math.Max(right, width / 2 + 3);
            }
            if (beat.Cell.Fermata || beat.Cell.IsGrace) right = Math.Max(right, 13);
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
        var labelWidth = MakeTextIn(ScoreTextArea.Technique, "P.M.", 9, Brush(Colors.White)).Width;
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
        if (keyChanges || timeShown)
        {
            lead = 28;
            if (keyChanges) lead += KeySignatureWidth(track, measureIndex);
            if (timeShown) lead += TimeSignatureWidth(measure) + 6;
        }
        var tempoText = TempoText(measure, measureIndex);
        if (tempoText is not null)
            lead = Math.Max(lead, 22 + MakeTextIn(ScoreTextArea.BarInfo, tempoText, 9, Brush(Colors.White), FontWeights.Bold).Width + 6);
        var sectionTitle = MarkerForMeasure(measureIndex)?.Title ?? measure.SectionName;
        if (ShowSectionHeadings && !string.IsNullOrWhiteSpace(sectionTitle))
            lead = Math.Max(lead, MakeTextIn(ScoreTextArea.BarInfo, sectionTitle, 10, Brush(Colors.White), FontWeights.Bold).Width + 8);
        if (measure.RepeatStart) lead = Math.Max(lead, 22);
        if (measure.AlternateEnding > 0) lead = Math.Max(lead, 34);
        var tripletFeel = TripletFeels.Effective(measure);
        if (tripletFeel != TripletFeels.None)
            lead = Math.Max(lead, MakeTextIn(ScoreTextArea.BarInfo, SwingSymbol(tripletFeel), 9, Brush(Colors.White)).Width + 8);
        var trail = measure.RepeatEnd
            ? MakeTextIn(ScoreTextArea.BarInfo, $"×{measure.RepeatCount}:|", 10, Brush(Colors.White), FontWeights.Bold).Width + 8
            : 10;
        if (measure.IsDoubleBar) trail += 4;
        // Duration-based spacing (standard): gaps are weighted by duration^0.62, see MeasureWarp.
        var warp = WarpFor(track, measureIndex);
        var rhythmicSpacing = RhythmicPixelsPerSlot * _scoreSpacing;
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

    private readonly record struct ScoreFacts(IReadOnlyList<PalmMutePassage> PalmMutePassages,
        IReadOnlyList<FadePassage> FadePassages);

    private readonly record struct StaffLayoutCacheKey(TrackModel Track, MeasureModel Measure,
        IReadOnlyList<TabCell> Cells, int MeasureIndex, int Slots, int Numerator, int Denominator,
        int KeySignature, double X, double StaffTop, double SlotWidth, double StaffScale, MeasureWarp Warp);

    // ---- duration-based spacing (one warp per bar, rebuilt when the score changes) ----
    private readonly Dictionary<MeasureModel, (int Generation, int Slots, MeasureWarp Warp)> _warps = new();

    /// <summary>Room (sixteenth-equivalents) for an engraved key/time signature so the first note clears it.</summary>
    private double HeaderLeadWeight(TrackModel track, int measureIndex)
    {
        var pixels = 0.0;
        if (KeySignatureChanges(track, measureIndex)) pixels += KeySignatureWidth(track, measureIndex);
        if (TimeSignatureShown(track, measureIndex)) pixels += TimeSignatureWidth(track.Measures[measureIndex]) + 6;
        return pixels <= 0 ? 0 : pixels / Math.Max(1, RhythmicPixelsPerSlot * _scoreSpacing);
    }

    internal MeasureWarp WarpFor(TrackModel track, int measureIndex)
    {
        var measure = track.Measures[measureIndex];
        var slots = SlotsFor(measureIndex);
        if (_warps.TryGetValue(measure, out var hit) && hit.Generation == _scoreGeneration && hit.Slots == slots) return hit.Warp;
        static IEnumerable<double> Onsets(List<TabCell> cells)
        {
            var cursor = 0.0;
            for (var i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                if (c.Notes.Count == 0 && !c.IsRest) continue;
                var start = c.RhythmicPosition ?? Math.Max(i, cursor);
                cursor = Math.Max(cursor, start + MusicTime.CellSlots(c));
                yield return start;
            }
        }
        // Repeat signs get their own room so the first/last notes never touch the dots.
        // The first note always clears the barline (the reference spacing); grace notes before the first beat need extra room.
        var lead = (measure.RepeatStart ? 1.8 : 0) + HeaderLeadWeight(track, measureIndex);
        var firstContent = measure.Cells.FirstOrDefault(c => c.Notes.Count > 0 || c.IsRest);
        if (firstContent is not null && firstContent.Notes.Any(n => n.IsGraceNote) && firstContent.Notes.Any(n => !n.IsGraceNote)) lead += 3.0;
        lead = Math.Max(lead, 1.6);
        // An overfull bar (more music than its time signature) squeezes everything inside the bar,
        // instead of letting the last beats spill into the next bar.
        static double ContentEnd(List<TabCell> cells)
        {
            var cursor = 0.0;
            for (var i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                if (c.Notes.Count == 0 && !c.IsRest) continue;
                var start = c.RhythmicPosition ?? Math.Max(i, cursor);
                cursor = Math.Max(cursor, start + MusicTime.CellSlots(c));
            }
            return cursor;
        }
        var contentEnd = Math.Max(ContentEnd(measure.Cells), Voice2HasContent(measure) ? ContentEnd(measure.Voice2Cells) : 0);
        var warp = MeasureWarp.Build(Onsets(measure.Cells).Concat(Voice2HasContent(measure) ? Onsets(measure.Voice2Cells) : Enumerable.Empty<double>()),
            contentEnd > slots + 0.05 ? contentEnd : slots,
            leadWeight: lead, trailWeight: measure.RepeatEnd ? 1.4 : 0);
        if (_warps.Count > 4096) _warps.Clear();
        _warps[measure] = (_scoreGeneration, slots, warp);
        return warp;
    }

    private ScoreFacts EnsureScoreFacts(TrackModel track, SongProject project)
    {
        if (_scoreFactsGeneration == _scoreGeneration && ReferenceEquals(_scoreFactsTrack, track) &&
            ReferenceEquals(_scoreFactsProject, project))
            return new ScoreFacts(_palmMutePassages, _fadePassages);

        _palmMutePassages = Notation == NotationMode.StaffOnly
            ? Array.Empty<PalmMutePassage>()
            : BuildPalmMutePassages(track, project);
        _fadePassages = BuildFadePassages(track, project);
        _scoreFactsTrack = track;
        _scoreFactsProject = project;
        _scoreFactsGeneration = _scoreGeneration;
        return new ScoreFacts(_palmMutePassages, _fadePassages);
    }

    private BarState BarStateFor(int measureIndex)
    {
        var project = _project;
        if (project is null || measureIndex < 0)
            return project is null ? default : MusicTime.AnalyzeBar(project, measureIndex);
        if (_barStateCache is null)
        {
            var count = 0;
            for (var track = 0; track < project.Tracks.Count; track++)
                count = Math.Max(count, project.Tracks[track].Measures.Count);
            _barStateCache = new BarState[count];
            _barStateComputed = new bool[count];
        }
        if (measureIndex >= _barStateCache.Length) return MusicTime.AnalyzeBar(project, measureIndex);
        if (!_barStateComputed![measureIndex])
        {
            _barStateCache[measureIndex] = MusicTime.AnalyzeBar(project, measureIndex);
            _barStateComputed[measureIndex] = true;
        }
        return _barStateCache[measureIndex];
    }

    private MarkerModel? MarkerForMeasure(int measureIndex)
    {
        var project = _project;
        if (project is null || measureIndex < 0) return null;
        var track = Track;
        var count = track?.Measures.Count ?? 0;
        if (_markerByMeasure is null || _markerCacheGeneration != _scoreGeneration ||
            !ReferenceEquals(_markerCacheProject, project) || _markerByMeasure.Length != count)
        {
            _markerByMeasure = new MarkerModel?[count];
            foreach (var marker in project.Markers)
                if (marker.MeasureIndex >= 0 && marker.MeasureIndex < count && _markerByMeasure[marker.MeasureIndex] is null)
                    _markerByMeasure[marker.MeasureIndex] = marker;
            _markerCacheProject = project;
            _markerCacheGeneration = _scoreGeneration;
        }
        return measureIndex < _markerByMeasure.Length ? _markerByMeasure[measureIndex] : null;
    }

    private StaffNotationMeasureLayout StaffLayoutFor(TrackModel track, MeasureModel measure, int measureIndex,
        int slots, double x, double staffTop, double slotWidth, int numerator, int denominator,
        int keySignature, IReadOnlyList<TabCell> cells)
    {
        var voice = ReferenceEquals(cells, measure.Voice2Cells) ? 1 : 0;
        if (_staffLayoutCache is null || _staffLayoutCache.GetLength(0) != track.Measures.Count)
        {
            _staffLayoutCache = new StaffNotationMeasureLayout?[track.Measures.Count, 2];
            _staffLayoutKeys = new StaffLayoutCacheKey[track.Measures.Count, 2];
        }

        var warp = WarpFor(track, measureIndex);
        var key = new StaffLayoutCacheKey(track, measure, cells, measureIndex, slots, numerator,
            denominator, keySignature, x, staffTop, slotWidth, _scoreSpacing, warp);
        if (_staffLayoutCache[measureIndex, voice] is { } cached && _staffLayoutKeys![measureIndex, voice] == key)
            return cached;

        var measureWidth = slotWidth * Math.Max(1, slots);
        var layout = _staff.CreateLayout(track, measure, measureIndex, slots, x, staffTop, slotWidth,
            numerator, denominator, keySignature, _scoreSpacing, cells, start => x + warp.CenterFraction(start) * measureWidth);
        _staffLayoutCache[measureIndex, voice] = layout;
        _staffLayoutKeys![measureIndex, voice] = key;
        return layout;
    }

    private readonly record struct MeasureEventWidth(
        double StartSlots, double EndSlots, double Left, double Right, double ComplexityPadding = 0);

    private static bool HasStringTuning(TrackModel? track)
        => track is not null && track.Kind is not TrackKind.Drums and not TrackKind.Keys && track.StringTunings.Count > 0;

    public void SetPosition(int measure, int cell, int @string, bool seekPlayback = true)
    {
        var track = Track;
        if (track is null) return;
        SelectedMeasure = Math.Clamp(measure, 0, Math.Max(0, track.Measures.Count - 1));
        SelectedCell = Math.Clamp(cell, 0, SlotsFor(SelectedMeasure) - 1);
        SelectedString = Math.Clamp(@string, 0, Math.Max(0, track.StringTunings.Count - 1));
        SelectionChangedNow(seekPlayback);
    }

    public void SetBar(int measure, bool seekPlayback = true) => SetPosition(measure, 0, SelectedString, seekPlayback);
}
