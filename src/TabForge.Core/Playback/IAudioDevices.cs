namespace TabForge.Playback;

/// <summary>
/// The audio device listing calls Settings needs: capture endpoints, output devices per driver, and the ASIO control panel.
/// Owns: the set of device calls and their result shapes. Does not own: the fallbacks, the channel cache or the running-engine answer,
/// the Windows calls (the adapter is Core/Platform/Windows/WindowsAudioDevices.cs), or the real-time
/// render path (the audio engine process opens the device itself).
/// Tests: none dedicated(the adapter's answers equal the direct NAudio calls).
/// </summary>
public interface IAudioDevices
{
    /// <summary>The active recording endpoints' friendly names.</summary>
    IReadOnlyList<string> CaptureEndpointNames();

    /// <summary>The output device names for a driver kind (<c>AudioDriverNames</c>): ASIO driver names, DirectSound descriptions or active render endpoints.</summary>
    IReadOnlyList<string> OutputNames(string driver);

    /// <summary>The installed ASIO driver names.</summary>
    IReadOnlyList<string> AsioDriverNames();

    /// <summary>The rates in <c>AudioDrivers.SampleRates</c> an output supports: the WASAPI mix rate (exclusive: the ones the device accepts), DirectSound's usual rates. ASIO is not answered here (none): only the engine process opens an ASIO driver, see AudioEngineClient.ProbeAsioAsync (and ShowAsioPanelAsync for its control panel). Errors propagate.</summary>
    IReadOnlyList<int> SupportedSampleRates(string driver, string device);
}
