using System;

namespace TabForge.Platform;

/// <summary>
/// The knob's arithmetic, shared by both toolkits' knob controls: clamping to a step, the angle of a value on the dial, and the value change of a
/// circular drag (clockwise raises), a straight drag near the centre and a wheel or arrow step.
/// Owns: the numbers. Does not own: pointer events, drawing, the typed value editor (the controls), the typed text rules (KnobValueParser).
/// Tests: KnobControlTests (full suite) and the Avalonia KnobControlTests.
/// </summary>
public static class KnobMath
{
    /// <summary>The dial's sweep in degrees; 0 degrees is straight up, the sweep is centred on it.</summary>
    public const double SweepDegrees = 270;

    /// <summary>Closer to the centre than this (DIPs) the pointer angle is unstable, so a straight drag is used.</summary>
    public const double AngleDragMinRadius = 8;

    /// <summary>The value clamped to the range and snapped to <paramref name="step"/> (0 = no snapping); a non-finite value gives the nearest end.</summary>
    public static double Clamp(double value, double minimum, double maximum, double step)
    {
        if (!double.IsFinite(value)) return double.IsPositiveInfinity(value) ? maximum : minimum;
        var c = Math.Clamp(value, minimum, maximum);
        return step > 0 ? Math.Clamp(Math.Round(Math.Round(c / step) * step, 6), minimum, maximum) : c;
    }

    /// <summary>Dial angle in degrees of a value (minimum at -SweepDegrees/2, maximum at +SweepDegrees/2).</summary>
    public static double AngleOf(double value, double minimum, double maximum)
    {
        var t = maximum > minimum ? (value - minimum) / (maximum - minimum) : 0;
        return -SweepDegrees / 2 + t * SweepDegrees;
    }

    /// <summary>The value after the pointer moved by <paramref name="angleDelta"/> radians around the centre (clockwise positive).</summary>
    public static double DragByAngle(double value, double angleDelta, double minimum, double maximum, bool fine) =>
        Math.Clamp(value + angleDelta / (Math.PI * 1.5) * (maximum - minimum) * (fine ? 0.25 : 1), minimum, maximum);

    /// <summary>The value after a straight drag of <paramref name="travel"/> DIPs (up plus right is positive).</summary>
    public static double DragByTravel(double value, double travel, double minimum, double maximum, bool fine) =>
        Math.Clamp(value + travel * (maximum - minimum) / (fine ? 600 : 150), minimum, maximum);

    /// <summary>Wheel / arrow step. A knob with a fine step (0.1 dB) keeps whole-unit coarse steps.</summary>
    public static double CoarseStep(double minimum, double maximum, double step, bool fine)
    {
        if (fine) return 1;
        var coarse = Math.Max(1, (maximum - minimum) / 32);
        return step is > 0 and < 1 ? Math.Round(coarse) : coarse;
    }
}

/// <summary>The displayed text of the 0-127 mixer knobs (track volume as a percent, pan as Centre / L n / R n), shared by both toolkits.</summary>
public static class KnobText
{
    /// <summary>Knob units per displayed percent of a track volume (127 = 100 percent).</summary>
    public const double VolumeUnitsPerPercent = 1.27;

    public const int PanCentre = 64;

    public static string VolumePercent(double knobValue) => $"{Math.Round(knobValue / VolumeUnitsPerPercent)}%";

    public static string Pan(double knobValue) =>
        (int)knobValue - PanCentre is var offset && offset == 0 ? "Centre" : offset < 0 ? $"L {-offset}" : $"R {offset}";
}
