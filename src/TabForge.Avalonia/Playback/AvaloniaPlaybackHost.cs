using System;
using TabForge.Audio;
using TabForge.Avalonia.Transport;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Playback;

namespace TabForge.Avalonia.Playback;

// Owns: the process's playback parts for the Avalonia app: the one engine client, the one winmm MIDI output and the per-document sessions
// whose engines route through them (the same routing the app makes per document).
// Does not own: the adapters' construction (AvaloniaPlaybackComposition), the engine's I/O (AudioEngineClient), the transport (AvaloniaPlaybackSession).
// Tests: AvaloniaPlaybackHostTests.
// Threads: UI thread only. Windows only.
public sealed class AvaloniaPlaybackHost : IDisposable
{
    private readonly PluginTrust _trust = new();   // the app's ONE trust state: the engine gate, the sessions and every window share it

    private readonly AudioEngineClient _client;
    private readonly IMidiOutput _midi = AvaloniaPlaybackComposition.CreateMidiOutput();
    // One preferences object for every engine of the app (metronome boost, count-in sound, section count-in and loop behaviour).
    private readonly PlaybackPreferences _preferences = new();

    public AvaloniaPlaybackHost(IUiThread ui)
    {
        _client = AvaloniaPlaybackComposition.CreateEngineClient(_trust);
        Sessions = new AvaloniaPlaybackSessions(ui, () => new PlaybackEngine(new RoutedMidiOutput(new SharedMidiOutput(_midi), _client), _preferences)) { Engine = _client, Trust = _trust };
    }

    public AvaloniaPlaybackSessions Sessions { get; }

    /// <summary>The app's one engine client (Settings asks it what an ASIO driver offers).</summary>
    public AudioEngineClient Engine => _client;

    /// <summary>The app's one trust state (handed to AppServices at composition).</summary>
    public PluginTrust Trust => _trust;

    /// <summary>Stops every session, then the engine client and the MIDI device.</summary>
    public void Dispose()
    {
        Sessions.Dispose();
        _client.Dispose();
        _midi.Dispose();
    }
}
