using TabForge.Models;

namespace TabForge.Services;

// Owns: the Appearance part of the settings catalogue (rows in Preferences order).
// Does not own: page layout (SettingsCatalog.Layout) or the stored values (AppSettings).
// Tests: TestSettingsFileSplitSnapshots.
public static partial class SettingsCatalog
{
    private static List<SettingDescriptor> AppearanceRows(AppSettings s)
    {
        var a = s.Appearance!;
        var timeline = s.Timeline!;
        var fv = s.Follow!;
        return new List<SettingDescriptor>
        {
            // Appearance
            Choice(Appearance, "Interface", "appearance.thememode", "Theme", v => Features.CatalogComposition.Platform.ApplyThemePreset(a, v), () => a.ThemeMode,
                new[] { "System", "Dark", "Light", "Custom" }, "Dark or Light sets the interface, accent and score colours together, keeping any colour you picked yourself (Reset colours drops those). System follows Windows. Custom keeps all your colours as they are.", "theme system dark light custom"),
            Button(Appearance, "Custom palette", "appearance.resetcolours", "Reset colours to the theme preset",
                "Drops every colour you set yourself (accent, panels, score page...) and applies the Dark or Light colours again.", "theme reset colours preset default restore"),
            Number(Appearance, "Interface", "appearance.uiscale", "Interface size", v => a.UiScale = v, () => a.UiScale, 0.8, 1.5,
                "Make the interface text and icons bigger or smaller (1 = normal).", "ui scale zoom interface size dpi", "x", 0.05, 2),
            Int(Appearance, "Interface", "appearance.tracktint", "Track colour tint", v => a.TrackTintPercent = v, () => a.TrackTintPercent, 0, 60,
                "How strongly each track's row and timeline lane take the track's colour (0 = off). Subtle and darker in the dark theme. Tracks can turn it off in Track properties.",
                "track colour color tint background row lane intensity transparency", unit: "%"),
            Choice(Appearance, "Interface", "appearance.density", "Spacing", v => a.Density = v == "Extra compact" ? "ExtraCompact" : v, () => a.Density == "ExtraCompact" ? "Extra compact" : a.Density,
                new[] { "Extra compact", "Compact", "Comfortable", "Spacious" }, "How tightly the toolbar and buttons are packed.", "extra xcompact compact comfortable spacious density"),
            Bool(Appearance, "Motion", "appearance.reduceanimations", "Reduce animations", v => a.ReduceAnimations = v, () => a.ReduceAnimations,
                "Disable short tab and timeline drag transitions.", "motion accessibility reduce animation"),
            Int(Appearance, "Track colours", "appearance.mutedtrackdim", "Muted track dimming", v => a.MutedTrackDimPercent = v, () => a.MutedTrackDimPercent, 0, 100,
                "How strongly a muted track is greyed out in the track list and in its timeline lane (0 = not dimmed, 100 = strongest). Tracks that are only silent because another track is soloed are not greyed.",
                "muted track dim grey gray dimming dimmed mute lane row opacity", unit: "%"),
            Number(Appearance, "Motion", "appearance.animationspeed", "Animation speed", v => a.AnimationSpeed = v, () => a.AnimationSpeed, 0.25, 2,
                "How fast tab and timeline drag animations run (1 = normal, higher = faster).", "motion animation faster slower duration", "x", 0.05, 2),
            Colour(Appearance, "Interface colours", "appearance.accent", "Accent colour", v => a.Accent = a.Own("Accent", v), () => a.Accent,
                "Focus, active controls and the primary selection colour.", "theme accent selection focus"),
            Choice(Appearance, "Interface colours", "appearance.playbackhighlight", "Playback highlight", v => a.PlaybackHighlight = v, () => a.PlaybackHighlight,
                PlaybackHighlightModes.All, "Colour of the notes and the line that show what is playing in the score. Follow accent uses the accent colour.", "playback highlight playing note colour notes cursor line follow accent"),
            Colour(Appearance, "Interface colours", "appearance.playbackhighlightcolour", "Playback highlight colour", v => a.PlaybackHighlightColour = v, () => a.PlaybackHighlightColour,
                "The colour of the playing notes and line when Playback highlight is Custom.", "playback highlight custom colour playing note colour", "appearance.playbackhighlight", PlaybackHighlightModes.Custom),
            // Playback look: the playing beat, the playing bar, the duration glow and the playback line (moved here from Playback & Practice).
            // Tombstones: "follow.colour" (Playing note colour) merged into Playback highlight / Playback highlight colour above (FollowSettings.HighlightColour is kept so old files load);
            // "follow.playhead" (Playback line colour) merged there earlier. Both move once in SettingsMigration.
            Bool(Appearance, "Playback look", "follow.highlight", "Tint the playing beat", v => fv.HighlightPlayedBeat = v, () => fv.HighlightPlayedBeat,
                "Tint the currently sounding beat.", "playing beat note highlight tint"),
            Bool(Appearance, "Playback look", "follow.playingbar", "Highlight the playing bar", v => fv.PlayingBarEnabled = v, () => fv.PlayingBarEnabled,
                "Shade the whole bar that is playing, across the staff and tab, behind the notes. Also in View > Highlight playing bar.", "playing bar highlight band wash current measure"),
            Colour(Appearance, "Playback look", "follow.playingbar.colour", "Playing bar colour", v => fv.PlayingBarColour = v, () => fv.PlayingBarColour,
                "Colour of the playing-bar band; the opacity below is applied on top of it.", "playing bar band colour", dependsOn: "follow.playingbar"),
            Number(Appearance, "Playback look", "follow.playingbar.opacity", "Playing bar opacity", v => fv.PlayingBarOpacity = v / 100, () => fv.PlayingBarOpacity * 100, 0, 100,
                "How see-through the playing bar is: 0% is invisible, 100% is solid. Subtle by default.", "playing bar band opacity intensity strength", "%", 1, 0, "follow.playingbar"),
            Bool(Appearance, "Playback look", "follow.playingbar.stopped", "Also show the cursor's bar when stopped", v => fv.PlayingBarWhenStopped = v, () => fv.PlayingBarWhenStopped,
                "When playback is stopped, shade the bar the edit cursor is in.", "playing bar band stopped cursor", dependsOn: "follow.playingbar"),
            Colour(Appearance, "Playback look", "follow.bg", "Playing beat background", v => fv.HighlightBackground = v, () => fv.HighlightBackground,
                "Background tint behind the elapsed part of the sounding beat.", "beat tint background"),
            Bool(Appearance, "Playback look", "follow.duration.enabled", "Duration tint", v => fv.DurationTintEnabled = v, () => fv.DurationTintEnabled,
                "Show the tail that marks the remaining duration of active notes; beat tint is controlled separately.", "duration shading tint tail"),
            Colour(Appearance, "Playback look", "follow.durationglow", "Duration glow colour", v => fv.DurationGlowColour = v, () => fv.DurationGlowColour,
                "Colour of the translucent active-note duration region.", "duration glow active note colour", dependsOn: "follow.duration.enabled"),
            Number(Appearance, "Playback look", "follow.durationopacity", "Duration glow intensity", v => fv.DurationGlowOpacity = v / 100, () => fv.DurationGlowOpacity * 100, 0, 100,
                "How strong the duration and beat shading is. 0% turns off the duration and beat fills; the playback line and note-position indicator remain.", "duration glow intensity opacity", "%", 1, 0, "follow.duration.enabled"),
            Number(Appearance, "Playback look", "follow.playhead.thickness", "Playback line thickness", v => fv.PlayheadThickness = v, () => fv.PlayheadThickness, 0.5, 5,
                "How thick the vertical playback line is in the score, in pixels.", "playhead line width thickness", "px", 0.1, 1),
            Colour(Appearance, "Interface colours", "appearance.selection", "Selection colour", v => a.SelectionColour = a.Own("SelectionColour", v), () => a.SelectionColour,
                "Colour used for selected score ranges.", "score selected range highlight"),
            Colour(Appearance, "Interface colours", "appearance.hover", "Hover colour", v => a.HoverColour = a.Own("HoverColour", v), () => a.HoverColour,
                "Base colour for score hover feedback.", "score hover pointer"),
            Colour(Appearance, "Custom palette", "appearance.background", "Window background", v => a.Background = a.Own("Background", v), () => a.Background,
                "Background behind panels in Custom theme.", "theme background dark", "appearance.thememode", "Custom"),
            Colour(Appearance, "Custom palette", "appearance.panel", "Panel background", v => a.Panel = a.Own("Panel", v), () => a.Panel,
                "Toolbar and side-panel background in Custom theme.", "theme panel chrome", "appearance.thememode", "Custom"),
            Colour(Appearance, "Custom palette", "appearance.titlebar", "Title bar background", v => a.TitleBarColour = a.Own("TitleBarColour", v), () => a.TitleBarColour,
                "Document title-bar background in Custom theme.", "title bar chrome background", "appearance.thememode", "Custom"),
            Colour(Appearance, "Custom palette", "appearance.tabactive", "Selected tab background", v => a.ActiveTabColour = a.Own("ActiveTabColour", v), () => a.ActiveTabColour,
                "Background of the active document tab.", "tab active selected background", "appearance.thememode", "Custom"),
            Colour(Appearance, "Custom palette", "appearance.tabhover", "Tab hover background", v => a.TabHoverColour = a.Own("TabHoverColour", v), () => a.TabHoverColour,
                "Background when the pointer is over a document tab.", "tab hover background", "appearance.thememode", "Custom"),
            Colour(Appearance, "Custom palette", "appearance.text", "Text colour", v => a.Text = a.Own("Text", v), () => a.Text,
                "Primary interface text in Custom theme.", "theme foreground font colour", "appearance.thememode", "Custom"),
            Colour(Appearance, "Custom palette", "appearance.muted", "Muted text colour", v => a.Muted = a.Own("Muted", v), () => a.Muted,
                "Secondary labels and hints in Custom theme.", "theme secondary grey", "appearance.thememode", "Custom"),
            Colour(Appearance, "Custom palette", "appearance.timelinescrollbar", "Timeline scrollbar handle", v => a.TimelineScrollBarThumbColour = v, () => a.TimelineScrollBarThumbColour,
                "Colour of the timeline scrollbar handle; it turns lighter when the mouse is over it.", "timeline scrollbar scroll thumb navigation bar"),
            Choice(Appearance, "Typography", "appearance.fontsource", "Interface font", v => a.FontSource = TabForge.Platform.Theme.FontProvider.SourceOf(v), () => TabForge.Platform.Theme.FontProvider.ChoiceOf(a.FontSource), TabForge.Platform.Theme.FontProvider.SourceChoices,
                "Windows uses Segoe UI and Consolas. TabForge bundled uses Inter and JetBrains Mono, shipped with the app, so text looks the same on every system. Applies to the interface, the score and the tab numbers.",
                "font typeface inter jetbrains mono segoe consolas bundled windows"),
            Text(Appearance, "Typography", "appearance.font", "Interface font family", v => a.FontFamily = v, () => a.FontFamily,
                "The typeface used by the interface.", "font family typeface"),
            Number(Appearance, "Typography", "appearance.fontsize", "Interface font size", v => a.FontSize = v, () => a.FontSize, 8, 24,
                "Text size in points, before the interface size setting is applied.", "font size text scale", "pt", 1, 0),
            Number(Appearance, "Typography", "appearance.iconsize", "Icon size", v => a.IconSize = v, () => a.IconSize, 10, 22,
                "Size of toolbar and title-bar icons, in pixels.", "icon px glyph toolbar buttons", "px", 1, 0),
            // Tombstone: "appearance.toolbaricons" (Show icons on the tool palette) is hidden; the palette is icon-only (no text-label mode exists). AppearanceSettings.ShowToolbarIcons is kept.

        };
    }
}
