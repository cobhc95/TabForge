using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Audio.Contracts.Builtin;
using TabForge.AudioEngine.Builtin;
using TabForge.AudioEngine.Builtin.Compressor;
using TabForge.AudioEngine.Builtin.Delay;
using TabForge.AudioEngine.Builtin.Gate;
using TabForge.AudioEngine.Builtin.Limiter;
using TabForge.Avalonia.Views.Fx;
using TabForge.Models;
using TabForge.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the own windows of the compressor, limiter, delay and noise gate: the meters, the light and the transfer curve follow what the engine reports, a changed control
//   reaches the engine, the factory presets apply as one undo step, and each window is rendered dark and light for review.
// Does not own: the sound (Builtin*Tests), the EQ window (BuiltinEditorWindowTests), the IR Loader window (IrLoaderWindowTests).
// Tests: this class. Renders go to TABFORGE_SHOTS_DIR when it is set, else the render folder of this class.
public sealed class BuiltinDynamicsWindowTests
{
    private const int Rate = 48000;

    private static (PluginSlot Slot, TrackModel Track, FxTestKit.FakeEngine Engine) Setup(string id)
    {
        var slot = new PluginSlot { Name = BuiltinEffects.Find(id)!.Name, Path = id, Format = PluginFormats.Builtin, Vendor = BuiltinEffects.Vendor };
        return (slot, FxTestKit.Track([slot], "Guitar"), new FxTestKit.FakeEngine());
    }

    private static void Shot(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        var dir = Environment.GetEnvironmentVariable("TABFORGE_SHOTS_DIR") is { Length: > 0 } custom ? Directory.CreateDirectory(custom).FullName : DialogTestKit.RenderDirectory(nameof(BuiltinDynamicsWindowTests));
        frame!.Save(Path.Combine(dir, name + ".png"));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheCompressorWindowShowsTheCurveAndMetersAndAppliesAPreset(bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var (slot, track, engine) = Setup(BuiltinEffects.CompressorId);
        var editor = new CompressorEditor(FxTestKit.Host(track, engine), track, slot);
        var window = new BuiltinEditorWindow(slot, editor);
        window.Show();
        using var compressor = new CompressorEffect(Rate, 512);
        compressor.SetParameter(CompressorEffect.ThresholdId, -24);
        compressor.SetParameter(CompressorEffect.KneeId, 8);
        BuiltinTestKit.Run(compressor, BuiltinTestKit.Sine(220, -9, Rate, Rate), 480);
        engine.RaiseParameters(0, 0, compressor.GetParameters());
        Dispatcher.UIThread.RunJobs();
        Assert.True(DialogTestKit.Named<TextBlock>(window, "CompressorReductionText").Text!.Length > 3);
        Assert.True(editor.Curve.IsVisible);
        Shot(window, light ? "compressor-light" : "compressor-dark");

        DialogTestKit.Named<Slider>(window, "Fxthreshold").Value = 0.25;
        Assert.Contains(engine.Calls, c => c.StartsWith($"param:Compressor:{CompressorEffect.ThresholdId}:", StringComparison.Ordinal));

        var presets = DialogTestKit.Named<ComboBox>(window, "CompressorPresets");
        Assert.Equal(new[] { "Bass", "Vocal", "Guitar" }, presets.Items.Cast<string>());
        presets.SelectedItem = "Vocal";
        Assert.Contains(engine.Calls, c => c.StartsWith("state:Compressor:", StringComparison.Ordinal));
        Assert.Equal(-20, BuiltinState.Parse(Convert.FromBase64String(slot.State!)).Values["threshold"]);
        Assert.Equal("Vocal", presets.SelectedItem);   // the factory preset stays shown, as the preset memory asks
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheLimiterWindowShowsReductionLevelAndLatency(bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var (slot, track, engine) = Setup(BuiltinEffects.LimiterId);
        var editor = new LimiterEditor(FxTestKit.Host(track, engine), track, slot);
        var window = new BuiltinEditorWindow(slot, editor);
        window.Show();
        using var limiter = new LimiterEffect(Rate, 512);
        BuiltinTestKit.Run(limiter, BuiltinTestKit.Sine(300, 6, Rate, Rate), 480);
        engine.RaiseParameters(0, 0, limiter.GetParameters());
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("72 samples", DialogTestKit.Named<TextBlock>(window, "LimiterLatency").Text);
        Assert.True(DialogTestKit.Named<MeterBar>(window, "LimiterReductionMeter").Fraction > 0.3);
        Shot(window, light ? "limiter-light" : "limiter-dark");
        DialogTestKit.Named<Slider>(window, "Fxceiling").Value = 0.5;
        Assert.Contains(engine.Calls, c => c.StartsWith($"param:Limiter:{LimiterEffect.CeilingId}:", StringComparison.Ordinal));
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheDelayWindowShowsOnlyTheTimeControlThatApplies(bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var (slot, track, engine) = Setup(BuiltinEffects.DelayId);
        var editor = new DelayEditor(FxTestKit.Host(track, engine), track, slot);
        var window = new BuiltinEditorWindow(slot, editor);
        window.Show();
        using var delay = new DelayEffect(Rate, 512);
        engine.RaiseParameters(0, 0, delay.GetParameters());
        Dispatcher.UIThread.RunJobs();
        Assert.True(DialogTestKit.Named<ComboBox>(window, "Fxdivision").Parent!.Parent is not null);
        Assert.True(DialogTestKit.Named<Grid>(window, "FxdivisionRow").IsVisible);
        Assert.False(DialogTestKit.Named<Grid>(window, "FxtimeRow").IsVisible);
        Assert.Equal("1/8", DialogTestKit.Named<ComboBox>(window, "Fxdivision").SelectedItem);
        Shot(window, light ? "delay-light" : "delay-dark");
        delay.SetParameter(DelayEffect.SyncId, 0);
        engine.RaiseParameters(0, 0, delay.GetParameters());
        Assert.False(DialogTestKit.Named<Grid>(window, "FxdivisionRow").IsVisible);
        Assert.True(DialogTestKit.Named<Grid>(window, "FxtimeRow").IsVisible);
        Shot(window, light ? "delay-ms-light" : "delay-ms-dark");
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheGateWindowLightsWhenTheGateIsOpen(bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var (slot, track, engine) = Setup(BuiltinEffects.GateId);
        var editor = new GateEditor(FxTestKit.Host(track, engine), track, slot);
        var window = new BuiltinEditorWindow(slot, editor);
        window.Show();
        using var gate = new GateEffect(Rate, 512);
        gate.SetParameter(GateEffect.ThresholdId, -40);
        BuiltinTestKit.Run(gate, BuiltinTestKit.Sine(300, -60, Rate / 4, Rate), 480);
        engine.RaiseParameters(0, 0, gate.GetParameters());
        Dispatcher.UIThread.RunJobs();
        Assert.False(editor.Light.On);
        Assert.Equal("Closed", DialogTestKit.Named<TextBlock>(window, "GateState").Text);
        Shot(window, light ? "gate-closed-light" : "gate-closed-dark");
        BuiltinTestKit.Run(gate, BuiltinTestKit.Sine(300, -18, Rate / 4, Rate), 480);
        engine.RaiseParameters(0, 0, gate.GetParameters());
        Assert.True(editor.Light.On);
        Assert.Equal("Open", DialogTestKit.Named<TextBlock>(window, "GateState").Text);
        Shot(window, light ? "gate-open-light" : "gate-open-dark");
        window.Close();
    }
}
