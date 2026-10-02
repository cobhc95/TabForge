using System.Diagnostics;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Playback;

namespace TabForge.Audio;

/// <summary>
/// One document's MIDI output. Channels whose track plays through plug-ins are sent to the audio engine (shared
/// ring, time-stamped); everything else goes to Windows MIDI exactly as before. While the engine is in use,
/// Windows MIDI is held back by the engine's latency so both stay in time (a small delay thread, idle otherwise).
/// With no plug-in tracks and "Play the whole song through the audio engine" switched off, this is a straight
/// pass-through: no delay, no thread, no engine. That setting is on by default, so normally the engine is in use.
/// </summary>
public sealed class RoutedMidiOutput : IMidiOutput
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

    public void Send(int deviceId, int status, int data1, int data2)
    {
        var routes = _routes;
        var channel = status & 0x0F;
        if (status < 0xF0 && routes[channel] is var slot && slot >= 0 && _engine.IsRunning)
        {
            // The engine schedules by timestamp, so a later stamp holds the event back (net < 0: Windows MIDI is the slower path).
            var engineHold = Compensation(_engine.LatencyTicks, WindowsMidiTicks, _engine.Mixer.PlayAllThroughEngine).EngineHoldTicks;
            _engine.Write(new TimedMidi
            {
                Timestamp = Stopwatch.GetTimestamp() + engineHold, Slot = slot,
                Status = (byte)status, Data1 = (byte)data1, Data2 = (byte)data2
            });
            return;
        }
        var delay = _engine.IsRunning && (_engineInUse || routes.Any(r => r >= 0)) ? _engine.LatencyTicks - WindowsMidiTicks : 0;
        if (delay <= 0) { _inner.Send(deviceId, status, data1, data2); return; }
        (_delay ??= new DelayLine(_inner)).Push(Stopwatch.GetTimestamp() + delay, deviceId, status, data1, data2);
    }

    /// <summary>Live input: straight to the device, or to the engine stamped "already due" so it plays in the next block.</summary>
    public void SendLive(int deviceId, int status, int data1, int data2)
    {
        var channel = status & 0x0F;
        if (status < 0xF0 && _routes[channel] is var slot && slot >= 0 && _engine.IsRunning)
        {
            _engine.Write(new TimedMidi
            {
                Timestamp = Stopwatch.GetTimestamp() - _engine.LatencyTicks, Slot = slot,
                Status = (byte)status, Data1 = (byte)data1, Data2 = (byte)data2
            });
            return;
        }
        _inner.Send(deviceId, status, data1, data2);
    }

    public void ResetAll()
    {
        _delay?.Clear();
        _inner.ResetAll();
        PanicOwnSlots(_routes);
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
        private readonly IMidiOutput _output;
        private readonly Queue<(long Due, int Device, int Status, int D1, int D2)> _queue = new();
        private readonly AutoResetEvent _signal = new(false);
        private readonly Thread _thread;
        private volatile bool _stop;
        private int _clearVersion; // guarded by _queue

        public DelayLine(IMidiOutput output)
        {
            _output = output;
            _thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "TabForge MIDI delay" };
            _thread.Start();
        }

        // Messages arrive in time order with the same delay, so a FIFO stays sorted.
        public void Push(long due, int device, int status, int d1, int d2)
        {
            lock (_queue) _queue.Enqueue((due, device, status, d1, d2));
            _signal.Set();
        }

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
                var spinTicks = Stopwatch.Frequency * 3 / 1000;
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
                        // Sleep until ~3 ms before due (a newly pushed earlier message wakes us), then spin.
                        _signal.WaitOne((int)Math.Max(1, (wait - spinTicks) * 1000 / Stopwatch.Frequency));
                        continue;
                    }
                    var spins = 0;
                    while (Stopwatch.GetTimestamp() < next.Due)
                    {
                        if ((++spins & 63) == 0) Thread.Yield(); else Thread.SpinWait(20);
                    }
                    lock (_queue)
                    {
                        // Cleared while we waited: the held message is stale and the queue now holds newer ones.
                        if (version != _clearVersion || _queue.Count == 0) continue;
                        _queue.Dequeue();
                    }
                    _output.Send(next.Device, next.Status, next.D1, next.D2);
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
