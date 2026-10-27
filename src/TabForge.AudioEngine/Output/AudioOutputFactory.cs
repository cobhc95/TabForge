using NAudio.CoreAudioApi;
using NAudio.Wave;
using TabForge.Audio.Contracts;

namespace TabForge.AudioEngine.Output;

// Owns: opening the audio device chosen in Settings (WASAPI shared/exclusive, ASIO, DirectSound).

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

    /// <summary>The rate used when the setting is "Device default" (0) and the device gives none of its own.</summary>
    public const int DefaultRate = 48000;

    /// <summary>The sample rate the engine should run at for this configuration.</summary>
    public static int SampleRateFor(EngineConfig config)
    {
        var chosen = config.SampleRate > 0 ? config.SampleRate : DefaultRate;
        if (config.Driver == WasapiExclusive && config.SampleRate <= 0)
        {
            try
            {
                var device = FindRenderDevice(config.Device);
                if (device is not null) return ExclusiveRate(device.AudioClient.MixFormat.SampleRate, rate => device.AudioClient.IsFormatSupported(AudioClientShareMode.Exclusive, WaveFormat.CreateIeeeFloatWaveFormat(rate, 2)), chosen);
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { return chosen; } // swallow: an unreadable device keeps the default rate
        }
        if (config.Driver != WasapiShared) return chosen;
        // Shared mode runs at the device's own mix rate (no resampling in the chain).
        try { return FindRenderDevice(config.Device)?.AudioClient.MixFormat.SampleRate ?? chosen; }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { return chosen; }
    }

    /// <summary>The rate for exclusive WASAPI at "Device default": the device's mix rate when it accepts it exclusively, else the first usual rate it accepts, else <paramref name="fallback"/>.</summary>
    public static int ExclusiveRate(int mixRate, Func<int, bool> accepts, int fallback) =>
        mixRate > 0 && accepts(mixRate) ? mixRate : AudioDriverNames.SampleRates.FirstOrDefault(accepts, fallback);

    /// <summary>The installed ASIO driver a device setting names; an empty or unknown name is the first installed one.</summary>
    public static string ResolveAsioName(string device)
    {
        var names = AsioOut.GetDriverNames();
        return names.FirstOrDefault(n => string.Equals(n, device, StringComparison.OrdinalIgnoreCase)) ?? names.FirstOrDefault()
            ?? throw new InvalidOperationException("No ASIO driver is installed.");
    }

    /// <summary>Creates and initialises the output for <paramref name="source"/>; returns it with its latency in ms.</summary>
    public static (IWavePlayer Player, int LatencyMs, string Description) Open(EngineConfig config, ISampleProvider source, EngineThreads threads)
    {
        if (config.Driver == Null)
        {
            var output = new NullOutput(source, config.BufferSize, string.Equals(config.Device, NullManual, StringComparison.OrdinalIgnoreCase));
            AsioInputChannelsUsed = 0;
            return (output, Math.Max(1, (int)Math.Round(output.BlockFrames * 1000.0 / source.WaveFormat.SampleRate)), "Null output (headless)");
        }
        // No blocking gen-2 GC pauses while the output runs (concurrent GC is on by default in the engine).
        try { System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency; } catch (InvalidOperationException) { } // swallow: low-latency mode is unavailable on this runtime
        var blockMs = Math.Max(1, (int)Math.Ceiling(config.BufferSize * 1000.0 / source.WaveFormat.SampleRate));
        switch (config.Driver)
        {
            case Asio:
            {
                var name = ResolveAsioName(config.Device);
                var asio = new AsioOut(name);
                bool ok = false, atInit = false;
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
                    RequestBufferSize(asio, config.BufferSize, threads.Log);
                    // Inputs: every channel from the chosen first one (it is "Input 1") through the same driver: the only way
                    // input and output work together on an exclusive ASIO driver. Last <= first is the one-channel set-up:
                    // only that channel is opened (the capture copies it to inputs 1 and 2).
                    var first = Math.Clamp(config.AsioInput, 0, Math.Max(0, inputs - 1));
                    var count = AsioInputCount(config, inputs);
                    asio.InputChannelOffset = first;
                    IWaveProvider provider = monoOut
                        ? new NAudio.Wave.SampleProviders.StereoToMonoSampleProvider(source) { LeftVolume = 0.5f, RightVolume = 0.5f }.ToWaveProvider()
                        : source.ToWaveProvider();
                    atInit = true;   // from here a failure may be the input channel count: only that is retried
                    if (config.AsioInputs && inputs >= 1) asio.InitRecordAndPlayback(provider, count, source.WaveFormat.SampleRate);
                    else asio.Init(provider);
                    AsioInputChannelsUsed = config.AsioInputs && inputs >= 1 ? count : 0;
                    AsioInputLatencyFrames = ReadInputLatency(asio, threads.Log);
                    var latency = Math.Max(1, (int)Math.Round(asio.PlaybackLatency * 1000.0 / source.WaveFormat.SampleRate));
                    ok = true;
                    return (asio, latency, $"ASIO: {name}");
                }
                catch (Exception ex) when (config.AsioInputs && atInit)
                {
                    // An input-side problem never costs the output: once more with two input channels, then with none. A driver
                    // that cannot be opened at all (busy, unplugged) fails before the init step and is not opened again.
                    try { asio.Dispose(); } catch (Exception) { } // swallow: best-effort dispose of a failed device
                    ok = true;   // disposed here
                    threads.Log.Write($"ASIO open failed with {(config.AllInputs ? "all inputs" : "inputs")} ({ex.Message}): retrying with {(config.AllInputs ? "two input channels" : "no inputs")}");
                    return Open(config.AllInputs ? config with { AllInputs = false } : config with { AsioInputs = false }, source, threads);
                }
                finally { if (!ok) { try { asio.Dispose(); } catch (Exception) { } // swallow: best-effort dispose of a failed device
 } }
            }
            case DirectSound:
            {
                var device = DirectSoundOut.Devices.FirstOrDefault(d => string.Equals(d.Description, config.Device, StringComparison.OrdinalIgnoreCase));
                // NAudio's DirectSoundOut splits this into two halves and refills one per notification: 40 ms (20 ms halves)
                // starves whenever the polling thread is delayed. At least 100 ms per half (200 ms buffer), or 4 of the user's blocks if larger.
                var latency = DirectSoundWatch.LatencyMs(config.BufferSize, source.WaveFormat.SampleRate);
                var ds = device is null ? new DirectSoundOut(latency) : new DirectSoundOut(device.Guid, latency);
                ds.Init(new DirectSoundWatch(source, latency, threads).ToWaveProvider());
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
    private static void RequestBufferSize(AsioOut asio, int wanted, EngineLogWriter log)
    {
        try
        {
            var field = typeof(AsioOut).GetField("driver", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field is null) { log.Write("buffer size request not possible: AsioOut driver field not found"); return; }
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
            log.Write($"buffer size request not possible: {ex.Message}");
        }
    }

    /// <summary>The input latency the opened ASIO driver reports (frames; 0 = not reported), used to line recorded takes up.</summary>
    public static int AsioInputLatencyFrames { get; private set; }

    /// <summary>ASIOGetLatencies' input value, through the same private driver field as <see cref="RequestBufferSize"/>.</summary>
    private static int ReadInputLatency(AsioOut asio, EngineLogWriter log)
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
            log.Write($"ASIO input latency not available: {ex.Message}");
            return 0;
        }
    }

    /// <summary>How many ASIO input channels the opened driver delivers to the engine (0 = none).</summary>
    public static int AsioInputChannelsUsed { get; private set; }

    /// <summary>How many ASIO input channels are opened from the chosen first one: one for the one-channel set-up (last &lt;= first), else all of them up to the cap.</summary>
    public static int AsioInputCount(EngineConfig config, int driverInputs)
    {
        var first = Math.Clamp(config.AsioInput, 0, Math.Max(0, driverInputs - 1));
        if (!config.AllInputs) return Math.Clamp(config.AsioInputLast - first + 1, 1, Math.Min(2, Math.Max(1, driverInputs - first)));   // two channels at most
        return config.AsioInputLast <= first ? 1 : Math.Clamp(driverInputs - first, 1, Audio.InputCapture.MaxChannels);
    }

    /// <summary>
    /// Engine main thread, after <see cref="Open"/> and before the Ready event is written: the inputs tracks can record on this
    /// device, "Input 1" first. For ASIO the driver's channel names from the chosen first input on, for WASAPI one unnamed
    /// entry per channel of the recording device (two when the device cannot be asked), none for the headless output. Never throws.
    /// </summary>
    /// <param name="probe">The WASAPI channel count of a recording device (tests replace it).</param>
    public static string[] RecordableInputs(EngineConfig config, EngineLogWriter log, Func<string, int>? probe = null)
    {
        if (config.Driver == Null) return Array.Empty<string>();
        if (config.Driver == Asio)
            return AsioChannels.Inputs.Skip(Math.Clamp(config.AsioInput, 0, Math.Max(0, AsioChannels.Inputs.Length - 1))).Take(AsioInputChannelsUsed).ToArray();
        if (!config.AllInputs) return new[] { "", "" };
        int count;
        try { count = (probe ?? Audio.InputCapture.DeviceChannels)(config.InputDevice); }
        catch (Exception ex) { log.Write($"input channel count not read: {ex.Message}"); count = 0; }
        return Enumerable.Repeat("", count is > 0 and <= Audio.InputCapture.MaxChannels ? count : 2).ToArray();
    }

    internal static string Safe(Func<string> name, string fallback)
    {
        try { var n = name(); return string.IsNullOrWhiteSpace(n) ? fallback : n; }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { return fallback; }
    }

    private static MMDevice? FindRenderDevice(string name)
    {
        using var enumerator = new MMDeviceEnumerator();
        if (!string.IsNullOrWhiteSpace(name))
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                if (string.Equals(device.FriendlyName, name, StringComparison.OrdinalIgnoreCase)) return device;
        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }
}
