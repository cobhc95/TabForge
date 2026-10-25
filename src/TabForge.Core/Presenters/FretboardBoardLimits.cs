namespace TabForge.Presenters;

// Owns: the caps that shape the drawn fretboard board: the widest board in virtual pixels, the largest string gap, the widest
//   aspect (board width : board height), whether the board keeps clear of the legend column, and whether the board keeps its real
//   proportions (FretboardSizeRules.RealAspect) at every size. Default holds the realistic caps the layout rules use when a caller passes
//   none; WpfPanel holds the WPF panel's own limits (no gap or aspect cap); an app passes its own values to draw a board that fills a larger pane.
// Does not own: the layout itself (FretboardGeometryRules.Compute applies these), the pane's height (the dock), the drawing.
// Tests: TabForge.Avalonia.Tests.FretboardSizeTests (Default), TabForge.Avalonia.Tests.FretboardRealProportionTests (RealProportions),
//   TestFretboardGeometry and TestInstrumentGeometryRules (WpfPanel).
/// <summary>The caps of one board layout. Every layout rule takes an optional value of this type and falls back to <see cref="Default"/>.</summary>
/// <param name="ClearsLegend">True when the board's width is taken off the content width by the legend column, so the board never runs under it.</param>
/// <param name="RealProportions">True: the board keeps the real aspect of its shown frets and strings, scaled to fit the pane (the aspect and gap caps do not apply).</param>
public readonly record struct FretboardBoardLimits(double MaxBoardWidth, double MaxStringGap, double AspectMax, bool ClearsLegend, bool RealProportions = false)
{
    /// <summary>The realistic caps: a board at most 1180 virtual pixels wide, a string gap at most 34, an aspect of at most 14:1, and a board that clears the legend column.</summary>
    public static FretboardBoardLimits Default => new(1180, FretboardSizeRules.StringGapMaxPx, FretboardSizeRules.AspectMax, ClearsLegend: true);

    /// <summary>The WPF panel's caps: a board at most 1180 virtual pixels wide, the whole content width, and no string-gap or aspect cap
    /// beyond the panel's own rules. An infinite <see cref="AspectMax"/> turns the aspect clamp off.</summary>
    public static FretboardBoardLimits WpfPanel => new(1180, double.PositiveInfinity, double.PositiveInfinity, ClearsLegend: false);
}
