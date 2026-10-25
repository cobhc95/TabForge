namespace TabForge.Capture;

// Owns: writing an animated GIF89a from frames: one shared palette, only the changed rectangle of each frame (the rest transparent over the previous frame), a loop count and per-frame delays.
// Does not own: getting the pixels (the capture host), the palette choice (GifPalette) or the compression (GifLzw).
// Tests: TabForge.Avalonia.Tests.GifEncoderTests.
public static class GifEncoder
{
    /// <summary>Colours kept for the picture; the last palette entry is the transparent one.</summary>
    public const int MaxPictureColours = 255;

    /// <summary>Writes <paramref name="frames"/> (all the same size, at least one) to <paramref name="output"/>. <paramref name="loops"/> 0 repeats forever.</summary>
    public static void Write(Stream output, IReadOnlyList<GifFrame> frames, int loops = 0, int maxColours = MaxPictureColours)
    {
        if (frames.Count == 0) throw new ArgumentException("A GIF needs at least one frame.", nameof(frames));
        foreach (var frame in frames)
        {
            frame.Validate();
            if (frame.Width != frames[0].Width || frame.Height != frames[0].Height) throw new ArgumentException("Every frame of a GIF must have the same size.", nameof(frames));
        }
        var palette = GifPalette.Build(frames, Math.Min(maxColours, MaxPictureColours));
        var transparent = palette.Count;
        var bits = Math.Max(1, (int)Math.Ceiling(Math.Log2(transparent + 1)));
        var width = frames[0].Width;
        var height = frames[0].Height;

        WriteHeader(output, width, height, palette, bits);
        if (frames.Count > 1) WriteLoop(output, loops);
        byte[]? previous = null;
        foreach (var frame in frames)
        {
            var indexes = Quantise(frame, palette);
            WriteFrame(output, frame, indexes, previous, transparent, bits);
            previous = indexes;
        }
        output.WriteByte(0x3B);
    }

    private static byte[] Quantise(GifFrame frame, GifPalette palette)
    {
        var px = frame.Rgba;
        var indexes = new byte[frame.Width * frame.Height];
        for (var i = 0; i < indexes.Length; i++)
            indexes[i] = palette.Nearest(px[i * 4], px[i * 4 + 1], px[i * 4 + 2]);
        return indexes;
    }

    private static void WriteHeader(Stream output, int width, int height, GifPalette palette, int bits)
    {
        output.Write("GIF89a"u8);
        WriteShort(output, width);
        WriteShort(output, height);
        output.WriteByte((byte)(0x80 | ((bits - 1) << 4) | (bits - 1)));
        output.WriteByte(0);
        output.WriteByte(0);
        var table = new byte[3 * (1 << bits)];
        palette.Rgb.CopyTo(table);
        output.Write(table);
    }

    private static void WriteLoop(Stream output, int loops)
    {
        output.WriteByte(0x21);
        output.WriteByte(0xFF);
        output.WriteByte(11);
        output.Write("NETSCAPE2.0"u8);
        output.WriteByte(3);
        output.WriteByte(1);
        WriteShort(output, loops);
        output.WriteByte(0);
    }

    private static void WriteFrame(Stream output, GifFrame frame, byte[] indexes, byte[]? previous, int transparent, int bits)
    {
        var (left, top, w, h) = previous is null ? (0, 0, frame.Width, frame.Height) : ChangedRectangle(frame.Width, frame.Height, indexes, previous);
        var pixels = new byte[w * h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var at = (top + y) * frame.Width + left + x;
                pixels[y * w + x] = previous is not null && previous[at] == indexes[at] ? (byte)transparent : indexes[at];
            }
        output.WriteByte(0x21);
        output.WriteByte(0xF9);
        output.WriteByte(4);
        output.WriteByte((byte)((1 << 2) | (previous is null ? 0 : 1)));
        WriteShort(output, Math.Clamp((int)Math.Round(frame.DelayMs / 10.0), 1, ushort.MaxValue));
        output.WriteByte((byte)transparent);
        output.WriteByte(0);
        output.WriteByte(0x2C);
        WriteShort(output, left);
        WriteShort(output, top);
        WriteShort(output, w);
        WriteShort(output, h);
        output.WriteByte(0);
        GifLzw.Write(output, pixels, bits);
    }

    /// <summary>The smallest rectangle holding every pixel that differs from the previous frame; one pixel when nothing changed.</summary>
    private static (int Left, int Top, int Width, int Height) ChangedRectangle(int width, int height, byte[] now, byte[] before)
    {
        int minX = width, minY = height, maxX = -1, maxY = -1;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                if (now[y * width + x] == before[y * width + x]) continue;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
        return maxX < 0 ? (0, 0, 1, 1) : (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static void WriteShort(Stream output, int value)
    {
        output.WriteByte((byte)(value & 0xFF));
        output.WriteByte((byte)((value >> 8) & 0xFF));
    }
}
