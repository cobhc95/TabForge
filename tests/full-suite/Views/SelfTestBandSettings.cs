using System.IO;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;
using TabForge.Views.Band;
using TabForge.Visualization;

namespace TabForge;

/// <summary>Band view stage 3: the layout is saved with the song (.tforge and .gp), the Band preferences apply, notation lanes are cached, a held row scrolls the list at the edge.</summary>
public static partial class SelfTest
{
    /// <summary>The Band layout as text: shown rows in order, then every row's own height, then rows per screen.</summary>
    private static string BandLayoutText(BandViewController band) =>
        string.Join(",", band.State.Order.Select(t => t.Name + (band.State.IsShown(t) ? "+" : "-") + (band.State.HeightOf(t) is { } h ? ":" + h.ToString("0") : "")))
        + "|" + band.State.RowsPerScreen;

    private static void TestBandLayoutSaved()
    {
        var project = BandSongOf(8, 5);
        string expected;
        using (var band = new BandViewController(new FakeBandHost(project)))
        {
            BandStage(band, 900, 800);
            band.Tick();
            BandStage(band, 900, 800);
            ClickPill(band, 4);                    // Extra 4 on
            ClickPill(band, 0);                    // Guitar off
            band.State.MoveShown(project.Tracks[4], 0);
            band.ChangeRowsPerScreen(1);
            band.View.Rows[1].RequestHeight(210);
            expected = BandLayoutText(band);
            Check("band saved: the layout is held by the song", project.BandLayout is { RowsPerScreen: 4 } && project.BandLayout.Heights.Count == 1, expected);
        }
        var folder = Path.Combine(Path.GetTempPath(), "tf-band-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var tforge = Path.Combine(folder, "band.tforge");
            ProjectService.Save(tforge, project);
            var gp = Path.Combine(folder, "band.gp");
            GuitarProExporter.Save(project, gp);
            // Band data lives only in the embedded TabForge project: every other entry of the .gp is the same with and without it.
            var layout = project.BandLayout;
            project.BandLayout = null;
            var plain = Path.Combine(folder, "plain.gp");
            GuitarProExporter.Save(project, plain);
            project.BandLayout = layout;
            Check("band saved: .gp entries outside the embedded TabForge project are unchanged by band data", GpOuterEntries(gp).SequenceEqual(GpOuterEntries(plain)) && GpOuterEntries(gp).Count > 1, string.Join(" / ", GpOuterEntries(gp).Except(GpOuterEntries(plain))));
            foreach (var (name, loaded) in new[] { (".tforge", ProjectService.Load(tforge)), (".gp", GuitarProExporter.TryReadEmbedded(gp)!) })
            {
                Check($"band saved: the {name} file carries the layout", loaded?.BandLayout is not null);
                if (loaded is null) continue;
                using var band = new BandViewController(new FakeBandHost(loaded));
                BandStage(band, 900, 800);
                band.Tick();
                BandStage(band, 900, 800);
                Eq($"band saved: pills, order, heights and rows per screen come back from {name}", expected, BandLayoutText(band));
                Check($"band saved: the rows come back from {name}", band.View.Rows.Select(r => r.Track.Name).SequenceEqual(new[] { "Extra 4", "Bass", "Drums" }) && Math.Abs(band.View.Rows.First(r => r.Track.Name == "Drums").Height - 210) < 1);
            }
            // A song saved before the layout existed opens with the first three rows.
            var old = new SongProject { Tracks = project.Tracks };
            using var fresh = new BandViewController(new FakeBandHost(old));
            BandStage(fresh, 900, 800);
            fresh.Tick();
            Check("band saved: a song with no saved layout shows its first three tracks", old.BandLayout is null && fresh.State.ShownTracks().SequenceEqual(old.Tracks.Take(3)) && fresh.State.RowsPerScreen == 3);
            // Ids of removed tracks and wild values in a file are ignored or clamped.
            var wild = new SongProject { Tracks = project.Tracks.Take(2).ToList() };
            wild.BandLayout = new BandLayoutData
            {
                Order = { Guid.NewGuid(), wild.Tracks[1].Id }, Shown = { Guid.NewGuid(), wild.Tracks[1].Id },
                Heights = { [wild.Tracks[0].Id] = double.NaN, [wild.Tracks[1].Id] = 1e9 }, RowsPerScreen = 99
            };
            using var odd = new BandViewController(new FakeBandHost(wild));
            odd.Tick();
            Check("band saved: unknown ids are ignored and values clamped", odd.State.Order.Count == 2 && odd.State.Order[0] == wild.Tracks[1] && odd.State.ShownTracks().Count == 1
                && odd.State.HeightOf(wild.Tracks[0]) is null && odd.State.HeightOf(wild.Tracks[1]) <= 4000 && odd.State.RowsPerScreen == BandLayoutState.MaxRowsPerScreen);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    private static List<string> GpOuterEntries(string path)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(path);
        var list = new List<string>();
        foreach (var entry in zip.Entries.Where(e => !e.FullName.StartsWith("TabForge/", StringComparison.Ordinal)).OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            using var stream = entry.Open();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            // The exporter's score part varies from run to run even for the same song, so it is checked for band data instead of compared.
            if (entry.FullName.EndsWith("score.gpif", StringComparison.Ordinal))
            {
                var text = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                list.Add(entry.FullName + ":" + (text.Contains("BandLayout", StringComparison.Ordinal) || text.Contains("RowsPerScreen", StringComparison.Ordinal) ? "has band data" : "clean"));
            }
            else list.Add(entry.FullName + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(ms.ToArray())));
        }
        return list;
    }

    private static void TestBandLayoutSafety()
    {
        var project = BandSongOf(8, 5);
        var host = new FakeBandHost(project);
        using var band = new BandViewController(host);
        BandStage(band, 900, 800);
        band.Tick();
        var clean = ProjectService.ContentHash(project);
        ClickPill(band, 4);
        band.ChangeRowsPerScreen(1);
        band.View.Rows[0].RequestHeight(190);
        Check("band safety: a layout change does not change the unsaved-changes hash", project.BandLayout is not null && ProjectService.ContentHash(project).AsSpan().SequenceEqual(clean));
        var layout = BandLayoutText(band);

        // Undo: an earlier state restores the music but never rewinds the layout.
        var encoder = new TabForge.Documents.ProjectStateEncoder();
        var state = encoder.Encode(project);
        ClickPill(band, 3);
        project.Tracks[0].Measures[0].Cells[0].Notes.Clear();
        var edited = BandLayoutText(band);
        var restored = encoder.Restore(state, project);
        Check("band safety: undo keeps the live layout", ReferenceEquals(restored.BandLayout, project.BandLayout) && restored.Tracks[0].Measures[0].Cells[0].Notes.Count == 1);
        using var after = new BandViewController(new FakeBandHost(restored));
        BandStage(after, 900, 800);
        after.Tick();
        Check("band safety: the restored song shows the layout from before the undo", BandLayoutText(after) == edited && edited != layout, edited);

        // Load: a corrupted block opens with the default layout; oversized lists are cut.
        var folder = Path.Combine(Path.GetTempPath(), "tf-bandsafe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "bad.tforge");
            var plain = new SongProject { Title = "Safe", Tracks = project.Tracks.Take(3).ToList() };
            ProjectService.Save(path, plain);
            var json = ProjectService.ReadJsonText(path);
            foreach (var bad in new[] { "\"BandLayout\":{\"Order\":\"x\",\"Shown\":5,\"Heights\":[1],\"RowsPerScreen\":\"many\"}", "\"BandLayout\":[1,2]", "\"BandLayout\":{\"Heights\":{\"not-a-guid\":1}}" })
            {
                var corrupt = json.Replace("\"BandLayout\":null", bad, StringComparison.Ordinal);
                Check("band safety: the test file has the block", corrupt != json);
                File.WriteAllText(path, corrupt);
                var loaded = ProjectService.Load(path);
                Check("band safety: a corrupted layout block opens with the default layout", loaded.Title == "Safe" && loaded.Tracks.Count == 3 && loaded.BandLayout is null);
            }
            var many = new BandLayoutData();
            for (var i = 0; i < 50; i++) { many.Order.Add(Guid.NewGuid()); many.Shown.Add(Guid.NewGuid()); many.Heights[Guid.NewGuid()] = 100; }
            plain.BandLayout = many;
            ProjectService.Save(path, plain);
            var capped = ProjectService.Load(path).BandLayout!;
            Check("band safety: the lists are cut to the track count", capped.Order.Count == 3 && capped.Shown.Count == 3 && capped.Heights.Count == 3);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    private static void TestBandSettings()
    {
        var project = BandSongOf(8, 4);
        var host = new FakeBandHost(project);
        using var band = new BandViewController(host);
        BandStage(band, 900, 800);
        band.Tick();
        BandStage(band, 900, 800);
        var settings = host.Settings.Timeline.Band;
        Check("band settings: defaults are tab, full neck, smooth follow, three rows, own order",
            settings is { LaneContent: BandChoices.Tab, InstrumentSize: BandChoices.FullNeck, SmoothFollow: true, RowsPerScreen: 3, KeepOrderInSync: false });
        var catalog = SettingsCatalog.Build(host.Settings).Select(d => d.Key).ToHashSet();
        Check("band settings: the Preferences page lists them", new[] { "band.instrumentsize", "band.lanecontent", "band.followscore", "band.smoothfollow", "band.rowsperscreen", "band.syncorder" }.All(catalog.Contains));
        var bad = new AppSettings { Timeline = new TimelineSettings { Band = new BandSettings { InstrumentSize = "x", LaneContent = "y", RowsPerScreen = 40 } } };
        SettingsValidator.Normalize(bad);
        Check("band settings: a bad file value falls back", bad.Timeline.Band is { InstrumentSize: BandChoices.FullNeck, LaneContent: BandChoices.Tab, RowsPerScreen: 5 });

        // Lane content.
        Check("band settings: lanes start as tab only", band.View.Rows.All(r => r.Lane.Editor.Notation == NotationMode.TabOnly));
        settings.LaneContent = BandChoices.Notation;
        band.Tick();
        Check("band settings: Notation shows the staff only", band.View.Rows.All(r => r.Lane.Editor.Notation == NotationMode.StaffOnly));
        Check("band settings: the lane-content command cycles", band.Run("Band.CycleLaneContent") && settings.LaneContent == BandChoices.Both && band.View.Rows.All(r => r.Lane.Editor.Notation == NotationMode.TabAndStaff));
        band.Run("Band.CycleLaneContent");
        Eq("band settings: and returns to tab", BandChoices.Tab, settings.LaneContent);

        // Instrument size.
        settings.InstrumentSize = BandChoices.TwelveFrets;
        band.Tick();
        var guitar = band.View.Rows.First(r => r.Track.Kind == TrackKind.Guitar);
        Eq("band settings: 12 frets limits the neck", 12, ShownStateOf(guitar.Instrument)?.DisplayFrets);
        project.Tracks[3].Kind = TrackKind.Keys;
        ClickPill(band, 3);
        settings.InstrumentSize = BandChoices.SmallKeyboard;
        band.Tick();
        BandStage(band, 900, 800);
        band.Tick();
        var keys = band.View.Rows.First(r => r.Track == project.Tracks[3]);
        Eq("band settings: a small keyboard has fewer keys", BandChoices.SmallKeys, ShownStateOf(keys.Instrument)?.KeyboardKeys);
        settings.InstrumentSize = BandChoices.FullNeck;
        band.Tick();
        Eq("band settings: full neck shows 24 frets", 24, ShownStateOf(guitar.Instrument)?.DisplayFrets);
        Eq("band settings: full size keeps the keyboard setting", host.Settings.Editing.KeyboardKeys, ShownStateOf(keys.Instrument)?.KeyboardKeys);
        ClickPill(band, 3);

        // Follow mode: smooth keeps the playhead at a fixed place, page mode holds the strip until the playhead leaves the page.
        var lane = band.View.Rows[0].Lane;
        band.Apply(6, 0.5, 12500, true, false);
        var smoothOffset = lane.Offset;
        settings.FollowLikeScore = false;
        settings.SmoothFollow = false;
        band.Tick();
        band.Apply(6, 0.5, 12500, true, false);
        var pageOffset = lane.Offset;
        band.Apply(6, 0.55, 12700, true, false);
        Check("band settings: page follow holds the strip while the playhead moves inside the page", Math.Abs(lane.Offset - pageOffset) < 1e-6, $"{smoothOffset:0} {pageOffset:0} {lane.Offset:0}");
        Check("band settings: the follow command toggles", band.Run("Band.ToggleSmoothFollow") && settings.FollowsScore);

        // Rows per screen: the setting is the default of a song without its own.
        settings.RowsPerScreen = 5;
        band.Tick();
        Eq("band settings: the rows-per-screen setting applies to a song without its own", 5, band.State.RowsPerScreen);
        band.ChangeRowsPerScreen(-2);
        settings.RowsPerScreen = 2;
        band.Tick();
        Eq("band settings: a song's own rows per screen is kept", 3, band.State.RowsPerScreen);

        // Row heights reset.
        band.View.Rows[0].RequestHeight(222);
        band.Run("Band.ResetRowHeights");
        Check("band settings: reset row heights clears them", band.State.HeightOf(band.View.Rows[0].Track) is null);

        // Order sync.
        var names = project.Tracks.Select(t => t.Name).ToList();
        ClickPill(band, 3);
        band.Tick();
        band.State.MoveShown(project.Tracks[3], 0);
        Check("band settings: sync off keeps the song's order", project.Tracks.Select(t => t.Name).SequenceEqual(names));
        settings.KeepOrderInSync = true;
        band.Tick();
        Check("band settings: sync on puts the Band in the timeline's order", band.State.Order.SequenceEqual(project.Tracks));
        var rows = band.View.Rows;
        band.View.Reorder.Begin(rows[0], 10);
        band.View.Reorder.Move(10 + rows[0].ActualHeight * 2);
        band.View.Reorder.End(false);
        Check("band settings: with sync on, dropping a row moves the song's track too", !project.Tracks.Select(t => t.Name).SequenceEqual(names) && band.State.Order.SequenceEqual(project.Tracks));
        project.MoveTrack(project.Tracks.Count - 1, 0);
        band.Tick();
        Check("band settings: with sync on, moving a track on the timeline moves its Band row", band.State.Order.SequenceEqual(project.Tracks));
    }

    private static void TestBandLaneContent()
    {
        var project = BandSong(40);
        var host = new FakeBandHost(project);
        host.Settings.Timeline.Band.LaneContent = BandChoices.Both;
        using var band = new BandViewController(host);
        var engravings = 0;
        TabEditorControl.RenderFaultInjection = _ => engravings++;
        try
        {
            BandStage(band, 900, 720);
            band.Tick();
            BandStage(band, 900, 720);
            BandStage(band, 900, 720);
            var first = engravings;
            Check("band lane content: Both is engraved on first layout", first >= project.Tracks.Count && band.View.Rows.All(r => r.Lane.Editor.Notation == NotationMode.TabAndStaff), first.ToString());
            var lane = band.View.Rows[0].Lane;
            Check("band lane content: a Both lane still fits its row", lane.ActualHeight > 20 && lane.Editor.Zoom > 0 && lane.Editor.SystemHeightNow * 1.0 <= lane.ActualHeight / 0.97 + 1, $"{lane.Editor.SystemHeightNow:0} in {lane.ActualHeight:0}");
            for (var frame = 0; frame < 240; frame++)
            {
                var bar = frame / 12;
                band.Apply(bar, frame % 12 / 12.0, bar * 2000.0 + frame % 12 * 160, true, false);
                if (frame % 8 == 0) BandStage(band, 900, 720);
            }
            Eq("band lane content: 240 frames of playback add no engraving", first, engravings);
            Check("band lane content: a click maps to a bar and cell", lane.BarCellAt(200) is not null);
            host.Settings.Timeline.Band.LaneContent = BandChoices.Notation;
            band.Tick();
            BandStage(band, 900, 720);
            BandStage(band, 900, 720);
            var staff = engravings;
            Check("band lane content: switching to Notation engraves the staff lanes", staff > first && band.View.Rows.All(r => r.Lane.Editor.Notation == NotationMode.StaffOnly));
            for (var frame = 0; frame < 120; frame++)
            {
                band.Apply(frame / 12, frame % 12 / 12.0, frame * 160.0, true, false);
                if (frame % 8 == 0) BandStage(band, 900, 720);
            }
            Eq("band lane content: Notation lanes are cached too", staff, engravings);
            band.Tick();
            BandStage(band, 900, 720);
            Eq("band lane content: an idle tick engraves nothing", staff, engravings);
        }
        finally { TabEditorControl.RenderFaultInjection = null; }
    }

    private static void TestBandReorderAutoScroll()
    {
        Eq("band autoscroll: nothing in the middle", 0.0, BandReorder.AutoScrollStep(200, 400));
        Check("band autoscroll: near the top scrolls up, faster at the edge", BandReorder.AutoScrollStep(30, 400) < 0 && BandReorder.AutoScrollStep(0, 400) < BandReorder.AutoScrollStep(30, 400));
        Check("band autoscroll: near the bottom scrolls down, never past the maximum", BandReorder.AutoScrollStep(390, 400) > 0 && BandReorder.AutoScrollStep(900, 400) == BandReorder.MaxScrollStep);

        var project = BandSongOf(8, 6);
        var host = new FakeBandHost(project);
        using var band = new BandViewController(host);
        BandStage(band, 900, 500);
        band.Tick();
        for (var i = 3; i < 6; i++) ClickPill(band, i);
        BandStage(band, 900, 500);
        var reorder = band.View.Reorder;
        var rows = band.View.Rows;
        Check("band autoscroll: the list scrolls", band.View.ScrollableHeight > 100, band.View.ScrollableHeight.ToString("0"));
        Check("band autoscroll: nothing happens without a drag", !reorder.AutoScroll(band.View.Viewport));
        reorder.Begin(rows[0], 10);
        reorder.Move(40);
        Check("band autoscroll: a held row in the middle does not scroll", reorder.Dragging && !reorder.AutoScroll(band.View.Viewport / 2) && band.View.ScrollOffset == 0);
        var target = reorder.Target;
        var scrolled = reorder.AutoScroll(band.View.Viewport - 4);
        BandStage(band, 900, 500);
        Check("band autoscroll: the pointer at the bottom edge scrolls the list", scrolled && band.View.ScrollOffset > 0);
        for (var i = 0; i < 40; i++) { BandStage(band, 900, 500); reorder.AutoScroll(band.View.Viewport - 4); }
        Check("band autoscroll: the held row follows and the target moves down", reorder.Target > target && band.View.ScrollOffset <= band.View.ScrollableHeight + 0.5, $"{target} -> {reorder.Target}");
        reorder.End(false);
        Check("band autoscroll: the drop lands at the end", band.State.Order[^1] == project.Tracks[0] || band.State.ShownTracks()[^1] == project.Tracks[0]);
        Check("band autoscroll: a finished drag scrolls no more", !reorder.AutoScroll(band.View.Viewport - 4));
    }

    private static void TestBandEmptyState()
    {
        var empty = new SongProject();
        var host = new FakeBandHost(empty);
        using var band = new BandViewController(host);
        BandStage(band, 900, 500);
        band.Tick();
        Check("band empty: a song with no tracks says so", band.View.EmptyText == BandView.EmptyNoTracks && band.View.EmptyVisible && band.View.Rows.Count == 0);
        var project = BandSongOf(4, 3);
        var other = new FakeBandHost(project);
        using var band2 = new BandViewController(other);
        BandStage(band2, 900, 500);
        band2.Tick();
        for (var i = 0; i < 3; i++) ClickPill(band2, i);
        Check("band empty: with every row hidden it says how to show one", band2.View.EmptyText == BandView.EmptyNoneShown && band2.View.EmptyVisible);
        ClickPill(band2, 0);
        Check("band empty: a shown row hides the message", !band2.View.EmptyVisible);
    }

    /// <summary>A right-click on the Band view opens its menu: lane content, instrument size, rows per screen, follow, zoom and the settings door.</summary>
    private static void TestBandMenu()
    {
        var project = BandSongOf(8, 4);
        var host = new FakeBandHost(project);
        using var band = new BandViewController(host);
        BandStage(band, 900, 800);
        band.Tick();
        var three = band.View.RowHeight;
        ClickPill(band, 0);
        band.Tick();
        Check("band rows: two shown rows share the whole height", band.View.Rows.Count == 2 && band.View.RowHeight > three * 1.4);
        ClickPill(band, 0);
        band.Tick();
        ClickPill(band, 3);
        band.Tick();
        Check("band rows: a 4th shown track fits on the screen too", band.View.Rows.Count == 4 && band.View.RowHeight < three && band.View.RowHeight * 4 <= band.View.Viewport + 1);
        ClickPill(band, 3);
        band.Tick();
        band.View.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Right) { RoutedEvent = System.Windows.UIElement.MouseRightButtonUpEvent });
        Check("band menu: a right-click opens it", band.LastMenu is { Items.Count: > 5 });
        var all = BandMenus.Build(new BandMenuState(BandChoices.Tab, BandChoices.FullNeck, 3, true), _ => "").SelectMany(m => m.Children is { } c ? c.Prepend(m) : new[] { m }).ToList();
        Check("band menu: lanes, instruments, rows, zoom and settings", new[] { BandMenus.Lanes, BandMenus.Instruments, BandMenus.Rows, ScoreMenus.Zoom, BandMenus.Settings }.All(h => all.Any(m => m.Header == h)));
        Check("band menu: the current lane content is ticked", all.Single(m => m.Id == BandMenus.ContentId && m.Checked).Arg == BandChoices.Tab);
        band.RunMenu(all.First(m => m.Id == BandMenus.ContentId && m.Arg == BandChoices.Both));
        band.Tick();
        Check("band menu: Both shows tab and notation", host.Settings.Timeline.Band!.LaneContent == BandChoices.Both && band.View.Rows.All(r => r.Lane.Editor.Notation == NotationMode.TabAndStaff));
        band.RunMenu(all.First(m => m.Id == BandMenus.RowsId && m.Arg == "2"));
        Eq("band menu: rows per screen", 2, band.View.RowsPerScreen);
        band.RunMenu(all.First(m => m.Id == BandMenus.LaneZoomInId));
        Check("band menu: zoom drives the Band lanes, not the score", host.Settings.Timeline.Band!.LaneZoom > 1 && !host.ScoreMenuRuns.Any(m => m.Id == ScoreMenus.ZoomInId));
    }
}
