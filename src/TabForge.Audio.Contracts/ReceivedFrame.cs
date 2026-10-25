using System.Buffers;
using System.IO;
using System.Text;

namespace TabForge.Audio.Contracts;

// Owns: one received frame, its type byte and a reader over its payload, and the pooled buffer behind them.
// Does not own: the framing and the size limits (Frames, FrameLimits) or what a type means (EngineProtocol, the command handlers).
// Tests: TestEngineMessagesRoundTrip, TestEngineMessagesGolden.
/// <summary>
/// A frame read by <see cref="Frames.Read"/>. Its payload lives in a buffer rented from the shared array pool; <see cref="Dispose"/> returns
/// that buffer. Dispose only when nothing still reads <see cref="Reader"/>: a frame that is never disposed is simply collected by the GC.
/// </summary>
public readonly struct ReceivedFrame : IDisposable
{
    private readonly byte[]? _buffer;

    internal ReceivedFrame(byte[] buffer, int length)
    {
        _buffer = buffer;
        Type = buffer[0];
        Reader = new BinaryReader(new MemoryStream(buffer, 1, length - 1, writable: false), Encoding.UTF8);
    }

    /// <summary>The command or event type byte.</summary>
    public byte Type { get; }

    /// <summary>The payload after the type byte.</summary>
    public BinaryReader Reader { get; }

    public void Deconstruct(out byte type, out BinaryReader reader)
    {
        type = Type;
        reader = Reader;
    }

    /// <summary>Returns the payload buffer to the pool; <see cref="Reader"/> must not be used afterwards.</summary>
    public void Dispose()
    {
        if (_buffer is not null) ArrayPool<byte>.Shared.Return(_buffer);
    }
}
