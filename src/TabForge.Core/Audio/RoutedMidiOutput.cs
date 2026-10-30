using System.Diagnostics;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Playback;

namespace TabForge.Audio;

// Owns: one document's MIDI output (engine vs Windows MIDI routing, latency holdback).
// Does not own: the engine (AudioEngineClient) or the scheduler clock (PlaybackEngine).
// Tests: AudioSmokeTests, PlaybackStartHoldTests.

/// <summary>
/// One document's MIDI output. Channels whose track plays through plug-ins are sent to the audio engine (shared
/// ring, time-stamped); everything else goes to Windows MIDI exactly as before. While the engine is in use,
/// Windows MIDI is held back by the engine's latency so both stay in time (a small delay thread, idle otherwise).
/// With no plug-in tracks and "Play the whole song through the audio engine" switched off, this is a straight
/// pass-through: no delay, no thread, no engine. That setting is on by default, so normally the engine is in use.
/// </summary>
public sealed partial class RoutedMidiOutput : IMidiOutput
{
    private readonly IMidiOutput _inner;
    private readonly AudioEngineClient _engine;
    private volatile int[] _routes = Enumerable.Repeat(-1, 16).ToArray();
    private DelayLine? _delay;

    public RoutedMidiOutput(IMidiOutput inner, AudioEngineClient engine)
    {
        _inner = inner;
        _engine = engine;
    }

    private volatile bool _engineInUse;

    private static long _windowsMidiTicks = 60 * Stopwatch.Frequency / 1000;

    /// <summary>The Windows MIDI synth's own output latency in ms (setting "Windows MIDI latency"); live, shared by all documents.</summary>
    public static int WindowsMidiLatencyMs
    {
        get => (int)(Volatile.Read(ref _windowsMidiTicks) * 1000 / Stopwatch.Frequency);
        set => Volatile.Write(ref _windowsMidiTicks, Math.Clamp(value, 0, 600) * Stopwatch.Frequency / 1000);
    }

    private static long WindowsMidiTicks => Volatile.Read(ref _windowsMidiTicks);

    /// <summary>
    /// How long each path is held back so plug-in (engine) and Windows MIDI notes for the same beat are heard together: the engine hears a
    /// message <paramref name="engineLatencyTicks"/> after its stamp, the Windows synth <paramref name="windowsLatencyTicks"/> after the send;
    /// the faster path waits for the slower one. Everything on the engine (<paramref name="playAll"/>): nothing on Windows MIDI to wait for.
    /// </summary>
    internal static (long EngineHoldTicks, long WindowsHoldTicks) Compensation(long engineLatencyTicks, long windowsLatencyTicks, bool playAll)
    {
        var net = playAll ? 0 : engineLatencyTicks - windowsLatencyTicks;
        return (net < 0 ? -net : 0, net > 0 ? net : 0);
    }

    /// <summary>
    /// Channel → engine slot (-1 = Windows MIDI). <paramref name="engineInUse"/>: the song has engine audio (clips,
    /// input or plug-ins), so Windows MIDI is held back by the engine latency to stay in time with it.
    /// </summary>
    public void SetRoutes(int[] routes, bool engineInUse = false)
    {
        _engineInUse = engineInUse;
        if (routes.Length != 16) throw new ArgumentException("16 channels expected.", nameof(routes));
        var previous = _routes;
        _routes = (int[])routes.Clone();
        if (!routes.Any(r => r >= 0)) PanicOwnSlots(previous); // let plug-in notes end cleanly
    }

    /// <summary>All notes off on this document's engine slots only: other open documents playing at the same time keep sounding.</summary>
    private void PanicOwnSlots(int[] routes)
    {
        // An ordered marker in the note ring, not a pipe command: a pipe panic lands whenever the engine's reader thread gets to it,
        // which can be after the note a seek sends right behind the reset, and then silences or drops that first note.
        var stamp = Stopwatch.GetTimestamp() + Compensation(_engine.LatencyTicks, WindowsMidiTicks, _engine.Mixer.PlayAllThroughEngine).EngineHoldTicks;
        foreach (var slot in routes.Where(r => r >= 0).Distinct())
            _engine.Write(new TimedMidi { Timestamp = stamp, Slot = slot, Status = 0xB0, Data1 = 123, Flags = TimedMidi.PanicFlag });
    }

    /// <summary>
    /// Wall-clock ms from a scheduler send to hearing it: the engine's output latency, the Windows MIDI synth's, or
    /// (both in use, each held back to the slower) their maximum. The visual playhead is drawn this far behind.
    /// </summary>
    public double AudibleLatencyMs
    {
        get
        {
            var running = _engine.IsRunning;
            var engineMs = running ? _engine.LatencyTicks * 1000.0 / Stopwatch.Frequency : 0;
            if (running && _engine.Mixer.PlayAllThroughEngine) return engineMs;
            var engineInvolved = running && (_engineInUse || _routes.Any(r => r >= 0));
            return engineInvolved ? Math.Max(engineMs, WindowsMidiLatencyMs) : WindowsMidiLatencyMs;
        }
    }

    public IReadOnlyList<MidiOutputDeviceInfo> Devices => _inner.Devices;

    /// <summary>Sound engine None: the built-in synth stays silent, so track messages bound for the default output (the Windows MIDI synth) are dropped. Anything else (an external MIDI output, a VST instrument's engine slot, the metronome) is untouched.</summary>
    private bool Silenced(int deviceId, int slot, bool metronome) =>
        !metronome && slot < 0 && deviceId == -1 && _engine.Mixer.SoundNone;

    public void Send(int deviceId, int status, int data1, int data2) => Send(deviceId, status, data1, data2, metronome: false);

    private void Send(int deviceId, int status, int data1, int data2, bool metronome)
    {
        var routes = _routes;
        var channel = status & 0x0F;
        if (status < 0xF0 && routes[channel] is var slot && slot >= 0 && _engine.IsRunning)
        {
            WriteToEngine(slot, status, data1, data2);
            return;
        }
        if (Silenced(deviceId, routes[channel], metronome)) return;
        SendWindows(deviceId, status, data1, data2);
    }

    /// <summary>
    /// A metronome or count-in click: the click's own engine slot (<see cref="AudioEngineClient.Click"/>), never a song track's route, so no
    /// track's sound source, FX, mute, solo, volume or pan applies. Without that slot (engine not running, or no free slot) it takes the ordinary route.
    /// </summary>
    public void SendMetronome(int deviceId, int status, int data1, int data2)
    {
        if (status < 0xF0 && _engine.SlotOf(_engine.Click.Track) is var clickSlot and >= 0)
        {
            WriteToEngine(clickSlot, status, data1, data2);
            return;
        }
        Send(deviceId, status, data1, data2, metronome: true);   // the click is exempt from SoundNone: its fallback is the Windows MIDI synth
    }

    /// <summary>One channel message to an engine slot, stamped with the engine's latency compensation.</summary>
    private void WriteToEngine(int slot, int status, int data1, int data2)
    {
        // The engine schedules by timestamp, so a later stamp holds the event back (net < 0: Windows MIDI is the slower path).
        var engineHold = Compensation(_engine.LatencyTicks, WindowsMidiTicks, _engine.Mixer.PlayAllThroughEngine).EngineHoldTicks;
        _engine.Write(new TimedMidi
        {
            Timestamp = Stopwatch.GetTimestamp() + engineHold, Slot = slot,
            Status = (byte)status, Data1 = (byte)data1, Data2 = (byte)data2
        });
    }

    private void SendWindows(int deviceId, int status, int data1, int data2)
    {
        var routes = _routes;
        var delay = _engine.IsRunning && (_engineInUse || routes.Any(r => r >= 0)) ? _engine.LatencyTicks - WindowsMidiTicks : 0;
        if (delay <= 0) { _inner.Send(deviceId, status, data1, data2); return; }
        (_delay ??= new DelayLine(_inner)).Push(Stopwatch.GetTimestamp() + delay, deviceId, status, data1, data2);
    }

    /// <summary>Live input: straight to the device, or to the engine stamped "already due" so it plays in the next block.</summary>
    public void SendLive(int deviceId, int status, int data1, int data2)
    {
        if (!SendLiveEngineOnly(status, data1, data2))
        {
            if (status < 0xF0 && _engine.Mixer.SoundNone && deviceId == -1) return;   // the monitored GM track is silent in None; the click never comes through here
            _inner.Send(deviceId, status, data1, data2);
        }
    }

    /// <summary>Live input to the channel's engine slot only; false (nothing sent) when the channel is not routed or the engine is not running.</summary>
    public bool SendLiveEngineOnly(int status, int data1, int data2)
    {
        var channel = status & 0x0F;
        if (status >= 0xF0 || _routes[channel] is not (var slot and >= 0) || !_engine.IsRunning) return false;
        _engine.Write(new TimedMidi
        {
            Timestamp = Stopwatch.GetTimestamp() - _engine.LatencyTicks, Slot = slot,
            Status = (byte)status, Data1 = (byte)data1, Data2 = (byte)data2
        });
        return true;
    }

    public void ResetAll()
    {
        _delay?.Clear();
        _inner.ResetAll();
        PanicOwnSlots(_routes);
        if (_engine.SlotOf(_engine.Click.Track) is var clickSlot and >= 0)
            _engine.Write(new TimedMidi { Timestamp = Stopwatch.GetTimestamp() + Compensation(_engine.LatencyTicks, WindowsMidiTicks, _engine.Mixer.PlayAllThroughEngine).EngineHoldTicks, Slot = clickSlot, Status = 0xB0, Data1 = 123, Flags = TimedMidi.PanicFlag });
    }

    public void Close()
    {
        _delay?.Dispose();
        _delay = null;
        _inner.Close();
    }

    /// <summary>The document that used this output is gone for good: its engine chains are unloaded now, not parked.</summary>
    public void ReleaseEngineOwner(object owner) => _engine.ReleaseOwner(owner);

    public void Dispose() => Close();

    /// <summary>Holds Windows MIDI messages until their due time (ordered, one small thread).</summary>
    internal sealed class DelayLine : IDisposable
    {
        /// <summary>Bound for held messages (matches the engine ring): past it the oldest message is dropped and counted.</summary>
        internal const int MaxQueued = 8192;
        private readonly IMidiOutput _output;
        private readonly Queue<(long Due, int Device, int Status, int D1, int D2)> _queue = new();
        private readonly AutoResetEvent _signal = new(false);
        private readonly Thread _thread;
        private volatile bool _stop;
        private int _clearVersion; // guarded by _queue
        private long _dropped; // guarded by _queue

        public DelayLine(IMidiOutput output)
        {
            _output = output;
            _thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "TabForge MIDI delay" };
            _thread.Start();
        }

        // Messages arrive in time order with the same delay, so a FIFO stays sorted. Bounded: the oldest goes when full.
        public void Push(long due, int device, int status, int d1, int d2)
        {
            lock (_queue)
            {
                if (_queue.Count >= MaxQueued) { _queue.Dequeue(); _dropped++; }
                _queue.Enqueue((due, device, status, d1, d2));
            }
            _signal.Set();
        }

        /// <summary>Messages dropped because the holdback queue was full (diagnostics).</summary>
        public long Dropped { get { lock (_queue) return _dropped; } }

        // A Clear while the thread waits on its peeked head must not let it dequeue (and so lose) a message pushed after the Clear.
        public void Clear() { lock (_queue) { _queue.Clear(); _clearVersion++; } }

        private void Run()
        {
            // RT-10 / D10: 1 ms timer resolution (the shared, reference-counted NativeTimer) only while messages flow, so the coarse wait
            // below is not up to ~15 ms late; given back after a second with nothing queued (an idle document costs no battery).
            var timer = new TabForge.Audio.Contracts.NativeTimer.Hold();
            var idleSince = Stopwatch.GetTimestamp();
            try
            {
                // The final approach to a due time is a busy spin (sub-millisecond delivery). A high-resolution timer wakes the thread
                // close enough that the spin is a fraction of a millisecond; without one, the 1 ms tick needs a 3 ms spin.
                using var wake = HighResolutionTimer.TryCreate();
                var waits = wake is null ? null : new WaitHandle[] { _signal, wake };
                var spinTicks = wake is null ? Stopwatch.Frequency * 3 / 1000 : Stopwatch.Frequency * 3 / 10000;
                while (!_stop)
                {
                    (long Due, int Device, int Status, int D1, int D2) next;
                    int version;
                    lock (_queue)
                    {
                        version = _clearVersion;
                        if (_queue.Count == 0) next = default;
                        else next = _queue.Peek();
                    }
                    if (next.Due == 0)
                    {
                        if (timer.IsHeld && Stopwatch.GetTimestamp() - idleSince > Stopwatch.Frequency) timer.Set(false);
                        _signal.WaitOne(50);
                        continue;
                    }
                    timer.Set(true);
                    idleSince = Stopwatch.GetTimestamp();
                    var wait = next.Due - Stopwatch.GetTimestamp();
                    if (wait > spinTicks)
                    {
                        // Sleep until just before due (a newly pushed earlier message wakes us), then spin.
                        var sleepMs = (int)Math.Max(1, (wait - spinTicks) * 1000 / Stopwatch.Frequency);
                        if (wake is not null && wake.Set((wait - spinTicks) * 10_000_000 / Stopwatch.Frequency)) WaitHandle.WaitAny(waits!, sleepMs + 2);
                        else if (spinTicks < Stopwatch.Frequency * 3 / 1000) spinTicks = Stopwatch.Frequency * 3 / 1000;   // the timer would not arm: back to the tick wait with its longer spin, decided again on the next turn
                        else _signal.WaitOne(sleepMs);
                        continue;
                    }
                    var spins = 0;
                    while (Stopwatch.GetTimestamp() < next.Due)
                    {
                        if ((++spins & 63) == 0) Thread.Yield(); else Thread.SpinWait(20);
                    }
                    // Batch: every message already due goes in one lock hold (chords/seeks), in order, outside the lock.
                    List<(long Due, int Device, int Status, int D1, int D2)>? batch = null;
                    lock (_queue)
                    {
                        // Cleared while we waited: the held message is stale and the queue now holds newer ones.
                        if (version != _clearVersion || _queue.Count == 0) continue;
                        _queue.Dequeue();
                        var now = Stopwatch.GetTimestamp();
                        while (_queue.Count > 0 && _queue.Peek().Due <= now)
                        {
                            (batch ??= new()).Add(_queue.Dequeue());
                        }
                    }
                    _output.Send(next.Device, next.Status, next.D1, next.D2);
                    if (batch is not null) foreach (var m in batch) _output.Send(m.Device, m.Status, m.D1, m.D2);
                }
            }
            finally
            {
                timer.Set(false);
            }
        }

        public void Dispose()
        {
            _stop = true;
            _signal.Set();
        }
    }
}
