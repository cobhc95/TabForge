namespace TabForge.AudioEngine.Builtin.Limiter;

// Owns: the smallest value among the last N pushed, in constant time per push and without allocation after construction (a monotonic queue in two arrays).
// Does not own: what the limiter does with it (LimiterEffect.Process.cs).
// Tests: BuiltinDynamicsTests.
internal sealed class SlidingMinimum
{
    private readonly double[] _values;
    private readonly int[] _times;
    private readonly int _window;
    private int _head, _count, _time;

    public SlidingMinimum(int window)
    {
        _window = Math.Max(1, window);
        _values = new double[_window + 1];
        _times = new int[_window + 1];
    }

    /// <summary>Adds a value; returns the minimum of this and the previous window - 1 values.</summary>
    public double Push(double value)
    {
        var size = _values.Length;
        while (_count > 0 && _values[(_head + _count - 1) % size] >= value) _count--;
        var slot = (_head + _count) % size;
        _values[slot] = value;
        _times[slot] = _time;
        _count++;
        while (unchecked(_time - _times[_head]) >= _window) { _head = (_head + 1) % size; _count--; }
        _time = unchecked(_time + 1);
        return _values[_head];
    }
}
