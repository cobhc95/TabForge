using TabForge.Services;

// Owns: the one fallback settings object a reader takes when no settings file is open: a fresh AppSettings on each call, because a caller may
//   change it (the first-song defaults do). The helper is used only at the real `??` fallback sites.
// Does not own: the settings values (AppSettings), the settings file (AppSettingsStore), or the deliberate direct constructions (the
//   first-start window size, the Preferences window's preview defaults).
// Tests: TabForge.Avalonia.Tests.SettingsDefaultsTests.

namespace TabForge.Avalonia.Platform;

/// <summary>The fallback settings for a caller that has no store yet.</summary>
internal static class SettingsDefaults
{
    /// <summary>A fresh set of defaults; never a shared instance, since a fallback reader may change it.</summary>
    public static AppSettings Fallback() => new();
}
