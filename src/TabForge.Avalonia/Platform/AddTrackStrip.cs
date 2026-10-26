using System;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the drawing of the "Add track" strip under the track rows: one band across the full pane width, the card fill, the plus glyph and
// label centred in the track list's width, and the hover glow.
// Does not own: the strip's size and words (AddTrackLane in Core), the row geometry (TimelineView), the hover and click (AddTrackBar), or the
//   Add track window, which the preview does not have yet.
// Tests: TabForge.Avalonia.Tests.AddTrackStripTests, TabForge.Avalonia.Tests.TrackListOwnerLayoutTests.
public static class AddTrackStrip
{
    private const string Font = "Segoe UI";
    private const double LabelFontSize = 12;
    private const double GlowOpacity = 0.10;
    /// <summary>A dragged audio file over the strip: an accent wash with a border and the drop words (the add-track drag shade).</summary>
    private const double DropFillOpacity = 0.14;
    private const double DropBorder = 2;
    /// <summary>The plus glyph: arm half-length and stroke, and the gap between it and the label.</summary>
    private const double PlusArm = 4.5;
    private const double PlusStroke = 1.4;
    private const double PlusGap = 8;
    private const double PlusWidth = PlusArm * 2;
    /// <summary>The palette key of the strip's fill: the theme strip brush, which has a light value (the card key holds only its dark one).</summary>
    public const string FillKey = "ChromeStrip";

    /// <summary>
    /// Draws the strip at <paramref name="top"/> across <paramref name="width"/>, with the label centred in the first <paramref name="labelAreaWidth"/>
    /// (the track list's width); <paramref name="hot"/> lights the hover glow and <paramref name="drop"/> shows the drop shade and drop words.
    /// </summary>
    public static void Draw(IDrawContext d, Func<string, UiColor> ink, double top, double width, double labelAreaWidth, bool hot, bool drop = false)
    {
        var height = AddTrackLane.Height;
        var accent = ink("Accent");
        d.Rect(ink(FillKey), null, 0, top, width, height);
        if (hot) d.Rect(accent.WithOpacity(GlowOpacity), null, 0, top, width, height);
        if (drop)
        {
            d.Rect(accent.WithOpacity(DropFillOpacity), null, 0, top, width, height);
            var border = new UiStroke(accent, DropBorder);
            d.Line(border, 0, top + DropBorder / 2, width, top + DropBorder / 2);                 // top
            d.Line(border, 0, top + height - DropBorder / 2, width, top + height - DropBorder / 2); // bottom
            d.Line(border, width - DropBorder / 2, top, width - DropBorder / 2, top + height);     // right (the left edge meets the track list)
        }
        var label = d.Text(drop ? AddTrackLane.DropLabel : AddTrackLane.Label, Font, LabelFontSize, ink("TextLegible"), d.PixelsPerDip, weight: 600);
        var groupWidth = PlusWidth + PlusGap + label.Width;
        var left = (labelAreaWidth - groupWidth) / 2;
        var centreY = top + height / 2;
        var plusX = left + PlusArm;
        var stroke = new UiStroke(ink("TextLegible"), PlusStroke);
        d.Line(stroke, plusX, centreY - PlusArm, plusX, centreY + PlusArm);
        d.Line(stroke, left, centreY, left + PlusWidth, centreY);
        d.DrawText(label, left + PlusWidth + PlusGap, centreY - label.Height / 2);
    }
}
