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
            // The playing-highlight rows (tint, playing bar, duration glow, playback line) live on the Appearance page: SettingsCatalog.Appearance.cs.
        };
    }
}
