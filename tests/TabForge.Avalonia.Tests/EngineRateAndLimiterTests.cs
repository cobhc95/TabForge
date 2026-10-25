using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NAudio.Wave;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Mixing;
using TabForge.AudioEngine.Output;
using TabForge.AudioEngine.Plugins;
using Xunit;
using EH = TabForge.AudioEngine.EngineHost.Headless;

namespace TabForge.Avalonia.Tests;

// Owns: a changed output sample rate reaching the engine (Configure rebuilds at the new rate, 0 = device default), an audio clip keeping its pitch and
// speed at any engine rate, and the render safety limiter being on or off as the render settings say.
// Does not own: the Preferences rows (OutputSettingsTests) or the maths of the limiter itself.
// Tests: this file.
[Collection("EngineHeadless")]
public class EngineRateAndLimiterTests
{
    private const int Slot = 0;
    private const double ClipHz = 440;

    private static EngineConfig Null(int rate) => new(AudioOutputFactory.Null, AudioOutputFactory.NullManual, rate, 256, false);

    [Fact]
    public void ARateOfZeroMeansTheDefaultAndOtherRatesAreUsedAsChosen()
    {
        Assert.Equal(48000, AudioOutputFactory.SampleRateFor(new EngineConfig(AudioDriverNames.DirectSound, "", 0, 256, false)));
        Assert.Equal(96000, AudioOutputFactory.SampleRateFor(new EngineConfig(AudioDriverNames.DirectSound, "", 96000, 256, false)));
        Assert.Equal(0, ReadBack(new EngineConfig(AudioDriverNames.Asio, "", 0, 256, false)).SampleRate);   // 0 survives the wire; the engine resolves it
        Assert.Equal(44100, ReadBack(new EngineConfig(AudioDriverNames.Asio, "", 44100, 256, false)).SampleRate);
    }

    [Fact]
    public void ExclusiveDeviceDefaultResolvesToTheDevicesOwnRate()
    {
        Assert.Equal(96000, AudioOutputFactory.ExclusiveRate(96000, r => r == 96000, 48000));
        Assert.Equal(44100, AudioOutputFactory.ExclusiveRate(0, r => r == 44100, 48000));
        Assert.Equal(88200, AudioOutputFactory.ExclusiveRate(48000, r => r is 88200 or 192000, 48000));
        Assert.Equal(48000, AudioOutputFactory.ExclusiveRate(0, _ => false, 48000));
    }

    private static EngineConfig ReadBack(EngineConfig config)
    {
        using var body = new MemoryStream();
        using (var w = new BinaryWriter(body, System.Text.Encoding.UTF8, leaveOpen: true)) w.Write(config);
        body.Position = 0;
        return new BinaryReader(body).ReadConfig();
    }

    [Fact]
    public void ApplyingAnotherRateReconfiguresTheEngineOutput()
    {
        using var shared = SharedBlock.Create($"tf-rate-{Guid.NewGuid():N}");
        EH.Attach(shared, (_, _, _) => new Silent());
        try
        {
            EH.Configure(Null(44100));
            Assert.Equal(44100, EH.SampleRate);
            Assert.Equal(44100, EH.Mix!.WaveFormat.SampleRate);
            EH.Configure(Null(96000));
            Assert.Equal(96000, EH.SampleRate);
            Assert.Equal(96000, EH.Mix!.WaveFormat.SampleRate);
            EH.Configure(Null(0));
            Assert.Equal(AudioOutputFactory.DefaultRate, EH.SampleRate);
        }
        finally { EH.Detach(); }
    }

    [Theory]
    [InlineData(44100)]
    [InlineData(48000)]
    [InlineData(96000)]
    public void AClipKeepsItsPitchAndSpeedWhateverRateTheEngineRunsAt(int rate)
    {
        var dir = NewDir();
        try
        {
            var clip = Path.Combine(dir, "tone.wav");
            WriteTone(clip, 44100, ClipHz, 0.5, 2.0);
            var (left, fileRate, _) = RenderMaster(dir, rate, new ClipSpec(clip, 0, 0, 2.0, 0, 0, 1), RenderFormat.Float32, limiter: true);
            Assert.Equal(rate, fileRate);
            var (first, last, crossings) = Crossings(left);
            var seconds = (last - first) / (double)rate;
            Assert.InRange(seconds, 1.95, 2.05);                                // the same length in seconds
            Assert.InRange((crossings - 1) / 2.0 / seconds, ClipHz * 0.99, ClipHz * 1.01);   // the same pitch
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void TheSafetyLimiterKeepsARenderedMixUnderItsCeilingAndOffKeepsTheClipping()
    {
        var dir = NewDir();
        try
        {
            var clip = Path.Combine(dir, "loud.wav");
            WriteTone(clip, 48000, 220, 2.0, 1.0);   // twice full scale
            var spec = new ClipSpec(clip, 0, 0, 1.0, 0, 0, 1);
            var on = Peak(RenderMaster(dir, 48000, spec, RenderFormat.Float32, limiter: true).Left);
            var off = Peak(RenderMaster(dir, 48000, spec, RenderFormat.Float32, limiter: false).Left);
            Assert.InRange(on, 0.9, SafetyLimiter.DefaultCeiling + 0.001f);
            Assert.InRange(off, 1.5, 2.1);   // far above full scale: nothing was limited
            Assert.Equal(0, RenderMaster(dir, 48000, spec, RenderFormat.Pcm24, limiter: true).Clipped);
            Assert.True(RenderMaster(dir, 48000, spec, RenderFormat.Pcm24, limiter: false).Clipped > 0);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- helpers

    private static string NewDir() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tf-rate-" + Guid.NewGuid().ToString("N"))).FullName;

    private static void WriteTone(string path, int rate, double hz, double amplitude, double seconds)
    {
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(rate, 1));
        for (var i = 0; i < (int)(rate * seconds); i++) writer.WriteSample((float)(amplitude * Math.Sin(2 * Math.PI * hz * i / rate)));
    }

    /// <summary>Renders the clip through the real offline renderer on the headless engine running at <paramref name="rate"/>; the master file left channel, its rate and its clipped sample count.</summary>
    private static (float[] Left, int Rate, long Clipped) RenderMaster(string dir, int rate, ClipSpec clip, RenderFormat format, bool limiter)
    {
        using var shared = SharedBlock.Create($"tf-rate-{Guid.NewGuid():N}");
        EH.Attach(shared, (_, _, _) => new Silent());
        try
        {
            EH.Configure(Null(rate));
            EH.LoadChain(Slot, "t", false, new List<PluginSpec> { new("silent", "VST2", true, true, 100, null, Id: "i", LocationTrusted: true) });
            var master = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".wav");
            var events = Path.Combine(dir, "events.bin");
            RenderEventFile.Write(events, Array.Empty<RenderEvent>());
            var spec = new RenderSpec
            {
                StartFrame = 0, EndFrame = 3L * rate, TailMode = RenderTailMode.None, Format = format, MasterPath = master, EventFile = events,
                SafetyLimiter = limiter, Threads = RenderThreads.One,
                Slots = { new RenderSlot { Slot = Slot, InMaster = true, Clips = new List<ClipSpec> { clip } } },
            };
            var renderer = new OfflineRenderer(spec, EH.Mix!, shared, EH.SampleRate, 256, EH.Threads, () => false, _ => { });
            RenderResult result;
            renderer.Prepare();
            try { result = renderer.Run(); } finally { renderer.Restore(); }
            using var reader = new WaveFileReader(master);
            var provider = reader.ToSampleProvider();
            var all = new List<float>();
            var buffer = new float[4096];
            int n;
            while ((n = provider.Read(buffer, 0, buffer.Length)) > 0) all.AddRange(buffer.Take(n));
            var left = all.Where((_, i) => i % 2 == 0).ToArray();
            return (left, reader.WaveFormat.SampleRate, result.Files.First(f => f.Slot == -1).ClippedSamples);
        }
        finally { EH.Detach(); }
    }

    private static float Peak(float[] samples) => samples.Max(Math.Abs);

    /// <summary>The first and last non-silent frame and how many times the signal crosses zero between them.</summary>
    private static (int First, int Last, int Crossings) Crossings(float[] x)
    {
        var first = Array.FindIndex(x, v => Math.Abs(v) > 0.01f);
        var last = Array.FindLastIndex(x, v => Math.Abs(v) > 0.01f);
        var crossings = 0;
        for (var i = first + 1; i <= last; i++) if (x[i - 1] < 0 != x[i] < 0) crossings++;
        return (first, last, crossings);
    }

    /// <summary>An instrument with nothing to play: the render hears only the audio clip.</summary>
    private sealed class Silent : IPluginInstance
    {
        public string Path => "silent";
        public bool IsInstrument => true;
        public bool HasEditor => false;
        public int LatencySamples => 0;
        public void Process(float[][] input, float[][] output, int frames, ReadOnlySpan<BlockMidi> midi, in TransportInfo transport) { foreach (var channel in output) Array.Clear(channel, 0, frames); }
        public byte[]? GetState() => null;
        public void SetState(byte[] state) { }
        public (int Width, int Height)? OpenEditor(IntPtr parent) => null;
        public void CloseEditor() { }
        public void EditorIdle() { }
        public void Dispose() { }
    }
}
