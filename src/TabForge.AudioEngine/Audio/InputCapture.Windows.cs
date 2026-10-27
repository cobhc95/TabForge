using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace TabForge.AudioEngine.Audio;

// Owns: the audio input device capture (lock-free ring to the audio thread, blocks to the recorder, armed-only open).

/// <summary>
/// The audio input (WASAPI, the chosen recording device): delivers every input channel (up to <see cref="MaxChannels"/>) to the audio thread through a
/// lock-free ring (for monitoring through the track's chain) and hands every captured block to the recorder.
/// Opened only while some track is armed; closed again when none is.
/// </summary>
public sealed class InputCapture : IDisposable
{
    private const int RingFrames = 1 << 15; // ~0.68 s at 48 kHz
    private readonly WasapiCapture? _capture;
    private const int ConvertFrames = 8192;
    /// <summary>The most input channels kept (a device with more delivers its first ones).</summary>
    public const int MaxChannels = TabForge.Audio.Contracts.AudioInputAddress.MaxChannels;
    // Sized once in the constructor (Channels wide); the callbacks never allocate.
    private float[] _ring = Array.Empty<float>();
    private long _write, _read;
    private readonly int _engineRate;
    private readonly int _channels;
    private float[] _converted = Array.Empty<float>();
    /// <summary>
    /// Band-limited windowed-sinc resamplers (one per channel pair) to the engine rate when the device cannot capture at it;
    /// null when the rates match. Preallocated, so the capture callback does not allocate.
    /// </summary>
    private SincResampler[]? _resamplers;
    private float[] _pairOut = Array.Empty<float>();
    private readonly int _maxChannels = MaxChannels;
    private string? _fault;
    private int _faultTaken;

    /// <summary>
    /// Engine main thread: the reason the input stopped (a fault in a capture callback, or the device ending the stream),
    /// once; null otherwise. A stopped input delivers nothing more: monitoring hears silence, the output is not touched.
    /// </summary>
    public string? TakeFault() => Volatile.Read(ref _fault) is { } fault && Interlocked.Exchange(ref _faultTaken, 1) == 0 ? fault : null;

    /// <summary>
    /// The WASAPI capture with every channel; when that cannot be opened and <paramref name="allInputs"/> was asked, once more
    /// with two channels (logged). Throws only when the two-channel capture fails too.
    /// </summary>
    public static InputCapture Open(string deviceName, int engineRate, bool allInputs, EngineLogWriter log)
    {
        if (allInputs)
        {
            try { return new InputCapture(deviceName, engineRate, MaxChannels); }
            catch (Exception ex) { log.Write($"input with all channels failed ({ex.Message}): opening two channels"); }
        }
        return new InputCapture(deviceName, engineRate, 2);
    }

    /// <summary>Channels in the ring, in <see cref="BlockCaptured"/> blocks and in <see cref="Read"/>: the device's, at least 2 (a mono device is doubled), at most <see cref="MaxChannels"/>.</summary>
    public int Channels { get; private set; } = 2;

    /// <summary>Constructor: every buffer, sized for the device's channel count and rate. Returns the largest input chunk.</summary>
    private int Allocate(int deviceChannels, int sourceRate, int engineRate)
    {
        Channels = Math.Clamp(deviceChannels, 2, Math.Clamp(_maxChannels, 2, MaxChannels));
        _ring = new float[RingFrames * Channels];
        _converted = new float[ConvertFrames * Channels];
        var first = CreateResampler(sourceRate, engineRate, ConvertFrames, out var maxChunkIn);
        if (first is null) return maxChunkIn;
        _resamplers = new SincResampler[(Channels + 1) / 2];
        _resamplers[0] = first;
        for (var p = 1; p < _resamplers.Length; p++) _resamplers[p] = new SincResampler(sourceRate, engineRate, maxChunkIn);
        _staging = new float[maxChunkIn * 2];
        _pairOut = new float[ConvertFrames * 2];
        return maxChunkIn;
    }
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

    /// <summary>Capture thread: every block of input (interleaved, <see cref="Channels"/> wide, at the engine rate), for recording. Must not block
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
        _maxChunkIn = Allocate(_channels, SourceRate, engineRate);
    }

    /// <summary>Feed mode: the device stream that feeds this capture (the portable backend's); disposed with it. Null for WASAPI and ASIO.</summary>
    public IDisposable? Source { get; init; }

    /// <summary>A feeding source reports that its device ended the stream: the same fault <see cref="TakeFault"/> delivers for a stopped WASAPI capture.</summary>
    public void ReportFault(string reason) => Volatile.Write(ref _fault, reason);

    /// <summary>Feed mode: the channels each fed block must have (0 for a capture that reads its own device).</summary>
    public int FeedChannels => _capture is null ? _channels : 0;

    /// <summary>ASIO callback thread: interleaved input samples at the engine rate.</summary>
    public void Feed(ReadOnlySpan<float> samples) => Guarded(samples);

    public InputCapture(string deviceName, int engineRate, int maxChannels = MaxChannels)
    {
        _maxChannels = maxChannels;
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
            // Capture at the engine rate when the device takes it (no resampling at all); otherwise float at its own rate.
            var atEngineRate = WaveFormat.CreateIeeeFloatWaveFormat(engineRate, format.Channels);
            var (supported, streamLatencyMs) = ProbeCapture(device, atEngineRate);
            if (supported) capture.WaveFormat = atEngineRate;
            else
            {
                if (format.Encoding != WaveFormatEncoding.IeeeFloat && !(format.Encoding == WaveFormatEncoding.Extensible && format.BitsPerSample == 32))
                    capture.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels);
                streamLatencyMs = ProbeCapture(device, capture.WaveFormat).LatencyMs;
            }
            // The device-reported stream latency plus our buffer, when Windows reports it; else the buffer alone.
            if (streamLatencyMs > 0) { LatencyMs = WasapiBufferMs + streamLatencyMs; LatencySource = $"device ({streamLatencyMs} ms stream + {WasapiBufferMs} ms buffer)"; }
            _channels = Math.Max(1, capture.WaveFormat.Channels);
            _engineRate = engineRate;
            SourceRate = capture.WaveFormat.SampleRate;
            _maxChunkIn = Allocate(_channels, SourceRate, engineRate);
            capture.DataAvailable += OnData;
            capture.RecordingStopped += OnStopped;
            capture.StartRecording();
            _capture = capture;
        }
        catch
        {
            if (capture is not null) { capture.DataAvailable -= OnData; capture.RecordingStopped -= OnStopped; try { capture.Dispose(); } catch { } // swallow: best-effort dispose of a replaced capture
 }
            throw;
        }
        finally { device.Dispose(); }
    }

    /// <summary>Engine main thread: how many input channels the WASAPI recording device offers (its shared-mode format), 0 when it cannot be read. Opens no stream.</summary>
    public static int DeviceChannels(string deviceName)
    {
        try
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
            using (device) return Math.Clamp(device.AudioClient.MixFormat.Channels, 0, MaxChannels);
        }
        catch (Exception) { return 0; }   // any failure of the device query: the caller falls back to two inputs
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
        Guarded(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, Math.Clamp(e.BytesRecorded, 0, e.Buffer.Length))));

    /// <summary>Capture thread: the device ended the stream with an error (unplugged, format changed).</summary>
    private void OnStopped(object? sender, StoppedEventArgs e) { if (e.Exception is { } ex) Volatile.Write(ref _fault, ex.Message); }

    /// <summary>
    /// Capture thread (the driver's own for ASIO): the outermost guard of the input path (conversion, ring, recorder queue).
    /// Nothing thrown here reaches the driver or the output: the input stops and the reason waits for <see cref="TakeFault"/>.
    /// </summary>
    private void Guarded(ReadOnlySpan<float> samples)
    {
        if (_fault is not null) return;
        try { OnSamples(samples); }
        catch (Exception ex) { Volatile.Write(ref _fault, ex.Message); }
    }

    private void OnSamples(ReadOnlySpan<float> samples)
    {
        var frames = samples.Length / _channels;
        var width = Channels;
        var kept = Math.Min(_channels, width);
        // Keep every channel up to the cap (mono is doubled). Every source frame is consumed, in chunks that fit the conversion
        // buffer; the resamplers keep their state across chunks and callbacks. Allocation-free (ASIO calls this on its driver thread).
        if (_resamplers is null)
        {
            for (var start = 0; start < frames; start += ConvertFrames)
            {
                var n = Math.Min(ConvertFrames, frames - start);
                for (var i = 0; i < n; i++)
                {
                    var from = (start + i) * _channels; var to = i * width;
                    for (var c = 0; c < kept; c++) _converted[to + c] = samples[from + c];
                    if (_channels == 1) _converted[to + 1] = samples[from];
                }
                Deliver(n);
            }
            return;
        }
        for (var start = 0; start < frames;)
        {
            var n = Math.Min(_maxChunkIn, frames - start);
            var produced = 0;
            // One channel pair at a time into the staging buffer, resampled, then interleaved into the conversion buffer.
            // Every pair's resampler sees the same frame counts, so they all produce the same number of frames.
            for (var p = 0; p < _resamplers.Length; p++)
            {
                var left = p * 2; var right = _channels == 1 ? 0 : Math.Min(left + 1, _channels - 1);
                for (var i = 0; i < n; i++)
                {
                    var from = (start + i) * _channels;
                    _staging[i * 2] = samples[from + left];
                    _staging[i * 2 + 1] = samples[from + right];
                }
                var made = _resamplers[p].Process(_staging.AsSpan(0, n * 2), _pairOut, ConvertFrames);
                produced = p == 0 ? made : Math.Min(produced, made);
                for (var i = 0; i < made; i++)
                {
                    _converted[i * width + left] = _pairOut[i * 2];
                    if (left + 1 < width) _converted[i * width + left + 1] = _pairOut[i * 2 + 1];
                }
            }
            if (produced > 0) Deliver(produced);
            start += n;
        }
    }

    /// <summary>Stereo copy of one input chunk for the resampler (sized once for the largest chunk).</summary>
    private float[] _staging = Array.Empty<float>();

    /// <summary>Capture thread: pushes converted frames to the monitoring ring and to the recorder's queue.</summary>
    private void Deliver(int produced)
    {
        var write = Volatile.Read(ref _write);
        var width = Channels;
        for (var i = 0; i < produced; i++)
        {
            var at = (int)((write + i) & (RingFrames - 1)) * width;
            var from = i * width;
            for (var c = 0; c < width; c++) _ring[at + c] = _converted[from + c];
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
    public void Read(InputBlock block, int frames)
    {
        var width = Math.Min(Channels, block.Ch.Length);
        frames = Math.Clamp(frames, 0, block.Ch[0].Length);   // never past the block's buffers
        block.Channels = width;
        block.Frames = frames;
        var write = Volatile.Read(ref _write);
        var read = _read;
        var margin = Math.Max(frames * 2, _engineRate / 200);
        if (write - read > frames + margin * 2) read = write - frames - margin; // too far behind: catch up
        if (write - read < frames) { for (var c = 0; c < width; c++) Array.Clear(block.Ch[c], 0, frames); return; }
        for (var c = 0; c < width; c++)
        {
            var dest = block.Ch[c];
            for (var i = 0; i < frames; i++) dest[i] = _ring[(int)((read + i) & (RingFrames - 1)) * Channels + c];
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
            var at = (int)((first + i) & (RingFrames - 1)) * Channels;
            var a = _ring[at]; var b = _ring[at + 1];
            l += Math.Abs(a); r += Math.Abs(b);
        }
        var use = r > l ? 1 : 0;
        for (var i = 0; i < n; i++) dest[i] = _ring[(int)((first + i) & (RingFrames - 1)) * Channels + use];
        return true;
    }

    public void Dispose()
    {
        Source?.Dispose();
        if (_capture is null) return;
        _capture.DataAvailable -= OnData;
        _capture.RecordingStopped -= OnStopped;
        try { _capture.StopRecording(); } catch (Exception) { } // swallow: best-effort stop of a capture that may already have stopped
        finally { try { _capture.Dispose(); } catch (Exception) { } // swallow: best-effort dispose during teardown
 }
    }
}
