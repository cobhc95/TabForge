using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the calls the FX chain window makes to the engine and what it shows from the engine: the CPU text, the docked and floating editors, the programs and
/// presets, the notices for a blocked or crashed plug-in, and the window options that decide docking.
/// Does not own: the engine (a recording fake stands in), the window or its controller.
/// Tests: this class.
/// </summary>
public class FxEngineCallTests
{
    private static (FxChainWindow Window, TrackModel Track, FxTestKit.FakeEngine Engine, PluginSettings Settings) Open(bool area = false, bool chainOn = true)
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        if (chainOn) track.SoundSource = SoundSources.Plugins;
        var engine = new FxTestKit.FakeEngine();
        var settings = new PluginSettings();
        var window = FxTestKit.Window(track, FxTestKit.Host(track, engine, settings));
        window.Show();
        if (area)
        {
            DialogTestKit.Named<FxEditorArea>(window, "EditorArea").Surface.SetAreaForTest((IntPtr)1234);
            DialogTestKit.RunQueuedUiWork();   // the window shows the focused plug-in again once the area has its native window
        }
        return (window, track, engine, settings);
    }

    [AvaloniaFact]
    public void CpuTextFollowsTheEngineAndIsBlankWhenItStops()
    {
        var (window, _, engine, _) = Open();
        var cpu = DialogTestKit.Named<TextBlock>(window, "CpuStatus");
        Assert.Equal(FxChainPresenter.CpuStatusText(true, 0.25, Environment.ProcessorCount), cpu.Text);
        engine.Running = false;
        var stopped = FxTestKit.Window(FxTestKit.Track(), FxTestKit.Host(FxTestKit.Track(), engine));
        stopped.Show();
        Assert.Equal("", DialogTestKit.Named<TextBlock>(stopped, "CpuStatus").Text);
        stopped.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void FocusingAPluginDocksItsEditorAndAsksForItsPrograms()
    {
        var (window, _, engine, _) = Open(area: true);
        FxTestKit.List(window).Click(1);
        Assert.Contains("programs:Sample reverb", engine.Calls);
        Assert.Contains("open:Sample reverb:docked:False", engine.Calls);
        FxTestKit.List(window).Click(0);
        Assert.Contains("close:Sample reverb", engine.Calls);   // the previous editor closes before the next opens
        Assert.Contains("open:Sample piano:docked:False", engine.Calls);
        window.Close();
        Assert.Contains("close:Sample piano", engine.Calls);   // closing the window closes the docked editor
    }

    [AvaloniaFact]
    public void DoubleClickFloatsThePluginWindowWithTheOnTopOption()
    {
        var (window, _, engine, settings) = Open(area: true);
        settings.PluginWindowsOnTop = true;
        FxTestKit.List(window).Click(1);
        FxTestKit.RaiseActivated(window, 1);
        Assert.Contains("open:Sample reverb:float:True", engine.Calls);
        var area = DialogTestKit.Named<FxEditorArea>(window, "EditorArea");
        Assert.Equal(FxEditorPlan.FloatingNotice, area.NoticeText);
        Assert.Equal(FxEditorPlan.ShowHereButton, area.ActionLabel);
        FxTestKit.Press(window, DialogTestKit.Named<Button>(window, "DockHereButton"));
        Assert.True(area.EditorVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void FloatingWhenTheEngineRefusesSaysTheChainMustBeOn()
    {
        var (window, _, engine, _) = Open(chainOn: false);
        engine.Opens = false;
        FxTestKit.List(window).Click(1);
        FxTestKit.RaiseActivated(window, 1);
        Assert.Equal(FxEditorPlan.FloatNeedsChainNotice, DialogTestKit.Named<FxEditorArea>(window, "EditorArea").NoticeText);
        window.Close();
    }

    [AvaloniaFact]
    public void DockingOffFloatsEachPluginAndTheOptionIsSaved()
    {
        var saved = 0;
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        track.SoundSource = SoundSources.Plugins;
        var engine = new FxTestKit.FakeEngine();
        var settings = new PluginSettings { DockPluginWindows = false };
        var window = FxTestKit.Window(track, FxTestKit.Host(track, engine, settings, saved: () => saved++));
        window.Show();
        FxTestKit.List(window).Click(0);
        Assert.Contains("open:Sample piano:float:False", engine.Calls);
        Assert.Null(DialogTestKit.Named<FxEditorArea>(window, "EditorArea").ActionLabel);
        var item = FxTestKit.Item(window, FxChainWindow.DockOptionText);
        Assert.False(item.IsChecked);
        item.IsChecked = true;
        Assert.True(settings.DockPluginWindows);
        Assert.Equal(1, saved);
        FxTestKit.Item(window, FxChainWindow.OnTopOptionText).IsChecked = true;
        Assert.True(settings.PluginWindowsOnTop);
        Assert.Equal(2, saved);
        window.Close();
    }

    [AvaloniaFact]
    public void ProgramsFillThePresetBoxAndPickingOneSetsTheProgram()
    {
        var (window, _, engine, _) = Open(area: true);
        FxTestKit.List(window).Click(0);
        engine.RaisePrograms(0, 0, 1, "Init", "Warm", "Bright");
        var box = DialogTestKit.Named<ComboBox>(window, "PresetBox");
        Assert.Equal(new[] { "Init", "Warm", "Bright" }, box.Items.OfType<object>().Take(3).Select(i => i.ToString()));
        Assert.Equal("Warm", box.SelectedItem?.ToString());
        box.SelectedIndex = 2;
        Assert.Contains("program:Sample piano:2", engine.Calls);
        window.Close();
    }

    [AvaloniaFact]
    public void SavedPresetsAreListedLoadedAndSaved()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var (window, track, engine, _) = Open(area: true);
        FxTestKit.List(window).Click(1);
        window.Dialogs.PresetName = (_, _) => System.Threading.Tasks.Task.FromResult<string?>("Mine");
        engine.Calls.Clear();
        FxTestKit.PressButton(window, "AddPresetButton");
        DialogTestKit.RunQueuedUiWork();
        Assert.Contains("collect", engine.Calls);
        Assert.Contains("Mine", PluginLibrary.PresetNames(track.Rig.Plugins[1]));
        var box = DialogTestKit.Named<ComboBox>(window, "PresetBox");
        Assert.Equal("★ Mine", box.SelectedItem?.ToString());   // the new preset is shown as chosen at once
        Assert.Single(box.Items.OfType<object>(), i => i.ToString() == "★ Mine");
        Assert.DoesNotContain(engine.Calls, c => c.StartsWith("state:"));   // saving applied nothing
        var item = box.Items.OfType<object>().First(i => i.ToString() == "★ Mine");
        box.SelectedItem = null;
        box.SelectedItem = item;
        Assert.Contains(engine.Calls, c => c.StartsWith("state:Sample reverb:"));
        Assert.Equal("★ Mine", box.SelectedItem?.ToString());
        FxTestKit.List(window).Click(0);
        FxTestKit.List(window).Click(1);
        Assert.Equal("★ Mine", DialogTestKit.Named<ComboBox>(window, "PresetBox").SelectedItem?.ToString());
        window.Close();
    }

    [AvaloniaFact]
    public void APickedProgramStaysInTheBoxWhenThePluginIsShownAgain()
    {
        var (window, track, engine, _) = Open(area: true);
        FxTestKit.List(window).Click(0);
        engine.RaisePrograms(0, 0, 1, "Init", "Warm", "Bright");
        DialogTestKit.Named<ComboBox>(window, "PresetBox").SelectedIndex = 2;
        Assert.Equal("Bright", track.Rig.Plugins[0].PresetName);
        FxTestKit.List(window).Click(1);
        FxTestKit.List(window).Click(0);
        Assert.Equal("Bright", DialogTestKit.Named<ComboBox>(window, "PresetBox").SelectedItem?.ToString());
        window.Close();
    }

    [AvaloniaFact]
    public void AnUnapprovedPluginIsBlockedUntilAllowed()
    {
        var path = @"C:\tf-fx-tests\not-approved.dll";
        var track = FxTestKit.Track(new[] { new PluginSlot { Name = "Stranger", Path = path, Format = "VST2" } });
        track.SoundSource = SoundSources.Plugins;
        var window = FxTestKit.Window(track, FxTestKit.Host(track, new FxTestKit.FakeEngine(), new PluginSettings()));
        window.Show();
        var area = DialogTestKit.Named<FxEditorArea>(window, "EditorArea");
        Assert.Equal(FxChainPresenter.BlockedNotice(path), area.NoticeText);
        Assert.Equal(FxEditorPlan.AllowButton, area.ActionLabel);
        window.Close();
    }

    [AvaloniaFact]
    public void ABigEditorGrowsTheAreaScrollBarsAndASmallOneDoesNot()
    {
        var (window, _, engine, _) = Open(area: true);
        FxTestKit.List(window).Click(0);
        var area = DialogTestKit.Named<FxEditorArea>(window, "EditorArea");
        engine.RaiseEditorSized(0, 0, 200, 120);
        Assert.False(area.HorizontalBarVisible || area.VerticalBarVisible, $"area {area.Bounds} frame {area.FrameSize} visible {area.EditorVisible}");
        engine.RaiseEditorSized(0, 0, 6000, 4000);
        Assert.True(area.HorizontalBarVisible && area.VerticalBarVisible);
        window.Close();
    }
}
