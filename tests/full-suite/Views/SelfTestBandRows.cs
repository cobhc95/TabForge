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
