namespace TabForge.LiveInput;

// Owns: the peak-hold mark of a meter: the highest recent peak, kept for a moment, then following the level down.
// Does not own: the peaks (the engine), the clock (the caller passes the time) or the drawing.
// Tests: no dedicated test class.
/// <summary>Holds the highest peak for <see cref="HoldMs"/>; allocates nothing.</summary>
public sealed class PeakHold
{
    public const double HoldMs = 1500;
    private double _held, _heldAtMs;

    /// <summary>The peak being held.</summary>
    public double Held => _held;

    /// <summary>Feeds the level seen at <paramref name="nowMs"/>; returns the held peak.</summary>
    public double Update(double peak, double nowMs)
    {
        if (peak >= _held || nowMs - _heldAtMs > HoldMs) { _held = peak; _heldAtMs = nowMs; }
        return _held;
    }
}
