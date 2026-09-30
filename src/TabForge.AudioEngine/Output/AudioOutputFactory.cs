using NAudio.CoreAudioApi;
using NAudio.Wave;
using TabForge.Audio.Contracts;

namespace TabForge.AudioEngine.Output;

/// <summary>Opens the audio device chosen in Settings > Audio & VST (WASAPI shared / exclusive, ASIO, DirectSound).</summary>
public static class AudioOutputFactory
{
    public const string WasapiShared = AudioDriverNames.WasapiShared;
    public const string WasapiExclusive = AudioDriverNames.WasapiExclusive;
    public const string Asio = AudioDriverNames.Asio;
    public const string DirectSound = AudioDriverNames.DirectSound;
    /// <summary>
    /// Headless output (tests only, never offered in Settings): no device; see <see cref="NullOutput"/>. Device
    /// <see cref="NullManual"/> runs no clock thread, the caller pumps blocks itself.
    /// </summary>
    public const string Null = "Null";
    public const string NullManual = "manual";

    /// <summary>Channel names of the ASIO driver opened last (empty for other drivers).</summary>
    public static (string[] Inputs, string[] Outputs) AsioChannels { get; private set; } = (Array.Empty<string>(), Array.Empty<string>());

    /// <summary>The sample rate the engine should run at for this configuration.</summary>
    public static int SampleRateFor(EngineConfig config)
    {
        if (config.Driver != WasapiShared) return config.SampleRate;
        // Shared mode runs at the device's own mix rate (no resampling in the chain).
        try { return FindRenderDevice(config.Device)?.AudioClient.MixFormat.SampleRate ?? config.SampleRate; }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { return config.SampleRate; }
    }

    /// <summary>Creates and initialises the output for <paramref name="source"/>; returns it with its latency in ms.</summary>
    public static (IWavePlayer Player, int LatencyMs, string Description) Open(EngineConfig config, ISampleProvider source)
    {
        if (config.Driver == Null)
        {
            var output = new NullOutput(source, config.BufferSize, string.Equals(config.Device, NullManual, StringComparison.OrdinalIgnoreCase));
            AsioInputChannelsUsed = 0;
            return (output, Math.Max(1, (int)Math.Round(output.BlockFrames * 1000.0 / source.WaveFormat.SampleRate)), "Null output (headless)");
        }
        // No blocking gen-2 GC pauses while the output runs (concurrent GC is on by default in the engine).
        try { System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency; } catch (InvalidOperationException) { }
        var blockMs = Math.Max(1, (int)Math.Ceiling(config.BufferSize * 1000.0 / source.WaveFormat.SampleRate));
        switch (config.Driver)
        {
            case Asio:
            {
                var names = AsioOut.GetDriverNames();
                var name = names.FirstOrDefault(n => string.Equals(n, config.Device, StringComparison.OrdinalIgnoreCase)) ?? names.FirstOrDefault()
                    ?? throw new InvalidOperationException("No ASIO driver is installed.");
                var asio = new AsioOut(name);
                var ok = false;
                try
                {
                    // Channel pair: the first channel of each pair, kept inside what the driver offers.
                    var outputs = asio.DriverOutputChannelCount;
                    var inputs = asio.DriverInputChannelCount;
                    AsioChannels = (Enumerable.Range(0, inputs).Select(i => Safe(() => asio.AsioInputChannelName(i), $"Input {i + 1}")).ToArray(),
                                    Enumerable.Range(0, outputs).Select(i => Safe(() => asio.AsioOutputChannelName(i), $"Output {i + 1}")).ToArray());
                    var firstOut = Math.Clamp(config.AsioOutput, 0, Math.Max(0, outputs - 1));
                    asio.ChannelOffset = firstOut;
                    var monoOut = config.AsioOutputLast <= firstOut || outputs - firstOut < 2;   // one output channel: the mix is summed to it
                    RequestBufferSize(asio, config.BufferSize);
                    // Inputs: first..last channel (one or two) through the same driver: the only way input and
                    // output work together on an exclusive ASIO driver.
                    var first = Math.Clamp(config.AsioInput, 0, Math.Max(0, inputs - 1));
                    var count = Math.Clamp(config.AsioInputLast - first + 1, 1, Math.Min(2, Math.Max(1, inputs - first)));
                    asio.InputChannelOffset = first;
                    IWaveProvider provider = monoOut
                        ? new NAudio.Wave.SampleProviders.StereoToMonoSampleProvider(source) { LeftVolume = 0.5f, RightVolume = 0.5f }.ToWaveProvider()
                        : source.ToWaveProvider();
                    if (config.AsioInputs && inputs >= 1) asio.InitRecordAndPlayback(provider, count, source.WaveFormat.SampleRate);
                    else asio.Init(provider);
                    AsioInputChannelsUsed = config.AsioInputs && inputs >= 1 ? count : 0;
                    AsioInputLatencyFrames = ReadInputLatency(asio);
                    var latency = Math.Max(1, (int)Math.Round(asio.PlaybackLatency * 1000.0 / source.WaveFormat.SampleRate));
                    ok = true;
                    return (asio, latency, $"ASIO: {name}");
                }
                finally { if (!ok) { try { asio.Dispose(); } catch (Exception) { } } }
            }
            case DirectSound:
            {
                var device = DirectSoundOut.Devices.FirstOrDefault(d => string.Equals(d.Description, config.Device, StringComparison.OrdinalIgnoreCase));
                // NAudio's DirectSoundOut splits this into two halves and refills one per notification: 40 ms (20 ms halves)
                // starves whenever the polling thread is delayed. 80 ms, or 4 blocks if larger, keeps a safe margin.
                var latency = Math.Max(80, blockMs * 4);
                var ds = device is null ? new DirectSoundOut(latency) : new DirectSoundOut(device.Guid, latency);
                ds.Init(new RenderWatch(source, latency).ToWaveProvider());
                return (ds, latency, $"DirectSound: {device?.Description ?? "default device"}");
            }
            default:
            {
                var exclusive = config.Driver == WasapiExclusive;
                var device = FindRenderDevice(config.Device) ?? throw new InvalidOperationException("No audio output device found.");
                var latency = Math.Max(exclusive ? 3 : 10, blockMs * 2);
                var wasapi = new WasapiOut(device, exclusive ? AudioClientShareMode.Exclusive : AudioClientShareMode.Shared, true, latency);
                wasapi.Init(source.ToWaveProvider());
                return (wasapi, latency, $"{config.Driver}: {device.FriendlyName}");
            }
        }
    }

    /// <summary>
    /// Requests a block size: the driver's buffer size is asked for by changing the preferred size NAudio then creates
    /// its buffers with (kept inside the driver's minimum, maximum and granularity). A driver that insists on its own panel
    /// setting keeps that; the real size is read back and shown.
    /// </summary>
    private static void RequestBufferSize(AsioOut asio, int wanted)
    {
        try
        {
            var field = typeof(AsioOut).GetField("driver", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field is null) { EngineLog.Write("buffer size request not possible: AsioOut driver field not found"); return; }
            var capability = (field.GetValue(asio) as NAudio.Wave.Asio.AsioDriverExt)?.Capabilities;
            if (capability is null || wanted <= 0) return;
            var min = capability.BufferMinSize; var max = capability.BufferMaxSize; var step = capability.BufferGranularity;
            var size = Math.Clamp(wanted, min, max);
            if (step > 0) size = min + (size - min) / step * step;                      // multiples of the granularity
            else if (step == -1) { var p = min; while (p * 2 <= size && p * 2 <= max) p *= 2; size = p; }   // powers of two
            capability.BufferPreferredSize = size;
        }
        catch (Exception ex) when (ex is System.Reflection.TargetException or InvalidCastException or NullReferenceException or MethodAccessException)
        {
            EngineLog.Write($"buffer size request not possible: {ex.Message}");
        }
    }

    /// <summary>
    /// Wraps the mix for DirectSound: raises the priority of NAudio's polling thread on its first refill and counts refills that
    /// came late (gap well above the expected cadence) or slow, logging them to the engine log.
    /// </summary>
    private sealed class RenderWatch : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _latencyMs;
        private long _last, _calls, _late, _slow, _lost;
        private bool _raised;
        public RenderWatch(ISampleProvider source, int latencyMs) { _source = source; _latencyMs = latencyMs; }
        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            if (!_raised) { _raised = true; EngineThreads.RaiseRenderPriority(); }
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            var read = _source.Read(buffer, offset, count);
            var end = System.Diagnostics.Stopwatch.GetTimestamp();
            var periodMs = count / 2 * 1000.0 / WaveFormat.SampleRate;
            var freq = (double)System.Diagnostics.Stopwatch.Frequency;
            if (++_calls > 4 && _last != 0)   // the first calls prefill the buffer back to back
            {
                var gapMs = (start - _last) * 1000.0 / freq;
                var costMs = (end - start) * 1000.0 / freq;
                var bad = false;
                if (gapMs > _latencyMs) { _lost++; bad = true; }
                else if (gapMs > periodMs * 1.6) { _late++; bad = true; }
                if (costMs > periodMs * 0.5) { _slow++; bad = true; }
                if (bad && (_late + _lost + _slow <= 20 || (_late + _lost + _slow) % 50 == 0))
                    EngineLog.Write($"DirectSound refill: gap {gapMs:F1} ms (expected {periodMs:F1}), render {costMs:F1} ms; late {_late}, over-buffer {_lost}, slow {_slow}");
            }
            _last = start;
            return read;
        }
    }

    /// <summary>The input latency the opened ASIO driver reports (frames; 0 = not reported), used to line recorded takes up.</summary>
    public static int AsioInputLatencyFrames { get; private set; }

    /// <summary>ASIOGetLatencies' input value, through the same private driver field as <see cref="RequestBufferSize"/>.</summary>
    private static int ReadInputLatency(AsioOut asio)
    {
        try
        {
            var field = typeof(AsioOut).GetField("driver", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if ((field?.GetValue(asio) as NAudio.Wave.Asio.AsioDriverExt)?.Driver is not { } driver) return 0;
            driver.GetLatencies(out var input, out _);
            return Math.Max(0, input);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)   // a driver error (AsioException, COM) or a changed NAudio field
        {
            EngineLog.Write($"ASIO input latency not available: {ex.Message}");
            return 0;
        }
    }

    /// <summary>How many ASIO input channels the opened driver delivers to the engine (0 = none).</summary>
    public static int AsioInputChannelsUsed { get; private set; }

    private static string Safe(Func<string> name, string fallback)
    {
        try { var n = name(); return string.IsNullOrWhiteSpace(n) ? fallback : n; }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { return fallback; }
    }

    private static MMDevice? FindRenderDevice(string name)
    {
        var enumerator = new MMDeviceEnumerator();
        if (!string.IsNullOrWhiteSpace(name))
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                if (string.Equals(device.FriendlyName, name, StringComparison.OrdinalIgnoreCase)) return device;
        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }
}
