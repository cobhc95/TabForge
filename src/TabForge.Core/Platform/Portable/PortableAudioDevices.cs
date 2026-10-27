using TabForge.Audio.Contracts;
using TabForge.Platform.Contracts;

namespace TabForge.Platform.Portable;

// Owns: the device lists Settings shows when the audio backend is miniaudio (output and recording device names), read from the tfportable library.
// Does not own: opening a device (the engine's MiniaudioBackend) or the Windows lists (Platform/Windows/WindowsAudioDevices.cs).
// Tests: PortableBackendTests (empty lists without the library; the null device with it).
/// <summary><see cref="IAudioDevices"/> over miniaudio. A context is created per question and released at once: lists are asked rarely and on a worker.</summary>
public sealed unsafe class PortableAudioDevices : IAudioDevices
{
    private readonly bool _nullDevice;

    /// <param name="nullDevice">True: miniaudio's null backend (tests).</param>
    public PortableAudioDevices(bool nullDevice = false) => _nullDevice = nullDevice;

    public IReadOnlyList<string> CaptureEndpointNames() => Names(capture: true);

    public IReadOnlyList<string> OutputNames(string driver) => Names(capture: false);

    public IReadOnlyList<string> AsioDriverNames() => Array.Empty<string>();

    /// <summary>miniaudio converts to the rate asked, so every usual rate is offered.</summary>
    public IReadOnlyList<int> SupportedSampleRates(string driver, string device) => AudioDriverNames.SampleRates;

    private IReadOnlyList<string> Names(bool capture)
    {
        if (TfPortable.Current is not { } lib) return Array.Empty<string>();
        var context = lib.AudioContextCreate(_nullDevice);
        if (context == 0) return Array.Empty<string>();
        try
        {
            var names = new List<string>();
            var count = lib.AudioDeviceCount(context, capture);
            for (var i = 0; i < count; i++)
                if (lib.AudioDeviceInfo(context, capture, i, out var name, out _, out _, out _)) names.Add(name);
            return names;
        }
        finally { lib.AudioContextDestroy(context); }
    }
}
