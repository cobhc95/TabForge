using System.Buffers;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TabForge.Audio.Contracts;

namespace TabForge.AudioEngine.Mixing;

// Owns: one WAV file of an offline render (downmix, resample, bit-depth convert, peak tracking, auto tail).

/// <summary>
/// One WAV file of an offline render (master or a stem): takes non-interleaved engine-rate blocks, downmixes to the
/// wanted channel count, resamples (WDL) when the file rate differs from the engine rate, converts to 16-bit (TPDF
/// dither, fixed seed so renders are bit-identical) / 24-bit / 32-bit float, and tracks peak and clipped samples.
/// Blocks of a silent tail can be held back and dropped (auto tail) or flushed.
/// </summary>
internal sealed class RenderWavSink : IDisposable
{
    private readonly int _srcRate, _dstRate, _channels;
    private readonly RenderFormat _format;
    private WaveFileWriter? _writer;
    private readonly float[] _inter;
    private byte[] _bytes = new byte[8192 * 8];
    private readonly PushSource? _push;
    private readonly WdlResamplingSampleProvider? _resampler;
    private readonly float[] _drain = new float[4096];
    private readonly List<(float[] L, float[] R, int N)> _held = new();
    private uint _rng = 0x9E3779B9;
    private long _outFrames;
    private long _cap = long.MaxValue;

    public string Path { get; }
    public int Slot { get; }
    public float Peak { get; private set; }
    public long Clipped { get; private set; }

    public RenderWavSink(string path, int slot, int engineRate, int fileRate, int channels, RenderFormat format, int maxBlock)
    {
        Path = path; Slot = slot;
        _srcRate = engineRate; _dstRate = fileRate <= 0 ? engineRate : fileRate;
        _channels = channels; _format = format;
        _inter = new float[maxBlock * channels];
        var wave = format switch
        {
            RenderFormat.Pcm16 => new WaveFormat(_dstRate, 16, channels),
            RenderFormat.Pcm24 => new WaveFormat(_dstRate, 24, channels),
            _ => WaveFormat.CreateIeeeFloatWaveFormat(_dstRate, channels),
        };
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        _writer = new WaveFileWriter(path, wave);
        if (_dstRate != _srcRate)
        {
            _push = new PushSource(WaveFormat.CreateIeeeFloatWaveFormat(_srcRate, channels));
            _resampler = new WdlResamplingSampleProvider(_push, _dstRate);
        }
    }

    /// <summary>Writes a block now.</summary>
    public void Write(float[] left, float[] right, int frames)
    {
        WrittenFrames += frames;
        var channels = _channels;
        if (channels == 2) for (var i = 0; i < frames; i++) { _inter[2 * i] = left[i]; _inter[2 * i + 1] = right[i]; }
        else for (var i = 0; i < frames; i++) _inter[i] = 0.5f * (left[i] + right[i]);
        if (_resampler is null) { Emit(_inter, frames); return; }
        _push!.Add(_inter, frames * channels);
        Drain();
    }

    /// <summary>Keeps a (silent) block back until <see cref="FlushHeld"/> or <see cref="DropHeld"/> (pooled: rented from the shared array pool, returned on flush/drop).</summary>
    public void Hold(float[] left, float[] right, int frames)
    {
        var l = ArrayPool<float>.Shared.Rent(frames);
        var r = ArrayPool<float>.Shared.Rent(frames);
        left.AsSpan(0, frames).CopyTo(l);
        right.AsSpan(0, frames).CopyTo(r);
        _held.Add((l, r, frames));
    }

    private void ReleaseHeld()
    {
        foreach (var (l, r, _) in _held) { ArrayPool<float>.Shared.Return(l); ArrayPool<float>.Shared.Return(r); }
        _held.Clear();
    }

    public void FlushHeld()
    {
        foreach (var (l, r, n) in _held) Write(l, r, n);
        ReleaseHeld();
    }

    public void DropHeld() => ReleaseHeld();

    /// <summary>Writes only the first <paramref name="frames"/> held frames and drops the rest (a delayed master that must end where the stems end).</summary>
    public void FlushHeldFirst(long frames)
    {
        foreach (var (l, r, n) in _held)
        {
            if (frames <= 0) break;
            var k = (int)Math.Min(n, frames);
            Write(l, r, k);
            frames -= k;
        }
        ReleaseHeld();
    }

    /// <summary>Engine-rate frames written so far (held frames not counted).</summary>
    public long WrittenFrames { get; private set; }

    private void Drain()
    {
        for (var guard = 0; guard < 256; guard++)
        {
            var got = _resampler!.Read(_drain, 0, _drain.Length - _drain.Length % _channels);
            if (got <= 0) break;
            Emit(_drain, got / _channels);
        }
    }

    private void Emit(float[] data, int frames)
    {
        if (_outFrames + frames > _cap) frames = (int)Math.Max(0, _cap - _outFrames);
        if (frames <= 0 || _writer is null) return;
        var count = frames * _channels;
        var bytesPerSample = _format switch { RenderFormat.Pcm16 => 2, RenderFormat.Pcm24 => 3, _ => 4 };
        if (_bytes.Length < count * bytesPerSample) _bytes = new byte[count * bytesPerSample];
        var peak = Peak; var clipped = Clipped;
        var b = _bytes;
        switch (_format)
        {
            case RenderFormat.Pcm16:
                for (var i = 0; i < count; i++)
                {
                    var x = data[i]; var a = MathF.Abs(x);
                    if (a > peak) peak = a;
                    if (a > 1f) clipped++;
                    var v = (int)MathF.Floor(x * 32767f + (Random01() - Random01()) + 0.5f);   // TPDF dither, +-1 LSB
                    v = Math.Clamp(v, -32768, 32767);
                    b[2 * i] = (byte)v; b[2 * i + 1] = (byte)(v >> 8);
                }
                break;
            case RenderFormat.Pcm24:
                for (var i = 0; i < count; i++)
                {
                    var x = data[i]; var a = MathF.Abs(x);
                    if (a > peak) peak = a;
                    if (a > 1f) clipped++;
                    var v = Math.Clamp((int)MathF.Round(x * 8388607f), -8388608, 8388607);
                    b[3 * i] = (byte)v; b[3 * i + 1] = (byte)(v >> 8); b[3 * i + 2] = (byte)(v >> 16);
                }
                break;
            default:
                for (var i = 0; i < count; i++)
                {
                    var a = MathF.Abs(data[i]);
                    if (a > peak) peak = a;
                    if (a > 1f) clipped++;
                }
                Buffer.BlockCopy(data, 0, b, 0, count * 4);
                break;
        }
        Peak = peak; Clipped = clipped;
        _writer.Write(b, 0, count * bytesPerSample);
        _outFrames += frames;
    }

    private float Random01()
    {
        _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
        return (_rng >> 8) * (1f / 16777216f);
    }

    /// <summary>Ends the file at exactly <paramref name="engineFrames"/> engine-rate frames (resampled: the matching count) and closes it.</summary>
    public RenderFileResult Finish(long engineFrames)
    {
        if (_resampler is not null)
        {
            _cap = (long)Math.Round(engineFrames * (double)_dstRate / _srcRate);
            var silence = new float[2048 * _channels];
            for (var i = 0; i < 16 && _outFrames < _cap; i++) { _push!.Add(silence, silence.Length); Drain(); }
            var pad = new float[_channels * 256];
            while (_outFrames < _cap) Emit(pad, (int)Math.Min(256, _cap - _outFrames));
        }
        _writer?.Dispose();
        _writer = null;
        return new RenderFileResult(Path, Slot, Peak, Clipped);
    }

    /// <summary>Closes and deletes the unfinished file.</summary>
    public void Abort()
    {
        try { _writer?.Dispose(); } catch (Exception) { } // swallow: best-effort dispose during teardown
        _writer = null;
        try { File.Delete(Path); } catch (Exception) { } // swallow: best-effort delete of a failed render
    }

    public void Dispose() { try { _writer?.Dispose(); } catch (Exception) { } // swallow: best-effort dispose during teardown
 _writer = null; }

    /// <summary>A queue the resampler pulls from: the render pushes engine-rate blocks in (ring: no O(n) shift on read).</summary>
    /// <remarks>
    /// Chunk contract shared with <see cref="MasterTapLimits.MaxChunkFrames"/> (see AudioEngineClient.ReadMasterAudio):
    /// one render block (≤ 512 frames) or resampler drain chunk at a time; the sink never holds an unbounded tail.
    /// Follow-up outside Audio scope (do not edit here): Services/Video/VideoExportFlow.cs:135
    /// (<c>r.FrameTap?.Invoke(i, pixels.ToArray())</c>) copies every video frame even when no tap is attached.
    /// Gate it the same pooled opt-in way: capture the pixels span once, invoke the tap only when attached, and offer
    /// a pooled/rented overload so the per-frame ToArray copy disappears.
    /// </remarks>
    private sealed class PushSource(WaveFormat format) : ISampleProvider
    {
        private float[] _data = new float[16384];
        private int _head, _count;
        public WaveFormat WaveFormat => format;

        public void Add(float[] samples, int count)
        {
            if (_count + count > _data.Length)
            {
                var bigger = new float[Math.Max(_data.Length * 2, _count + count)];
                var first = Math.Min(_count, _data.Length - _head);
                Array.Copy(_data, _head, bigger, 0, first);
                if (_count > first) Array.Copy(_data, 0, bigger, first, _count - first);
                _data = bigger; _head = 0;
            }
            var tail = (_head + _count) % _data.Length;
            var firstAdd = Math.Min(count, _data.Length - tail);
            Array.Copy(samples, 0, _data, tail, firstAdd);
            if (firstAdd < count) Array.Copy(samples, firstAdd, _data, 0, count - firstAdd);
            _count += count;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var n = Math.Min(count, _count);
            var first = Math.Min(n, _data.Length - _head);
            Array.Copy(_data, _head, buffer, offset, first);
            if (first < n) Array.Copy(_data, 0, buffer, offset + first, n - first);
            _head = (_head + n) % _data.Length;
            _count -= n;
            return n;
        }
    }
}
