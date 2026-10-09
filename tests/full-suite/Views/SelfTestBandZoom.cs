using TabForge.Presets;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views.Band;

namespace TabForge;

/// <summary>Band lane zoom (all lanes together, saved), hidden-instrument rows, rows-per-screen refit and the Reset view menu item.</summary>
public static partial class SelfTest
{
    private static void TestBandLaneZoom()
    {
        var project = BandUnevenSong(60);
        var host = new FakeBandHost(project);
        host.Settings.Timeline.Band = new BandSettings();
        using var band = new BandViewController(host);
        BandStage(band, 1000, 900);
        band.Tick();
        BandStage(band, 1000, 900);
        var lanes = band.View.Rows.Select(r => r.Lane).ToList();
        var before = lanes.Select(l => l.Editor.Zoom).ToList();

        band.RunMenu(new TabForge.Views.MenuSpec { Id = TabForge.Views.BandMenus.LaneZoomOutId });
        BandStage(band, 1000, 900);
        var zoomed = host.Settings.Timeline.Band!.LaneZoom;
        Check("band zoom: the menu zooms out and saves it", zoomed < 1 && Math.Abs(zoomed - 1 / BandChoices.LaneZoomStep) < 0.01, zoomed.ToString());
        Check("band zoom: every lane shrank by the same factor", lanes.Select((l, i) => l.Editor.Zoom < before[i] - 0.001).All(x => x) && lanes.Max(l => l.Editor.Zoom) - lanes.Min(l => l.Editor.Zoom) < 0.001,
            string.Join(",", lanes.Select(l => l.Editor.Zoom.ToString("0.00"))));
        band.Tick(); band.Tick();
        Check("band zoom: every lane carries the zoom", lanes.All(l => Math.Abs(l.UserZoom - zoomed) < 0.001));
        band.RunMenu(new TabForge.Views.MenuSpec { Id = TabForge.Views.BandMenus.LaneZoomInId });
        band.RunMenu(new TabForge.Views.MenuSpec { Id = TabForge.Views.BandMenus.LaneZoomInId });
        BandStage(band, 1000, 900);
        Check("band zoom: zoom in works and stays within limits", host.Settings.Timeline.Band!.LaneZoom > 1 && host.Settings.Timeline.Band.LaneZoom <= BandChoices.MaxLaneZoom);
        band.RunMenu(new TabForge.Views.MenuSpec { Id = TabForge.Views.BandMenus.LaneZoomResetId });
        Check("band zoom: reset returns to 1", host.Settings.Timeline.Band!.LaneZoom == 1);
        Check("band zoom: Preferences has the row", SettingsCatalog.Build(host.Settings).Any(d => d.Key == "band.lanezoom"));
        Check("band zoom: the menu has lane zoom items", TabForge.Views.BandMenus.Build(new TabForge.Views.BandMenuState("Tab", "Full neck", 3, true), _ => "")
            .Any(m => m.Children?.Any(c => c.Id == TabForge.Views.BandMenus.LaneZoomInId) == true));
    }

    private static void TestBandViewReset()
    {
        var project = BandUnevenSong(30);
        for (var i = project.Tracks.Count; i < 6; i++) project.Tracks.Add(new TrackModel { Name = "T" + i, Measures = TemplateFactory.Measures(30) });
        var host = new FakeBandHost(project);
        host.Settings.Timeline.Band = new BandSettings();
        using var band = new BandViewController(host);
        BandStage(band, 1000, 900);
        band.Tick();
        foreach (var t in project.Tracks) if (!band.State.ShownTracks().Contains(t)) band.State.Toggle(t);
        band.Tick();
        BandStage(band, 1000, 900);

        // 3 -> 5 rows per screen: every row fits the viewport at once, custom heights are dropped.
        band.View.Rows[0].RequestHeight(500);
        band.ChangeRowsPerScreen(2);
        BandStage(band, 1000, 900);
        var total = band.View.Rows.Take(5).Sum(r => r.ActualHeight + 2);
        Check("band rows: five rows per screen all fit the viewport", band.State.RowsPerScreen == 5 && total <= band.View.Viewport + 1 && band.State.HeightOf(band.View.Rows[0].Track) is null, total.ToString("0") + " of " + band.View.Viewport.ToString("0"));
        Check("band rows: a row is never taller than the viewport", band.View.Rows.All(r => r.ActualHeight <= band.View.Viewport + 1));

        // Hidden instrument: header strip plus the full-width lane.
        band.ToggleInstrumentOf(0);
        BandStage(band, 1000, 900);
        var row = band.View.Rows[0];
        Check("band hidden: the lane takes the freed width", row.InstrumentHidden && row.Lane.ActualWidth > 1000 - BandRow.HiddenHeaderWidth - 30, row.Lane.ActualWidth.ToString("0"));

        // Reset view keeps the shown tracks and their order.
        var order = band.State.ShownTracks().ToList();
        var band0 = host.Settings.Timeline.Band!;
        band0.LaneZoom = 2; band0.LaneLayout = BandChoices.Horizontal; band0.PlayheadLine = BandChoices.FullRow; band0.InstrumentWidth = 400;
        band.RunMenu(new TabForge.Views.MenuSpec { Id = TabForge.Views.BandMenus.ResetViewId });
        BandStage(band, 1000, 900);
        Check("band reset: settings back to defaults", band0.LaneZoom == 1 && band0.LaneLayout == BandChoices.Vertical && band0.PlayheadLine == BandChoices.TabOnly && band0.InstrumentWidth == 0);
        Check("band reset: rows per screen and hidden instruments restored", band.State.RowsPerScreen == 3 && !band.State.IsInstrumentHidden(project.Tracks[0]));
        Check("band reset: the shown tracks and their order stay", band.State.ShownTracks().SequenceEqual(order));
        Check("band reset: the menu has Reset view", TabForge.Views.BandMenus.Build(new TabForge.Views.BandMenuState("Tab", "Full neck", 3, true), _ => "").Any(m => m.Id == TabForge.Views.BandMenus.ResetViewId));
    }
}
