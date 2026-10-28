using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Audio.Contracts.Builtin;
using TabForge.AudioEngine.Builtin;
using TabForge.AudioEngine.Builtin.IrLoader;
using TabForge.AudioEngine.Plugins;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the built-in effect framework and the IR Loader's sound: partitioned convolution against direct convolution, zero latency and its readout,
//   Auto level, resampling, separate Left / Right files, the state round trip (engine state and the song file), trust and the Add plug-in list.
// Does not own: the monitor chain in renders (MonitorFxRenderTests), the editor window (IrLoaderWindowTests).
// Tests: this class.
public sealed class BuiltinEffectTests : IDisposable
{
    private const int Rate = 48000;
    private readonly string _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tf-builtin-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() { try { Directory.Delete(_folder, true); } catch (IOException) { } }

    [Theory]
    [InlineData(100)]
    [InlineData(1500)]
    [InlineData(5000)]
    public void PartitionedConvolutionMatchesDirectConvolution(int taps)
    {
        var rng = new Random(taps);
        var ir = Enumerable.Range(0, taps).Select(i => (float)((rng.NextDouble() * 2 - 1) * Math.Exp(-i / (taps / 4.0)) * 0.05)).ToArray();
        var input = Enumerable.Range(0, 12000).Select(_ => (float)(rng.NextDouble() * 2 - 1)).ToArray();
        var convolver = new PartitionedConvolver(ir);
        var output = new float[input.Length];
        int[] blocks = [37, 128, 1, 500, 64, 333, 1024, 7];
        for (int pos = 0, b = 0; pos < input.Length; b++)
        {
            var n = Math.Min(blocks[b % blocks.Length], input.Length - pos);
            convolver.Process(input.AsSpan(pos, n), output.AsSpan(pos, n));
            pos += n;
        }
        var max = 0.0;
        for (var t = 0; t < input.Length; t++)
        {
            var sum = 0.0;
            for (var k = 0; k <= t && k < taps; k++) sum += ir[k] * (double)input[t - k];
            max = Math.Max(max, Math.Abs(sum - output[t]));
        }
        Assert.True(max < 1e-4, $"largest error {max:E2} against direct convolution ({taps} taps)");
    }

    [Fact]
    public void TheIrLoaderAddsNoLatencyAndSaysSo()
    {
        using var effect = Loaded(Wav("short.wav", [0.5f, 0.25f, 0.125f]), autoLevel: false);
        Assert.Equal(0, effect.LatencySamples);
        var (left, _) = Run(effect, Impulse(256));
        Assert.Equal(0.5f, left[0], 5);
        Assert.Equal(0.25f, left[1], 5);
        var latency = effect.GetParameters().Single(p => p.Id == IrLoaderEffect.LatencyId);
        Assert.True(latency.ReadOnly);
        Assert.Contains("0 samples", latency.Text);
    }

    [Fact]
    public void AutoLevelBringsALoudIrToZeroDbAndOffKeepsItsLevel()
    {
        var loud = Wav("loud.wav", [10f, 0, 0, 0]);
        using (var levelled = Loaded(loud, autoLevel: true))
        {
            Assert.Equal(1f, Run(levelled, Impulse(256)).Left[0], 3);
            Assert.Equal(-20, levelled.GetParameters().Single(p => p.Id == IrLoaderEffect.AutoGainId).Value, 1);
        }
        using var raw = Loaded(loud, autoLevel: false);
        Assert.Equal(10f, Run(raw, Impulse(256)).Left[0], 3);
    }

    [Fact]
    public void ResamplingKeepsTheLengthAndTheTone()
    {
        var sine = Enumerable.Range(0, 4410).Select(i => (float)Math.Sin(2 * Math.PI * 1000 * i / 44100)).ToArray();
        var resampled = IrPrep.Resample(sine, 44100, Rate);
        Assert.Equal(4800, resampled.Length);
        for (var i = 200; i < 4600; i++) Assert.True(Math.Abs(resampled[i] - Math.Sin(2 * Math.PI * 1000 * i / Rate)) < 0.01, $"sample {i}");
        var ir = Wav("rate.wav", Enumerable.Range(0, 441).Select(i => i == 0 ? 1f : 0f).ToArray(), 44100);
        using var effect = Loaded(ir, autoLevel: false);
        Assert.Equal(10, effect.GetParameters().Single(p => p.Id == IrLoaderEffect.LengthId).Value, 0);   // 441 samples at 44.1 kHz = 480 at 48 kHz = 10 ms
    }

    [Fact]
    public async Task SeparateLeftAndRightFilesEachFeedTheirSide()
    {
        var left = Wav("left.wav", [1f, 0, 0, 0]);
        var right = Wav("right.wav", Enumerable.Range(0, 20).Select(i => i == 10 ? 0.5f : 0f).ToArray());
        using var effect = new IrLoaderEffect(Rate, 256);
        effect.SetParameter(IrLoaderEffect.AutoLevelId, 0);
        effect.SetParameter(IrLoaderEffect.SeparateId, 1);
        effect.SetState(BuiltinState.OneText(IrLoaderEffect.LeftKey, left));
        effect.SetState(BuiltinState.OneText(IrLoaderEffect.RightKey, right));
        await effect.Ready.WaitAsync(TimeSpan.FromSeconds(10));
        var (outL, outR) = Run(effect, Impulse(256));
        Assert.Equal(1f, outL[0], 4);
        Assert.Equal(0f, outL[10], 4);
        Assert.Equal(0f, outR[0], 4);
        Assert.Equal(0.5f, outR[10], 4);
    }

    [Fact]
    public async Task AMissingFilePassesTheSoundAndSaysWhichFile()
    {
        using var effect = new IrLoaderEffect(Rate, 256);
        string? reported = null;
        effect.Problem += (file, _) => reported = file;
        var missing = Path.Combine(_folder, "gone.wav");
        effect.SetState(BuiltinState.OneText(IrLoaderEffect.FileKey, missing));
        await effect.Ready.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1f, Run(effect, Impulse(256)).Left[0], 5);
        Assert.Equal(missing, reported);
        var status = effect.GetParameters().Single(p => p.Id == IrLoaderEffect.StatusId);
        Assert.Equal(IrLoaderEffect.StatusProblem, status.Value);
        Assert.Contains(missing, status.Text);
    }

    [Fact]
    public void AnIrLoaderSlotSurvivesTheEngineStateAndTheSongFile()
    {
        var file = Wav("cab.wav", [1f, 0.5f]);
        using var first = new IrLoaderEffect(Rate, 256);
        first.SetParameter(IrLoaderEffect.MixId, 40);
        first.SetParameter(IrLoaderEffect.GainId, -3);
        first.SetParameter(IrLoaderEffect.TrimId, 1);
        first.SetState(BuiltinState.OneText(IrLoaderEffect.FileKey, file));
        var state = first.GetState()!;
        using var second = (IrLoaderEffect)BuiltinEffectFactory.Create(BuiltinEffects.IrLoaderId, Rate, 256);
        second.SetState(state);
        Assert.Equal(40, Param(second, IrLoaderEffect.MixId));
        Assert.Equal(-3, Param(second, IrLoaderEffect.GainId));
        Assert.Equal(1, Param(second, IrLoaderEffect.TrimId));
        Assert.Equal(file, second.Text(IrLoaderEffect.FileKey));

        var project = new SongProject { Title = "ir" };
        project.Tracks.Add(new TrackModel { Name = "Guitar", Measures = TemplateFactory.Measures(1) });
        var slot = new PluginSlot { Name = "IR Loader", Path = BuiltinEffects.IrLoaderId, Format = PluginFormats.Builtin, Vendor = BuiltinEffects.Vendor, State = Convert.ToBase64String(state) };
        project.Tracks[0].Rig.Plugins.Add(slot);
        var song = Path.Combine(_folder, "ir.tforge");
        ProjectService.Save(song, project);
        var reopened = ProjectService.Load(song).Tracks[0].Rig.Plugins.Single();
        Assert.Equal((BuiltinEffects.IrLoaderId, PluginFormats.Builtin, slot.Id, slot.State), (reopened.Path, reopened.Format, reopened.Id, reopened.State));
        var spec = new PluginTrust().BuildSpecs([reopened], Array.Empty<string>(), new PluginSettings(), new NoFileTrust()).Single();
        Assert.Equal(BuiltinEffects.IrLoaderId, spec.Path);
        Assert.False(spec.Skip);
        Assert.True(spec.LocationTrusted);
    }

    [Fact]
    public void TheAddPluginListShowsBuiltinsFirstAndFindsThem()
    {
        var scanned = new[] { new VstPluginInfo("Aardvark Amp", @"C:\Plugins\a.vst3", "VST3", "Aard", "Effect") };
        foreach (var ascending in new[] { true, false })
            foreach (var column in Enumerable.Range(0, 4))
            {
                var rows = PluginBrowserListPresenter.Rows(scanned, [], new System.Collections.Generic.HashSet<string>(), "", column, ascending);
                var ir = rows.Single(r => r.Info.Path == BuiltinEffects.IrLoaderId);
                Assert.Equal(("IR Loader", "TabForge", "Built-in", "Effect"), (ir.Name, ir.Vendor, ir.Format, ir.Role));
                Assert.All(rows.Take(BuiltinEffects.All.Count), r => Assert.Equal("Built-in", r.Format));   // every built-in comes before the user's plug-ins, whatever the sort
            }
        Assert.Single(PluginBrowserListPresenter.Rows(scanned, [], new System.Collections.Generic.HashSet<string>(), "ir load", 0, true));
        Assert.Contains(PluginBrowserListPresenter.Rows([], [], new System.Collections.Generic.HashSet<string>(), "tabforge", 0, true), r => r.Info.Path == BuiltinEffects.IrLoaderId);
        Assert.Equal(PluginFormats.Builtin, PluginFormats.Of(BuiltinEffects.IrLoaderId));
        Assert.True(new PluginTrust().IsTrusted(BuiltinEffects.IrLoaderId, new PluginSettings(), new NoFileTrust()));
    }

    private static double Param(IPluginParameters effect, uint id) => effect.GetParameters().Single(p => p.Id == id).Value;

    private IrLoaderEffect Loaded(string file, bool autoLevel)
    {
        var effect = new IrLoaderEffect(Rate, 256);
        effect.SetParameter(IrLoaderEffect.AutoLevelId, autoLevel ? 1 : 0);
        effect.SetState(BuiltinState.OneText(IrLoaderEffect.FileKey, file));
        Assert.True(effect.Ready.Wait(10_000));
        return effect;
    }

    private static float[] Impulse(int frames) { var x = new float[frames]; x[0] = 1; return x; }

    private static (float[] Left, float[] Right) Run(IPluginInstance effect, float[] mono)
    {
        var input = new[] { (float[])mono.Clone(), (float[])mono.Clone() };
        var output = new[] { new float[mono.Length], new float[mono.Length] };
        var transport = new TransportInfo();
        effect.Process(input, output, mono.Length, default, transport);
        return (output[0], output[1]);
    }

    private string Wav(string name, float[] samples, int rate = Rate)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, IrWaveFile.Write([samples], rate));
        return path;
    }

    private sealed class NoFileTrust : TabForge.Platform.IPluginFileTrust
    {
        public string? FinalPath(string fullPath) => null;
        public bool HasValidEmbeddedSignature(string fullPath) => false;
    }
}
