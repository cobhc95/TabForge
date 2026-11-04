using System;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: audio, MIDI, parameters and orderly Quit through the native helper with the deterministic 32-bit test plug-in.
// Does not own: the shared layout/fake handshake (Vst32BridgeBlockTests) or real fixtures (Vst32RealPluginTests).
[Collection("Vst32Native")]
public sealed class Vst32BridgeAudioTests
{
    private const int Frames = 256;

    [Vst32Fact]
    public void ProcessesATestSignalThroughTheTestPlugin()
    {
        using var plugin = Vst32TestAssets.Start(Vst32TestAssets.TestPluginPath!, instrument: false, Frames);
        Assert.False(plugin.IsInstrument);
        Assert.False(plugin.HasEditor);
        Assert.Equal(0, plugin.LatencySamples);

        var input = new[] { new float[Frames], new float[Frames] };
        var output = new[] { new float[Frames], new float[Frames] };
        double inputSum = 0, outputSum = 0;
        var phase = 0.0;
        for (var block = 0; block < 8; block++)
        {
            for (var i = 0; i < Frames; i++)
            {
                var sample = (float)(0.25 * Math.Sin(phase));
                phase += 2 * Math.PI * 1000 / 48000;
                input[0][i] = sample;
                input[1][i] = sample;
            }
            plugin.Process(input, output, Frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(block, Frames));
            Assert.True(Vst32TestAssets.AllFinite(output[0], output[1]), "output must be finite");
            if (block == 0) continue;   // the first block may pass through dry while the helper warms up
            foreach (var sample in input[0]) inputSum += sample * sample;
            foreach (var sample in output[0]) outputSum += sample * sample;
        }
        Assert.True(Vst32TestAssets.Rms(output[0]) > 0.01f, "output must be non-silent");
        var ratio = Math.Sqrt(outputSum / Math.Max(1e-12, inputSum));
        Assert.InRange(ratio, 0.95, 1.05);   // default parameter is unity gain
    }

    [Vst32Fact]
    public void AMidiNoteOnMakesTheTestPluginProduceSound()
    {
        using var plugin = Vst32TestAssets.Start(Vst32TestAssets.TestPluginPath!, instrument: false, Frames);
        var input = new[] { new float[Frames], new float[Frames] };
        var output = new[] { new float[Frames], new float[Frames] };
        var note = new BlockMidi { Frame = 10, Status = 0x90, Data1 = 60, Data2 = 100 };
        var peak = 0.0f;
        for (var block = 0; block < 8; block++)
        {
            var midi = block == 0 ? new[] { note } : Array.Empty<BlockMidi>();
            plugin.Process(input, output, Frames, midi, Vst32TestAssets.Transport(block, Frames));
            Assert.True(Vst32TestAssets.AllFinite(output[0], output[1]), "output must be finite");
            peak = Math.Max(peak, Vst32TestAssets.Peak(output[0]));
        }
        Assert.True(peak > 0.01f, "the note-on must reach effProcessEvents and produce the test tone");
    }

    [Vst32Fact]
    public void ParameterChangesAreAppliedThroughTheHelper()
    {
        using var plugin = Vst32TestAssets.Start(Vst32TestAssets.TestPluginPath!, instrument: false, maxBlock: 1024);
        var parameters = plugin.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(0u, parameters[0].Id);
        Assert.Contains("gain", parameters[0].Name, StringComparison.OrdinalIgnoreCase);

        Assert.True(plugin.SetParameter(0, 0.25), "set quiet parameter");
        var quiet = ProcessTone(plugin, 1024, blocks: 4);
        Assert.True(plugin.SetParameter(0, 0.75), "set loud parameter");
        var loud = ProcessTone(plugin, 1024, blocks: 4);
        Assert.True(loud > quiet * 2.5f, $"gain 0.5 must be quieter than gain 1.5 (quiet {quiet:0.0000}, loud {loud:0.0000})");
    }

    [Vst32Fact]
    public void OfflineEmptyBlocksSwitchThePluginAndAudioStillFlows()
    {
        using var plugin = Vst32TestAssets.Start(Vst32TestAssets.TestPluginPath!, instrument: false, Frames);
        plugin.SetOfflineMode(true);
        plugin.SetOfflineMode(false);
        var output = ProcessTone(plugin, Frames, blocks: 2);
        Assert.True(output > 0.001f, "audio must still flow after the offline mode switch");
    }

    [Vst32Fact]
    public void DisposeSendsQuitAndTheHelperExitsCleanly()
    {
        var plugin = Vst32TestAssets.Start(Vst32TestAssets.TestPluginPath!, instrument: false, Frames);
        var pid = plugin.HostProcessId;
        plugin.Dispose();
        Assert.True(Vst32TestAssets.WaitsForExit(pid, 4000), "the helper must exit after CmdQuit");
    }

    private static float ProcessTone(RemotePlugin plugin, int frames, int blocks)
    {
        var input = new[] { new float[frames], new float[frames] };
        var output = new[] { new float[frames], new float[frames] };
        var phase = 0.0;
        double sum = 0;
        var count = 0;
        for (var block = 0; block < blocks; block++)
        {
            for (var i = 0; i < frames; i++)
            {
                var sample = (float)(0.25 * Math.Sin(phase));
                phase += 2 * Math.PI * 440 / 48000;
                input[0][i] = sample;
                input[1][i] = sample;
            }
            plugin.Process(input, output, frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(block, frames));
            foreach (var sample in output[0]) { sum += sample * (double)sample; count++; }
        }
        return (float)Math.Sqrt(sum / Math.Max(1, count));
    }
}
