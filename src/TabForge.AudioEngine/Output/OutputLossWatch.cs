using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace TabForge.AudioEngine.Output;

// Owns: telling "this output is gone or must be reopened" from one open output: a stop with an error (WASAPI device invalidated or unplugged),
//     an ASIO driver reset request, and a change of the Windows default playback device the output follows.
// Does not own: reopening it (EngineSession.Device.cs) or opening it (AudioOutputFactory).
// Threads: raises on the driver's or the COM notification thread; the owner posts the reaction to the engine main thread.

/// <summary>Watches one open output and raises <c>lost</c> once per signal with the reason; silent after <see cref="Dispose"/>.</summary>
internal sealed class OutputLossWatch : IMMNotificationClient, IDisposable
{
    private readonly Action<string> _lost;
    private readonly MMDeviceEnumerator? _enumerator;
    private readonly IWavePlayer _player;
    private readonly EventHandler<StoppedEventArgs> _onStopped;
    private readonly EventHandler? _onReset;
    private const long CooldownMs = 1000;   // a storm or a flapping device raises one loss, not one per signal
    private long _lastRaisedMs = long.MinValue / 2;
    private volatile bool _closed;

    /// <param name="followsDefaultDevice">True when the output plays on whatever the default playback device is.</param>
    public OutputLossWatch(IWavePlayer player, bool followsDefaultDevice, Action<string> lost)
    {
        _lost = lost;
        _player = player;
        _onStopped = (_, e) => { if (e.Exception is { } ex) Raise($"output stopped: {ex.GetBaseException().Message}"); };
        player.PlaybackStopped += _onStopped;
        if (player is AsioOut asio) { _onReset = (_, _) => Raise("the ASIO driver asked for a reset"); asio.DriverResetRequest += _onReset; }
        else if (player is IResetRequestingOutput resettable) { _onReset = (_, _) => Raise("the output asked for a reset"); resettable.ResetRequested += _onReset; }
        if (!followsDefaultDevice) return;
        _enumerator = new MMDeviceEnumerator();
        _enumerator.RegisterEndpointNotificationCallback(this);
    }

    private void Raise(string why)
    {
        if (_closed) return;
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastRaisedMs);
        if (now - last < CooldownMs || Interlocked.CompareExchange(ref _lastRaisedMs, now, last) != last) return;
        _lost(why);
    }

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia) Raise("the default playback device changed");
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        _closed = true;
        _player.PlaybackStopped -= _onStopped;
        if (_onReset is not null)
        {
            if (_player is AsioOut asio) asio.DriverResetRequest -= _onReset;
            else if (_player is IResetRequestingOutput resettable) resettable.ResetRequested -= _onReset;
        }
        if (_enumerator is null) return;
        try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch (System.Runtime.InteropServices.COMException) { } // swallow: best-effort unregister of a callback on teardown
        _enumerator.Dispose();
    }
}
