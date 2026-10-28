using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NAudio.Wave;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Mixing;
using TabForge.AudioEngine.Output;
using TabForge.AudioEngine.Plugins;
using Xunit;
using EH = TabForge.AudioEngine.EngineHost.Headless;

namespace TabForge.Avalonia.Tests;

// Owns: the Monitor FX chain staying live-only: the speakers hear it, an offline render (export, bounce, stems, video export: all use OfflineRenderer) never
//   runs it even when its slot is named in the render, and the live recording tap (live video) takes the mix before it.
// Does not own: the chain's UI (MonitorFxTitleButtonTests), the renderer's other rules (EngineRateAndLimiterTests).
// Tests: this file.
[Collection("EngineHeadless")]
public sealed class MonitorFxRenderTests
{
    private const float Level = 0.25f;

    [Fact]
    public void TheMonitorChainIsHeardLiveButNeverRenderedOrRecorded()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tf-monitor-" + Guid.NewGuid().ToString("N"))).FullName;
        using var shared = SharedBlock.Create($"tf-monitor-{Guid.NewGuid():N}");
        EH.Attach(shared, (spec, _, _) => spec.Id == "mon" ? new Constant(0f, false) : new Constant(Level, true));
        try
        {
            EH.Configure(new EngineConfig(AudioOutputFactory.Null, AudioOutputFactory.NullManual, 48000, 256, false));
            EH.LoadChain(0, "t", false, new List<PluginSpec> { new("src", "VST2", true, true, 100, null, Id: "src", LocationTrusted: true) });
            EH.LoadChain(MixEngine.MonitorSlot, "Monitor", false, new List<PluginSpec> { new("mon", "VST2", false, true, 100, null, Id: "mon", LocationTrusted: true) });
            Assert.NotNull(EH.ChainAt(MixEngine.MonitorSlot));

            // Live: the speakers get the monitor chain (here: silence); the recording tap gets the mix before it.
            var taped = 0f;
            EH.Tap.Sink = (_, _, block, frames) => { lock (dir) taped = Math.Max(taped, block.Take(frames * 2).Max(Math.Abs)); };
            EH.Mix!.Tap = EH.Tap;
            EH.Tap.Enable(true, EH.SampleRate);
            var live = new float[512];
            var heard = 0f;
            for (var i = 0; i < 20; i++) { EH.Mix!.Read(live, 0, live.Length); heard = Math.Max(heard, live.Max(Math.Abs)); }
            for (var wait = 0; wait < 100 && Volatile.Read(ref taped) == 0; wait++) Thread.Sleep(10);
            EH.Tap.Enable(false, EH.SampleRate);
            Assert.Equal(0f, heard);
            lock (dir) Assert.True(taped > 0.05f, $"the recording tap got {taped}: it must take the mix before the Monitor FX");

            // Offline: even a render that names the monitor slot leaves it out.
            var master = Path.Combine(dir, "master.wav");
            var events = Path.Combine(dir, "events.bin");
            RenderEventFile.Write(events, Array.Empty<RenderEvent>());
            var spec = new RenderSpec
            {
                StartFrame = 0, EndFrame = 48000 / 2, TailMode = RenderTailMode.None, MasterPath = master, EventFile = events, Threads = RenderThreads.One,
                Slots = { new RenderSlot { Slot = 0, InMaster = true }, new RenderSlot { Slot = MixEngine.MonitorSlot, InMaster = true } },
            };
            var renderer = new OfflineRenderer(spec, EH.Mix!, shared, EH.SampleRate, 256, EH.Threads, () => false, _ => { });
            renderer.Prepare();
            try { renderer.Run(); } finally { renderer.Restore(); }
            using var reader = new WaveFileReader(master);
            var provider = reader.ToSampleProvider();
            var buffer = new float[8192];
            var peak = 0f;
            int n;
            while ((n = provider.Read(buffer, 0, buffer.Length)) > 0) peak = Math.Max(peak, buffer.Take(n).Max(Math.Abs));
            Assert.True(peak > 0.05f, $"the render peaked at {peak}: the monitor chain (silence) must not be in it");
        }
        finally
        {
            EH.Detach();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    /// <summary>An instrument that plays a constant level, or an effect that outputs it (0: silence).</summary>
    private sealed class Constant(float level, bool instrument) : IPluginInstance
    {
        public string Path => instrument ? "src" : "mon";
        public bool IsInstrument => instrument;
        public bool HasEditor => false;
        public int LatencySamples => 0;
        public void Process(float[][] input, float[][] output, int frames, ReadOnlySpan<BlockMidi> midi, in TransportInfo transport) { foreach (var channel in output) Array.Fill(channel, level, 0, frames); }
        public byte[]? GetState() => null;
        public void SetState(byte[] state) { }
        public (int Width, int Height)? OpenEditor(IntPtr parent) => null;
        public void CloseEditor() { }
        public void EditorIdle() { }
        public void Dispose() { }
    }
}
