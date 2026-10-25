using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using TabForge.Audio;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Fx;
using TabForge.LiveInput;
using TabForge.Models;
using TabForge.Platform.Theme;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the slim live input strip in use: the M and S buttons (lit states, solo heard alone), the Tuner switch and window, the cents text rate, the frame loop
/// asking for no layout, and no overlap at the window minimum width.
/// Does not own: the sliders and the strip position (LiveStripSliderTests).
/// Tests: this class.
/// </summary>
public class LiveStripBehaviourTests
{
    private static void Click(Window window, Control target)
    {
        var at = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window) ?? default;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
    }

    [AvaloniaFact]
    public void MuteAndSoloToggleWithTheTrackListLitColoursAndSoloIsHeardAlone()
    {
        var (live, strip, session, client) = LiveStripSliderTests.OpenLive();
        var knobs = strip.Knobs;
        Click(live, knobs.Mute);
        Assert.True(knobs.Mute.IsChecked); Assert.True(session.Muted);
        Assert.Equal(0, client.LiveInput.Mix.Volume);
        Click(live, knobs.Solo);
        Assert.True(knobs.Solo.IsChecked); Assert.True(session.Soloed);
        Assert.NotEqual(0, client.LiveInput.Mix.Volume);   // a muted track that is soloed plays
        var ink = AvaloniaPalette.InkOf(false);
        Assert.Equal(ink("TrackMuted"), TrackHeaderColumn.ToggleColours(ink, TrackRowCells.Cell.Mute, true).Fill);
        Assert.Equal(ink("TrackSolo"), TrackHeaderColumn.ToggleColours(ink, TrackRowCells.Cell.Solo, true).Fill);
        Click(live, knobs.Solo);
        Assert.False(session.Soloed);
        Assert.Equal(0, client.LiveInput.Mix.Volume);
        live.Close(); session.Dispose(); client.Dispose();
    }

    [Fact]
    public void LiveSoloSilencesSongTracksUnlessSoloedToo()
    {
        var project = new SongProject();
        var plain = new TrackModel { Name = "A", Volume = 100 };
        var soloed = new TrackModel { Name = "B", Volume = 100, Solo = true };
        project.Tracks.Add(plain);
        Assert.Equal(100, EngineMixLevels.Levels(project, 100)(plain).Volume);
        Assert.Equal(0, EngineMixLevels.Levels(project, 100, liveSolo: true)(plain).Volume);
        project.Tracks.Add(soloed);
        Assert.Equal(100, EngineMixLevels.Levels(project, 100, liveSolo: true)(soloed).Volume);
        Assert.Equal(0, EngineMixLevels.Levels(project, 100, liveSolo: true)(plain).Volume);
    }

    [AvaloniaFact]
    public void TunerSwitchAndWindowWorkAndNothingResizesPerFrame()
    {
        var (live, strip, session, client) = LiveStripSliderTests.OpenLive();
        var tuner = strip.Tuner;
        Assert.True(tuner.IsOn);
        Click(live, tuner.Switch);
        Assert.False(tuner.IsOn);
        Assert.False(session.TunerOn);
        Assert.True(tuner.Window.Bounds.Height <= ChromeMetrics.LiveStripHeight);
        tuner.Window.SelectedItem = LiveStripRules.WindowItemText(50);
        Assert.Equal(50, session.TunerWindowMs);
        Click(live, tuner.Switch);
        Assert.True(tuner.IsOn);
        Click(live, tuner.Switch);
        Assert.False(tuner.IsOn);
        var before = (strip.Knobs.Bounds, strip.Meters.Bounds, tuner.Bounds);
        var level = 0f;
        for (var i = 0; i < 30; i++) { level = (level + 0.13f) % 1; var l = level; strip.PeakSource = () => (l, l / 2); strip.Tick(i * 7, DateTime.UtcNow); }
        Assert.True(strip.IsMeasureValid && strip.IsArrangeValid, "meters repaint only: no layout is asked for per frame");
        live.UpdateLayout();
        Assert.Equal(before, (strip.Knobs.Bounds, strip.Meters.Bounds, tuner.Bounds));
        live.Close(); session.Dispose(); client.Dispose();
    }

    [AvaloniaFact]
    public void CentsTextChangesAtMostAFewTimesASecond()
    {
        var tuner = new LiveStripTuner();
        var readout = new LiveTunerReadout();
        var t = DateTime.UtcNow;
        var changes = 0;
        var last = tuner.CentsText;
        for (var i = 0; i < 100; i++)
        {
            readout.Update((float)(440 * Math.Pow(2, (i % 40 - 20) / 1200.0)), 0.95f, i + 1, t.AddMilliseconds(i * 10));
            tuner.Show(readout, i * 10);   // one second of frames at 100 Hz
            if (tuner.CentsText != last) { changes++; last = tuner.CentsText; }
        }
        Assert.InRange(changes, 1, 5);
        Assert.EndsWith("\u00a2", tuner.CentsText);
        tuner.ShowOff();
        Assert.Equal("", tuner.CentsText);
    }

    private static Control[] Groups(LiveInputStrip strip) => new Control[] { (Control)strip.Meters.Parent!, strip.Tuner, strip.Tuner.Controls, strip.Knobs, strip.Knobs.Buttons };

    /// <summary>How many rows the groups sit on (groups in a row share a centre line; rows are a control height apart).</summary>
    private static int Rows(LiveInputStrip strip) =>
        1 + Groups(strip).Select(g => LiveStripSliderTests.TopLeftIn(g, strip).Y + g.Bounds.Height / 2).OrderBy(y => y).Zip(Groups(strip).Select(g => LiveStripSliderTests.TopLeftIn(g, strip).Y + g.Bounds.Height / 2).OrderBy(y => y).Skip(1), (a, b) => b - a).Count(gap => gap > 10);

    /// <summary>Lays the strip out at a width by hand (a shown test window keeps its size), as a window resize would.</summary>
    private static void Resize(LiveInputStrip strip, double width)
    {
        strip.Measure(new Size(width, double.PositiveInfinity));
        strip.Arrange(new Rect(0, 0, width, strip.DesiredSize.Height));
    }

    [AvaloniaTheory]
    [InlineData(500)]
    [InlineData(640)]
    [InlineData(900)]
    [InlineData(1600)]
    public void GroupsWrapWholeWithoutOverlapOrClipAndReturnToOneRowWhenWide(int width)
    {
        var (live, strip, session, client) = LiveStripSliderTests.OpenLive(width: width);
        var groups = Groups(strip);
        void Check(bool update = true)
        {
            if (update) live.UpdateLayout();
            var rects = groups.Select(g => new Rect(LiveStripSliderTests.TopLeftIn(g, strip), g.Bounds.Size)).ToArray();
            for (var i = 0; i < rects.Length; i++)
            {
                Assert.True(rects[i].Right <= strip.Bounds.Width + 0.5 && rects[i].Bottom <= strip.Bounds.Height + 0.5, $"group {i} clipped at {width}");
                Assert.True(groups[i].Bounds.Width >= groups[i].DesiredSize.Width - 0.5, $"group {i} narrower than its content");
                for (var j = i + 1; j < rects.Length; j++) Assert.False(rects[i].Intersects(rects[j]), $"groups {i} and {j} overlap at {width}");
            }
        }
        Check();
        var rows = Rows(strip);
        if (width >= 1600) Assert.Equal(1, rows);
        if (width <= 500) Assert.True(rows >= 2, "narrow: groups drop to more rows");
        Resize(strip, 1600); Check(false);
        Assert.Equal(1, Rows(strip));
        Resize(strip, width); Check(false);
        live.UpdateLayout();
        var before = groups.Select(g => g.Bounds).ToArray();
        strip.TunerSource = () => (441f, 0.95f, 1);
        strip.Tick(0, DateTime.UtcNow);
        live.UpdateLayout();
        Assert.Equal(before, groups.Select(g => g.Bounds).ToArray());
        live.Close(); session.Dispose(); client.Dispose();
    }
}
