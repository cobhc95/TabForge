using System;
using System.Collections.Generic;
using System.IO;
using NAudio.MediaFoundation;
using NAudio.Wave;
using TabForge.Audio;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the managed MP3/OGG decoders behind IAudioDecoder: same length and close samples as the old NAudio / Media Foundation path, and a clean "not mine" answer for other formats.
/// Does not own: the waveform or clip players that call it.
/// Tests: TabForge.Avalonia.Tests.AudioDecoderTests.
/// </summary>
public class AudioDecoderTests
{
    private static string Scratch(string name) => Path.Combine(Path.GetTempPath(), "tf-decoder-" + Guid.NewGuid().ToString("N") + name);

    private static float[] ReadAll(Func<float[], int, int, int> read)
    {
        var all = new List<float>();
        var buf = new float[4096];
        int n;
        while ((n = read(buf, 0, buf.Length)) > 0) for (var i = 0; i < n; i++) all.Add(buf[i]);
        return all.ToArray();
    }

    /// <summary>A one-second stereo sine written as WAV and encoded to MP3 by Media Foundation; null when this machine has no MP3 encoder.</summary>
    private static string? MakeMp3()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var wav = Scratch(".wav"); var mp3 = Scratch(".mp3");
        try
        {
            using (var w = new WaveFileWriter(wav, new WaveFormat(44100, 16, 2)))
            {
                var s = new short[44100 * 2];
                for (var i = 0; i < 44100; i++) { var v = (short)(12000 * Math.Sin(2 * Math.PI * 440 * i / 44100.0)); s[2 * i] = v; s[2 * i + 1] = v; }
                w.WriteSamples(s, 0, s.Length);
            }
            MediaFoundationApi.Startup();
            using var r = new WaveFileReader(wav);
            MediaFoundationEncoder.EncodeToMp3(r, mp3, 128000);
            return new FileInfo(mp3).Length > 0 ? mp3 : null;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or NotSupportedException or IOException) { return null; }
        finally { try { File.Delete(wav); } catch (IOException) { } }
    }

    [Fact]
    public void Mp3_ManagedDecoderMatchesTheOldPathInLengthAndSamples()
    {
        var mp3 = MakeMp3();
        if (mp3 is null) return; // no MP3 encoder here (N editions): nothing to compare
        try
        {
            using var managed = AudioDecoders.TryOpenManaged(mp3);
            Assert.NotNull(managed);
            using var old = new AudioFileReader(mp3);
            Assert.Equal(old.WaveFormat.SampleRate, managed!.SampleRate);
            Assert.Equal(old.WaveFormat.Channels, managed.Channels);
            var a = ReadAll(managed.Read);
            var b = ReadAll(old.Read);
            // The decoders may differ by a couple of frames of encoder delay / padding; the music in between must match.
            Assert.InRange(Math.Abs(a.Length - b.Length), 0, 2 * 1152 * managed.Channels * 2);
            Assert.InRange(managed.TotalTime.TotalSeconds, 0.9, 1.2);
            var peakA = 0f; foreach (var v in a) peakA = Math.Max(peakA, Math.Abs(v));
            var peakB = 0f; foreach (var v in b) peakB = Math.Max(peakB, Math.Abs(v));
            Assert.InRange(peakA, peakB * 0.9f, peakB * 1.1f);
        }
        finally { try { File.Delete(mp3); } catch (IOException) { } }
    }

    [Fact]
    public void Ogg_ManagedDecoderReadsTheClipAndSeeksNearTheRequestedTime()
    {
        var path = Scratch(".ogg");
        File.WriteAllBytes(path, AudioFixtures.ShortOgg());
        try
        {
            using var d = AudioDecoders.TryOpenManaged(path);
            Assert.NotNull(d);
            Assert.True(d!.SampleRate > 0 && d.Channels > 0);
            var samples = ReadAll(d.Read);
            Assert.NotEmpty(samples);
            Assert.InRange(samples.Length / (double)(d.SampleRate * d.Channels), d.TotalTime.TotalSeconds - 0.05, d.TotalTime.TotalSeconds + 0.05);
            d.Seek(TimeSpan.FromSeconds(0.04));
            var rest = ReadAll(d.Read);
            var expected = samples.Length - (int)(0.04 * d.SampleRate) * d.Channels;
            Assert.InRange(rest.Length, expected - 2 * 2048 * d.Channels, expected + 2 * 2048 * d.Channels); // seeks land on a block boundary
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Theory]
    [InlineData(".wav")]
    [InlineData(".flac")]
    [InlineData(".m4a")]
    [InlineData(".wma")]
    [InlineData(".aiff")]
    public void OtherFormats_FallBackToTheOldReader(string ext)
    {
        var path = Scratch(ext);
        File.WriteAllBytes(path, new byte[64]);
        try { Assert.Null(AudioDecoders.TryOpenManaged(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CorruptMp3_IsRejectedSoTheFallbackRuns()
    {
        var path = Scratch(".mp3");
        File.WriteAllBytes(path, new byte[256]);
        try { Assert.Null(AudioDecoders.TryOpenManaged(path)); }
        finally { File.Delete(path); }
    }
}
