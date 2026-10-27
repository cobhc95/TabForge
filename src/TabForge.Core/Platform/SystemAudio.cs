using NAudio.Wave;
using TabForge.Platform.Contracts;

namespace TabForge.Platform;

// Owns: choosing, once, the system audio services of this OS (MP3 encoder: Media Foundation on Windows, LAME elsewhere; compressed-audio fallback reader) and the non-Windows stub.
// Does not own: the ports (Platform/Contracts/IMp3Encoder.cs) or the Windows Media Foundation calls (Platform/Windows/WindowsMp3Encoder.cs, WindowsSystemAudioReader.cs).
// Tests: PortabilityGuardTests; the render and waveform tests use the Windows adapters unchanged.

/// <summary>The non-Windows stub: no system MP3 encoder and no system decoder; export of MP3 is switched off, decoding keeps the managed path.</summary>
public sealed class NullSystemAudio : IMp3Encoder, ISystemAudioReader
{
    public bool IsAvailable() => false;
    bool ISystemAudioReader.IsAvailable => false;
    public void Encode(IWaveProvider pcm16, string path, int bitsPerSecond) => throw new PlatformNotSupportedException("No system MP3 encoder on this platform.");
    public SystemAudioReading Open(string file) => throw new PlatformNotSupportedException("No system audio decoder on this platform.");
}

/// <summary>The system audio services of this OS, chosen once at start-up.</summary>
public static class SystemAudio
{
    /// <summary>The MP3 encoder: Media Foundation on Windows, LAME elsewhere (and on Windows only with the dev switch <see cref="Portable.LameMp3Encoder.DevVariable"/>=lame).</summary>
    public static IMp3Encoder Mp3 { get; } = CreateMp3(OperatingSystem.IsWindows(), string.Equals(Environment.GetEnvironmentVariable(Portable.LameMp3Encoder.DevVariable), "lame", StringComparison.OrdinalIgnoreCase));

    /// <summary>The MP3 encoder for a host: the Windows one on a Windows host unless <paramref name="lameRequested"/>; LAME otherwise.</summary>
    public static IMp3Encoder CreateMp3(bool windowsHost, bool lameRequested)
    {
#if WINDOWS
        if (windowsHost && !lameRequested) return new Windows.WindowsMp3Encoder();
#endif
        return new Portable.LameMp3Encoder();
    }

#if WINDOWS
    public static ISystemAudioReader Reader { get; } = new Windows.WindowsSystemAudioReader();
#else
    public static ISystemAudioReader Reader { get; } = new NullSystemAudio();
#endif
}
