using NLayer;
using NVorbis;

namespace TabForge.Audio;

// Owns: the portable compressed-audio decode seam (IAudioDecoder) and the managed decoders behind it: NLayer for MP3, NVorbis for OGG.
// Does not own: the fallback for everything else (WAV/AIFF/FLAC/AAC/M4A/WMA stay on NAudio and, on Windows, Media Foundation: callers try this first, then their old reader).
// Tests: AudioDecoderTests.

/// <summary>A decoded audio file read as interleaved 32-bit float samples.</summary>
public interface IAudioDecoder : IDisposable
{
    int SampleRate { get; }
    int Channels { get; }
    TimeSpan TotalTime { get; }
    /// <summary>Where the next <see cref="Read"/> starts.</summary>
    TimeSpan Time { get; }
    void Seek(TimeSpan time);
    /// <summary>Reads up to <paramref name="count"/> samples (all channels); 0 means the end.</summary>
    int Read(float[] buffer, int offset, int count);
}

public static class AudioDecoders
{
    /// <summary>True for the extensions the managed decoders handle (MP3, OGG).</summary>
    public static bool IsManagedFormat(string file)
    {
        var ext = Path.GetExtension(file);
        return ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Opens <paramref name="file"/> with a managed decoder, or returns null when its format is not one of them or the decoder rejects it (the caller then uses its fallback reader).</summary>
    public static IAudioDecoder? TryOpenManaged(string file)
    {
        if (!IsManagedFormat(file)) return null;
        FileStream? stream = null;
        try
        {
            stream = File.OpenRead(file);
            return Path.GetExtension(file).Equals(".ogg", StringComparison.OrdinalIgnoreCase) ? new OggDecoder(stream) : new Mp3Decoder(stream);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            stream?.Dispose();
            return null; // not decodable by the managed reader (corrupt, wrong container, locked): the fallback reader reports or plays it
        }
    }

    private sealed class Mp3Decoder : IAudioDecoder
    {
        private readonly Stream _stream;
        private readonly MpegFile _file;
        public Mp3Decoder(Stream stream) { _stream = stream; _file = new MpegFile(stream); }
        public int SampleRate => _file.SampleRate;
        public int Channels => _file.Channels;
        public TimeSpan TotalTime => _file.Duration;
        public TimeSpan Time => _file.Time;
        public void Seek(TimeSpan time) => _file.Time = time;
        public int Read(float[] buffer, int offset, int count) => _file.ReadSamples(buffer, offset, count);
        public void Dispose() { _file.Dispose(); _stream.Dispose(); }
    }

    private sealed class OggDecoder : IAudioDecoder
    {
        private readonly VorbisReader _reader;
        public OggDecoder(Stream stream) { _reader = new VorbisReader(stream, true); }
        public int SampleRate => _reader.SampleRate;
        public int Channels => _reader.Channels;
        public TimeSpan TotalTime => _reader.TotalTime;
        public TimeSpan Time => _reader.TimePosition;
        public void Seek(TimeSpan time) => _reader.TimePosition = time;
        public int Read(float[] buffer, int offset, int count) => _reader.ReadSamples(buffer, offset, count);
        public void Dispose() => _reader.Dispose();
    }
}
