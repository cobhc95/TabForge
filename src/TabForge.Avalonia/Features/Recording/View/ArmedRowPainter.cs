using System;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Features.Recording.View;

/// <summary>
/// The armed strip of an armed track: the row the track list shows with the red "● Audio (armed)" or "● MIDI (armed)" label, the input level meter,
/// and the space the joined monitor-and-input pill takes (its two halves are placed by <see cref="ArmedRowLayer"/> at the positions given
/// here). It always sits directly under the track's main line (<see cref="TimelineView.ArmedStripTopOf"/>), at the same offset in every track state.
/// The timeline lane beside it shows the "Armed: press Record…" hint.
/// Owns: the strip's height and every position in it, its drawing and its texts.
/// Does not own: the row geometry (TimelineView), the controls and their edits (ArmedRowLayer), the levels (TrackHeaderColumn holds them), the arm itself.
/// Tests: TabForge.Avalonia.Tests.ArmedRowTests.
/// </summary>
public static class ArmedRowPainter
{
    /// <summary>The strip's height (about 40 DIPs): the sub-row directly under the track's main line.</summary>
    public const double SubRowHeight = 40;

    /// <summary>The label's left edge in the track list, its width, and the gap before the pill.</summary>
    public const double LabelLeft = TrackRowCells.GridLeft + 8, LabelWidth = 92, ControlGap = 6;

    /// <summary>The widths of the pill's two halves: the monitor toggle's, and the input selector's (the halves share their 1 DIP divider).</summary>
    public const double MonitorWidth = 30, InputWidth = 128;

    /// <summary>The pill's height in Comfortable (the base track row height); it scales with the row height, so Compact and Extra compact shrink it.</summary>
    public const double PillDips = 24, PillLeastDips = 18, PillMostDips = 26;

    /// <summary>The pill's height for a track row height: <see cref="PillDips"/> at the default row height, scaled with it and kept in a sensible range.</summary>
    public static double PillHeightOf(double rowHeight) =>
        Math.Round(Math.Clamp(PillDips * rowHeight / TimelineView.DefaultRowHeight, PillLeastDips, PillMostDips));

    /// <summary>The input level meter's gap after the pill, and its size.</summary>
    public const double MeterGap = 8, MeterWidth = 110, MeterHeight = 8;

    /// <summary>The label and the timeline hint's text size.</summary>
    public const double TextSize = 11;

    /// <summary>The timeline hint's left edge in the lane.</summary>
    public const double HintLeft = 8;

    private const string Font = "Segoe UI";

    /// <summary>The monitor half's left edge in the track list.</summary>
    public static double MonitorLeft => LabelLeft + LabelWidth + ControlGap;

    /// <summary>The input selector half's left edge in the track list: it overlaps the monitor half by the 1 DIP divider.</summary>
    public static double InputLeft => MonitorLeft + MonitorWidth - 1;

    /// <summary>The input level meter's left edge in the track list.</summary>
    public static double MeterLeft => InputLeft + InputWidth + MeterGap;

    /// <summary>The sub-row's label: "● MIDI (armed)" for a MIDI input, else "● Audio (armed)".</summary>
    public static string LabelOf(TrackModel track) => AudioInputs.IsMidiOnly(track) ? "● MIDI (armed)" : "● Audio (armed)";

    /// <summary>The hint the lane shows while an armed track has no clips.</summary>
    public static string LaneHintOf(TrackModel track) =>
        AudioInputs.IsMidiOnly(track) ? "Armed (MIDI): press Record to record here" : "Armed: press Record to record here, or drop audio files";

    /// <summary>The width of the peak-hold tick of a MIDI input's meter.</summary>
    public const double HoldTickWidth = 2;

    /// <summary>Draws the label and the level meter in the strip whose top is <paramref name="top"/>; <paramref name="level"/> is the input peak, 0..1, and <paramref name="hold"/> the peak-hold tick of a MIDI input (0: none).</summary>
    public static void Draw(IDrawContext d, Func<string, UiColor> ink, TrackModel track, double top, double level, double hold = 0)
    {
        var label = d.Text(LabelOf(track), Font, TextSize, ink("Danger"), d.PixelsPerDip, weight: 600);
        d.DrawText(label, LabelLeft, top + (SubRowHeight - label.Height) / 2);
        var meterTop = top + (SubRowHeight - MeterHeight) / 2;
        d.Rect(ink("TrackSliderTrack"), new UiStroke(ink("TrackSliderTrackBorder"), 1), MeterLeft, meterTop, MeterWidth, MeterHeight, MeterHeight / 2);
        var fill = Math.Clamp(level, 0, 1) * MeterWidth;
        if (fill > 0) d.Rect(ink("TrackAudible"), null, MeterLeft, meterTop, fill, MeterHeight, MeterHeight / 2);
        var tick = Math.Clamp(hold, 0, 1) * MeterWidth;
        if (tick > fill + HoldTickWidth) d.Rect(ink("TrackAudible"), null, MeterLeft + tick - HoldTickWidth, meterTop, HoldTickWidth, MeterHeight, 0);
    }

    /// <summary>Draws the timeline hint in the lane's sub-row whose top is <paramref name="top"/>.</summary>
    public static void DrawLaneHint(IDrawContext d, Func<string, UiColor> ink, TrackModel track, double top)
    {
        var hint = d.Text(LaneHintOf(track), Font, TextSize, ink("TextMuted"), d.PixelsPerDip);
        d.DrawText(hint, HintLeft, top + (SubRowHeight - hint.Height) / 2);
    }
}
