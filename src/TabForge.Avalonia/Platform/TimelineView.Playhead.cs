using TabForge.Avalonia.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The timeline's playhead state: the played bar and the fraction through it, its x, and the section-bracket option that marks the section
/// holding it. A frame that moves the playhead less than a pixel draws nothing; the body redraws only when the bar changes.
/// Owns: the playhead position and its x. Does not own: the playhead's moving layers (TimelinePlayheadLayer), the drawing of the brackets
/// (TimelineView.Drawing) or the clock that moves it (the arrangement's playhead subscription).
/// Tests: TabForge.Avalonia.Tests.TimelineViewTests.
/// </summary>
public sealed partial class TimelineView
{
    /// <summary>The bar the playhead sits in; -1 hides it.</summary>
    public int PlayheadBar { get; private set; }

    /// <summary>How far through <see cref="PlayheadBar"/> the playhead is, 0 to 1 (the line sits at that point of the bar).</summary>
    public double PlayheadFraction { get; private set; }

    /// <summary>Which play (1-based pass) of <see cref="PlayheadBar"/> the playhead is on; 1 outside repeats.</summary>
    public int PlayheadPass { get; private set; } = 1;

    /// <summary>
    /// Moves the playhead to <paramref name="bar"/> and <paramref name="fraction"/>. The timeline repaints only when the bar changes or the
    /// playhead's whole-DIP x changes, so a frame that moves it less than a pixel draws nothing.
    /// </summary>
    public void SetPlayhead(int bar, double fraction, int pass = 1)
    {
        // The brackets, the section glow and the bar-style marker live in the body and depend on the played bar, so the body draws again when the bar changes (never per frame).
        var barChanged = bar != PlayheadBar;
        var passChanged = Math.Max(1, pass) != PlayheadPass;
        PlayheadBar = bar;
        PlayheadFraction = fraction;
        PlayheadPass = Math.Max(1, pass);
        if (barChanged) InvalidateVisual();
        if (passChanged) PlayheadLayer.Redraw();
        PlayheadLayer.Place();
    }

    /// <summary>The playhead's x in DIPs, or NaN while it is hidden.</summary>
    internal double PlayheadX()
    {
        if (PlayheadBar < 0 || PlayheadBar >= BarCount) return double.NaN;
        var barLeft = BarX(PlayheadBar);
        return barLeft + (_boundaries[PlayheadBar + 1] - barLeft) * Math.Clamp(PlayheadFraction, 0, 1);
    }

    /// <summary>Whether the [ ] brackets mark the section that holds the playhead (the WPF "section brackets" option, on by default).</summary>
    public bool ShowSectionBrackets { get => _showSectionBrackets; set => Redraw(ref _showSectionBrackets, value); }
    private bool _showSectionBrackets = true;
}
