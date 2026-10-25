namespace TabForge.Presenters;

/// <summary>One settings area the main window applies, in the order of <see cref="MainSettingsFlow.Steps"/>.</summary>
public enum MainSettingsStep
{
    InstrumentAndEditing, TransportAndGeneral, Follow, ScoreAppearance, Playhead, ScoreLayout, Timeline, IconSize, WindowSize,
    ViewToggles, Refresh, AudioRoute,
}

/// <summary>What a step redraws when the visual settings changed since the last apply.</summary>
public enum MainSettingsRedraw { None, ScoreRelayout, Retheme }

// Owns: the order the main window applies settings in, which steps run for a given call, and which steps redraw on a visual change.
// Does not own: what a step applies (the window's appliers), the settings store, each applier's own change check.
// Tests: no dedicated test class.
/// <summary>The toolkit-free ordered apply list of the main window's settings.</summary>
public static class MainSettingsFlow
{
    // One data dependency: Playhead reads the colour and opacity that Follow puts on the score appearance. Refresh runs after every
    // value is in place, and the engine route is last.
    public static readonly IReadOnlyList<MainSettingsStep> Steps = Enum.GetValues<MainSettingsStep>();

    /// <summary>The steps one apply runs: the window size only at load, the audio route unless the caller skips it.</summary>
    public static IEnumerable<MainSettingsStep> StepsFor(bool applyWindowSize, bool applyAudioRoute) =>
        Steps.Where(step => step switch
        {
            MainSettingsStep.WindowSize => applyWindowSize,
            MainSettingsStep.AudioRoute => applyAudioRoute,
            _ => true,
        });

    public static MainSettingsRedraw RedrawOf(MainSettingsStep step) => step switch
    {
        MainSettingsStep.ScoreLayout => MainSettingsRedraw.ScoreRelayout,
        MainSettingsStep.Refresh => MainSettingsRedraw.Retheme,
        _ => MainSettingsRedraw.None,
    };
}
