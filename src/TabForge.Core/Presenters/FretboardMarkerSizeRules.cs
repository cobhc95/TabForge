using System;

namespace TabForge.Presenters;

// Owns: the effective size factor of fretboard note markers for the "Note marker size" setting: the radius never exceeds half the
//   gap between adjacent strings minus 0.5, so neighbouring markers never overlap.
// Does not own: drawing or the setting store.
// Tests: TabForge.Avalonia.Tests.FretboardPlaybackMarkerTests.
public static class FretboardMarkerSizeRules
{
    public const int MinPercent = 60;
    public const int MaxPercent = 160;
    public const int StepPercent = 10;

    /// <summary>The marker size setting as a scale: the percent snapped to 10% steps inside the allowed range, divided by 100.</summary>
    public static double Scale(int percent) =>
        Math.Clamp((int)(Math.Round(percent / (double)StepPercent) * StepPercent), MinPercent, MaxPercent) / 100.0;

    /// <summary>Factor applied to a marker's radius and its number: at most <paramref name="scale"/>, and never so large the marker overlaps its neighbours.</summary>
    public static double Factor(double scale, double baseRadius, double stringGap)
    {
        if (baseRadius <= 0) return scale;
        var fit = (stringGap / 2 - 0.5) / baseRadius;
        return Math.Min(scale, Math.Max(0, fit));
    }

    /// <summary>The share of the string gap a scale-highlight mark's radius takes: the renderer's 0.3 of the smaller of the string gap and the
    /// fret width. The panel sizes the mark from the string gap alone and caps it at 11 DIPs (see InstrumentPanelView.Fretboard).</summary>
    public const double MaxRadiusShareOfGap = 0.3;

    /// <summary>The label font size of a marker of <paramref name="radius"/>: the radius itself. A Segoe UI digit is about 1.35 em tall and half an
    /// em wide, so a two-digit number at that size spans about two thirds of the dot's diameter and fits inside it (the two-digit check in
    /// TabForge.Avalonia.Tests.InstrumentFretNumberTests), while a marker shrunk by the note-marker-size setting shrinks its number with it. No role's
    /// number reaches the cap at the default marker size (13 at the sounding marker's radius of 16, 10 to 12 for the other roles), so the sizes
    /// stand there and only a grown or a shrunken marker is limited by its dot.</summary>
    public static double LabelFontSize(double radius) => radius;
}
