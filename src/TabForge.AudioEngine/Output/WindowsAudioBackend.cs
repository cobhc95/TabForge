using NAudio.Wave;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Audio;

namespace TabForge.AudioEngine.Output;

// Owns: the Windows adapter of IAudioBackend: every call goes to the code that was always used (AudioOutputFactory for WASAPI/ASIO/DirectSound and the
//   headless output, InputCapture for the WASAPI/ASIO input). No behaviour of its own.
// Does not own: the device code itself.
// Tests: AudioBackendSelectionTests.

/// <summary>The default backend on Windows (and the headless output everywhere): NAudio WASAPI, ASIO and DirectSound exactly as before the seam existed.</summary>
public sealed class WindowsAudioBackend : IAudioBackend
{
    public static readonly WindowsAudioBackend Instance = new();

    public string Id => "windows";
    public bool IsAvailable => true;

    public IReadOnlyList<AudioDeviceInfo> Devices(bool capture) => Array.Empty<AudioDeviceInfo>();   // the Windows lists come from IAudioDevices (WindowsAudioDevices) in the app

    public int SampleRateFor(EngineConfig config) => AudioOutputFactory.SampleRateFor(config);

    public OpenedOutput OpenOutput(EngineConfig config, ISampleProvider source, EngineThreads threads)
    {
        var (player, latencyMs, description) = AudioOutputFactory.Open(config, source, threads);
        return new OpenedOutput(player, latencyMs, description);
    }

    public InputCapture OpenInput(EngineConfig config, int engineRate, EngineLogWriter log) => InputCapture.Open(config.InputDevice, engineRate, config.AllInputs, log);

    public string[] RecordableInputs(EngineConfig config, EngineLogWriter log) => AudioOutputFactory.RecordableInputs(config, log);
}
