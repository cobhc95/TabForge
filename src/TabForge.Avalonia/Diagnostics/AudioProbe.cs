using System;
using System.Linq;
using System.Threading;
using NAudio.Wave;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Output;

namespace TabForge.Avalonia.Diagnostics;

// Owns: the Linux package's headless audio-path probe (--audio-probe): opens the real miniaudio
// output (never the null device) on the default device, holds it briefly and closes it.
// Does not own: the backend (MiniaudioBackend) or the native library (TfPortable).
// Tests: run by the wine-stage17 workflow under virtual audio (PulseAudio null sink, JACK dummy).
internal static class AudioProbe
{
    public const string Argument = "--audio-probe";

    public static int Run(string[] args)
    {
        // --audio-probe [seconds]
        var seconds = args.Length > 1 && int.TryParse(args[1], out var s) ? Math.Clamp(s, 1, 30) : 5;
        try
        {
            using var backend = new MiniaudioBackend(nullDevice: false);
            if (!backend.IsAvailable) return Fail("the system audio library is not available (libtfportable missing or no backend started)");
            var devices = backend.Devices(capture: false);
            Console.Out.WriteLine($"audio-probe backend={backend.BackendName} devices={devices.Count}");
            foreach (var device in devices.Take(8))
                Console.Out.WriteLine($"audio-probe device name={device.Name} default={device.IsDefault} channels={device.MaxChannels} rate={device.NativeRate}");
            var threads = new EngineThreads(new EngineLogWriter());
            var opened = backend.OpenOutput(new EngineConfig("miniaudio", "", 48000, 512, false), new Silence(), threads);
            try
            {
                Console.Out.WriteLine($"audio-probe opened {opened.Description} latency={opened.LatencyMs}ms");
                opened.Player.Play();
                Thread.Sleep(seconds * 1000);
                opened.Player.Stop();
            }
            finally
            {
                opened.Player.Dispose();
            }
            Console.Out.WriteLine("audio-probe ok");
            return 0;
        }
        catch (Exception ex)
        {
            return Fail(ex.GetBaseException().Message);
        }
    }

    private sealed class Silence : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public int Read(float[] buffer, int offset, int sampleCount)
        {
            Array.Clear(buffer, offset, sampleCount);
            return sampleCount;
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"audio-probe FAILED: {message}");
        return 1;
    }
}
