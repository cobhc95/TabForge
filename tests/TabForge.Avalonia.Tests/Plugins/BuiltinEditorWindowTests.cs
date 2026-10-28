using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Audio.Contracts.Builtin;
using TabForge.AudioEngine.Builtin;
using TabForge.AudioEngine.Builtin.Compressor;
using TabForge.AudioEngine.Builtin.Eq;
using TabForge.AudioEngine.Builtin.Gate;
using TabForge.AudioEngine.Builtin.Limiter;
using TabForge.Avalonia.Views.Fx;
using TabForge.Models;
using TabForge.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the own windows of the built-in effects: each opens, shows what the engine reports (curve, meters, light, controls), sends a changed control to the engine, and is
//   rendered dark and light for review; the EQ points drag (frequency and gain), turn Q with the wheel and reset by double-click; the compressor factory presets
//   apply as one undo step; the Add path finds a window for each effect and the generic parameter list stays complete.
// Does not own: the sound (Builtin*Tests), the IR Loader window (IrLoaderWindowTests).
// Tests: this class. Renders go to TABFORGE_SHOTS_DIR when it is set, else the render folder of this class.
public sealed class BuiltinEditorWindowTests
{
    private const int Rate = 48000;

    private static (PluginSlot Slot, TrackModel Track, FxTestKit.FakeEngine Engine) Setup(string id)
    {
        var info = BuiltinEffects.Find(id)!;
        var slot = new PluginSlot { Name = info.Name, Path = id, Format = PluginFormats.Builtin, Vendor = BuiltinEffects.Vendor };
        var track = FxTestKit.Track([slot], "Guitar");
        return (slot, track, new FxTestKit.FakeEngine());
    }

    private static void Shot(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        var dir = Environment.GetEnvironmentVariable("TABFORGE_SHOTS_DIR") is { Length: > 0 } custom ? Directory.CreateDirectory(custom).FullName : DialogTestKit.RenderDirectory(nameof(BuiltinEditorWindowTests));
        frame!.Save(Path.Combine(dir, name + ".png"));
    }

    private static void Feed(BuiltinEffect effect, float[] mono, int block = 480) => BuiltinTestKit.Run(effect, mono, block);

    [Fact]
    public void EveryBuiltinEffectHasAWindowAndTheGenericListIsComplete()
    {
        foreach (var info in BuiltinEffects.All)
        {
            var slot = new PluginSlot { Path = info.Id, Format = PluginFormats.Builtin };
            Assert.True(TabForge.Avalonia.Views.Fx.BuiltinEditors.HasWindow(slot), info.Name);
            Assert.True(TabForge.Avalonia.Views.Fx.BuiltinEditors.IsBuiltin(slot));
            using var effect = BuiltinEffectFactory.Create(info.Id, Rate, 512);
            var list = effect.GetParameters();
            Assert.NotEmpty(list);
            Assert.All(list, p => Assert.False(string.IsNullOrWhiteSpace(p.Name)));
            Assert.All(list.Where(p => p.Stepped && !p.ReadOnly), p => Assert.True(p.Max >= p.Min));
        }
    }

    [AvaloniaFact]
    public void OpeningABuiltinOpensItsOwnWindowOnceAndFindsIt()
    {
        var (slot, track, engine) = Setup(BuiltinEffects.EqId);
        var owner = new Window();
        owner.Show();
        Assert.True(BuiltinEditors.TryOpen(owner, FxTestKit.Host(track, engine), track, slot));
        var first = BuiltinEditors.Find(owner, slot);
        Assert.IsType<EqEditor>(Assert.IsType<BuiltinEditorWindow>(first).Editor);
        Assert.True(BuiltinEditors.TryOpen(owner, FxTestKit.Host(track, engine), track, slot));
        Assert.Single(owner.OwnedWindows);
        first!.Close();
        owner.Close();
    }

    [AvaloniaFact]
    public void SelectingABuiltinEmbedsItsEditorInTheFxChainWindowAndOpenEditorPopsItOut()
    {
        var (slot, track, engine) = Setup(BuiltinEffects.EqId);
        var window = FxTestKit.Window(track, FxTestKit.Host(track, engine));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        FxTestKit.List(window).Click(0);   // select the EQ row: its editor appears at once
        Dispatcher.UIThread.RunJobs();
        var host = DialogTestKit.Named<BuiltinEditorHost>(window, "BuiltinEditorHost");
        Assert.True(host.IsVisible, $"focused {FxTestKit.List(window).FocusedIndex} count {FxTestKit.List(window).Count} slot {host.Slot} editor {host.Editor}");
        Assert.Same(slot, host.Slot);
        var editor = Assert.IsType<EqEditor>(host.Editor);
        Assert.True(editor.IsEffectivelyVisible);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && (t.Text ?? "").Contains("has its own window"));
        Assert.False(window.ParameterPanel.IsVisible);
        Shot(window, "fxchain-eq-embedded");

        FxTestKit.PressButton(window, "ParametersButton");   // "Open editor": pops the editor out; the embedded one stays
        Assert.IsType<BuiltinEditorWindow>(BuiltinEditors.Find(window, slot));
        Assert.True(host.IsVisible);
        BuiltinEditors.Find(window, slot)!.Close();
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheEqWindowShowsTheCurveSendsDragsWheelAndReset(bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var (slot, track, engine) = Setup(BuiltinEffects.EqId);
        var editor = new EqEditor(FxTestKit.Host(track, engine), track, slot);
        var window = new BuiltinEditorWindow(slot, editor);
        window.Show();
        using var eq = new EqEffect(Rate, 512);
        foreach (var (band, hz, gain, q) in new[] { (0, 120.0, 4.0, 0.8), (1, 420.0, -3.5, 2.0), (2, 3200.0, 5.0, 1.2), (3, 9000.0, 3.0, 0.8) })
        {
            eq.SetParameter(EqEffect.BandId(band, EqEffect.FreqField), hz);
            eq.SetParameter(EqEffect.BandId(band, EqEffect.GainField), gain);
            eq.SetParameter(EqEffect.BandId(band, EqEffect.QField), q);
        }
        eq.SetParameter(EqEffect.LowCutFreqId, 70);
        eq.SetParameter(EqEffect.LowCutSlopeId, EqEffect.Slope12);
        engine.RaiseParameters(0, 0, eq.GetParameters());
        Dispatcher.UIThread.RunJobs();
        var curve = editor.Curve;
        Assert.Equal(4.0, curve.ValueAt(EqEffect.Index(0, EqEffect.GainField)));
        Assert.Equal("Fxb2gain", DialogTestKit.Named<Slider>(window, "Fxb2gain").Name);
        Shot(window, light ? "eq-light" : "eq-dark");

        var dot = curve.TranslatePoint(curve.DotPosition(1), window)!.Value;
        window.MouseDown(dot, MouseButton.Left);
        var target = curve.TranslatePoint(new Point(curve.DotPosition(1).X + 80, curve.DotPosition(1).Y - 40), window)!.Value;
        window.MouseMove(target);
        window.MouseUp(target, MouseButton.Left);
        Assert.Equal(1, curve.Selected);
        Assert.Contains(engine.Calls, c => c.StartsWith($"param:EQ:{EqEffect.BandId(1, EqEffect.FreqField)}:", StringComparison.Ordinal));
        Assert.Contains(engine.Calls, c => c.StartsWith($"param:EQ:{EqEffect.BandId(1, EqEffect.GainField)}:", StringComparison.Ordinal));
        Assert.True(curve.ValueAt(EqEffect.Index(1, EqEffect.FreqField)) > 420);
        Assert.True(curve.ValueAt(EqEffect.Index(1, EqEffect.GainField)) > -3.5);

        var before = curve.ValueAt(EqEffect.Index(1, EqEffect.QField));
        window.MouseWheel(curve.TranslatePoint(curve.DotPosition(1), window)!.Value, new Vector(0, 1));
        Assert.True(curve.ValueAt(EqEffect.Index(1, EqEffect.QField)) > before);
        Assert.Contains(engine.Calls, c => c.StartsWith($"param:EQ:{EqEffect.BandId(1, EqEffect.QField)}:", StringComparison.Ordinal));

        curve.ResetBand(1);
        Assert.Equal(EqEffect.Descriptors[EqEffect.Index(1, EqEffect.FreqField)].Default, curve.ValueAt(EqEffect.Index(1, EqEffect.FreqField)));
        Assert.Equal(0.0, curve.ValueAt(EqEffect.Index(1, EqEffect.GainField)));
        Assert.Equal(EqEffect.BellQ, curve.ValueAt(EqEffect.Index(1, EqEffect.QField)));
        window.Close();
    }
}
