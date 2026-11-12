using System;
using Avalonia.Controls;
using TabForge.Platform.Theme;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: reading the monitor a window is on (working area in DIPs and display scaling) for the interface-size recommendation, and applying
//     that recommendation while the user has not chosen a size of their own.
// Does not own: the heuristic (UiScaleRecommendation), applying a scale to a window (ThemeSettingsBinding), or the Settings button (PreferencesWindow).
// Tests: UiScaleRecommendationTests.
/// <summary>The interface size recommended for a window's monitor, and the automatic default that follows it.</summary>
public static class MonitorUiScale
{
    /// <summary>The recommendation for the screen <paramref name="window"/> is on (the primary screen before it is shown); null when there is no screen.</summary>
    public static UiScaleAdvice? Recommend(Window window)
    {
        if (WindowUiScale.Monitor(window) is not { } monitor) return null;
        return UiScaleRecommendation.For(monitor.WorkingArea.Width, monitor.WorkingArea.Height, monitor.Scaling);
    }

    /// <summary>
    /// Puts <paramref name="appearance"/> on the recommended size and back under the monitor's control (the Settings button and a reset).
    /// Returns true when the size changed. An equal size (within epsilon) or a missing screen leaves all settings untouched.
    /// </summary>
    public static bool UseRecommended(Window window, AppearanceSettings appearance)
    {
        if (Recommend(window) is not { } advice || Math.Abs(advice.Scale - appearance.UiScale) < 1e-9) return false;
        appearance.UiScaleUserOverridden = false;
        appearance.UiScale = advice.Scale;
        return true;
    }

    /// <summary>
    /// The automatic default: while the user has not chosen a size, the window takes the recommendation for its monitor when it opens and when
    /// it moves to a monitor with another display scaling. A size the user chose is never replaced and nothing is asked.
    /// </summary>
    public static void Follow(Window window, MainWindowSettings? settings)
    {
        if (settings is null) return;
        void Apply()
        {
            var appearance = settings.Settings.Appearance;
            if (appearance.UiScaleUserOverridden) return;
            if (Recommend(window) is not { } advice || Math.Abs(advice.Scale - appearance.UiScale) < 1e-9) return;
            appearance.UiScale = advice.Scale;
            settings.SaveSettings();   // the change notice re-applies the scale to every window (ThemeSettingsBinding)
        }
        window.Opened += (_, _) => Apply();
        window.ScalingChanged += (_, _) => Apply();
    }
}
