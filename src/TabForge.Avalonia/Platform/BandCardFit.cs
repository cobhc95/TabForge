using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Services;
using TabForge.Views.Score;

namespace TabForge.Avalonia.Platform;

// Owns: how the Band view's cards share the view's height: the automatic size of the fretboards (as small as the staff needs to reach a readable zoom, never
//   below the board's smallest size), the staff zoom (the zoom at which every shown card fits, times the band lane zoom), how many
//   lines the first card shows (two only while they fit), with the leftover height (a few pixels) left under the last card, never inside a card above a tab line.
// Does not own: the card's parts (BandTrackPanel), the lane zoom value (BandSettings.LaneZoom), the scroll view (BandView).
// Tests: TabForge.Avalonia.Tests.BandCardFitTests.
internal static class BandCardFit
{
    /// <summary>Height kept free for the cards' rounding to whole pixels, so the cards never overflow the view by a pixel or two.</summary>
    private const double FitSlack = 8;

    /// <summary>The staff zoom the automatic fretboard size aims for (the main score's own is 1): the fretboards shrink until the staff reaches it.</summary>
    public const double TargetStaffZoom = 0.5;

    /// <summary>The smallest staff zoom of a card, below the main score's, so five cards fit at a low lane zoom.</summary>
    public const double MinStaffZoom = 0.25;

    /// <summary>Sets the zoom, the first card's lines and the cards' extra height for a view <paramref name="height"/> tall.</summary>
    public static void Apply(IReadOnlyList<BandTrackPanel> cards, double height, double gap, double laneZoom)
    {
        if (cards.Count == 0 || height <= 1) return;
        if (cards.Max(card => card.StaffLineUnits()) <= 0) return;
        SizeAutoFretboards(cards, height, gap);
        var fixedHeight = cards.Sum(card => card.NaturalHeight(0)) + (cards.Count - 1) * gap;
        var fitZoom = Math.Min(1, (height - fixedHeight - FitSlack) / StaffUnits(cards));
        var zoom = Math.Clamp(fitZoom * laneZoom, MinStaffZoom, ScoreZoomRules.MaxFactor);
        foreach (var card in cards)
        {
            card.Staff.MinZoom = MinStaffZoom;
            if (Math.Abs(card.Staff.Zoom - zoom) > 1e-6) card.Staff.SetZoom(zoom);
        }
        foreach (var card in cards) card.SystemCount = BandTrackPanel.OtherCardSystems;   // a row count the user chose is applied after the fit (BandView)
        var oneLine = cards.Sum(card => card.NaturalHeight(BandTrackPanel.OtherCardSystems)) + (cards.Count - 1) * gap;
        var twoLines = oneLine + cards[0].StaffLineHeight() * (BandTrackPanel.ShownSystems - BandTrackPanel.OtherCardSystems);
        if (twoLines <= height) cards[0].SystemCount = BandTrackPanel.ShownSystems;
    }

    // The height of one line of every card before the zoom, each less the room above its tab that nothing uses.
    private static double StaffUnits(IReadOnlyList<BandTrackPanel> cards) => cards.Sum(card => card.StaffLineUnits() - card.CropUnits());

    /// <summary>
    /// Sizes the fretboards the user has not sized: every one at its natural height, then all of them smaller by the same share (never below the board's smallest
    /// size) until the staff can reach <see cref="TargetStaffZoom"/>.
    /// </summary>
    private static void SizeAutoFretboards(IReadOnlyList<BandTrackPanel> cards, double height, double gap)
    {
        var boards = cards.Where(card => card.InstrumentIsAuto).ToList();
        foreach (var board in boards) board.SetInstrumentSize(null, board.InstrumentAutoMaxHeight);
        var room = boards.Sum(board => board.InstrumentAutoMaxHeight - board.InstrumentMinHeight);
        if (room <= 0) return;
        var naturalFixed = cards.Sum(card => card.NaturalHeight(0)) + (cards.Count - 1) * gap;
        var missing = TargetStaffZoom * StaffUnits(cards) - (height - FitSlack - naturalFixed);
        var share = Math.Clamp(missing / room, 0, 1);
        foreach (var board in boards) board.SetInstrumentSize(null, board.InstrumentAutoMaxHeight - share * (board.InstrumentAutoMaxHeight - board.InstrumentMinHeight));
    }
}
