using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Views;
using TabForge.Views.Band;
using TabForge.Visualization;

namespace TabForge;

/// <summary>Band view stage 2: track pills choose the rows, rows per screen and row heights have bounds, dragging reorders the Band only, the tab lanes mark the notes, a stopped instrument shows the cursor's notes.</summary>
public static partial class SelfTest
{
    /// <summary>A Band song of <paramref name="bars"/> bars with <paramref name="tracks"/> tracks (the first three are guitar, bass and drums).</summary>
    private static SongProject BandSongOf(int bars, int tracks)
    {
        var project = BandSong(bars);
        for (var i = project.Tracks.Count; i < tracks; i++)
            project.Tracks.Add(new TrackModel { Name = "Extra " + i, Measures = TemplateFactory.Measures(bars) });
        return project;
    }

    private static InstrumentVisualState? ShownStateOf(InstrumentPanel panel) =>
        (InstrumentVisualState?)typeof(InstrumentPanel).GetField("_state", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(panel);

    private static void ClickPill(BandViewController band, int index) =>
        band.View.Pills[index].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static void TestBandPillsAndRows()
    {
        var project = BandSongOf(8, 5);
        var host = new FakeBandHost(project);
        using var band = new BandViewController(host);
        BandStage(band, 900, 800);
        band.Tick();
        BandStage(band, 900, 800);
        Eq("band pills: one pill per track", 5, band.View.Pills.Count);
        var close = band.View.CloseButton;
        var closeBox = close.TransformToAncestor(band.View).TransformBounds(new System.Windows.Rect(close.RenderSize));
        close.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Check("band header: a close button at the right edge asks the host to leave Band view", close.Visibility == System.Windows.Visibility.Visible && closeBox.Right > 900 - 12 && closeBox.Width >= 22 && closeBox.Top >= 0 && closeBox.Bottom < 40
            && System.Windows.Automation.AutomationProperties.GetName(close) == "Close Band view" && host.ScoreMenuRuns.Count(m => m.Id == BandView.CloseId) == 1, $"{closeBox}, runs {host.ScoreMenuRuns.Count}");
        Check("band pills: the first three tracks are on by default", band.View.Rows.Select(r => r.Track).SequenceEqual(project.Tracks.Take(3))
            && band.View.Pills.Select(p => p.IsChecked == true).SequenceEqual(new[] { true, true, true, false, false }));
        var kept = band.View.Rows[1];
        ClickPill(band, 3);
        Eq("band pills: a pill adds its row", 4, band.View.Rows.Count);
        Check("band pills: the other rows are kept (their lanes are not engraved again)", ReferenceEquals(kept, band.View.Rows[1]));
        ClickPill(band, 0);
        Check("band pills: clicking a lit pill removes its row", band.View.Rows.All(r => r.Track != project.Tracks[0]) && band.View.Pills[0].IsChecked == false);
        Check("band pills: a hidden row lets go of its song", band.View.Pills.Count == 5);
        ClickPill(band, 0);
        ClickPill(band, 4);
        BandStage(band, 900, 800);
        Eq("band pills: there is no limit on shown rows", 5, band.View.Rows.Count);
        var height = band.View.RowHeight;
        Check("band pills: five shown rows keep the three-per-screen height (the rest scroll)", band.View.Rows.All(r => Math.Abs(r.ActualHeight - height) < 1) && band.View.Rows.Count > band.State.RowsPerScreen);
        host.Selected = project.Tracks[2];
        band.ToggleSelectedTrackRow();
        Check("band pills: the toggle-row command hides the selected track", band.View.Rows.All(r => r.Track != project.Tracks[2]) && band.View.Pills[2].IsChecked == false);
        project.Tracks.Add(new TrackModel { Name = "Late", Measures = TemplateFactory.Measures(8) });
        band.Tick();
        Check("band pills: a new track gets a pill, hidden", band.View.Pills.Count == 6 && band.View.Pills[5].IsChecked == false && band.View.Rows.Count == 4);

        // The layout choice (fretboard beside or above the tab) and the track colour frame.
        var settings = host.Settings.Timeline.Band!;
        var first = band.View.Rows[0];
        BandStage(band, 900, 800);
        var besideWidth = first.InstrumentColumnWidth;
        var besideHeight = first.ActualHeight;
        Rect BoxOf(FrameworkElement e) => e.TransformToAncestor(first).TransformBounds(new Rect(e.RenderSize));
        Check("band layout: the fretboard is beside the tab by default, with the divider and the frame", !settings.FretboardAbove && settings.ColourFrame && !first.Stacked && first.WidthGripShown
            && BoxOf(first.InstrumentSide).Right <= BoxOf(first.Lane).Left + 0.5, $"{BoxOf(first.InstrumentSide)} / {BoxOf(first.Lane)}");
        var caption = band.View.Caption.TransformToAncestor(band.View).TransformBounds(new Rect(band.View.Caption.RenderSize));
        var chevron = band.View.LayoutButton.TransformToAncestor(band.View).TransformBounds(new Rect(band.View.LayoutButton.RenderSize));
        Check("band layout: the caption sits inside the roll-down button, which reaches past it for the chevron", chevron.Left <= caption.Left && chevron.Right >= caption.Right + 10 && !band.View.Caption.IsHitTestVisible && band.View.LayoutButton.ToolTip is not null, $"{caption} / {chevron}");
        var saves = host.Saves;
        band.LayoutMenu.RunMenu(new MenuSpec { Id = BandLayoutMenu.PlaceId, Arg = BandLayoutMenu.Above });
        BandStage(band, 900, 800); band.Tick(); BandStage(band, 900, 800);
        var above = BoxOf(first.InstrumentSide);
        var under = BoxOf(first.Lane);
        Check("band layout: above puts each fretboard over its score at the full row width, hides the divider and saves", settings.FretboardAbove && host.Saves == saves + 1 && above.Bottom <= under.Top + 0.5
            && Math.Abs(above.Width - under.Width) < 1 && above.Width > first.ActualWidth - 20 && band.View.Rows.All(r => r.Stacked && !r.WidthGripShown), $"{above} / {under}");
        Check("band layout: above makes the rows taller by the instrument, and the rows scroll", first.ActualHeight > besideHeight + 60 && Math.Abs(under.Height - besideHeight) < 40 && band.View.ScrollableHeight > 0,
            $"{besideHeight:0} -> {first.ActualHeight:0}, lane {under.Height:0}, scroll {band.View.ScrollableHeight:0}");
        var menu = BandLayoutMenu.Build(settings, _ => "");
        Check("band layout: the menu ticks the current choice", menu.Count == 6 && menu[4].IsSeparator && menu[5].Id == BandLayoutMenu.ResetId && !menu[0].Checked && menu[1].Checked && menu[1].Radio && menu[2].IsSeparator && menu[3].Checked && !menu[3].Radio);
        band.Run(BandLayoutMenu.ToggleAboveId);
        BandStage(band, 900, 800); band.Tick(); BandStage(band, 900, 800);
        Check("band layout: back to beside restores the side-by-side widths and the row height", !settings.FretboardAbove && Math.Abs(first.InstrumentColumnWidth - besideWidth) < 1 && first.WidthGripShown
            && Math.Abs(first.ActualHeight - besideHeight) < 1, $"{besideWidth:0} -> {first.InstrumentColumnWidth:0}, {besideHeight:0} -> {first.ActualHeight:0}");
        var wanted = ColourHex.TryParse(first.Track.ColorHex, out var hex) ? Color.FromRgb(hex.R, hex.G, hex.B) : Colors.Gray;
        var noEffect = true;
        for (DependencyObject? at = first.Lane; at is not null && !ReferenceEquals(at, band.View); at = VisualTreeHelper.GetParent(at)) noEffect &= at is not UIElement { Effect: not null };
        Check("band frame: the line has the track's colour, the halo shows, and no ancestor of the score has an Effect", first.Frame.HaloVisible && first.Frame.Line.BorderBrush is SolidColorBrush line
            && Color.FromRgb(line.Color.R, line.Color.G, line.Color.B) == wanted && line.Color.A is > 100 and < 180 && first.Frame.Line.CornerRadius.TopLeft == 6 && noEffect, $"{first.Frame.Line.BorderBrush}");
        band.Run(BandLayoutMenu.ToggleFrameId);
        Check("band frame: off, the row has its plain outline again", !settings.ColourFrame && !first.Frame.HaloVisible && first.BorderThickness.Left == 1 && first.Frame.Line.BorderThickness.Left == 0);
        settings.FretboardAbove = true;
        var loaded = System.Text.Json.JsonSerializer.Deserialize<Services.AppSettings>(System.Text.Json.JsonSerializer.Serialize(host.Settings))!.Timeline.Band!;
        Check("band layout: both choices come back from saved settings (a fresh profile is above with the frame)", loaded.FretboardAbove && !loaded.ColourFrame && new Services.BandSettings() is { FretboardAbove: true, ColourFrame: true });
        Check("band layout: the compact strip has no roll-down button", !new TabForge.CompactMode.CompactModeStrip().Children.OfType<System.Windows.Controls.Button>()
            .Any(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Band view layout"));
    }

    private static void TestBandRowSizing()
    {
        var project = BandSongOf(8, 4);
        var host = new FakeBandHost(project);
        using var band = new BandViewController(host);
        BandStage(band, 900, 800);
        band.Tick();
        BandStage(band, 900, 800);
        Eq("band rows: three per screen by default", 3, band.State.RowsPerScreen);
        for (var i = 0; i < 10; i++) band.ChangeRowsPerScreen(1);
        Eq("band rows: at most five per screen", 5, band.State.RowsPerScreen);
        for (var i = 0; i < 10; i++) band.ChangeRowsPerScreen(-1);
        Eq("band rows: at least one per screen", 1, band.State.RowsPerScreen);
        BandStage(band, 900, 800);
        var one = band.View.RowHeight;
        band.ChangeRowsPerScreen(2);
        BandStage(band, 900, 800);
        Check("band rows: more rows per screen make each row shorter", band.View.RowHeight < one && band.View.Rows.All(r => Math.Abs(r.Height - band.View.RowHeight) < 1), $"{one:0} -> {band.View.RowHeight:0}");

        var row = band.View.Rows[1];
        var viewport = band.View.Viewport;
        row.RequestHeight(10);
        Eq("band rows: a row never shrinks below the minimum", BandLayoutState.MinRowHeight, row.Height);
        row.RequestHeight(99999);
        Eq("band rows: a row never grows past the viewport", viewport, row.Height);
        row.RequestHeight(200);
        Check("band rows: a dragged height sticks, the others keep the shared one", row.Height == 200 && Math.Abs(band.View.Rows[0].Height - band.View.RowHeight) < 1);
        band.ChangeRowsPerScreen(1);
        Check("band rows: a change of rows per screen refits every row (custom heights are dropped)", Math.Abs(row.Height - band.View.RowHeight) < 1 && band.State.HeightOf(row.Track) is null);
        row.RequestReset();
        Check("band rows: a reset returns to the shared height", Math.Abs(row.Height - band.View.RowHeight) < 1);

        // Fresh settings: the fretboard above the tab, the first row's instrument only, and as many whole systems per row as fit the pane.
        var fresh = new FakeBandHost(BandSongOf(40, 4));
        var prefs = fresh.Settings.Timeline.Band = new Services.BandSettings();
        using var auto = new BandViewController(fresh);
        void Lay(double h) { for (var i = 0; i < 3; i++) { BandStage(auto, 1000, h); auto.Tick(); } BandStage(auto, 1000, h); }
        Lay(900);
        var ar = auto.View.Rows;
        Check("band defaults: fretboard above, and only the first row shows its instrument", prefs.FretboardAbove && ar.Count == 3 && ar.All(r => r.Stacked) && !ar[0].InstrumentHidden && ar[1].InstrumentHidden && ar[2].InstrumentHidden);
        string Shape() => string.Join(" ", ar.Select(r => $"{r.Lane.VisibleSystems}x{r.SystemHeight:0} in {r.Lane.ActualHeight:0}")) + $" fretboard {ar[0].InstrumentHeightNow:0} ({ar[0].InstrumentMin:0}-{ar[0].InstrumentNatural:0}) scroll {auto.View.ScrollableHeight:0}";
        bool Tight() => ar.All(r => Math.Abs(r.Lane.ActualHeight - r.Lane.VisibleSystems * r.SystemHeight) < 2);   // a row is as tall as its lines of music, no empty score under them
        Lay(620);
        var small = ar[0].InstrumentHeightNow;
        Check("band defaults: a normal pane shows all three rows at once, one system each, the fretboard at its least height", ar.All(r => r.Lane.VisibleSystems == 1) && auto.View.ScrollableHeight < 1
            && small >= ar[0].InstrumentMin - 1 && small < ar[0].InstrumentNatural - 1, Shape());
        Lay(900);
        Check("band defaults: a taller pane gives the fretboard more height before any second system", ar.All(r => r.Lane.VisibleSystems == 1) && Tight() && auto.View.ScrollableHeight < 1 && ar[0].InstrumentHeightNow > small + 10, Shape());
        Lay(1700);
        Check("band defaults: a tall pane has the fretboard at its natural height, the main row one line ahead of the others, and no empty score", ar[0].Lane.VisibleSystems == ar[1].Lane.VisibleSystems + 1 && ar[1].Lane.VisibleSystems == ar[2].Lane.VisibleSystems && ar[1].Lane.VisibleSystems >= 1 && Tight() && auto.View.ScrollableHeight < 1 && Math.Abs(ar[0].InstrumentHeightNow - ar[0].InstrumentNatural) < 1, Shape());
        Lay(300);
        Check("band defaults: far too small a pane keeps one system per lane and the rows scroll", ar.All(r => r.Lane.VisibleSystems == 1) && auto.View.ScrollableHeight > 1, Shape());
        Lay(1700);
        ar[1].RequestHeight(ar[1].StackedExtra + ar[1].Chrome + ar[1].SystemHeight * 3);
        Check("band defaults: a row dragged to three systems is remembered for the other rows of the next tabs, not the main row", prefs.SystemsPerRow == 3 && prefs.MainSystemsPerRow == 0, prefs.SystemsPerRow.ToString());
        ar[1].RequestInstrumentToggle(); ar[2].RequestInstrumentToggle();
        var next = new FakeBandHost(BandSongOf(8, 3));
        next.Settings.Timeline.Band = prefs;
        using var second = new BandViewController(next);
        BandStage(second, 1000, 900); second.Tick();
        Check("band defaults: every fretboard turned on becomes the rule, and a second new song starts with all on", prefs.Fretboards == Services.BandChoices.FretboardsAll && second.View.Rows.Count == 3 && second.View.Rows.All(r => !r.InstrumentHidden), prefs.Fretboards);
        var stored = BandSongOf(8, 3);
        stored.BandLayout = new BandLayoutData { Shown = stored.Tracks.Take(2).Select(t => t.Id).ToList(), HiddenInstruments = new() { stored.Tracks[0].Id } };
        var keeps = new FakeBandHost(stored);
        keeps.Settings.Timeline.Band = prefs;
        using var third = new BandViewController(keeps);
        BandStage(third, 1000, 900); third.Tick();
        Check("band defaults: an old song keeps only which tracks it shows; its stored instruments are not read and not rewritten", third.View.Rows.Count == 2 && third.View.Rows.All(r => !r.InstrumentHidden) && stored.BandLayout!.HiddenInstruments.Count == 1);
        auto.ResetView();
        Lay(900);
        Check("band defaults: Reset view restores above, the first fretboard only and whole systems that fit", prefs.FretboardAbove && prefs.ColourFrame && prefs.Fretboards == Services.BandChoices.FretboardsFirst && prefs.SystemsPerRow == 0 && prefs.MainSystemsPerRow == 0
            && !ar[0].InstrumentHidden && ar[1].InstrumentHidden && ar[2].InstrumentHidden && auto.State.HeightOf(ar[1].Track) is null && ar.All(r => r.Lane.VisibleSystems >= 1), Shape());
    }

    private static void TestBandReorder()
    {
        var pitches = new[] { 100.0, 100, 100, 100 };
        Eq("band reorder: a small drag changes nothing", 0, BandLayoutState.DropIndex(pitches, 0, 20));
        Eq("band reorder: passing half of the next row swaps", 1, BandLayoutState.DropIndex(pitches, 0, 60));
        Eq("band reorder: dragged to the bottom lands last", 3, BandLayoutState.DropIndex(pitches, 0, 300));
        Eq("band reorder: dragged up past half of the one above", 1, BandLayoutState.DropIndex(pitches, 2, 140));
        Eq("band reorder: dragged to the top lands first", 0, BandLayoutState.DropIndex(pitches, 3, 0));
        Eq("band reorder: the slot of a row moved down", 200.0, BandLayoutState.SlotTop(pitches, 0, 2));

        var project = BandSongOf(8, 5);
        var host = new FakeBandHost(project);
        using var band = new BandViewController(host);
        BandStage(band, 900, 900);
        band.Tick();
        ClickPill(band, 3);
        ClickPill(band, 4);
        BandStage(band, 900, 900);
        var names = project.Tracks.Select(t => t.Name).ToList();
        var engravings = 0;
        TabEditorControl.RenderFaultInjection = _ => engravings++;
        try
        {
            var rows = band.View.Rows;
            var pitch = rows[0].ActualHeight + rows[0].Margin.Bottom;
            var reorder = band.View.Reorder;
            reorder.Begin(rows[0], 10);
            reorder.Move(12);
            Check("band reorder: a tiny movement does not start a drag", !reorder.Dragging);
            reorder.Move(10 + pitch * 2 + 5);
            Check("band reorder: the held row follows the pointer with a transform", reorder.Dragging && Math.Abs(rows[0].Shift.Y - (pitch * 2 + 5)) < 1 && reorder.Target == 2, $"{rows[0].Shift.Y:0} target {reorder.Target}");
            Eq("band reorder: dragging engraves nothing", 0, engravings);
            var held = rows[0];
            reorder.End(false);
            Check("band reorder: the drop moves the row in the Band", band.View.Rows[2] == held && band.View.Rows.Select(r => r.Track.Name).Take(3).SequenceEqual(new[] { "Bass", "Drums", "Guitar" }));
            Check("band reorder: the song's own track order is untouched", project.Tracks.Select(t => t.Name).SequenceEqual(names));
            Check("band reorder: the shifts are cleared", band.View.Rows.All(r => r.Shift.Y == 0));
            Check("band reorder: the order is the Band's own", band.State.Order[2] == project.Tracks[0] && band.State.ShownTracks().Count == 5);
            Eq("band reorder: dropping engraves nothing", 0, engravings);
        }
        finally { TabEditorControl.RenderFaultInjection = null; }
    }

    private static void TestBandNoteGlow()
    {
        var project = BandSong(40);
        var host = new FakeBandHost(project);
        using var band = new BandViewController(host);
        BandStage(band, 900, 720);
        band.Tick();
        BandStage(band, 900, 720);
        var lane = band.View.Rows[0].Lane;
        var engravings = 0;
        TabEditorControl.RenderFaultInjection = _ => engravings++;
        try
        {
            lane.ShowAt(2, 0);
            lane.ShowSounding(new[] { (2, 4, 0, "5", false), (2, 4, 2, "7", true) });
            var glow = lane.Glow;
            var repaints = glow.Repaints;
            Check("band glow: each sounding note gets a chip", glow.Chips.Count == 2 && glow.Chips[0].Y < glow.Chips[1].Y && glow.Chips[1].Struck, glow.Chips.Count.ToString());
            lane.ShowSounding(new[] { (2, 4, 0, "5", false), (2, 4, 2, "7", true) });
            Eq("band glow: the same notes repaint nothing", repaints, glow.Repaints);
            lane.ShowSounding(new[] { (2, 8, 0, "5", false) });
            Check("band glow: new notes move the glow", glow.Chips.Count == 1 && glow.Chips[0].X > 0 && glow.Repaints == repaints + 1);
            lane.ShowAt(3, 0.5);
            Check("band glow: the strip sliding leaves the glow on its note (same transform)", ReferenceEquals(glow.RenderTransform, lane.Editor.RenderTransform));
            lane.ShowSounding(Array.Empty<(int, int, int, string, bool)>());
            Check("band glow: no notes, no glow", glow.Chips.Count == 0);
            // A song playing through the controller marks the notes that sound at that time, with no engraving.
            band.Apply(2, 0.0, 0, true, false);
            BandStage(band, 900, 720);
            Eq("band glow: marking notes never engraves", 0, engravings);
            var picture = new System.Windows.Media.Imaging.RenderTargetBitmap(900, 720, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            lane.ShowSounding(new[] { (2, 4, 0, "5", true) });
            picture.Render(band.View);
            Check("band glow: no green column element is left in the lane", lane.PlayheadVisible && lane.Glow.Chips.Count == 1);
        }
        finally { TabEditorControl.RenderFaultInjection = null; }
    }

    private static void TestBandStoppedInstruments()
    {
        var project = BandSong(8);
        var host = new FakeBandHost(project);
        using var band = new BandViewController(host);
        BandStage(band, 900, 720);
        band.Tick();
        BandStage(band, 900, 720);
        host.Editor.SetPosition(1, 4, 0, false);   // bar 1, the second beat: fret 1 on every track
        band.Tick();
        var shown = ShownStateOf(band.View.Rows[0].Instrument);
        Check("band stopped: the instrument shows the notes at the cursor", shown is { IsPlaying: false } s && s.Notes.Any(n => n.Role == VisualRole.Selected && n.Fret == 1), shown is null ? "none" : string.Join(",", shown.Notes.Select(n => n.Role + ":" + n.Fret)));
        host.Editor.SetPosition(1, 8, 0, false);
        band.Tick();
        Check("band stopped: moving the cursor within the bar updates it", ShownStateOf(band.View.Rows[0].Instrument)!.Notes.Any(n => n.Role == VisualRole.Selected && n.Fret == 2));
        Check("band stopped: every instrument follows the cursor", band.View.Rows.All(r => ShownStateOf(r.Instrument)!.Notes.Any(n => n.Role == VisualRole.Selected)));
    }
}
