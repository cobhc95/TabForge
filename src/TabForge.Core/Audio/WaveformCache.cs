using System.Diagnostics;
using System.IO;
using NAudio.Wave;
using TabForge.Services;

namespace TabForge.Audio;

// Owns: waveform peak outlines (background decode, memory budget, MediaAccess gating).
// Does not own: the decoder (NAudioMediaSource) or the clip draw path.
// Tests: AudioSmokeTests.

/// <summary>An opened audio file, as the waveform reader sees it (tests replace the real decoder).</summary>
internal interface IMediaSource : IDisposable
{
    int SampleRate { get; }
    int Channels { get; }
    double TotalSeconds { get; }
    int Read(float[] buffer, int offset, int count);
}

internal sealed class NAudioMediaSource : IMediaSource
{
    private readonly IDisposable _reader;
    private readonly ISampleProvider? _samples;
    private readonly IAudioDecoder? _decoder;
    private readonly TimeSpan _total;

    /// <summary>Opens <paramref name="file"/> with NAudio's file reader; when that cannot decode it (some MP3/M4A files), the system reader (Windows: Media Foundation) is tried.</summary>
    public NAudioMediaSource(string file)
    {
        _decoder = AudioDecoders.TryOpenManaged(file); // MP3/OGG decode the same way on every OS; anything it rejects takes the readers below
        if (_decoder is not null) { _reader = _decoder; _total = _decoder.TotalTime; return; }
        try
        {
            var reader = new AudioFileReader(file);
            _reader = reader; _samples = reader; _total = reader.TotalTime;
        }
        catch (Exception first) when (first is not OutOfMemoryException && TabForge.Platform.SystemAudio.Reader.IsAvailable)
        {
            try
            {
                var reading = TabForge.Platform.SystemAudio.Reader.Open(file);
                _reader = reading.Reader; _samples = reading.Samples; _total = reading.Total;
            }
            catch (Exception) { throw first is IOException or InvalidDataException or UnauthorizedAccessException ? first : new InvalidDataException("could not be decoded", first); } // the Media Foundation attempt failed too: the first error stands
        }
    }
    public int SampleRate => _decoder?.SampleRate ?? _samples!.WaveFormat.SampleRate;
    public int Channels => _decoder?.Channels ?? _samples!.WaveFormat.Channels;
    public double TotalSeconds => _total.TotalSeconds;
    public int Read(float[] buffer, int offset, int count) => _decoder is not null ? _decoder.Read(buffer, offset, count) : _samples!.Read(buffer, offset, count);
    public void Dispose() => _reader.Dispose();
}

public enum WaveState { Loading, Ready, NeedsApproval, Failed }

/// <summary>What a clip shows while (or instead of) its outline: nothing yet, the outline, an approval request or an error.</summary>
public readonly record struct WaveStatus(WaveState State, string? Message)
{
    /// <summary>The file is not there (as opposed to unreadable): the clip is drawn hatched and the Find missing media command can relink it.</summary>
    public bool IsMissing => State == WaveState.Failed && Message == WaveformService.MissingMessage;
}


/// <summary>The application's one <see cref="WaveformService"/>, created at the composition root and handed to the views that draw waveforms; code that can be given the service takes it as a parameter instead of reading this.</summary>
public static class WaveformCache
{
    public static WaveformService Default { get; } = new();
    public static readonly string[] Extensions = { ".wav", ".mp3", ".aif", ".aiff", ".flac", ".ogg", ".m4a", ".wma" };
}
