using System.Text.Json.Serialization;
using TabForge.Docking;
using TabForge.Documents;

namespace TabForge.Services;

// Owns: the follow (scrolling while playing) and playing-highlight settings and the FollowModes names.
// Does not own: loading, saving and validating (AppSettingsStore, SettingsValidator) or the Preferences rows (SettingsCatalog).
// Tests: TestPreferencesCatalog, TestSettingsFileSplitSnapshots.
/// <summary>
/// Keeping the score under the player's eyes while they play: a jump mode with anticipation, a smooth
/// mode paced to the music, no scrolling once the end is already on screen, and "stop following if the
/// user scrolls".
/// </summary>
public sealed class FollowSettings
{
    /// <summary>"Off", "Jump" or "Smooth".</summary>
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
    /// <summary>Stop following when the user scrolls by hand; following resumes on the next transport action.</summary>
    public bool StopOnManualScroll { get; set; } = true;
    /// <summary>Do not scroll once the last system (or the loop end) is already fully visible.</summary>
    public bool StopAtEnd { get; set; } = true;
    /// <summary>Tint the beat that is currently sounding.</summary>
    public bool HighlightPlayedBeat { get; set; } = true;
    /// <summary>Show active note duration shading behind the playhead.</summary>
    public bool DurationTintEnabled { get; set; } = false;
    /// <summary>Colour of the sounding note heads, fret numbers and playhead.</summary>
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
    /// <summary>Opt-in: a translucent band over the whole bar that is playing (off by default).</summary>
    public bool PlayingBarEnabled { get; set; } = false;
    /// <summary>Colour of the playing-bar band.</summary>
    public string PlayingBarColour { get; set; } = "#FFE066";
    /// <summary>Opacity of the playing-bar band (0.05..0.6).</summary>
    public double PlayingBarOpacity { get; set; } = 0.20;
    /// <summary>Also band the edit cursor's bar while playback is stopped.</summary>
    public bool PlayingBarWhenStopped { get; set; } = false;
}

public static class FollowModes
{
    public const string Off = "Off";
    public const string Jump = "Jump";
    public const string Smooth = "Smooth";
}
