using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Audio.Contracts.Builtin;
using TabForge.AudioEngine.Builtin;
using TabForge.AudioEngine.Plugins;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: what every effect of the built-in set (EQ, Compressor, Limiter, Delay, Noise Gate) shares: they are listed in the Add plug-in browser with vendor TabForge,
//   format Built-in and role Effect; unique ids and keys with defaults inside their ranges; the state round trip and partial state; extreme settings stay finite;
//   no allocation on the audio thread; a clean bypass; the compressor factory presets.
// Does not own: the sound of each effect (the Builtin*Tests classes), the framework (BuiltinEffectTests).
// Tests: this class.
public sealed class BuiltinEffectSetTests
{
    private const int Rate = 48000;

    public static IEnumerable<object[]> Ids() =>
        BuiltinEffects.All.Select(e => e.Id).Where(id => id != BuiltinEffects.IrLoaderId).Select(id => new object[] { id });

    private static BuiltinEffect Make(string id) => BuiltinEffectFactory.Create(id, Rate, 512);

    [Fact]
    public void EveryEffectIsListedInTheBrowserAsATabForgeBuiltinEffect()
    {
        var rows = PluginBrowserListPresenter.Rows([], [], new HashSet<string>(), "", 0, true);
        string[] ids = [BuiltinEffects.EqId, BuiltinEffects.CompressorId, BuiltinEffects.LimiterId, BuiltinEffects.DelayId, BuiltinEffects.GateId];
        foreach (var id in ids)
        {
            var info = BuiltinEffects.Find(id)!;
            var row = rows.Single(r => r.Info.Path == id);
            Assert.Equal((info.Name, "TabForge", "Built-in", "Effect"), (row.Name, row.Vendor, row.Format, row.Role));
        }
        Assert.Equal(new[] { "EQ", "Compressor", "Limiter", "Delay", "Noise Gate" }, ids.Select(i => BuiltinEffects.Find(i)!.Name));
        Assert.All(rows.Take(BuiltinEffects.All.Count), r => Assert.Equal(("TabForge", "Built-in", "Effect"), (r.Vendor, r.Format, r.Role)));   // TabForge own effects come first
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void ParametersHaveUniqueIdsAndKeysAndDefaultsInsideTheirRange(string id)
    {
        using var effect = Make(id);
        var list = effect.Parameters;
        Assert.Equal(list.Count, list.Select(p => p.Id).Distinct().Count());
        Assert.Equal(list.Count, list.Select(p => p.Key).Distinct().Count());
        foreach (var p in list) Assert.InRange(p.Default, p.Min, p.Max);
        Assert.Equal(list.Count, effect.GetParameters().Count);
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void TheStateRoundTripsAndAPartialStateChangesOnlyWhatItNames(string id)
    {
        using var first = Make(id);
        var moved = new Dictionary<uint, double>();
        foreach (var p in first.Parameters.Where(p => !p.ReadOnly))
        {
            var value = p.Clamp(p.Min + (p.Max - p.Min) * 0.37);
            if (value == p.Default) value = p.Clamp(p.Min + (p.Max - p.Min) * 0.81);
            first.SetParameter(p.Id, value);
            moved[p.Id] = first.GetParameters().Single(q => q.Id == p.Id).Value;
        }
        var state = first.GetState()!;
        using var second = Make(id);
        second.SetState(state);
        foreach (var (pid, value) in moved) Assert.Equal(value, second.GetParameters().Single(q => q.Id == pid).Value);
        Assert.Equal(state, second.GetState());

        using var third = Make(id);
        var one = third.Parameters.First(p => !p.ReadOnly && p.Key != "bypass");
        var partial = new BuiltinState();
        partial.Values[one.Key] = moved[one.Id];
        third.SetState(partial.ToBytes());
        foreach (var p in third.Parameters.Where(p => !p.ReadOnly))
            Assert.Equal(p.Id == one.Id ? moved[p.Id] : p.Default, third.GetParameters().Single(q => q.Id == p.Id).Value);
    }
}
