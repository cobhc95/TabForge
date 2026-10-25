using TabForge.Models;

namespace TabForge.Services;

// Owns: the Playback part of the settings catalogue (rows in Preferences order).
// Does not own: page layout (SettingsCatalog.Layout) or the stored values (AppSettings).
// Tests: TestSettingsFileSplitSnapshots.
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
            Bool(Playback, "Follow", "follow.playback", "Follow playback", v => fv.FollowPlayback = v, () => fv.FollowPlayback,
                "Keep the score and the timeline scrolled to the playhead while playing. Off: the playhead still moves, nothing scrolls.", "follow playback auto scroll playhead timeline score", hotkey: "View.FollowPlayback"),
            Int(Playback, "Follow", "follow.resume", "Resume following after manual scroll", v => fv.ResumeAfterSeconds = v, () => fv.ResumeAfterSeconds, 1, 10,
                "After you scroll by hand during playback, following pauses and comes back to the playhead after this many seconds.", "follow resume manual scroll pause seconds delay", "seconds", dependsOn: "follow.playback"),
            Choice(Playback, "Follow", "follow.mode", "Scroll the score while playing", v => fv.Mode = v, () => fv.Mode,
                new[] { FollowModes.Jump, FollowModes.Smooth }, "Jump a line at a time, or scroll smoothly.", "follow auto scroll mode playhead", dependsOn: "follow.playback"),
            Bool(Playback, "Follow", "follow.horizontal", "Horizontal follow", v => fv.HorizontalFollow = v, () => fv.HorizontalFollow,
                "Scroll sideways as playback reaches the end of the visible line of music.", "follow sideways x axis", dependsOn: "follow.playback"),
            Bool(Playback, "Follow", "follow.vertical", "Vertical follow", v => fv.VerticalFollow = v, () => fv.VerticalFollow,
                "Scroll down when the playing line of music reaches the trigger position.", "follow vertical y axis", dependsOn: "follow.playback"),
            Int(Playback, "Follow", "follow.anticipation", "Look-ahead bars", v => fv.AnticipationBars = v, () => fv.AnticipationBars, 0, 4,
                "Keep this many bars visible ahead of the playhead before horizontal scrolling begins.", "follow horizontal anticipation lookahead ahead bars", "bars", dependsOn: "follow.horizontal"),
            Int(Playback, "Follow", "follow.verticaltrigger", "Vertical trigger position", v => fv.VerticalTriggerPercent = v, () => fv.VerticalTriggerPercent, 40, 95,
                "Start scrolling when the playing line of music reaches this height in the window (percent from the top).", "follow vertical trigger lookahead percent", "%", dependsOn: "follow.vertical"),
            Int(Playback, "Follow", "follow.margin", "Playhead position in the view", v => fv.MarginPercent = v, () => fv.MarginPercent, 0, 60,
                "Where the playing line of music sits in the window after scrolling (percent from the top).", "follow target viewport top margin", "%", dependsOn: "follow.vertical"),
            Bool(Playback, "Follow", "follow.stopmanual", "Pause following when I scroll by hand", v => fv.StopOnManualScroll = v, () => fv.StopOnManualScroll,
                "Stop automatic movement while you scroll by hand.", "manual scroll pause"),
            Bool(Playback, "Follow", "follow.stopatend", "Stop scrolling once the end is visible", v => fv.StopAtEnd = v, () => fv.StopAtEnd,
                "Stop scrolling once the last line of music or the end of the loop is already visible.", "last end visible"),
            Int(Playback, "Follow", "follow.fps", "Smooth scrolling smoothness", v => fv.MaxFps = v, () => fv.MaxFps, 10, 240,
                "How many times per second the score updates while scrolling smoothly. Higher = smoother but uses more power; match your screen if you can.", "fps frame rate refresh hz animation", "fps"),
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
                "How strong the playback shading is. 0% turns off the duration and beat fills; the playback line and note-position indicator remain.", "duration glow intensity opacity", "%", 1, 0, "follow.duration.enabled"),
            Bool(Playback, "Highlighting", "follow.playingbar", "Highlight the playing bar", v => fv.PlayingBarEnabled = v, () => fv.PlayingBarEnabled,
                "Shade the whole bar that is playing, across the staff and tab, behind the notes. Off by default. Also in View > Highlight playing bar.", "playing bar highlight band wash current measure"),
            Colour(Playback, "Highlighting", "follow.playingbar.colour", "Playing bar colour", v => fv.PlayingBarColour = v, () => fv.PlayingBarColour,
                "Colour of the playing-bar band; the opacity below is applied on top of it.", "playing bar band colour", dependsOn: "follow.playingbar"),
            Number(Playback, "Highlighting", "follow.playingbar.opacity", "Playing bar strength", v => fv.PlayingBarOpacity = v / 100, () => fv.PlayingBarOpacity * 100, 5, 60,
                "How strongly the playing bar is shaded.", "playing bar band opacity intensity", "%", 1, 0, "follow.playingbar"),
            Bool(Playback, "Highlighting", "follow.playingbar.stopped", "Also show the cursor's bar when stopped", v => fv.PlayingBarWhenStopped = v, () => fv.PlayingBarWhenStopped,
                "When playback is stopped, shade the bar the edit cursor is in.", "playing bar band stopped cursor", dependsOn: "follow.playingbar"),
            // Tombstone: the "follow.playhead" row (Playback line colour) is hidden; Appearance > Playback highlight colours the line now.
            // FollowSettings.PlayheadColour stays so old files load; a changed value moves into Playback highlight Custom (SettingsMigration).
            Number(Playback, "Highlighting", "follow.playhead.thickness", "Playback line thickness", v => fv.PlayheadThickness = v, () => fv.PlayheadThickness, 0.5, 5,
                "How thick the vertical playback line is in the score, in pixels.", "playhead line width thickness", "px", 0.1, 1),

        };
    }
}
