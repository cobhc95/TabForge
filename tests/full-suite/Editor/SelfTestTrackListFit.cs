using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TabForge.Controllers;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>The track-list dock fits its rows (no empty band) after every kind of resize, and dragging its splitter stretches the rows.</summary>
public static partial class SelfTest
{
    /// <summary>The dock laid out at an explicit size (no top-level window, so the screen size and DPI cannot clamp it).</summary>
    private sealed class FitStage
    {
        private readonly DockWorkspace _workspace; private double _height;
        public FitStage(DockWorkspace workspace, double width, double height) { _workspace = workspace; Width = width; _height = height; }
        public double Width { get; }
        public double Height { get => _height; set { _height = value; UpdateLayout(); } }
        public double ActualWidth => Width;
        public double ActualHeight => _height;
        public void UpdateLayout()
        {
            _workspace.Measure(new Size(Width, _height));
            _workspace.Arrange(new Rect(0, 0, Width, _height));
            _workspace.UpdateLayout();
        }
        public void Render(System.Windows.Media.Imaging.RenderTargetBitmap bitmap) => bitmap.Render(_workspace);
        public void Close() { }
    }

    private sealed class FitHost : ITrackListFitHost
    {
        public TimelineSettings Timeline { get; } = new();
        public ArrangementPanel Arrangement { get; init; } = null!;
        public DockWorkspace Dock { get; init; } = null!;
        public System.Windows.Threading.Dispatcher Dispatcher => System.Windows.Threading.Dispatcher.CurrentDispatcher;
        public int Saves;
        public void SaveSettings() => Saves++;
        public void SetStatus(string text) { }
    }

    /// <summary>Set TABFORGE_TRACKFIT_PNG to a folder to get before/after pictures of the track list from this test.</summary>
    private static void TrackFitPng(FitStage window, string name)
    {
        if (Environment.GetEnvironmentVariable("TABFORGE_TRACKFIT_PNG") is not { Length: > 0 } folder) return;
        Directory.CreateDirectory(folder);
        PumpUi(); window.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        window.Render(bitmap);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(folder, $"timeline-fit-{name}.png"));
        encoder.Save(file);
    }

    private static void TestTrackColumnHeaderFit()
    {
        var rows = TabForge.Views.ArrangementPanel.ColumnMinimumsForTest().ToList();
        foreach (var (id, min, need) in rows)
            Check($"track columns: the minimum width of '{id}' fits its header text", min >= need, $"min {min:0.#} need {need:0.#}");
        Check("track columns: every column was checked", rows.Count == 9, rows.Count.ToString());
    }

    private static void TestTrackListFit()
    {
        var song = new SongProject();
        for (var i = 0; i < 7; i++) song.Tracks.Add(MixerTestTrack("T" + i, TrackKind.Guitar, 30));
        var panel = new ArrangementPanel();
        panel.Bind(song, Array.Empty<Playback.MidiOutputDeviceInfo>());
        var workspace = new DockWorkspace(new Window());
        var window = new FitStage(workspace, 1000, 800);
        workspace.SetEditorContent(new Border());
        workspace.RegisterPanel("timeline", "Arrangement", panel, 440, 112, "timeline", "score-editor");
        workspace.RestoreLayout(null);
        using var alive = KeepAlive();
        try
        {
            window.UpdateLayout();
            PumpUi();
            double Gap() { PumpUi(); workspace.UpdateLayout(); return panel.ActualHeight + TrackListFitController.Chrome - panel.PreferredHeight(); }

            // Before: the old behaviour (a fit only when the tracks change) leaves a band once the window grows.
            workspace.FitPanelHeight("timeline", panel.PreferredHeight());
            var fitGap = Gap();
            window.Height = 1100; window.UpdateLayout();
            var oldGap = Gap();
            TrackFitPng(window, "before-gap");
            Check("track list fit: before the fix, growing the window left empty space under the last track", oldGap > 20, $"{oldGap:0.0}px");
            Log.Add($"  info  track list fit: gap after a fit {fitGap:0.0}px, after growing the window without the fit {oldGap:0.0}px");

            var host = new FitHost { Arrangement = panel, Dock = workspace };
            var fit = new TrackListFitController(host, workspace);
            fit.FitToTracks();
            Check("track list fit: fitting leaves no empty space", Math.Abs(Gap()) < 1.5, $"{Gap():0.0}px");

            window.Height = 1250; window.UpdateLayout(); PumpUi(); fit.Flush();
            Check("track list fit: growing the window leaves no empty space", Math.Abs(Gap()) < 1.5, $"{Gap():0.0}px");
            window.Height = 900; window.UpdateLayout(); PumpUi(); fit.Flush();
            Check("track list fit: shrinking the window leaves no empty space", Math.Abs(Gap()) < 1.5, $"{Gap():0.0}px");

            var saved = workspace.CaptureLayout();
            workspace.RestoreLayout(saved); window.UpdateLayout(); PumpUi(); fit.Flush();
            Check("track list fit: restoring a dock layout leaves no empty space", Math.Abs(Gap()) < 1.5, $"{Gap():0.0}px");

            workspace.LayoutTransform = new ScaleTransform(1.25, 1.25);
            window.UpdateLayout(); PumpUi(); fit.Flush(); PumpUi(); fit.Flush();
            Check("track list fit: a larger UI scale leaves no empty space", Math.Abs(Gap()) < 1.5, $"{Gap():0.0}px");
            workspace.LayoutTransform = Transform.Identity;
            window.UpdateLayout(); PumpUi(); fit.Flush();

            song.Mixer.ShowGroupsInTrackList = true;
            panel.Bind(song, Array.Empty<Playback.MidiOutputDeviceInfo>());
            fit.FitToTracks();
            Check("track list fit: group header rows are counted", Math.Abs(Gap()) < 1.5 && panel.PreferredHeight() > ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight + 7 * ArrangementPanel.DefaultTrackRowHeight, $"{Gap():0.0}px");
            song.Mixer.ShowGroupsInTrackList = false;
            song.Tracks.Add(MixerTestTrack("T7", TrackKind.Guitar, 30));
            panel.Bind(song, Array.Empty<Playback.MidiOutputDeviceInfo>());
            fit.FitToTracks();
            Check("track list fit: adding a track grows the dock to fit", Math.Abs(Gap()) < 1.5, $"{Gap():0.0}px");
            song.Tracks.RemoveAt(7);
            panel.Bind(song, Array.Empty<Playback.MidiOutputDeviceInfo>());
            fit.FitToTracks();

            // While the splitter is dragged the rows keep their height (a drawn shade previews them); releasing it stretches the rows once.
            var defaultPane = panel.PreferredHeight();
            TrackFitPng(window, "default");
            workspace.SimulateSplitterDrag("timeline", defaultPane + 140, complete: false); PumpUi();
            Check("track list fit: while dragging the rows keep their height (no relayout per step)", Math.Abs(panel.TrackRowHeight - ArrangementPanel.DefaultTrackRowHeight) < 0.06, $"{panel.TrackRowHeight:0.0}");
            workspace.SimulateSplitterDrag("timeline", defaultPane + 140, complete: true); PumpUi();
            var stretched = panel.TrackRowHeight;
            Check("track list fit: releasing a taller splitter makes the rows taller", stretched > ArrangementPanel.DefaultTrackRowHeight + 10, $"{stretched:0.0}");
            var heights = panel.TrackRowActualHeights;
            Check("track list fit: the track controls and the timeline lanes stay aligned",
                heights.Count == 7 && heights.All(h => Math.Abs(h - stretched) < 0.6) &&
                Math.Abs(ArrangementPanel.RowTopOf(song, 3) - 3 * stretched) < 0.01 && Math.Abs(ArrangementPanel.RowsHeight(song) - 7 * stretched) < 0.01,
                string.Join(",", heights.Select(h => h.ToString("0.0"))));
            Check("track list fit: after the drag there is no empty space and the height is saved",
                Math.Abs(Gap()) < 1.5 && Math.Abs(host.Timeline.TrackRowHeight - panel.TrackRowHeight) < 0.06 && host.Saves > 0, $"{Gap():0.0}px, saved {host.Timeline.TrackRowHeight:0.0}");

            TrackFitPng(window, "stretched");
            window.Height = 1600; window.UpdateLayout(); PumpUi(); fit.Flush();
            workspace.SimulateSplitterDrag("timeline", 1000, complete: true); PumpUi();
            Check("track list fit: the row height stops at three times the default and the dock snaps to the rows",
                Math.Abs(panel.TrackRowHeight - ArrangementPanel.MaxTrackRowHeight) < 0.06 && Math.Abs(Gap()) < 1.5, $"{panel.TrackRowHeight:0.0}, {Gap():0.0}px");

            workspace.SimulateSplitterDrag("timeline", 100, complete: true); PumpUi();
            Check("track list fit: dragging smaller than the rows stops at the 3-row collapsed height with the default row height",
                Math.Abs(panel.TrackRowHeight - ArrangementPanel.DefaultTrackRowHeight) < 0.06 && Math.Abs(Gap() - (ArrangementPanel.CollapsedPaneHeight - panel.PreferredHeight())) < 1.5, $"{panel.TrackRowHeight:0.0}, {Gap():0.0}px");

            // Persistence and reset.
            host.Timeline.TrackRowHeight = 48; host.Timeline.TrackListHeight = 0;
            panel.SetTrackRowHeight(ArrangementPanel.DefaultTrackRowHeight);
            fit.FitToTracks();
            Check("track list fit: the saved row height is applied and the dock fits it",
                Math.Abs(panel.TrackRowHeight - 48) < 0.06 && Math.Abs(Gap()) < 1.5, $"{panel.TrackRowHeight:0.0}, {Gap():0.0}px");
            fit.ResetRowHeight();
            Check("track list fit: reset returns to the default height and fits",
                Math.Abs(panel.TrackRowHeight - ArrangementPanel.DefaultTrackRowHeight) < 0.06 && Math.Abs(host.Timeline.TrackRowHeight - ArrangementPanel.DefaultTrackRowHeight) < 0.06 && Math.Abs(Gap()) < 1.5, $"{panel.TrackRowHeight:0.0}, {Gap():0.0}px");

            // Auto-fit off: nothing moves.
            host.Timeline.AutoFitTrackList = false;
            workspace.SimulateSplitterDrag("timeline", defaultPane + 140, complete: true); PumpUi();
            Check("track list fit: with auto-fit off a drag leaves the row height alone", Math.Abs(panel.TrackRowHeight - ArrangementPanel.DefaultTrackRowHeight) < 0.06, $"{panel.TrackRowHeight:0.0}");
            window.Height = 1900; window.UpdateLayout(); PumpUi(); fit.Flush();
            Check("track list fit: with auto-fit off the window resize is not refitted", Math.Abs(Gap()) > 5, $"{Gap():0.0}px");
        }
        finally { window.Close(); }
        TwoPanelsKeepTheirOwnRowHeight();
        foreach (var (tracks, groups, height) in new[] { (1, false, 1500), (4, false, 1500), (4, true, 1500), (20, false, 1700), (20, false, 800) })
            TrackListDragLimits(tracks, groups, height);
        Check("track list fit: 'Reset track row height' is a bindable command", HotkeyCatalog.All.Any(a => a.Id == "View.ResetTrackRowHeight"));
    }

    private static void TwoPanelsKeepTheirOwnRowHeight()
    {
        SongProject Make() { var p = new SongProject(); for (var i = 0; i < 4; i++) p.Tracks.Add(MixerTestTrack("T" + i, TrackKind.Guitar, 30)); return p; }
        var songA = Make(); var songB = Make();
        var a = new ArrangementPanel(); var b = new ArrangementPanel();
        a.Bind(songA, Array.Empty<Playback.MidiOutputDeviceInfo>());
        b.Bind(songB, Array.Empty<Playback.MidiOutputDeviceInfo>());
        var window = new StackPanel { Children = { a, b } };
        a.Height = 300; b.Height = 300;
        using var alive = KeepAlive();
        try
        {
            window.Measure(new Size(900, 700)); window.Arrange(new Rect(0, 0, 900, 700)); window.UpdateLayout(); PumpUi();
            a.SetTrackRowHeight(50); PumpUi(); window.Measure(new Size(900, 700)); window.Arrange(new Rect(0, 0, 900, 700)); window.UpdateLayout();
            Check("two track lists: stretching one leaves the other's row height alone", Math.Abs(b.TrackRowHeight - ArrangementPanel.DefaultTrackRowHeight) < 0.01 && Math.Abs(a.TrackRowHeight - 50) < 0.01, $"{a.TrackRowHeight} / {b.TrackRowHeight}");
            Check("two track lists: the other one's track rows and timeline lanes stay aligned at the default",
                b.TrackRowActualHeights.All(h => Math.Abs(h - ArrangementPanel.DefaultTrackRowHeight) < 0.6) && Math.Abs(ArrangementPanel.RowTopOf(songB, 2) - 2 * ArrangementPanel.DefaultTrackRowHeight) < 0.01 &&
                a.TrackRowActualHeights.All(h => Math.Abs(h - 50) < 0.6) && Math.Abs(ArrangementPanel.RowTopOf(songA, 2) - 100) < 0.01,
                string.Join(",", b.TrackRowActualHeights.Select(h => h.ToString("0.0"))));
        }
        finally { }
    }

    /// <summary>The splitter stops where all rows fit at the default row height and where they fit at the largest; a too-short window caps at what the other panes allow.</summary>
    private static void TrackListDragLimits(int trackCount, bool groups, int windowHeight)
    {
        var song = new SongProject();
        for (var i = 0; i < trackCount; i++) song.Tracks.Add(MixerTestTrack("T" + i, TrackKind.Guitar, 30));
        song.Mixer.ShowGroupsInTrackList = groups;
        if (groups) { song.Tracks[0].Kind = TrackKind.Bass; if (trackCount > 2) song.Tracks[2].Kind = TrackKind.Keys; }
        var panel = new ArrangementPanel();
        panel.Bind(song, Array.Empty<Playback.MidiOutputDeviceInfo>());
        var workspace = new DockWorkspace(new Window());
        var window = new FitStage(workspace, 1000, windowHeight);
        workspace.SetEditorContent(new Border());
        workspace.RegisterPanel("timeline", "Arrangement", panel, 440, 112, "timeline", "score-editor");
        workspace.RestoreLayout(null);
        var host = new FitHost { Arrangement = panel, Dock = workspace };
        var fit = new TrackListFitController(host, workspace);
        var label = $"track list limits ({trackCount} tracks{(groups ? ", groups" : "")}, window {windowHeight})";
        using var alive = KeepAlive();
        try
        {
            window.UpdateLayout(); PumpUi();
            fit.FitToTracks(); PumpUi();
            double Gap() { PumpUi(); workspace.UpdateLayout(); return panel.ActualHeight + TrackListFitController.Chrome - panel.PreferredHeight(); }
            var minPane = panel.PreferredHeightAt(ArrangementPanel.DefaultTrackRowHeight);
            var maxPane = panel.PreferredHeightAt(ArrangementPanel.MaxTrackRowHeight);
            var tooShort = windowHeight < 1000 && trackCount > 10;
            // Dragging down stops at the 3-row collapsed height (or at the rows when there are fewer than 3).
            var floorGap = Math.Min(ArrangementPanel.CollapsedPaneHeight, minPane) - minPane;
            bool AtFloor() => tooShort || Math.Abs(Gap() - floorGap) < 1.5;

            workspace.SimulateSplitterDrag("timeline", 40, complete: false); PumpUi();
            var heldMin = AtFloor();
            Check($"{label}: dragging down stops where every row fits ({(tooShort ? "window too short: capped by the other panes" : "no clipped row")})", heldMin && (trackCount == 1 ? panel.TrackRowHeight >= ArrangementPanel.DefaultTrackRowHeight : Math.Abs(panel.TrackRowHeight - ArrangementPanel.DefaultTrackRowHeight) < 0.06),   // one row: the pane's own minimum height stretches it a little
                 $"{Gap():0.0}px, rows {panel.TrackRowHeight:0.0}");
            workspace.SimulateSplitterDrag("timeline", 40, complete: true); PumpUi();
            Check($"{label}: no snapping after release at the minimum", AtFloor(), $"{Gap():0.0}px");

            workspace.SimulateSplitterDrag("timeline", 4000, complete: false); PumpUi();
            workspace.SimulateSplitterDrag("timeline", 4000, complete: true); PumpUi();
            if (maxPane + 460 < windowHeight - 40)
                Check($"{label}: releasing a drag up stops where the rows fit at the largest height (no gap)", Math.Abs(Gap()) < 1.5 && Math.Abs(panel.TrackRowHeight - ArrangementPanel.MaxTrackRowHeight) < 0.06, $"{Gap():0.0}px, rows {panel.TrackRowHeight:0.0}");
            else
                Check($"{label}: releasing a drag up never leaves empty space", Gap() < 2.5, $"{Gap():0.0}px");
            // After a window resize and a track change the limits still hold.
            window.Height = windowHeight + 100; window.UpdateLayout(); PumpUi(); fit.Flush();
            workspace.SimulateSplitterDrag("timeline", 40, complete: true); PumpUi();
            Check($"{label}: the limit still holds after the window is resized", AtFloor(), $"{Gap():0.0}px");
        }
        finally { window.Close(); }
    }
}
