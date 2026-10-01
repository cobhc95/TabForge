using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Services;
using TabForge.Visualization;
using RenderDraw = TabForge.Visualization.Draw;

namespace TabForge.Views;

public enum LedgerLineMode
{
    Standard,
    Minimal,
    Hidden
}

internal readonly record struct StaffLedgerLineSegment(double X1, double X2, double Y);

/// <summary>
/// Shared rhythmic and geometric layout for one measure. TAB and staff notation consume the same
/// beat centers, while note values, meter, pitch, beams and expressive marks are resolved here.
/// </summary>
internal sealed class StaffNotationMeasureLayout
{
    private IReadOnlyList<StaffLedgerLineSegment>? _standardLedgerSegments;
    private IReadOnlyList<StaffLedgerLineSegment>? _minimalLedgerSegments;
    public required double StaffTop { get; init; }
    /// <summary>Leftmost x for 8va/15ma captions so they stay clear of the tempo text and bar number (set by the editor).</summary>
    public double OctaveLabelMinX { get; set; } = double.NegativeInfinity;
    /// <summary>Right edge of the bar (captions must not cross it).</summary>
    public double Right { get; set; } = double.PositiveInfinity;
    public required double SlotWidth { get; init; }
    public required double StaffScale { get; init; }
    public required IReadOnlyList<StaffNotationBeat> Beats { get; init; }
    public required IReadOnlyList<StaffNotationBeamGroup> BeamGroups { get; init; }
    public required IReadOnlyList<StaffNotationBeamSegment> BeamSegments { get; init; }
    public required IReadOnlyList<StaffNotationTupletGroup> TupletGroups { get; init; }
    public required IReadOnlyList<StaffNotationTie> Ties { get; init; }
    public required IReadOnlyList<StaffNotationSlur> HopoSlurs { get; init; }
    /// <summary>Short slanted strokes for shift, legato and slide-in / slide-out marks (the reference's slide lines).</summary>
    public IReadOnlyList<StaffNotationSlideStroke> Slides { get; init; } = Array.Empty<StaffNotationSlideStroke>();
    /// <summary>Voice 2: its markings go below the staff, voice 1's above.</summary>
    public bool IsSecondVoice { get; init; }
    /// <summary>Right edge of the key / time signature drawn at the bar start (the editor sets it; marks that reach back must stay right of it).</summary>
    public double ContentLeft { get; set; } = double.NegativeInfinity;
    /// <summary>Row stacking of the markings around this staff; shared by both voices of a bar (set by the editor, else private to the layout).</summary>
    public MarkSkyline Skyline { get; set; } = new();
    /// <summary>Voice 2 only: voice 1's layout of the same bar (set by the editor before drawing), so a fermata both voices hold can be drawn once in the TAB.</summary>
    public StaffNotationMeasureLayout? FirstVoice { get; set; }
    public StaffNotationBeat? BeatForCell(int cellIndex) => Beats.FirstOrDefault(b => b.CellIndex == cellIndex);

    /// <summary>Voice 2's beat holds a fermata that voice 1 also holds at the same onset.</summary>
    internal bool FermataSharedWithFirstVoice(StaffNotationBeat beat)
    {
        if (!IsSecondVoice || FirstVoice is null || !beat.Cell.Fermata) return false;
        foreach (var other in FirstVoice.Beats)
            if (other.Cell.Fermata && Math.Abs(other.StartSlots - beat.StartSlots) < 0.001) return true;
        return false;
    }

    internal bool TryGetLedgerSegments(LedgerLineMode mode, out IReadOnlyList<StaffLedgerLineSegment> segments)
    {
        segments = mode == LedgerLineMode.Standard ? _standardLedgerSegments ?? Array.Empty<StaffLedgerLineSegment>()
            : mode == LedgerLineMode.Minimal ? _minimalLedgerSegments ?? Array.Empty<StaffLedgerLineSegment>()
            : Array.Empty<StaffLedgerLineSegment>();
        return mode == LedgerLineMode.Hidden ||
               (mode == LedgerLineMode.Standard && _standardLedgerSegments is not null) ||
               (mode == LedgerLineMode.Minimal && _minimalLedgerSegments is not null);
    }

    internal void SetLedgerSegments(LedgerLineMode mode, IReadOnlyList<StaffLedgerLineSegment> segments)
    {
        if (mode == LedgerLineMode.Standard) _standardLedgerSegments = segments;
        else if (mode == LedgerLineMode.Minimal) _minimalLedgerSegments = segments;
    }
}

internal sealed class StaffNotationBeat
{
    public required int CellIndex { get; init; }
    public required TabCell Cell { get; init; }
    public required double StartSlots { get; init; }
    public required double DurationSlots { get; init; }
    public required double CenterX { get; init; }
    public required int Flags { get; init; }
    public required List<StaffNotationNote> Notes { get; init; }
    /// <summary>Grace notes of this beat (drawn small, before the main note; not part of the beat's stem, beam or accidentals).</summary>
    public List<StaffNotationNote> GraceNotes { get; init; } = new();
    public bool IsRest => Cell.IsRest && Cell.Notes.Count == 0;
    public bool IsDrum { get; init; }
    public double StaffTop { get; init; }
    /// <summary>Drum tracks: staff position / notehead per GM percussion note (the track's drum preset).</summary>
    public Func<int, TabForge.Services.DrumMapEntry>? DrumMap { get; init; }
    public bool AutoStemInvert { get; init; }
    /// <summary>The bar has a second voice: voice 1 stems always go up and voice 2 stems down.</summary>
    public bool TwoVoices { get; init; }
    /// <summary>Drum beats with sounds on both sides of the middle line get a second, down stem for the low sounds (kick, snare).</summary>
    public double? LowerStemTopY { get; set; }
    public double LowerStemEndY { get; set; }
    public bool HasStem => !IsRest && Cell.Notes.Count > 0 && StaffNotationRenderer.NormalizeDuration(Cell.DurationDenominator) > 1;
    public bool IsBeamable => HasStem && Flags > 0;
    public bool StemUp { get; set; }
    public double StemX { get; set; }
    public double StemStartY { get; set; }
    public double StemEndY { get; set; }
    public double MinY { get; set; }
    public double MaxY { get; set; }
    public int BeamGroupIndex { get; set; } = -1;
}

internal sealed class StaffNotationNote
{
    public required TabNote Source { get; init; }
    public required int WrittenMidi { get; init; }
    public required int StaffStep { get; init; }
    public required int Letter { get; init; }
    public required int Octave { get; init; }
    public required int Alteration { get; init; }
    public required double Y { get; init; }
    public double X { get; set; }
    public string? Accidental { get; set; }
    public int AccidentalColumn { get; set; }
}

internal sealed class StaffNotationBeamGroup
{
    public required int Index { get; init; }
    public required IReadOnlyList<StaffNotationBeat> Beats { get; init; }
    public bool StemUp { get; set; }
    public required double MetricStart { get; init; }
    public required double MetricWidth { get; init; }
    public required int MaxFlags { get; init; }
    public double Slope { get; set; }
    public double Intercept { get; set; }
    public double BaseYAt(double x) => Intercept + Slope * (x - Beats[0].StemX);
}

internal readonly record struct StaffNotationBeamSegment(
    int GroupIndex, int Level, int FirstCell, int LastCell,
    double X1, double Y1, double X2, double Y2, bool IsPartial);

internal sealed class StaffNotationTupletGroup
{
    public required IReadOnlyList<StaffNotationBeat> Beats { get; init; }
    public required int Numerator { get; init; }
    public int BeamGroupIndex { get; init; } = -1;
    public bool IsBeamed => BeamGroupIndex >= 0;
}

/// <param name="StartInset">How far right of X1 the arc starts (5 normally; more clears a ghost bracket after the first head).</param>
/// <param name="EndInset">How far left of X2 the arc ends (5 normally; more stops short of a ghost bracket or a chord-mate's accidental).</param>
internal readonly record struct StaffNotationTie(
    double X1, double Y1, double X2, double Y2, bool Above, bool IsStub, bool TowardLeft, double StartInset = 5, double EndInset = 5);

internal readonly record struct StaffNotationSlideStroke(double X1, double Y1, double X2, double Y2);

internal readonly record struct StaffNotationSlur(
    double X1, double Y1, double X2, double Y2, bool StemsUp, double StartInset = 5, double EndInset = 5);

/// <summary>
/// Engraves score notation in four explicit stages: musical events and durations, metrical beam
/// grouping, staff-pitch geometry, and drawing. The returned layout is also used by the TAB renderer.
/// </summary>
internal sealed class StaffNotationRenderer
{
    public const double StaffGap = 9.0;
    private const double StemLength = 3.0 * StaffGap;
    private const double BeamThickness = 1.7;
    private const double BeamGap = 3.4;
    internal const double HeadRadiusX = StaffGap * 0.56;
    private const double HeadRadiusY = StaffGap * 0.38;
    private const double MiddleLineOffset = 2 * StaffGap;
    private const double PositionEpsilon = 0.001;
    /// <summary>How far a standard ledger line runs past the notehead on each side (the marks beside a ledger note clear it).</summary>
    private const double LedgerOverhang = 3.5;
    private static bool OnLedger(StaffNotationMeasureLayout layout, StaffNotationNote note) =>
        note.Y < layout.StaffTop - 1 || note.Y > layout.StaffTop + 4 * StaffGap + 1;
    /// <summary>Imported tuplet beats sit on whole file ticks, so neighbours drift by a fraction of a slot from their exact ratio.</summary>
    private const double TupletTickSlack = 0.15;

    private static readonly int[] NaturalPitchClasses = { 0, 2, 4, 5, 7, 9, 11 };
    private static readonly int[] SharpOrder = { 3, 0, 4, 1, 5, 2, 6 };
    private static readonly int[] FlatOrder = { 6, 2, 5, 1, 4, 0, 3 };

    /// <summary>Creates the single rhythmic/geometry layout consumed by both score staves.</summary>
    public StaffNotationMeasureLayout CreateLayout(
        TrackModel? track,
        MeasureModel measure,
        int measureIndex,
        int slots,
        double x,
        double staffTop,
        double slotWidth,
        int numerator = 4,
        int denominator = 4,
        int keySignature = 0,
        double staffScale = 1.0,
        IReadOnlyList<TabCell>? cellsOverride = null,
        Func<double, double>? centerOf = null)
    {
        staffScale = Math.Clamp(double.IsFinite(staffScale) ? staffScale : 1.0, 0.5, 2.0);
        var isDrum = track?.Kind == TrackKind.Drums;
        var staffBottom = staffTop + 4 * StaffGap;
        var clef = ClefInfo.From(measure.Clef);
        var keyAlterations = KeyAlterations(keySignature);
        var beats = new List<StaffNotationBeat>();

        var inferredCursor = 0.0;
        var sourceCells = cellsOverride ?? measure.Cells;
        var voiceIndex = ReferenceEquals(sourceCells, measure.Voice2Cells) ? 1 : 0;
        var twoVoices = measure.Voice2Cells.Any(c => c.Notes.Count > 0);
        for (var i = 0; i < sourceCells.Count; i++)
        {
            var cell = sourceCells[i];
            if (cell.Notes.Count == 0 && !cell.IsRest && !cell.HasAnnotation) continue;

            var rawStart = cell.RhythmicPosition ?? Math.Max(i, inferredCursor);
            var start = Math.Max(0, double.IsFinite(rawStart) ? rawStart : i);
            var duration = MusicTime.CellSlots(cell);
            inferredCursor = Math.Max(inferredCursor, start + duration);
            var centerX = centerOf?.Invoke(start) ?? x + (start + 0.5) * slotWidth;
            var notes = new List<StaffNotationNote>();
            var graceNotes = new List<StaffNotationNote>();
            var hasMainNote = cell.Notes.Any(candidate => !candidate.IsGraceNote);
            if (!isDrum)
            {
                var octaveTranspose = clef.OctaveShift;
                if (octaveTranspose == 0 && track?.Kind is TrackKind.Guitar or TrackKind.Bass) octaveTranspose = 12;
                foreach (var note in cell.Notes)
                {
                    var fretted = (track?.Kind is TrackKind.Guitar or TrackKind.Bass) &&
                                  note.StringIndex >= 0 && note.StringIndex < track.StringTunings.Count
                        ? track.PitchOf(note.StringIndex, note.Fret)
                        : 0;
                    // Artificial, tap, pinch (pick) and semi (slap) harmonics are written at the fretted note with a diamond head, as in
                    // the reference; their stored pitch is the much higher sounding harmonic. A natural harmonic keeps its stored pitch.
                    var soundingMidi = IsWrittenAtFret(note.Techniques) && fretted > 0
                        ? fretted
                        : note.MidiValue > 0 ? note.MidiValue : fretted;
                    if (soundingMidi <= 0) continue;
                    var writtenMidi = soundingMidi + octaveTranspose;
                    var pitch = SpellPitch(writtenMidi, keyAlterations, keySignature);
                    var y = staffBottom - (pitch.DiatonicIndex - clef.BottomDiatonicIndex) * (StaffGap / 2.0);
                    (note.IsGraceNote && hasMainNote ? graceNotes : notes).Add(new StaffNotationNote
                    {
                        Source = note,
                        WrittenMidi = writtenMidi,
                        StaffStep = pitch.DiatonicIndex,
                        Letter = pitch.Letter,
                        Octave = pitch.Octave,
                        Alteration = pitch.Alteration,
                        Y = y
                    });
                }
            }

            beats.Add(new StaffNotationBeat
            {
                CellIndex = i,
                Cell = cell,
                StartSlots = start,
                DurationSlots = duration,
                CenterX = centerX,
                Flags = FlagsFor(cell.DurationDenominator),
                Notes = notes,
                GraceNotes = graceNotes,
                IsDrum = isDrum,
                StaffTop = staffTop,
                DrumMap = isDrum && track is not null ? midi => TabForge.Services.DrumMaps.For(track, midi) : null,
                AutoStemInvert = voiceIndex == 1,
                TwoVoices = twoVoices,
                MinY = staffTop + MiddleLineOffset,
                MaxY = staffTop + MiddleLineOffset
            });
        }

        beats.Sort((a, b) =>
        {
            var onset = a.StartSlots.CompareTo(b.StartSlots);
            return onset != 0 ? onset : a.CellIndex.CompareTo(b.CellIndex);
        });

        var metricGroups = BuildMetricGroups(numerator, denominator, slots);
        var beamGroups = BuildBeamGroups(beats, metricGroups);
        AssignBeamStemDirections(beamGroups, staffTop + MiddleLineOffset);
        AssignNoteheadsAndStems(beats, staffTop, staffBottom, isDrum);
        var beamSegments = BuildBeamSegments(beamGroups);
        var tuplets = BuildTupletGroups(beats, beamGroups);
        ResolveAccidentals(beats, keyAlterations);
        AssignAccidentalColumns(beats);
        var ties = BuildTies(track, measureIndex, slots, beats, voiceIndex, staffTop);
        var slurs = BuildHopoSlurs(beats, staffTop);

        return new StaffNotationMeasureLayout
        {
            StaffTop = staffTop,
            SlotWidth = slotWidth,
            StaffScale = staffScale,
            Right = x + slotWidth * Math.Max(1, slots),
            Beats = beats,
            BeamGroups = beamGroups,
            BeamSegments = beamSegments,
            TupletGroups = tuplets,
            Ties = ties,
            HopoSlurs = slurs,
            Slides = BuildSlideStrokes(track, measureIndex, voiceIndex, beats, staffTop),
            IsSecondVoice = voiceIndex == 1
        };
    }

    /// <summary>Staff y of each sound of a drum beat, from the track's drum map (a beamed drum beat keeps a nominal MinY/MaxY).</summary>
    internal static IEnumerable<double> DrumHeadYs(StaffNotationBeat beat)
    {
        foreach (var n in beat.Cell.Notes)
        {
            var entry = beat.DrumMap?.Invoke(n.MidiValue > 0 ? n.MidiValue : n.Fret);
            yield return entry is null ? beat.MinY : beat.StaffTop + entry.StaffStep * StaffGap / 2;
        }
    }

    /// <summary>Claims the notation's own ink (heads, accidentals, stems, flags, beams, grace notes) so every mark stacks outside it.</summary>
    internal static void SeedSkyline(StaffNotationMeasureLayout layout)
    {
        var sky = layout.Skyline;
        foreach (var beat in layout.Beats)
        {
            if (beat.IsRest)
            {
                // The rest glyph is ink too (a stack of flags on a 32nd/64th rest reaches well below the staff middle): marks stack outside it.
                var duration = NormalizeDuration(beat.Cell.DurationDenominator);
                var restTop = layout.StaffTop + StaffGap * (duration <= 2 ? (duration <= 1 ? 1.0 : 1.5) : 1.0);
                var restBottom = duration <= 1 ? restTop + StaffGap * 0.5 : duration == 2 ? restTop + StaffGap * 0.5
                    : duration == 4 ? layout.StaffTop + StaffGap + 18
                    : layout.StaffTop + StaffGap * 1.1 + StaffGap * (1.5 + 0.9 * ((duration switch { 8 => 1, 16 => 2, 32 => 3, _ => 4 }) - 1));
                sky.Claim(beat.CenterX - 6.5, beat.CenterX + 6.5 + (beat.Cell.Dots > 0 ? 4 + beat.Cell.Dots * 4 : 0), restTop - 1, restBottom + 1);
                continue;
            }
            foreach (var c in GhostClusters(layout, beat))
                sky.Claim(c.L - HeadRadiusX - 13 - c.Pad, c.R + HeadRadiusX + 8 + c.Pad, c.InkY - c.Half - 1, c.InkY + c.Half + 1);
            foreach (var n in beat.Notes)
            {
                sky.Claim(n.X - HeadRadiusX - 2.2, n.X + HeadRadiusX + 2.2, n.Y - HeadRadiusY - 2.6, n.Y + HeadRadiusY + 2.6);   // the rotated (and outlined) head
                // Ledger lines run from the staff to the head: that column is ink too.
                var staffBottom = layout.StaffTop + 4 * StaffGap;
                if (n.Y > staffBottom + 1) sky.Claim(n.X - HeadRadiusX - 3.5, n.X + HeadRadiusX + 3.5, staffBottom, n.Y + 1);
                else if (n.Y < layout.StaffTop - 1) sky.Claim(n.X - HeadRadiusX - 3.5, n.X + HeadRadiusX + 3.5, n.Y - 1, layout.StaffTop);
                for (var d = 0; d < Math.Clamp(beat.Cell.Dots, 0, 2); d++)
                    sky.Claim(n.X + HeadRadiusX + 1.5 + d * 4, n.X + HeadRadiusX + 6 + d * 4, n.Y - 4.5, n.Y + 2);
                if (n.Accidental is not null)
                {
                    var ax = AccidentalX(layout, beat, n);
                    sky.Claim(ax - 4, ax + 4, n.Y - 8, n.Y + 8);
                }
            }
            if (beat.IsDrum)
                foreach (var y in DrumHeadYs(beat)) sky.Claim(beat.CenterX - 7, beat.CenterX + 7, y - 7, y + 7);
            if (beat.Notes.Count > 0 && beat.Cell.Notes.Any(n => n.Techniques.Any(t => t is "ArpeggioDown" or "ArpeggioUp" or "BrushDown" or "BrushUp")))
            {
                // The arpeggio / brush line and its arrowhead left of the chord (head reaches 5.7 px past the line's end) are ink too.
                var ax = beat.CenterX - 13 - (beat.Notes.Any(n => n.Accidental is not null) ? 8 : 0);
                var ay1 = beat.Notes.Min(n => n.Y) - 4;
                sky.Claim(ax - 3.5, ax + 3.5, ay1 - 6, Math.Max(beat.Notes.Max(n => n.Y) + 4, ay1 + 9) + 6);
            }
            if ((beat.Cell.Staccato || beat.Cell.Tenuto) && beat.Notes.Count > 0)
            {
                var below = !beat.HasStem || beat.StemUp;
                if (below) sky.Claim(beat.CenterX - 4, beat.CenterX + 4, beat.MaxY + 6, beat.MaxY + 9 + (beat.Cell.Staccato && beat.Cell.Tenuto ? 9 : 3));
                else sky.Claim(beat.CenterX - 4, beat.CenterX + 4, beat.MinY - 9 - (beat.Cell.Staccato && beat.Cell.Tenuto ? 9 : 3), beat.MinY - 6);
            }
            if (beat.HasStem)
            {
                var top = Math.Min(beat.StemStartY, beat.StemEndY);
                var bottom = Math.Max(beat.StemStartY, beat.StemEndY);
                sky.Claim(beat.StemX - 1, beat.StemX + 1, top, bottom);
                if (beat.Flags > 0 && beat.BeamGroupIndex < 0)
                    sky.Claim(beat.StemX - 0.5, beat.StemX + 8.5, beat.StemUp ? top : bottom - 15 - (beat.Flags - 1) * BeamGap, beat.StemUp ? top + 15 + (beat.Flags - 1) * BeamGap : bottom);
                if (beat.LowerStemTopY is { } lowerTop) sky.Claim(beat.CenterX - 6, beat.CenterX - 4, lowerTop, beat.LowerStemEndY);
            }
            if (beat.GraceNotes.Count > 0)
            {
                var gy = beat.GraceNotes.Min(g => g.Y);
                var gx = beat.CenterX - GraceOffset(beat, GhostRoom(layout.StaffTop, beat));   // where DrawGraceNotes puts the first grace head
                sky.Claim(gx - 9 * Math.Min(3, beat.GraceNotes.Count) - 4, gx + 7, gy - 18, beat.GraceNotes.Max(g => g.Y) + 4);
            }
        }
        foreach (var beam in layout.BeamSegments)
            sky.Claim(Math.Min(beam.X1, beam.X2) - 0.5, Math.Max(beam.X1, beam.X2) + 0.5,
                Math.Min(beam.Y1, beam.Y2) - BeamThickness, Math.Max(beam.Y1, beam.Y2) + BeamThickness);
        foreach (var tie in layout.Ties)
            if (!tie.IsStub) sky.Claim(tie.X1, tie.X2, Math.Min(tie.Y1, tie.Y2) - (tie.Above ? 21 : 0), Math.Max(tie.Y1, tie.Y2) + (tie.Above ? 0 : 21));
            else
            {
                var sign = tie.TowardLeft ? -1.0 : 1.0;
                var xa = tie.X1 + sign * 5; var xb = tie.X1 + sign * 13;
                sky.Claim(Math.Min(xa, xb), Math.Max(xa, xb), tie.Y1 - (tie.Above ? 18 : 0), tie.Y1 + (tie.Above ? 0 : 18));
            }
    }

    /// <summary>
    /// A mark of <paramref name="height"/> and <paramref name="x0"/>..<paramref name="x1"/> stacked outside the notation ink:
    /// above the staff (voice 1) or below it (voice 2), starting <paramref name="distance"/> from the staff edge.
    /// Returns the top of the placed box.
    /// </summary>
    internal static double PlaceMark(StaffNotationMeasureLayout layout, double x0, double x1, double height, double distance)
    {
        if (layout.IsSecondVoice) return layout.Skyline.PlaceBelow(x0, x1, height, layout.StaffTop + 4 * StaffGap + distance);
        return layout.Skyline.PlaceAbove(x0, x1, height, layout.StaffTop - distance);
    }

    /// <summary>A centred text mark claimed in the skyline; the glyph is drawn inside its box.</summary>
    private static void DrawStackedText(DrawingContext dc, StaffNotationMeasureLayout layout, string text, double size, FontWeight? weight,
        double cx, double distance, Brush brush)
    {
        var ft = MakeText(text, size, brush, weight);
        var h = size * 0.95;
        var top = PlaceMark(layout, cx - ft.Width / 2, cx + ft.Width / 2, h, distance);
        TabForge.Visualization.Draw.DrawText(dc, ft, new Point(cx - ft.Width / 2, top + h / 2 - ft.Height / 2));
    }

    /// <summary>A centred text mark stacked below the staff (after the dynamics row).</summary>
    private static void DrawStackedBelow(DrawingContext dc, StaffNotationMeasureLayout layout, string text, double size, FontWeight? weight,
        double cx, double distance, Brush brush)
    {
        var ft = MakeText(text, size, brush, weight);
        var h = size * 0.95;
        var top = layout.Skyline.PlaceBelow(cx - ft.Width / 2, cx + ft.Width / 2, h, layout.StaffTop + 4 * StaffGap + distance);
        TabForge.Visualization.Draw.DrawText(dc, ft, new Point(cx - ft.Width / 2, top + h / 2 - ft.Height / 2));
    }

    /// <summary>Draws glyphs and the already-resolved geometry; no rhythmic decisions happen here.</summary>
    public void DrawMeasure(
        DrawingContext dc,
        StaffNotationMeasureLayout layout,
        int measureIndex,
        Color ink,
        Color faint,
        Color accent,
        Color playColor,
        Color paper,
        Color staffLineColor,
        LedgerLineMode ledgerLineMode,
        IReadOnlySet<(int bar, int cell, int s)> sounding,
        IReadOnlySet<(int bar, int cell, int s)> struck)
    {
        if (Math.Abs(layout.StaffScale - 1.0) > 0.001)
            dc.PushTransform(new ScaleTransform(1, layout.StaffScale, 0, layout.StaffTop));
        var engravingInk = EngravingInkColor(ink, paper);
        var inkBrush = RenderDraw.Solid(engravingInk);
        var faintBrush = RenderDraw.Solid(faint);
        var paperBrush = RenderDraw.Solid(paper);

        DrawLedgerLines(dc, layout, staffLineColor, ledgerLineMode);
        DrawTuplets(dc, layout, inkBrush);   // first: the brackets sit next to the notation, every other mark stacks outside them

        foreach (var beat in layout.Beats)
        {
            if (beat.IsRest)
            {
                DrawRest(dc, beat.Cell, beat.CenterX, layout.StaffTop, inkBrush);
                if (beat.Cell.Fermata) DrawStackedText(dc, layout, layout.IsSecondVoice ? "𝄑" : "𝄐", 12, null, beat.CenterX, 8, inkBrush);   // a held rest keeps its fermata (voice 2: inverted, below)
                continue;
            }

            if (beat.IsDrum)
            {
                DrawDrumHeads(dc, beat, measureIndex, engravingInk, playColor, paper, sounding, struck);
                continue;
            }

            foreach (var note in beat.Notes)
            {
                var isSounding = sounding.Contains((measureIndex, beat.CellIndex, note.Source.StringIndex));
                var isStruck = struck.Contains((measureIndex, beat.CellIndex, note.Source.StringIndex));
                var notePlaybackColor = playColor == default ? Color.FromRgb(0x3F, 0xB9, 0x50) : playColor;
                var playBrush = RenderDraw.Solid(notePlaybackColor);
                var noteBrush = isSounding
                    ? playBrush
                    : RenderDraw.Solid(note.Source.Dead ? Color.FromRgb(110, 118, 128) : engravingInk);

                if (isSounding)
                    dc.DrawEllipse(RenderDraw.Solid(Color.FromArgb(isStruck ? (byte)85 : (byte)42,
                            notePlaybackColor.R, notePlaybackColor.G, notePlaybackColor.B)),
                        null, new Point(note.X, note.Y), isStruck ? 11 : 9.4, isStruck ? 9 : 7.6);

                var open = NormalizeDuration(beat.Cell.DurationDenominator) <= 2;
                dc.PushTransform(new RotateTransform(-20, note.X, note.Y));
                if (note.Source.Dead)
                {
                    DrawDeadNoteHead(dc, note.X, note.Y, noteBrush);
                }
                else if (HasHarmonic(note.Source.Techniques))
                {
                    // Harmonics use a diamond notehead (hollow for open durations).
                    var d = new StreamGeometry();
                    using (var g = d.Open())
                    {
                        g.BeginFigure(new Point(note.X - HeadRadiusX, note.Y), true, true);
                        g.LineTo(new Point(note.X, note.Y - HeadRadiusY - 1.2), true, false);
                        g.LineTo(new Point(note.X + HeadRadiusX, note.Y), true, false);
                        g.LineTo(new Point(note.X, note.Y + HeadRadiusY + 1.2), true, false);
                    }
                    d.Freeze();
                    dc.DrawGeometry(noteBrush, RenderDraw.Pen(noteBrush, 1.2), d); // The standard harmonics are solid diamonds
                }
                else if (open)
                {
                    // Mask the staff line under hollow heads, as a notation glyph should.
                    dc.DrawEllipse(paperBrush, null, new Point(note.X, note.Y), HeadRadiusX, HeadRadiusY);
                    dc.DrawEllipse(null, RenderDraw.Pen(noteBrush, 1.35), new Point(note.X, note.Y), HeadRadiusX, HeadRadiusY);
                }
                else dc.DrawEllipse(noteBrush, null, new Point(note.X, note.Y), HeadRadiusX, HeadRadiusY);
                dc.Pop();

                DrawAugmentationDots(dc, beat.Cell, note, noteBrush);
                if (note.Accidental is not null)
                    DrawCentered(dc, note.Accidental, AccidentalX(layout, beat, note),
                        note.Y, 15, noteBrush);
            }

            foreach (var c in GhostClusters(layout, beat)) // ghost notes: one pair of brackets around each cluster of touching ghost heads
            {
                // The 13 px bracket is stretched vertically (a bigger font would also widen it into the heads and accidentals).
                dc.PushTransform(new ScaleTransform(1, c.Half / GhostInkHalf, 0, c.InkY));
                Draw(dc, "(", c.L - HeadRadiusX - GhostOpenGap - c.Pad, c.InkY - GhostInkCentre, 13, inkBrush);   // closer to the heads: clears accidentals and the previous beat's stem
                Draw(dc, ")", c.R + HeadRadiusX + 1 + c.Pad, c.InkY - GhostInkCentre, 13, inkBrush);
                dc.Pop();
            }
            DrawGraceNotes(dc, beat, inkBrush, GhostRoom(layout.StaffTop, beat));
            var harmonicCaption = beat.Notes.Select(n => TabEditorControl.HarmonicCaption(n.Source.Techniques)).FirstOrDefault(c => c.Length > 0);
            if (!string.IsNullOrEmpty(harmonicCaption)) // The reference: the caption sits below the staff
                DrawStackedBelow(dc, layout, harmonicCaption, 8.5, FontWeights.SemiBold, beat.CenterX, 8, inkBrush);
            DrawBeatMarks(dc, layout, beat, inkBrush);

            var articulationY = layout.StaffTop + 4 * StaffGap + 4;
            if (beat.Cell.Accent != 0)
            {
                // One mark above the notes: ">" accent, "^" heavy accent (marcato).
                DrawStackedText(dc, layout, beat.Cell.Accent == 2 ? "^" : ">", 10, FontWeights.Bold, beat.CenterX, 8, inkBrush);
            }
            if ((beat.Cell.Staccato || beat.Cell.Tenuto) && beat.Notes.Count > 0)
            {
                // Beside the notehead on the side away from the stem, tenuto stacked outside the dot.
                var below = !beat.HasStem || beat.StemUp;
                var headY = below ? beat.MaxY + 9 : beat.MinY - 9;
                var step = below ? 6.0 : -6.0;
                var dotPen = RenderDraw.Pen(inkBrush, 1.6);
                if (beat.Cell.Staccato) { dc.DrawEllipse(inkBrush, null, new Point(beat.CenterX, headY), 1.5, 1.5); headY += step; }
                if (beat.Cell.Tenuto) dc.DrawLine(dotPen, new Point(beat.CenterX - 3.5, headY), new Point(beat.CenterX + 3.5, headY));
            }
            if (beat.Cell.Fermata) DrawStackedText(dc, layout, layout.IsSecondVoice ? "𝄑" : "𝄐", 12, null, beat.CenterX, 8, inkBrush);   // two voices: upright above and inverted below, as engraved
        }

        DrawStemsAndFlags(dc, layout, inkBrush);
        DrawBeams(dc, layout, inkBrush);
        DrawTremoloSlashes(dc, layout, inkBrush);
        DrawOctaveMarkings(dc, layout, inkBrush);
        DrawTies(dc, layout.Ties, inkBrush, layout.ContentLeft);
        DrawHopoSlurs(dc, layout.HopoSlurs, inkBrush);
        DrawSlideStrokes(dc, layout.Slides, inkBrush);
        if (Math.Abs(layout.StaffScale - 1.0) > 0.001) dc.Pop();
    }

    private static void DrawOctaveMarkings(DrawingContext dc, StaffNotationMeasureLayout layout, Brush brush)
    {
        var linePen = RenderDraw.Pen(brush, 0.9);
        StaffNotationBeat? first = null;
        StaffNotationBeat? last = null;
        void Finish()
        {
            if (first is null || last is null) return;
            var left = first.CenterX - 5;
            var right = last.CenterX + Math.Max(7, last.DurationSlots * layout.SlotWidth / 2);
            var label = first.Cell.OctaveShiftSemitones switch
            {
                12 => "8va", -12 => "8vb", 24 => "15ma", _ => "15mb"
            };
            var labelLeft = Math.Max(left, layout.OctaveLabelMinX);
            var labelWidth = MakeText(label, 8.5, brush, FontWeights.SemiBold).Width;
            var boxTop = PlaceMark(layout, left - 2, Math.Max(right, labelLeft + labelWidth), 20, 6) + 3;   // the caption (drawn 1.5 px above the line row, taller than its box) is inside the claim
            var y = boxTop + 13;
            Draw(dc, label, labelLeft, boxTop - 1.5, 8.5, brush, FontWeights.SemiBold);
            dc.DrawLine(linePen, new Point(left, y), new Point(right, y));
            dc.DrawLine(linePen, new Point(left, y), new Point(left, y + 4));
            dc.DrawLine(linePen, new Point(right, y), new Point(right, y + 4));
            first = null;
            last = null;
        }
        foreach (var beat in layout.Beats)
        {
            if (beat.Cell.OctaveShiftSemitones is not (-24 or -12 or 12 or 24)) continue;
            var contiguous = last is not null && last.Cell.OctaveShiftSemitones == beat.Cell.OctaveShiftSemitones &&
                            beat.StartSlots <= last.StartSlots + last.DurationSlots + PositionEpsilon;
            if (!contiguous) Finish();
            first ??= beat;
            last = beat;
        }
        Finish();
    }

    public static string RestGlyph(TabCell cell) => NormalizeDuration(cell.DurationDenominator) switch
    {
        <= 1 => "𝄻",
        2 => "𝄼",
        4 => "𝄽",
        8 => "𝄾",
        16 => "𝄿",
        32 => "𝅀",
        _ => "𝅁"
    };

    /// <summary>Harmonics the reference writes at the fretted pitch (not the sounding one): artificial, tap, pinch and semi.</summary>
    internal static bool IsWrittenAtFret(IEnumerable<string> techniques) =>
        techniques.Any(t => t is "ArtificialHarmonic" or "TapHarmonic" or "PinchHarmonic" or "SemiHarmonic");

    private static bool HasHarmonic(HashSet<string> techniques) =>
        techniques.Contains("Harmonic") || techniques.Contains("ArtificialHarmonic") ||
        techniques.Contains("PinchHarmonic") || techniques.Contains("TapHarmonic") ||
        techniques.Contains("SemiHarmonic") || techniques.Contains("FeedbackHarmonic");

    /// <summary>Bend text next to a notehead: amount of the peak (full, 1/2, 1 1/2 ...), with P.B. before a pre-bend.</summary>
    internal static string BendNotationLabel(TabNote note)
    {
        var points = TabEditorControl.EffectiveBendPoints(note);
        var peak = points.Count == 0 ? 4 : points.Max(p => p.Value);
        var amount = TabEditorControl.BendAmountLabel(peak);
        var pre = note.BendTypeName.StartsWith("Prebend", StringComparison.Ordinal) || points.Count > 0 && points[0].Offset < 1 && points[0].Value > 0.01;
        var release = note.BendTypeName.EndsWith("Release", StringComparison.Ordinal) || points.Count > 1 && points[^1].Value < peak - 0.01 && points[^1].Value < 0.5;
        return (pre ? "P.B. " : "") + amount + (release ? " R" : "");
    }

    /// <summary>How far left of the beat centre the first grace head sits: clear of the main notes' accidentals and ghost brackets.
    /// <paramref name="ghostRoom"/> is <see cref="GhostRoom"/> for the beat.</summary>
    internal static double GraceOffset(StaffNotationBeat beat, double ghostRoom)
    {
        var offset = 15.0;
        if (beat.Notes.Count == 0) return offset;
        var lead = beat.CenterX - beat.Notes.Min(n => n.X) + HeadRadiusX;
        if (ghostRoom > 0) offset = Math.Max(offset, lead + ghostRoom + 13);   // the grace slash clears the "(" (its ink ends 10 px left of the head)
        if (beat.Notes.Any(n => n.Accidental is not null))
        {
            var maxColumn = beat.Notes.Where(n => n.Accidental is not null).Max(n => n.AccidentalColumn);
            offset = Math.Max(offset, lead + 6.5 + 4.5 + maxColumn * 10 + 4 + 4 + ghostRoom);
        }
        return offset;
    }

    /// <summary>Extra room ghost brackets take left of the chord (8 px, more beside a ledger line): accidentals and grace notes sit outside it.</summary>
    internal static double GhostRoom(double staffTop, StaffNotationBeat beat)
    {
        var room = 0.0;
        foreach (var n in beat.Notes)
        {
            if (!n.Source.Ghost) continue;
            var ledger = n.Y < staffTop - 1 || n.Y > staffTop + 4 * StaffGap + 1;
            room = Math.Max(room, 8 + (ledger ? GhostLedgerPad : 0));
        }
        return room;
    }

    /// <summary>The (up to three) grace notes in the order they are drawn right to left: the latest in time first.</summary>
    internal static IReadOnlyList<StaffNotationNote> GraceDrawOrder(StaffNotationBeat beat) =>
        beat.GraceNotes.OrderBy(g => g.Source.GraceOnsetOffsetSlots).Take(3).Reverse().ToList();

    /// <summary>Small slashed grace notes just before the main note (stem up, slash across the stem).</summary>
    private static void DrawGraceNotes(DrawingContext dc, StaffNotationBeat beat, Brush brush, double ghostRoom)
    {
        if (beat.GraceNotes.Count == 0) return;
        var pen = RenderDraw.Pen(brush, 0.8);
        var x = beat.CenterX - GraceOffset(beat, ghostRoom);
        foreach (var grace in GraceDrawOrder(beat)) // latest grace nearest the main note, so they read left to right in time
        {
            var rx = HeadRadiusX * 0.72; var ry = HeadRadiusY * 0.72;
            dc.PushTransform(new RotateTransform(-20, x, grace.Y));
            dc.DrawEllipse(brush, null, new Point(x, grace.Y), rx, ry);
            dc.Pop();
            var stemX = x + rx * 0.92;
            dc.DrawLine(pen, new Point(stemX, grace.Y), new Point(stemX, grace.Y - 17));
            dc.DrawLine(pen, new Point(stemX - 4, grace.Y - 9), new Point(stemX + 4.5, grace.Y - 14));
            x -= 9;
        }
    }

    /// <summary>Wavy arpeggio line / straight brush arrow left of a chord, trill "tr" + wave, wah +/o, above-staff marks.</summary>
    private static void DrawBeatMarks(DrawingContext dc, StaffNotationMeasureLayout layout, StaffNotationBeat beat, Brush brush)
    {
        var techniques = beat.Cell.Notes.SelectMany(n => n.Techniques).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (beat.Notes.Count > 0 && (techniques.Contains("ArpeggioDown") || techniques.Contains("ArpeggioUp") ||
                                     techniques.Contains("BrushDown") || techniques.Contains("BrushUp")))
        {
            var wavy = techniques.Contains("ArpeggioDown") || techniques.Contains("ArpeggioUp");
            var down = techniques.Contains("ArpeggioDown") || techniques.Contains("BrushDown");
            var x = beat.CenterX - 13 - (beat.Notes.Any(n => n.Accidental is not null) ? 8 : 0);
            var y1 = beat.Notes.Min(n => n.Y) - 4;
            var y2 = Math.Max(beat.Notes.Max(n => n.Y) + 4, y1 + 9);
            var pen = RenderDraw.Pen(brush, 1.0);
            if (wavy)
            {
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(x, y1), false, false);
                    for (var y = y1; y < y2; y += 3.5) c.LineTo(new Point(x + (((int)((y - y1) / 3.5) % 2) == 0 ? 1.8 : -1.8), y + 1.75), true, false);
                }
                g.Freeze();
                dc.DrawGeometry(null, pen, g);
            }
            else dc.DrawLine(pen, new Point(x, y1), new Point(x, y2));
            var tipY = down ? y2 + 1.5 : y1 - 1.5;
            var dir = down ? 1 : -1;
            var head = new StreamGeometry();
            using (var c = head.Open())
            {
                c.BeginFigure(new Point(x, tipY), true, true);
                c.LineTo(new Point(x - 2.6, tipY - dir * 4.2), true, false);
                c.LineTo(new Point(x + 2.6, tipY - dir * 4.2), true, false);
            }
            head.Freeze();
            dc.DrawGeometry(brush, null, head);
        }
        if (techniques.Contains("Trill"))
        {
            var trWidth = MakeText("tr", 9, brush, FontWeights.SemiBold).Width;
            var trTop = PlaceMark(layout, beat.CenterX - 5 - trWidth / 2, beat.CenterX + 4 + 8 * 2.6 + 1, 15, 6);
            DrawCentered(dc, "tr", beat.CenterX - 5, trTop + 4.5, 9, brush, FontWeights.SemiBold);
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                var y = trTop + 12;
                c.BeginFigure(new Point(beat.CenterX + 4, y), false, false);
                for (var i = 1; i <= 8; i++) c.LineTo(new Point(beat.CenterX + 4 + i * 2.6, y + (i % 2 == 0 ? 0 : -2)), true, false);
            }
            g.Freeze();
            dc.DrawGeometry(null, RenderDraw.Pen(brush, 0.9), g);
        }
        if (techniques.Contains("WahClose") || techniques.Contains("WahOpen"))
        {
            DrawStackedText(dc, layout, techniques.Contains("WahClose") ? "+" : "o", 10, FontWeights.Bold, beat.CenterX, 8, brush);
            // The words appear where the pedal turns on, not on every beat of a run.
            var at = layout.Beats.ToList().IndexOf(beat);
            var previousHasWah = at > 0 && layout.Beats[at - 1].Cell.Notes.Any(n => n.Techniques.Contains("WahClose") || n.Techniques.Contains("WahOpen"));
            if (!previousHasWah) DrawStackedBelow(dc, layout, "Wah-wah on", 8.5, null, beat.CenterX, 8, brush);
        }
        if (techniques.Contains("Tapping") || techniques.Contains("LeftTap"))
        {
            // Tapped notes carry a "+" above the notation.
            DrawStackedText(dc, layout, "+", 10, FontWeights.Bold, beat.CenterX, 8, brush);
        }
    }

    /// <summary>Tremolo picking: 1-3 slashes across the stem (above the head when the note has no stem).</summary>
    private static void DrawTremoloSlashes(DrawingContext dc, StaffNotationMeasureLayout layout, Brush brush)
    {
        var pen = RenderDraw.Pen(brush, 1.7);
        foreach (var beat in layout.Beats)
        {
            var count = TabEditorControl.TremoloSlashCount(beat.Cell);
            if (count == 0 || beat.IsRest || beat.IsDrum) continue;
            double cx, cy;
            if (beat.HasStem) { cx = beat.StemX; cy = (beat.StemStartY + beat.StemEndY) / 2 + (beat.Flags > 0 ? (beat.StemUp ? 3 : -3) : 0); }
            else { cx = beat.CenterX; cy = beat.MinY - 12; }
            for (var k = 0; k < count; k++)
            {
                var y = cy + (k - (count - 1) / 2.0) * 3.4;
                dc.DrawLine(pen, new Point(cx - 4.8, y + 2.2), new Point(cx + 4.8, y - 2.2));
            }
        }
    }

    private static void DrawVibratoMark(DrawingContext dc, double centerX, double y, bool wide, Brush brush)
    {
        const double halfWidth = 8;
        var amplitude = wide ? 2.1 : 1.35;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(centerX - halfWidth, y), false, false);
            for (var index = 1; index <= 8; index++)
                context.LineTo(new Point(centerX - halfWidth + index * 2,
                    y + Math.Sin(index * Math.PI / 2) * amplitude), true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, RenderDraw.Pen(brush, 0.9), geometry);
    }

    private static void DrawDeadNoteHead(DrawingContext dc, double x, double y, Brush brush)
    {
        var pen = RenderDraw.RoundPen(brush, 1.4);
        dc.DrawLine(pen, new Point(x - 4.2, y - 3.2), new Point(x + 4.2, y + 3.2));
        dc.DrawLine(pen, new Point(x - 4.2, y + 3.2), new Point(x + 4.2, y - 3.2));
    }

    internal static int NormalizeDuration(int denominator)
        => Math.Clamp(denominator <= 0 ? 16 : denominator, 1, 64);

    private void AssignNoteheadsAndStems(List<StaffNotationBeat> beats, double staffTop, double staffBottom, bool isDrum)
    {
        var middleY = staffTop + MiddleLineOffset;
        foreach (var beat in beats)
        {
            if (isDrum)
            {
                // Percussion staff: each sound sits at its mapped position (kick low, snare middle,
                // hi-hat/cymbals above); one up-stem joins the beat's notes.
                var ys = beat.Cell.Notes.Select(n => staffTop + (beat.DrumMap?.Invoke(n.MidiValue > 0 ? n.MidiValue : n.Fret).StaffStep ?? 4) * StaffGap / 2).ToList();
                beat.MinY = ys.Count > 0 ? ys.Min() : middleY;
                beat.MaxY = ys.Count > 0 ? ys.Max() : middleY;
                if (!beat.HasStem) continue;
                beat.StemUp = true;
                beat.StemX = beat.CenterX + 5;
                beat.StemStartY = beat.MaxY;
                beat.StemEndY = Math.Min(beat.MinY, middleY) - StemLength;
                var upperYs = ys.Where(y => y <= middleY).ToList();
                var lowerYs = ys.Where(y => y > middleY).ToList();
                if (upperYs.Count > 0 && lowerYs.Count > 0)
                {
                    // Two voices drum notation: cymbals up, drums down, so no stem runs the whole staff.
                    beat.StemStartY = upperYs.Max();
                    beat.StemEndY = upperYs.Min() - StemLength;
                    beat.LowerStemTopY = lowerYs.Min();
                    beat.LowerStemEndY = lowerYs.Max() + StemLength * 0.85;
                }
                continue;
            }
            if (beat.Notes.Count == 0) continue;

            // Whole notes have no stems, but still need full notehead and pitch geometry.
            var naturalUp = beat.TwoVoices ? !beat.AutoStemInvert : (beat.Notes.Average(n => n.Y) > middleY) ^ beat.AutoStemInvert;
            beat.StemUp = beat.Cell.StemDirection switch
            {
                StemDirection.Up => true,
                StemDirection.Down => false,
                StemDirection.Invert => !naturalUp,
                _ => naturalUp
            };
            ArrangeChordHeads(beat);
            beat.MinY = beat.Notes.Min(n => n.Y);
            beat.MaxY = beat.Notes.Max(n => n.Y);
            if (beat.HasStem) SetStemGeometry(beat, beat.StemUp);
        }

        foreach (var beat in beats.Where(b => b.BeamGroupIndex >= 0))
        {
            var group = beats.Where(b => b.BeamGroupIndex == beat.BeamGroupIndex).ToList();
            if (group.Count == 0) continue;
            var meanY = group.SelectMany(b => b.Notes).Select(n => n.Y).DefaultIfEmpty(middleY).Average();
            var stemOverride = group.Select(b => b.Cell.StemDirection).FirstOrDefault(value => value != StemDirection.Auto);
            var naturalUp = group.All(b => b.IsDrum) || (group[0].TwoVoices ? !group[0].AutoStemInvert : (meanY > middleY) ^ group[0].AutoStemInvert);
            var up = stemOverride switch { StemDirection.Up => true, StemDirection.Down => false, StemDirection.Invert => !naturalUp, _ => naturalUp };
            foreach (var member in group)
            {
                member.StemUp = up;
                if (member.IsDrum)
                {
                    member.MinY = member.MaxY = middleY;
                    member.StemX = member.CenterX + 4;
                    member.StemStartY = middleY;
                    member.StemEndY = middleY - StemLength;
                }
                else
                {
                    ArrangeChordHeads(member);
                    SetStemGeometry(member, up);
                }
            }
        }
    }

    private static void ArrangeChordHeads(StaffNotationBeat beat)
    {
        var ordered = beat.Notes.OrderBy(n => n.StaffStep).ThenBy(n => n.Source.StringIndex).ToList();
        var adjacentRun = 0;
        var previousStep = int.MinValue;
        foreach (var note in ordered)
        {
            if (previousStep == int.MinValue || note.StaffStep - previousStep > 1) adjacentRun = 0;
            else if (note.StaffStep - previousStep == 1) adjacentRun++;
            note.X = beat.CenterX;
            if (adjacentRun > 0)
                note.X += beat.StemUp
                    ? (adjacentRun % 2 == 1 ? -6.5 : 0)
                    : (adjacentRun % 2 == 1 ? 6.5 : 0);
            previousStep = note.StaffStep;
        }
    }

    private static void SetStemGeometry(StaffNotationBeat beat, bool up)
    {
        if (beat.Notes.Count == 0) return;
        beat.MinY = beat.Notes.Min(n => n.Y);
        beat.MaxY = beat.Notes.Max(n => n.Y);
        var minX = beat.Notes.Min(n => n.X);
        var maxX = beat.Notes.Max(n => n.X);
        beat.StemUp = up;
        beat.StemX = up ? maxX + HeadRadiusX * 0.9 : minX - HeadRadiusX * 0.9;
        beat.StemStartY = up ? beat.MaxY : beat.MinY;
        // The stem reaches a full length past the note farthest in its direction (a wide chord's stem clears every head).
        beat.StemEndY = up ? beat.MinY - StemLength : beat.MaxY + StemLength;
    }

    private static List<StaffNotationBeamGroup> BuildBeamGroups(
        IReadOnlyList<StaffNotationBeat> beats,
        IReadOnlyList<MetricGroup> metricGroups)
    {
        var result = new List<StaffNotationBeamGroup>();
        var pending = new List<StaffNotationBeat>();
        var pendingMetric = -1;

        void Finish()
        {
            if (pending.Count >= 2)
            {
                var metric = metricGroups[pendingMetric];
                var group = new StaffNotationBeamGroup
                {
                    Index = result.Count,
                    Beats = pending.ToArray(),
                    StemUp = false,
                    MetricStart = metric.Start,
                    MetricWidth = metric.Width,
                    MaxFlags = pending.Max(b => b.Flags)
                };
                result.Add(group);
                foreach (var beat in pending) beat.BeamGroupIndex = group.Index;
            }
            pending = new List<StaffNotationBeat>();
            pendingMetric = -1;
        }

        foreach (var beat in beats)
        {
            var metricIndex = FindMetricGroup(metricGroups, beat.StartSlots);
            var forcedAcrossMetric = pending.Count > 0 &&
                (beat.Cell.BeamMode == BeamMode.Force || pending[^1].Cell.BeamMode == BeamMode.Force);
            var atExplicitBreak = beat.Cell.BeamMode == BeamMode.Break;
            var continues = beat.IsBeamable && pending.Count > 0 && !atExplicitBreak &&
                           (metricIndex == pendingMetric || forcedAcrossMetric) &&
                           Math.Abs(pending[^1].StartSlots + pending[^1].DurationSlots - beat.StartSlots) < (beat.Cell.Tuplet.Numerator >= 2 && pending[^1].Cell.Tuplet.Numerator >= 2 ? TupletTickSlack : PositionEpsilon);
            if (!continues) Finish();
            if (beat.IsBeamable && metricIndex >= 0)
            {
                if (pending.Count == 0) pendingMetric = metricIndex;
                pending.Add(beat);
            }
            else Finish();
        }
        Finish();
        return result;
    }

    private static void AssignBeamStemDirections(IReadOnlyList<StaffNotationBeamGroup> groups, double middleY)
    {
        foreach (var group in groups)
        {
            var notes = group.Beats.SelectMany(b => b.Notes).ToList();
            var stemOverride = group.Beats.Select(b => b.Cell.StemDirection).FirstOrDefault(value => value != StemDirection.Auto);
            var naturalUp = group.Beats.All(b => b.IsDrum) ||
                (group.Beats[0].TwoVoices ? !group.Beats[0].AutoStemInvert : notes.Count > 0 && ((notes.Average(n => n.Y) > middleY) ^ group.Beats[0].AutoStemInvert));
            var up = stemOverride switch { StemDirection.Up => true, StemDirection.Down => false, StemDirection.Invert => !naturalUp, _ => naturalUp };
            group.StemUp = up;
        }
    }

    private static List<MetricGroup> BuildMetricGroups(int numerator, int denominator, int slots)
    {
        numerator = Math.Clamp(numerator, 1, 32);
        denominator = Math.Clamp(denominator, 1, 64);
        var unit = 16.0 / denominator;
        var pattern = new List<double>();
        if (denominator >= 8 && numerator >= 3 && numerator % 3 == 0)
        {
            pattern.Add(unit * 3); // compound pulse (6/8, 9/8, 6/16 ...)
        }
        else if (denominator == 8)
        {
            var beats = numerator switch
            {
                5 => new[] { 3, 2 },
                7 => new[] { 2, 2, 3 },
                4 => new[] { 2, 2 },
                8 => new[] { 3, 3, 2 },
                10 => new[] { 3, 3, 2, 2 },
                _ => Enumerable.Repeat(1, numerator).ToArray()
            };
            pattern.AddRange(beats.Select(n => n * unit));
        }
        else
        {
            pattern.AddRange(Enumerable.Repeat(unit, numerator));
        }

        var groups = new List<MetricGroup>();
        var at = 0.0;
        var patternIndex = 0;
        while (at < slots - PositionEpsilon && groups.Count < 64)
        {
            var width = pattern[patternIndex % pattern.Count];
            var end = Math.Min(slots, at + width);
            if (end <= at + PositionEpsilon) break;
            groups.Add(new MetricGroup(at, end, end - at));
            at = end;
            patternIndex++;
        }
        if (groups.Count == 0) groups.Add(new MetricGroup(0, Math.Max(1, slots), Math.Max(1, slots)));
        return groups;
    }

    private static int FindMetricGroup(IReadOnlyList<MetricGroup> groups, double start)
    {
        for (var i = 0; i < groups.Count; i++)
            if (start >= groups[i].Start - PositionEpsilon && start < groups[i].End - PositionEpsilon / 2)
                return i;
        return -1;
    }

    private static List<StaffNotationBeamSegment> BuildBeamSegments(IReadOnlyList<StaffNotationBeamGroup> groups)
    {
        var segments = new List<StaffNotationBeamSegment>();
        foreach (var group in groups)
        {
            var first = group.Beats[0];
            var last = group.Beats[^1];
            var firstNatural = NaturalStemEnd(first, group.StemUp);
            var lastNatural = NaturalStemEnd(last, group.StemUp);
            var dx = last.StemX - first.StemX;
            var slope = Math.Abs(dx) < 0.01 ? 0 : Math.Clamp((lastNatural - firstNatural) / dx, -0.22, 0.22);
            var intercept = group.Beats.Average(b => NaturalStemEnd(b, group.StemUp) - slope * (b.StemX - first.StemX));

            // Preserve a normal minimum stem length after limiting the slope.
            foreach (var beat in group.Beats)
            {
                var relativeX = beat.StemX - first.StemX;
                if (group.StemUp) intercept = Math.Min(intercept, beat.MinY - 18 - slope * relativeX);
                else intercept = Math.Max(intercept, beat.MaxY + 18 - slope * relativeX);
            }

            group.Slope = slope;
            group.Intercept = intercept;
            foreach (var beat in group.Beats) beat.StemEndY = group.BaseYAt(beat.StemX);

            segments.Add(new StaffNotationBeamSegment(group.Index, 1, first.CellIndex, last.CellIndex,
                first.StemX, group.BaseYAt(first.StemX), last.StemX, group.BaseYAt(last.StemX), false));

            for (var level = 2; level <= group.MaxFlags; level++)
            {
                    // The secondary beam fills the beat. Each additional flag level divides that
                    // beat once more (32nds group their third beam by half-beat, 64ths by eighth).
                    var subdivision = group.MetricWidth / Math.Pow(2, level - 2);
                var eligible = group.Beats.Where(b => b.Flags >= level).ToList();
                var cursor = 0;
                while (cursor < eligible.Count)
                {
                    var run = new List<StaffNotationBeat> { eligible[cursor] };
                    var band = SubdivisionIndex(eligible[cursor].StartSlots, group.MetricStart, subdivision);
                    var next = cursor + 1;
                    while (next < eligible.Count &&
                           SubdivisionIndex(eligible[next].StartSlots, group.MetricStart, subdivision) == band &&
                           !eligible[next].Cell.BreakSecondaryBeamBefore &&
                           Math.Abs(run[^1].StartSlots + run[^1].DurationSlots - eligible[next].StartSlots) < PositionEpsilon)
                    {
                        run.Add(eligible[next]);
                        next++;
                    }

                    var offset = (level - 1) * BeamGap * (group.StemUp ? 1 : -1);
                    if (run.Count >= 2)
                    {
                        var a = run[0];
                        var b = run[^1];
                        segments.Add(new StaffNotationBeamSegment(group.Index, level, a.CellIndex, b.CellIndex,
                            a.StemX, group.BaseYAt(a.StemX) + offset,
                            b.StemX, group.BaseYAt(b.StemX) + offset, false));
                    }
                    else
                    {
                        var only = run[0];
                        var position = group.Beats.ToList().IndexOf(only);
                        var partialRight = position < group.Beats.Count - 1;
                        var neighbor = partialRight ? group.Beats[position + 1] : group.Beats[position - 1];
                        var hook = Math.Clamp(Math.Abs(neighbor.StemX - only.StemX) * 0.42, 3.5, 7.0);
                        var x1 = only.StemX;
                        var x2 = only.StemX + (partialRight ? hook : -hook);
                        segments.Add(new StaffNotationBeamSegment(group.Index, level, only.CellIndex, only.CellIndex,
                            x1, group.BaseYAt(x1) + offset,
                            x2, group.BaseYAt(x2) + offset, true));
                    }
                    cursor = next;
                }
            }
        }
        return segments;
    }

    private static int SubdivisionIndex(double start, double groupStart, double width)
        => width <= PositionEpsilon ? 0 : (int)Math.Floor((start - groupStart + PositionEpsilon) / width);

    private static double NaturalStemEnd(StaffNotationBeat beat, bool up)
        => up ? beat.MinY - StemLength : beat.MaxY + StemLength; // measured from the farthest note of a chord

    private static List<StaffNotationTupletGroup> BuildTupletGroups(
        IReadOnlyList<StaffNotationBeat> beats,
        IReadOnlyList<StaffNotationBeamGroup> beamGroups)
    {
        var result = new List<StaffNotationTupletGroup>();
        var run = new List<StaffNotationBeat>();
        (int Numerator, int Denominator) ratio = (0, 0);

        void Finish()
        {
            if (ratio.Numerator >= 2)
            {
                for (var offset = 0; offset + ratio.Numerator <= run.Count; offset += ratio.Numerator)
                {
                    var members = run.Skip(offset).Take(ratio.Numerator).ToArray();
                    var groupIndex = members[0].BeamGroupIndex;
                    var beamed = groupIndex >= 0 && members.All(b => b.BeamGroupIndex == groupIndex);
                    result.Add(new StaffNotationTupletGroup
                    {
                        Beats = members,
                        Numerator = ratio.Numerator,
                        BeamGroupIndex = beamed ? groupIndex : -1
                    });
                }
            }
            run = new List<StaffNotationBeat>();
            ratio = (0, 0);
        }

        foreach (var beat in beats)
        {
            var current = beat.Cell.Tuplet;
            var valid = current.Numerator >= 2 && (beat.Cell.Notes.Count > 0 || beat.IsRest);
            var contiguous = run.Count == 0 ||
                             (current == ratio && Math.Abs(run[^1].StartSlots + run[^1].DurationSlots - beat.StartSlots) < TupletTickSlack);
            if (!valid || !contiguous) Finish();
            if (valid)
            {
                if (run.Count == 0) ratio = current;
                run.Add(beat);
            }
            else Finish();
        }
        Finish();
        return result;
    }

    private static void ResolveAccidentals(IReadOnlyList<StaffNotationBeat> beats, IReadOnlyList<int> keyAlterations)
    {
        var state = new Dictionary<(int Letter, int Octave), int>();
        foreach (var beat in beats)
        foreach (var note in beat.Notes.OrderBy(n => n.StaffStep))
        {
            var signatureAlteration = keyAlterations[note.Letter];
            var key = (note.Letter, note.Octave);
            var current = state.TryGetValue(key, out var prior) ? prior : signatureAlteration;
            if (!note.Source.Tied && !beat.Cell.IsTied && note.Alteration != current)
                note.Accidental = note.Alteration switch { > 0 => "♯", < 0 => "♭", _ => "♮" };
            state[key] = note.Alteration;
        }
    }

    /// <summary>Accidentals hang off the chord's leftmost head, so a head displaced to the left (seconds) never sits under an accidental.</summary>
    private static double AccidentalX(StaffNotationMeasureLayout layout, StaffNotationBeat beat, StaffNotationNote note) =>
        beat.Notes.Min(n => n.X) - HeadRadiusX - 6.5 - GhostRoom(layout.StaffTop, beat) - note.AccidentalColumn * 10;

    private const double GhostLedgerPad = LedgerOverhang + 2;
    /// <summary>Distance from a ghost cluster's first head to the "(" glyph's origin (its ink ends about 3.5 px from the head).</summary>
    private const double GhostOpenGap = 8.5;
    /// <summary>Ink of a 13 px bracket drawn at y: centre y + 10.5, half-height 5.6. A lone ghost head keeps the bracket's
    /// historic 2.5 px drop; a cluster's brackets are centred on it and stretched to cover every head.</summary>
    private const double GhostInkCentre = 10.5, GhostInkHalf = 5.6;

    /// <summary>Clusters of ghost heads in one beat (heads closer than a bracket's height share one pair of brackets);
    /// InkY/Half are the brackets' ink centre and half-height, Pad clears ledger lines.</summary>
    private static List<(double L, double R, double InkY, double Half, double Pad)> GhostClusters(StaffNotationMeasureLayout layout, StaffNotationBeat beat)
    {
        var result = new List<(double, double, double, double, double)>();
        var ghosts = beat.Notes.Where(n => n.Source.Ghost).OrderBy(n => n.Y).ToList();
        for (var i = 0; i < ghosts.Count;)
        {
            var j = i;
            while (j + 1 < ghosts.Count && ghosts[j + 1].Y - ghosts[j].Y < 13) j++;
            var group = ghosts.GetRange(i, j - i + 1);
            var span = group[^1].Y - group[0].Y;
            var pad = group.Any(n => OnLedger(layout, n)) ? GhostLedgerPad : 0;
            var mid = (group[0].Y + group[^1].Y) / 2;
            result.Add((group.Min(n => n.X), group.Max(n => n.X), span > 0 ? mid : mid + 2.5, GhostInkHalf + span / 2, pad));
            i = j + 1;
        }
        return result;
    }

    private static void AssignAccidentalColumns(IReadOnlyList<StaffNotationBeat> beats)
    {
        foreach (var beat in beats)
        {
            var placed = new List<(int Step, int Column)>();
            foreach (var note in beat.Notes.Where(n => n.Accidental is not null).OrderBy(n => n.StaffStep))
            {
                var column = 0;
                while (placed.Any(p => p.Column == column && Math.Abs(p.Step - note.StaffStep) <= 2)) column++;
                note.AccidentalColumn = column;
                placed.Add((note.StaffStep, column));
            }
        }
    }

    /// <summary>A simile bar shows only its sign: no tie runs into it or out of it (the notes behind it are a copy kept for playback).</summary>
    internal static bool IsSimileBar(TrackModel? track, int measureIndex) =>
        track is not null && measureIndex >= 0 && measureIndex < track.Measures.Count && (track.Measures[measureIndex].SimileOneBar || track.Measures[measureIndex].SimileTwoBar);

    /// <summary>
    /// Where an arc between two heads starts and ends so it stays clear of the ink around them: a ghost bracket after the first head
    /// and in front of the second, and the accidentals of the second beat that sit at about the arc's height. Returns (5, 5), the plain
    /// head-to-head arc, when there is nothing to avoid or no room to avoid it in.
    /// </summary>
    internal static (double Start, double End) ArcInsets(StaffNotationBeat originBeat, StaffNotationNote origin,
        StaffNotationBeat destBeat, StaffNotationNote dest, double staffTop)
    {
        double start = 5, end = 5;
        if (origin.Source.Ghost) start = Math.Max(start, HeadRadiusX + 8 + (OnLedgerAt(staffTop, origin) ? GhostLedgerPad : 0));
        if (dest.Source.Ghost) end = Math.Max(end, HeadRadiusX + GhostOpenGap + 1.5 + (OnLedgerAt(staffTop, dest) ? GhostLedgerPad : 0));
        var room = GhostRoom(staffTop, destBeat);
        var leftmost = destBeat.Notes.Min(n => n.X);
        foreach (var other in destBeat.Notes)
        {
            if (other.Accidental is null || Math.Abs(other.Y - dest.Y) > 14) continue;
            var accidentalLeft = leftmost - HeadRadiusX - 6.5 - room - other.AccidentalColumn * 10 - 4.5;
            end = Math.Max(end, dest.X - accidentalLeft + 1.5);
        }
        // Too tight to stop short (the arc would turn back on itself): keep the plain head-to-head arc.
        return dest.X - end - (origin.X + start) < 12 ? (5, 5) : (start, end);
    }

    private static bool OnLedgerAt(double staffTop, StaffNotationNote note) => note.Y < staffTop - 1 || note.Y > staffTop + 4 * StaffGap + 1;

    private static List<StaffNotationTie> BuildTies(TrackModel? track, int measureIndex, int measureSlots,
        IReadOnlyList<StaffNotationBeat> beats, int voiceIndex, double staffTop)
    {
        var ties = new List<StaffNotationTie>();
        foreach (var beat in beats.Where(b => b.Notes.Count > 0))
        foreach (var note in beat.Notes)
        {
            if (note.Source.Tied || beat.Cell.IsTied)
            {
                var previous = PreviousNoteInMeasure(beats, beat, note.Source, track);
                if (previous is { } prior)
                {
                    var (start, end) = ArcInsets(prior.Beat, prior.Note, beat, note, staffTop);
                    ties.Add(new StaffNotationTie(prior.Note.X, prior.Note.Y, note.X, note.Y,
                        Above: !beat.StemUp, IsStub: false, TowardLeft: false, StartInset: start, EndInset: end));
                }
                else if (beat.StartSlots < PositionEpsilon && !IsSimileBar(track, measureIndex - 1) &&
                          FindAdjacentBarNote(track, measureIndex - 1, note.Source, track, voiceIndex) is { } barPrior)
                {
                    // The stub reaches left over the chord's accidentals and ghost bracket: start it beyond them.
                    var stubX = note.X;
                    var room = GhostRoom(staffTop, beat);
                    var leftmost = beat.Notes.Min(n => n.X);
                    foreach (var other in beat.Notes)
                    {
                        if (other.Accidental is null || Math.Abs(other.Y - note.Y) > 14) continue;
                        stubX = Math.Min(stubX, leftmost - HeadRadiusX - 6.5 - room - other.AccidentalColumn * 10 - 4.5 + 4);
                    }
                    if (note.Source.Ghost) stubX = Math.Min(stubX, note.X - HeadRadiusX - GhostOpenGap - 1.5 - (OnLedgerAt(staffTop, note) ? GhostLedgerPad : 0) + 4);
                    ties.Add(new StaffNotationTie(stubX, note.Y, stubX, note.Y,
                        Above: !beat.StemUp, IsStub: true, TowardLeft: true));
                }
            }

            if (track is null || measureIndex + 1 >= track.Measures.Count || IsSimileBar(track, measureIndex + 1) ||
                beat.StartSlots + beat.DurationSlots < measureSlots - PositionEpsilon) continue;
            var next = NextNoteInMeasure(beats, beat, note.Source, track);
            if (next is not null) continue; // Same-measure destination draws the full tie above.
            var nextBar = FindAdjacentBarTieDestination(track, measureIndex + 1, note.Source, track, voiceIndex);
            if (nextBar is { } destination && destination.StartSlots < PositionEpsilon &&
                (destination.Cell.IsTied || destination.Note.Tied))
                ties.Add(new StaffNotationTie(note.Source.Ghost ? note.X + HeadRadiusX + 3 + (OnLedgerAt(staffTop, note) ? GhostLedgerPad : 0) : note.X, note.Y,
                    note.Source.Ghost ? note.X + HeadRadiusX + 3 + (OnLedgerAt(staffTop, note) ? GhostLedgerPad : 0) : note.X, note.Y,
                    Above: !beat.StemUp, IsStub: true, TowardLeft: false));
        }
        return ties;
    }

    private static (StaffNotationBeat Beat, StaffNotationNote Note)? PreviousNoteInMeasure(
        IReadOnlyList<StaffNotationBeat> beats, StaffNotationBeat current, TabNote target, TrackModel? track)
    {
        foreach (var beat in beats.Where(b => b.StartSlots < current.StartSlots - PositionEpsilon).OrderByDescending(b => b.StartSlots))
        {
            var note = beat.Notes.FirstOrDefault(n => SameTieIdentity(n.Source, target, track));
            if (note is not null)
                return Math.Abs(beat.StartSlots + beat.DurationSlots - current.StartSlots) < PositionEpsilon
                    ? (beat, note)
                    : null;
            if (UsesStringIdentity(track) && beat.Notes.Any(n => n.Source.StringIndex == target.StringIndex)) return null;
        }
        return null;
    }

    private static TabNote? FindAdjacentBarNote(TrackModel? track, int measureIndex, TabNote target, TrackModel? identityTrack, int voiceIndex)
    {
        if (track is null || measureIndex < 0 || measureIndex >= track.Measures.Count) return null;
        var cells = track.Measures[measureIndex].CellsForVoice(voiceIndex);
        foreach (var pair in cells.Select((cell, index) => (Cell: cell, Index: index))
                     .Where(pair => pair.Cell.Notes.Count > 0)
                     .OrderByDescending(pair => pair.Cell.RhythmicPosition ?? pair.Index))
        {
            var note = pair.Cell.Notes.FirstOrDefault(n => SameTieIdentity(n, target, identityTrack));
            if (note is not null) return note;
            if (UsesStringIdentity(identityTrack) && pair.Cell.Notes.Any(n => n.StringIndex == target.StringIndex)) return null;
        }
        return null;
    }

    private static (TabCell Cell, TabNote Note, double StartSlots)? FindAdjacentBarTieDestination(
        TrackModel track, int measureIndex, TabNote target, TrackModel? identityTrack, int voiceIndex)
    {
        if (measureIndex < 0 || measureIndex >= track.Measures.Count) return null;
        var measure = track.Measures[measureIndex];
        var cells = measure.CellsForVoice(voiceIndex);
        var events = cells.Select((cell, index) => (Cell: cell, Index: index))
            .Where(pair => pair.Cell.Notes.Count > 0 || pair.Cell.IsRest)
            .Select(pair => (pair.Cell, Start: pair.Cell.RhythmicPosition ?? pair.Index))
            .ToList();
        if (events.Count == 0) return null;
        var firstStart = events.Min(pair => pair.Start);
        foreach (var pair in events.Where(pair => Math.Abs(pair.Start - firstStart) < PositionEpsilon))
        {
            var note = pair.Cell.Notes.FirstOrDefault(n => SameTieIdentity(n, target, identityTrack));
            if (note is not null) return (pair.Cell, note, pair.Start);
        }
        return null;
    }

    private static StaffNotationNote? NextNoteInMeasure(
        IReadOnlyList<StaffNotationBeat> beats, StaffNotationBeat current, TabNote target, TrackModel? track)
    {
        foreach (var beat in beats.Where(b => b.StartSlots > current.StartSlots + PositionEpsilon).OrderBy(b => b.StartSlots))
        {
            var note = beat.Notes.FirstOrDefault(n => SameTieIdentity(n.Source, target, track));
            if (note is not null) return note;
        }
        return null;
    }

    private static bool UsesStringIdentity(TrackModel? track)
        => track?.Kind is TrackKind.Guitar or TrackKind.Bass;

    private static bool SameTieIdentity(TabNote candidate, TabNote target, TrackModel? track)
        => candidate.MidiValue == target.MidiValue &&
           (!UsesStringIdentity(track) || candidate.StringIndex == target.StringIndex);

    private static List<StaffNotationSlur> BuildHopoSlurs(IReadOnlyList<StaffNotationBeat> beats, double staffTop)
    {
        var result = new List<StaffNotationSlur>();
        foreach (var beat in beats)
        foreach (var note in beat.Notes.Where(n => n.Source.Techniques.Contains("HOPOOrigin")))
        {
            var previousOrigin = beats.Where(b => b.StartSlots < beat.StartSlots - PositionEpsilon)
                .SelectMany(b => b.Notes.Select(n => (Beat: b, Note: n)))
                .LastOrDefault(pair => pair.Note.Source.StringIndex == note.Source.StringIndex &&
                                       pair.Note.Source.Techniques.Contains("HOPOOrigin"));
            if (previousOrigin.Note is not null) continue;

            var destination = beats.Where(b => b.StartSlots > beat.StartSlots + PositionEpsilon)
                .SelectMany(b => b.Notes.Select(n => (Beat: b, Note: n)))
                .FirstOrDefault(pair => pair.Note.Source.StringIndex == note.Source.StringIndex &&
                                        pair.Note.Source.Techniques.Contains("HOPODestination"));
            if (destination.Note is null) continue;
            if (MakeHopoSlur(beat, note, destination.Beat, destination.Note, staffTop) is { } slur) result.Add(slur);
        }
        AddLegacyHopoSlurs(beats, staffTop, result);
        return result;
    }

    /// <summary>
    /// The editor's H toggle only sets a generic "HOPO" bit (no origin/destination pair). Same rule as the tab arc: the first such note
    /// on a string starts a phrase that runs over the following notes on that string that carry the bit; one slur from start to end.
    /// </summary>
    private static void AddLegacyHopoSlurs(IReadOnlyList<StaffNotationBeat> beats, double staffTop, List<StaffNotationSlur> result)
    {
        static bool IsLegacy(TabNote n) => n.Techniques.Contains("HOPO") && !n.Techniques.Contains("HOPOOrigin") && !n.Techniques.Contains("HOPODestination");
        var ordered = beats.OrderBy(b => b.StartSlots).ToList();
        for (var i = 0; i < ordered.Count; i++)
        foreach (var note in ordered[i].Notes.Where(n => IsLegacy(n.Source)))
        {
            var stringIndex = note.Source.StringIndex;
            var startsPhrase = true;
            for (var j = i - 1; j >= 0 && startsPhrase; j--)
            {
                var earlier = ordered[j].Notes.FirstOrDefault(n => n.Source.StringIndex == stringIndex);
                if (earlier is null) continue;
                startsPhrase = !earlier.Source.Techniques.Contains("HOPO");
                break;
            }
            if (!startsPhrase) continue;
            (StaffNotationBeat Beat, StaffNotationNote Note)? last = null;
            for (var j = i + 1; j < ordered.Count; j++)
            {
                var next = ordered[j].Notes.FirstOrDefault(n => n.Source.StringIndex == stringIndex);
                if (next is null) continue;
                if (!next.Source.Techniques.Contains("HOPO")) break;
                last = (ordered[j], next);
            }
            if (last is { } end && MakeHopoSlur(ordered[i], note, end.Beat, end.Note, staffTop) is { } slur) result.Add(slur);
        }
    }

    private static StaffNotationSlur? MakeHopoSlur(StaffNotationBeat beat, StaffNotationNote note,
        StaffNotationBeat destinationBeat, StaffNotationNote destinationNote, double staffTop)
    {
        var destination = (Beat: destinationBeat, Note: destinationNote);
        {
            if (destination.Beat.CenterX - beat.CenterX < 18) return null;
            // A destination with an accidental: the slur ends at the accidental's left edge instead of running through it.
            var endX = destination.Note.Accidental is not null ? destination.Beat.Notes.Min(n => n.X) - HeadRadiusX - 13 - GhostRoom(staffTop, destination.Beat) - destination.Note.AccidentalColumn * 10 : destination.Note.X;
            if (endX - note.X < 18) endX = destination.Note.X;   // too tight to stop short of the accidental (the arc would turn back on itself): keep the old head-to-head arc
            // Ghost brackets and a chord-mate's accidental at the slur's height: start / end clear of them (the stop-short case above already is).
            var (startInset, endInset) = ArcInsets(beat, note, destination.Beat, destination.Note, staffTop);
            var stoppedShort = endX != destination.Note.X;
            // ArcInsets measured its room against the destination head; a slur that already stops short has less, so a ghost start
            // inset could carry the start past the end. Keep the plain start then.
            if (stoppedShort && endX - 5 - (note.X + startInset) < 12) startInset = 5;
            return new StaffNotationSlur(note.X, note.Y, endX, destination.Note.Y, beat.StemUp, startInset, stoppedShort ? 5 : endInset);
        }
    }

    private static void DrawStemsAndFlags(DrawingContext dc, StaffNotationMeasureLayout layout, Brush brush)
    {
        var stemPen = RenderDraw.Pen(brush, 0.9);
        foreach (var beat in layout.Beats)
        {
            if (!beat.HasStem) continue;
            dc.DrawLine(stemPen, new Point(beat.StemX, beat.StemStartY), new Point(beat.StemX, beat.StemEndY));
            if (beat.LowerStemTopY is { } lowerTop && beat.BeamGroupIndex < 0)
                dc.DrawLine(stemPen, new Point(beat.CenterX - 5, lowerTop), new Point(beat.CenterX - 5, beat.LowerStemEndY));
            if (beat.BeamGroupIndex < 0) DrawFlags(dc, beat, brush);
        }
    }

    private static void DrawFlags(DrawingContext dc, StaffNotationBeat beat, Brush brush)
    {
        for (var f = 0; f < beat.Flags; f++)
        {
            var y = beat.StemUp ? beat.StemEndY + f * BeamGap : beat.StemEndY - f * BeamGap;
            dc.DrawGeometry(brush, null, FlagGeometry(beat.StemX, y, beat.StemUp));
        }
    }

    private static void DrawBeams(DrawingContext dc, StaffNotationMeasureLayout layout, Brush brush)
    {
        var pen = RenderDraw.Pen(brush, BeamThickness);
        foreach (var beam in layout.BeamSegments)
            dc.DrawLine(pen, new Point(beam.X1, beam.Y1), new Point(beam.X2, beam.Y2));
    }

    private static void DrawTuplets(DrawingContext dc, StaffNotationMeasureLayout layout, Brush brush)
    {
        var sky = layout.Skyline;
        foreach (var tuplet in layout.TupletGroups)
        {
            var first = tuplet.Beats[0];
            var last = tuplet.Beats[^1];
            var up = first.StemUp;
            var numberText = tuplet.Numerator.ToString(CultureInfo.InvariantCulture);
            if (tuplet.IsBeamed)
            {
                var beam = layout.BeamGroups[tuplet.BeamGroupIndex];
                var edgeMin = double.PositiveInfinity;
                var edgeMax = double.NegativeInfinity;
                foreach (var beat in tuplet.Beats)
                {
                    var y = beam.BaseYAt(beat.StemX);
                    edgeMin = Math.Min(edgeMin, y);
                    edgeMax = Math.Max(edgeMax, y);
                }
                // The reference brackets beamed tuplets too: the number sits in a gap of the bracket line. The whole
                // bracket claims its box, stacked outside the beams, stems and heads of the beats it spans.
                var x0 = first.StemX - 2; var x1 = last.StemX + 2;
                var mid = (first.StemX + last.StemX) / 2;
                var tick = up ? 4.0 : -4.0;
                var blockTop = up ? sky.PlaceAbove(x0, x1, 15, edgeMin - 1) : sky.PlaceBelow(x0, x1, 15, edgeMax + 1);
                var numberY = up ? blockTop + 4.5 : blockTop + 10.5;
                var lineY = up ? blockTop + 10.5 : blockTop + 4.5;
                var bracketPen = RenderDraw.Pen(brush, 0.9);
                dc.DrawLine(bracketPen, new Point(x0, lineY), new Point(Math.Max(x0, mid - 8), lineY));
                dc.DrawLine(bracketPen, new Point(Math.Min(x1, mid + 8), lineY), new Point(x1, lineY));
                dc.DrawLine(bracketPen, new Point(x0, lineY), new Point(x0, lineY + tick));
                dc.DrawLine(bracketPen, new Point(x1, lineY), new Point(x1, lineY + tick));
                DrawCentered(dc, numberText, mid, numberY, 11, brush, FontWeights.SemiBold);
            }
            else
            {
                var top = tuplet.Beats.Min(b => b.MinY);
                var bottom = tuplet.Beats.Max(b => b.MaxY);
                var left = first.CenterX;
                var right = last.CenterX;
                if (right - left < 10) { left -= 5; right += 5; }
                var blockTop = up ? sky.PlaceAbove(left, right, 16, top - 1) : sky.PlaceBelow(left, right, 16, bottom + 1);
                var lineY = up ? blockTop + 11 : blockTop + 5;
                var tickY = up ? lineY + 5 : lineY - 5;
                var pen = RenderDraw.Pen(brush, 1);
                dc.DrawLine(pen, new Point(left, lineY), new Point(right, lineY));
                dc.DrawLine(pen, new Point(left, lineY), new Point(left, tickY));
                dc.DrawLine(pen, new Point(right, lineY), new Point(right, tickY));
                DrawCentered(dc, numberText, (left + right) / 2, up ? blockTop + 4.5 : blockTop + 11.5, 11, brush, FontWeights.SemiBold);
            }
        }
    }

    /// <summary>Rests are drawn as vector shapes (not font glyphs): full-size, black, the same on every machine.</summary>
    private static readonly TabCell WholeRestCell = new() { IsRest = true, DurationDenominator = 1 };

    /// <summary>The whole rest an empty bar shows (the reference engraves one in an empty first voice).</summary>
    internal static void DrawWholeBarRest(DrawingContext dc, double cx, double staffTop, Color ink) =>
        DrawRest(dc, WholeRestCell, cx, staffTop, RenderDraw.Solid(ink));

    private static void DrawRest(DrawingContext dc, TabCell cell, double cx, double staffTop, Brush brush)
    {
        var duration = NormalizeDuration(cell.DurationDenominator);
        var g = StaffGap;
        var restPen = RenderDraw.RoundPen(brush, 1.7);
        double dotY;
        if (duration <= 1)
        {
            dc.DrawRectangle(brush, null, new Rect(cx - 5.5, staffTop + g, 11, g * 0.5)); // hangs from the 4th line
            dotY = staffTop + g * 1.6;
        }
        else if (duration == 2)
        {
            dc.DrawRectangle(brush, null, new Rect(cx - 5.5, staffTop + 2 * g - g * 0.5, 11, g * 0.5)); // sits on the middle line
            dotY = staffTop + 2 * g - g * 0.9;
        }
        else if (duration == 4)
        {
            var y0 = staffTop + g * 1.0;
            var geometry = new StreamGeometry();
            using (var c = geometry.Open())
            {
                c.BeginFigure(new Point(cx - 2.2, y0), false, false);
                c.LineTo(new Point(cx + 2.6, y0 + 4.6), true, true);
                c.LineTo(new Point(cx - 2.6, y0 + 9.2), true, true);
                c.LineTo(new Point(cx + 2.4, y0 + 13.4), true, true);
                c.BezierTo(new Point(cx - 3.6, y0 + 13.0), new Point(cx - 4.2, y0 + 17.4), new Point(cx + 0.4, y0 + 18.0), true, true);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, restPen, geometry);
            dotY = y0 + 7;
        }
        else
        {
            // Eighth and shorter: a slanted stem with one flag dot per beam count.
            var flags = duration switch { 8 => 1, 16 => 2, 32 => 3, _ => 4 };
            var top = staffTop + g * 1.1;
            var bottom = top + g * (1.5 + 0.9 * (flags - 1));
            var stemTopX = cx + 2.8 + 0.6 * (flags - 1);
            var stemBottomX = cx - 2.2 - 0.6 * (flags - 1);
            dc.DrawLine(restPen, new Point(stemTopX, top), new Point(stemBottomX, bottom));
            for (var f = 0; f < flags; f++)
            {
                var fy = top + g * 0.8 * (f + 1) - 1;
                var fx = stemTopX - (stemTopX - stemBottomX) * (fy - top) / (bottom - top);
                dc.DrawEllipse(brush, null, new Point(fx - 2.3, fy), 1.9, 1.9);
                dc.DrawLine(RenderDraw.RoundPen(brush, 1.2), new Point(fx - 2.3, fy), new Point(fx + 1.6, fy - 2.2));
            }
            dotY = top + g * 0.6;
        }
        for (var d = 0; d < Math.Clamp(cell.Dots, 0, 2); d++)
            dc.DrawEllipse(brush, null, new Point(cx + 8 + d * 4, dotY), 1.4, 1.4);
    }

    internal static double RestCenterY(TabCell cell, double staffTop)
        => staffTop + (NormalizeDuration(cell.DurationDenominator) <= 1 ? 3 * StaffGap : 2 * StaffGap);

    private static void DrawDrumHeads(DrawingContext dc, StaffNotationBeat beat, int measureIndex,
        Color ink, Color playColor, Color paper, IReadOnlySet<(int bar, int cell, int s)> sounding,
        IReadOnlySet<(int bar, int cell, int s)> struck)
    {
        var isSounding = false;
        var isStruck = false;
        foreach (var note in beat.Cell.Notes)
        {
            var key = (measureIndex, beat.CellIndex, note.StringIndex);
            if (sounding.Contains(key)) isSounding = true;
            if (struck.Contains(key)) isStruck = true;
        }
        var play = playColor == default ? Color.FromRgb(0x3F, 0xB9, 0x50) : playColor;
        var pen = RenderDraw.Pen(isSounding ? play : ink, 1.4);
        var fill = RenderDraw.Solid(isSounding ? play : ink);
        foreach (var note in beat.Cell.Notes)
        {
            // Each sound at its own staff position from the track's drum map.
            var entry = beat.DrumMap?.Invoke(note.MidiValue > 0 ? note.MidiValue : note.Fret);
            var y = entry is null ? beat.MinY : beat.StaffTop + entry.StaffStep * StaffGap / 2;
            var x = beat.CenterX;
            if (isSounding)
                dc.DrawEllipse(RenderDraw.Solid(Color.FromArgb(isStruck ? (byte)85 : (byte)42, play.R, play.G, play.B)),
                    null, new Point(x, y), isStruck ? 10 : 8.5, isStruck ? 8 : 6.8);
            switch (entry?.Head ?? "x")
            {
                case "normal":
                    dc.DrawEllipse(fill, null, new Point(x, y), HeadRadiusX, HeadRadiusY);
                    break;
                case "diamond":
                    var d = new StreamGeometry();
                    using (var g = d.Open())
                    {
                        g.BeginFigure(new Point(x - 4.5, y), true, true);
                        g.LineTo(new Point(x, y - 4.5), true, false); g.LineTo(new Point(x + 4.5, y), true, false); g.LineTo(new Point(x, y + 4.5), true, false);
                    }
                    d.Freeze();
                    dc.DrawGeometry(RenderDraw.Solid(paper), pen, d);
                    break;
                default: // x, circle (open hi-hat = x in a circle)
                    // Masked locally so the staff line cannot split the X glyph.
                    dc.DrawEllipse(RenderDraw.Solid(paper), null, new Point(x, y), 5.4, 5.4);
                    dc.DrawLine(pen, new Point(x - 4, y - 4), new Point(x + 4, y + 4));
                    dc.DrawLine(pen, new Point(x - 4, y + 4), new Point(x + 4, y - 4));
                    if (entry?.Head == "circle") dc.DrawEllipse(null, RenderDraw.Pen(isSounding ? play : ink, 1), new Point(x, y), 6.2, 6.2);
                    break;
            }
        }
    }

    internal static IReadOnlyList<double> LedgerLinePositions(double y, double staffTop)
    {
        var staffBottom = staffTop + 4 * StaffGap;
        var lines = new List<double>();
        var nextY = staffTop - StaffGap;
        if (y < staffTop - StaffGap / 2)
        {
            while (nextY >= y - PositionEpsilon)
            {
                lines.Add(nextY);
                nextY -= StaffGap;
            }
        }
        else if (y > staffBottom + StaffGap / 2)
        {
            nextY = staffBottom + StaffGap;
            while (nextY <= y + PositionEpsilon)
            {
                lines.Add(nextY);
                nextY += StaffGap;
            }
        }
        return lines;
    }

    internal static IReadOnlyList<StaffLedgerLineSegment> LedgerLineSegments(
        StaffNotationMeasureLayout layout, LedgerLineMode mode)
    {
        if (mode == LedgerLineMode.Hidden) return Array.Empty<StaffLedgerLineSegment>();
        if (layout.TryGetLedgerSegments(mode, out var cached)) return cached;

        var halfWidth = mode == LedgerLineMode.Standard ? HeadRadiusX + LedgerOverhang : HeadRadiusX + 2.4;
        var onsets = new List<List<StaffNotationBeat>>();
        foreach (var beat in layout.Beats)
        {
            if (beat.IsDrum || beat.Notes.Count == 0) continue;
            if (onsets.Count == 0 || Math.Abs(onsets[^1][0].StartSlots - beat.StartSlots) > PositionEpsilon)
                onsets.Add(new List<StaffNotationBeat>());
            onsets[^1].Add(beat);
        }

        var result = new List<StaffLedgerLineSegment>();
        foreach (var onset in onsets)
        {
            var lineYs = new Dictionary<int, double>();
            var intervals = new Dictionary<int, List<(double Left, double Right)>>();
            foreach (var beat in onset)
            foreach (var note in beat.Notes)
            foreach (var ledgerY in LedgerLinePositions(note.Y, layout.StaffTop))
            {
                var key = (int)Math.Round(ledgerY * 100);
                lineYs[key] = ledgerY;
                if (!intervals.TryGetValue(key, out var lineIntervals))
                    intervals[key] = lineIntervals = new List<(double Left, double Right)>();
                lineIntervals.Add((note.X - halfWidth, note.X + halfWidth));
            }

            foreach (var (key, lineIntervals) in intervals)
            {
                var ordered = lineIntervals.OrderBy(interval => interval.Left).ToArray();
                if (ordered.Length == 0) continue;
                var left = ordered[0].Left;
                var right = ordered[0].Right;
                foreach (var interval in ordered.Skip(1))
                {
                    if (interval.Left <= right + 0.75)
                    {
                        right = Math.Max(right, interval.Right);
                        continue;
                    }
                    result.Add(new StaffLedgerLineSegment(left, right, lineYs[key]));
                    left = interval.Left;
                    right = interval.Right;
                }
                result.Add(new StaffLedgerLineSegment(left, right, lineYs[key]));
            }
        }
        var resultArray = result.ToArray();
        layout.SetLedgerSegments(mode, resultArray);
        return resultArray;
    }

    /// <summary>Thickness of the staff lines and of the ledger lines: they are one unit.</summary>
    internal const double StaffLineThickness = 1.0;

    /// <summary>The pen for staff lines AND ledger lines: the staff-line colour (which already carries the staff-line opacity), one thickness.</summary>
    internal static Pen StaffLinePen(Color staffLineColor) => RenderDraw.Pen(staffLineColor, StaffLineThickness);

    internal static Color EngravingInkColor(Color ink, Color paper)
    {
        const double soften = 0.14;
        static byte Mix(byte foreground, byte background, double amount)
            => (byte)Math.Round(foreground * (1 - amount) + background * amount);
        return Color.FromArgb(ink.A,
            Mix(ink.R, paper.R, soften), Mix(ink.G, paper.G, soften), Mix(ink.B, paper.B, soften));
    }

    private static void DrawLedgerLines(DrawingContext dc, StaffNotationMeasureLayout layout,
        Color staffLineColor, LedgerLineMode mode)
    {
        if (mode == LedgerLineMode.Hidden) return;
        // Exactly the staff lines' pen: ledger lines are an extension of the staff and always match it.
        var pen = StaffLinePen(staffLineColor);
        foreach (var line in LedgerLineSegments(layout, mode))
            dc.DrawLine(pen, new Point(line.X1, line.Y), new Point(line.X2, line.Y));
    }

    private static void DrawAugmentationDots(DrawingContext dc, TabCell cell, StaffNotationNote note, Brush brush)
    {
        var isLine = Math.Abs(note.StaffStep % 2) == 0;
        var dotY = isLine ? note.Y - StaffGap / 2 : note.Y;
        for (var d = 0; d < Math.Clamp(cell.Dots, 0, 2); d++)
            dc.DrawEllipse(brush, null, new Point(note.X + HeadRadiusX + 3 + d * 4, dotY), 1.4, 1.4);
    }

    private static void DrawTies(DrawingContext dc, IReadOnlyList<StaffNotationTie> ties, Brush brush, double contentLeft = double.NegativeInfinity)
    {
        foreach (var tie in ties)
        {
            // A stub reaching back over the key / time signature is left to the previous bar's outgoing stub.
            if (tie.IsStub && tie.TowardLeft && tie.X1 - 13 < contentLeft) continue;
            if (tie.IsStub) DrawTieStub(dc, tie.X1, tie.Y1, tie.TowardLeft, tie.Above, brush);
            else DrawTie(dc, tie.X1, tie.Y1, tie.X2, tie.Y2, tie.Above, brush, tie.StartInset, tie.EndInset);
        }
    }

    private static void DrawTie(DrawingContext dc, double x1, double y1, double x2, double y2, bool above, Brush brush, double startInset = 5, double endInset = 5)
    {
        var dir = above ? -1.0 : 1.0;
        var y1b = y1 + dir * 6;
        var y2b = y2 + dir * 6;
        var (sx, c1, c2, ex, span) = ArcShape(x1, x2, startInset, endInset);
        var bow = Math.Clamp(span * 0.10 + 4, 5, 14);
        var figure = new PathFigure { StartPoint = new Point(sx, y1b), IsClosed = false };
        figure.Segments.Add(new BezierSegment(
            new Point(c1, y1b + dir * bow),
            new Point(c2, y2b + dir * bow),
            new Point(ex, y2b), true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        dc.DrawGeometry(null, RenderDraw.Pen(brush, 1.2), geometry);
    }

    /// <summary>
    /// Horizontal shape of a tie / slur between heads at <paramref name="x1"/> and <paramref name="x2"/>: start, the two control points,
    /// end, and the span the bow height is taken from. The plain (5, 5) arc keeps its historic shape exactly; an inset arc places its
    /// control points inside its own (shorter) span, otherwise a control point past the end would make the arc hook back on itself.
    /// </summary>
    internal static (double Start, double Control1, double Control2, double End, double BowSpan) ArcShape(double x1, double x2, double startInset, double endInset)
    {
        var start = x1 + startInset;
        var end = x2 - endInset;
        if (startInset == 5 && endInset == 5) return (start, x1 + (x2 - x1) * 0.30, x1 + (x2 - x1) * 0.70, end, Math.Abs(x2 - x1));
        var drawn = end - start;
        return (start, start + drawn * 0.25, start + drawn * 0.75, end, Math.Abs(drawn) + 10);
    }

    private static void DrawTieStub(DrawingContext dc, double x, double y, bool towardLeft, bool above, Brush brush)
    {
        var dir = above ? -1.0 : 1.0;
        var sign = towardLeft ? -1.0 : 1.0;
        var yb = y + dir * 6;
        var figure = new PathFigure { StartPoint = new Point(x + sign * 5, yb), IsClosed = false };
        figure.Segments.Add(new BezierSegment(
            new Point(x + sign * 11, yb + dir * 6),
            new Point(x + sign * 13, yb + dir * 10),
            new Point(x + sign * 11, yb + dir * 12), true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        dc.DrawGeometry(null, RenderDraw.Pen(brush, 1.2), geometry);
    }

    /// <summary>
    /// The reference's slide strokes in the staff: a short slanted line between the two heads of a shift / legato slide, a slash leading
    /// into a head (slide in from below / above) and a slash trailing from it (slide out up / down). Clear of the chord's accidentals and
    /// ghost bracket on the left, and of the ghost bracket and augmentation dots on the right.
    /// </summary>
    internal static List<StaffNotationSlideStroke> BuildSlideStrokes(TrackModel? track, int measureIndex, int voiceIndex,
        IReadOnlyList<StaffNotationBeat> beats, double staffTop)
    {
        var result = new List<StaffNotationSlideStroke>();
        if (track is null) return result;
        double Left(StaffNotationBeat beat) => beat.Notes.Min(n => n.X) - HeadRadiusX - 2 - GhostRoom(staffTop, beat)
            - (beat.Notes.Any(n => n.Accidental is not null) ? 11 + beat.Notes.Where(n => n.Accidental is not null).Max(n => n.AccidentalColumn) * 10 : 0);
        double Right(StaffNotationBeat beat, StaffNotationNote note) => note.X + HeadRadiusX + 2 + beat.Cell.Dots * 4 +
            (note.Source.Ghost ? 8 + (OnLedgerAt(staffTop, note) ? GhostLedgerPad : 0) : 0);
        foreach (var mark in TabSlideNotation.ForMeasure(track, measureIndex, voiceIndex))
        {
            var beat = beats.FirstOrDefault(b => b.CellIndex == mark.SourceCellIndex);
            var note = beat?.Notes.FirstOrDefault(n => ReferenceEquals(n.Source, mark.Source));
            if (beat is null || note is null) continue;
            switch (mark.Kind)
            {
                case TabSlideMarkKind.IncomingFromBelow:
                case TabSlideMarkKind.IncomingFromAbove:
                {
                    var below = mark.Kind == TabSlideMarkKind.IncomingFromBelow;
                    var x2 = Left(beat);
                    // Only as long as the room after the previous beat's ink (its head, ghost bracket, dots and stem) allows.
                    var previous = beats.Where(b => b.StartSlots < beat.StartSlots - PositionEpsilon && b.Notes.Count > 0).OrderByDescending(b => b.StartSlots).FirstOrDefault();
                    var length = Math.Min(7, x2 - (previous is null ? double.NegativeInfinity : previous.Notes.Max(n => Right(previous, n)) + 4));
                    if (length < 3.5) break;
                    result.Add(new StaffNotationSlideStroke(x2 - length, note.Y + (below ? 4.5 : -4.5) * length / 7, x2, note.Y + (below ? -0.5 : 0.5)));
                    break;
                }
                case TabSlideMarkKind.Connection when mark.TargetMeasureIndex == measureIndex &&
                    beats.FirstOrDefault(b => b.CellIndex == mark.TargetCellIndex) is { } destBeat &&
                    destBeat.Notes.FirstOrDefault(n => ReferenceEquals(n.Source, mark.Target)) is { } destNote &&
                    !beats.Any(b => b.Notes.Count > 0 && b.StartSlots > beat.StartSlots + PositionEpsilon && b.StartSlots < destBeat.StartSlots - PositionEpsilon) &&   // nothing between: a stroke never crosses another beat
                    Left(destBeat) - Right(beat, note) >= 8:
                    result.Add(new StaffNotationSlideStroke(Right(beat, note), note.Y, Left(destBeat), destNote.Y));
                    break;
                case TabSlideMarkKind.Connection:
                case TabSlideMarkKind.OutgoingUp:
                case TabSlideMarkKind.OutgoingDown:
                {
                    // No head to run to in this bar (or no room): a short trailing stroke in the slide's direction.
                    var up = mark.Kind == TabSlideMarkKind.OutgoingUp ||
                             mark.Kind == TabSlideMarkKind.Connection && (mark.Target is null || mark.Target.Fret >= mark.Source.Fret);   // same string: the fret gives the direction (as in the tab)
                    var x1 = Right(beat, note);
                    var next = beats.Where(b => b.StartSlots > beat.StartSlots + PositionEpsilon && b.Notes.Count > 0).OrderBy(b => b.StartSlots).FirstOrDefault();
                    var length = Math.Min(7, (next is null ? double.PositiveInfinity : Left(next) - 1.5 - x1));
                    if (length < 3.5) break;
                    result.Add(new StaffNotationSlideStroke(x1, note.Y + (up ? 0.5 : -0.5), x1 + length, note.Y + (up ? -4.5 : 4.5) * length / 7));
                    break;
                }
            }
        }
        return result;
    }

    private static void DrawSlideStrokes(DrawingContext dc, IReadOnlyList<StaffNotationSlideStroke> slides, Brush brush)
    {
        if (slides.Count == 0) return;
        var pen = RenderDraw.Pen(brush, 1.1);
        foreach (var slide in slides) dc.DrawLine(pen, new Point(slide.X1, slide.Y1), new Point(slide.X2, slide.Y2));
    }

    private static void DrawHopoSlurs(DrawingContext dc, IReadOnlyList<StaffNotationSlur> slurs, Brush brush)
    {
        var pen = RenderDraw.Pen(brush, 1.1);
        foreach (var slur in slurs)
        {
            var direction = slur.StemsUp ? 1.0 : -1.0;
            var (sx, c1, c2, ex, span) = ArcShape(slur.X1, slur.X2, slur.StartInset, slur.EndInset);
            var start = new Point(sx, slur.Y1 + direction * 6);
            var end = new Point(ex, slur.Y2 + direction * 6);
            var bow = Math.Clamp(span * 0.10 + 4, 5, 12);
            var figure = new PathFigure { StartPoint = start, IsClosed = false };
            figure.Segments.Add(new BezierSegment(
                new Point(c1, start.Y + direction * bow),
                new Point(c2, end.Y + direction * bow), end, true));
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            dc.DrawGeometry(null, pen, geometry);
        }
    }

    internal static Geometry FlagGeometry(double x, double y, bool up)
    {
        var direction = up ? 1.0 : -1.0;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            // A full flag: it leaves the stem tip with some body (2.6 px thick, under the 3.4 px gap between stacked flags), sweeps right
            // and curls down to a point 15 px below, like the reference's eighth, sixteenth and thirty-second flags.
            context.BeginFigure(new Point(x, y), true, true);
            context.BezierTo(new Point(x + 2, y + 3.8 * direction), new Point(x + 6, y + 6 * direction),
                new Point(x + 3.2, y + 13 * direction), true, false);
            context.BezierTo(new Point(x + 3.5, y + 8.5 * direction), new Point(x + 2.2, y + 6.5 * direction),
                new Point(x, y + 2.8 * direction), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static int FlagsFor(int denominator) => NormalizeDuration(denominator) switch
    {
        <= 4 => 0,
        8 => 1,
        16 => 2,
        32 => 3,
        _ => 4
    };

    private static int[] KeyAlterations(int keySignature)
    {
        var alterations = new int[7];
        var count = Math.Clamp(Math.Abs(keySignature), 0, 7);
        var order = keySignature >= 0 ? SharpOrder : FlatOrder;
        var value = keySignature >= 0 ? 1 : -1;
        for (var i = 0; i < count; i++) alterations[order[i]] = value;
        return alterations;
    }

    private static PitchSpelling SpellPitch(int midi, IReadOnlyList<int> keyAlterations, int keySignature)
    {
        var midiOctave = midi / 12 - 1;
        PitchSpelling? best = null;
        var bestScore = double.MaxValue;
        for (var octave = midiOctave - 1; octave <= midiOctave + 1; octave++)
        for (var letter = 0; letter < 7; letter++)
        for (var alteration = -1; alteration <= 1; alteration++)
        {
            var naturalMidi = 12 * (octave + 1) + NaturalPitchClasses[letter];
            if (naturalMidi + alteration != midi) continue;
            var expected = keyAlterations[letter];
            var score = alteration == expected ? 0.0 : 10 + Math.Abs(alteration - expected);
            score += Math.Abs(alteration) * 0.1;
            if (keySignature > 0 && alteration < 0 || keySignature < 0 && alteration > 0) score += 0.25;
            if (keySignature == 0 && alteration < 0) score += 0.2; // neutral keys default to sharp spelling
            if (score < bestScore)
            {
                bestScore = score;
                best = new PitchSpelling(letter, octave, alteration, octave * 7 + letter);
            }
        }
        return best ?? new PitchSpelling(0, midiOctave, 0, midiOctave * 7);
    }

    private static void DrawCentered(DrawingContext dc, string text, double cx, double cy, double size, Brush brush,
        FontWeight? weight = null)
    {
        var formatted = MakeText(text, size, brush, weight);
        TabForge.Visualization.Draw.DrawText(dc, formatted, new Point(cx - formatted.Width / 2, cy - formatted.Height / 2));
    }

    private static void Draw(DrawingContext dc, string text, double x, double y, double size, Brush brush,
        FontWeight? weight = null) => TabForge.Visualization.Draw.DrawText(dc, MakeText(text, size, brush, weight), new Point(x, y));

    private static FormattedText MakeText(string text, double size, Brush brush, FontWeight? weight) =>
        TabEditorControl.CachedText(text, size, brush, weight, "Segoe UI Symbol");

    private readonly record struct MetricGroup(double Start, double End, double Width);
    private readonly record struct PitchSpelling(int Letter, int Octave, int Alteration, int DiatonicIndex);

    private readonly record struct ClefInfo(int BottomDiatonicIndex, int OctaveShift)
    {
        public static ClefInfo From(string? clef)
        {
            var value = (clef ?? "").Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
            var bottom = 4 * 7 + 2; // default treble: E4 on the bottom line
            if (value.Contains("bass", StringComparison.Ordinal)) bottom = 2 * 7 + 4;
            else if (value.Contains("alto", StringComparison.Ordinal)) bottom = 3 * 7 + 3;
            else if (value.Contains("tenor", StringComparison.Ordinal)) bottom = 3 * 7 + 1;
            else if (value.Length > 0 && value[0] is 'g' or 'f' or 'c')
            {
                var clefLine = value.Length > 1 && value[1] is >= '1' and <= '5'
                    ? value[1] - '0'
                    : 2; // G8 is the standard octave treble clef, not a line-eight clef.
                var reference = value[0] switch
                {
                    'g' => 4 * 7 + 4, // G4
                    'f' => 3 * 7 + 3, // F3
                    _ => 4 * 7         // C4
                };
                bottom = reference - 2 * (clefLine - 1);
            }
            var shift = value.Contains("8vb", StringComparison.Ordinal) ? 12
                : value.Contains("8va", StringComparison.Ordinal) ? -12
                : 0;
            return new ClefInfo(bottom, shift);
        }
    }
}
