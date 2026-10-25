using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace TabForge.Documents;

// Owns: the growable byte buffer that bars are encoded into, and the variable-length writers the bar codec uses.
// Does not own: the field order of a bar (BarCodec) and the reading side (BarReader).
// Tests: TestUndoDeltaStates.
/// <summary>Growable byte buffer reused for every bar encoded.</summary>
internal sealed class BarWriter
{
    private byte[] _buffer = new byte[4096];
    private int _length;

    public ReadOnlySpan<byte> Written => _buffer.AsSpan(0, _length);

    public void Reset() => _length = 0;

    private Span<byte> Take(int count)
    {
        if (_length + count > _buffer.Length) Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + count));
        var span = _buffer.AsSpan(_length, count);
        _length += count;
        return span;
    }

    public void VarU(ulong value)
    {
        if (_length + 10 > _buffer.Length) Array.Resize(ref _buffer, _buffer.Length * 2);
        var buffer = _buffer;
        var at = _length;
        while (value >= 0x80)
        {
            buffer[at++] = (byte)(value | 0x80);
            value >>= 7;
        }
        buffer[at++] = (byte)value;
        _length = at;
    }

    public void Int(int value) => VarU((uint)((value << 1) ^ (value >> 31)));

    public void NInt(int? value) => VarU(value is { } v ? (ulong)(uint)((v << 1) ^ (v >> 31)) + 1 : 0);

    public void NBool(bool? value) => VarU(value is null ? 0UL : value.Value ? 2UL : 1UL);

    public void Dbl(double value)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        if (bits == 0) { VarU(0); return; }
        VarU(1);
        BinaryPrimitives.WriteInt64LittleEndian(Take(8), bits);
    }

    public void NDbl(double? value)
    {
        if (value is null) { VarU(2); return; }
        Dbl(value.Value);
    }

    public void Str(string? value)
    {
        if (value is null) { VarU(0); return; }
        VarU((ulong)value.Length + 1);
        if (value.Length > 0) MemoryMarshal.AsBytes(value.AsSpan()).CopyTo(Take(value.Length * 2));
    }

    public void Count(int? count) => VarU(count is { } c ? (ulong)c + 1 : 0);
}
