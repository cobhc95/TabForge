namespace TabForge.Capture;

// Owns: the variable-width LZW compression of one GIF image's colour indexes, written as data sub-blocks.
// Does not own: the GIF headers and frame layout (GifEncoder).
// Tests: TabForge.Avalonia.Tests.GifEncoderTests (round trip through a decoder).
internal static class GifLzw
{
    private const int MaxCodes = 4096;

    /// <summary>Writes the minimum code size byte, the compressed <paramref name="indexes"/> in sub-blocks of at most 255 bytes, and the block terminator.</summary>
    public static void Write(Stream output, ReadOnlySpan<byte> indexes, int colourBits)
    {
        var minSize = Math.Max(2, colourBits);
        output.WriteByte((byte)minSize);
        var clear = 1 << minSize;
        var end = clear + 1;
        var writer = new BitWriter(output);
        var codeSize = minSize + 1;
        var table = new Dictionary<int, int>();
        var next = end + 1;
        writer.Write(clear, codeSize);
        if (indexes.Length == 0) { writer.Write(end, codeSize); writer.Finish(); return; }
        int prefix = indexes[0];
        for (var i = 1; i < indexes.Length; i++)
        {
            var symbol = indexes[i];
            var key = (prefix << 8) | symbol;
            if (table.TryGetValue(key, out var known)) { prefix = known; continue; }
            writer.Write(prefix, codeSize);
            if (next < MaxCodes)
            {
                table[key] = next++;
                if (next > (1 << codeSize) && codeSize < 12) codeSize++;
            }
            else
            {
                writer.Write(clear, codeSize);
                table.Clear();
                next = end + 1;
                codeSize = minSize + 1;
            }
            prefix = symbol;
        }
        writer.Write(prefix, codeSize);
        writer.Write(end, codeSize);
        writer.Finish();
    }

    private sealed class BitWriter(Stream output)
    {
        private readonly byte[] _block = new byte[255];
        private int _length;
        private int _bits;
        private int _count;

        public void Write(int code, int size)
        {
            _bits |= code << _count;
            _count += size;
            while (_count >= 8) { Push((byte)(_bits & 0xFF)); _bits >>= 8; _count -= 8; }
        }

        public void Finish()
        {
            if (_count > 0) Push((byte)(_bits & 0xFF));
            Flush();
            output.WriteByte(0);
        }

        private void Push(byte value)
        {
            _block[_length++] = value;
            if (_length == _block.Length) Flush();
        }

        private void Flush()
        {
            if (_length == 0) return;
            output.WriteByte((byte)_length);
            output.Write(_block, 0, _length);
            _length = 0;
        }
    }
}
