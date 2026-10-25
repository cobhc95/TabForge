using System.Collections.Generic;
using System.Threading;
using TabForge.AudioEngine.Mixing;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the master tap's frame numbering across a loss (LV-09): the blocks a stalled sink could not take still move the numbering on,
/// so the recording's audio keeps the length of the run. Tests: AudioEngine/Mixing/MasterTap.</summary>
public class MasterTapNumberingTests
{
    private const int Rate = 48000, Block = 256, LostBlocks = 4;
    private const int RingBlocks = (1 << 17) / Block;   // the tap's ring holds this many blocks
    private const int WaitMs = 5000;

    [Fact]
    public void BlocksLostToAStalledSinkStillAdvanceTheNumbering()
    {
        var tap = new MasterTap();
        var entered = new ManualResetEventSlim(); var release = new ManualResetEventSlim();
        var chunks = new List<(long First, int Frames)>();
        tap.Sink = (_, first, _, frames) => { entered.Set(); release.Wait(WaitMs); lock (chunks) chunks.Add((first, frames)); };
        var block = new float[Block * 2];
        tap.Enable(true, Rate);

        tap.Write(block, 0, Block);
        Assert.True(entered.Wait(WaitMs));                         // the drain thread now holds the first block
        for (var i = 1; i < RingBlocks + LostBlocks; i++) tap.Write(block, 0, Block);   // the ring fills, then blocks are lost
        release.Set();
        SpinWait.SpinUntil(() => { lock (chunks) return Sum(chunks) >= RingBlocks * (long)Block - Block; }, WaitMs);
        tap.Write(block, 0, Block);                                 // the next block that fits carries the loss
        var total = (RingBlocks + LostBlocks + 1) * (long)Block;
        Assert.True(SpinWait.SpinUntil(() => { lock (chunks) return chunks.Count > 0 && chunks[^1].First + chunks[^1].Frames == total; }, WaitMs));
        tap.Enable(false, Rate);

        Assert.Equal(LostBlocks * Block, tap.Lost);
    }

    private static long Sum(List<(long First, int Frames)> chunks)
    {
        long sum = 0;
        foreach (var c in chunks) sum += c.Frames;
        return sum;
    }
}
