namespace TabForge.Models;

// Owns: the velocity meter of a MIDI-armed track's input strip: the latest note-on of a track in one atomic slot (written on the MIDI driver thread,
//   no lock, no allocation) and the bar and peak-hold tick it makes (read on the UI thread at each meter tick).
// Does not own: which tracks listen to which device (MidiInputMonitor), the tick that polls it (ArmedRowLayer) or the drawing (ArmedRowPainter).
// Tests: TabForge.Avalonia.Tests.ArmedRowMidiMeterTests.
// Threads: MidiNoteSlot.Record on the MIDI driver thread; MidiNoteSlot.Packed and MidiVelocityMeter on the UI thread.
/// <summary>The latest note-on of one track: its velocity and the millisecond it arrived, packed in one long so a reader never sees half of a write.</summary>
public sealed class MidiNoteSlot
{
    private long _packed;   // (arrival ms << 8) | velocity; 0: no note yet

    /// <summary>The latest note as <see cref="MidiVelocityMeter.Update"/> reads it; 0 when none arrived.</summary>
    public long Packed => Volatile.Read(ref _packed);

    /// <summary>Any thread, no waiting: notes a note-on of <paramref name="velocity"/> (1..127) heard at <paramref name="nowMs"/>.</summary>
    public void Record(int velocity, long nowMs) => Volatile.Write(ref _packed, (nowMs << 8) | (uint)Math.Clamp(velocity, 1, 127));
}

/// <summary>
/// The bar and the peak-hold tick of one track: a note-on jumps the bar to velocity / 127, the tick stays at that height for <see cref="HoldMs"/> and then both fall
/// at <see cref="FallPerSecond"/>. Allocates nothing.
/// </summary>
public sealed class MidiVelocityMeter
{
    /// <summary>How long the tick holds its height (the audio meter's hold, about a second).</summary>
    public const double HoldMs = 1000;

    /// <summary>How fast the bar and the tick fall, as a part of the full bar per second: the audio meter's fall (its engine peak loses 0.9 per block, about 86 dB a second) over the 60 dB meter scale.</summary>
    public const double FallPerSecond = 1.5;

    private double _level, _hold, _holdAtMs, _lastMs = double.NaN;
    private long _seen;

    /// <summary>The bar, 0..1.</summary>
    public double Level => _level;

    /// <summary>The peak-hold tick, 0..1; never below the bar.</summary>
    public double Hold => _hold;

    /// <summary>True when the bar and the tick are both at rest at the bottom: nothing is left to repaint.</summary>
    public bool IsIdle => _hold <= 0;

    /// <summary>The velocity (1..127) of a packed note, 0 for none.</summary>
    public static int VelocityOf(long packed) => (int)(packed & 0xFF);

    /// <summary>Moves to <paramref name="nowMs"/>: falls from the last call, then takes the note in <paramref name="packed"/> when it is a new one.</summary>
    public void Update(long packed, double nowMs)
    {
        var step = double.IsNaN(_lastMs) ? 0 : Math.Max(0, nowMs - _lastMs) / 1000 * FallPerSecond;
        _lastMs = nowMs;
        _level = Math.Max(0, _level - step);
        if (nowMs - _holdAtMs > HoldMs) _hold = Math.Max(_level, _hold - step);
        if (packed == _seen || packed == 0) return;
        _seen = packed;
        var velocity = VelocityOf(packed) / 127.0;
        var arrivedMs = Math.Min(nowMs, packed >> 8);
        _level = Math.Max(0, velocity - Math.Max(0, nowMs - arrivedMs) / 1000 * FallPerSecond);
        if (velocity >= _hold || nowMs - _holdAtMs > HoldMs) { _hold = velocity; _holdAtMs = arrivedMs; }
        if (_hold < _level) _hold = _level;
    }
}
