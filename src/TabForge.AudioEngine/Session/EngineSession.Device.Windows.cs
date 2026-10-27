using System.Diagnostics;
using NAudio.Wave;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Editors;
using TabForge.AudioEngine.Mixing;
using TabForge.AudioEngine.Output;
using TabForge.AudioEngine.Plugins;
using TabForge.AudioEngine.Synth;

namespace TabForge.AudioEngine;

// Owns: the engine session's output device: open, configure, reconfigure, ASIO input and the Windows volume follow, split from EngineSession.cs.
// Does not own: the other parts of the engine session (EngineSession).
internal sealed partial class EngineSession
{
    // ---- Device ----

    /// <summary>True when the open output is an ASIO driver (the Windows-only check, so the rest of the engine never names NAudio.Wave.AsioOut).</summary>
    private bool PlayerIsAsio => Player is NAudio.Wave.AsioOut;

    /// <summary>Opens the audio device on the current mix and starts it (also reopens it after an offline render).</summary>
    private (int LatencyMs, string Description) OpenOutput()
    {
        AssertMain();
        var (player, latencyMs, description) = AudioBackends.For(Config).OpenOutput(Config, Mix!, Threads);
        Player = player;
        _asioBufferFrames = 0;
        if (player is NAudio.Wave.AsioOut asioOut)
        {
            _asioBufferFrames = asioOut.FramesPerBuffer;
            PrepareAsioScratch(_asioBufferFrames, Math.Max(2, AudioOutputFactory.AsioInputChannelsUsed));
            asioOut.AudioAvailable += OnAsioInput;
        }
        Mix!.OutputLatencyTicks = latencyMs * Stopwatch.Frequency / 1000;
        Shared!.LatencyTicks = Mix.DelayTicks;
        Shared.SampleRate = SampleRate;
        var opened = player;
        _lossWatch = new OutputLossWatch(opened, Config.Driver == AudioOutputFactory.WasapiShared && string.IsNullOrWhiteSpace(Config.Device),
            why => Threads.Post(() => ReopenOutput(opened, why)));
        Mix.ResetClock();
        if (Config.PopGuard) (Mix.Fade ??= new OutputFade(SampleRate)).FadeIn(); else Mix.Fade = null;   // no callback runs here
        Player.Play();
        FollowWindowsVolume(Config.Driver == AudioOutputFactory.Asio && Config.FollowWindowsVolume);
        return (latencyMs, description);
    }

    private OutputLossWatch? _lossWatch;
    private const int ReopenAttempts = 3, ReopenPauseMs = 300;

    /// <summary>
    /// Engine main thread: the open output was lost (device unplugged or invalidated, default device changed, ASIO reset request). The
    /// device is configured again from the same settings, up to <see cref="ReopenAttempts"/> times; each failed attempt tells TabForge
    /// (DeviceError). The channel state the songs set is kept by the session, so the instruments come back with the sound.
    /// </summary>
    private void ReopenOutput(IWavePlayer lost, string why)
    {
        AssertMain();
        if (!ReferenceEquals(Player, lost)) return;   // already reopened or stopped meanwhile
        Log.Write($"output lost ({why}): reopening");
        TryReopen(1);
    }

    /// <summary>One reopen attempt; a failed one schedules the next after a pause on the engine thread (never blocking it), unless a newer Configure happened meanwhile.</summary>
    private void TryReopen(int attempt)
    {
        Configure(Config);
        if (Player is not null || attempt >= ReopenAttempts) return;
        var generation = _configureGeneration;
        Task.Delay(ReopenPauseMs).ContinueWith(_ => Threads.Post(() => { if (generation == _configureGeneration && Player is null) TryReopen(attempt + 1); }), TaskScheduler.Default);
    }

    private int _configureGeneration;

    public void Configure(EngineConfig config)
    {
        AssertMain();
        if (Defer(() => Configure(config))) return;
        _configureGeneration++;
        var oldBlock = Math.Clamp(Config.BufferSize, 16, 8192);
        var oldRate = SampleRate;
        Config = config = AudioBackends.Normalize(config, OperatingSystem.IsWindows());
        StopOutput();   // from here no audio callback runs until OpenOutput
        try
        {
            SampleRate = AudioBackends.For(config).SampleRateFor(config);
            var maxBlock = Math.Clamp(config.BufferSize, 16, 8192);
            // The live plug-ins follow the new rate / block size in place (they keep their settings); see ReconfigureLive.
            if (SampleRate != oldRate || maxBlock != oldBlock) ReconfigureLive(SampleRate, maxBlock);
            var previous = Loaded.Values.ToList();
            Volatile.Write(ref Mix, new MixEngine(Shared!, SampleRate, maxBlock, Threads, Transports, SynthOff, ChannelState));   // the reader thread reads it (Panic)
            Mix!.LiveLimiter = LiveLimiter;
            Mix.Tap = Tap;
            if (config.Driver == AudioOutputFactory.WasapiShared) Mix!.Ceiling = 8f;   // +18 dB: the Windows float mixer clips after its volume
            // The recording is the nominal mix on every driver (what File > Render writes), not the 1/ceiling level Windows plays at: the safety limiter keeps hot material under full scale.
            Tap.Gain = 1f;
            // Chains depend on the sample rate and block size (their buffers): rebuild every requested chain for the new engine.
            foreach (var (slot, request) in Requested.ToList()) BuildChain(slot, request.Track, request.UseMidiSynth, request.Specs);
            // The fresh mix never held the previous chains (SetChain returned null), and the old device is stopped: nothing can be
            // inside them, so they are disposed now (carried-over plug-ins are in their Kept sets; their clip players leave the disk thread).
            foreach (var chain in previous)
            {
                if (Loaded.ContainsValue(chain)) continue;   // not rebuilt (cannot happen for a requested slot): still in use
                foreach (var plugin in chain.Plugins) if (!chain.Kept.Contains(plugin)) Editors.CloseFor(plugin);
                try { chain.Dispose(); } catch (Exception ex) { Log.Write($"dispose failed: {ex.Message}"); }
            }
            if (Input is not null) { Input.Dispose(); Input = null; }
            var (latencyMs, description) = OpenOutput();
            ApplyArms();   // after the output: it tells an ASIO driver (which feeds the input itself) from a device that needs its own capture
            var bufferFrames = _asioBufferFrames > 0 ? _asioBufferFrames : Math.Clamp(config.BufferSize, 16, 8192);
            var channels = config.Driver == AudioOutputFactory.Asio ? AudioOutputFactory.AsioChannels : (Array.Empty<string>(), Array.Empty<string>()); var inputs = AudioBackends.For(config).RecordableInputs(config, Log);   // read before the event is written: Ready is never left half written
            Send(EngineEvent.Ready, w =>
            {
                w.Write(SampleRate); w.Write(latencyMs); w.WriteString(description); w.Write(bufferFrames);
                AsioChannelNames.Write(w, channels.Item1);
                AsioChannelNames.Write(w, channels.Item2); w.Write(inputs.Length); foreach (var n in inputs) w.WriteString(n);
                w.Write(EngineNames.ProtocolVersion);
            });
            Log.Write($"output started: {description}, {SampleRate} Hz, {latencyMs} ms");
        }
        catch (Exception ex)
        {
            Log.Write($"output failed: {ex}");
            Send(EngineEvent.DeviceError, w => w.WriteString(ex.GetBaseException().Message));
        }
    }

    /// <summary>
    /// Engine main thread: the breadcrumb around one plug-in call, with its kind (the watchdog allows each kind its own time). None for
    /// isolated plug-ins (their calls are bounded pipe waits; a hang there is reported through Crashed and costs only that plug-in) and
    /// for the built-in GM synth. Returns whether a breadcrumb was set (pass it to <see cref="LeaveCall"/>).
    /// </summary>
    private static bool EnterCall(int slot, int index, IPluginInstance? plugin, string path, PluginCallKind kind)
    {
        if (plugin is GmSynth || Shared is not { } shared) return false;
        // Isolated: marked as busy for a reason (so TabForge does not take the engine for deaf), never blamed.
        shared.EnterMain(slot, index, path, kind, blame: plugin is not Isolation.RemotePlugin);
        return true;
    }

    private static void LeaveCall(bool entered) { if (entered) Shared?.LeaveMain(); }

    /// <summary>
    /// Configure, after the old device stopped: every live instance (chain plug-ins, bypassed ones, GM synths) switches to the
    /// new rate and block size in place, keeping its live settings, before the chains are rebuilt around them.
    /// One that cannot follow stays loaded but silent and is reported.
    /// </summary>
    private void ReconfigureLive(int sampleRate, int maxBlock)
    {
        AssertMain();
        var done = new HashSet<IPluginInstance>(ReferenceEqualityComparer.Instance);
        void One(int slot, int index, IPluginInstance plugin)
        {
            if (!done.Add(plugin)) return;
            var entered = EnterCall(slot, index, plugin, plugin.Path, PluginCallKind.Load);
            try { plugin.Reconfigure(sampleRate, maxBlock); }
            catch (Exception ex)
            {
                Log.Write($"plug-in could not follow the device change: {plugin.Path}: {ex.GetBaseException().Message}");
                Send(EngineEvent.PluginFailed, w => { w.Write(slot); w.Write(index); w.WriteString(plugin.Path); w.WriteString($"It could not switch to {sampleRate} Hz / {maxBlock} frames: {ex.GetBaseException().Message}"); });
            }
            finally { LeaveCall(entered); }
        }
        foreach (var (slot, chain) in Loaded)
        {
            foreach (var e in chain.Effects) One(slot, e.Index, e.Plugin);
            if (chain.MidiSynth is { } synth) One(slot, -1, synth);
        }
        foreach (var (slot, parked) in Parked) foreach (var p in parked.Values) One(slot, -1, p);
        Log.Write($"device change: {done.Count} live plug-in(s) reconfigured to {sampleRate} Hz / {maxBlock} frames");
    }

    private string AsioInputLabel()
    {
        var names = AudioOutputFactory.AsioChannels.Inputs;
        var first = Math.Clamp(Config.AsioInput, 0, Math.Max(0, names.Length - 1));
        return names.Length == 0 ? "ASIO input" : AudioOutputFactory.AsioInputChannelsUsed > 1 && first + 1 < names.Length ? $"{names[first]} + {names[first + 1]}" : names[first];
    }

    private int _asioBufferFrames;
    private float[] _asioScratch = new float[4096];

    /// <summary>Engine thread, when an ASIO driver opens: room for the largest input block it can deliver.</summary>
    private void PrepareAsioScratch(int bufferFrames, int inputChannels)
    {
        var needed = Math.Max(bufferFrames, 8192) * Math.Max(32, inputChannels);
        if (_asioScratch.Length < needed) _asioScratch = new float[needed];
    }

    /// <summary>ASIO driver thread (real time: no allocation, no lock, no main-thread check): the block of input for the channel pair; copied into the input ring.</summary>
    private void OnAsioInput(object? sender, NAudio.Wave.AsioAudioAvailableEventArgs e)
    {
        var input = Input;
        if (input is null) return;
        var needed = e.SamplesPerBuffer * Math.Max(1, e.InputBuffers.Length);
        // Sized when the driver opens (PrepareAsioScratch); a driver that changes its buffer mid-stream is skipped
        // for that block rather than allocating on its real-time thread.
        if (_asioScratch.Length < needed || e.InputBuffers.Length != input.FeedChannels) return;   // also a capture built for another channel count: never fed
        var written = e.GetAsInterleavedSamples(_asioScratch);
        input.Feed(_asioScratch.AsSpan(0, written));
    }

    private NAudio.CoreAudioApi.MMDevice? _volumeDevice;

    /// <summary>True while the Windows volume is followed (the main loop then refreshes the Volume Mixer slider about once a second).</summary>
    public bool FollowingWindowsVolume => _volumeDevice is not null;

    /// <summary>
    /// ASIO does not pass through the Windows mixer, so the Windows volume keys do nothing to it. When asked, the Windows
    /// master volume (and mute) of the default playback device becomes a software gain on the engine's output.
    /// </summary>
    private void FollowWindowsVolume(bool on)
    {
        AssertMain();
        try
        {
            if (_volumeDevice is not null)
            {
                _volumeDevice.AudioEndpointVolume.OnVolumeNotification -= OnWindowsVolume;
                _volumeDevice.Dispose();
                _volumeDevice = null;
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { }
        _endpointGain = 1f; _sessionGain = 1f; _following = false;
        if (Mix is not null) Mix.MasterGain = 1f;
        if (!on || Mix is null) return;
        try
        {
            _volumeDevice = new NAudio.CoreAudioApi.MMDeviceEnumerator().GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
            var endpoint = _volumeDevice.AudioEndpointVolume;
            endpoint.OnVolumeNotification += OnWindowsVolume;
            _endpointGain = EndpointGain(endpoint.MasterVolumeLevel, endpoint.Mute);
            _following = true;
            ApplyFollowedGain();
            RefreshSessionVolume();
            Log.Write($"following the Windows volume of {_volumeDevice.FriendlyName}: {endpoint.MasterVolumeLevel:0.00} dB (scalar {endpoint.MasterVolumeLevelScalar:0.000}), gain {_endpointGain:0.000}, path offset {20 * Math.Log10(_pathOffsetGain):0.00} dB");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            Log.Write($"windows volume not available: {ex.Message}");
        }
    }

    private float _endpointGain = 1f, _sessionGain = 1f, _pathOffsetGain = 1f;
    private bool _following;

    /// <summary>
    /// ASIO bypasses the Windows mixer entirely, so both the device's master volume and TabForge's own slider in the
    /// Volume Mixer (the session the built-in Windows MIDI synth plays in) are applied here; otherwise the engine on ASIO
    /// plays far louder than the Windows MIDI tracks whenever TabForge's app slider is below the master.
    /// Main thread, or the COM notification thread (<see cref="OnWindowsVolume"/>).
    /// </summary>
    private void ApplyFollowedGain()
    {
        if (Mix is { } mix && _following) mix.MasterGain = _endpointGain * _sessionGain * _pathOffsetGain;
    }

    /// <summary>Engine thread, about once a second while following: TabForge's app volume in the Windows Volume Mixer.</summary>
    public void RefreshSessionVolume()
    {
        AssertMain();
        if (_volumeDevice is null || UiProcessId == 0) return;
        try
        {
            var manager = _volumeDevice.AudioSessionManager;
            manager.RefreshSessions();
            var sessions = manager.Sessions;
            float? found = null;
            for (var i = 0; i < sessions.Count; i++)
            {
                using var session = sessions[i];
                if ((int)session.GetProcessID != UiProcessId) continue;
                var volume = session.SimpleAudioVolume;
                found = Math.Min(found ?? 1f, SessionGain(volume.Volume, volume.Mute));
            }
            var gain = found ?? 1f;   // TabForge has no session yet (nothing played through Windows): nothing to follow
            if (Math.Abs(gain - _sessionGain) > 1e-4f)
            {
                _sessionGain = gain;
                ApplyFollowedGain();
                Log.Write($"following TabForge's Volume Mixer slider: gain {gain:0.000} (total {_endpointGain * _sessionGain:0.000})");
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException) { }
    }

    /// <summary>
    /// The endpoint volume Windows really applies is its MasterVolumeLevel in dB (measured on an Audient iD4: loopback level
    /// tracks the reported dB exactly from 0 to -35 dB; the old scalar² curve was up to 5 dB off). The scalar is only the slider.
    /// </summary>
    private static float EndpointGain(float db, bool muted) => muted ? 0f : Gain.FromDb(Math.Clamp(db, -96f, 0f));

    /// <summary>A Volume Mixer session slider is applied as scalar² (measured: 0.5 = -12.04 dB, 0.25 = -24.08 dB).</summary>
    private static float SessionGain(float scalar, bool muted) => muted ? 0f : Math.Clamp(scalar, 0f, 1f) * Math.Clamp(scalar, 0f, 1f);

    /// <summary>COM notification thread.</summary>
    private void OnWindowsVolume(NAudio.CoreAudioApi.AudioVolumeNotificationData data)
    {
        float db;
        try { db = _volumeDevice?.AudioEndpointVolume.MasterVolumeLevel ?? 0f; }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { return; }
        _endpointGain = EndpointGain(db, data.Muted);
        ApplyFollowedGain();
    }

    public void StopOutput()
    {
        AssertMain();
        if (Player is not null && Config.PopGuard) Mix?.Fade?.FadeOutAndDrain((int)(Mix.OutputLatencyTicks * 1000 / Stopwatch.Frequency));   // bounded: silence reaches the device and what it buffered plays out before it stops
        FollowWindowsVolume(false);
        _lossWatch?.Dispose();
        _lossWatch = null;
        if (Player is NAudio.Wave.AsioOut asioOut) asioOut.AudioAvailable -= OnAsioInput;
        try { Player?.Stop(); Player?.Dispose(); }
        catch (Exception ex) { Log.Write($"output stop failed: {ex.Message}"); }
        Player = null;
    }

    /// <summary>What each ASIO driver offered when first asked (key: installed driver name). Ceiling: the installed ASIO drivers. Thread: engine main thread.</summary>
    private readonly Dictionary<string, AsioOffer> _asioOffers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Engine main thread: answers a settings question about an ASIO driver from the open driver, or by opening it briefly (see <see cref="AsioProbe"/>).</summary>
    public void ProbeAsio(int requestId, string driver)
    {
        AssertMain();
        var held = Config.Driver == AudioOutputFactory.Asio ? Player as NAudio.Wave.AsioOut : null;
        var answer = AsioProbe.Answer(requestId, driver, () => held is null ? null : AudioOutputFactory.ResolveAsioName(Config.Device), AudioOutputFactory.ResolveAsioName, () => AsioProbe.Read(held!, Log), name => AsioProbe.OpenBriefly(name, Log), _asioOffers, Log);
        Send(EngineEvent.AsioProbed, answer.Write);
    }

    /// <summary>Stopwatch timestamp when the ASIO control panel that blocks the main thread opened (0: none). The engine watchdog waits for it only up to <see cref="EngineWatchdog.PanelCapSec"/>.</summary>
    public long PanelOpenedTicks;

    /// <summary>Engine main thread: shows the control panel of an ASIO driver, of the open driver or of one opened briefly (see <see cref="AsioProbe.ShowPanel"/>).</summary>
    public void ShowAsioPanel(int requestId, string driver)
    {
        AssertMain();
        var held = Config.Driver == AudioOutputFactory.Asio ? Player as NAudio.Wave.AsioOut : null;
        Volatile.Write(ref PanelOpenedTicks, Stopwatch.GetTimestamp());
        AsioPanelShownMessage answer;
        try { answer = AsioProbe.ShowPanel(requestId, driver, held is null ? null : AudioOutputFactory.ResolveAsioName(Config.Device), AudioOutputFactory.ResolveAsioName, () => held!.ShowControlPanel(), AsioProbe.ShowBriefly, Log); }
        finally { Volatile.Write(ref PanelOpenedTicks, 0); }
        Send(EngineEvent.AsioPanelShown, answer.Write);
    }
}
