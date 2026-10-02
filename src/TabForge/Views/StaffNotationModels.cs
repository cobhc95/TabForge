using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Services;
using TabForge.Visualization;
using RenderDraw = TabForge.Visualization.Draw;
using TabForge.Views.Score;

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
