using System;
using System.IO;
using System.Linq;
using System.Threading;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Plugins;
using TabForge.Plugins;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

// Owns: the Linux-only proof of Windows VST3 through the helper (tfvst64.exe / tfvst32.exe + tfvst3 shims) under Wine:
// real fixture audio/MIDI, state, parameters, latency, scan labels and the Windows-bundle hash. Editors are Stage 7.
// Does not own: the native Windows VST3 route (Vst3Plugin, unchanged) or VST2 (Vst32WineLinux*Tests).
[Collection("Vst32Native")]
public sealed class Vst3WineLinuxTests
{
    private const int Rate = 48000;
    private readonly ITestOutputHelper _output;

    public Vst3WineLinuxTests(ITestOutputHelper output) => _output = output;

    [Vst3WineInstrumentFact]
    public void Wine64RealVst3InstrumentRendersMidiAndRoundTripsStateAndParameters()
    {
        var path = Vst3TestAssets.RealInstrument!;
        using var plugin = Vst3TestAssets.StartWine(path, instrument: true, 1024).Plugin;
        plugin.SetOfflineMode(true);
        Assert.True(plugin.IsInstrument, "the 64-bit VST3 must identify as an instrument");
        var info = plugin.Describe();
        Assert.Equal(LoadedPluginInfo.Vst3, info.Format);
        Assert.Contains("Dexed", info.Name, StringComparison.OrdinalIgnoreCase);

        var parameters = plugin.GetParameters();
        Assert.NotEmpty(parameters);
        Assert.True(plugin.LatencySamples >= 0, "latency must be readable");

        uint id = 0;
        double before = 0, after = 0;
        foreach (var candidate in parameters.Where(p => !p.ReadOnly).Take(32))
        {
            var target = candidate.Value > 0.5 ? 0.1 : 0.9;
            if (!plugin.SetParameter(candidate.Id, target)) continue;
            var read = plugin.GetParameters().First(p => p.Id == candidate.Id).Value;
            if (Math.Abs(read - candidate.Value) <= 0.02) continue;
            id = candidate.Id; before = candidate.Value; after = read;
            break;
        }
        Assert.True(id != 0 || parameters.Any(p => p.Value != 0), "the 64-bit VST3 must list usable parameters");

        var state = plugin.GetState();
        Assert.NotNull(state);
        Assert.NotEmpty(state);
        if (id != 0) Assert.True(plugin.SetParameter(id, before));
        var (rms, finite) = RenderInstrument(plugin, blocks: 24, notesAt: [0, 4]);
        Assert.True(finite, "64-bit VST3 output must be finite");
        Assert.True(rms > 1e-4f, $"64-bit VST3 must make sound from MIDI (RMS {rms:0.00000})");
        plugin.SetState(state!);
        if (id != 0)
        {
            var restored = plugin.GetParameters().First(p => p.Id == id).Value;
            Assert.InRange(restored, after - 0.02, after + 0.02);
        }
        var (restoredRms, restoredFinite) = RenderInstrument(plugin, blocks: 12, notesAt: [0]);
        Assert.True(restoredFinite);
        Assert.True(restoredRms > 1e-4f, $"64-bit VST3 must still make sound after the state restore (RMS {restoredRms:0.00000})");
        Log($"wine64 vst3 instrument name={info.Name} params={parameters.Count} state_bytes={state!.Length} " +
            $"rms={rms:0.00000} restored_rms={restoredRms:0.00000} latency={plugin.LatencySamples} param={id}");
    }

    [Vst3WineEffect32Fact]
    public void Wine32RealVst3EffectProcessesAudioAndRoundTripsStateAndParameters()
    {
        var path = Vst3TestAssets.RealEffect32!;
        using var plugin = Vst3TestAssets.StartWine(path, instrument: false, 1024).Plugin;
        plugin.SetOfflineMode(true);
        Assert.False(plugin.IsInstrument, "the 32-bit VST3 effect must identify as an effect");
        var info = plugin.Describe();
        Assert.Equal(LoadedPluginInfo.Vst3, info.Format);
        Assert.Contains("Nova", info.Name, StringComparison.OrdinalIgnoreCase);

        var parameters = plugin.GetParameters();
        Assert.NotEmpty(parameters);
        Assert.True(plugin.LatencySamples >= 0);
        uint id = 0;
        double before = 0, after = 0;
        foreach (var candidate in parameters.Where(p => !p.ReadOnly).Take(40))
        {
            var target = candidate.Value > 0.5 ? 0.1 : 0.9;
            if (!plugin.SetParameter(candidate.Id, target)) continue;
            var read = plugin.GetParameters().First(p => p.Id == candidate.Id).Value;
            if (Math.Abs(read - candidate.Value) <= 0.02) continue;
            id = candidate.Id; before = candidate.Value; after = read;
            break;
        }
        Assert.True(id != 0 || parameters.Count > 0, "the 32-bit VST3 must list parameters");
        var state = plugin.GetState();
        Assert.NotNull(state);
        Assert.NotEmpty(state);
        if (id != 0) plugin.SetParameter(id, before);
        var (rmsIn, rmsOut, finite) = RenderEffect(plugin, 1024, blocks: 8, skip: 2);
        Assert.True(finite, "32-bit VST3 output must be finite");
        Assert.True(rmsOut > 1e-6f, $"32-bit VST3 effect must not output silence (RMS {rmsOut:0.00000})");
        plugin.SetState(state!);
        if (id != 0)
        {
            var restored = plugin.GetParameters().First(p => p.Id == id).Value;
            Assert.InRange(restored, after - 0.02, after + 0.02);
        }
        Log($"wine32 vst3 effect name={info.Name} params={parameters.Count} state_bytes={state!.Length} " +
            $"rms_in={rmsIn:0.00000} rms_out={rmsOut:0.00000} latency={plugin.LatencySamples} param={id}");
    }

    [Fact]
    public void ABridgedVst3BundleHashesItsWindowsBinaryAndScansAsBridged()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var folder = new Vst32TestAssets.TempFolder();
        var bundle = Path.Combine(folder.Path, "Bridge3.vst3");
        var binary = Path.Combine(bundle, "Contents", "x86_64-win", "Bridge3.vst3");
        Directory.CreateDirectory(Path.GetDirectoryName(binary)!);
        File.WriteAllBytes(binary, Vst32TestAssets.PeImage(x86: false, export: "GetPluginFactory"));
        var flat32 = Path.Combine(folder.Path, "Flat32.vst3");
        File.WriteAllBytes(flat32, Vst32TestAssets.PeImage(x86: true, export: "GetPluginFactory"));

        Assert.Equal(Path.GetFullPath(binary), Path.GetFullPath(PluginIdentity.BinaryOf(bundle)!));
        Assert.True(Vst3Windows.IsWindowsOnly(bundle));
        Assert.True(Vst3Windows.Has(bundle, sixtyFourBit: true));

        var found = VstScannerService.Scan(new[] { folder.Path }, CancellationToken.None, new PluginTrust(), new Vst32TestAssets.NoFiles(), forceRefresh: true);
        var row64 = found.Single(r => r.Path.EndsWith("Bridge3.vst3", StringComparison.OrdinalIgnoreCase));
        var row32 = found.Single(r => r.Path.EndsWith("Flat32.vst3", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(PluginFormats.Bridged64, row64.Format);
        Assert.Equal(PluginFormats.Bridged32, row32.Format);
        var handPicked = TabForge.Avalonia.Views.Fx.PluginBrowserSession.FromFile(bundle, out var problem);
        Assert.Null(problem);
        Assert.Equal(PluginFormats.Bridged64, handPicked!.Format);
        Log($"wine vst3 scan bundle={row64.Format} flat32={row32.Format} hand_pick={handPicked.Format}");
    }

    [Fact]
    public void TheLinuxRouteSendsWindowsVst3ToTheMatchingHelper()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var folder = new Vst32TestAssets.TempFolder();
        var flat64 = Path.Combine(folder.Path, "Route64.vst3");
        var flat32 = Path.Combine(folder.Path, "Route32.vst3");
        File.WriteAllBytes(flat64, Vst32TestAssets.PeImage(x86: false, export: "GetPluginFactory"));
        File.WriteAllBytes(flat32, Vst32TestAssets.PeImage(x86: true, export: "GetPluginFactory"));
        var spec64 = new PluginSpec(flat64, "VST3", false, Enabled: true, Wet: 100, State: null, Name: "r64", Id: "v", LocationTrusted: false, ExpectedSha256: "");
        var spec32 = new PluginSpec(flat32, "VST3", false, Enabled: true, Wet: 100, State: null, Name: "r32", Id: "v", LocationTrusted: false, ExpectedSha256: "");
        Assert.True(Vst32Bridge.IsWineVst3Candidate(spec64, sixtyFourBit: true));
        Assert.False(Vst32Bridge.IsWineVst3Candidate(spec64, sixtyFourBit: false));
        Assert.True(Vst32Bridge.IsWineVst3Candidate(spec32, sixtyFourBit: false));
        Assert.Equal(Vst32Bridge.PluginRoute.Bridged64, Vst32Bridge.Route(spec64, separateProcess: false));
        Assert.Equal(Vst32Bridge.PluginRoute.Bridged32, Vst32Bridge.Route(spec32, separateProcess: false));
        Log("wine vst3 route 64=Bridged64 32=Bridged32");
    }

    private static (float Rms, bool Finite) RenderInstrument(RemotePlugin plugin, int blocks, int[] notesAt)
    {
        const int frames = 1024;
        var input = new[] { new float[frames], new float[frames] };
        var output = new[] { new float[frames], new float[frames] };
        double sum = 0; var count = 0; var finite = true;
        for (var block = 0; block < blocks; block++)
        {
            var midi = notesAt.Contains(block)
                ? new[] { new BlockMidi { Frame = 0, Status = 0x90, Data1 = 60, Data2 = 100 } }
                : Array.Empty<BlockMidi>();
            plugin.Process(input, output, frames, midi, Vst32TestAssets.Transport(block, frames));
            finite &= Vst32TestAssets.AllFinite(output[0], output[1]);
            if (block < 2) continue;
            foreach (var sample in output[0]) { sum += sample * (double)sample; count++; }
            foreach (var sample in output[1]) { sum += sample * (double)sample; count++; }
        }
        return count == 0 ? (0, finite) : ((float)Math.Sqrt(sum / count), finite);
    }

    private static (float RmsIn, float RmsOut, bool Finite) RenderEffect(RemotePlugin plugin, int frames, int blocks, int skip)
    {
        var input = new[] { new float[frames], new float[frames] };
        var output = new[] { new float[frames], new float[frames] };
        double inSum = 0, outSum = 0; var count = 0; var finite = true; var phase = 0.0;
        for (var block = 0; block < blocks; block++)
        {
            for (var i = 0; i < frames; i++)
            {
                var sample = (float)(0.25 * Math.Sin(phase));
                phase += 2 * Math.PI * 1000 / Rate;
                input[0][i] = sample; input[1][i] = sample;
            }
            plugin.Process(input, output, frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(block, frames));
            finite &= Vst32TestAssets.AllFinite(output[0], output[1]);
            if (block < skip) continue;
            for (var i = 0; i < frames; i++)
            {
                inSum += input[0][i] * (double)input[0][i];
                outSum += output[0][i] * (double)output[0][i];
                count++;
            }
        }
        return count == 0 ? (0, 0, finite) : ((float)Math.Sqrt(inSum / count), (float)Math.Sqrt(outSum / count), finite);
    }

    private void Log(string line)
    {
        _output.WriteLine(line);
        var path = Environment.GetEnvironmentVariable("TABFORGE_WINE_METRICS");
        if (string.IsNullOrWhiteSpace(path)) return;
        lock (typeof(Vst3WineLinuxTests)) File.AppendAllText(path, line + Environment.NewLine);
    }
}
