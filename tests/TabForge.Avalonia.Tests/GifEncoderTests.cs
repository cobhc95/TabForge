using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TabForge.Capture;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the GIF encoder checks: header, loop block, frame count and delays, and a pixel round trip through a small decoder written here.
/// Does not own: the encoder (TabForge.Capture.GifEncoder).
/// Tests: this class.
/// </summary>
public class GifEncoderTests
{
    private static GifFrame Solid(int w, int h, byte r, byte g, byte b, int delay = 100, Action<byte[]>? edit = null)
    {
        var px = new byte[w * h * 4];
        for (var i = 0; i < px.Length; i += 4) { px[i] = r; px[i + 1] = g; px[i + 2] = b; px[i + 3] = 255; }
        edit?.Invoke(px);
        return new GifFrame(w, h, px, delay);
    }

    private static byte[] Encode(IReadOnlyList<GifFrame> frames)
    {
        using var stream = new MemoryStream();
        GifEncoder.Write(stream, frames);
        return stream.ToArray();
    }

    [Fact]
    public void WritesHeaderSizeLoopAndTrailer()
    {
        var bytes = Encode(new[] { Solid(7, 5, 10, 20, 30), Solid(7, 5, 200, 20, 30) });
        Assert.Equal("GIF89a", Encoding.ASCII.GetString(bytes, 0, 6));
        Assert.Equal(7, bytes[6] | (bytes[7] << 8));
        Assert.Equal(5, bytes[8] | (bytes[9] << 8));
        Assert.Contains("NETSCAPE2.0", Encoding.ASCII.GetString(bytes));
        Assert.Equal(0x3B, bytes[^1]);
    }

    [Fact]
    public void RoundTripsFramesDelaysAndPixels()
    {
        var frames = new[]
        {
            Solid(40, 30, 10, 20, 30, 100),
            Solid(40, 30, 10, 20, 30, 250, px => { for (var i = 0; i < 40 * 4 * 5; i += 4) { px[i] = 250; px[i + 1] = 240; px[i + 2] = 10; } }),
            Solid(40, 30, 200, 100, 50, 70),
        };
        var decoded = GifDecoder.Decode(Encode(frames));
        Assert.Equal(3, decoded.Count);
        Assert.Equal(new[] { 100, 250, 70 }, decoded.ConvertAll(f => f.DelayMs));
        for (var f = 0; f < frames.Length; f++)
            for (var i = 0; i < 40 * 30; i++)
                Assert.True(Near(frames[f].Rgba, decoded[f].Rgb, i), $"frame {f} pixel {i}");
    }

    [Fact]
    public void ManyColoursAndLongRunsSurviveLzwTableResets()
    {
        var rng = new Random(7);
        var frame = Solid(120, 100, 0, 0, 0, 50, px => { for (var i = 0; i < px.Length; i += 4) { px[i] = (byte)(rng.Next(6) * 40); px[i + 1] = (byte)(rng.Next(6) * 40); px[i + 2] = (byte)(rng.Next(4) * 64); } });
        var decoded = GifDecoder.Decode(Encode(new[] { frame }));
        for (var i = 0; i < 120 * 100; i++) Assert.True(Near(frame.Rgba, decoded[0].Rgb, i), $"pixel {i}");
    }

    [Fact]
    public void RejectsMismatchedFramesAndEmptyInput()
    {
        Assert.Throws<ArgumentException>(() => Encode(Array.Empty<GifFrame>()));
        Assert.Throws<ArgumentException>(() => Encode(new[] { Solid(4, 4, 0, 0, 0), Solid(5, 4, 0, 0, 0) }));
        Assert.Throws<ArgumentException>(() => new GifFrame(2, 2, new byte[3], 10).Validate());
    }

    private static bool Near(byte[] rgba, byte[] rgb, int pixel) =>
        Math.Abs(rgba[pixel * 4] - rgb[pixel * 3]) <= 1 && Math.Abs(rgba[pixel * 4 + 1] - rgb[pixel * 3 + 1]) <= 1 && Math.Abs(rgba[pixel * 4 + 2] - rgb[pixel * 3 + 2]) <= 1;

    /// <summary>A decoded frame: the full canvas as R, G, B after compositing, and the delay in ms.</summary>
    private sealed record DecodedFrame(byte[] Rgb, int DelayMs);

    /// <summary>A minimal GIF89a reader for the encoder output: global palette, transparency, sub-rectangles, no interlace.</summary>
    private static class GifDecoder
    {
        public static List<DecodedFrame> Decode(byte[] data)
        {
            var pos = 6;
            int Short() { var v = data[pos] | (data[pos + 1] << 8); pos += 2; return v; }
            var w = Short(); var h = Short();
            var packed = data[pos]; pos += 3;
            var palette = new byte[3 * (1 << ((packed & 7) + 1))];
            Array.Copy(data, pos, palette, 0, palette.Length); pos += palette.Length;
            var canvas = new byte[w * h * 3];
            var frames = new List<DecodedFrame>();
            int delay = 0, transparent = -1;
            while (data[pos] != 0x3B)
            {
                var block = data[pos++];
                if (block == 0x21)
                {
                    var label = data[pos++];
                    if (label == 0xF9) { var flags = data[pos + 1]; delay = (data[pos + 2] | (data[pos + 3] << 8)) * 10; transparent = (flags & 1) != 0 ? data[pos + 4] : -1; }
                    while (data[pos] != 0) pos += data[pos] + 1;
                    pos++;
                    continue;
                }
                var left = Short(); var top = Short(); var iw = Short(); var ih = Short(); pos++;
                var indexes = Lzw(data, ref pos, iw * ih);
                for (var y = 0; y < ih; y++)
                    for (var x = 0; x < iw; x++)
                    {
                        var index = indexes[y * iw + x];
                        if (index == transparent) continue;
                        Array.Copy(palette, index * 3, canvas, ((top + y) * w + left + x) * 3, 3);
                    }
                frames.Add(new DecodedFrame((byte[])canvas.Clone(), delay));
            }
            return frames;
        }

        private static byte[] Lzw(byte[] data, ref int pos, int count)
        {
            var min = data[pos++];
            var stream = new List<byte>();
            while (data[pos] != 0) { stream.AddRange(new ArraySegment<byte>(data, pos + 1, data[pos])); pos += data[pos] + 1; }
            pos++;
            var clear = 1 << min; var end = clear + 1;
            var output = new List<byte>(count);
            var table = new List<byte[]>();
            void Reset() { table.Clear(); for (var i = 0; i < clear + 2; i++) table.Add(i < clear ? new[] { (byte)i } : Array.Empty<byte>()); }
            Reset();
            var size = min + 1; var bitPos = 0; byte[]? previous = null;
            while (bitPos + size <= stream.Count * 8)
            {
                var code = 0;
                for (var b = 0; b < size; b++) code |= ((stream[(bitPos + b) >> 3] >> ((bitPos + b) & 7)) & 1) << b;
                bitPos += size;
                if (code == clear) { Reset(); size = min + 1; previous = null; continue; }
                if (code == end) break;
                byte[] entry = code < table.Count ? table[code] : Append(previous!, previous![0]);
                output.AddRange(entry);
                if (previous is not null) table.Add(Append(previous, entry[0]));
                previous = entry;
                if (table.Count == (1 << size) && size < 12) size++;
            }
            return output.ToArray();
        }

        private static byte[] Append(byte[] a, byte b) { var r = new byte[a.Length + 1]; a.CopyTo(r, 0); r[^1] = b; return r; }
    }
}
