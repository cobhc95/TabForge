using System.IO;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Isolation;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the isolated plug-in's engine-side control channel waits: an unanswered command ends at its timeout
//   (never blocks past it) and a reply nobody waits for is dropped and counted.
// Does not own: the plug-in side (Answer) or the wire frames (golden tests).
// Tests: this file.
public sealed class PluginControlChannelTests
{
    [Fact]
    public void AnUnansweredCommandEndsAtItsTimeout()
    {
        using var pipe = new MemoryStream();   // written, never read: no reply ever arrives
        var channel = new PluginControlChannel(pipe, new EngineLogWriter());
        var before = System.Environment.TickCount64;
        var data = channel.Ask(1, null, expect: 2, timeoutMs: 200, out var timedOut);
        Assert.Null(data);
        Assert.True(timedOut);
        Assert.True(System.Environment.TickCount64 - before < 5000);   // bounded by the timeout, not forever
    }

    [Fact]
    public void AReplyNobodyWaitsForIsDroppedAndCounted()
    {
        using var pipe = new MemoryStream();
        var channel = new PluginControlChannel(pipe, new EngineLogWriter());
        using var reply = new MemoryStream();
        using (var w = new BinaryWriter(reply, System.Text.Encoding.UTF8, leaveOpen: true)) { w.Write(999u); w.Write(new byte[] { 1, 2, 3 }); }
        reply.Position = 0;
        Assert.False(channel.Receive(7, new BinaryReader(reply)));
        Assert.Equal(1, channel.Dropped);
    }
}
