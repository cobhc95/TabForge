// Owns: the status bar audio device state: whether the device button shows, and its text and tooltip for the running output or the idle ASIO device.
// Does not own: the engine (AudioEngineClient), the settings (AppSettings), the device enumeration (the caller passes FirstDeviceOfDriver), or the status bar controls (MainWindow.Clips.cs).
// Tests: no dedicated test class.
using TabForge.Audio.Contracts;

namespace TabForge.Presenters;

/// <summary>
/// The audio device facts the status bar reads: the engine's running output (null when the engine is not running), the configured driver,
/// device and sample rate, and a lookup of the first device a driver lists (called only while the device is idle and not configured).
/// </summary>
public sealed record AudioDeviceStatusInput(
    bool EngineRunning,
    (string Driver, string Device, int Rate, int Buffer, int LatencyMs)? RunningOutput,
    string ConfiguredDriver,
    string ConfiguredDevice,
    int ConfiguredSampleRate,
    Func<string, string?> FirstDeviceOfDriver);

/// <summary>
/// What the status bar shows. When <see cref="DeviceButtonVisible"/> is false the output label shows instead and <see cref="Text"/> and
/// <see cref="Tooltip"/> are null.
/// </summary>
public sealed record AudioDeviceStatus(bool DeviceButtonVisible, string? Text, string? Tooltip);

/// <summary>
/// Audio device status bar texts. Thread: UI thread only (the engine's status event is raised on the UI thread).
/// </summary>
public static class AudioDeviceStatusPresenter
{
    private const string InUseTooltip = "Audio device in use · click for Settings > Audio & Plug-ins (driver, device, sample rate, ASIO channels)";
    private const string IdleAsioTooltip = "ASIO is selected. It starts with the first track that uses plug-ins, audio clips or recording. Click for Settings > Audio & Plug-ins.";

    /// <summary>The status for the current audio state.</summary>
    public static AudioDeviceStatus Decide(AudioDeviceStatusInput input)
    {
        var running = input.EngineRunning ? input.RunningOutput : null;
        var asioChosen = input.ConfiguredDriver == AudioDriverNames.Asio;
        // The engine only runs while a track needs it; a chosen ASIO driver is still shown.
        if (running is null && !asioChosen) return new AudioDeviceStatus(false, null, null);
        if (running is { } output) return new AudioDeviceStatus(true, RunningText(output), InUseTooltip);
        return new AudioDeviceStatus(true, IdleText(input), IdleAsioTooltip);
    }

    private static string RunningText((string Driver, string Device, int Rate, int Buffer, int LatencyMs) output)
    {
        var label = output.Device.StartsWith(output.Driver, StringComparison.OrdinalIgnoreCase) ? output.Device : $"{output.Driver}: {output.Device}";
        return $"{label} ({output.LatencyMs} ms · {output.Rate / 1000.0:0.#} kHz · {output.Buffer} smp)";
    }

    private static string IdleText(AudioDeviceStatusInput input)
    {
        var name = input.ConfiguredDevice.Length > 0 ? input.ConfiguredDevice : (input.FirstDeviceOfDriver(input.ConfiguredDriver) ?? "no driver found");
        return $"ASIO: {name} (idle · {input.ConfiguredSampleRate / 1000.0:0.#} kHz)";
    }
}
