using TabForge.Platform.Contracts;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Output;
using TabForge.Platform;
using TabForge.Platform.Portable;
using TabForge.Playback;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: cross-platform Stage 3, the devices: miniaudio on its null device (list, play, capture, lost-device report), RtMidi (list, open, send), and the LAME
//   round trip (encode a sine, decode with NLayer, check the duration). Each test runs only when its native library sits next to the test program
//   (tfportable.dll built from native/tfportable; libmp3lame from the NAudio.Lame test package), so a machine without them stays green.
// Does not own: which adapter an OS chooses (PortableBackendTests).
// Tests: itself.
public sealed class PortableDeviceTests
{
    private static EngineConfig Config() => new(AudioDriverNames.Miniaudio, "", 48000, 256, false);

    private sealed class CountingSource : ISampleProvider
    {
        private readonly SignalGenerator _sine = new(48000, 2) { Type = SignalGeneratorType.Sin, Frequency = 440, Gain = 0.25 };
        public long Reads;
        public WaveFormat WaveFormat => _sine.WaveFormat;
        public int Read(float[] buffer, int offset, int count) { Interlocked.Increment(ref Reads); return _sine.Read(buffer, offset, count); }
    }

    private sealed class ThrowingSource : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public int Read(float[] buffer, int offset, int count) => throw new InvalidOperationException("boom");
    }

    private static bool WaitFor(Func<bool> condition, int ms = 3000)
    {
        var until = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < until) { if (condition()) return true; Thread.Sleep(10); }
        return condition();
    }

    [Fact]
    public void MiniaudioNullDeviceListsAndPlays()
    {
        if (TfPortable.Current is null) return;
        using var backend = new MiniaudioBackend(nullDevice: true);
        Assert.True(backend.IsAvailable);
        Assert.Equal("Null", backend.BackendName);
        Assert.NotEmpty(backend.Devices(capture: false));
        Assert.NotEmpty(backend.Devices(capture: true));
        Assert.Equal(48000, backend.SampleRateFor(Config()));
        var source = new CountingSource();
        var opened = backend.OpenOutput(Config(), source, new EngineThreads(new EngineLogWriter()));
        try
        {
            Assert.True(opened.LatencyMs >= 1);
            Assert.Contains("Null", opened.Description);
            Assert.Equal(0, source.Reads);   // opened, not started
            opened.Player.Play();
            Assert.True(WaitFor(() => source.Reads > 5), "the null device never pulled the mix");
            Assert.Equal(NAudio.Wave.PlaybackState.Playing, opened.Player.PlaybackState);
        }
        finally { opened.Player.Stop(); opened.Player.Dispose(); }
        var after = source.Reads;
        Thread.Sleep(80);
        Assert.Equal(after, source.Reads);   // stopped: no more callbacks
    }

    [Fact]
    public void MiniaudioNullDeviceCapturesIntoTheInputRing()
    {
        if (TfPortable.Current is null) return;
        using var backend = new MiniaudioBackend(nullDevice: true);
        using var input = backend.OpenInput(Config(), 48000, new EngineLogWriter());
        Assert.True(input.Channels >= 2);
        Assert.True(WaitFor(() => input.TotalFrames > 1000), "no input frames arrived");
        Assert.Null(input.TakeFault());
        Assert.True(backend.RecordableInputs(Config(), new EngineLogWriter()).Length >= 2);
    }

    [Fact]
    public void MiniaudioOutputReportsAFailingMixThroughPlaybackStopped()
    {
        if (TfPortable.Current is null) return;
        using var backend = new MiniaudioBackend(nullDevice: true);
        var opened = backend.OpenOutput(Config(), new ThrowingSource(), new EngineThreads(new EngineLogWriter()));
        Exception? reported = null;
        opened.Player.PlaybackStopped += (_, e) => reported = e.Exception;
        try
        {
            opened.Player.Play();
            Assert.True(WaitFor(() => reported is not null), "the failing mix was not reported");   // OutputLossWatch reopens the device from this
            Assert.Contains("boom", reported!.Message);
        }
        finally { opened.Player.Dispose(); }
    }

    [Fact]
    public void RtMidiListsOpensAndSends()
    {
        if (TfPortable.Current is null) return;
        var inputs = new RtMidiInputBackend();
        Assert.True(inputs.IsAvailable);
        var names = inputs.InputNames();
        Assert.Throws<MidiPortException>(() => { using var port = inputs.Open(9999); port.Start(); });   // no such device
        if (names.Count > 0)
        {
            using var port = inputs.Open(0);
            port.RawMessage += _ => { };
            try { port.Start(); port.Stop(); } catch (MidiPortException) { /* swallow: the first device may be in use by another program */ }
        }
        var devices = new RtMidiOutputDevices();
        Assert.True(devices.IsSupported);
        var count = devices.OutputDeviceCount();
        var output = new WinMmMidiOutput(devices);
        Assert.Equal("Default MIDI output", output.Devices[0].Name);
        Assert.Equal((int)count + 1, output.Devices.Count);
        Assert.False(devices.TryGetOutputProductName(count + 5, out _));
        if (count == 0) return;
        Assert.True(devices.TryGetOutputProductName(0, out var name) && name.Length > 0);
        var handle = devices.OpenOutput(0);
        if (handle == IntPtr.Zero) return;   // in use elsewhere
        devices.ShortMessage(handle, 0x90 | (60 << 8) | (1 << 16));   // note on, velocity 1: inaudible
        devices.ShortMessage(handle, 0x80 | (60 << 8));
        devices.ResetOutput(handle);
        devices.CloseOutput(handle);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public void LameEncodesASineThatDecodesToTheSameLength(int channels)
    {
        var encoder = new LameMp3Encoder();
        if (!encoder.IsAvailable()) return;   // libmp3lame is not next to this program
        const double seconds = 1.5;
        var sine = new SignalGenerator(44100, channels) { Type = SignalGeneratorType.Sin, Frequency = 440, Gain = 0.5 }.Take(TimeSpan.FromSeconds(seconds)).ToWaveProvider16();
        var path = Path.Combine(Path.GetTempPath(), "tabforge-lame-" + Guid.NewGuid().ToString("N") + ".mp3");
        try
        {
            encoder.Encode(sine, path, 128_000);
            var size = new FileInfo(path).Length;
            Assert.InRange(size, 128_000 / 8 * seconds * 0.8, 128_000 / 8 * seconds * 1.3);   // constant bitrate: about 24 KB
            using var decoded = new NLayer.MpegFile(path);
            Assert.Equal(44100, decoded.SampleRate);
            Assert.Equal(channels, decoded.Channels);
            Assert.InRange(decoded.Duration.TotalSeconds, seconds - 0.1, seconds + 0.1);
            var samples = new float[44100 * channels];   // one second: past the encoder delay
            var read = decoded.ReadSamples(samples, 0, samples.Length);
            Assert.True(read > 0);
            Assert.True(samples.Take(read).Max(s => Math.Abs(s)) > 0.05f, "the decoded MP3 is silent");   // a tone, not silence
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void LameRejectsWhatItCannotEncode()
    {
        var encoder = new LameMp3Encoder();
        var floats = new SignalGenerator(44100, 2).Take(TimeSpan.FromMilliseconds(10)).ToWaveProvider();
        Assert.Throws<ArgumentException>(() => encoder.Encode(floats, Path.Combine(Path.GetTempPath(), "never.mp3"), 128_000));
    }
}
