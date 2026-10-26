using System;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Features.Recording.View;

/// <summary>
/// The armed strip of an armed track: the row the track list shows with the red "● Audio (armed)" or "● MIDI (armed)" label, the input level meter,
/// and the space the monitor toggle and the input picker take (their controls are placed by <see cref="ArmedRowLayer"/> at the positions given
/// here). It draws under the track's base band when the track has no lanes yet, and inside the last lane band once the track has lanes, so a lane
/// band is never left blank. The timeline lane beside it shows the "Armed: press Record…" hint.
/// Owns: the strip's height and every position in it, its drawing and its texts.
/// Does not own: the row geometry (TimelineView), the controls and their edits (ArmedRowLayer), the levels (TrackHeaderColumn holds them), the arm itself.
/// Tests: TabForge.Avalonia.Tests.ArmedRowTests.
/// </summary>
public static class ArmedRowPainter
{
    /// <summary>The strip's height: the armed clip-lane strip (about 40 DIPs), the sub-row under a track with no lanes, or the last lane band's lower part when it has lanes.</summary>
    public const double SubRowHeight = 40;

    /// <summary>
    /// The strip's top: inside the last lane band (bottom-aligned) once the track has lanes, else the base band's bottom (the sub-row).
    /// <paramref name="bandBottom"/> is the bottom of the track's base band (its notation band plus its lanes).
    /// </summary>
    public static double StripTopOf(TrackModel track, double bandBottom) =>
        ClipLanes.LaidOutCount(track) > 0 ? bandBottom - SubRowHeight : bandBottom;

    /// <summary>The label's left edge in the track list, and the space each control leaves after the one before it.</summary>
    public const double LabelLeft = TrackRowCells.GridLeft + 8, LabelWidth = 92, ControlGap = 6;

    /// <summary>The monitor toggle's size.</summary>
    public const double MonitorWidth = 26, MonitorHeight = 20;

    /// <summary>The input picker's size.</summary>
    public const double InputWidth = 136, InputHeight = 22;

    /// <summary>The input level meter's gap after the picker, and its size.</summary>
    public const double MeterGap = 8, MeterWidth = 110, MeterHeight = 8;

    /// <summary>The label and the timeline hint's text size.</summary>
    public const double TextSize = 11;

    /// <summary>The timeline hint's left edge in the lane.</summary>
    public const double HintLeft = 8;

    private const string Font = "Segoe UI";

    /// <summary>The monitor toggle's left edge in the track list.</summary>
    public static double MonitorLeft => LabelLeft + LabelWidth + ControlGap;

    /// <summary>The input picker's left edge in the track list.</summary>
    public static double InputLeft => MonitorLeft + MonitorWidth + ControlGap;

    /// <summary>The input level meter's left edge in the track list.</summary>
    public static double MeterLeft => InputLeft + InputWidth + MeterGap;

    /// <summary>The sub-row's label: "● MIDI (armed)" for a MIDI input, else "● Audio (armed)".</summary>
    public static string LabelOf(TrackModel track) => AudioInputs.IsMidi(track.AudioInput) ? "● MIDI (armed)" : "● Audio (armed)";

    /// <summary>The hint the lane shows while an armed track has no clips.</summary>
    public static string LaneHintOf(TrackModel track) =>
        AudioInputs.IsMidi(track.AudioInput) ? "Armed (MIDI): press Record to record here" : "Armed: press Record to record here, or drop audio files";

    /// <summary>Draws the label and the level meter in the strip whose top is <see cref="StripTopOf"/> for <paramref name="bandBottom"/>; <paramref name="level"/> is the input peak, 0..1.</summary>
    public static void Draw(IDrawContext d, Func<string, UiColor> ink, TrackModel track, double bandBottom, double level)
    {
        var top = StripTopOf(track, bandBottom);
        var label = d.Text(LabelOf(track), Font, TextSize, ink("Danger"), d.PixelsPerDip, weight: 600);
        d.DrawText(label, LabelLeft, top + (SubRowHeight - label.Height) / 2);
        var meterTop = top + (SubRowHeight - MeterHeight) / 2;
        d.Rect(ink("TrackSliderTrack"), new UiStroke(ink("TrackSliderTrackBorder"), 1), MeterLeft, meterTop, MeterWidth, MeterHeight, MeterHeight / 2);
        var fill = Math.Clamp(level, 0, 1) * MeterWidth;
        if (fill > 0) d.Rect(ink("TrackAudible"), null, MeterLeft, meterTop, fill, MeterHeight, MeterHeight / 2);
    }

    /// <summary>Draws the timeline hint in the lane's sub-row whose top is <paramref name="top"/>.</summary>
    public static void DrawLaneHint(IDrawContext d, Func<string, UiColor> ink, TrackModel track, double top)
    {
        var hint = d.Text(LaneHintOf(track), Font, TextSize, ink("TextMuted"), d.PixelsPerDip);
        d.DrawText(hint, HintLeft, top + (SubRowHeight - hint.Height) / 2);
    }
}
