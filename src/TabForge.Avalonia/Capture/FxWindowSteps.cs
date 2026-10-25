using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Audio;
using TabForge.Avalonia.Playback;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Capture;
using TabForge.LiveInput;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Capture;

// Owns: the verbs for the two FX chain windows a capture needs without any audio: live-fx (the live input window with its strip fed made-up meter and tuner readings) and fx-chain (an imported REAPER chain with containers whose plug-ins are not installed).
// Does not own: the windows (FxChainWindow, LiveInputStrip), the made-up chain text (CaptureFixtures), opening and placing ordinary tool windows (ToolSteps).
// Tests: TabForge.Avalonia.Tests.CaptureFixtureTests.
internal sealed class FxWindowSteps(CaptureSession session)
{
    private const double TickMs = 100;
    private LiveInputStrip? _strip;
    private (float In, float Out) _peaks;
    private (float Hz, float Clarity, int Seq) _tuner;
    private double _clockMs;

    public void Register(Dictionary<string, Func<CaptureStep, Task>> verbs)
    {
        verbs["live-fx"] = LiveFxAsync;
        verbs["fx-chain"] = FxChainAsync;
    }

    /// <summary>"open": the live input FX window (tuner on) with the strip reading the made-up values; "readings" (in, out, hz, repeat): sets the meter peaks and the tuner pitch (0 Hz: silence) and runs <c>repeat</c> frames.</summary>
    private async Task LiveFxAsync(CaptureStep step)
    {
        if (step.Text == "readings")
        {
            _peaks = ((float)step.Option("in", 0.0), (float)step.Option("out", 0.0));
            for (var i = 0; i < step.Option("repeat", 4); i++)
            {
                _tuner = ((float)step.Option("hz", 0.0), 0.95f, _tuner.Seq + 1);
                _clockMs += TickMs;
                (_strip ?? throw new InvalidOperationException("Run live-fx open first.")).Tick(_clockMs, DateTime.UtcNow);
                await Task.Delay(30);
            }
            await session.SettleAsync(80);
            return;
        }
        if (step.Text != "open") throw new InvalidOperationException($"Unknown live-fx action \"{step.Text}\".");
        var client = AvaloniaPlaybackComposition.CreateEngineClient(new PluginTrust());
        var host = new StripHost(client);
        var live = new LiveInputSession(host);
        live.Track.Rig.Plugins = FxChainWindow.SampleChain();
        live.Track.SoundSource = SoundSources.Plugins;
        _strip = new LiveInputStrip(live, client) { PeakSource = () => _peaks, TunerSource = () => _tuner };
        _strip.Tuner.Switch.IsChecked = true;
        var chain = new StandaloneFxChainHost(host.Settings, new HotkeySettings(), new PluginTrust(), null, new[] { live.Track }, _ => { })
        {
            AppWideChainTitle = FxChainPresenter.WindowTitle(MainWindowLiveInput.FxChainName), AppWideChainNote = LiveInputHint.Build(live), LiveStrip = _strip,
        };
        await Show(new FxChainWindow(chain, live.Track), "LiveInputFx", step);
    }

    /// <summary>"reaper": an FX chain window on an imported REAPER chain (a container with two plug-ins, a reverb and a bypassed delay) whose files exist on no machine.</summary>
    private async Task FxChainAsync(CaptureStep step)
    {
        if (step.Text != "reaper") throw new InvalidOperationException($"Unknown fx-chain action \"{step.Text}\".");
        var gone = @"C:\Program Files\Common Files\VST3\";
        var catalog = new[] { "Gate", "Amp", "Reverb", "Delay" }.Select(n => new VstPluginInfo("Example " + n, gone + "Example" + n + ".vst3", "VST3", "Sample Audio")).ToList();
        var imported = ReaperChainImporter.Build(ReaperChainImporter.Parse(CaptureFixtures.ReaperChain), catalog);
        var track = new TrackModel { Name = "Imported chain", SoundSource = SoundSources.Plugins };
        track.Rig.Plugins = imported.Chain;
        track.Rig.Plugins.Add(new PluginSlot { Name = "Example Chorus", Path = gone + "ExampleChorus.vst3" });   // a slot whose file is missing: the plain "not installed" row
        track.Rig.Containers = imported.Chain.FirstOrDefault()?.ChainContainers;   // the import packs the containers on the first plug-in; the window reads them from the rig
        foreach (var slot in imported.Chain) slot.ChainContainers = null;
        var chain = new StandaloneFxChainHost(new PluginSettings(), new HotkeySettings(), new PluginTrust(), null, new[] { track }, _ => { });
        await Show(new FxChainWindow(chain, track), "FxChain", step);
    }

    private async Task Show(FxChainWindow window, string name, CaptureStep step)
    {
        var size = step.NumbersOption("size");
        window.Width = size?[0] ?? 980;
        window.Height = size?[1] ?? 620;
        CaptureMetrics.PlaceOffScreen(window);
        window.Show();
        session.Tools[name] = window;
        await session.SettleAsync(CaptureSession.SettleMs * 2);
    }

    private sealed class StripHost(AudioEngineClient engine) : ILiveInputSessionHost
    {
        public PluginSettings Settings { get; } = new();
        public AudioEngineClient Engine { get; } = engine;
        public IUiThread UiThread { get; } = TabForge.Avalonia.Platform.AvaloniaUiThread.ForApplication();
        public void SyncAudioEngine() { }
        public void SaveSettings() { }
        public void SetStatus(string text) { }
        public (bool Restore, bool Remember) AskRestoreChain() => (false, false);
        public void ShowFxChain(Action closed) { }
    }
}
