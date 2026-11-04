using System;
using System.IO;
using System.Linq;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Plugins;
using TabForge.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Windows-side proof that the same helper source builds a working x64 PE: its selftest, the native x64 self-test,
// and the TCP/pipe audio, state, parameter and fault paths with the deterministic 64-bit test DLL.
// Does not own: the Linux Wine 64-bit route (Vst32WineLinux64Tests) or the 32-bit behaviour (the other Vst32 tests).
[Collection("Vst32Native")]
public sealed class Vst32Native64Tests
{
    private const int Frames = 256;

    [Vst64HelperFact]
    public void The64BitHelperSelfTestPasses()
    {
        var (exit, output, error) = Vst32TestAssets.RunHelper64Captured("--selftest");
        Assert.True(exit == 0, $"tfvst64 --selftest exit {exit}: {error}");
        Assert.Contains("tfvst64 selftest: ok", output);
    }

    [Vst64Fact]
    public void The64BitNativeSelfTestPasses()
    {
        var (exit, output, error) = Vst32TestAssets.RunNativeTest64Captured();
        Assert.True(exit == 0, $"tfvst64test exit {exit}: {error}");
        Assert.Contains("tfvst64test: ok", output);
    }

    [Vst64Fact]
    public void The64BitHelperRendersFiniteNonSilentAudio()
    {
        var (plugin, _) = Vst32TestAssets.Start64(Vst32TestAssets.TestPluginPath64!, instrument: false, Frames);
        try
        {
            plugin.SetOfflineMode(true);
            Assert.True(plugin.SetParameter(0, 0.25), "set the deterministic gain to 0.25 (gain 0.5)");
            var (rmsIn, rmsOut, finite) = RenderEffect(plugin, blocks: 8, skip: 1);
            Assert.True(finite, "every x64 output sample must be finite");
            Assert.True(rmsOut > 0.01f, $"the x64 deterministic plug-in must be non-silent (RMS {rmsOut:0.00000})");
            var ratio = rmsOut / Math.Max(1e-9f, rmsIn);
            Assert.InRange(ratio, 0.45f, 0.55f);   // dry bypass would be 1.0, so the x64 helper processed the block
            Assert.Equal(0L, plugin.MissedBlocks);
        }
        finally { plugin.Dispose(); }
    }

    [Vst64Fact]
    public void The64BitHelperRoundTripsStateAndParameters()
    {
        using var plugin = Vst32TestAssets.Start64(Vst32TestAssets.TestPluginPath64!, instrument: false, 1024).Plugin;
        plugin.SetOfflineMode(true);
        var parameter = Assert.Single(plugin.GetParameters());
        Assert.Contains("gain", parameter.Name, StringComparison.OrdinalIgnoreCase);
        Assert.True(plugin.SetParameter(0, 0.75));
        Assert.InRange(plugin.GetParameters().Single().Value, 0.70, 0.80);
        var state = plugin.GetState();
        Assert.NotNull(state);
        Assert.NotEmpty(state);
        Assert.True(plugin.SetParameter(0, 0.25));
        Assert.InRange(plugin.GetParameters().Single().Value, 0.20, 0.30);
        plugin.SetState(state!);
        Assert.InRange(plugin.GetParameters().Single().Value, 0.70, 0.80);   // the state captured at 0.75
        var (rmsIn, rmsOut, finite) = RenderEffect(plugin, blocks: 6, skip: 1);
        Assert.True(finite, "state-restored x64 audio must be finite");
        Assert.InRange(rmsOut / Math.Max(1e-9f, rmsIn), 1.45f, 1.55f);
    }

    [Vst64Fact]
    public void The64BitHelperSurvivesAPluginFaultAndKeepsServing()
    {
        var folder = Directory.CreateTempSubdirectory("tf-vst64-fault").FullName;
        try
        {
            var dll = Path.Combine(folder, "passthrough64.dll");
            File.Copy(Vst32TestAssets.TestPluginPath64!, dll);
            File.WriteAllBytes(Path.Combine(folder, "passthrough32.faultonce"), []);
            using var plugin = Vst32TestAssets.Start64(dll, instrument: false, Frames).Plugin;
            plugin.SetOfflineMode(true);
            var input = Enumerable.Repeat(0.25f, Frames).ToArray();
            var output = new[] { new float[Frames], new float[Frames] };
            plugin.Process(new[] { input, input }, output, Frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(0, Frames));
            Assert.All(output[0], sample => Assert.Equal(0.0f, sample));   // the faulted x64 process block is silent, not torn
            plugin.Process(new[] { input, input }, output, Frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(1, Frames));
            Assert.Equal(input, output[0]);   // and the next block flows through the still-live helper
            Assert.False(plugin.LinkFailed);
        }
        finally { try { Directory.Delete(folder, recursive: true); } catch (IOException) { } }
    }

    [WindowsFact]
    public void A64BitVst2PeStaysNativeOnWindows()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var path = folder.Write("New64.dll", Vst32TestAssets.PeImage(x86: false));
        var spec = new PluginSpec(path, PluginFormats.Vst2, IsInstrument: false, Enabled: true, Wet: 100, State: null, Name: "New64", Id: "new64", LocationTrusted: true);

        Assert.False(Vst32Bridge.IsBridged(spec));
        Assert.False(Vst32Bridge.IsWine64Candidate(spec));
        Assert.Equal(Vst32Bridge.PluginRoute.InProcess, Vst32Bridge.Route(spec, separateProcess: false));
        Assert.Equal(Vst32Bridge.PluginRoute.Isolated, Vst32Bridge.Route(spec, separateProcess: true));
    }

    private static (float RmsIn, float RmsOut, bool Finite) RenderEffect(RemotePlugin plugin, int blocks, int skip)
    {
        var input = new[] { new float[Frames], new float[Frames] };
        var output = new[] { new float[Frames], new float[Frames] };
        double inSum = 0, outSum = 0;
        var count = 0;
        var finite = true;
        var phase = 0.0;
        for (var block = 0; block < blocks; block++)
        {
            for (var i = 0; i < Frames; i++)
            {
                var sample = (float)(0.25 * Math.Sin(phase));
                phase += 2 * Math.PI * 1000 / 48000;
                input[0][i] = sample;
                input[1][i] = sample;
            }
            plugin.Process(input, output, Frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(block, Frames));
            finite &= Vst32TestAssets.AllFinite(output[0], output[1]);
            if (block < skip) continue;
            for (var i = 0; i < Frames; i++)
            {
                inSum += input[0][i] * (double)input[0][i];
                outSum += output[0][i] * (double)output[0][i];
                count++;
            }
        }
        return count == 0 ? (0, 0, finite) : ((float)Math.Sqrt(inSum / count), (float)Math.Sqrt(outSum / count), finite);
    }
}
