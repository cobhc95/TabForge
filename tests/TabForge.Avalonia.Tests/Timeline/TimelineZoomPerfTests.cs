using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Platform;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the smoothness of the timeline zoom: a wheel zoom only relays the bars out (never reloads), one relayout per display frame however many ticks arrived, one settle event for the save,
///   the pointer anchor, the 0.25-4.0 clamp, the discrete button steps and that a structure edit still reloads.
/// Tests: ArrangementView.ZoomByWheel, TimelineView.Relayout, TimelineZoomGesture.
/// </summary>
public class TimelineZoomPerfTests
{
    private sealed class FakeTimer : IUiTimer
    {
        public TimeSpan Interval { get; set; }
        public bool IsEnabled { get; private set; }
        public event EventHandler? Tick;
        public void Start() => IsEnabled = true;
        public void Stop() => IsEnabled = false;
        public void Fire() { if (IsEnabled) Tick?.Invoke(this, EventArgs.Empty); }
        public bool Subscribed => Tick is not null;
    }

    private sealed class Rig
    {
        public SongDocument Song = null!;
        public ArrangementView View = null!;
        public Window Window = null!;
        public FakeTimer Settle = new();
        public Queue<Action> Frames = new();
        public int Settled;
        public double SettledFactor;
        public void Frame() { while (Frames.Count > 0) Frames.Dequeue()(); }
    }

    private static Rig Open()
    {
        var rig = new Rig { Song = new SongDocument(() => null) { Project = DemoSongFactory.Create() } };
        rig.View = new ArrangementView(rig.Song.Project, rig.Song, null, rig.Settle, rig.Frames.Enqueue);
        rig.View.ZoomSettled += factor => { rig.Settled++; rig.SettledFactor = factor; };
        rig.Window = new Window { Width = 1000, Height = 600, Content = rig.View };
        rig.Window.Show();
        rig.Window.UpdateLayout();
        return rig;
    }

    [AvaloniaFact]
    public void ThirtyWheelStepsCallReloadZeroTimesDuringGesture()
    {
        var rig = Open();
        var reloads = rig.View.Timeline.ReloadCount;
        for (var i = 0; i < 30; i++)
        {
            rig.View.ZoomByWheel(i % 3 == 0 ? -1 : 1, 300);
            rig.Frame();
        }
        Assert.Equal(reloads, rig.View.Timeline.ReloadCount);
        Assert.Equal(30, rig.View.Timeline.RelayoutCount);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void TicksWithinOneFrameMakeOneRelayout()
    {
        var rig = Open();
        var before = rig.View.Timeline.RelayoutCount;
        for (var i = 0; i < 7; i++) rig.View.ZoomByWheel(1, 300);
        Assert.Equal(before, rig.View.Timeline.RelayoutCount);
        rig.Frame();
        Assert.Equal(before + 1, rig.View.Timeline.RelayoutCount);
        Assert.Equal(Math.Pow(1.15, 7), rig.View.Timeline.ZoomFactor, 6);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void SettingsAreReportedOnceAtSettle()
    {
        var rig = Open();
        for (var i = 0; i < 10; i++) { rig.View.ZoomByWheel(1, 300); rig.Frame(); }
        Assert.Equal(0, rig.Settled);
        rig.Settle.Fire();
        rig.Settle.Fire();
        Assert.Equal(1, rig.Settled);
        Assert.Equal(rig.View.Timeline.ZoomFactor, rig.SettledFactor, 9);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void BarUnderThePointerStaysWithinOnePixelAfterTenSteps()
    {
        var rig = Open();
        var timeline = rig.View.Timeline;
        var sideways = timeline.GetVisualAncestors().OfType<ScrollViewer>().First();
        const double anchor = 300;
        var fraction = (sideways.Offset.X + anchor) / timeline.BarsWidth;
        for (var i = 0; i < 10; i++)
        {
            rig.View.ZoomByWheel(1, anchor);
            rig.Frame();
            Assert.True(Math.Abs(sideways.Offset.X + anchor - fraction * timeline.BarsWidth) <= 1, $"step {i}: drift");
        }
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void PinchStepsWithinOneFrameMakeOneRelayoutAnchoredAndSettleOnce()
    {
        var rig = Open();
        var timeline = rig.View.Timeline;
        var sideways = timeline.GetVisualAncestors().OfType<ScrollViewer>().First();
        const double centre = 400;
        var fraction = (sideways.Offset.X + centre) / timeline.BarsWidth;
        var before = timeline.RelayoutCount;
        rig.View.ZoomByPinch(1.1, centre);
        rig.View.ZoomByPinch(1.1, centre);
        rig.View.ZoomByPinch(1.1, centre);
        Assert.Equal(before, timeline.RelayoutCount);
        rig.Frame();
        Assert.Equal(before + 1, timeline.RelayoutCount);
        Assert.Equal(1.1 * 1.1 * 1.1, timeline.ZoomFactor, 6);
        Assert.True(Math.Abs(sideways.Offset.X + centre - fraction * timeline.BarsWidth) <= 1, "pinch centre drifted");
        Assert.Equal(0, rig.Settled);
        rig.Settle.Fire();
        rig.Settle.Fire();
        Assert.Equal(1, rig.Settled);
        Assert.Equal(timeline.ZoomFactor, rig.SettledFactor, 9);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void FactorClampsAtTheEnds()
    {
        var rig = Open();
        rig.View.ZoomByWheel(100, 300); rig.Frame();
        Assert.Equal(4.0, rig.View.Timeline.ZoomFactor, 9);
        rig.View.ZoomByWheel(-100, 300); rig.Frame();
        Assert.Equal(0.25, rig.View.Timeline.ZoomFactor, 9);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void ButtonsStillStepDiscretely()
    {
        var rig = Open();
        var standard = rig.View.Timeline.BarsWidth;
        rig.View.SetTimelineZoom(TimelineZoomRules.Step(rig.View.Timeline.ZoomFactor, 1));
        Assert.Equal(1.5, rig.View.Timeline.ZoomFactor);
        Assert.Equal(standard * 1.5, rig.View.Timeline.BarsWidth, 6);
        Assert.Equal(0, rig.Settled);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void StructureEditDuringZoomStillReloads()
    {
        var rig = Open();
        rig.View.ZoomByWheel(2, 300); rig.Frame();
        var timeline = rig.View.Timeline;
        var reloads = timeline.ReloadCount;
        var factor = timeline.ZoomFactor;
        var width = timeline.BarsWidth;
        timeline.Reload(rig.Song.Project);
        Assert.Equal(reloads + 1, timeline.ReloadCount);
        Assert.Equal(factor, timeline.ZoomFactor, 9);
        Assert.Equal(width, timeline.BarsWidth, 6);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void DetachMidZoomStopsTheSettleTimerAndLaterTicksTouchNothing()
    {
        var rig = Open();
        rig.View.ZoomByWheel(1, 300);
        Assert.True(rig.Settle.IsEnabled);
        Assert.True(rig.Settle.Subscribed);
        var relayouts = rig.View.Timeline.RelayoutCount;
        rig.Window.Content = null;   // the view leaves the tree before its frame and its settle
        Assert.False(rig.Settle.IsEnabled);
        Assert.False(rig.Settle.Subscribed);
        rig.Frame();
        rig.Settle.Fire();
        Assert.Equal(relayouts, rig.View.Timeline.RelayoutCount);
        Assert.Equal(0, rig.Settled);
        Assert.Equal(0, rig.View.ZoomFramesApplied);
    }

    [AvaloniaFact]
    public void DetachMidSettleStopsTheTimerAfterTheFrameApplied()
    {
        var rig = Open();
        rig.View.ZoomByWheel(1, 300);
        rig.Frame();
        Assert.Equal(1, rig.View.ZoomFramesApplied);
        var relayouts = rig.View.Timeline.RelayoutCount;
        rig.Window.Content = null;
        rig.Settle.Fire();
        Assert.Equal(0, rig.Settled);
        Assert.Equal(relayouts, rig.View.Timeline.RelayoutCount);
    }

    [AvaloniaFact]
    public void AClampedZoomStepIsNotCountedAsAnAppliedFrame()
    {
        var rig = Open();
        rig.View.Timeline.ZoomFactor = 4;
        rig.View.ZoomByWheel(5, 300);
        rig.Frame();
        Assert.Equal(0, rig.View.ZoomFramesApplied);
        rig.Window.Close();
    }

    [AvaloniaFact]
    public void RelayoutOnTheDemoSongIsFast()
    {
        var rig = Open();
        var timeline = rig.View.Timeline;
        timeline.ZoomFactor = 1.1;
        var watch = Stopwatch.StartNew();
        const int runs = 200;
        for (var i = 0; i < runs; i++)
        {
            timeline.ZoomFactor = 1 + (i % 2 == 0 ? 0.2 : 0.4);
            rig.Window.UpdateLayout();
        }
        var ms = watch.Elapsed.TotalMilliseconds / runs;
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "relayout-ms.txt"), ms.ToString("F3"));
        Assert.True(ms < 4, $"relayout + layout took {ms:F2} ms");
        rig.Window.Close();
    }
}
