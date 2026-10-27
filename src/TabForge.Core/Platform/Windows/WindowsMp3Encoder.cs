using TabForge.Platform.Contracts;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace TabForge.Platform.Windows;

// Owns: Media Foundation start-up (once per process) and the MP3 encoder calls behind IMp3Encoder.
/// <summary>The Windows adapter of <see cref="IMp3Encoder"/>: the Media Foundation MP3 encoder.</summary>
public sealed class WindowsMp3Encoder : IMp3Encoder
{
    public bool IsAvailable()
    {
        try
        {
            WindowsMediaFoundation.EnsureStarted();
            return MediaFoundationEncoder.GetEncodeBitrates(AudioSubtypes.MFAudioFormat_MP3, 44100, 2).Length > 0;
        }
        catch (Exception) { return false; } // Not logged: job probe: false is reported by the caller
    }

    public void Encode(IWaveProvider pcm16, string path, int bitsPerSecond)
    {
        WindowsMediaFoundation.EnsureStarted();
        MediaFoundationEncoder.EncodeToMp3(pcm16, path, bitsPerSecond);
    }
}

/// <summary>Media Foundation starts once per process (on first use) and is left running until the process exits: a Startup per use had no matching Shutdown.</summary>
internal static class WindowsMediaFoundation
{
    private static readonly Lazy<bool> Started = new(() => { MediaFoundationApi.Startup(); return true; });

    public static void EnsureStarted() => _ = Started.Value;
}

/// <summary>The Windows adapter of <see cref="ISystemAudioReader"/>: the Media Foundation reader.</summary>
public sealed class WindowsSystemAudioReader : ISystemAudioReader
{
    public bool IsAvailable => true;

    public SystemAudioReading Open(string file)
    {
        WindowsMediaFoundation.EnsureStarted();
        var reader = new MediaFoundationReader(file);
        return new SystemAudioReading(reader, reader.ToSampleProvider(), reader.TotalTime);
    }
}
