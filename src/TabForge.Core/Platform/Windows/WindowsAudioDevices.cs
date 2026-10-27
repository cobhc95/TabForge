using TabForge.Platform.Contracts;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using TabForge.Audio.Contracts;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge.Platform.Windows;

// Owns: the NAudio device listing calls of the app (MMDeviceEnumerator, DirectSoundOut.Devices, AsioOut driver names).
// Does not own: the fallbacks, the channel cache or the running-engine answer, or the real-time render path (the engine process).
// Tests: none dedicated(compares the listing answers with the direct NAudio calls).
/// <summary><see cref="IAudioDevices"/> over NAudio, one device call per member and no caching. Errors propagate; the caller decides the fallback.</summary>
public sealed class WindowsAudioDevices : IAudioDevices
{
    public IReadOnlyList<string> CaptureEndpointNames() => EndpointNames(DataFlow.Capture);

    public IReadOnlyList<string> OutputNames(string driver) => driver switch
    {
        AudioDriverNames.Asio => AsioOut.GetDriverNames(),
        AudioDriverNames.DirectSound => DirectSoundOut.Devices.Select(d => d.Description).ToList(),
        _ => EndpointNames(DataFlow.Render),
    };

    public IReadOnlyList<string> AsioDriverNames() => AsioOut.GetDriverNames();

    public IReadOnlyList<int> SupportedSampleRates(string driver, string device)
    {
        if (driver == AudioDriverNames.Asio) return Array.Empty<int>();   // only the engine process opens ASIO: AudioEngineClient.ProbeAsioAsync
        if (driver == AudioDriverNames.DirectSound) return OutputRates.Supported(driver, _ => true, () => 0);
        using var enumerator = new MMDeviceEnumerator();
        var endpoint = device.Length > 0
            ? enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).FirstOrDefault(d => string.Equals(d.FriendlyName, device, StringComparison.OrdinalIgnoreCase))
            : null;
        endpoint ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        return OutputRates.Supported(driver, rate => endpoint.AudioClient.IsFormatSupported(AudioClientShareMode.Exclusive, WaveFormat.CreateIeeeFloatWaveFormat(rate, 2)),
            () => endpoint.AudioClient.MixFormat.SampleRate);
    }

    private static IReadOnlyList<string> EndpointNames(DataFlow flow) =>
        new MMDeviceEnumerator().EnumerateAudioEndPoints(flow, DeviceState.Active).Select(d => d.FriendlyName).ToList();
}
