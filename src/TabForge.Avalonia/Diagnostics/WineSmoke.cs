using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Plugins;
using TabForge.Plugins;

namespace TabForge.Avalonia.Diagnostics;

// Owns: the Linux package's headless smoke path (--wine-smoke): the shipped self-contained engine starts the bundled Wine,
// prepares the private prefix through the real WineRuntime/Vst32Bridge route, loads one Windows plug-in and renders blocks.
// Does not own: the helper protocol (RemotePlugin), the routing decision (Vst32Bridge) or first-run setup (WineRuntime).
// Tests: the wine-stage6 workflow runs it from the extracted tar.gz and the mounted AppImage.
internal static class WineSmoke
{
    public const string Argument = "--wine-smoke";
    private const int Rate = 48000;

    public static int Run(string[] args)
    {
        // --wine-smoke <dll-or-vst3> <instrument 0|1> [frames] [blocks]
        if (args.Length < 3 || !int.TryParse(args[2], out var instrument)) return Usage();
        var path = Path.GetFullPath(args[1]);
        if (!File.Exists(path) && !Directory.Exists(path)) return Fail($"not found: {path}");
        var frames = args.Length > 3 && int.TryParse(args[3], out var f) ? Math.Clamp(f, 64, 2048) : 512;
        var blocks = args.Length > 4 && int.TryParse(args[4], out var b) ? Math.Clamp(b, 4, 64) : 16;
        try
        {
            var spec = new PluginSpec(path, path.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase) ? "VST3" : "VST2",
                instrument != 0, Enabled: true, Wet: 100, State: null, Name: Path.GetFileNameWithoutExtension(path),
                Id: "wine-smoke", LocationTrusted: false, ExpectedSha256: Sha256(path));
            var launch = Vst32Bridge.Launch(spec);
            var threads = new EngineThreads(new EngineLogWriter());
            using var plugin = new RemotePlugin(spec, Rate, frames, threads, launch);
            plugin.SetOfflineMode(true);
            var input = new[] { new float[frames], new float[frames] };
            var output = new[] { new float[frames], new float[frames] };
            double sum = 0;
            long count = 0;
            var finite = true;
            var phase = 0.0;
            for (var block = 0; block < blocks; block++)
            {
                for (var i = 0; i < frames; i++)
                {
                    var sample = (float)(0.25 * Math.Sin(phase));
                    phase += 2 * Math.PI * 1000 / Rate;
                    input[0][i] = sample;
                    input[1][i] = sample;
                }
                var midi = instrument != 0 && block == 0
                    ? new[] { new BlockMidi { Frame = 0, Status = 0x90, Data1 = 60, Data2 = 100 } }
                    : Array.Empty<BlockMidi>();
                plugin.Process(input, output, frames, midi, Transport(block, frames));
                finite &= output[0].All(float.IsFinite) && output[1].All(float.IsFinite);
                if (block < 2) continue;
                foreach (var sample in output[0]) { sum += sample * (double)sample; count++; }
                foreach (var sample in output[1]) { sum += sample * (double)sample; count++; }
            }
            var rms = count == 0 ? 0 : Math.Sqrt(sum / count);
            var line = $"wine-smoke ok path={path} helper={Path.GetFileName(launch.HelperPath)} frames={frames} blocks={blocks} " +
                       $"rms={rms:0.000000} finite={finite} prefix={WineBridgeSupport.PrefixPath}";
            Console.Out.WriteLine(line);
            if (!finite) return Fail("output contains a non-finite sample");
            if (rms < (instrument != 0 ? 1e-4 : 1e-6)) return Fail($"output is silent (RMS {rms:0.000000})");
            return 0;
        }
        catch (Exception ex)
        {
            return Fail(ex.GetBaseException().Message);
        }
    }

    private static TransportInfo Transport(int block, int frames) => new()
    {
        Tempo = 120, PpqPosition = block * (double)frames / (2 * Rate), Playing = true,
        Meter = new TransportMeter { Numerator = 4, Denominator = 4, BarStartPpq = 0 },
    };

    private static string Sha256(string path)
    {
        if (Directory.Exists(path))
        {
            // A VST3 bundle: hash the Windows binary the engine will actually load.
            var binary = PluginIdentity.BinaryOf(path) ?? throw new InvalidOperationException($"No loadable binary in {path}");
            path = binary;
        }
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: TabForge --wine-smoke <dll-or-vst3> <instrument 0|1> [frames] [blocks]");
        return 2;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"wine-smoke FAILED: {message}");
        return 1;
    }
}
