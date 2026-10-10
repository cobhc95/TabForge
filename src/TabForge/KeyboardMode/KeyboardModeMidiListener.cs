using System.Collections.Concurrent;
using TabForge.Audio;

namespace TabForge.KeyboardMode;

// Owns: listening to the MIDI keyboard for Keyboard mode: one subscription to a shared MIDI input (one device or all) while it is on, each note event turned into song time on arrival
//   (clock at its stamp minus the output latency) and queued for the UI thread, and every channel message passed on as it arrives (Thru, for the practice track). Never plays, records or arms anything.
// Does not own: the devices (MidiInputHub), the clock, the judge that drains the queue (KeyboardModeKeyboardSession) or when it is on.
// Tests: TestKeyboardModeMidiListener.
public sealed class KeyboardModeMidiListener : IDisposable
{
    /// <summary>The most events kept between two frames; a stalled UI drops the newest rather than grow.</summary>
    private const int MaxQueued = 512;

    private readonly IMidiInputSource _input;
    private readonly Func<long, double> _songSecAt;
    private readonly Func<double> _latencySec;
    private readonly ConcurrentQueue<KeyboardModePlayed> _queue = new();
    private int _queued;
    private volatile bool _on;

    /// <param name="input">A client of the shared MIDI input.</param>
    /// <param name="songSecAt">Song seconds at a stopwatch stamp (any thread); NaN when stopped or paused.</param>
    /// <param name="latencySec">Delay between a stamp and what is heard (any thread), seconds.</param>
    public KeyboardModeMidiListener(IMidiInputSource input, Func<long, double> songSecAt, Func<double> latencySec)
    {
        _input = input; _songSecAt = songSecAt; _latencySec = latencySec;
    }

    /// <summary>MIDI driver thread: every channel message of the player while listening (note, pedal, bend, pressure; a program change is not passed on), for the practice track to play.</summary>
    public event Action<int, int, int>? Thru;

    /// <summary>The device listened to (null: every device); only a hub client can tell devices apart.</summary>
    public string? Device
    {
        get => (_input as IMidiInputClient)?.Device;
        set { if (_input is IMidiInputClient client && client.Device != value) client.Device = value; }
    }

    /// <summary>True while subscribed and the input is open.</summary>
    public bool IsListening => _on;

    /// <summary>True: a press while the song time is unknown (paused) is queued with a NaN time (pitch only; the judge ignores it) instead of dropped. Wait mode turns it on.</summary>
    public volatile bool KeepUnstamped;

    /// <summary>Subscribes and opens the input (idempotent). Returns false when no device could be opened (nothing stays subscribed).</summary>
    public bool Start()
    {
        if (_on) return true;
        _input.Message += OnMessage;
        if (_input.Open() > 0) return _on = true;
        _input.Message -= OnMessage;
        return false;
    }

    /// <summary>Unsubscribes, closes this listener's hold on the input and drops what is queued (idempotent).</summary>
    public void Stop()
    {
        if (!_on) return;
        _on = false;
        _input.Message -= OnMessage;
        _input.Close();
        while (_queue.TryDequeue(out _)) { }
        Volatile.Write(ref _queued, 0);
    }

    public void Dispose() => Stop();

    /// <summary>UI thread: hands every queued event to <paramref name="sink"/> in arrival order.</summary>
    public void Drain(Action<KeyboardModePlayed> sink)
    {
        while (_queue.TryDequeue(out var e)) { Interlocked.Decrement(ref _queued); sink(e); }
    }

    /// <summary>MIDI driver thread: a lock-free enqueue, nothing that waits.</summary>
    private void OnMessage(int status, int data1, int data2, long stamp)
    {
        if (!_on) return;
        var type = status & 0xF0;
        if (type is >= 0x80 and < 0xF0 and not 0xC0) Thru?.Invoke(status, data1, data2);
        if (type is not (0x80 or 0x90)) return;
        var sec = _songSecAt(stamp);
        var unstamped = double.IsNaN(sec);   // paused or stopped: the song time is unknown, so it is not judged
        if ((unstamped && !KeepUnstamped) || Volatile.Read(ref _queued) >= MaxQueued) return;
        Interlocked.Increment(ref _queued);
        _queue.Enqueue(new KeyboardModePlayed(data1, type == 0x90 && data2 > 0, unstamped ? double.NaN : KeyboardModeJudge.Compensate(sec, _latencySec())));
    }
}
