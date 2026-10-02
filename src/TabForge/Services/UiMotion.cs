namespace TabForge.Services;

// Owns: the shared animation settings (reduce-animations switch, speed) and the durations derived from them.
// Does not own: the animations that use them.
// Tests: TestOrderAnimationAndSpeedCommands.
/// <summary>Shared duration policy for the application's short drag transitions.</summary>
public static class UiMotion
{
    private static volatile bool _reduced;
    private static double _speed = 1;

    public static void Configure(bool reduceAnimations, double speed)
    {
        _reduced = reduceAnimations;
        _speed = double.IsFinite(speed) ? Math.Clamp(speed, 0.25, 2) : 1;
    }

    public static double DurationMilliseconds(double baseDuration) =>
        _reduced ? 0 : Math.Clamp(baseDuration * _speed, 0, 2000);
}
