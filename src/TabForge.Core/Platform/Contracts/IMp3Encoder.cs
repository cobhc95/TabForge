using NAudio.Wave;

namespace TabForge.Platform.Contracts;

// Owns: the port for the MP3 encoder (Media Foundation on Windows, LAME elsewhere) (render export) and the system's fallback reader for compressed audio the managed decoders reject.
// Does not own: the Windows Media Foundation calls (Platform/Windows/WindowsMp3Encoder.cs, WindowsSystemAudioReader.cs) or which one is chosen (Platform/SystemAudio.cs).
// Tests: PortabilityGuardTests; the render and waveform tests use the Windows adapters unchanged.
/// <summary>Encodes 16-bit PCM to an MP3 file with the operating system's encoder.</summary>
public interface IMp3Encoder
{
    /// <summary>Whether the system has an MP3 encoder (Windows N editions and some LTSC installs do not).</summary>
    bool IsAvailable();

    /// <summary>Writes <paramref name="pcm16"/> to <paramref name="path"/> at <paramref name="bitsPerSecond"/>.</summary>
    void Encode(IWaveProvider pcm16, string path, int bitsPerSecond);
}

/// <summary>An opened file from <see cref="ISystemAudioReader"/>: dispose <see cref="Reader"/> when done.</summary>
public sealed record SystemAudioReading(IDisposable Reader, ISampleProvider Samples, TimeSpan Total);

/// <summary>The system's own decoder, used only after the managed decoders and NAudio's readers failed (MP3/AAC/M4A the others reject).</summary>
public interface ISystemAudioReader
{
    /// <summary>False where the system offers no decoder to ask.</summary>
    bool IsAvailable { get; }

    /// <summary>Opens <paramref name="file"/>; throws when the system cannot decode it.</summary>
    SystemAudioReading Open(string file);
}

