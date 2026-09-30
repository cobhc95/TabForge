using System.Collections.Concurrent;
using System.IO;
using NAudio.Wave;

namespace TabForge.Audio;

/// <summary>
/// Waveform outlines for drawing audio clips: the peak level of every 10 ms of a file, read once in the background
/// (a 5-minute file is ~30,000 numbers) and kept for the session. Drawing never waits: until a file is read its
/// clip shows a plain block, then the timeline redraws.
/// </summary>
public static class WaveformCache
{
    public const double SecondsPerPeak = 0.01;
    private static readonly ConcurrentDictionary<string, float[]> Peaks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, bool> Pending = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A file's outline finished reading (raised on a background thread).</summary>
    public static event Action<string>? Ready;

    public static float[]? Get(string file)
    {
        if (Peaks.TryGetValue(file, out var peaks)) return peaks;
        if (Pending.TryAdd(file, true)) _ = Task.Run(() => Read(file));
        return null;
    }

    private static void Read(string file)
    {
        try
        {
            using var reader = new AudioFileReader(file);
            var channels = Math.Max(1, reader.WaveFormat.Channels);
            var perPeak = Math.Max(1, (int)(reader.WaveFormat.SampleRate * SecondsPerPeak)) * channels;
            var buffer = new float[perPeak * 64];
            var peaks = new List<float>();
            int read;
            var current = 0f; var inPeak = 0;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                for (var i = 0; i < read; i++)
                {
                    current = Math.Max(current, Math.Abs(buffer[i]));
                    if (++inPeak >= perPeak) { peaks.Add(current); current = 0; inPeak = 0; }
                }
            if (inPeak > 0) peaks.Add(current);
            Peaks[file] = peaks.ToArray();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException or FormatException)
        {
            Peaks[file] = Array.Empty<float>();
        }
        Ready?.Invoke(file);
    }

    /// <summary>Length of an audio file in seconds (0 when it cannot be read).</summary>
    public static double LengthOf(string file)
    {
        try { using var reader = new AudioFileReader(file); return reader.TotalTime.TotalSeconds; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException or FormatException) { return 0; }
    }

    public static readonly string[] Extensions = { ".wav", ".mp3", ".aif", ".aiff", ".flac", ".ogg", ".m4a", ".wma" };
}
