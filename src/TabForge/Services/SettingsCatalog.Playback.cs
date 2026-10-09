using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Services;

// Owns: the Playback part of the settings catalogue (rows in Preferences order).
// Does not own: page layout (SettingsCatalog.Layout) or the stored values (AppSettings).
// Tests: TestPreferencesCatalog, TestSettingsFileSplitSnapshots.
public static partial class SettingsCatalog
{
    private static List<SettingDescriptor> PlaybackRows(AppSettings s)
    {
        var a = s.Appearance!;
        var fv = s.Follow!;
        var timeline = s.Timeline!;
        return new List<SettingDescriptor>
        {
            // Playback
            Choice(Playback, "Follow", "follow.mode", "Scroll the score while playing", v => fv.Mode = v, () => fv.Mode,
                new[] { FollowModes.Off, FollowModes.Jump, FollowModes.Smooth }, "Off, jump a line at a time, or scroll smoothly.", "follow auto scroll mode playhead"),
            Bool(Playback, "Follow", "follow.horizontal", "Horizontal follow", v => fv.HorizontalFollow = v, () => fv.HorizontalFollow,
                "Scroll horizontally as playback reaches the end of the visible system.", "follow sideways x axis", dependsOn: "follow.mode", dependsOnValue: "Jump|Smooth"),
            Bool(Playback, "Follow", "follow.vertical", "Vertical follow", v => fv.VerticalFollow = v, () => fv.VerticalFollow,
                "Scroll vertically when the active system reaches its trigger position.", "follow vertical y axis", dependsOn: "follow.mode", dependsOnValue: "Jump|Smooth"),
            Int(Playback, "Follow", "follow.anticipation", "Look-ahead bars", v => fv.AnticipationBars = v, () => fv.AnticipationBars, 0, 4,
                "Keep this many bars visible ahead of the playhead before horizontal scrolling begins.", "follow horizontal anticipation lookahead ahead bars", "bars", dependsOn: "follow.horizontal"),
            Int(Playback, "Follow", "follow.verticaltrigger", "Vertical trigger position", v => fv.VerticalTriggerPercent = v, () => fv.VerticalTriggerPercent, 40, 95,
                "Start following when the active system reaches this viewport position.", "follow vertical trigger lookahead percent", "%", dependsOn: "follow.vertical"),
            Int(Playback, "Follow", "follow.margin", "Playhead position in the view", v => fv.MarginPercent = v, () => fv.MarginPercent, 0, 60,
                "Preferred vertical position of the active system from the top of the viewport.", "follow target viewport top margin", "%", dependsOn: "follow.vertical"),
            Bool(Playback, "Follow", "follow.stopmanual", "Pause following when I scroll by hand", v => fv.StopOnManualScroll = v, () => fv.StopOnManualScroll,
                "Stop automatic movement while you scroll by hand.", "manual scroll pause"),
            Bool(Playback, "Follow", "follow.stopatend", "Stop scrolling once the end is visible", v => fv.StopAtEnd = v, () => fv.StopAtEnd,
                "Avoid scrolling once the last system or loop end is already visible.", "last end visible"),
            Int(Playback, "Follow", "follow.fps", "Smooth scrolling frame rate", v => fv.MaxFps = v, () => fv.MaxFps, 10, 240,
                "Refresh ceiling for Smooth mode.", "fps frame rate animation", "fps"),
            Bool(Playback, "Highlighting", "follow.highlight", "Tint the playing beat", v => fv.HighlightPlayedBeat = v, () => fv.HighlightPlayedBeat,
                "Tint the currently sounding beat.", "playing beat note highlight tint"),
            Colour(Playback, "Highlighting", "follow.colour", "Playing note colour", v => fv.HighlightColour = v, () => fv.HighlightColour,
                "Colour of sounding note heads and fret numbers.", "note playback green"),
            Colour(Playback, "Highlighting", "follow.bg", "Playing beat background", v => fv.HighlightBackground = v, () => fv.HighlightBackground,
                "Background tint behind the elapsed part of the sounding beat.", "beat tint background"),
            Bool(Playback, "Highlighting", "follow.duration.enabled", "Duration tint", v => fv.DurationTintEnabled = v, () => fv.DurationTintEnabled,
                "Show the tail that marks the remaining duration of active notes; beat tint is controlled separately.", "duration shading tint tail"),
            Colour(Playback, "Highlighting", "follow.durationglow", "Duration glow colour", v => fv.DurationGlowColour = v, () => fv.DurationGlowColour,
                "Colour of the translucent active-note duration region.", "duration glow active note colour", dependsOn: "follow.duration.enabled"),
            Number(Playback, "Highlighting", "follow.durationopacity", "Duration glow intensity", v => fv.DurationGlowOpacity = v / 100, () => fv.DurationGlowOpacity * 100, 0, 100,
                "Scales playback shading opacity. 0% disables the duration and beat fills; the playback line and note-position indicator remain.", "duration glow intensity opacity", "%", 1, 0, "follow.duration.enabled"),
            Bool(Playback, "Highlighting", "follow.playingbar", "Highlight the playing bar", v => fv.PlayingBarEnabled = v, () => fv.PlayingBarEnabled,
                "Shade the whole bar that is playing, across the staff and tab, behind the notes. Off by default. Also in View > Highlight playing bar.", "playing bar highlight band wash current measure"),
            Colour(Playback, "Highlighting", "follow.playingbar.colour", "Playing bar colour", v => fv.PlayingBarColour = v, () => fv.PlayingBarColour,
                "Colour of the playing-bar band; the opacity below is applied on top of it.", "playing bar band colour", dependsOn: "follow.playingbar"),
            Number(Playback, "Highlighting", "follow.playingbar.opacity", "Playing bar opacity", v => fv.PlayingBarOpacity = v / 100, () => fv.PlayingBarOpacity * 100, 5, 60,
                "How strongly the playing bar is shaded.", "playing bar band opacity intensity", "%", 1, 0, "follow.playingbar"),
            Bool(Playback, "Highlighting", "follow.playingbar.stopped", "Also show the cursor's bar when stopped", v => fv.PlayingBarWhenStopped = v, () => fv.PlayingBarWhenStopped,
                "When playback is stopped, shade the bar the edit cursor is in.", "playing bar band stopped cursor", dependsOn: "follow.playingbar"),
            Colour(Playback, "Highlighting", "follow.playhead","Playback line colour", v => fv.PlayheadColour = v, () => fv.PlayheadColour,
                "Colour of the vertical playback line in the score (the timeline keeps its white line).", "playhead line cursor"),
            Number(Playback, "Highlighting", "follow.playhead.thickness", "Playback line thickness", v => fv.PlayheadThickness = v, () => fv.PlayheadThickness, 0.5, 5,
                "Stroke width of the vertical playback line in the score.", "playhead line width thickness", "px", 0.1, 1),

        };
    }
}
