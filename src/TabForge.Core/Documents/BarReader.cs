using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace TabForge.Documents;

// Owns: reading one encoded bar back: the exact mirror of BarWriter.
// Does not own: the field order of a bar (BarCodec.Read) and the writing side (BarWriter).
// Tests: TestUndoDeltaStates.
internal ref struct BarReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    public BarReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _position = 0;
    }

    public bool AtEnd => _position == _data.Length;

    internal int Remaining => _data.Length - _position;

    public ulong VarU()
    {
        ulong value = 0;
        var shift = 0;
        while (true)
        {
            if (shift > 63) throw new InvalidDataException("The undo state is damaged.");
            if (_position >= _data.Length) throw new InvalidDataException("The undo state is damaged.");
            var next = _data[_position++];
            value |= (ulong)(next & 0x7F) << shift;
            if (next < 0x80) return value;
            shift += 7;
        }
    }

    private static int Unzig(ulong raw) => (int)((uint)raw >> 1) ^ -(int)((uint)raw & 1);

    public int Int() => Unzig(VarU());

    public int? NInt()
    {
        var raw = VarU();
        return raw == 0 ? null : Unzig(raw - 1);
    }

    public bool? NBool() => VarU() switch { 0 => null, 1 => false, _ => true };

    private double DblTagged(ulong tag)
    {
        if (tag == 0) return 0d;
        if (_data.Length - _position < 8) throw new InvalidDataException("The undo state is damaged.");
        var bits = BinaryPrimitives.ReadInt64LittleEndian(_data.Slice(_position, 8));
        _position += 8;
        return BitConverter.Int64BitsToDouble(bits);
    }

    public double Dbl() => DblTagged(VarU());

    public double? NDbl()
    {
        var tag = VarU();
        return tag == 2 ? null : DblTagged(tag);
    }

    public string? Str()
    {
        var raw = VarU();
        if (raw == 0) return null;
        if (raw - 1 > (ulong)(int.MaxValue / 2)) throw new InvalidDataException("The undo state is damaged.");
        var length = (int)(raw - 1);
        if (length == 0) return "";
        if (_data.Length - _position < length * 2) throw new InvalidDataException("The undo state is damaged.");
        var text = new string(MemoryMarshal.Cast<byte, char>(_data.Slice(_position, length * 2)));
        _position += length * 2;
        return text;
    }

    /// <summary>List length, or -1 for a null list.</summary>
    public int Count()
    {
        var raw = VarU();
        if (raw > int.MaxValue) throw new InvalidDataException("The undo state is damaged.");
        return (int)raw - 1;
    }
}
