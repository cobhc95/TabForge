using System;
using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: what the plug-in area shows when a docked editor does not open (the notice with Retry and Open in separate window), and that the BYPASSED stamp
/// takes the native editor surface out of the way.
/// Does not own: the real editor window (needs an engine and a plug-in), the popup lookup (needs a native window).
/// Tests: this class.
/// </summary>
public class FxEditorFailureTests
{
    private static (FxChainWindow Window, FxTestKit.FakeEngine Engine, FxEditorArea Area) Open()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        track.SoundSource = SoundSources.Plugins;
        var engine = new FxTestKit.FakeEngine { Opens = false };
        var window = FxTestKit.Window(track, FxTestKit.Host(track, engine));
        window.Show();
        var area = DialogTestKit.Named<FxEditorArea>(window, "EditorArea");
        area.Surface.SetAreaForTest((IntPtr)1234);
        DialogTestKit.RunQueuedUiWork();
        FxTestKit.List(window).Click(1);
        return (window, engine, area);
    }

    [AvaloniaFact]
    public void ARefusedDockedOpenSaysSoWithRetryAndASeparateWindowButton()
    {
        var (window, _, area) = Open();
        Assert.Equal(FxEditorPlan.EditorFailedNotice, area.NoticeText);
        Assert.Equal(FxEditorPlan.RetryButton, area.ActionLabel);
        Assert.Equal(FxEditorPlan.SeparateWindowButton, area.SecondActionLabel);
        Assert.False(area.EditorVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void RetryClosesAndOpensTheDockedEditorAgain()
    {
        var (window, engine, area) = Open();
        engine.Opens = true;
        engine.Calls.Clear();
        FxTestKit.PressButton(window, "DockHereButton");
        Assert.Contains("close:Sample reverb", engine.Calls);
        Assert.Contains("open:Sample reverb:docked:False", engine.Calls);
        Assert.True(area.EditorVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void TheSeparateWindowButtonFloatsThePluginWindow()
    {
        var (window, engine, area) = Open();
        engine.Opens = true;
        FxTestKit.PressButton(window, "SecondActionButton");
        Assert.Contains("open:Sample reverb:float:False", engine.Calls);
        Assert.Equal(FxEditorPlan.FloatingNotice, area.NoticeText);
        window.Close();
    }

    [AvaloniaFact]
    public void ClosingTheFxWindowLeavesTheFloatingEditorOpen()
    {
        var (window, engine, _) = Open();
        engine.Opens = true;
        FxTestKit.PressButton(window, "SecondActionButton");
        engine.Calls.Clear();
        window.Close();
        DialogTestKit.RunQueuedUiWork();
        Assert.DoesNotContain("close:Sample reverb", engine.Calls);
    }

    [AvaloniaFact]
    public void ClosingTheFloatingEditorLeavesTheFxWindowOpenAndForgetsItAsFloating()
    {
        var (window, engine, area) = Open();
        engine.Opens = true;
        FxTestKit.PressButton(window, "SecondActionButton");
        Assert.Equal(FxEditorPlan.FloatingNotice, area.NoticeText);
        engine.RaiseEditorClosed();
        DialogTestKit.RunQueuedUiWork();
        Assert.True(window.IsVisible);
        Assert.NotEqual(FxEditorPlan.FloatingNotice, area.NoticeText);
        window.Close();
    }

    [AvaloniaFact]
    public void TheBypassStampHidesTheNativeSurfaceAndShowsItAgain()
    {
        var area = new FxEditorArea(() => IntPtr.Zero);
        area.ShowOverlay(true, light: false);
        Assert.True(area.OverlayVisible);
        Assert.False(area.Surface.IsVisible);
        area.ShowOverlay(false, light: false);
        Assert.False(area.OverlayVisible);
        Assert.True(area.Surface.IsVisible);
    }

    [AvaloniaFact]
    public void TheBypassStampHidesAndShowsThePopupOnceEachWhenItChanges()
    {
        var calls = new List<(IntPtr, bool)>();
        var area = new FxEditorArea(() => (IntPtr)77, (popup, visible) => calls.Add((popup, visible)));
        area.ShowOverlay(true, light: false);
        area.ShowOverlay(true, light: false);
        area.ShowOverlay(false, light: false);
        area.ShowOverlay(false, light: false);
        Assert.Equal(new[] { ((IntPtr)77, false), ((IntPtr)77, true) }, calls);
    }

    private static (FxEditorController Controller, FxTestKit.FakeEngine Engine, AvaloniaPlaybackSessionTests.FakeUiThread Ui, FxEditorArea Area, PluginSlot Slot, List<string> Reshown) SlowOpen()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        track.SoundSource = SoundSources.Plugins;
        var engine = new FxTestKit.FakeEngine();
        var ui = new AvaloniaPlaybackSessionTests.FakeUiThread();
        var area = new FxEditorArea(() => IntPtr.Zero, (_, _) => { });
        area.Surface.SetAreaForTest((IntPtr)1234);
        var slot = track.Rig.Plugins[1];
        var reshown = new List<string>();
        var controller = new FxEditorController(FxTestKit.Host(track, engine), track, area, ui, () => IntPtr.Zero, () => true, () => slot, () => reshown.Add("reshow"), () => { }, () => IntPtr.Zero);
        controller.Show(slot);
        return (controller, engine, ui, area, slot, reshown);
    }

    [AvaloniaFact]
    public void TheFirstOpenOfAPluginGetsTheLongCheckAndANormalAnswerEndsIt()
    {
        var (controller, engine, ui, area, _, _) = SlowOpen();
        Assert.True(ui.FrameTimer.IsEnabled);
        Assert.Equal(FxEditorController.FirstOpenCheckMs, (int)ui.FrameTimer.Interval.TotalMilliseconds);
        engine.RaiseEditorSized(0, 1, 800, 600);
        Assert.False(ui.FrameTimer.IsEnabled);
        ui.FrameTimer.Fire();
        Assert.True(area.EditorVisible);
        controller.Dispose();
    }

    [AvaloniaFact]
    public void ASlowPluginThatAnswersAfterTheCheckGaveUpReplacesTheFailureNotice()
    {
        var (controller, engine, ui, area, _, reshown) = SlowOpen();
        ui.FrameTimer.Fire();   // no popup and no answer yet
        Assert.Equal(FxEditorPlan.EditorFailedNotice, area.NoticeText);
        engine.RaiseEditorSized(0, 1, 800, 600);
        Assert.Equal(new[] { "reshow" }, reshown);
        controller.Dispose();
    }
}
