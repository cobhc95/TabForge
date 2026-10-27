using NAudio.Wave;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Audio;

namespace TabForge.AudioEngine.Output;

// Owns: the portable IAudioBackend over miniaudio (CoreAudio on macOS; ALSA, PulseAudio, PipeWire and JACK on Linux; the null device for tests): the device
//   lists, the sample-rate choice, and opening the output and input streams.
// Does not own: the native library (TfPortable), the streams (MiniaudioOut, MiniaudioInput) or the settings UI.
// Tests: PortableBackendTests.

/// <summary>The audio backend for every OS but Windows, and an opt-in dev backend on Windows. Needs the tfportable native library next to the program.</summary>
public sealed class MiniaudioBackend : IAudioBackend, IDisposable
{
    /// <summary>The process-wide backend on the real devices.</summary>
    public static readonly MiniaudioBackend Shared = new(nullDevice: false);

    private readonly bool _nullDevice;
    private readonly object _gate = new();
    private nint _context;
    private bool _contextTried;

    /// <param name="nullDevice">True: miniaudio's null backend only (a clocked device that plays to nowhere; tests).</param>
    public MiniaudioBackend(bool nullDevice) => _nullDevice = nullDevice;

    public string Id => "miniaudio";
    public bool IsAvailable => Context != 0;

    private static TfPortable? Lib => TfPortable.Current;

    /// <summary>The miniaudio context, created on first use; zero when the library is missing or no backend starts.</summary>
    private nint Context
    {
        get
        {
            lock (_gate)
            {
                if (!_contextTried)
                {
                    _contextTried = true;
                    _context = Lib?.AudioContextCreate(_nullDevice) ?? 0;
                }
                return _context;
            }
        }
    }

    /// <summary>The name of the miniaudio backend in use ("WASAPI", "ALSA", "Core Audio", "Null"...), empty when unavailable.</summary>
    public string BackendName => Context != 0 && Lib is { } lib ? lib.AudioBackendName(Context) : "";

    public IReadOnlyList<AudioDeviceInfo> Devices(bool capture)
    {
        var context = Context;
        if (context == 0 || Lib is not { } lib) return Array.Empty<AudioDeviceInfo>();
        try
        {
            var list = new List<AudioDeviceInfo>();
            var count = lib.AudioDeviceCount(context, capture);
            for (var i = 0; i < count; i++)
                if (lib.AudioDeviceInfo(context, capture, i, out var name, out var isDefault, out var channels, out var rate)) list.Add(new AudioDeviceInfo(name, isDefault, channels, rate));
            return list;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException) { return Array.Empty<AudioDeviceInfo>(); }   // swallow: a library of another version lists nothing
    }

    private AudioDeviceInfo? Find(bool capture, string name)
    {
        var devices = Devices(capture);
        if (!string.IsNullOrWhiteSpace(name))
            foreach (var d in devices) if (string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)) return d;
        foreach (var d in devices) if (d.IsDefault) return d;
        return devices.Count > 0 ? devices[0] : null;
    }

    public int SampleRateFor(EngineConfig config)
    {
        if (config.SampleRate > 0) return config.SampleRate;
        var native = Find(false, config.Device)?.NativeRate ?? 0;
        return native >= 8000 ? native : AudioOutputFactory.DefaultRate;
    }

    private (TfPortable Lib, nint Context) Require()
    {
        var context = Context;
        if (context == 0 || Lib is not { } lib) throw new InvalidOperationException("The system audio library (tfportable) is not installed next to TabForge.");
        return (lib, context);
    }

    public OpenedOutput OpenOutput(EngineConfig config, ISampleProvider source, EngineThreads threads)
    {
        var (lib, context) = Require();
        var period = Math.Clamp(config.BufferSize, 16, 8192);
        var device = config.Device ?? "";
        var player = new MiniaudioOut(lib, context, device, source, period, threads);
        var rate = Math.Max(1, player.ActualRate);
        var latencyMs = Math.Max(1, (int)Math.Round(Math.Max(player.LatencyFrames, period) * 1000.0 / rate));
        return new OpenedOutput(player, latencyMs, $"{BackendName}: {(string.IsNullOrWhiteSpace(device) ? "default device" : device)}");
    }

    public InputCapture OpenInput(EngineConfig config, int engineRate, EngineLogWriter log)
    {
        var (lib, context) = Require();
        var info = Find(true, config.InputDevice);
        var wanted = config.AllInputs ? Math.Clamp(info?.MaxChannels is > 0 ? info.MaxChannels : 2, 1, InputCapture.MaxChannels) : 2;
        var name = info?.Name ?? "";
        MiniaudioInput stream;
        try { stream = new MiniaudioInput(lib, context, name, wanted, 0); }
        catch (Exception ex) when (wanted != 2)
        {
            log.Write($"input with {wanted} channels failed ({ex.Message}): opening two channels");
            stream = new MiniaudioInput(lib, context, name, 2, 0);
        }
        var latencyMs = Math.Max(1, (int)Math.Round(stream.LatencyFrames * 1000.0 / Math.Max(1, stream.SampleRate)));
        var capture = new InputCapture(string.IsNullOrEmpty(name) ? "default input" : name, engineRate, stream.Channels, latencyMs, stream.SampleRate) { Source = stream };
        stream.Attach(capture);
        stream.Start();
        return capture;
    }

    public string[] RecordableInputs(EngineConfig config, EngineLogWriter log)
    {
        if (!config.AllInputs) return new[] { "", "" };
        var channels = Find(true, config.InputDevice)?.MaxChannels ?? 0;
        return Enumerable.Repeat("", channels is > 0 and <= InputCapture.MaxChannels ? channels : 2).ToArray();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_context != 0 && Lib is { } lib) lib.AudioContextDestroy(_context);
            _context = 0;
            _contextTried = true;
        }
    }
}
