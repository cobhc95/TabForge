using System;
using System.Collections.Generic;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the View hotkeys that flip or cycle one saved display setting (auto-fit track list, Add-track lane, lines between tracks, playback
//   position marker, fretboard string spacing, scale highlight strength up and down, smooth page-turn follow, fretboard size lock, Follow playback on or off). Each one
//   changes the live settings and saves the settings file; the views read the setting when they redraw.
// Does not own: the settings values and their rules (Core AppSettings, SettingsValidator), the views that draw them, the menu rows (MainMenuTable),
//   the key table (MainWindowKeyboard), the layout hotkeys (MainWindowViewHotkeys).
// Tests: TabForge.Avalonia.Tests.MainWindowMenuTests (every id in Commands is handled).
public static class MainWindowViewToggles
{
    /// <summary>The command ids this class handles, in the order of <see cref="Changes"/>.</summary>
    public static readonly IReadOnlyList<string> Commands = new[]
    {
        "View.AutoFitTrackList", "View.ToggleAddTrackLane", "View.ToggleTrackLines", "View.CyclePlayheadStyle", "View.CycleStringSpacing",
        "View.ScaleHighlightBrighter", "View.ScaleHighlightDimmer", "View.SmoothFollow", "View.LockInstrumentSize", FollowPlaybackCommandId,
        ClearScaleCommandId, FretboardPositionCommandId,
    };

    /// <summary>The View menu rows this class also handles: clearing the scale highlight, and moving the fretboard pane above or below the score.</summary>
    private const string ClearScaleCommandId = "View.ClearScale";
    private const string FretboardPositionCommandId = "View.FretboardPosition";
    private const string FollowPlaybackCommandId = "View.FollowPlayback";

    /// <summary>The status notices flipping Follow playback shows.</summary>
    private const string FollowOnText = "Follow playback on";
    private const string FollowOffText = "Follow playback off";

    /// <summary>The status notices moving the fretboard pane shows.</summary>
    private const string FretboardAboveText = "Fretboard above the score";
    private const string FretboardBelowText = "Fretboard below the score";

    /// <summary>What each command does to the settings it owns.</summary>
    private static readonly IReadOnlyDictionary<string, Action<AppSettings>> Changes = new Dictionary<string, Action<AppSettings>>(StringComparer.Ordinal)
    {
        ["View.AutoFitTrackList"] = s => s.Timeline.AutoFitTrackList = !s.Timeline.AutoFitTrackList,
        ["View.ToggleAddTrackLane"] = s => s.Timeline.ShowAddTrackLane = !s.Timeline.ShowAddTrackLane,
        ["View.ToggleTrackLines"] = s => s.Timeline.ShowTrackLines = !s.Timeline.ShowTrackLines,
        ["View.CyclePlayheadStyle"] = s => s.Timeline.PlayheadStyle = PlayheadStyles.Next(s.Timeline.PlayheadStyle),
        ["View.CycleStringSpacing"] = s => s.Editing.FretStringSpacing = FretStringSpacings.Next(s.Editing.FretStringSpacing),
        ["View.ScaleHighlightBrighter"] = s => s.Editing.ScaleHighlightStrength = StepStrength(s.Editing.ScaleHighlightStrength, ScaleHighlightStyles.StrengthStep),
        ["View.ScaleHighlightDimmer"] = s => s.Editing.ScaleHighlightStrength = StepStrength(s.Editing.ScaleHighlightStrength, -ScaleHighlightStyles.StrengthStep),
        ["View.SmoothFollow"] = s => s.Follow.ContinuousScroll = !s.Follow.ContinuousScroll,
        ["View.LockInstrumentSize"] = s => s.Appearance.LockInstrumentSize = !s.Appearance.LockInstrumentSize,
    };

    /// <param name="commands">The window's command table; each id is added once.</param>
    /// <param name="settings">The shared settings the commands change and save; null (no settings file) makes them do nothing.</param>
    /// <param name="status">The status notice moving the fretboard pane shows; null shows nothing (the Compose call leaves it unset).</param>
    public static void Register(MainWindowCommands commands, MainWindowSettings? settings, Action<string>? status = null)
    {
        foreach (var (commandId, change) in Changes)
            commands.Add(commandId, () => Change(settings, change));
        commands.Add(ClearScaleCommandId, () => Change(settings, s => s.Editing.ScaleHighlight = null));
        commands.Add(FretboardPositionCommandId, () => MoveFretboard(settings, status));
        commands.Add(FollowPlaybackCommandId, () => ToggleFollowPlayback(settings, status));
    }

    /// <summary>Moves the fretboard pane above the score or below it (the binding moves the pane on the settings change).</summary>
    private static void MoveFretboard(MainWindowSettings? settings, Action<string>? status)
    {
        if (settings is null) return;
        settings.Settings.Appearance.FretboardAtBottom = !settings.Settings.Appearance.FretboardAtBottom;
        settings.SaveSettings();
        status?.Invoke(settings.Settings.Appearance.FretboardAtBottom ? FretboardBelowText : FretboardAboveText);
    }

    /// <summary>Flips Follow playback (score and timeline together), saves it and tells the player which way it went.</summary>
    private static void ToggleFollowPlayback(MainWindowSettings? settings, Action<string>? status)
    {
        if (settings is null) return;
        var follow = settings.Settings.Follow;
        follow.FollowPlayback = !follow.FollowPlayback;
        settings.SaveSettingsDeferred();
        status?.Invoke(follow.FollowPlayback ? FollowOnText : FollowOffText);
    }

    /// <summary>Applies one setting change to the live settings and saves them; a null settings host changes nothing.</summary>
    private static void Change(MainWindowSettings? settings, Action<AppSettings> change)
    {
        if (settings is null) return;
        change(settings.Settings);
        settings.SaveSettingsDeferred();
    }

    /// <summary>The scale highlight strength moved one step, kept inside the allowed range.</summary>
    private static int StepStrength(int current, int step) =>
        Math.Clamp(current + step, ScaleHighlightStyles.MinStrength, ScaleHighlightStyles.MaxStrength);
}
