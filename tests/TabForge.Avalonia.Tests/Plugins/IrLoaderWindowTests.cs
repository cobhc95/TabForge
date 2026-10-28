using System;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Audio.Contracts.Builtin;
using TabForge.AudioEngine.Builtin;
using TabForge.AudioEngine.Builtin.IrLoader;
using TabForge.AudioEngine.Plugins;
using TabForge.Avalonia.Views.Fx;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the IR Loader window: a chosen file goes to the engine as a partial state, into the slot (an undo step) and the recent list; the engine's readouts and
//   status (a missing file in its own words) show; the picture is computed; and the renders of the window (dark and light, loaded and missing) for review.
// Does not own: the sound (BuiltinEffectTests), the FX chain window's buttons (FxGenericControlsTests).
// Tests: this class. Renders go to TABFORGE_SHOTS_DIR when it is set, else the render folder of this class.
public sealed class IrLoaderWindowTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheWindowLoadsAFileShowsItsPictureAndReadouts(bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tf-irwin-" + Guid.NewGuid().ToString("N"))).FullName;
        var wav = Path.Combine(dir, "Cab 4x12 SM57.wav");
        File.WriteAllBytes(wav, IrWaveFile.Write([CabLikeIr()], 48000));
        var slot = new PluginSlot { Name = "IR Loader", Path = BuiltinEffects.IrLoaderId, Format = PluginFormats.Builtin, Vendor = BuiltinEffects.Vendor };
        var track = FxTestKit.Track([slot], "Guitar");
        var engine = new FxTestKit.FakeEngine();
        var settings = new PluginSettings();
        var editor = new IrLoaderEditor(FxTestKit.Host(track, engine, settings), track, slot);
        var window = new BuiltinEditorWindow(slot, editor);
        window.Show();

        editor.SetFile(IrLoaderEffect.FileKey, wav);
        Assert.Contains(engine.Calls, c => c.StartsWith("state:IR Loader:", StringComparison.Ordinal));
        Assert.Equal(wav, BuiltinState.Parse(Convert.FromBase64String(slot.State!)).Text[IrLoaderEffect.FileKey]);
        Assert.Equal(wav, settings.RecentImpulseFiles[0]);

        using var effect = new IrLoaderEffect(48000, 256);
        effect.SetState(BuiltinState.OneText(IrLoaderEffect.FileKey, wav));
        Assert.True(effect.Ready.Wait(10_000));
        var block = new[] { new float[256], new float[256] };
        var transport = new TransportInfo();
        for (var i = 0; i < 400; i++) ((IPluginInstance)effect).Process(block, block, 256, default, transport);   // a CPU reading, as while playing
        engine.RaiseParameters(0, 0, effect.GetParameters());
        var graph = DialogTestKit.Named<IrGraphView>(window, "IrGraph");
        for (var i = 0; i < 300 && !graph.HasData; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
        Assert.True(graph.HasData);
        Assert.Contains("Cab 4x12 SM57.wav", DialogTestKit.Named<TextBlock>(window, "IrStatus").Text);
        Assert.Contains("0 samples", DialogTestKit.Named<TextBlock>(window, "IrReadout").Text);
        Shot(window, light ? "ir-loader-light" : "ir-loader-dark");

        using var missing = new IrLoaderEffect(48000, 256);
        var gone = Path.Combine(dir, "Moved away.wav");
        missing.SetState(BuiltinState.OneText(IrLoaderEffect.FileKey, gone));
        Assert.True(missing.Ready.Wait(10_000));
        engine.RaiseParameters(0, 0, missing.GetParameters());
        for (var i = 0; i < 300 && graph.HasData; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
        Assert.Contains("Missing: " + gone, DialogTestKit.Named<TextBlock>(window, "IrStatus").Text);
        Shot(window, light ? "ir-loader-missing-light" : "ir-loader-missing-dark");
        window.Close();
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    private static void Shot(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        var dir = Environment.GetEnvironmentVariable("TABFORGE_SHOTS_DIR") is { Length: > 0 } custom ? Directory.CreateDirectory(custom).FullName : DialogTestKit.RenderDirectory(nameof(IrLoaderWindowTests));
        frame!.Save(Path.Combine(dir, name + ".png"));
    }

    /// <summary>A guitar-cabinet-like IR: a sharp start with a few resonances, decaying over about 100 ms.</summary>
    private static float[] CabLikeIr()
    {
        var rng = new Random(57);
        var ir = new float[4800];
        for (var i = 0; i < ir.Length; i++)
        {
            var t = i / 48000.0;
            ir[i] = (float)(Math.Exp(-t * 40) * (0.6 * Math.Sin(2 * Math.PI * 110 * t) + 0.3 * Math.Sin(2 * Math.PI * 2400 * t) + 0.25 * (rng.NextDouble() * 2 - 1)));
        }
        ir[0] = 1;
        return ir;
    }

    [AvaloniaFact]
    public void ChooseListsBrowseFirstAndAChosenFileLoadsFromTheHostingWindow()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tf-irchoose-" + Guid.NewGuid().ToString("N"))).FullName;
        var wav = Path.Combine(dir, "Room.wav");
        File.WriteAllBytes(wav, IrWaveFile.Write([CabLikeIr()], 48000));
        var slot = new PluginSlot { Name = "IR Loader", Path = BuiltinEffects.IrLoaderId, Format = PluginFormats.Builtin, Vendor = BuiltinEffects.Vendor };
        var track = FxTestKit.Track([slot], "Guitar");
        var engine = new FxTestKit.FakeEngine();
        var settings = new PluginSettings { RecentImpulseFiles = { Path.Combine(dir, "Old.wav") } };
        TopLevel? pickedFor = null;
        var editor = new IrLoaderEditor(FxTestKit.Host(track, engine, settings), track, slot, (top, _) => { pickedFor = top; return System.Threading.Tasks.Task.FromResult<string?>(wav); });
        var window = new BuiltinEditorWindow(slot, editor);
        window.Show();

        var choose = DialogTestKit.Named<Button>(window, "IrChoose" + IrLoaderEffect.FileKey);
        var menu = Assert.IsType<MenuFlyout>(choose.Flyout);
        choose.Flyout!.ShowAt(choose);
        Dispatcher.UIThread.RunJobs();
        var items = menu.Items.OfType<MenuItem>().ToList();
        Assert.Equal("Browse…", items[0].Header);
        Assert.Equal("Old.wav", items[1].Header);
        items[0].RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        for (var i = 0; i < 100 && slot.State is null; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
        Assert.Same(window, pickedFor);   // the picker belongs to the window that shows the editor
        Assert.Equal(wav, BuiltinState.Parse(Convert.FromBase64String(slot.State!)).Text[IrLoaderEffect.FileKey]);
        window.Close();
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    [AvaloniaFact]
    public void BrowseButtonResolvesTheHostingWindowAndLoadsThePickedFile()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tf-irbrowse-" + Guid.NewGuid().ToString("N"))).FullName;
        var wav = Path.Combine(dir, "Room.wav");
        File.WriteAllBytes(wav, IrWaveFile.Write([CabLikeIr()], 48000));
        var slot = new PluginSlot { Name = "IR Loader", Path = BuiltinEffects.IrLoaderId, Format = PluginFormats.Builtin, Vendor = BuiltinEffects.Vendor };
        var track = FxTestKit.Track([slot], "Guitar");
        var settings = new PluginSettings { RecentImpulseFiles = { Path.Combine(dir, "Old.wav") } };
        TopLevel? pickedFor = null; string? startedIn = null;
        var editor = new IrLoaderEditor(FxTestKit.Host(track, new FxTestKit.FakeEngine(), settings), track, slot, (top, folder) => { pickedFor = top; startedIn = folder; return System.Threading.Tasks.Task.FromResult<string?>(wav); });
        var window = new BuiltinEditorWindow(slot, editor);
        window.Show();
        DialogTestKit.Named<Button>(window, "IrBrowse" + IrLoaderEffect.FileKey).RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        for (var i = 0; i < 100 && slot.State is null; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
        Assert.Same(window, pickedFor);
        Assert.Equal(dir, startedIn);
        Assert.Equal(wav, BuiltinState.Parse(Convert.FromBase64String(slot.State!)).Text[IrLoaderEffect.FileKey]);
        window.Close();
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }
}
