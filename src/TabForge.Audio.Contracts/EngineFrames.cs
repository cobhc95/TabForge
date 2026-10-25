using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Buffers;
using System.IO;
using System.Text;

namespace TabForge.Audio.Contracts;

// Owns: the Frames wire format helpers.
// Does not own: the rest of this type (see the main file).
// Tests: no dedicated test class.
/// <summary>
/// Framing: [int32 length][byte type][payload]. Payloads use BinaryWriter primitives. Every read is bounded so a
/// broken or hostile peer cannot make the other side allocate without limit.
/// Coalescing: SetPosition (17) is last-wins idempotent (one immutable transport record per owner); senders may drop
/// all but the latest queued SetPosition per owner, and the reader applies each with a single volatile write.
/// Timed MIDI never goes through the pipe (see SharedBlock's lock-free ring and its DroppedWrites counter).
/// </summary>
public static class Frames
{
    public const int MaxFrameBytes = 32 * 1024 * 1024;

    /// <summary>A connection's frame buffer above this size is dropped after its frame, so one large message does not keep its memory.</summary>
    private const int KeptFrameBufferBytes = 256 * 1024;
    private const int LengthBytes = 4;

    /// <summary>One connection's reusable frame buffer and its writer.</summary>
    private sealed class FrameBuffer
    {
        public readonly MemoryStream Bytes = new();
        public readonly BinaryWriter Writer;
        public FrameBuffer() => Writer = new BinaryWriter(Bytes, Encoding.UTF8, leaveOpen: true);
    }

    // Key: the connection stream (weak, so a closed connection's buffer goes with it). Ceiling: one buffer per live stream, kept only while it
    // is at most KeptFrameBufferBytes. Thread: a stream's buffer is only used under lock (stream), the lock its writes already take.
    private static readonly ConditionalWeakTable<Stream, FrameBuffer> Buffers = new();

    /// <summary>Writes one frame with one stream write, from the connection's reused buffer (no allocation per frame in steady state).</summary>
    public static void Write(Stream stream, byte type, Action<BinaryWriter>? payload = null)
    {
        lock (stream)
        {
            var frame = Buffers.GetValue(stream, _ => new FrameBuffer());
            var bytes = frame.Bytes;
            bytes.SetLength(0);
            var writer = frame.Writer;
            writer.Write(0);   // the length, filled in below
            writer.Write(type);
            payload?.Invoke(writer);
            writer.Flush();
            var length = bytes.Length - LengthBytes;
            if (length > MaxFrameBytes)
            {
                Buffers.Remove(stream);
                throw new InvalidDataException("Message too large.");
            }
            var buffer = bytes.GetBuffer();
            BinaryPrimitives.WriteInt32LittleEndian(buffer, (int)length);
            stream.Write(buffer, 0, (int)bytes.Length);
            stream.Flush();
            if (bytes.Capacity > KeptFrameBufferBytes) Buffers.Remove(stream);
        }
    }

    /// <summary>
    /// Reads one frame; null when the peer closed the connection. The type byte is read first and the frame is refused when its length
    /// exceeds <paramref name="maxBytesFor"/> of that type (see <see cref="FrameLimits"/>), before any payload buffer is taken. The payload
    /// goes into a buffer rented from the shared array pool: call <see cref="ReceivedFrame.Dispose"/> once nothing reads its reader any more.
    /// </summary>
    public static ReceivedFrame? Read(Stream stream, Func<byte, int> maxBytesFor)
    {
        Span<byte> header = stackalloc byte[4];
        if (!ReadExactly(stream, header)) return null;
        var length = BitConverter.ToInt32(header);
        if (length < 1 || length > MaxFrameBytes) throw new InvalidDataException("Bad message length.");
        Span<byte> typeByte = stackalloc byte[1];
        if (!ReadExactly(stream, typeByte)) return null;
        var type = typeByte[0];
        var limit = maxBytesFor(type);
        if (length > limit) throw new InvalidDataException($"Message type {type} is {length} bytes; its limit is {limit}.");
        var buffer = ArrayPool<byte>.Shared.Rent(length);
        var complete = false;
        try
        {
            buffer[0] = type;
            if (!ReadExactly(stream, buffer.AsSpan(1, length - 1))) return null;
            complete = true;
            return new ReceivedFrame(buffer, length);
        }
        finally
        {
            if (!complete) ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool ReadExactly(Stream stream, Span<byte> buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = stream.Read(buffer[read..]);
            if (n <= 0) return false;
            read += n;
        }
        return true;
    }

    public static void WriteString(this BinaryWriter writer, string? value) => writer.Write(value ?? "");

    public static string ReadBoundedString(this BinaryReader reader, int maxChars = 32_768)
    {
        var value = reader.ReadString();
        if (value.Length > maxChars) throw new InvalidDataException("Text too long.");
        return value;
    }

    public static void Write(this BinaryWriter w, EngineConfig c)
    {
        w.WriteString(c.Driver); w.WriteString(c.Device); w.Write(c.SampleRate); w.Write(c.BufferSize); w.Write(c.SeparateProcessPerPlugin); w.WriteString(c.InputDevice); w.Write(c.AsioInput); w.Write(c.AsioOutput); w.Write(c.AsioInputLast); w.Write(c.AsioInputs); w.Write(c.FollowWindowsVolume); w.Write(c.AsioOutputLast); w.Write(c.AllInputs); w.Write(c.PopGuard);
    }

    /// <summary>The tail (<see cref="EngineConfig.AllInputs"/>, then <see cref="EngineConfig.PopGuard"/>) is absent from an older sender: each missing field is on.</summary>
    public static EngineConfig ReadConfig(this BinaryReader r) => ReadConfigHead(r) with { AllInputs = TailBool(r), PopGuard = TailBool(r) };

    /// <summary>The output rate: 0 stays 0 (the device default, see <see cref="EngineConfig.SampleRate"/>), any other value is kept inside a sane range.</summary>
    private static int ReadRate(BinaryReader r) => r.ReadInt32() is var hz and > 0 ? Math.Clamp(hz, 8000, 384000) : 0;

    private static bool TailBool(BinaryReader r) => r.BaseStream.Position >= r.BaseStream.Length || r.ReadBoolean();

    private static EngineConfig ReadConfigHead(BinaryReader r) =>
        new(r.ReadBoundedString(64), r.ReadBoundedString(256), ReadRate(r), Math.Clamp(r.ReadInt32(), 16, 16384), r.ReadBoolean(), r.ReadBoundedString(256), Math.Clamp(r.ReadInt32(), 0, 62), Math.Clamp(r.ReadInt32(), 0, 62), Math.Clamp(r.ReadInt32(), 0, 63), r.ReadBoolean(), r.ReadBoolean(), Math.Clamp(r.ReadInt32(), 0, 63));

    /// <summary>The owner id at the tail of SetPosition, SetClips and SetTransport: absent (an older sender) or out of range is owner 0.</summary>
    public static int ReadOwnerTail(this BinaryReader r)
    {
        if (r.BaseStream.Position >= r.BaseStream.Length) return 0;
        var owner = r.ReadInt32();
        return owner is >= 0 and < SongOwners.Max ? owner : 0;
    }

    /// <summary>The transport speed at the tail of SetPosition: absent (an older sender) or not finite is 1; otherwise clamped to a sane playback range.</summary>
    public static double ReadSpeedTail(this BinaryReader r)
    {
        if (r.BaseStream.Position >= r.BaseStream.Length) return 1.0;
        var speed = r.ReadDouble();
        return double.IsFinite(speed) ? Math.Clamp(speed, 0.05, 8.0) : 1.0;
    }

    public static void Write(this BinaryWriter w, IReadOnlyList<ClipSpec> clips)
    {
        w.Write(clips.Count);
        foreach (var c in clips)
        {
            w.WriteString(c.File); w.Write(c.StartSec); w.Write(c.OffsetSec); w.Write(c.SourceLengthSec); w.Write(c.GainDb); w.Write(c.Pitch); w.Write(c.Speed); w.Write(c.FadeInSec); w.Write(c.FadeOutSec);
        }
    }

    public static List<ClipSpec> ReadClips(this BinaryReader r)
    {
        var count = r.ReadInt32();
        if (count is < 0 or > 4096) throw new InvalidDataException("Bad clip count.");
        var list = new List<ClipSpec>(count);
        for (var i = 0; i < count; i++)
        {
            var file = r.ReadBoundedString(1024);
            double Finite(double v, double min, double max) => double.IsFinite(v) ? Math.Clamp(v, min, max) : min;
            list.Add(new ClipSpec(file, Finite(r.ReadDouble(), 0, 86_400), Finite(r.ReadDouble(), 0, 86_400), Finite(r.ReadDouble(), 0, 86_400),
                Finite(r.ReadDouble(), -96, 24), Finite(r.ReadDouble(), -24, 24), Finite(r.ReadDouble(), 0.25, 4), Finite(r.ReadDouble(), 0, 3_600), Finite(r.ReadDouble(), 0, 3_600)));
        }
        return list;
    }

    public static void Write(this BinaryWriter w, IReadOnlyList<MidiProcSpec> list)
    {
        w.Write(list.Count);
        foreach (var p in list) { w.WriteString(p.Type); w.Write(p.Enabled); w.WriteString(p.ParamsJson); }
    }

    public static List<MidiProcSpec> ReadMidiProcs(this BinaryReader r)
    {
        var count = r.ReadInt32();
        if (count is < 0 or > 64) throw new InvalidDataException("Bad MIDI processor count.");
        var list = new List<MidiProcSpec>(count);
        for (var i = 0; i < count; i++) list.Add(new MidiProcSpec(r.ReadBoundedString(48), r.ReadBoolean(), r.ReadBoundedString(64 * 1024)));
        return list;
    }

    public static void Write(this BinaryWriter w, IReadOnlyList<PluginSpec> chain)
    {
        w.Write(chain.Count);
        foreach (var p in chain)
        {
            w.WriteString(p.Path); w.WriteString(p.Format); w.Write(p.IsInstrument); w.Write(p.Enabled); w.Write(p.Wet); w.WriteString(p.State); w.WriteString(p.Name); w.WriteString(p.Pins); w.Write(p.OutputDb); w.WriteString(p.Id); w.Write(p.Skip); w.Write(p.Untrusted); w.WriteString(p.ExpectedSha256); w.Write(p.LocationTrusted);
        }
    }

    public static List<PluginSpec> ReadChain(this BinaryReader r)
    {
        var count = r.ReadInt32();
        if (count is < 0 or > 64) throw new InvalidDataException("Bad chain length.");
        var list = new List<PluginSpec>(count);
        for (var i = 0; i < count; i++)
        {
            var path = r.ReadBoundedString(1024); var format = r.ReadBoundedString(8); var inst = r.ReadBoolean();
            var enabled = r.ReadBoolean(); var wet = Math.Clamp(r.ReadInt32(), 0, 100);
            var state = r.ReadBoundedString(PluginStateLimits.MaxBase64Chars);
            var name = r.ReadBoundedString(256);
            var pins = r.ReadBoundedString(32);
            var outputDb = r.ReadDouble();
            var id = r.ReadBoundedString(64);
            var skip = r.ReadBoolean();
            var untrusted = r.ReadBoolean();
            var expectedSha = r.ReadBoundedString(64);
            var locationTrusted = r.ReadBoolean();
            list.Add(new PluginSpec(path, format, inst, enabled, wet, state.Length == 0 ? null : state, name, pins,
                double.IsFinite(outputDb) ? Math.Clamp(outputDb, -60, 12) : 0, id, skip, untrusted, expectedSha, locationTrusted));
        }
        return list;
    }
}
