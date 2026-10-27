using System.Text.Json.Serialization;

namespace TabForge.Services;

// Owns: the follow (scrolling while playing) and playing-highlight settings and the FollowModes names.
// Does not own: loading, saving and validating (AppSettingsStore, SettingsValidator) or the Preferences rows (SettingsCatalog).
// Tests: TestSettingsFileSplitSnapshots.
/// <summary>
/// Keeping the score under the player's eyes while they play: a jump mode with anticipation, a smooth
/// mode paced to the music, no scrolling once the end is already on screen, and "stop following if the
/// user scrolls".
/// </summary>
public sealed class FollowSettings
{
    /// <summary>The master switch: false = nothing auto-scrolls (score and timeline) while the playhead still moves. The hotkey and Preferences flip this one value.</summary>
    public bool FollowPlayback { get; set; } = true;
    /// <summary>Seconds after the last hand scroll before following resumes (1..10).</summary>
    public int ResumeAfterSeconds { get; set; } = 3;
    /// <summary>"Jump" or "Smooth"; whether following runs at all is <see cref="FollowPlayback"/>.</summary>
    public string Mode { get; set; } = FollowModes.Smooth;
    /// <summary>Where the played system is parked, as a percentage of the viewport height from the top.</summary>
    public int MarginPercent { get; set; } = 20;
    /// <summary>System edge threshold as a percentage of the score viewport.</summary>
    public int VerticalTriggerPercent { get; set; } = 80;
    /// <summary>Follow score movement horizontally while playback is active.</summary>
    public bool HorizontalFollow { get; set; } = true;
    /// <summary>Follow score movement vertically while playback is active.</summary>
    public bool VerticalFollow { get; set; } = true;
    /// <summary>Bars of look-ahead required to stay visible in jump mode (TuxGuitar: 1).</summary>
    public int AnticipationBars { get; set; } = 1;
    /// <summary>Refresh ceiling for smooth scrolling, in frames per second (TuxGuitar: 40).</summary>
    /// <summary>Follow-scroll frame-rate cap; 240 = match the display refresh (default).</summary>
    public int MaxFps { get; set; } = 240;
    /// <summary>Follow style: false = page turn (instant half-screen / next-line jump, default); true = the same turns, glided.</summary>
    public bool ContinuousScroll { get; set; }
    /// <summary>Stop following when the user scrolls by hand; following resumes after <see cref="ResumeAfterSeconds"/> or on the next transport action.</summary>
    public bool StopOnManualScroll { get; set; } = true;
    /// <summary>Do not scroll once the last system (or the loop end) is already fully visible.</summary>
    public bool StopAtEnd { get; set; } = true;
    /// <summary>Tint the beat that is currently sounding.</summary>
    public bool HighlightPlayedBeat { get; set; } = true;
    /// <summary>Show active note duration shading behind the playhead.</summary>
    public bool DurationTintEnabled { get; set; } = true;
    /// <summary>Retired: the colour of the sounding notes and the line is now Appearance playback highlight (accent or Custom). Kept so old files load; a changed value moves there once (SettingsMigration).</summary>
    public string HighlightColour { get; set; } = "#3FB950";
    /// <summary>Background tint behind the sounding beat (TuxGuitar tints the played beat too).</summary>
    public string HighlightBackground { get; set; } = "#1E3A2A";
    /// <summary>Independent colour of the vertical playback cursor.</summary>
    public string PlayheadColour { get; set; } = "#3FB950";
    /// <summary>Colour of the active-note duration glow.</summary>
    public string DurationGlowColour { get; set; } = "#3FB950";
    /// <summary>Opacity of duration shading and optional beat tint (0..1); zero disables both fills.</summary>
    public double DurationGlowOpacity { get; set; } = 0;
    /// <summary>Intensity of hover/active section glows (0..1).</summary>
    public double SectionGlowIntensity { get; set; } = 0.45;
    public double PlayheadThickness { get; set; } = 1.7;
    /// <summary>Scroll the fretboard so the played position stays visible.</summary>
    public bool FollowFretboard { get; set; } = true;
    /// <summary>A translucent band over the whole bar that is playing (on by default; subtle).</summary>
    public bool PlayingBarEnabled { get; set; } = true;
    /// <summary>Colour of the playing-bar band.</summary>
    public string PlayingBarColour { get; set; } = "#FFE066";
    /// <summary>The playing-bar band's default opacity (5%).</summary>
    public const double DefaultPlayingBarOpacity = 0.05;
    /// <summary>Opacity of the playing-bar band, 0 (invisible) to 1; the band's alpha is this value. Default 5%.</summary>
    public double PlayingBarOpacity { get; set; } = DefaultPlayingBarOpacity;
    /// <summary>Which playing-bar default move has run: 1 = subtle and on (from the old off / 20%), 2 = 5% (from 10%); see SettingsMigration.</summary>
    public int PlayingBarVersion { get; set; } = 2;
    /// <summary>Also band the edit cursor's bar while playback is stopped.</summary>
    public bool PlayingBarWhenStopped { get; set; } = false;
}

public static class FollowModes
{
    public const string Jump = "Jump";
    public const string Smooth = "Smooth";
}
