using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using TabForge.Audio;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: that a restarted audio engine (its synths are new, so every instrument is a piano) gets the song's programs again: the window sends
///     the setup once after the engine is ready and its chains are loaded, and the playback engine sends every channel's program whether it
///     plays or is stopped.
/// Does not own: the engine side (PianoAfterDeviceRestartTests), the synth (AudioSmokeTests).
/// Tests: AvaloniaEngineEvents.OnDeviceReady / OnChainLoaded / OnPluginCrashed, PlaybackEngine.RearmChannelSetup.
/// </summary>
public class EngineRestartResendsSetupTests
{
    private const int SettleMs = 600, StartWaitMs = 15000;

    [Fact]
    public void ReadyEngineAndLoadedChainsMakeTheShownSongSendItsSetupOnce()
    {
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        var (host, engine) = HostWithSong();
        using var events = new AvaloniaEngineEvents(client, host, new AvaloniaPlaybackSessionTests.FakeUiThread());

        Raise(client, "DeviceReady");
        Assert.Equal(1, engine.Rearms);
        Raise(client, "ChainLoaded");
        Raise(client, "ChainLoaded");
        Assert.Equal(1, engine.Rearms);   // not once per chain: a restart sends it once
    }

    [Fact]
    public void AFlappingDeviceSendsTheSetupOncePerBurst()
    {
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        var (host, engine) = HostWithSong();
        using var events = new AvaloniaEngineEvents(client, host, new AvaloniaPlaybackSessionTests.FakeUiThread());

        Raise(client, "DeviceReady");
        Raise(client, "DeviceReady");
        Raise(client, "DeviceReady");
        Raise(client, "ChainLoaded");
        Assert.Equal(1, engine.Rearms);
    }

    [Fact]
    public void ACrashedEngineSendsTheSetupAgainWhenItsChainsAreLoaded()
    {
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        var (host, engine) = HostWithSong();
        using var events = new AvaloniaEngineEvents(client, host, new AvaloniaPlaybackSessionTests.FakeUiThread());
        Raise(client, "DeviceReady");
        Assert.Equal(1, engine.Rearms);

        Raise(client, "PluginCrashed", "");
        Assert.Equal(1, engine.Rearms);   // the new synths do not exist yet
        Raise(client, "ChainLoaded");
        Assert.Equal(2, engine.Rearms);
    }

    [Fact]
    public void AStoppedSongSendsEveryChannelsProgramToAFreshSynth()
    {
        var song = FullDemoSongFactory.Create();
        var output = new ProgramRecorder();
        using var engine = new PlaybackEngine(output);
        engine.Start(song, new PlaybackOptions(), _ => { }, () => { });
        var played = output.WaitForPrograms();
        engine.Stop();
        Assert.Contains(played.Values, program => program != 0);   // the song really has other sounds than piano

        engine.RearmChannelSetup();

        Assert.Equal(played, output.Take());
    }

    [Fact]
    public void APlayingSongSendsEveryChannelsProgramToAFreshSynth()
    {
        var song = FullDemoSongFactory.Create();
        var output = new ProgramRecorder();
        using var engine = new PlaybackEngine(output);
        engine.Start(song, new PlaybackOptions(), _ => { }, () => { });
        var played = output.WaitForPrograms();
        Assert.Contains(played.Values, program => program != 0);

        engine.RearmChannelSetup();
        Thread.Sleep(SettleMs);
        var resent = output.Take();
        engine.Stop();

        Assert.Equal(played, resent);
    }

    private static (EventHost Host, AvaloniaPlaybackSessionTests.FakeEngine Engine) HostWithSong()
    {
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        var document = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => document.Project);
        return (new EventHost(document, session), engine);
    }

    // Raises one of the client's events from the test (the client's own handlers are what the controller attached to).
    private static void Raise(AudioEngineClient client, string eventName, params object[] args)
    {
        var handler = (Delegate?)typeof(AudioEngineClient).GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(client);
        handler?.DynamicInvoke(args);
    }

    /// <summary>The last program each (device, channel) was given.</summary>
    private sealed class ProgramRecorder : IMidiOutput
    {
        private readonly Dictionary<(int Device, int Channel), int> _programs = new();
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();

        public void Send(int deviceId, int status, int data1, int data2)
        {
            if ((status & 0xF0) == 0xC0) lock (_programs) _programs[(deviceId, status & 0x0F)] = data1;
        }

        /// <summary>The programs sent since the last call.</summary>
        public Dictionary<(int Device, int Channel), int> Take()
        {
            lock (_programs) { var copy = new Dictionary<(int Device, int Channel), int>(_programs); _programs.Clear(); return copy; }
        }

        /// <summary>The programs of the first setup sent (the start compiles the song first).</summary>
        public Dictionary<(int Device, int Channel), int> WaitForPrograms()
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < StartWaitMs)
            {
                Thread.Sleep(SettleMs / 4);
                lock (_programs) if (_programs.Count > 0) break;
            }
            Thread.Sleep(SettleMs / 4);   // the rest of the setup
            return Take();
        }

        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }

    private sealed class EventHost : IAvaloniaEngineEventHost
    {
        private readonly SongDocument _song;
        private readonly AvaloniaPlaybackSession _session;
        public EventHost(SongDocument song, AvaloniaPlaybackSession session) => (_song, _session) = (song, session);
        public IReadOnlyList<SongDocument> Songs => new[] { _song };
        public SongDocument Active => _song;
        public PluginSettings Plugins { get; } = new();
        public global::Avalonia.Controls.Window? Owner => null;
        public void SaveSettings() { }
        public void PanicAll() { }
        public void ShowStatus(string text) { }
        public void SyncActive() { }
        public AvaloniaPlaybackSession? SessionOf(SongDocument song) => ReferenceEquals(song, _song) ? _session : null;
    }
}
