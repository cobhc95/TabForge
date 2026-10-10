using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using TabForge.Services.Video;

namespace TabForge;

/// <summary>
/// The audio of an encoded MP4 plays at the pitch and length it was fed: a 440 Hz sine goes through <see cref="LiveVideoSession"/> at 44.1 and
/// 48 kHz, is decoded with the Media Foundation source reader and measured (part of <see cref="SelfTest"/>).
/// </summary>
public static partial class SelfTest
{
    /// <summary>Decodes the audio stream to 16-bit PCM; returns the rate the reader reports, the channels, and the interleaved samples.</summary>
    private static (int Rate, int Channels, short[] Pcm, int NativeRate) DecodeMp4Audio(string path)
    {
        Mf.MFStartup(Mf.Version, 0);
        try
        {
            Mf.MFCreateSourceReaderFromURL(path, null, out var reader);
            reader.GetNativeMediaType(Mf.FirstAudioStream, 0, out var native);
            native.GetUINT32(Mf.AudioRate, out var nativeRate);
            Mf.MFCreateMediaType(out var want);
            want.SetGUID(Mf.MajorType, Mf.Audio); want.SetGUID(Mf.Subtype, Mf.Pcm);
            want.SetUINT32(Mf.AudioBits, 16);
            reader.SetCurrentMediaType(Mf.FirstAudioStream, IntPtr.Zero, want);
            reader.GetCurrentMediaType(Mf.FirstAudioStream, out var got);
            got.GetUINT32(Mf.AudioRate, out var rate); got.GetUINT32(Mf.AudioChannels, out var channels);
            var bytes = new System.Collections.Generic.List<byte>();
            while (true)
            {
                reader.ReadSample(Mf.FirstAudioStream, 0, out _, out var flags, out _, out var sample);
                if (sample is not null)
                {
                    sample.ConvertToContiguousBuffer(out var buffer);
                    buffer.Lock(out var ptr, out _, out var length);
                    var chunk = new byte[length]; Marshal.Copy(ptr, chunk, 0, length); bytes.AddRange(chunk);
                    buffer.Unlock(); Marshal.ReleaseComObject(buffer); Marshal.ReleaseComObject(sample);
                }
                if ((flags & Mf.EndOfStream) != 0) break;
            }
            Marshal.ReleaseComObject(reader);
            var pcm = new short[bytes.Count / 2];
            Buffer.BlockCopy(bytes.ToArray(), 0, pcm, 0, pcm.Length * 2);
            return (rate, channels, pcm, nativeRate);
        }
        finally { Mf.MFShutdown(); }
    }

    /// <summary>The strongest frequency (whole Hz, 100..4000) in the first channel of interleaved samples, by Goertzel over up to the first 2 s after 0.2 s.</summary>
    private static double DominantHz(short[] pcm, int channels, int rate)
    {
        var x = new float[Math.Min(pcm.Length / channels - rate / 5, rate * 2)];
        for (var i = 0; i < x.Length; i++) x[i] = pcm[(i + rate / 5) * channels];
        return DominantHz(x, rate);
    }

    private static double DominantHz(float[] x, int rate)
    {
        double best = 0, bestHz = 0;
        for (var hz = 100; hz <= 4000; hz++)
        {
            var coeff = 2 * Math.Cos(2 * Math.PI * hz / rate); double s1 = 0, s2 = 0;
            foreach (var v in x) { var s0 = v + coeff * s1 - s2; s2 = s1; s1 = s0; }
            var power = s1 * s1 + s2 * s2 - coeff * s1 * s2;
            if (power > best) { best = power; bestHz = hz; }
        }
        return bestHz;
    }

    private static void TestVideoAudioPitch()
    {
        foreach (var rate in new[] { 44100, 48000 })
        {
            var path = Path.Combine(Path.GetTempPath(), $"tf-pitch-{Guid.NewGuid():N}.mp4");
            try
            {
                var session = LiveVideoSession.Start(path, 1920, 1080, 30, _ => true);
                var chunk = new float[240 * 2];
                var n = 0L;
                for (var c = 0; c < 600; c++)   // 3 s in 240-frame chunks, paced like the engine's tap
                {
                    for (var i = 0; i < 240; i++, n++) chunk[i * 2] = chunk[i * 2 + 1] = 0.3f * MathF.Sin(2 * MathF.PI * 440 * n / rate);
                    session.OnAudio(rate, c * 240L, (float[])chunk.Clone(), 240);   // the client hands out a fresh array per chunk
                    Thread.Sleep(5);
                }
                session.Finish();
                var (outRate, channels, pcm, nativeRate) = DecodeMp4Audio(path);
                var frames = pcm.Length / Math.Max(1, channels);
                var hz = DominantHz(pcm, channels, outRate);
                var seconds = frames / (double)outRate;
                Check($"video audio pitch: {rate} Hz source plays as 440 Hz for its full length",
                    nativeRate == rate && Math.Abs(hz - 440) < 3 && Math.Abs(seconds - n / (double)rate) < 0.3,
                    $"native {nativeRate} Hz, decoded {outRate} Hz x {channels}: {hz:F1} Hz, {seconds:F2} s");
            }
            finally { try { File.Delete(path); } catch (IOException) { } }
        }
    }
}
