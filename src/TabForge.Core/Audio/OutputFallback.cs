using TabForge.Audio.Contracts;

namespace TabForge.Audio;

// Owns: this session's fallback from a saved output that would not open (ASIO driver unplugged or busy) to WASAPI (shared).
// Does not own: the settings: the saved driver, device, rate and buffer are never touched, so the next start tries them again.
// Tests: TabForge.Avalonia.Tests.OutputFallbackTests.
// Thread: the client's sync (UI) and event (reader) threads; guarded by a lock. State: one failed output per client, gone with it.
public sealed class OutputFallback
{
    private readonly object _gate = new();
    private (string Driver, string Device)? _failed;

    /// <summary>The config to open: <paramref name="wanted"/>, or WASAPI (shared) on the default device while the wanted output is the one that failed this session.</summary>
    public EngineConfig Apply(EngineConfig wanted)
    {
        lock (_gate)
            return _failed is { } f && f.Driver == wanted.Driver && f.Device == wanted.Device
                ? wanted with { Driver = AudioDriverNames.WasapiShared, Device = "", SampleRate = 0 } : wanted;
    }

    /// <summary>The open of <paramref name="attempted"/> failed: the notice to show when this session now falls back to WASAPI (shared), else null (already on it, or already fell back once).</summary>
    public string? Failed(EngineConfig attempted)
    {
        lock (_gate)
        {
            if (attempted.Driver == AudioDriverNames.WasapiShared || _failed is not null) return null;
            _failed = (attempted.Driver, attempted.Device);
            var name = attempted.Device.Length > 0 ? $"{attempted.Driver} device {attempted.Device}" : $"{attempted.Driver} device";
            return $"{name} not available, using WASAPI";
        }
    }
}
