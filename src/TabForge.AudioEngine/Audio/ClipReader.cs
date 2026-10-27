using NAudio.Wave;
using TabForge.Audio;

namespace TabForge.AudioEngine.Audio;

// Owns: opening one clip file as float samples: the managed MP3/OGG decoder first, NAudio's file reader (Media Foundation underneath on Windows) for everything else or when the managed one rejects the file.
// Does not own: resampling, channel mixing or the stretch (ClipPlayer).
// Tests: AudioDecoderTests (the decoder seam).
internal sealed class ClipReader : ISampleProvider, IDisposable
{
    private readonly IAudioDecoder? _decoder;
    private readonly AudioFileReader? _file;

    public ClipReader(string path)
    {
        _decoder = AudioDecoders.TryOpenManaged(path);
        if (_decoder is null) _file = new AudioFileReader(path);
        WaveFormat = _decoder is { } d ? WaveFormat.CreateIeeeFloatWaveFormat(d.SampleRate, d.Channels) : _file!.WaveFormat;
    }

    public WaveFormat WaveFormat { get; }
    public TimeSpan TotalTime => _decoder?.TotalTime ?? _file!.TotalTime;
    public TimeSpan CurrentTime
    {
        get => _decoder?.Time ?? _file!.CurrentTime;
        set
        {
            if (_decoder is null) _file!.CurrentTime = value;
            else if (value > TimeSpan.Zero) _decoder.Seek(value); // a fresh decoder already starts at 0 (a managed seek to 0 can drop the first block)
        }
    }

    public int Read(float[] buffer, int offset, int count) => _decoder is not null ? _decoder.Read(buffer, offset, count) : _file!.Read(buffer, offset, count);
    public void Dispose() { _decoder?.Dispose(); _file?.Dispose(); }
}
