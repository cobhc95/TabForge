using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Audio.Contracts.Builtin;
using TabForge.AudioEngine.Builtin;
using TabForge.AudioEngine.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: what the audio thread must never do in any effect of the built-in set (EQ, Compressor, Limiter, Delay, Noise Gate): produce a non-finite sample at extreme
//   settings, allocate, click on bypass, or lose its latency report on a rate change; and the compressor factory presets.
// Does not own: the sound of each effect (the Builtin*Tests classes), the list and state (BuiltinEffectSetTests).
// Tests: this class.
public sealed class BuiltinEffectSafetyTests
{
    private const int Rate = 48000;

    public static IEnumerable<object[]> Ids() => BuiltinEffectSetTests.Ids();

    private static BuiltinEffect Make(string id) => BuiltinEffectFactory.Create(id, Rate, 512);

    [Theory]
    [MemberData(nameof(Ids))]
    public void ExtremeSettingsAndLoudNoiseStayFinite(string id)
    {
        foreach (var high in new[] { false, true })
        {
            using var effect = Make(id);
            foreach (var p in effect.Parameters.Where(p => !p.ReadOnly && p.Key != "bypass")) effect.SetParameter(p.Id, high ? p.Max : p.Min);
            var rng = new Random(3);
            var noise = Enumerable.Range(0, Rate).Select(_ => (float)(rng.NextDouble() * 4 - 2)).ToArray();
            var (left, right) = BuiltinTestKit.Run(effect, noise);
            Assert.All(left.Concat(right), x => Assert.True(float.IsFinite(x) && Math.Abs(x) < 1e6, id));
        }
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void TheAudioThreadAllocatesNothing(string id)
    {
        using var effect = Make(id);
        var input = new[] { BuiltinTestKit.Sine(330, -10, 512, Rate), BuiltinTestKit.Sine(550, -10, 512, Rate) };
        var output = new[] { new float[512], new float[512] };
        var transport = new TransportInfo { Tempo = 100, Speed = 1 };
        var instance = (IPluginInstance)effect;
        for (var i = 0; i < 20; i++) instance.Process(input, output, 512, default, transport);
        var knob = effect.Parameters.First(p => !p.ReadOnly && p.Key != "bypass");
        effect.SetParameter(knob.Id, knob.Max);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++) instance.Process(input, output, 512, default, transport);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
