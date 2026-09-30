using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace TabForge.AudioEngine.Audio;

/// <summary>
/// The audio input (WASAPI, the chosen recording device): delivers inputs 1 and 2 to the audio thread through a
/// lock-free ring (for monitoring through the track's chain) and hands every captured block to the recorder.
/// Opened only while some track is armed; closed again when none is.
/// </summary>
public sealed class InputCapture : IDisposable
{
    private const int RingFrames = 1 << 15; // ~0.68 s at 48 kHz
    private readonly WasapiCapture? _capture;
    private readonly float[] _ring = new float[RingFrames * 2];
    private long _write, _read;
    private readonly int _engineRate;
    private readonly int _channels;
    private readonly float[] _converted = new float[8192 * 2];
    /// <summary>
    /// RT-09: band-limited windowed-sinc resampler to the engine rate when the device cannot capture at it; null when the rates
    /// match. Preallocated, so the capture callback does not allocate.
    /// </summary>
    private readonly SincResampler? _resampler;
    private readonly int _maxChunkIn;
    private const int WasapiBufferMs = 10;

    /// <summary>The rate the input really arrives at (the device's own, or the engine's when it captures at it).</summary>
    public int SourceRate { get; }
    /// <summary>How <see cref="LatencyMs"/> was found (log / diagnostics).</summary>
    public string LatencySource { get; } = "fixed";

    private static SincResampler? CreateResampler(int sourceRate, int engineRate, int capacityFrames, out int maxChunkIn)
    {
        maxChunkIn = capacityFrames;
        if (sourceRate <= 0 || sourceRate == engineRate) return null;
        // Output never exceeds the conversion buffer: input chunks are sized for the rate ratio (one chunk yields at most n / step + 1 frames).
        maxChunkIn = Math.Max(16, (int)((capacityFrames - 4) * (double)sourceRate / engineRate));
        return new SincResampler(sourceRate, engineRate, maxChunkIn);   // windowed sinc: no audible aliasing (linear interpolation aliased)
    }

    /// <summary>Capture thread: every block of input (interleaved stereo at the engine rate), for recording. Must not block
    /// (for ASIO this is the driver's real-time thread): the recorder only queues it.</summary>
    public Action<float[], int>? BlockCaptured;
    public string DeviceName { get; }
    public int LatencyMs { get; }

    /// <summary>Feed mode (ASIO): the driver delivers the input; call <see cref="Feed"/> from its callback.</summary>
    /// <param name="sourceRate">Rate of the fed samples (0: the engine rate, as ASIO delivers).</param>
    public InputCapture(string name, int engineRate, int channels, int latencyMs, int sourceRate = 0)
    {
        DeviceName = name;
        LatencyMs = latencyMs;
        LatencySource = "driver";
        _channels = Math.Max(1, channels);
        _engineRate = engineRate;
        SourceRate = sourceRate > 0 ? sourceRate : engineRate;
        _resampler = CreateResampler(SourceRate, engineRate, _converted.Length / 2, out _maxChunkIn);
        if (_resampler is not null) _staging = new float[_maxChunkIn * 2];
    }

    /// <summary>ASIO callback thread: interleaved input samples at the engine rate.</summary>
    public void Feed(ReadOnlySpan<float> samples) => OnSamples(samples);

    public InputCapture(string deviceName, int engineRate)
    {
        using var enumerator = new MMDeviceEnumerator();
        MMDevice? device = null;
        if (!string.IsNullOrWhiteSpace(deviceName))
            foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                if (device is null && string.Equals(d.FriendlyName, deviceName, StringComparison.OrdinalIgnoreCase)) device = d;
                else d.Dispose();
            }
        device ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
        DeviceName = device.FriendlyName;
        LatencyMs = WasapiBufferMs;
        WasapiCapture? capture = null;
        try
        {
            capture = new WasapiCapture(device, true, WasapiBufferMs);
            var format = capture.WaveFormat;
            // RT-09: capture at the engine rate when the device takes it (no resampling at all); otherwise float at its own rate.
            var atEngineRate = WaveFormat.CreateIeeeFloatWaveFormat(engineRate, format.Channels);
            var (supported, streamLatencyMs) = ProbeCapture(device, atEngineRate);
            if (supported) capture.WaveFormat = atEngineRate;
            else
            {
                if (format.Encoding != WaveFormatEncoding.IeeeFloat && !(format.Encoding == WaveFormatEncoding.Extensible && format.BitsPerSample == 32))
                    capture.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels);
                streamLatencyMs = ProbeCapture(device, capture.WaveFormat).LatencyMs;
            }
            // The device-reported stream latency plus our buffer, when Windows reports it; else the buffer alone (the old fixed 10 ms).
            if (streamLatencyMs > 0) { LatencyMs = WasapiBufferMs + streamLatencyMs; LatencySource = $"device ({streamLatencyMs} ms stream + {WasapiBufferMs} ms buffer)"; }
            _channels = Math.Max(1, capture.WaveFormat.Channels);
            _engineRate = engineRate;
            SourceRate = capture.WaveFormat.SampleRate;
            _resampler = CreateResampler(SourceRate, engineRate, _converted.Length / 2, out _maxChunkIn);
            if (_resampler is not null) _staging = new float[_maxChunkIn * 2];
            capture.DataAvailable += OnData;
            capture.StartRecording();
            _capture = capture;
        }
        catch
        {
            if (capture is not null) { capture.DataAvailable -= OnData; try { capture.Dispose(); } catch { } }
            throw;
        }
        finally { device.Dispose(); }
    }

    /// <summary>
    /// Whether the device captures <paramref name="format"/> in shared mode, and the stream latency Windows reports for it (ms, 0 when
    /// unknown), from a throwaway client (the capture's own client is private to NAudio).
    /// </summary>
    private static (bool Supported, int LatencyMs) ProbeCapture(MMDevice device, WaveFormat format)
    {
        try
        {
            using var probe = device.AudioClient;
            if (!probe.IsFormatSupported(AudioClientShareMode.Shared, format)) return (false, 0);
            probe.Initialize(AudioClientShareMode.Shared, AudioClientStreamFlags.None, WasapiBufferMs * 10_000L, 0, format, Guid.Empty);
            return (true, (int)Math.Round(probe.StreamLatency / 10_000.0));
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException) { return (false, 0); }
    }

    private void OnData(object? sender, WaveInEventArgs e) =>
        OnSamples(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, e.BytesRecorded)));

    private void OnSamples(ReadOnlySpan<float> samples)
    {
        var frames = samples.Length / _channels;
        var capacity = _converted.Length / 2;
        // Keep the first two channels (mono is doubled). Every source frame is consumed, in chunks that fit the conversion
        // buffer; the resampler keeps its state across chunks and callbacks. Allocation-free (ASIO calls this on its driver thread).
        if (_resampler is null)
        {
            for (var start = 0; start < frames; start += capacity)
            {
                var n = Math.Min(capacity, frames - start);
                for (var i = 0; i < n; i++)
                {
                    var l = samples[(start + i) * _channels];
                    _converted[i * 2] = l;
                    _converted[i * 2 + 1] = _channels > 1 ? samples[(start + i) * _channels + 1] : l;
                }
                Deliver(n);
            }
            return;
        }
        for (var start = 0; start < frames;)
        {
            var n = Math.Min(_maxChunkIn, frames - start);
            // Stereo pairs into the staging buffer (the device may deliver 1..n channels), then resample into the conversion buffer.
            for (var i = 0; i < n; i++)
            {
                var l = samples[(start + i) * _channels];
                _staging[i * 2] = l;
                _staging[i * 2 + 1] = _channels > 1 ? samples[(start + i) * _channels + 1] : l;
            }
            var produced = _resampler.Process(_staging.AsSpan(0, n * 2), _converted, capacity);
            if (produced > 0) Deliver(produced);
            start += n;
        }
    }

    /// <summary>Stereo copy of one input chunk for the resampler (sized once for the largest chunk).</summary>
    private readonly float[] _staging = Array.Empty<float>();

    /// <summary>Capture thread: pushes converted frames to the monitoring ring and to the recorder's queue.</summary>
    private void Deliver(int produced)
    {
        var write = Volatile.Read(ref _write);
        for (var i = 0; i < produced; i++)
        {
            var at = (int)((write + i) & (RingFrames - 1)) * 2;
            _ring[at] = _converted[i * 2];
            _ring[at + 1] = _converted[i * 2 + 1];
        }
        Volatile.Write(ref _write, write + produced);
        TotalFrames += produced;
        var captured = BlockCaptured;
        captured?.Invoke(_converted, produced);
    }

    /// <summary>Frames delivered since the capture opened (diagnostics and tests).</summary>
    public long TotalFrames { get; private set; }

    /// <summary>
    /// Audio thread: the latest input for one block. Keeps only a small safety margin behind the capture so
    /// monitoring latency stays low; silence while not enough input has arrived yet. No allocation.
    /// </summary>
    public void Read(float[] left, float[] right, int frames)
    {
        var write = Volatile.Read(ref _write);
        var read = _read;
        var margin = Math.Max(frames * 2, _engineRate / 200);
        if (write - read > frames + margin * 2) read = write - frames - margin; // too far behind: catch up
        if (write - read < frames) { Array.Clear(left, 0, frames); Array.Clear(right, 0, frames); return; }
        for (var i = 0; i < frames; i++)
        {
            var at = (int)((read + i) & (RingFrames - 1)) * 2;
            left[i] = _ring[at];
            right[i] = _ring[at + 1];
        }
        _read = read + frames;
    }

    /// <summary>
    /// Engine main thread (tuner): the newest <paramref name="dest"/>.Length input frames as mono (louder of inputs 1 and 2), oldest first.
    /// Reads the ring without consuming it (the audio thread's read position is untouched); false until that much has arrived.
    /// A rare torn sample near the newest edge is harmless for pitch detection.
    /// </summary>
    public bool CopyLatestMono(float[] dest)
    {
        var n = dest.Length;
        var write = Volatile.Read(ref _write);
        if (n > RingFrames - 4096 || write < n) return false;
        double l = 0, r = 0;
        var first = write - n;
        for (var i = 0; i < n; i++)
        {
            var at = (int)((first + i) & (RingFrames - 1)) * 2;
            var a = _ring[at]; var b = _ring[at + 1];
            l += Math.Abs(a); r += Math.Abs(b);
        }
        var use = r > l ? 1 : 0;
        for (var i = 0; i < n; i++) dest[i] = _ring[(int)((first + i) & (RingFrames - 1)) * 2 + use];
        return true;
    }

    public void Dispose()
    {
        if (_capture is null) return;
        _capture.DataAvailable -= OnData;
        try { _capture.StopRecording(); } catch (Exception) { }
        finally { try { _capture.Dispose(); } catch (Exception) { } }
    }
}
