using TabForge.Models;
using TabForge.Services;
using TabForge.Views.Band;

namespace TabForge;

/// <summary>Band lanes stand on the same bar at the same place (horizontal and vertical); the vertical layout wraps, follows, glows and hit-tests.</summary>
public static partial class SelfTest
{
    /// <summary>A band song whose bass is denser in every third bar, so the tracks' own bar widths differ.</summary>
    private static SongProject BandUnevenSong(int bars)
    {
        var project = BandSong(bars);
        var bass = project.Tracks[1];
        for (var b = 0; b < bars; b += 3)
            for (var i = 0; i < 16; i++)
            {
                var cell = bass.Measures[b].Cells[i];
                if (cell.Notes.Count > 0) continue;
                cell.DurationDenominator = 16;
                cell.Notes.Add(new TabNote { StringIndex = 1, Fret = i % 5, MidiValue = 45 + i % 5 });
            }
        return project;
    }

    private static string LanesAgree(BandViewController band, int bar, bool vertical)
    {
        var lanes = band.View.Rows.Select(r => r.Lane).Where(l => l.HasGeometry(bar)).ToList();
        var first = lanes[0];
        foreach (var l in lanes.Skip(1))
        {
            if (Math.Abs(l.Editor.Zoom - first.Editor.Zoom) > 1e-6) return "zoom " + l.Editor.Zoom + " vs " + first.Editor.Zoom;
            if (l.SystemOf(bar) != first.SystemOf(bar)) return "system " + l.SystemOf(bar) + " vs " + first.SystemOf(bar);
            if (!vertical && Math.Abs(l.Offset - first.Offset) > 1e-6) return "offset " + l.Offset + " vs " + first.Offset;
            // Inside a bar each track spaces its own notes, so the line may differ by less than the bar; the bars themselves are identical (below).
            var barWidth = first.Editor.Layout.GetLayout(first.Editor.Track).Measure(bar).Width * first.Editor.Zoom;
            if (Math.Abs(l.PlayheadAtLane.X - first.PlayheadAtLane.X) > barWidth) return "playhead x " + l.PlayheadAtLane.X + " vs " + first.PlayheadAtLane.X;
            if (l.PlayheadVisible != first.PlayheadVisible) return "playhead visibility";
            foreach (var probe in new[] { bar, Math.Max(0, bar - 1), Math.Min(bar + 1, first.Editor.Track!.Measures.Count - 1) })
            {
                var a = first.Editor.Layout.GetLayout(first.Editor.Track).Measure(probe);
                var c = l.Editor.Layout.GetLayout(l.Editor.Track).Measure(probe);
                if (Math.Abs(a.X - c.X) > 0.01 || Math.Abs(a.Width - c.Width) > 0.01) return $"bar {probe} at {a.X:0.0}/{a.Width:0.0} vs {c.X:0.0}/{c.Width:0.0}";
            }
        }
        return "";
    }

    private static void TestBandEmptyBarLine()
    {
        // A lane whose bar holds no notes has no spacing of its own: its line stands where the lanes with notes put it.
        var project = BandUnevenSong(12);
        foreach (var measure in project.Tracks[2].Measures.Skip(3).Take(3))
            foreach (var cell in measure.Cells) cell.Notes.Clear();
        var host = new FakeBandHost(project);
        host.Settings.Timeline.Band = new BandSettings();
        using var band = new BandViewController(host);
        BandStage(band, 1000, 900);
        band.Tick();
        BandStage(band, 1000, 900);
        var lanes = band.View.Rows.Select(r => r.Lane).ToList();
        var bad = "";
        for (var bar = 3; bar < 6; bar++)
        {
            band.Apply(bar, 0.4, bar * 2000.0, true, false);
            var gap = Math.Abs(lanes[2].PlayheadAtLane.X - lanes[0].PlayheadAtLane.X);
            if (lanes[2].BarHasNotes(bar) || gap > 1.5) { bad = $"bar {bar}: empty lane x {lanes[2].PlayheadAtLane.X:0.0} vs {lanes[0].PlayheadAtLane.X:0.0}"; break; }
        }
        Check("band lanes: a lane with an empty bar draws its line at the same x as the lanes with notes", bad == "", bad);
    }

    private static void TestBandLanesInSync()
    {
        var project = BandUnevenSong(150);
        var host = new FakeBandHost(project);
        host.Settings.Timeline.Band = new BandSettings { LaneLayout = BandChoices.Horizontal };
        using var band = new BandViewController(host);
        BandStage(band, 1000, 800);
        band.Tick();
        BandStage(band, 1000, 800);
        host.Settings.Follow.Mode = FollowModes.Jump;
        band.Tick();
        // Rows of different heights must still draw at one size.
        band.View.Rows[0].RequestHeight(150);
        BandStage(band, 1000, 800);
        var worst = "";
        void Probe(int bar, double fraction, string what)
        {
            band.Apply(bar, fraction, bar * 2000.0, true, false);
            var bad = LanesAgree(band, bar, false);
            if (bad != "" && worst == "") worst = what + " bar " + bar + ": " + bad;
        }
        for (var bar = 0; bar < 150; bar++) { Probe(bar, 0.2, "playing"); Probe(bar, 0.8, "playing"); }
        Probe(5, 0.0, "seek"); Probe(41, 0.5, "seek"); Probe(120, 0.1, "seek"); Probe(2, 0.5, "seek back");
        Check("band lanes: every row shows the same bar at the same place, with the line at the same x (play, seeks)", worst == "", worst);

        // A row taken out and put back joins the others where they stand.
        Probe(60, 0.3, "before");
        ClickPill(band, 0);
        band.Tick(); BandStage(band, 1000, 800);
        Probe(61, 0.3, "row removed");
        ClickPill(band, 0);
        band.Tick(); BandStage(band, 1000, 800);
        Probe(62, 0.3, "row added");
        Probe(63, 0.3, "row added");
        Check("band lanes: rows added and removed keep the lanes together", worst == "", worst);
        BandStage(band, 800, 700);
        Probe(64, 0.6, "resized");
        Check("band lanes: a resized view keeps the lanes together", worst == "", worst);
    }

    private static void TestBandVerticalLanes()
    {
        var project = BandUnevenSong(80);
        var host = new FakeBandHost(project);
        host.Settings.Timeline.Band = new BandSettings();
        using var band = new BandViewController(host);
        host.Settings.Follow.Mode = FollowModes.Jump;
        BandStage(band, 1000, 900);
        band.Tick();
        band.Tick();
        BandStage(band, 1000, 900);
        var lanes = band.View.Rows.Select(r => r.Lane).ToList();
        Check("band vertical: the layout is vertical by default", BandChoices.NormalizeLayout(null) == BandChoices.Vertical && lanes.All(l => l.Vertical));
        Check("band vertical: the lanes wrap into several lines", lanes.All(l => l.SystemCount > 2), lanes[0].SystemCount.ToString());

        var bad = "";
        for (var bar = 0; bar < 80; bar++)
        {
            band.Apply(bar, 0.4, bar * 2000.0, true, false);
            var disagree = LanesAgree(band, bar, true);
            if (disagree != "" && bad == "") bad = "bar " + bar + ": " + disagree;
            foreach (var l in lanes)
                if (l.PlayheadVisible && (l.PlayheadAtLane.Y < -1 || l.PlayheadAtLane.Y > l.ActualHeight - 4) && bad == "") bad = "bar " + bar + ": the line is out of the lane at y " + l.PlayheadAtLane.Y.ToString("0");
        }
        Check("band vertical: every lane keeps the playing bar in view and in step", bad == "", bad);
        band.Apply(70, 0.5, 140000, true, false);
        var seekBad = LanesAgree(band, 70, true);
        Check("band vertical: a long seek lands on the bar in every lane", seekBad == "" && lanes[0].PlayheadVisible, seekBad);

        // Click: the line's own spot hits the playing bar.
        var lane = lanes[1];
        var spot = lane.PlayheadAtLane;
        var hit = lane.BarCellAt(spot.X + 1, spot.Y + 8);
        Check("band vertical: a click at the playhead's spot is the playing bar", hit is { Bar: 70 }, hit?.ToString());
        Check("band vertical: a click reaches the host", lane.Click(spot.X + 1, spot.Y + 8) && host.Cursor is { Track: 1, Bar: 70 }, host.Cursor?.ToString());

        // The now-sounding glow lands in the bar's own line.
        lane.ShowSounding(new[] { (70, 0, 0, "3", true) });
        Check("band vertical: a sounding note is glowed inside the lane", lane.Glow.Chips.Count == 1 && lane.Glow.Chips[0].Y * lane.Editor.Zoom + lane.SlideY is > 0 and var gy && gy < lane.ActualHeight, lane.Glow.Chips.Count > 0 ? (lane.Glow.Chips[0].Y * lane.Editor.Zoom + lane.SlideY).ToString("0") : "none");

        // The command and the menu switch the layout; the lanes follow.
        band.Run("Band.CycleLaneLayout");
        band.Tick();
        BandStage(band, 1000, 900);
        Check("band vertical: the command switches to horizontal", host.Settings.Timeline.Band!.LaneLayout == BandChoices.Horizontal && lanes.All(l => !l.Vertical));
        band.Apply(30, 0.5, 60000, true, false);
        Check("band vertical: horizontal lanes are still in step", LanesAgree(band, 30, false) == "");
        band.RunMenu(new TabForge.Views.MenuSpec { Id = TabForge.Views.BandMenus.LayoutId, Arg = BandChoices.Vertical });
        Check("band vertical: the menu switches back", host.Settings.Timeline.Band!.LaneLayout == BandChoices.Vertical);
        var catalog = SettingsCatalog.Build(host.Settings).Select(d => d.Key).ToHashSet();
        Check("band vertical: Preferences has the layout row", catalog.Contains("band.lanelayout"));
        Check("band vertical: the menu has the layout items", TabForge.Views.BandMenus.Build(new TabForge.Views.BandMenuState("Tab", "Full neck", 3, true), _ => "").Any(m => m.Header == "Lane layout" && m.Children?.Count == 2));
    }

    private static void TestBandRowsSamePage()
    {
        var project = BandUnevenSong(120);
        var host = new FakeBandHost(project);
        host.Settings.Timeline.Band = new BandSettings { WidthPerRow = true };
        using var band = new BandViewController(host);
        BandStage(band, 1000, 900);
        band.Tick();
        BandStage(band, 1000, 900);
        // Rows with different instrument widths leave their lanes different widths.
        band.State.SetWidth(project.Tracks[2], 380);
        band.State.SetWidth(project.Tracks[0], 120);
        band.Tick();
        BandStage(band, 1000, 900);
        var lanes = band.View.Rows.Select(r => r.Lane).ToList();
        Check("band page: the lanes really differ in width", lanes.Select(l => Math.Round(l.ActualWidth)).Distinct().Count() > 1);
        var bad = "";
        for (var bar = 0; bar < 120 && bad == ""; bar++)
        {
            if (lanes.Select(l => l.SystemOf(bar)).Distinct().Count() > 1) bad = "bar " + bar + " is in systems " + string.Join("/", lanes.Select(l => l.SystemOf(bar)));
            else if (lanes.Select(l => Math.Round(l.Editor.Zoom, 4)).Distinct().Count() > 1) bad = "zoom " + string.Join("/", lanes.Select(l => l.Editor.Zoom));
        }
        Check("band page: every row has the same bars on a page and the same tab scale", bad == "", bad);
    }

    private static void TestBandRowsSamePageHorizontal()
    {
        var project = BandSong(120);
        foreach (var t in project.Tracks.Take(2)) t.StringTunings = new List<int> { 64, 59, 55, 50, 45, 40, 35 };
        var host = new FakeBandHost(project);
        host.Settings.Timeline.Band = new BandSettings { WidthPerRow = true, LaneLayout = BandChoices.Horizontal };
        host.Settings.Follow.Mode = FollowModes.Jump;
        using var band = new BandViewController(host);
        BandStage(band, 2000, 1100);
        band.Tick();
        BandStage(band, 2000, 1100);
        band.State.SetWidth(project.Tracks[0], 300);
        band.State.SetWidth(project.Tracks[1], 520);
        band.State.SetWidth(project.Tracks[2], 400);
        band.Tick();
        BandStage(band, 2000, 1100);
        var lanes = band.View.Rows.Select(r => r.Lane).ToList();
        Check("band page horizontal: the lanes differ in width", lanes.Select(l => Math.Round(l.ActualWidth)).Distinct().Count() > 1);
        var bad = "";
        foreach (var bar in new[] { 5, 40, 89, 90, 91, 92, 93 })
        {
            band.Apply(bar, 0.0, bar * 2000.0, true, false);
            var z = lanes.Select(l => l.Editor.Zoom).ToList();
            var x = lanes.Select(l => l.PlayheadAtLane.X).ToList();
            var o = lanes.Select(l => l.Offset).ToList();
            var bx = lanes.Select(l => l.Editor.Layout.GetLayout(l.Editor.Track).Measure(bar).X * l.Editor.Zoom).ToList();
            if (bx.Max() - bx.Min() > 0.5) { bad = $"bar {bar}: bar x {string.Join("/", bx.Select(v => v.ToString("0.0")))}"; break; }
            if (z.Max() - z.Min() > 1e-6) bad = $"bar {bar}: zoom {string.Join("/", z)}";
            else if (o.Max() - o.Min() > 1e-6) bad = $"bar {bar}: offset {string.Join("/", o)}";
            else if (x.Max() - x.Min() > 2) bad = $"bar {bar}: playhead x {string.Join("/", x.Select(v => v.ToString("0")))}";
            if (bad != "") break;
        }
        Check("band page horizontal: same scale, same first bar, playheads within 2 px", bad == "", bad);
    }

    private static void TestBandRowsSamePageDemo()
    {
        var project = TabForge.Presets.FullDemoSongFactory.Create();
        var host = new FakeBandHost(project);
        host.Settings.Timeline.Band = new BandSettings { WidthPerRow = true, LaneLayout = BandChoices.Horizontal };
        host.Settings.Follow.Mode = FollowModes.Jump;
        using var band = new BandViewController(host);
        BandStage(band, 2000, 1100);
        band.Tick();
        ClickPill(band, 3);
        band.ChangeRowsPerScreen(1);
        band.Tick();
        BandStage(band, 2000, 1100);
        band.State.SetWidth(project.Tracks[0], 300);
        band.State.SetWidth(project.Tracks[1], 520);
        band.State.SetWidth(project.Tracks[2], 400);
        band.Tick();
        BandStage(band, 2000, 1100);
        var lanes = band.View.Rows.Select(r => r.Lane).ToList();
        var bad = "";
        band.View.Rows[2].RequestHeight(140);
        BandStage(band, 2000, 1100);
        band.View.Rows[2].RequestReset();
        BandStage(band, 2000, 1100);
        BandStage(band, 1700, 1000);
        BandStage(band, 2000, 1100);
        for (var step = 0; step < 100 && bad == ""; step++)
        {
            var bar = 84 + step / 10;
            band.Apply(bar, step % 10 / 10.0, bar * 2000.0, true, false);
            BandStage(band, 2000, 1100);
            new System.Windows.Media.Imaging.RenderTargetBitmap(2000, 1100, 96, 96, System.Windows.Media.PixelFormats.Pbgra32).Render(band.View);
            band.Tick();
            BandStage(band, 2000, 1100);
            band.Apply(bar, step % 10 / 10.0, bar * 2000.0, true, false);
            var z = lanes.Select(l => l.Editor.Zoom).ToList();
            var o = lanes.Select(l => l.Offset).ToList();
            var bx = lanes.Select(l => l.Editor.Layout.GetLayout(l.Editor.Track).Measure(bar).X * l.Editor.Zoom).ToList();
            if (z.Max() - z.Min() > 1e-6) bad = $"bar {bar}: zoom {string.Join("/", z.Select(v => v.ToString("0.000")))}";
            else if (o.Max() - o.Min() > 1e-6) bad = $"bar {bar}: offset {string.Join("/", o)}";
            else if (bx.Max() - bx.Min() > 0.5) bad = $"bar {bar}: bar x {string.Join("/", bx.Select(v => v.ToString("0.0")))}";
        }
        Check("band demo: four rows share the scale, the first bar and the bar positions", lanes.Count == 4 && bad == "", lanes.Count + " rows " + bad);
    }
}
