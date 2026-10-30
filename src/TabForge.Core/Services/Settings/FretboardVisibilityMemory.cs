namespace TabForge.Services;

/// <summary>The editor modes that remember whether the fretboard (instrument pane) is shown.</summary>
public enum FretboardMode
{
    /// <summary>The score (tab) editor: its choice is <see cref="AppearanceSettings.ShowFretboard"/>, the main one.</summary>
    Tab,
    /// <summary>The piano roll: its own choice, off by default, never shared with the tab.</summary>
    PianoRoll,
    /// <summary>The Band view: every band track keeps its own fretboard settings, so there is no shared value.</summary>
    Band,
}

// Owns: which remembered value a fretboard show/hide choice belongs to, per editor mode.
// Does not own: when a choice is the user's own (the writers decide), the dock or the settings file.
// Tests: FretboardVisibilityMemoryTests.
/// <summary>The mode a fretboard choice is remembered in: Tab mode reads and writes Appearance.ShowFretboard, piano roll its own
/// Appearance.ShowFretboardInPianoRoll, and Band mode has none (each band track keeps its own settings).</summary>
public static class FretboardVisibilityMemory
{
    /// <summary>The value a mode remembers; null for <see cref="FretboardMode.Band"/> (no shared value) and unknown modes.</summary>
    public static bool? Remembered(AppSettings settings, FretboardMode mode) => mode switch
    {
        FretboardMode.Tab => settings.Appearance.ShowFretboard,
        FretboardMode.PianoRoll => settings.Appearance.ShowFretboardInPianoRoll,
        _ => null,
    };

    /// <summary>Stores <paramref name="shown"/> as the mode's remembered value; true when it changed. Band mode writes nothing.</summary>
    public static bool Remember(AppSettings settings, FretboardMode mode, bool shown)
    {
        if (Remembered(settings, mode) is not { } current || current == shown) return false;
        switch (mode)
        {
            case FretboardMode.Tab: settings.Appearance.ShowFretboard = shown; break;
            case FretboardMode.PianoRoll: settings.Appearance.ShowFretboardInPianoRoll = shown; break;
            default: return false;
        }
        return true;
    }
}
