using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using TabForge.Docking;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Rendering;
using TabForge.Services;
using TabForge.Shell;
using TabForge.Views;
using TabForge.Views.Band;
using TabForge.Visualization;

namespace TabForge;

/// <summary>Band view stage 1: a row per track, lanes engraved once and slid (not redrawn) while playing, a click maps to a bar and cell, and the Band layout holds the Band view and the timeline only.</summary>
public static partial class SelfTest
{
    private sealed class FakeBandHost : IBandViewHost
    {
        public FakeBandHost(SongProject project)
        {
            Project = project;
            Editor = new TabEditorControl { Project = project, SelectedTrackIndex = 0 };
            ActiveDocument = DocumentSession.FromProject(project, null);
        }
        public Window Window { get; } = new();
        public AppSettings Settings { get; } = new() { Timeline = new TimelineSettings { Band = new BandSettings { LaneLayout = BandChoices.Horizontal } } };
        public TabEditorControl Editor { get; }
        public SongProject Project { get; }
        public TrackModel? Selected;
        public TrackModel? SelectedTrack => Selected ?? (Project.Tracks.Count > 0 ? Project.Tracks[0] : null);
        public DocumentSession ActiveDocument { get; }
        public (bool LeftHanded, bool ShowNoteNames, string? Scale, int Horizon) InstrumentOptions => (false, false, null, 4);
        public VisualOptions? Visual => null;
        public (int Track, int Bar, int Cell)? Cursor;
        public void SaveSettings() { }
        public void SetStatus(string text) { }
        public void MoveSongTrack(int from, int to) => Project.MoveTrack(from, to);
        public List<MenuSpec> ScoreMenuRuns { get; } = new();
        public void RunScoreMenu(MenuSpec spec) => ScoreMenuRuns.Add(spec);
        public void ShowCursor(int trackIndex, int bar, int cell) => Cursor = (trackIndex, bar, cell);
    }

    /// <summary>A song of <paramref name="bars"/> bars with a guitar, a bass and a drum track; every beat has a note.</summary>
    private static SongProject BandSong(int bars)
    {
        var project = new SongProject { Tempo = 120, TimeSignatureNumerator = 4, TimeSignatureDenominator = 4 };
        foreach (var (name, kind) in new[] { ("Guitar", TrackKind.Guitar), ("Bass", TrackKind.Bass), ("Drums", TrackKind.Drums) })
        {
            var track = new TrackModel { Name = name, Kind = kind, Measures = TemplateFactory.Measures(bars) };
            foreach (var measure in track.Measures)
                for (var i = 0; i < 4; i++)
                {
                    var cell = measure.Cells[i * 4];
                    cell.DurationDenominator = 4;
                    cell.Notes.Add(new TabNote { StringIndex = 0, Fret = i, MidiValue = 40 + i });
                }
            project.Tracks.Add(track);
        }
        return project;
    }

    private static void BandStage(BandViewController band, double width, double height)
    {
        band.View.Measure(new Size(width, height));
        band.View.Arrange(new Rect(0, 0, width, height));
        band.View.UpdateLayout();
    }

    private static void TestBandViewRows()
    {
        var project = BandSong(8);
        var host = new FakeBandHost(project);
        using var band = new BandViewController(host);
        BandStage(band, 900, 720);
        band.Tick();
        BandStage(band, 900, 720);
        Eq("band: one row per track", project.Tracks.Count, band.View.Rows.Count);
        Check("band: the rows are in track order", band.View.Rows.Select(r => r.Track).SequenceEqual(project.Tracks));
        var height = band.View.RowHeight;
        Check("band: three rows fill the panel", band.View.Rows.All(r => Math.Abs(r.ActualHeight - height) < 1) && 3 * (height + 2) <= 720 + 1, $"{height:0}");
        band.Tick();
        Eq("band: a second tick keeps the rows", 1, band.Rebuilds);
        project.Tracks.Add(new TrackModel { Name = "Lead", Measures = TemplateFactory.Measures(8) });
        band.Tick();
        Eq("band: a new track gets a pill but no row until it is switched on", 4, band.View.Pills.Count);
        Eq("band: the rows stay three", 3, band.View.Rows.Count);
        Eq("band: the rows were rebuilt once for it", 2, band.Rebuilds);
        Check("band: the instruments got their notes", band.InstrumentRefreshes > 0);
    }

    /// <summary>The live view refreshes its instruments at most every 33 ms of wall clock while playing; the frame-driven view (the export's) refreshes on every playing tick.</summary>
    private static void TestBandFrameClock()
    {
        var project = BandSong(8);
        var timeline = RenderSpecBuilder.Compile(project);
        using var live = new BandViewController(new FakeBandHost(project));
        BandStage(live, 900, 720);
        live.Tick();
        BandStage(live, 900, 720);
        Check("band frame clock: the live view runs on the wall clock by default", !live.UseFrameClock);
        // Two playing ticks on one bar: the stopwatch spans both, so a pair under 33 ms also has a gap under 33 ms on the controller's clock.
        var paired = 0;
        var throttled = 0;
        for (var attempt = 0; attempt < 10 && paired < 3; attempt++)
        {
            var sw = Stopwatch.StartNew();
            var before = live.InstrumentRefreshes;
            live.ProbePlay = (attempt + 1, 0.1, 1000, timeline); live.Tick();
            live.ProbePlay = (attempt + 1, 0.2, 1010, timeline); live.Tick();
            var refreshes = live.InstrumentRefreshes - before;
            if (sw.Elapsed.TotalMilliseconds >= 33) continue;
            paired++;
            if (refreshes == 1) throttled++;
        }
        Check("band frame clock: the live view refreshes once for two playing ticks within 33 ms", paired > 0 && throttled == paired, $"{throttled} of {paired} pairs");
        using var frame = new BandViewController(new FakeBandHost(project)) { UseFrameClock = true };
        BandStage(frame, 900, 720);
        frame.Tick();
        BandStage(frame, 900, 720);
        var start = frame.InstrumentRefreshes;
        for (var i = 0; i < 5; i++) { frame.ProbePlay = (3, 0.1 * i, 1000 + 10 * i, timeline); frame.Tick(); }
        Eq("band frame clock: with the frame clock every playing tick refreshes", 5, frame.InstrumentRefreshes - start);
    }

    private static void TestBandLaneCache()
    {
        var project = BandSong(40);
        var host = new FakeBandHost(project);
        using var band = new BandViewController(host);
        var engravings = 0;
        TabEditorControl.RenderFaultInjection = _ => engravings++;   // called once for every system the editors engrave
        try
        {
            BandStage(band, 900, 720);
            band.Tick();
            BandStage(band, 900, 720);
            BandStage(band, 900, 720);
            var first = engravings;
            Check("band lane: the lanes are engraved on first layout", first >= project.Tracks.Count, first.ToString());
            var lane = band.View.Rows[0].Lane;
            var slides = lane.Slides;
            for (var frame = 0; frame < 480; frame++)
            {
                var bar = frame / 12;
                band.Apply(bar, frame % 12 / 12.0, bar * 2000.0 + frame % 12 * 160, true, false);
                if (frame % 8 == 0) BandStage(band, 900, 720);
            }
            var bitmap = new RenderTargetBitmap(900, 720, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(band.View);
            if (Environment.GetEnvironmentVariable("TABFORGE_BAND_PNG") is { Length: > 0 } png)   // optional picture for looking at the rows
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = System.IO.File.Create(png);
                encoder.Save(file);
            }
            Eq("band lane: 480 frames of playback add no engraving", first, engravings);
            Check("band lane: the strip slid with the playhead", lane.Slides - slides > 400 && lane.Offset > 500, $"{lane.Slides - slides} slides, offset {lane.Offset:0}");
            project.MarkTimelineChanged();
            band.Tick();
            band.Tick();
            Eq("band lane: a burst of edits waits for a quiet moment before engraving", first, engravings);
            System.Threading.Thread.Sleep(150);
            band.Tick();
            BandStage(band, 900, 720);
            Check("band lane: an edit engraves the lanes again", engravings > first, $"{engravings} vs {first}");
            var afterEdit = engravings;
            band.Tick();
            BandStage(band, 900, 720);
            Eq("band lane: an idle tick engraves nothing", afterEdit, engravings);
        }
        finally { TabEditorControl.RenderFaultInjection = null; }
    }

    private static void TestBandViewLifecycle()
    {
        var project = BandSong(8);
        var host = new FakeBandHost(project);
        var band = new BandViewController(host);
        BandStage(band, 900, 720);
        band.Tick();
        BandStage(band, 900, 720);
        Check("band: the playhead shows once the lanes have a size", band.View.Rows.All(r => r.Lane.PlayheadVisible));
        host.Editor.Appearance.DarkPaper = !host.Editor.Appearance.DarkPaper;
        band.Tick();
        BandStage(band, 900, 720);
        band.Tick();
        Check("band: a look change keeps the playhead", band.View.Rows.All(r => r.Lane.PlayheadVisible));
        var oldRow = band.View.Rows[0];
        var before = band.Rebuilds;
        project.Tracks[0].Name = "Lead";
        band.Tick();
        Eq("band: a renamed track rebuilds the rows", before + 1, band.Rebuilds);
        Check("band: the new row carries the new name", band.View.Rows[0].Track.Name == "Lead" && !ReferenceEquals(oldRow, band.View.Rows[0]));
        Check("band: a discarded lane lets go of the song", oldRow.Lane.Editor.Project is null);
        project.Tracks[0].ColorHex = "#00FF00";
        band.Tick();
        Eq("band: a new colour rebuilds the rows", before + 2, band.Rebuilds);
        band.Dispose();
        Eq("band: a disposed view has no rows", 0, band.View.Rows.Count);
        Check("band: a disposed view does not tick", !band.IsTicking);

        var closed = DockLayoutController.BuiltInLayout("Band");
        closed.Root = DockWorkspace.Tabs("only-timeline", "timeline");
        Check("band layout: closing the Band view leaves a layout that asks for the score back", DockLayoutController.NeedsScoreBack(closed));
        Check("band layout: the Band layout itself and Compose do not", !DockLayoutController.NeedsScoreBack(DockLayoutController.BuiltInLayout("Band"))
            && !DockLayoutController.NeedsScoreBack(DockLayoutController.BuiltInLayout("Compose")));
    }

    private static void TestBandLaneClick()
    {
        var project = BandSong(40);
        var host = new FakeBandHost(project);
        using var band = new BandViewController(host);
        BandStage(band, 900, 720);
        band.Tick();
        BandStage(band, 900, 720);
        var row = band.View.Rows[1];
        var lane = row.Lane;
        lane.ShowAt(10, 0.5);
        var at = lane.PlayheadStripX - lane.Offset;   // where the line stands in the lane (it follows like the score: page turns by default)
        var hit = lane.BarCellAt(at);
        Check("band click: the playhead's own spot is bar 10, the middle cell", hit is { Bar: 10 } h && Math.Abs(h.Cell - 8) <= 1, hit is { } v ? $"{v.Bar}/{v.Cell}" : "none");
        lane.ShowAt(0, 0);
        var layout = lane.Editor.Layout.GetLayout(project.Tracks[1]);
        var bar = layout.Measure(3);
        var x = (bar.X + bar.Width * 0.9) * lane.Editor.Zoom - lane.Offset;
        Check("band click: near the end of bar 3 is bar 3, a late cell", lane.BarCellAt(x) is { Bar: 3 } late && late.Cell >= 12, lane.BarCellAt(x)?.ToString());
        Check("band click: a lane click reaches the host with the track and position", lane.Click(x) && host.Cursor is { Track: 1, Bar: 3 }, host.Cursor?.ToString());
        project.Tracks[1].Kind = TrackKind.Audio;
        Check("band click: an audio track has no bar to hit", lane.BarCellAt(10) is null);
    }

    private static void TestBandLayoutPreset()
    {
        var band = DockLayoutController.BuiltInLayout("Band");
        var shown = DockWorkspace.PanelsOf(band).OrderBy(p => p, StringComparer.Ordinal).ToList();
        Check("band layout: only the Band view and the timeline", shown.SequenceEqual(new[] { "band", "timeline" }), string.Join(",", shown));
        Check("band layout: the other panels are closed", new[] { "instrument", "tools", "sections", "structure", "rhythm", "layout" }.All(band.ClosedPanels.Contains));
        Check("band layout: Band is a built-in layout name", DockLayoutController.BuiltInLayoutNames.Contains("Band"));
        Check("band layout: a layout with the Band view needs no score", DockLayoutTree.Validate(band, true, _ => true));
        Check("band layout: a layout without the Band view and the score is refused", !DockLayoutTree.Validate(
            new DockWorkspaceState { Root = DockWorkspace.Tabs("only-timeline", "timeline") }, true, _ => true));

        var workspace = new DockWorkspace(new Window());
        workspace.SetEditorContent(new Grid());
        foreach (var (id, host, anchor) in new[]
        {
            ("instrument", "instrument", "score-editor"), ("timeline", "timeline", "score-editor"), ("band", "band", "score-editor"), ("learn", "band", "score-editor"),
            ("tools", "tools", "structure"), ("structure", "tools", "tools"), ("rhythm", "tools", "tools"), ("layout", "tools", "tools"),
            ("sections", "side", "score-editor")
        })
            workspace.RegisterPanel(id, id, new Border(), 180, 100, host, anchor, startsClosed: id is "band" or "learn");
        workspace.RestoreLayout(null);
        Check("band layout: an older layout does not grow a Band view", !workspace.IsPanelVisible("band") && workspace.IsPanelVisible("instrument"));
        workspace.ApplyLayout(band);
        Check("band layout: applying it shows the Band view and the timeline", workspace.IsPanelVisible("band") && workspace.IsPanelVisible("timeline"));
        Check("band layout: applying it hides the fretboard and the side panels", !workspace.IsPanelVisible("instrument") && !workspace.IsPanelVisible("tools") && !workspace.IsPanelVisible("sections"));
        var captured = workspace.CaptureLayout();
        Check("band layout: no score in the tree", !HasEditorNode(captured.Root));
        workspace.SetPanelVisible("tools", true);
        Check("band layout: a panel opened from the Panels menu still appears", workspace.IsPanelVisible("tools"));
        workspace.ApplyLayout(DockLayoutController.BuiltInLayout("Compose"));
        Check("band layout: Compose brings the score back", HasEditorNode(workspace.CaptureLayout().Root) && !workspace.IsPanelVisible("band"));
    }

    private static void TestBandHostForwards()
    {
        var project = BandSong(8);
        var pane = new FakeBandHost(project);
        var documents = new DocumentManager();
        var options = new AppOptions();
        int? selected = null; (int From, int To)? moved = null; (string Category, string? Row)? opened = null;
        IBandViewHost host = new BandHost(pane, documents, options, () => (true, false, "Major", 6),
            (from, to) => moved = (from, to), i => selected = i, () => throw new InvalidOperationException("zoom"), (c, r) => opened = (c, r));
        Check("band host: pane basics come from the window's own pane host", host.Project == project && host.Settings == pane.Settings && host.Editor == pane.Editor && host.Window == pane.Window && host.SelectedTrack == project.Tracks[0]);
        Check("band host: the active song and the visual options are read live", host.ActiveDocument == documents.Active && host.Visual == options.Visual);
        Check("band host: the instrument options come from the delegate", host.InstrumentOptions == (true, false, "Major", 6));
        host.MoveSongTrack(2, 0);
        Check("band host: the order sync moves the song's track", moved == (2, 0));
        host.ShowCursor(1, 3, 4);
        Check("band host: a lane click selects the track and puts the editor on the bar and cell", selected == 1 && pane.Editor.SelectedMeasure == 3 && pane.Editor.SelectedCell == 4, $"{selected} {pane.Editor.SelectedMeasure}/{pane.Editor.SelectedCell}");
        host.RunScoreMenu(new MenuSpec { Id = BandMenus.SettingsId });
        Check("band host: the Band settings item opens Timeline & Tracks at the Band row", opened == (SettingsCatalog.Timeline, BandMenus.SettingsRow));
        opened = null;
        host.RunScoreMenu(new MenuSpec { Id = "other" });
        Check("band host: any other id does nothing", opened is null);
        using var band = new BandViewController(host);
        BandStage(band, 900, 720);
        band.Tick();
        Check("band host: the controller builds its rows through the host", band.View.Rows.Count == 3, band.View.Rows.Count.ToString());
    }

    private static bool HasEditorNode(DockNodeState? node) =>
        node is not null && (node.Kind == "editor" || HasEditorNode(node.First) || HasEditorNode(node.Second));
}
