namespace TabForge.Sf2Synth.Dsp;

// Owns: loading a cabinet impulse response from a .wav (PCM 16/24/32 or float 32, any channels: the first is used, at most
//   MaxSeconds), resampled to the engine rate and normalised to 0 dB at 1 kHz. Off the audio thread; throws FormatException.
// Does not own: running it (FftConvolver), the synthetic cabinet (Guitar/CabinetImpulse).
// Tests: GuitarLayerTests.
internal static class WavIr
{
    public const double MaxSeconds = 2;

    public static float[] Load(string path, double rate)
    {
        var b = File.ReadAllBytes(path);
        if (b.Length < 12 || BitConverter.ToUInt32(b, 0) != 0x46464952 || BitConverter.ToUInt32(b, 8) != 0x45564157) throw new FormatException("not a WAV file");
        int format = 0, channels = 0, bits = 0, srate = 0, at = 12;
        while (at + 8 <= b.Length)
        {
            var id = BitConverter.ToUInt32(b, at);
            var size = (int)Math.Min(BitConverter.ToUInt32(b, at + 4), (uint)(b.Length - at - 8));
            var body = at + 8;
            if (id == 0x20746D66)   // "fmt "
            {
                (format, channels, srate, bits) = (BitConverter.ToUInt16(b, body), BitConverter.ToUInt16(b, body + 2), BitConverter.ToInt32(b, body + 4), BitConverter.ToUInt16(b, body + 14));
                if (format == 0xFFFE && size >= 26) format = BitConverter.ToUInt16(b, body + 24);
            }
            else if (id == 0x61746164 && channels > 0)   // "data"
                return Normalise(Resampler.Convert(Decode(b.AsSpan(body, size), format, channels, bits, (int)(MaxSeconds * srate)), srate, rate), rate);
            at = body + size + (size & 1);
        }
        throw new FormatException("no audio data");
    }

    private static float[] Decode(ReadOnlySpan<byte> d, int format, int channels, int bits, int max)
    {
        var bytes = bits / 8;
        if (!(format == 1 && bytes is 2 or 3 or 4) && !(format == 3 && bytes == 4)) throw new FormatException($"WAV format {format}/{bits} bit not supported");
        var n = Math.Min(max, d.Length / (bytes * channels));
        if (n == 0) throw new FormatException("empty WAV");
        var x = new float[n];
        for (var i = 0; i < n; i++)
        {
            var s = d.Slice(i * bytes * channels, bytes);
            x[i] = format == 3 ? BitConverter.ToSingle(s) : bytes switch
            {
                2 => BitConverter.ToInt16(s) / 32768f,
                3 => ((s[0] << 8) | (s[1] << 16) | (s[2] << 24)) / 2147483648f,
                _ => BitConverter.ToInt32(s) / 2147483648f,
            };
        }
        return x;
    }

    public static float[] Normalise(float[] h, double rate)
    {
        double re = 0, im = 0, w = 2 * Math.PI * 1000 / rate;
        for (var n = 0; n < h.Length; n++) { re += h[n] * Math.Cos(w * n); im -= h[n] * Math.Sin(w * n); }
        var mag = Math.Sqrt(re * re + im * im);
        if (!(mag > 1e-6) || h.Any(v => !float.IsFinite(v))) throw new FormatException("silent or broken impulse");
        return h.Select(v => (float)(v / mag)).ToArray();
    }
}
