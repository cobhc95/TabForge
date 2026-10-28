using System;
using System.Linq;
using TabForge.Audio.Contracts.Builtin;
using TabForge.AudioEngine.Builtin;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the clean bypass of the built-in effects (no click, ends exactly on the input), the latency report after a sample rate change, and the compressor
//   factory presets (Bass, Vocal, Guitar) whose keys must all exist.
// Does not own: the sound of each effect (the Builtin*Tests classes), the list and state (BuiltinEffectSetTests).
// Tests: this class.
public sealed class BuiltinEffectBypassAndPresetTests
{
    private const int Rate = 48000;

    private static BuiltinEffect Make(string id) => BuiltinEffectFactory.Create(id, Rate, 512);

    [Theory]
    [InlineData("eq")]
    [InlineData("compressor")]
    [InlineData("gate")]
    [InlineData("delay")]
    public void BypassEndsExactlyOnTheInputWithoutAClick(string name)
    {
        using var effect = Make(BuiltinEffects.Prefix + name);
        foreach (var p in effect.Parameters.Where(p => !p.ReadOnly && p.Key is "threshold" or "mix" or "b2gain")) effect.SetParameter(p.Id, p.Key == "mix" ? 100 : p.Key == "b2gain" ? 12 : -60);
        effect.SetParameter(effect.Parameters.Single(p => p.Key == "bypass").Id, 1);
        var input = BuiltinTestKit.Sine(440, -10, Rate, Rate);
        var (left, _) = BuiltinTestKit.Run(effect, input);
        for (var i = 9600; i < input.Length; i++) Assert.Equal(input[i], left[i], 5);
        var biggest = 0.0;
        for (var i = 1; i < left.Length; i++) biggest = Math.Max(biggest, Math.Abs(left[i] - left[i - 1]));
        Assert.True(biggest < 0.15, name);
    }

    [Fact]
    public void OnlyTheLimiterReportsLatencyAndAReconfigureKeepsEveryEffectWorking()
    {
        foreach (var info in BuiltinEffects.All.Where(e => e.Id != BuiltinEffects.IrLoaderId))
        {
            using var effect = Make(info.Id);
            Assert.Equal(info.Id == BuiltinEffects.LimiterId ? 72 : 0, effect.LatencySamples);
            effect.Reconfigure(96000, 1024);
            var (left, _) = BuiltinTestKit.Run(effect, BuiltinTestKit.Sine(440, -12, 9600, 96000));
            Assert.All(left, x => Assert.True(float.IsFinite(x)));
            Assert.Equal(info.Id == BuiltinEffects.LimiterId ? 144 : 0, effect.LatencySamples);
        }
    }

    [Fact]
    public void TheCompressorHasBassVocalAndGuitarFactoryPresetsWhoseKeysAllExist()
    {
        Assert.Equal(new[] { "Bass", "Vocal", "Guitar" }, BuiltinPresets.Names(BuiltinEffects.CompressorId));
        Assert.Empty(BuiltinPresets.Names(BuiltinEffects.EqId));
        Assert.Null(BuiltinPresets.State(BuiltinEffects.CompressorId, "Missing"));
        using var compressor = Make(BuiltinEffects.CompressorId);
        var keys = compressor.Parameters.Select(p => p.Key).ToHashSet();
        foreach (var name in BuiltinPresets.Names(BuiltinEffects.CompressorId))
        {
            Assert.All(BuiltinPresets.Keys(BuiltinEffects.CompressorId, name), k => Assert.Contains(k, keys));
            compressor.SetState(BuiltinPresets.State(BuiltinEffects.CompressorId, name)!);
        }
        Assert.Equal(-24, compressor.GetParameters().Single(p => p.Id == compressor.Parameters.Single(q => q.Key == "threshold").Id).Value);
    }
}
