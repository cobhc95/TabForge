namespace TabForge.Capture;

// Owns: choosing at most 256 colours for a set of frames (median cut over the colours that occur) and mapping every pixel to its nearest one.
// Does not own: the GIF byte layout (GifEncoder), the compression (GifLzw).
// Tests: TabForge.Avalonia.Tests.GifEncoderTests.

/// <summary>A palette built from frames. Mapping is nearest colour with no dithering: screenshots are flat colours and dithering would bloat the file.</summary>
public sealed class GifPalette
{
    private readonly byte[] _rgb;
    private readonly Dictionary<int, byte> _cache = new();

    private GifPalette(byte[] rgb) => _rgb = rgb;

    /// <summary>The number of colours (1 to 256).</summary>
    public int Count => _rgb.Length / 3;

    /// <summary>The palette as R, G, B triples.</summary>
    public ReadOnlySpan<byte> Rgb => _rgb;

    /// <summary>Builds a palette of at most <paramref name="maxColours"/> colours from the opaque pixels of <paramref name="frames"/>.</summary>
    public static GifPalette Build(IEnumerable<GifFrame> frames, int maxColours)
    {
        if (maxColours is < 2 or > 256) throw new ArgumentOutOfRangeException(nameof(maxColours), "A GIF palette holds 2 to 256 colours.");
        var counts = new Dictionary<int, int>();
        foreach (var frame in frames)
        {
            var px = frame.Rgba;
            for (var i = 0; i < px.Length; i += 4)
            {
                if (px[i + 3] < 128) continue;
                var key = (px[i] << 16) | (px[i + 1] << 8) | px[i + 2];
                counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
            }
        }
        if (counts.Count == 0) counts[0] = 1;
        var boxes = new List<List<KeyValuePair<int, int>>> { counts.ToList() };
        while (boxes.Count < maxColours)
        {
            var widest = boxes.Where(b => b.Count > 1).OrderByDescending(Range).FirstOrDefault();
            if (widest is null) break;
            boxes.Remove(widest);
            var channel = WidestChannel(widest);
            widest.Sort((a, b) => Channel(a.Key, channel).CompareTo(Channel(b.Key, channel)));
            var half = widest.Sum(p => (long)p.Value) / 2;
            var split = 1;
            long seen = widest[0].Value;
            while (split < widest.Count - 1 && seen < half) seen += widest[split++].Value;
            boxes.Add(widest.GetRange(0, split));
            boxes.Add(widest.GetRange(split, widest.Count - split));
        }
        var rgb = new byte[boxes.Count * 3];
        for (var b = 0; b < boxes.Count; b++)
        {
            double r = 0, g = 0, bl = 0, total = 0;
            foreach (var entry in boxes[b])
            {
                r += Channel(entry.Key, 0) * entry.Value; g += Channel(entry.Key, 1) * entry.Value; bl += Channel(entry.Key, 2) * entry.Value; total += entry.Value;
            }
            rgb[b * 3] = (byte)Math.Round(r / total);
            rgb[b * 3 + 1] = (byte)Math.Round(g / total);
            rgb[b * 3 + 2] = (byte)Math.Round(bl / total);
        }
        return new GifPalette(rgb);
    }

    /// <summary>The index of the palette colour nearest to this colour.</summary>
    public byte Nearest(byte r, byte g, byte b)
    {
        var key = (r << 16) | (g << 8) | b;
        if (_cache.TryGetValue(key, out var hit)) return hit;
        var best = 0;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < Count; i++)
        {
            int dr = r - _rgb[i * 3], dg = g - _rgb[i * 3 + 1], db = b - _rgb[i * 3 + 2];
            var distance = dr * dr + dg * dg + db * db;
            if (distance < bestDistance) { bestDistance = distance; best = i; }
        }
        return _cache[key] = (byte)best;
    }

    private static int Channel(int key, int channel) => (key >> (16 - 8 * channel)) & 0xFF;

    private static int WidestChannel(List<KeyValuePair<int, int>> box)
    {
        var best = 0;
        var bestRange = -1;
        for (var c = 0; c < 3; c++)
        {
            var range = box.Max(p => Channel(p.Key, c)) - box.Min(p => Channel(p.Key, c));
            if (range > bestRange) { bestRange = range; best = c; }
        }
        return best;
    }

    private static int Range(List<KeyValuePair<int, int>> box) => Enumerable.Range(0, 3).Max(c => box.Max(p => Channel(p.Key, c)) - box.Min(p => Channel(p.Key, c)));
}
