using System;
using System.IO;
using System.Linq;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Plugins;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

// Owns: the required real 32-bit VST2 fixtures through the actual helper (kind, audio/MIDI, PurestGain level, probe hash rule, Quit).
// Does not own: the deterministic test plug-in (Vst32BridgeAudioTests) or the shared layout (Vst32BridgeBlockTests).
[Collection("Vst32Native")]
public sealed class Vst32RealPluginTests
{
    private const int Frames = 1024;
    private readonly ITestOutputHelper _output;

    public Vst32RealPluginTests(ITestOutputHelper output) => _output = output;

    [Vst32RealTheory]
    [InlineData("Acceleration32", false)]
    [InlineData("ADClip932", false)]
    [InlineData("Console4Channel32", false)]
    [InlineData("Density32", false)]
    [InlineData("PurestGain32", false)]
    [InlineData("TAL-NoiseMaker32", true)]
    [InlineData("TAL-BassLine-101-32", true)]
    [InlineData("Dexed32", true)]
    public void EveryRealFixtureLoadsAndProcesses(string name, bool instrument)
    {
        var path = Vst32TestAssets.FixturePath(name);
        Assert.True(File.Exists(path), $"missing fixture: {path}");
        using var plugin = Vst32TestAssets.Start(path, instrument, Frames);
        Assert.Equal(instrument, plugin.IsInstrument);
        _output.WriteLine($"{name}: kind={(plugin.IsInstrument ? "Instrument" : "Effect")} editor={plugin.HasEditor} latency={plugin.LatencySamples} tail={plugin.TailSamples}");

        var input = new[] { new float[Frames], new float[Frames] };
        var output = new[] { new float[Frames], new float[Frames] };
        double sum = 0;
        var count = 0;
        var phase = 0.0;
        var blocks = instrument ? 24 : 8;
        for (var block = 0; block < blocks; block++)
        {
            if (instrument)
            {
                Array.Clear(input[0]); Array.Clear(input[1]);
            }
            else
            {
                for (var i = 0; i < Frames; i++)
                {
                    var sample = (float)(0.25 * Math.Sin(phase));
                    phase += 2 * Math.PI * 1000 / 48000;
                    input[0][i] = sample;
                    input[1][i] = sample;
                }
            }
            var midi = instrument && (block == 0 || block == 4)
                ? new[] { new BlockMidi { Frame = 0, Status = 0x90, Data1 = 60, Data2 = 100 } }
                : Array.Empty<BlockMidi>();
            plugin.Process(input, output, Frames, midi, Vst32TestAssets.Transport(block, Frames));
            Assert.True(Vst32TestAssets.AllFinite(output[0], output[1]), $"{name}: output must be finite");
            if (block < 2) continue;   // the first blocks may pass through dry while the helper warms up
            foreach (var sample in output[0]) { sum += sample * (double)sample; count++; }
            foreach (var sample in output[1]) { sum += sample * (double)sample; count++; }
        }
        var rms = count == 0 ? 0 : (float)Math.Sqrt(sum / count);
        _output.WriteLine($"{name}: output RMS {Vst32TestAssets.Db(rms):0.0} dBFS, peak {Vst32TestAssets.Db(Vst32TestAssets.Peak(output[0])):0.0} dBFS");
        Assert.True(rms > 1e-5f, $"{name}: output must be non-silent (RMS {Vst32TestAssets.Db(rms):0.0} dBFS)");
    }

    [Vst32RealFact]
    public void PurestGainChangesLevelAgainstDry()
    {
        var path = Vst32TestAssets.FixturePath("PurestGain32");
        Assert.True(File.Exists(path), $"missing fixture: {path}");
        using var plugin = Vst32TestAssets.Start(path, instrument: false, Frames);
        var parameters = plugin.GetParameters();
        Assert.NotEmpty(parameters);
        var id = parameters[0].Id;
        _output.WriteLine($"PurestGain32: parameter 0 = {parameters[0].Name} ({parameters[0].Text})");

        var dry = Play(plugin, Frames, blocks: 6, skip: 2);
        Assert.True(plugin.SetParameter(id, 0.0), "set the gain parameter to 0");
        var low = Play(plugin, Frames, blocks: 8, skip: 4);
        Assert.True(plugin.SetParameter(id, 1.0), "set the gain parameter to 1");
        var high = Play(plugin, Frames, blocks: 8, skip: 4);
        _output.WriteLine($"PurestGain32: dry {Vst32TestAssets.Db(dry):0.0} dBFS, value 0 {Vst32TestAssets.Db(low):0.0} dBFS, value 1 {Vst32TestAssets.Db(high):0.0} dBFS");
        Assert.True(low < dry * 0.5f, $"the minimum gain must be clearly quieter than dry (dry {dry:0.0000}, low {low:0.0000})");
        Assert.True(high > dry * 1.05f, $"the maximum gain must be louder than dry (dry {dry:0.0000}, high {high:0.0000})");
    }

    [Vst32RealTheory]
    [InlineData("Acceleration32", false)]
    [InlineData("ADClip932", false)]
    [InlineData("Console4Channel32", false)]
    [InlineData("Density32", false)]
    [InlineData("PurestGain32", false)]
    [InlineData("TAL-NoiseMaker32", true)]
    [InlineData("TAL-BassLine-101-32", true)]
    [InlineData("Dexed32", true)]
    public void ProbeIdentifiesEveryFixture(string name, bool instrument)
    {
        var path = Vst32TestAssets.FixturePath(name);
        Assert.True(File.Exists(path), $"missing fixture: {path}");
        var (exit, output, error) = Vst32TestAssets.RunHelperCaptured("--probe", path, Vst32TestAssets.Sha256(path));
        Assert.True(exit == 0, $"{name}: probe exit {exit}: {error}");
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).FirstOrDefault();
        Assert.NotNull(line);
        var parts = line!.Split('|');
        Assert.Equal(3, parts.Length);
        Assert.Equal(instrument ? "Instrument" : "Effect", parts[0]);
        _output.WriteLine($"{name}: probe {line}");
    }

    [Vst32RealFact]
    public void ProbeRefusesAChangedHashWithExitFive()
    {
        var path = Vst32TestAssets.FixturePath("PurestGain32");
        Assert.True(File.Exists(path), $"missing fixture: {path}");
        var (exit, _, error) = Vst32TestAssets.RunHelperCaptured("--probe", path, new string('0', 64));
        Assert.Equal(5, exit);
        Assert.Contains("SHA-256", error, StringComparison.OrdinalIgnoreCase);
    }

    [Vst32RealFact]
    public void TheRealHelperExitsCleanlyOnQuit()
    {
        var path = Vst32TestAssets.FixturePath("PurestGain32");
        Assert.True(File.Exists(path), $"missing fixture: {path}");
        var plugin = Vst32TestAssets.Start(path, instrument: false, Frames);
        var pid = plugin.HostProcessId;
        plugin.Dispose();
        Assert.True(Vst32TestAssets.WaitsForExit(pid, 4000), "the real helper must exit after CmdQuit");
    }

    private static float Play(RemotePlugin plugin, int frames, int blocks, int skip)
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
                phase += 2 * Math.PI * 1000 / 48000;
                input[0][i] = sample;
                input[1][i] = sample;
            }
            plugin.Process(input, output, frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(block, frames));
            if (block < skip) continue;
            foreach (var sample in output[0]) { sum += sample * (double)sample; count++; }
            foreach (var sample in output[1]) { sum += sample * (double)sample; count++; }
        }
        return count == 0 ? 0 : (float)Math.Sqrt(sum / count);
    }
}
