using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: what an FX chain export says about plug-ins that could not be exported (one named line each, with its reason), that one slow plug-in does not
/// blank the ones after it (the info is asked for twice and merged), and the plain text of a VST3 helper that is missing.
/// Does not own: the engine's answers themselves.
/// Tests: this class.
/// </summary>
public class RfxExportGapTests
{
    private static LoadedPluginInfo Info(uint id) => new(LoadedPluginInfo.Vst2, id, "", 2, 2, true, "Acme", "Plug");

    private static List<PluginSlot> Chain(int count) =>
        Enumerable.Range(1, count).Select(i => new PluginSlot { Name = "P" + i, Path = $@"C:\vst\p{i}.dll", Format = "VST2" }).ToList();

    private static RfxChainExporter.Result Export(IReadOnlyList<PluginSlot> slots, IReadOnlyList<LoadedPluginInfo?> info, Func<PluginSlot, RfxExportGap>? why = null) =>
        RfxChainExporter.Export(RfxChainExporter.FromSlots(slots, info, why));

    [Fact]
    public void OnePlugInWithoutAnswerIsTheOnlyOneNamed()
    {
        var slots = Chain(4);
        var result = Export(slots, new LoadedPluginInfo?[] { Info(1), null, Info(3), Info(4) });
        Assert.Equal(3, result.Written);
        Assert.Equal(new[] { "Exported 3 of 4 plug-ins.\nP2: didn't answer in time" }, result.Notes);
    }

    [Fact]
    public void WhenEveryPlugInAnswersNothingIsNamed()
    {
        var slots = Chain(4);
        var result = Export(slots, slots.Select((_, i) => (LoadedPluginInfo?)Info((uint)i + 1)).ToList());
        Assert.Equal(4, result.Written);
        Assert.Empty(result.Notes);
    }

    [Fact]
    public void EachReasonHasItsPlainLine()
    {
        var slots = Chain(3);
        var reasons = new Dictionary<string, RfxExportGap> { ["P1"] = RfxExportGap.NotInstalled, ["P2"] = RfxExportGap.NotTrusted, ["P3"] = RfxExportGap.NoAnswer };
        var info = new LoadedPluginInfo?[] { LoadedPluginInfo.Unavailable, LoadedPluginInfo.Unavailable, null };
        var result = Export(slots, info, s => reasons[s.Name]);
        Assert.Equal("Exported 0 of 3 plug-ins.\nP1: not installed on this PC\nP2: needs your approval in Plug-in safety\nP3: didn't answer in time", result.Notes.Single());
        Assert.Equal("P4: couldn't read its settings (default settings written)", RfxExportGaps.Line("P4", RfxExportGap.StateUnreadable));
    }

    [Fact]
    public void ASamePlugInTwiceIsNamedOnce()
    {
        var slots = Chain(2);
        slots[1].Name = slots[0].Name;
        var result = Export(slots, new LoadedPluginInfo?[] { null, null });
        Assert.Equal("Exported 0 of 2 plug-ins.\nP1: didn't answer in time", result.Notes.Single());
    }

    [Fact]
    public async Task AskedTwiceTheMissingOnesAreFilledAndNoOtherIsChanged()
    {
        var calls = 0;
        var result = await AudioEngineClient.CollectInfoAsync(4, () =>
        {
            calls++;
            return Task.FromResult(calls == 1 ? new LoadedPluginInfo?[] { Info(1), null, null, null } : new LoadedPluginInfo?[] { Info(9), null, Info(3), Info(4) });
        });
        Assert.Equal(2, calls);
        Assert.Equal(new uint?[] { 1, null, 3, 4 }, result.Select(r => r?.UniqueId).ToArray());
    }

    [Fact]
    public async Task ACompleteFirstAnswerIsNotAskedAgain()
    {
        var calls = 0;
        var result = await AudioEngineClient.CollectInfoAsync(2, () => { calls++; return Task.FromResult(new LoadedPluginInfo?[] { Info(1), Info(2) }); });
        Assert.Equal(1, calls);
        Assert.All(result, Assert.NotNull);
    }

    [Fact]
    public async Task AChainThatChangedUnderTheFirstAskAnswersTheSecondTime()
    {
        var calls = 0;
        var result = await AudioEngineClient.CollectInfoAsync(3, () =>
        {
            calls++;
            return Task.FromResult(calls == 1 ? new LoadedPluginInfo?[3] : new LoadedPluginInfo?[] { Info(1), Info(2), Info(3) });
        });
        Assert.All(result, Assert.NotNull);
        Assert.Empty(Export(Chain(3), result).Notes);
    }

    [Fact]
    public void AMissingVst3HelperIsNamedInPlainWords()
    {
        var vst3 = new PluginSpec(@"C:\vst\a.vst3", "VST3", false, true, 100, null);
        var vst2 = new PluginSpec(@"C:\vst\a.dll", "VST2", false, true, 100, null);
        var raw = new DllNotFoundException("Dll was not found.");
        Assert.Equal("TabForge's VST3 helper (tfvst3.dll) is missing or can't load.", EngineSession.LoadFailureText(vst3, raw));
        Assert.Equal("Dll was not found.", EngineSession.LoadFailureText(vst2, raw));
        Assert.Equal("boom", EngineSession.LoadFailureText(vst3, new InvalidOperationException("boom")));
    }
}
