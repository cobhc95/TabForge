// Owns: the score zoom arithmetic shared by both apps: the 10% zoom steps (50% to 200%), the factor clamp, and the "Fit width" factor for the continuous views.
// Does not own: the zoom box, the scroll viewers or the page width (the Avalonia ScorePreviewView).
// Tests: TabForge.Avalonia.Tests.MainWindowRowCommandsTests (ZoomStepsAre...), SelfTestScoreScale.
namespace TabForge.Views.Score;

public static class ScoreZoomRules
{
    /// <summary>The zoom factor the score can show at (1 = 100%).</summary>
    public const double MinFactor = 0.5, MaxFactor = 2.0;

    /// <summary>The percent steps the zoom in and zoom out rows move by.</summary>
    public const double StepPercent = 10;

    /// <summary>Viewport width (DIPs) up to which "Fit width" in the continuous views shows the score at 100%.</summary>
    public const double FitReferenceWidth = 1600;

    /// <summary>The next factor one step (direction +1 or -1) from <paramref name="factor"/>, rounded to a step first and kept within 50% to 200%.</summary>
    public static double Step(double factor, int direction)
    {
        var percent = Math.Clamp(Math.Round(factor * 100 / StepPercent, MidpointRounding.AwayFromZero) * StepPercent + direction * StepPercent, 50, 200);
        return percent / 100.0;
    }

    /// <summary>"Fit width" in the continuous views: 100% up to <see cref="FitReferenceWidth"/>, then growing with the viewport
    /// (10% steps, at most 150%), so a large window shows the score larger instead of mostly empty space.</summary>
    public static double FitContinuous(double viewport) =>
        Math.Clamp(Math.Round(viewport / FitReferenceWidth * 10, MidpointRounding.AwayFromZero) / 10, 1.0, 1.5);
}
