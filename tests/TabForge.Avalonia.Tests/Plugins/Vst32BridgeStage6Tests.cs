using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Mixing;
using TabForge.AudioEngine.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the stage-6 double-buffer safety and bypass rules at the block/link level and with the real helper.
// Does not own: the crash/hang scenarios (Vst32BridgeCrashTests) or the real-fixture soak/parity (Vst32RealStage6Tests).
[Collection("Vst32Native")]
public sealed class Vst32BridgeStage6Tests
{
    private const int Frames = 256;

    [Fact]
    public void ATimedOutBlockKeepsItsSlotAndStaleDoneIsIgnored()
    {
        var id = Guid.NewGuid().ToString("N");
        using var block = PluginHostBlock.Create(id, Frames, portable: true, bufferSlots: 2);
        var link = new PluginHostLink(block, 48000);
        var input = new[] { Enumerable.Repeat(0.25f, Frames).ToArray(), Enumerable.Repeat(-0.5f, Frames).ToArray() };
        var output = new[] { new float[Frames], new float[Frames] };

        // Generation 1 owns slot 1; nobody answers it within the budget.
        link.Process(input, output, Frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(0, Frames));
        Assert.Equal(input[0], output[0]);
        Assert.Equal(1, block.RequestGeneration);

        // The child still owns slot 1: the late output marker must survive the next (bypassed) block.
        block.ChannelSlot(1, 2)[0] = 7f;
        link.Process(input, output, Frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(1, Frames));
        Assert.Equal(7f, block.ChannelSlot(1, 2)[0]);
        Assert.Equal(0f, block.ChannelSlot(0, 2)[0]);
        Assert.True(link.MissedBlocks >= 2);
        Assert.False(link.Failed);

        // The late answer releases the slot; generation 2 is written to slot 0, never over the in-flight slot 1.
        block.Complete(1);
        var second = new[] { Enumerable.Repeat(0.75f, Frames).ToArray(), Enumerable.Repeat(0.75f, Frames).ToArray() };
        link.Process(second, output, Frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(2, Frames));
        Assert.Equal(2, block.RequestGeneration);
        Assert.Equal(0.75f, block.ChannelSlot(2, 0)[0]);
        Assert.Equal(7f, block.ChannelSlot(1, 2)[0]);   // the timed-out slot was not overwritten

        // A stale done for generation 1 is ignored while waiting for generation 2.
        block.DoneGeneration = 1;
        Assert.False(block.WaitDone(2, 30));
        Assert.False(link.Failed);
    }

    [Fact]
    public void ABudgetSkippedBlockSendsNothingAndIsNotAMiss()
    {
        var id = Guid.NewGuid().ToString("N");
        using var block = PluginHostBlock.Create(id, Frames, portable: true, bufferSlots: 2);
        var link = new PluginHostLink(block, 48000);
        var input = new[] { Enumerable.Repeat(0.2f, Frames).ToArray(), Enumerable.Repeat(0.2f, Frames).ToArray() };
        var output = new[] { new float[Frames], new float[Frames] };
        try
        {
            // A callback whose deadline is already spent: the rest of the chain is bypassed without a request.
            PluginHostLink.BeginCallback(Stopwatch.GetTimestamp() - Stopwatch.Frequency / 10, Frames, 48000);
            link.Process(input, output, Frames, ReadOnlySpan<BlockMidi>.Empty, default);
            Assert.Equal(1, link.BudgetSkippedBlocks);
            Assert.Equal(0, link.MissedBlocks);
            Assert.Equal(0, block.RequestGeneration);
            Assert.Equal(input[0], output[0]);
        }
        finally { PluginHostLink.EndCallback(); }
    }

    [Vst32RealFact]
    public void BypassKeepsTheHelperLoadedAndIdle()
    {
        var path = Vst32TestAssets.FixturePath("PurestGain32");
        Assert.True(System.IO.File.Exists(path), $"missing fixture: {path}");
        var plugin = Vst32TestAssets.Start(path, instrument: false, Frames, bufferSlots: 2);
        var pid = plugin.HostProcessId;
        Assert.True(plugin.SetParameter(0, 1.0));
        using var shared = SharedBlock.Create($"tf-vst32-bypass-{Guid.NewGuid():N}");
        using var chain = new TrackChain(0, -1, null, new[] { new TrackChain.Effect(plugin, 1f, 0) }, Frames);
        using var reference = new TrackChain(0, -1, null, Array.Empty<TrackChain.Effect>(), Frames);
        var inputL = Enumerable.Repeat(0.2f, Frames).ToArray();
        var inputR = Enumerable.Repeat(-0.2f, Frames).ToArray();
        var mixL = new float[Frames];
        var mixR = new float[Frames];
        var expectedL = new float[Frames];
        var expectedR = new float[Frames];
        try
        {
            Render(chain, shared, inputL, inputR, mixL, mixR);
            Render(chain, shared, inputL, inputR, mixL, mixR);
            var processed = Vst32TestAssets.Rms(mixL);
            Render(reference, shared, inputL, inputR, expectedL, expectedR);

            chain.SetPluginBypass(0, true);
            Array.Clear(mixL); Array.Clear(mixR);
            Render(chain, shared, inputL, inputR, mixL, mixR);
            var dry = Vst32TestAssets.Rms(mixL);
            Assert.Equal(expectedL, mixL);
            Assert.Equal(expectedR, mixR);
            Assert.True(processed > dry * 1.2f, $"the gain must alter the processed block (processed {processed:0.000}, dry {dry:0.000})");

            // Loaded and idle: no command was sent for bypass and the helper still answers.
            Assert.False(plugin.LinkFailed);
            Assert.False(Vst32TestAssets.WaitsForExit(pid, 10));
            Assert.NotEmpty(plugin.GetParameters());
            Assert.Equal(LoadedPluginInfo.Vst2, plugin.Describe().Format);

            chain.SetPluginBypass(0, false);
            Array.Clear(mixL); Array.Clear(mixR);
            Render(chain, shared, inputL, inputR, mixL, mixR);
            Assert.True(Vst32TestAssets.Rms(mixL) > dry * 1.2f, "un-bypass must process again");
        }
        finally
        {
            chain.Dispose();
        }
        Assert.True(Vst32TestAssets.WaitsForExit(pid, 4000), "the helper must exit with the chain");
    }

    [Fact]
    public void BothGenerationPathsSkipZero()
    {
        var input = new[] { Enumerable.Repeat(0.25f, Frames).ToArray(), Enumerable.Repeat(-0.25f, Frames).ToArray() };
        var output = new[] { new float[Frames], new float[Frames] };

        // Audio path: the generation after -1 would be 0 ("none"), so it skips to 1.
        using (var block = PluginHostBlock.Create(Guid.NewGuid().ToString("N"), Frames, portable: true, bufferSlots: 2))
        {
            var link = new PluginHostLink(block, 48000) { GenerationForTest = -1 };
            link.Process(input, output, Frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(0, Frames));
            Assert.Equal(1, block.RequestGeneration);
            Assert.NotEqual(0, link.GenerationForTest);
        }

        // Main-thread (offline switch) path: same skip, exercised through SetOfflineMode and a responder.
        using (var block = PluginHostBlock.Create(Guid.NewGuid().ToString("N"), Frames, portable: true, bufferSlots: 2))
        {
            var link = new PluginHostLink(block, 48000) { GenerationForTest = -1 };
            var responder = new Thread(() => { if (block.TakeRequest(3000, out var generation)) block.Complete(generation); }) { IsBackground = true };
            responder.Start();
            link.SetOfflineMode(true);
            Assert.True(responder.Join(3000), "the offline request must be answered");
            Assert.Equal(1, block.RequestGeneration);
        }

        // Near the integer top the increment wraps to int.MinValue; 0 is never handed out either way.
        using (var block = PluginHostBlock.Create(Guid.NewGuid().ToString("N"), Frames, portable: true, bufferSlots: 2))
        {
            var link = new PluginHostLink(block, 48000) { GenerationForTest = int.MaxValue };
            link.Process(input, output, Frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(0, Frames));
            Assert.Equal(int.MinValue, block.RequestGeneration);
            Assert.NotEqual(0, block.RequestGeneration);
        }
    }

    [Fact]
    public void AFailedAudioBlockAllocatesNothing()
    {
        using var block = PluginHostBlock.Create(Guid.NewGuid().ToString("N"), Frames, portable: true, bufferSlots: 2);
        var link = new PluginHostLink(block, 48000, graceMs: 0);
        var input = new[] { Enumerable.Repeat(0.25f, Frames).ToArray(), Enumerable.Repeat(-0.25f, Frames).ToArray() };
        var output = new[] { new float[Frames], new float[Frames] };

        link.Process(input, output, Frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(0, Frames));   // no answer: in flight
        Assert.False(link.Failed);
        var before = GC.GetAllocatedBytesForCurrentThread();
        link.Process(input, output, Frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(1, Frames));   // grace 0: Fail on the audio path
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(link.Failed);
        Assert.Equal(PluginHostLink.FailureReason.GraceExpired, link.Reason);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void AProcessAfterTeardownIsDryWithoutThrowing()
    {
        using var block = PluginHostBlock.Create(Guid.NewGuid().ToString("N"), Frames, portable: true, bufferSlots: 2);
        var link = new PluginHostLink(block, 48000);
        var input = new[] { Enumerable.Repeat(0.25f, Frames).ToArray(), Enumerable.Repeat(-0.25f, Frames).ToArray() };
        var output = new[] { new float[Frames], new float[Frames] };
        block.Dispose();

        link.Process(input, output, Frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(0, Frames));
        Assert.Equal(input[0], output[0]);
        Assert.Equal(input[1], output[1]);

        link.IsInstrument = true;
        link.Process(input, output, Frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(1, Frames));
        Assert.All(output[0], sample => Assert.Equal(0f, sample));
        Assert.All(output[1], sample => Assert.Equal(0f, sample));
    }

    [Vst32RealFact]
    public void ReconfigureCarriesTheStateToTheNewHelper()
    {
        var path = Vst32TestAssets.FixturePath("PurestGain32");
        Assert.True(System.IO.File.Exists(path), $"missing fixture: {path}");
        using var plugin = Vst32TestAssets.Start(path, instrument: false, Frames, bufferSlots: 2);
        Assert.True(plugin.SetParameter(0, 0.75));
        var oldPid = plugin.HostProcessId;
        plugin.Reconfigure(44100, 128);
        Assert.NotEqual(oldPid, plugin.HostProcessId);
        Assert.True(Vst32TestAssets.WaitsForExit(oldPid, 4000), "the old helper must exit on reconfigure");
        var parameters = plugin.GetParameters();
        Assert.NotEmpty(parameters);
        Assert.InRange(parameters[0].Value, 0.74, 0.76);
        var input = Enumerable.Repeat(0.2f, 64).ToArray();
        var output = new[] { new float[64], new float[64] };
        plugin.Process(new[] { input, input }, output, 64, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(0, 64));
        Assert.True(Vst32TestAssets.AllFinite(output[0], output[1]));
    }

    private static void Render(TrackChain chain, SharedBlock shared, float[] left, float[] right, float[] mixL, float[] mixR) =>
        chain.Render(mixL, mixR, 0, Frames, Vst32TestAssets.Transport(0, Frames), shared, monitor: null, peers: null, busL: left, busR: right);
}
