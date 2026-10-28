using Avalonia.Headless.XUnit;
using TabForge.Avalonia;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Preferences colours and highlight values that reach the score's appearance: the playing-beat highlight, the duration glow and the
//   playing bar (Follow page, SC-06), and the selection and accent colours (Appearance page, SC-07).
// Does not own: the settings file (MainWindowSettings), the drawing of the highlight (Core ScoreRenderer).
// Tests: ScoreAppearanceSettingsTests (this file).
public class ScoreAppearanceSettingsTests
{
    [AvaloniaFact]
    public void FollowHighlightAndPlayingBarSettingsReachTheScore()
    {
        var settings = new AppSettings();
        settings.Follow.HighlightPlayedBeat = false;
        settings.Appearance.PlaybackHighlight = PlaybackHighlightModes.Custom;
        settings.Appearance.PlaybackHighlightColour = "#112233";
        settings.Follow.HighlightBackground = "#445566";
        settings.Follow.DurationGlowColour = "#778899";
        settings.Follow.DurationGlowOpacity = 0.5;
        settings.Follow.PlayingBarEnabled = true;
        settings.Follow.PlayingBarColour = "#AABBCC";
        settings.Follow.PlayingBarOpacity = 0.4;
        settings.Follow.PlayingBarWhenStopped = true;
        var score = new ScorePreviewView();

        new ScoreSettingsApplier(score).Apply(settings);

        var look = score.Probe.Appearance;
        Assert.False(look.HighlightPlayedBeat);
        Assert.Equal(new UiColor(0xFF, 0x11, 0x22, 0x33), look.PlaybackColor);
        Assert.Equal(new UiColor(0xFF, 0x44, 0x55, 0x66), look.HighlightBackground);
        Assert.Equal(new UiColor(0xFF, 0x77, 0x88, 0x99), look.DurationGlowColor);
        Assert.Equal(0.5, look.DurationGlowOpacity);
        Assert.True(look.PlayingBarEnabled);
        Assert.Equal(new UiColor(0xFF, 0xAA, 0xBB, 0xCC), look.PlayingBarColor);
        Assert.Equal(0.4, look.PlayingBarOpacity);
        Assert.True(look.PlayingBarWhenStopped);
    }

    [AvaloniaFact]
    public void PlaybackHighlightFollowsTheAccentWhenChosen()
    {
        var settings = new AppSettings();
        Assert.Equal(PlaybackHighlightModes.Custom, settings.Appearance.PlaybackHighlight);
        settings.Appearance.PlaybackHighlight = PlaybackHighlightModes.FollowAccent;
        settings.Appearance.Accent = "#0A0B0C";
        var score = new ScorePreviewView();

        new ScoreSettingsApplier(score).Apply(settings);

        Assert.Equal(new UiColor(0xFF, 0x0A, 0x0B, 0x0C), score.Probe.Appearance.PlaybackColor);   // the lit notes and the playing line
    }

    [AvaloniaFact]
    public void SelectionAndAccentSettingsReachTheScore()
    {
        var settings = new AppSettings();
        settings.Appearance.SelectionColour = "#010203";
        settings.Appearance.Accent = "#0A0B0C";
        var score = new ScorePreviewView();

        _ = new ScoreSettingsBinding(score, settings);

        Assert.Equal(new UiColor(0xFF, 0x01, 0x02, 0x03), score.Probe.Appearance.SelectionColor);
        Assert.Equal(new UiColor(0xFF, 0x0A, 0x0B, 0x0C), score.Probe.Appearance.AccentColor);
    }
}
