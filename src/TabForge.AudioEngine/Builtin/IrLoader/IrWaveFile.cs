using System.Buffers.Binary;

namespace TabForge.AudioEngine.Builtin.IrLoader;

// Owns: reading an impulse-response WAV file into float channels: RIFF/WAVE with PCM 8/16/24/32-bit, IEEE float 32/64-bit, and WAVE_FORMAT_EXTENSIBLE of either.
// Does not own: resampling, trimming or levelling (IrPrep), the convolution (PartitionedConvolver).
// Tests: BuiltinEffectTests.
// Threads: a worker (never the audio thread: it reads the file).

/// <summary>A decoded impulse response: one array per channel (one or two) at <paramref name="SampleRate"/>.</summary>
public sealed record IrAudio(float[][] Channels, int SampleRate)
{
    public int Length => Channels.Length == 0 ? 0 : Channels[0].Length;
}

/// <summary>Reads impulse responses from WAV files. Only the first two channels are kept; longer than <see cref="MaxSeconds"/> is cut.</summary>
public static class IrWaveFile
{
    public const int MaxSeconds = 10;
    private const int MaxFileBytes = 256 * 1024 * 1024;

    /// <summary>Reads <paramref name="path"/>; throws <see cref="FileNotFoundException"/> or <see cref="InvalidDataException"/> with a readable message.</summary>
    public static IrAudio Read(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"The IR file was not found: {path}", path);
        var info = new FileInfo(path);
        if (info.Length > MaxFileBytes) throw new InvalidDataException("The IR file is too large.");
        return Parse(File.ReadAllBytes(path));
    }

    /// <summary>Decodes the bytes of a WAV file.</summary>
    public static IrAudio Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12 || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes.Slice(8, 4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("The IR file is not a WAV file.");
        int format = 0, channels = 0, rate = 0, bits = 0;
        ReadOnlySpan<byte> data = default;
        var pos = 12;
        while (pos + 8 <= bytes.Length)
        {
            var id = bytes.Slice(pos, 4);
            var size = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(pos + 4, 4)), (uint)(bytes.Length - pos - 8));
            var body = bytes.Slice(pos + 8, size);
            if (id.SequenceEqual("fmt "u8) && size >= 16)
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(body);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
                rate = BinaryPrimitives.ReadInt32LittleEndian(body[4..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(body[14..]);
                if (format == 0xFFFE && size >= 26) format = BinaryPrimitives.ReadUInt16LittleEndian(body[24..]);   // extensible: the sub-format's first two bytes
            }
            else if (id.SequenceEqual("data"u8)) data = body;
            pos += 8 + size + (size & 1);
        }
        if (channels is < 1 or > 32 || rate is < 8000 or > 768000) throw new InvalidDataException("The IR file has no valid format.");
        var bytesPer = bits / 8;
        var isFloat = format == 3 && bits is 32 or 64;
        if (!isFloat && (format != 1 || bits is not (8 or 16 or 24 or 32))) throw new InvalidDataException($"This WAV encoding is not supported (format {format}, {bits}-bit). Use PCM or 32-bit float.");
        var frames = Math.Min(data.Length / (bytesPer * channels), MaxSeconds * rate);
        if (frames == 0) throw new InvalidDataException("The IR file holds no audio.");
        var kept = Math.Min(channels, 2);
        var result = new float[kept][];
        for (var c = 0; c < kept; c++) result[c] = new float[frames];
        for (var f = 0; f < frames; f++)
            for (var c = 0; c < kept; c++)
                result[c][f] = Sample(data.Slice((f * channels + c) * bytesPer, bytesPer), bits, isFloat);
        return new IrAudio(result, rate);
    }

    private static float Sample(ReadOnlySpan<byte> s, int bits, bool isFloat) => (isFloat, bits) switch
    {
        (true, 32) => BinaryPrimitives.ReadSingleLittleEndian(s),
        (true, _) => (float)BinaryPrimitives.ReadDoubleLittleEndian(s),
        (_, 8) => (s[0] - 128) / 128f,
        (_, 16) => BinaryPrimitives.ReadInt16LittleEndian(s) / 32768f,
        (_, 24) => ((s[0] | (s[1] << 8) | (s[2] << 16)) << 8 >> 8) / 8388608f,
        _ => BinaryPrimitives.ReadInt32LittleEndian(s) / 2147483648f,
    };

    /// <summary>Writes a 32-bit float WAV (tests and tools); <paramref name="channels"/> all of one length.</summary>
    public static byte[] Write(float[][] channels, int sampleRate)
    {
        var frames = channels[0].Length;
        var dataBytes = frames * channels.Length * 4;
        var bytes = new byte[44 + dataBytes];
        "RIFF"u8.CopyTo(bytes); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 36 + dataBytes); "WAVE"u8.CopyTo(bytes.AsSpan(8));
        "fmt "u8.CopyTo(bytes.AsSpan(12)); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20), 3); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22), (short)channels.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), sampleRate); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), sampleRate * 4 * channels.Length);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32), (short)(4 * channels.Length)); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34), 32);
        "data"u8.CopyTo(bytes.AsSpan(36)); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40), dataBytes);
        for (var f = 0; f < frames; f++)
            for (var c = 0; c < channels.Length; c++)
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(44 + (f * channels.Length + c) * 4), channels[c][f]);
        return bytes;
    }
}
