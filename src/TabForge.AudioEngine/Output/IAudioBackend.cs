using NAudio.Wave;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Audio;

namespace TabForge.AudioEngine.Output;

// Owns: the engine's audio device seam: the one interface the engine session opens its output and input through, the device list shape, and the
//   choice of backend (Windows NAudio WASAPI/ASIO/DirectSound by default; miniaudio on every other OS, and on Windows only when the dev switch asks).
// Does not own: the backends (WindowsAudioBackend, MiniaudioBackend), the mix, or the settings UI.
// Tests: PortableBackendTests (the choice per OS, the miniaudio backend on its null device), AudioBackendSelectionTests.

/// <summary>One audio device a backend lists.</summary>
public sealed record AudioDeviceInfo(string Name, bool IsDefault, int MaxChannels, int NativeRate);

/// <summary>An open, not yet started output: the player (<c>Play</c> starts it), its latency in ms and a line for the log.</summary>
public sealed record OpenedOutput(IWavePlayer Player, int LatencyMs, string Description);

/// <summary>The device layer of the engine: open the output, open the input capture, list devices. Everything above it (mix, chains, recorder) is device-free.</summary>
public interface IAudioBackend
{
    /// <summary>Short name for logs and tests ("windows", "miniaudio").</summary>
    string Id { get; }

    /// <summary>False when the backend cannot run here (the native library is missing).</summary>
    bool IsAvailable { get; }

    /// <summary>The devices of one direction; empty when there are none or the backend is unavailable. Never throws.</summary>
    IReadOnlyList<AudioDeviceInfo> Devices(bool capture);

    /// <summary>The rate the engine runs at for <paramref name="config"/> (0 in the settings = the device default).</summary>
    int SampleRateFor(EngineConfig config);

    /// <summary>Opens the output for <paramref name="source"/>; the caller starts it. Throws when the device cannot be opened.</summary>
    OpenedOutput OpenOutput(EngineConfig config, ISampleProvider source, EngineThreads threads);

    /// <summary>Opens the input capture on the configured recording device (started). Throws when it cannot be opened.</summary>
    InputCapture OpenInput(EngineConfig config, int engineRate, EngineLogWriter log);

    /// <summary>The inputs a track can record on with this config, "Input 1" first (names may be empty). Never throws.</summary>
    string[] RecordableInputs(EngineConfig config, EngineLogWriter log);
}

/// <summary>Chooses the backend for a config. Windows keeps today's NAudio device code; the miniaudio backend is for every other OS.</summary>
public static class AudioBackends
{
    /// <summary>Dev switch for Windows: set to "portable" to offer and use the miniaudio backend (needs tfportable.dll next to the engine). Default off.</summary>
    public const string DevVariable = "TABFORGE_AUDIO_BACKEND";

    /// <summary>Whether the miniaudio driver is offered on Windows (the dev switch).</summary>
    public static bool PortableOnWindows => string.Equals(Environment.GetEnvironmentVariable(DevVariable), "portable", StringComparison.OrdinalIgnoreCase);

    /// <summary>The backend for <paramref name="config"/> on this OS.</summary>
    public static IAudioBackend For(EngineConfig config) => For(config, OperatingSystem.IsWindows());

    /// <summary>The backend for <paramref name="config"/> on a Windows host or not (tests simulate the other OS with it).</summary>
    public static IAudioBackend For(EngineConfig config, bool windowsHost)
    {
        if (config.Driver == AudioOutputFactory.Null) return WindowsAudioBackend.Instance;   // the headless output opens no device on any OS
        return windowsHost && config.Driver != AudioDriverNames.Miniaudio ? WindowsAudioBackend.Instance : MiniaudioBackend.Shared;
    }

    /// <summary>On a non-Windows host a Windows driver name (settings copied from a Windows machine) becomes the miniaudio driver on the default device. Windows configs are returned unchanged.</summary>
    public static EngineConfig Normalize(EngineConfig config, bool windowsHost) =>
        windowsHost || config.Driver is AudioDriverNames.Miniaudio or AudioOutputFactory.Null ? config : config with { Driver = AudioDriverNames.Miniaudio, Device = "" };
}
