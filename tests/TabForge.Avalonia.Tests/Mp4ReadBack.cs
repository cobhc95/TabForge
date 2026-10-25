using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TabForge.Services.Video;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: reading a test MP4 back (Media Foundation source reader: video sample count and times, decoded left channel) and fitting
/// decoded sound to its source (best lag and gain, signal over error in dB, as SelfTestLiveAudioFidelity fits the chord). Used by the video tests.</summary>
internal static class Mp4ReadBack
{
    private const int MaxLagFrames = 4096;   // the AAC encoder's priming is shorter than this
    private const double PcmScale = 32768.0;

    public sealed record Track(int VideoFrames, double VideoEnd, double AudioEnd, float[] Left);

    /// <summary>The MP4's frames (the end is the last frame's time plus one frame at <paramref name="fps"/>) and its sound.</summary>
    public static Track Read(string path, int fps)
    {
        Mf.MFStartup(Mf.Version, 0);
        try
        {
            Mf.MFCreateSourceReaderFromURL(path, null, out var reader);
            var frames = 0; long lastVideo = 0;
            while (true)
            {
                reader.ReadSample(Mf.FirstVideoStream, 0, out _, out var flags, out var time, out var sample);
                if (sample is not null) { frames++; lastVideo = Math.Max(lastVideo, time); Marshal.ReleaseComObject(sample); }
                if ((flags & Mf.EndOfStream) != 0) break;
            }
            Mf.MFCreateMediaType(out var want);
            want.SetGUID(Mf.MajorType, Mf.Audio); want.SetGUID(Mf.Subtype, Mf.Pcm);
            want.SetUINT32(Mf.AudioBits, 16);
            reader.SetCurrentMediaType(Mf.FirstAudioStream, IntPtr.Zero, want);
            reader.GetCurrentMediaType(Mf.FirstAudioStream, out var got);
            got.GetUINT32(Mf.AudioRate, out var rate); got.GetUINT32(Mf.AudioChannels, out var channels);
            var left = new List<float>(); double audioEnd = 0;
            while (true)
            {
                reader.ReadSample(Mf.FirstAudioStream, 0, out _, out var flags, out var time, out var sample);
                if (sample is not null)
                {
                    sample.ConvertToContiguousBuffer(out var buffer);
                    buffer.Lock(out var ptr, out _, out var length);
                    var pcm = new short[length / 2]; Marshal.Copy(ptr, pcm, 0, pcm.Length);
                    buffer.Unlock(); Marshal.ReleaseComObject(buffer); Marshal.ReleaseComObject(sample);
                    for (var i = 0; i < pcm.Length; i += channels) left.Add((float)(pcm[i] / PcmScale));
                    audioEnd = Math.Max(audioEnd, time / 1e7 + pcm.Length / channels / (double)rate);
                }
                if ((flags & Mf.EndOfStream) != 0) break;
            }
            Marshal.ReleaseComObject(reader);
            return new Track(frames, lastVideo / 1e7 + 1.0 / fps, audioEnd, left.ToArray());
        }
        finally { Mf.MFShutdown(); }
    }

    /// <summary>The decoded sound against its source: best lag and gain over half a second from the middle of the source, in dB.</summary>
    public static double FitSnr(float[] source, float[] decoded, int rate)
    {
        var fitFrames = rate / 2;
        var from = Math.Max(0, source.Length / 2 - fitFrames / 2);
        var best = double.NegativeInfinity;
        for (var lag = 0; lag <= MaxLagFrames && from + lag + fitFrames <= decoded.Length && from + fitFrames <= source.Length; lag++)
        {
            double dot = 0, rr = 0, dd = 0;
            for (var i = 0; i < fitFrames; i++) { var r = source[from + i]; var d = decoded[from + lag + i]; dot += r * d; rr += r * r; dd += d * d; }
            if (rr <= 0) break;
            var gain = dot / rr; var err = dd - 2 * gain * dot + gain * gain * rr;   // |d - g r|^2
            best = Math.Max(best, 10 * Math.Log10(gain * gain * rr / Math.Max(err, 1e-30)));
        }
        return best;
    }
}
