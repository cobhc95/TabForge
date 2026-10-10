using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using TabForge.Services;

namespace TabForge.Diagnostics;

// Owns: the `--render-timeline` command and the scenes it draws: playhead and marker style, hover shade, a media drop in three modes, a clip move, track lines, MIDI clips and the scrolled collapsed pane.
// Does not own: the timeline and its drag handling (Views/ArrangementPanel*.cs, Views/TrackTimeline*.cs).
// Tests: no named test.

internal static partial class DiagnosticCommands
{
    /// <summary>
    /// `--render-timeline &lt;song&gt; &lt;out.png&gt; [bar] [style] [hoverBar hoverTrack] [light]`: the arrangement timeline in an off-screen
    /// window, with the playhead at the 1-based <c>bar</c> (selected track 1), the playback position marker style (Line, "Bar marker" or
    /// Both; write Bar-marker as BarMarker) and, when given, the hover shade on the 1-based bar and track. Add <c>light</c> for the light theme.
    /// Add <c>drop</c>, <c>dropnew</c> or <c>dropbelow</c> to draw a two-bar MIDI groove being dragged over the timeline: onto a free stretch
    /// of track 1's lane, onto a stretch where track 1 already has a clip (a new lane opens), or below the last track (a new track).
    /// Add <c>lines</c> to draw the lines between tracks, <c>clips</c> for MIDI clips on tracks 1, 4 and 7, and <c>scroll</c> for the
    /// collapsed three-row pane scrolled down two rows.
    /// </summary>
    private static int RunRenderTimeline(string[] args)
    {
        if (args.Length < 3) return Usage("--render-timeline <song> <out.png> [bar] [Line|BarMarker|Both] [hoverBar hoverTrack] [light] [drop|dropnew|dropbelow|move] [lines] [clips] [scroll]");
        return Guard("Render timeline", () =>
        {
            var outPath = FilePathPolicy.OutputFile(args[2], "timeline render", ".png");
            var project = LoadAny(args[1]);
            var light = args.Any(a => a.Equals("light", StringComparison.OrdinalIgnoreCase));
            var keywords = new[] { "light", "drop", "dropnew", "dropbelow", "move", "mute", "lines", "clips", "scroll" };
            bool Has(string k) => args.Skip(3).Any(a => a.Equals(k, StringComparison.OrdinalIgnoreCase));
            double? mutedDim = null;
            if (args.Skip(3).FirstOrDefault(a => a.StartsWith("dim", StringComparison.OrdinalIgnoreCase)) is { } dimArg && int.TryParse(dimArg[3..], out var dimPercent))
                mutedDim = Math.Clamp(dimPercent, 0, 100) / 100.0;   // `dim35`: the muted-track dimming strength
            if (args.Skip(3).Any(a => a.Equals("mute", StringComparison.OrdinalIgnoreCase)) && project.Tracks.Count > 1) project.Tracks[1].Mute = true;   // `mute`: the second track is drawn muted
            var move = args.Skip(3).Any(a => a.Equals("move", StringComparison.OrdinalIgnoreCase));
            var drop = args.Skip(3).FirstOrDefault(a => a.StartsWith("drop", StringComparison.OrdinalIgnoreCase))?.ToLowerInvariant();
            var positional = args.Skip(3).Where(a => !keywords.Contains(a, StringComparer.OrdinalIgnoreCase) && !a.StartsWith("dim", StringComparison.OrdinalIgnoreCase)).ToArray();
            var bar = positional.Length > 0 && int.TryParse(positional[0], out var b) ? Math.Max(1, b) - 1 : 4;
            var style = positional.Length > 1 ? Services.PlayheadStyles.Normalize(positional[1].Replace("BarMarker", "Bar marker", StringComparison.OrdinalIgnoreCase)) : Services.PlayheadStyles.Line;
            int hoverBar = -1, hoverTrack = -1;
            if (positional.Length > 3 && int.TryParse(positional[2], out var hb) && int.TryParse(positional[3], out var ht)) { hoverBar = hb - 1; hoverTrack = ht - 1; }

            var appearance = new AppearanceSettings();
            ThemeService.ApplyPreset(appearance, light ? "Light" : "Dark");
            ThemeService.Apply(appearance);

            if (drop is not null || move) AddDropRenderClip(project);
            if (Has("clips")) foreach (var t in new[] { 0, 3, 6 }.Where(t => t < project.Tracks.Count)) AddDropRenderClip(project, t);
            var panel = new Views.ArrangementPanel { PlayheadStyle = style, ShowTrackLines = Has("lines") };
            if (mutedDim is { } dim) panel.ViewOptions = new Visualization.VisualOptions { MutedDim = dim };
            panel.Bind(project, Array.Empty<Playback.MidiOutputDeviceInfo>());
            var clock = new Audio.SongClock(Audio.AudioEngineClient.Instance);
            panel.SetSongTime(b => clock.BarStartSec(project, b), sec => clock.BarAt(project, sec));
            var width = 1300; var height = (int)Math.Min(560, 40 + 24 + Views.ArrangementPanel.RowsHeight(project) + (drop == "dropbelow" ? 110 : 60));
            if (Has("scroll")) height = (int)Views.ArrangementPanel.CollapsedPaneHeight + 40;
            var window = new Window
            {
                Content = panel, Width = width, Height = height, ShowInTaskbar = false, ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000,
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            };
            window.SetResourceReference(Control.BackgroundProperty, "WindowBrush");
            try
            {
                window.Show();
                Pump(); window.UpdateLayout(); Pump();
                panel.SetSelectedTrack(0);
                panel.SetPlayhead(Math.Clamp(bar, 0, Math.Max(0, (project.Tracks.FirstOrDefault()?.Measures.Count ?? 1) - 1)), 0.5);
                if (hoverBar >= 0 && hoverTrack >= 0 && panel.CellCentre(hoverBar, hoverTrack) is { } centre) panel.SimulateHover(centre);
                if (drop is not null) SimulateDropForRender(panel, project, drop);
                if (move) SimulateMoveForRender(panel, project);
                if (Has("scroll"))
                    panel.LanesScroll.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent });
                window.UpdateLayout(); Pump();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                FilePathPolicy.WriteAtomically(outPath, encoder.Save);
            }
            finally { window.Close(); }
            return Ok;
        });
    }

    /// <summary>The render's existing clip: a MIDI clip on track 1 over bars 3-6, so a drop there needs a new lane.</summary>
    internal static void AddDropRenderClip(Models.SongProject project, int trackIndex = 0)
    {
        if (project.Tracks.Count <= trackIndex) return;
        var clock = new Audio.SongClock(Audio.AudioEngineClient.Instance);
        var start = clock.BarStartSec(project, 2);
        var end = clock.BarStartSec(project, 6);
        var notes = Enumerable.Range(0, 16).Select(i => new Models.ClipNote(i * (end - start) / 16, (end - start) / 20, 40 + i % 5 * 3, 100)).ToList();
        var track = project.Tracks[trackIndex];
        track.AudioClips.Add(new Models.AudioClip { Name = "Existing take", StartSec = start, SourceLengthSec = end - start, FileLengthSec = end - start, Notes = notes });
        Models.ClipLanes.Ensure(track, 1);
    }

    /// <summary>The render's clip dragged (moved, with the faint original and the ghost) to the second track's lane at bar 9.</summary>
    internal static void SimulateMoveForRender(Views.ArrangementPanel panel, Models.SongProject project)
    {
        if (project.Tracks.Count < 2 || project.Tracks[0].AudioClips.Count == 0) return;
        var timeline = panel.TimelineForTest;
        var gridTop = Views.ArrangementPanel.RulerHeight + Views.ArrangementPanel.SectionHeight;
        var clip = project.Tracks[0].AudioClips[^1];
        var press = new Point(timeline.XOfBar(3) + 10, gridTop + Views.ArrangementPanel.RowTopOf(project, 0) + Views.ArrangementPanel.DefaultTrackRowHeight + 12);
        var to = new Point(timeline.XOfBar(8) + 10, gridTop + Views.ArrangementPanel.RowTopOf(project, 1) + 10);
        panel.SimulateClipMove(clip, 0, press, to);
    }

    /// <summary>A two-bar MIDI groove dragged over the timeline (drop, dropnew or dropbelow), drawn without fades.</summary>
    internal static void SimulateDropForRender(Views.ArrangementPanel panel, Models.SongProject project, string mode)
    {
        var groove = new MidiFileData
        {
            Ppq = 480, LengthTicks = 480 * 8,
            Notes = Enumerable.Range(0, 16).Select(i => new MidiFileNote(i * 240, i * 240 + 120, 9, i % 4 == 0 ? 36 : i % 4 == 2 ? 38 : 42, 100)).ToList(),
        };
        var session = Views.MediaDropSession.FromItems(new[] { new DropItem { Path = "Groove 01.mid", Name = "Groove 01", Kind = DropItemKind.Midi, Midi = groove } });
        var timeline = panel.TimelineForTest;
        var gridTop = Views.ArrangementPanel.RulerHeight + Views.ArrangementPanel.SectionHeight;
        var laneY = gridTop + Views.ArrangementPanel.RowTopOf(project, 0) + Views.ArrangementPanel.DefaultTrackRowHeight + 12;
        var point = mode switch
        {
            "dropnew" => new Point(timeline.XOfBar(3) + 3, laneY),
            "dropbelow" => new Point(timeline.XOfBar(5) + 3, gridTop + Views.ArrangementPanel.RowsHeight(project) + 20),
            _ => new Point(timeline.XOfBar(8) + 3, laneY),
        };
        panel.SimulateMediaDrag(session, point);
    }

    private static void Pump() =>
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => { }));
}
