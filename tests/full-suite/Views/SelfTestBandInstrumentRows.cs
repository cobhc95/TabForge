using TabForge.Models;
using TabForge.Services;
using TabForge.Views;
using TabForge.Views.Band;

namespace TabForge;

/// <summary>Band view rows: per-row instrument hide, the instrument/lane width grip (shared or per row, saved) and the playhead line length.</summary>
public static partial class SelfTest
{
    private static void TestBandInstrumentRows()
    {
        var project = BandSongOf(8, 4);
        var host = new FakeBandHost(project);
        using var band = new BandViewController(host);
        BandStage(band, 1000, 800);
        band.Tick();
        BandStage(band, 1000, 800);
        var settings = host.Settings.Timeline.Band!;
        var row0 = band.View.Rows[0];
        var row1 = band.View.Rows[1];

        // Hide / show one row's instrument.
        var laneBefore = row0.Lane.ActualWidth;
        row0.RequestInstrumentToggle();
        BandStage(band, 1000, 800);
        Check("band instrument: the toggle hides only that row's instrument", row0.InstrumentHidden && !row1.InstrumentHidden
            && row0.Instrument.Visibility != System.Windows.Visibility.Visible && row1.Instrument.Visibility == System.Windows.Visibility.Visible);
        Check("band instrument: the lane takes the width", row0.Lane.ActualWidth > laneBefore + 100, $"{laneBefore:0} -> {row0.Lane.ActualWidth:0}");
        Check("band instrument: the hide is not written into the song", project.BandLayout is null || project.BandLayout.HiddenInstruments.Count == 0);
        var laneAt = row0.Lane.TransformToAncestor(row0).Transform(new System.Windows.Point(0, 0));
        var headerBox = row0.Header.TransformToAncestor(row0).TransformBounds(new System.Windows.Rect(row0.Header.RenderSize));
        // The row grows by the strip's height: the lane is as tall as beside a shown instrument and nothing of its first system lies under the strip.
        const double edge = BandRowFrame.Inset;   // the track colour frame (on by default) lies between the row's edge and its content
        Check("band instrument: hidden, a thin name strip tops the row, the lane spans it and loses no score space", Math.Abs(laneAt.X - edge) <= 1 && Math.Abs(row0.Lane.ActualWidth - (row0.ActualWidth - 2 * edge)) <= 1
            && !row0.ToggleLit && row0.ToggleTip.StartsWith("Show the ") && headerBox.Height <= 20 && headerBox.Top <= edge + 1 && Math.Abs(headerBox.Width - row0.Lane.ActualWidth) <= 1
            && Math.Abs(laneAt.Y - headerBox.Bottom) <= 1 && row0.Lane.TopCutUsed == 0 && row0.Lane.ActualHeight >= row1.Lane.ActualHeight - 1 && Math.Abs(row0.ActualHeight - row1.ActualHeight - BandRow.StripHeight) <= 1 && row0.Lane.VisibleSystems >= row1.Lane.VisibleSystems,
            $"lane x {laneAt.X:0.#} y {laneAt.Y:0.#} w {row0.Lane.ActualWidth:0} of {row0.ActualWidth:0}, strip {headerBox}, inset taken {row0.Lane.TopCutUsed:0.#}, systems {row0.Lane.VisibleSystems} vs {row1.Lane.VisibleSystems}, lit {row0.ToggleLit}");
        row0.RequestInstrumentToggle();
        BandStage(band, 1000, 800);
        Check("band instrument: shown again, the column is back at the shared width with the toggle lit", row0.ToggleLit && row0.ToggleTip.StartsWith("Hide the ")
            && Math.Abs(row0.InstrumentColumnWidth - row1.InstrumentColumnWidth) < 1 && row0.InstrumentColumnWidth >= 200 && row0.Header.ActualWidth > row0.InstrumentColumnWidth - 2 && row0.Lane.TopCut == 0,
            $"column {row0.InstrumentColumnWidth:0} vs {row1.InstrumentColumnWidth:0}, header {row0.Header.ActualWidth:0}, lit {row0.ToggleLit}");
        Check("band instrument: the header icon shows it again", !row0.InstrumentHidden && Math.Abs(row0.Lane.ActualWidth - laneBefore) < 2 && project.BandLayout!.HiddenInstruments.Count == 0);

        // One shared width.
        row0.RequestWidth(300); row0.CommitWidth();
        BandStage(band, 1000, 800);
        Check("band width: a drag sets every row's instrument width", settings.InstrumentWidth == 300 && band.View.Rows.All(r => Math.Abs(r.InstrumentColumnWidth - 300) < 2));
        row0.RequestWidth(5); row0.CommitWidth();
        Eq("band width: clamped to the minimum", BandChoices.MinInstrumentWidth, settings.InstrumentWidth);
        row0.RequestWidth(99999); row0.CommitWidth();
        Eq("band width: clamped to the maximum", BandChoices.MaxInstrumentWidth, settings.InstrumentWidth);
        row0.RequestWidthReset();
        Eq("band width: a double-click resets it", 0.0, settings.InstrumentWidth);

        // A drag of many moves: nothing is fitted, synced or saved until it ends.
        BandStage(band, 1000, 800);
        var rowsNow = band.View.Rows;
        int FitsNow() => rowsNow.Sum(r => r.Lane.Fits);
        var (fits0, saves0, applies0, rebuilds0) = (FitsNow(), host.Saves, band.WidthDrag.Applies, band.Rebuilds);
        for (var i = 1; i <= 20; i++) row0.RequestWidth(220 + i * 5);
        Check("band width drag: 20 moves fit no lane, save nothing and store nothing", FitsNow() == fits0 && host.Saves == saves0 && band.Rebuilds == rebuilds0
            && band.WidthDrag.Applies == applies0 && settings.InstrumentWidth == 0, $"fits {FitsNow() - fits0}, saves {host.Saves - saves0}, applies {band.WidthDrag.Applies - applies0}");
        row0.CommitWidth();
        BandStage(band, 1000, 800);
        Check("band width drag: the end stores the last width, saves once and sizes every row", settings.InstrumentWidth == 320 && host.Saves == saves0 + 1
            && band.WidthDrag.Applies == applies0 + 1 && !band.WidthDrag.Active && rowsNow.All(r => Math.Abs(r.InstrumentColumnWidth - 320) < 2) && FitsNow() > fits0,
            $"width {settings.InstrumentWidth}, saves {host.Saves - saves0}, applies {band.WidthDrag.Applies - applies0}");

        // A row with its instrument hidden keeps its full lane, wrapped at its own width, whatever the shared instrument width is.
        var layoutBefore = settings.LaneLayout;
        settings.LaneLayout = BandChoices.Vertical;
        row1.RequestInstrumentToggle();
        row0.RequestWidth(220); row0.CommitWidth();
        band.Tick();
        BandStage(band, 1000, 800);
        var (hiddenLane, shownLane) = (row1.Lane.ActualWidth, row0.Lane.ActualWidth);
        double PageOf(BandRow r) => r.Lane.Editor.PageWidthOverride * r.Lane.Editor.Zoom;
        Check("band hidden instrument: its lane wraps at its own width", Math.Abs(PageOf(row1) - hiddenLane) < 1, $"page {PageOf(row1):0} lane {hiddenLane:0}");
        row0.RequestWidth(420); row0.CommitWidth();
        BandStage(band, 1000, 800);
        Check("band hidden instrument: a wider shared instrument narrows only the other rows' lanes", Math.Abs(row1.Lane.ActualWidth - hiddenLane) < 1
            && Math.Abs(PageOf(row1) - hiddenLane) < 1 && row0.Lane.ActualWidth < shownLane - 100 && Math.Abs(PageOf(row0) - row0.Lane.ActualWidth) < 1,
            $"hidden {hiddenLane:0} -> {row1.Lane.ActualWidth:0} (page {PageOf(row1):0}), shown {shownLane:0} -> {row0.Lane.ActualWidth:0} (page {PageOf(row0):0})");
        row0.RequestWidth(220); row0.CommitWidth();
        BandStage(band, 1000, 800);
        Check("band hidden instrument: a narrower shared instrument leaves its lane as wide as its row allows", Math.Abs(row1.Lane.ActualWidth - hiddenLane) < 1 && Math.Abs(PageOf(row1) - hiddenLane) < 1);
        row1.RequestInstrumentToggle();
        BandStage(band, 1000, 800);
        Check("band hidden instrument: shown again, its lane wraps with the others", Math.Abs(row1.Lane.ActualWidth - row0.Lane.ActualWidth) < 1 && Math.Abs(PageOf(row1) - PageOf(row0)) < 1);
        settings.LaneLayout = layoutBefore;
        row0.RequestWidthReset();
        band.Tick();

        // Per row, saved with the song.
        var menu = AllMenuItems(BandMenus.Build(new BandMenuState(BandChoices.Tab, BandChoices.FullNeck, 3, true), _ => "")).ToList();
        band.RunMenu(menu.First(m => m.Id == BandMenus.WidthModeId && m.Arg == BandMenus.ThisRow));
        Check("band width: the menu switches to this row only", settings.WidthPerRow);
        row1.RequestWidth(260); row1.CommitWidth();
        BandStage(band, 1000, 800);
        Check("band width: this row only changes just that row", Math.Abs(row1.InstrumentColumnWidth - 260) < 2 && Math.Abs(row0.InstrumentColumnWidth - 260) > 20 && settings.InstrumentWidth == 0);
        Check("band width: the own width is this view's, not written into the song", band.State.WidthOf(row1.Track) == 260 && (project.BandLayout is null || project.BandLayout.InstrumentWidths.Count == 0));
        var reopened = new BandLayoutState();
        reopened.Sync(project);
        Check("band width: a reopened song starts without it", reopened.WidthOf(row1.Track) is null);
        band.RunMenu(menu.First(m => m.Id == BandMenus.ResetWidthsId));
        Check("band width: Reset row widths clears them", band.State.WidthOf(row1.Track) is null && project.BandLayout!.InstrumentWidths.Count == 0);

        // Saved values are capped on load.
        project.BandLayout = new BandLayoutData { InstrumentWidths = new() { [row0.Track.Id] = double.NaN, [row1.Track.Id] = 1e9 } };
        var capped = new BandLayoutState();
        capped.Sync(project);
        Check("band width: widths stored in an older song are not read", capped.WidthOf(row0.Track) is null && capped.WidthOf(row1.Track) is null);

        // Playhead line.
        Check("band playhead: the setting defaults to tab only", settings.PlayheadLine == BandChoices.TabOnly);
        BandStage(band, 1000, 800);
        var lane = band.View.Rows[0].Lane;
        lane.ShowAt(1, 0.5, false);
        var tabOnly = lane.PlayheadSpan;
        Check("band playhead: tab only is shorter than the row", tabOnly.Height > 4 && tabOnly.Height < lane.ActualHeight - 4, $"{tabOnly.Height:0} of {lane.ActualHeight:0}");
        band.RunMenu(new TabForge.Views.MenuSpec { Id = BandMenus.PlayheadId, Arg = BandChoices.FullRow });
        Check("band playhead: full row spans the lane", Math.Abs(lane.PlayheadSpan.Height - lane.ActualHeight) < 1 && lane.PlayheadSpan.Top == 0);
    }
}
