using System;
using System.Threading.Tasks;
using TabForge.Audio;
using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the blocking and async state-collect paths agreeing when there is nothing to collect (the engine runs but no
//   track has a slot): both return at once with an empty collection instead of waiting out the timeout.
// Does not own: the collection itself (AsioProbeTests covers the ask/answer waiting) or the save flow.
// Tests: this file.
public sealed class StateCollectSyncTests
{
    [Fact]
    public void TheBlockingCollectReturnsAtOnceWhenNoTrackHasASlot()
    {
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();   // running, but no slots: nothing to ask
        var before = Environment.TickCount64;
        var collected = client.CollectStates(Array.Empty<TrackModel>(), 800);
        Assert.Empty(collected.Results);
        Assert.True(Environment.TickCount64 - before < 800);   // returned at once: it never waited out the timeout
    }

    [Fact]
    public async Task TheAsyncCollectAgreesWithTheBlockingOneWhenIdle()
    {
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        var collected = await client.CollectStatesAsync(Array.Empty<TrackModel>(), 800);
        Assert.Empty(collected.Results);
    }
}
