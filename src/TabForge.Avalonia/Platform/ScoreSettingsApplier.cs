using TabForge.Avalonia;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: copying the Preferences rows that the score uses into the score view: the default note value, "Move to the next note after writing"
//   and "Plus key lengthens notes" (the editor's tool state and key map), the rest-fill flags ("Fill bars with rests", "Merge rests on delete",
//   "Prevent bar overflow"), the score wheel distance, the Follow settings, the playback line thickness, and the playing-beat highlight,
//   duration glow and playing-bar values of the Follow page. Runs on every settings change; the score repaints only when one of its appearance
//   values changed, and the hotkey map is refreshed so the + / - polarity follows.
// Does not own: the settings store (MainWindowSettings), the edit rules (Core ScoreEditCommands), the follow maths (Core FollowScrollPolicy)
//   or the clip trim after clip edits (MainWindowClipKeys).
// Tests: TabForge.Avalonia.Tests.FollowSettingsTests, TabForge.Avalonia.Tests.ScoreAppearanceSettingsTests.
// Thread: UI only.
internal sealed class ScoreSettingsApplier
{
    private readonly ScorePreviewView _score;
    // The default note value last copied in. The editor's current value changes as the player enters notes, so it is copied only when the setting changes.
    private int? _appliedDefaultDuration;
    // The highlight values last copied in; a save that changes none of them repaints nothing.
    private ScoreHighlightValues? _appliedHighlight;

    public ScoreSettingsApplier(ScorePreviewView score) => _score = score;

    /// <summary>Copies the rows in <paramref name="settings"/> into the score view; a row that did not change leaves the editor state alone.</summary>
    public void Apply(AppSettings settings)
    {
        var editing = settings.Editing;
        if (_appliedDefaultDuration != editing.DefaultDuration)
        {
            _score.Editor.Tools.DurationDenominator = editing.DefaultDuration;
            _appliedDefaultDuration = editing.DefaultDuration;
        }
        _score.Editor.Tools.AutoAdvanceAfterEntry = editing.MoveToNextNoteAfterWriting;
        _score.Editor.Tools.PlusKeyLengthensNotes = editing.PlusKeyLengthensNotes;
        _score.Editor.Tools.FillBarsWithRests = editing.FillBarsWithRests;
        _score.Editor.Tools.MergeRestsOnDelete = editing.MergeRestsOnDelete;
        _score.Editor.Tools.PreventBarOverflow = editing.PreventBarOverflow;
        settings.Hotkeys.PlusKeyLengthensNotes = editing.PlusKeyLengthensNotes;
        _score.Editor.SetHotkeys(HotkeyCatalog.BuildMap(settings.Hotkeys));
        _score.WheelScrollPixels = editing.ScoreWheelScrollPixels;
        _score.Follow = settings.Follow;
        var highlightChanged = ApplyHighlight(settings.Follow, settings.Appearance);
        var thickness = settings.Follow.PlayheadThickness;
        var thicknessChanged = _score.Probe.Appearance.PlayheadThickness != thickness;
        _score.Probe.Appearance.PlayheadThickness = thickness;
        if (thicknessChanged || highlightChanged) _score.Refresh();
    }

    /// <summary>Copies the playing-beat highlight, the duration glow and the playing bar of the Follow page; true when one of them changed.</summary>
    private bool ApplyHighlight(FollowSettings follow, AppearanceSettings appearance)
    {
        var light = global::Avalonia.Application.Current?.ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Light;
        var text = Palette.Get("FollowHighlightText", light);
        var values = new ScoreHighlightValues(follow.HighlightPlayedBeat,
            PlaybackHighlightColour.Of(appearance, text),
            ScoreSettingsBinding.ParseOr(follow.HighlightBackground, Palette.Get("FollowHighlightBackground")),
            ScoreSettingsBinding.ParseOr(follow.DurationGlowColour, Palette.Get("FollowDurationGlow")), follow.DurationTintEnabled ? follow.DurationGlowOpacity : 0,   // Duration tint off: no glow drawn
            follow.PlayingBarEnabled, ScoreSettingsBinding.ParseOr(follow.PlayingBarColour, Palette.Get("FollowPlayingBar")),
            follow.PlayingBarOpacity, follow.PlayingBarWhenStopped);
        if (values == _appliedHighlight) return false;
        _appliedHighlight = values;
        var look = _score.Probe.Appearance;
        look.HighlightPlayedBeat = values.PlayedBeat;
        look.PlaybackColor = values.PlaybackColour;
        look.HighlightBackground = values.PlayedBeatBackground;
        look.DurationGlowColor = values.GlowColour;
        look.DurationGlowOpacity = values.GlowOpacity;
        look.PlayingBarEnabled = values.PlayingBar;
        look.PlayingBarColor = values.PlayingBarColour;
        look.PlayingBarOpacity = values.PlayingBarOpacity;
        look.PlayingBarWhenStopped = values.PlayingBarWhenStopped;
        return true;
    }
}

/// <summary>The colour of the lit notes and the playing line: the custom pick, else the accent.</summary>
internal static class PlaybackHighlightColour
{
    public static UiColor Of(AppearanceSettings appearance, UiColor fallback) => appearance.PlaybackHighlight switch
    {
        PlaybackHighlightModes.Custom => ScoreSettingsBinding.ParseOr(appearance.PlaybackHighlightColour, fallback),
        _ => ScoreSettingsBinding.ParseOr(appearance.Accent, fallback),
    };
}

/// <summary>The Follow page's highlight values as the score reads them: a change in any of them repaints it.</summary>
internal readonly record struct ScoreHighlightValues(bool PlayedBeat, UiColor PlaybackColour, UiColor PlayedBeatBackground, UiColor GlowColour,
    double GlowOpacity, bool PlayingBar, UiColor PlayingBarColour, double PlayingBarOpacity, bool PlayingBarWhenStopped);
