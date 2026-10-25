using System;
using System.IO;
using TabForge.Audio.Contracts;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the engine frame writer: each frame leaves in one stream write from the connection's reused buffer, and reads back intact.
/// Does not own: the read limits (FrameLimits) or the engine commands.
/// Tests: this class.
/// </summary>
public class EngineFrameWriteTests
{
    private const int FramesWritten = 50;
    private const byte FrameType = 7;

    private sealed class CountingStream : MemoryStream
    {
        public int Writes { get; private set; }
        public override void Write(byte[] buffer, int offset, int count) { Writes++; base.Write(buffer, offset, count); }
    }

    [Fact]
    public void EachFrameIsOneWriteAndReadsBackIntact()
    {
        var stream = new CountingStream();
        for (var i = 0; i < FramesWritten; i++) Frames.Write(stream, FrameType, w => { w.Write(i); w.Write("frame " + i); });
        Assert.Equal(FramesWritten, stream.Writes);

        stream.Position = 0;
        for (var i = 0; i < FramesWritten; i++)
        {
            using var frame = Frames.Read(stream, _ => 1024) ?? throw new InvalidDataException("missing frame");
            Assert.Equal(FrameType, frame.Type);
            Assert.Equal(i, frame.Reader.ReadInt32());
            Assert.Equal("frame " + i, frame.Reader.ReadString());
        }
        Assert.Null(Frames.Read(stream, _ => 1024));
    }
}
