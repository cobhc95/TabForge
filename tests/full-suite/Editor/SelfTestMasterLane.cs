using System.Windows;
using System.Windows.Controls;
using TabForge.Controllers;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>The master track lane: an overview row above track 1 (part of <see cref="SelfTest"/>).</summary>
public static partial class SelfTest
{
    /// <summary>Lines per track, the bars each line fills, the one-row shift of the track rows, hit testing and the menu items.</summary>
    private static void TestMasterLane()
    {
        var defaults = new AppSettings().Timeline;
        Check("master lane: off by default, 25% of a neutral grey", !defaults.ShowMasterTrack && defaults.MasterTrackOpacityPercent == 25 && defaults.MasterTrackColour == MasterLane.DefaultColour);

        var song = new SongProject();
        var tracks = new TrackController();
        for (var i = 0; i < 3; i++) song.Tracks.Add(tracks.CreateTrack(song, TrackKind.Guitar));
        if (song.Tracks.Any(t => t.Measures.Count < 8)) { Check("master lane: the test song has 8 bars", false, $"{song.Tracks[0].Measures.Count}"); return; }
        void Fill(int track, int from, int to)
        {
            for (var b = from; b <= to; b++) song.Tracks[track].Measures[b].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 3, MidiValue = 67 });
        }
        Fill(0, 1, 5); Fill(1, 2, 7); Fill(2, 3, 6);   // bars 2-6, 3-8 and 4-7

        var panel = new ArrangementPanel();
        panel.Bind(song, Array.Empty<Playback.MidiOutputDeviceInfo>());
        var window = new Window { Content = panel, Width = 1000, Height = 500 };
        using var alive = KeepAlive();
        try
        {
            ShowTestWindow(window);
            var timeline = panel.TimelineForTest;
            var gridTop = ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight;
            var row = ArrangementPanel.RowHeightFor(song);
            var offTops = Enumerable.Range(0, 3).Select(i => ArrangementPanel.RowTopOf(song, i)).ToArray();
            var offRows = ArrangementPanel.RowsHeight(song);
            var offHeight = panel.PreferredHeight();
            Check("master lane: hidden, there is no lane and track 1 starts at the top", timeline.MasterLaneLineCount == 0 && panel.MasterLaneRow is null && offTops[0] == 0 && timeline.TrackAt(gridTop + 3) == 0);
            Check("master lane: the empty-area menu offers it, unchecked", panel.BuildEmptyAreaMenu().Items.OfType<MenuItem>().Any(m => m.Header as string == "Show master track" && m.IsCheckable && !m.IsChecked));

            Check("master lane: the row menu's item turns it on from the current state", panel.RunViewCommand(new MenuSpec { Id = MasterLane.CommandId, Checked = false }) && panel.ShowMasterLane);
            window.UpdateLayout(); PumpUi();
            Check("master lane: one line per track", timeline.MasterLaneLineCount == 3);
            Check("master lane: each line fills its track's bars", timeline.MasterLaneBars(0).SequenceEqual(Enumerable.Range(1, 5)) && timeline.MasterLaneBars(1).SequenceEqual(Enumerable.Range(2, 6)) && timeline.MasterLaneBars(2).SequenceEqual(Enumerable.Range(3, 4)),
                string.Join(" | ", Enumerable.Range(0, 3).Select(t => string.Join(",", timeline.MasterLaneBars(t)))));
            Check("master lane: bar 1 is empty on every line", Enumerable.Range(0, 3).All(t => !timeline.MasterLaneHasContent(t, 0)));
            // Hover: the strip shades its bar over the whole strip as no track; a track lane still reports its track; leaving clears it.
            timeline.SimulateHover(new Point(timeline.XOfBar(3) + 5, gridTop + 4));
            var onStrip = timeline.HoverCellAt(new Point(timeline.XOfBar(3) + 5, gridTop + 4));
            var stripShade = panel.HoverCellBounds;
            Check("master lane: hovering the strip shades bar 3 over the whole strip, as no track", onStrip == (3, TrackTimeline.MasterHoverTrack)
                && stripShade is { } sb && Math.Abs(sb.Height - (MasterLane.OffsetOf(song) - 2)) < 0.5 && Math.Abs(sb.X - timeline.XOfBar(3)) < 1.5, $"{onStrip} {stripShade}");
            timeline.SimulateHover(new Point(timeline.XOfBar(3) + 5, gridTop + row + 10));
            Check("master lane: hovering track 1's lane still reports track 1", timeline.HoverCellAt(new Point(timeline.XOfBar(3) + 5, gridTop + row + 10)) == (3, 0)
                && panel.HoverCellBounds is { } tb && Math.Abs(tb.Height - (row - 2)) < 0.5, $"{panel.HoverCellBounds}");
            timeline.SimulateHover(new Point(5, 5));
            Check("master lane: moving off clears the hover shade", panel.HoverCellBounds is null);
            // Colours: with track colours on, each line is its track's block colour; with them off, every line is the one colour.
            var trackColours = timeline.MasterLaneByTrack && timeline.MasterLaneLineColour(0) == timeline.TrackBlockColour(0) && timeline.MasterLaneLineColour(1) == timeline.TrackBlockColour(1);
            Check("master lane: with track colours on, line 1 is track 1's colour and differs from line 2 when the tracks differ",
                trackColours && (timeline.TrackBlockColour(0) == timeline.TrackBlockColour(1) || timeline.MasterLaneLineColour(0) != timeline.MasterLaneLineColour(1)),
                $"{timeline.MasterLaneLineColour(0)} {timeline.MasterLaneLineColour(1)}");
            timeline.MasterLaneByTrack = false; timeline.MasterLaneColour = System.Windows.Media.Colors.Red;
            Check("master lane: with track colours off, every line uses the master colour", timeline.MasterLaneLineColour(0) == System.Windows.Media.Colors.Red && timeline.MasterLaneLineColour(2) == System.Windows.Media.Colors.Red);
            timeline.MasterLaneByTrack = true; timeline.MasterLaneColour = null;
            Check("master lane: every track row moves down by one row", Enumerable.Range(0, 3).All(i => Math.Abs(ArrangementPanel.RowTopOf(song, i) - offTops[i] - row) < 0.01)
                && Math.Abs(ArrangementPanel.RowsHeight(song) - offRows - row) < 0.01 && Math.Abs(panel.PreferredHeight() - offHeight - row) < 0.01);
            Check("master lane: a point in track 1's row is still track 1, the lane is no track", timeline.TrackAt(gridTop + row + 3) == 0 && ArrangementPanel.RowIndexAt(song, row + 3) == 0
                && timeline.TrackAt(gridTop + 3) == -1 && timeline.TrackAt(gridTop + 2 * row + 3) == 1, $"{timeline.TrackAt(gridTop + row + 3)} {timeline.TrackAt(gridTop + 3)}");
            var label = panel.MasterLaneRow as Border;
            Check("master lane: the list shows a Master row of one row height above track 1", label is not null && VisualDescendants<TextBlock>(label).Any(t => t.Text == "Master") && Math.Abs(label.ActualHeight - row) < 0.5
                && Math.Abs(label.TranslatePoint(new Point(0, 0), (UIElement)System.Windows.Media.VisualTreeHelper.GetParent(label)).Y) < 0.5);
            Check("master lane: both menus show it checked", panel.BuildEmptyAreaMenu().Items.OfType<MenuItem>().Any(m => m.Header as string == "Show master track" && m.IsChecked)
                && TrackRowMenus.Build(new TrackRowMenuState(false, true, true, "", true), _ => "").Any(s => s.Id == MasterLane.CommandId && s.Checked && s.SettingKey == "timeline.mastertrack"));

            // The row's controls: master FX, volume and pan in the track rows' columns, on the master state the Mixer uses.
            var (fx, volume, pan) = panel.MasterLaneWidgets;
            double X(FrameworkElement e) => e.TranslatePoint(new Point(0, 0), panel).X;
            var rowFx = VisualDescendants<FxSplitButton>(panel).FirstOrDefault(b => !ReferenceEquals(b, fx));
            var rowSliders = VisualDescendants<Slider>(panel).Where(s => !ReferenceEquals(s, volume) && !ReferenceEquals(s, pan)).Take(2).Cast<FrameworkElement>().ToList();
            if (panel.PanKnobs && VisualDescendants<KnobControl>(panel).FirstOrDefault(k => !ReferenceEquals(k, panel.MasterVolumeKnob)) is { } panKnob) rowSliders = new List<FrameworkElement> { rowSliders[0], panKnob };   // track rows show a pan knob in this style
            Check("master lane: the row has an FX button, a volume slider and a pan slider inside it", fx is not null && volume is not null && pan is not null && label is not null
                && new FrameworkElement[] { fx, volume, pan }.All(e => e.IsDescendantOf(label)) && VisualDescendants<Slider>(label).Count() == 2);
            if (fx is not null && volume is not null && pan is not null && rowFx is not null && rowSliders.Count == 2)
            {
                Check("master lane: its controls sit in track 1's FX, volume and pan columns", Math.Abs(X(fx) - X(rowFx)) < 0.5 && Math.Abs(X(volume) - X(rowSliders[0])) < 0.5 && Math.Abs(X(pan) - X(rowSliders[1])) < 0.5
                    && Math.Abs(volume.ActualWidth - rowSliders[0].ActualWidth) < 0.5, $"fx {X(fx)}/{X(rowFx)} vol {X(volume)}/{X(rowSliders[0])} pan {X(pan)}/{X(rowSliders[1])}");
                volume.Value = 61;
                Check("master lane: the row's volume sets the master volume the Mixer reads", (int)panel.MasterVolumeKnob.Value == 61);
                panel.MasterVolumeKnob.Value = 87;   // the Mixer's master fader sets the knob
                Check("master lane: a master volume change from the Mixer moves the row's slider", (int)volume.Value == 87);
                panel.MasterVolumeKnob.Value = 100;
                pan.Value = -20;
                Check("master lane: the row's pan sets the song's master pan", song.Mixer.MasterPan == -20);
                song.Mixer.MasterPan = 15; panel.SyncMixValues();
                Check("master lane: a master pan change from the Mixer moves the row's slider", (int)pan.Value == 15);
                song.Mixer.MasterPan = 0;
                Check("master lane: the FX button shows the master chain's power state", fx.ChainOn == song.Mixer.Master.On);
            }
            else Check("master lane: track 1's FX button and sliders are found", false);

            panel.ShowMasterLane = false;
            window.UpdateLayout(); PumpUi();
            Check("master lane: hidden again, the rows are where they were", Enumerable.Range(0, 3).All(i => ArrangementPanel.RowTopOf(song, i) == offTops[i]) && panel.MasterLaneRow is null
                && ArrangementPanel.RowsHeight(song) == offRows && panel.PreferredHeight() == offHeight && timeline.TrackAt(gridTop + 3) == 0);
        }
        finally { window.Close(); }
    }
}
