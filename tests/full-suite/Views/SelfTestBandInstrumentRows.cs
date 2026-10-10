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
        Check("band instrument: the × hides only that row's instrument", row0.InstrumentHidden && !row1.InstrumentHidden
            && row0.Instrument.Visibility != System.Windows.Visibility.Visible && row1.Instrument.Visibility == System.Windows.Visibility.Visible);
        Check("band instrument: the lane takes the width", row0.Lane.ActualWidth > laneBefore + 100, $"{laneBefore:0} -> {row0.Lane.ActualWidth:0}");
        Check("band instrument: the hide is saved with the layout", project.BandLayout!.HiddenInstruments.SequenceEqual(new[] { row0.Track.Id }));
        row0.RequestInstrumentToggle();
        BandStage(band, 1000, 800);
        Check("band instrument: the header icon shows it again", !row0.InstrumentHidden && Math.Abs(row0.Lane.ActualWidth - laneBefore) < 2 && project.BandLayout!.HiddenInstruments.Count == 0);

        // One shared width.
        row0.RequestWidth(300);
        BandStage(band, 1000, 800);
        Check("band width: a drag sets every row's instrument width", settings.InstrumentWidth == 300 && band.View.Rows.All(r => Math.Abs(r.InstrumentColumnWidth - 300) < 2));
        row0.RequestWidth(5);
        Eq("band width: clamped to the minimum", BandChoices.MinInstrumentWidth, settings.InstrumentWidth);
        row0.RequestWidth(99999);
        Eq("band width: clamped to the maximum", BandChoices.MaxInstrumentWidth, settings.InstrumentWidth);
        row0.RequestWidthReset();
        Eq("band width: a double-click resets it", 0.0, settings.InstrumentWidth);

        // Per row, saved with the song.
        var menu = AllMenuItems(BandMenus.Build(new BandMenuState(BandChoices.Tab, BandChoices.FullNeck, 3, true), _ => "")).ToList();
        band.RunMenu(menu.First(m => m.Id == BandMenus.WidthModeId && m.Arg == BandMenus.ThisRow));
        Check("band width: the menu switches to this row only", settings.WidthPerRow);
        row1.RequestWidth(260);
        BandStage(band, 1000, 800);
        Check("band width: this row only changes just that row", Math.Abs(row1.InstrumentColumnWidth - 260) < 2 && Math.Abs(row0.InstrumentColumnWidth - 260) > 20 && settings.InstrumentWidth == 0);
        Check("band width: the own width is saved", project.BandLayout!.InstrumentWidths.TryGetValue(row1.Track.Id, out var saved) && saved == 260);
        var reopened = new BandLayoutState();
        reopened.Sync(project);
        Eq("band width: a reopened song keeps it", 260.0, reopened.WidthOf(row1.Track));
        band.RunMenu(menu.First(m => m.Id == BandMenus.ResetWidthsId));
        Check("band width: Reset row widths clears them", band.State.WidthOf(row1.Track) is null && project.BandLayout!.InstrumentWidths.Count == 0);

        // Saved values are capped on load.
        project.BandLayout = new BandLayoutData { InstrumentWidths = new() { [row0.Track.Id] = double.NaN, [row1.Track.Id] = 1e9 } };
        var capped = new BandLayoutState();
        capped.Sync(project);
        Check("band width: a bad saved width is dropped or capped", capped.WidthOf(row0.Track) is null && capped.WidthOf(row1.Track) == BandChoices.MaxInstrumentWidth);

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
