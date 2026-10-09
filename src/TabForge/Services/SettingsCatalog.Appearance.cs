using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Services;

// Owns: the Appearance part of the settings catalogue (rows in Preferences order).
// Does not own: page layout (SettingsCatalog.Layout) or the stored values (AppSettings).
// Tests: TestPreferencesCatalog, TestSettingsFileSplitSnapshots.
public static partial class SettingsCatalog
{
    private static List<SettingDescriptor> AppearanceRows(AppSettings s)
    {
        var a = s.Appearance!;
        var timeline = s.Timeline!;
        return new List<SettingDescriptor>
        {
            // Appearance
            Choice(Appearance, "Interface", "appearance.thememode", "Theme", v => ThemeService.ApplyPreset(a, v), () => a.ThemeMode,
                new[] { "System", "Dark", "Light", "Custom" }, "Theme preset: Dark, Light or System sets the interface, accent and score page colours at once; any colour can still be changed afterwards. Custom keeps your colours as they are.", "theme system dark light custom"),
            Number(Appearance, "Interface", "appearance.uiscale", "UI scale", v => a.UiScale = v, () => a.UiScale, 0.8, 1.5,
                "Scale interface text and icon controls.", "zoom interface size scale dpi", "x", 0.05, 2),
            Int(Appearance, "Interface", "appearance.tracktint", "Track colour tint", v => a.TrackTintPercent = v, () => a.TrackTintPercent, 0, 60,
                "How strongly each track's row and timeline lane take the track's colour (0 = off). Subtle and darker in the dark theme. Tracks can turn it off in Track properties.",
                "track colour color tint background row lane intensity transparency", unit: "%"),
            Choice(Appearance, "Interface", "appearance.density", "Spacing", v => a.Density = v, () => a.Density,
                new[] { "Compact", "Comfortable", "Spacious" }, "How tightly the toolbar and buttons are packed.", "compact comfortable spacious density"),
            Bool(Appearance, "Motion", "appearance.reduceanimations", "Reduce animations", v => a.ReduceAnimations = v, () => a.ReduceAnimations,
                "Disable short tab and timeline drag transitions.", "motion accessibility reduce animation"),
            Int(Appearance, "Track colours", "appearance.mutedtrackdim", "Muted track dimming", v => a.MutedTrackDimPercent = v, () => a.MutedTrackDimPercent, 0, 100,
                "How strongly a muted track is greyed out in the track list and in its timeline lane (0 = not dimmed, 100 = strongest). Tracks that are only silent because another track is soloed are not greyed.",
                "muted track dim grey gray dimming dimmed mute lane row opacity", unit: "%"),
            Number(Appearance, "Motion", "appearance.animationspeed", "Animation speed", v => a.AnimationSpeed = v, () => a.AnimationSpeed, 0.25, 2,
                "Scale the duration of tab and timeline drag animations.", "motion animation faster slower duration", "x", 0.05, 2),
            Colour(Appearance, "Interface colours", "appearance.accent", "Accent colour", v => a.Accent = v, () => a.Accent,
                "Focus, active controls and the primary selection colour.", "theme accent selection focus"),
            Colour(Appearance, "Interface colours", "appearance.selection", "Selection colour", v => a.SelectionColour = v, () => a.SelectionColour,
                "Colour used for selected score ranges.", "score selected range highlight"),
            Colour(Appearance, "Interface colours", "appearance.hover", "Hover colour", v => a.HoverColour = v, () => a.HoverColour,
                "Base colour for score hover feedback.", "score hover pointer"),
            Colour(Appearance, "Custom palette", "appearance.background", "Window background", v => a.Background = v, () => a.Background,
                "Background behind panels in Custom theme.", "theme background dark", "appearance.thememode", "Custom"),
            Colour(Appearance, "Custom palette", "appearance.panel", "Panel background", v => a.Panel = v, () => a.Panel,
                "Toolbar and side-panel background in Custom theme.", "theme panel chrome", "appearance.thememode", "Custom"),
            Colour(Appearance, "Custom palette", "appearance.titlebar", "Title bar background", v => a.TitleBarColour = v, () => a.TitleBarColour,
                "Document title-bar background in Custom theme.", "title bar chrome background", "appearance.thememode", "Custom"),
            Colour(Appearance, "Custom palette", "appearance.tabactive", "Selected tab background", v => a.ActiveTabColour = v, () => a.ActiveTabColour,
                "Background of the active document tab.", "tab active selected background", "appearance.thememode", "Custom"),
            Colour(Appearance, "Custom palette", "appearance.tabhover", "Tab hover background", v => a.TabHoverColour = v, () => a.TabHoverColour,
                "Background when the pointer is over a document tab.", "tab hover background", "appearance.thememode", "Custom"),
            Colour(Appearance, "Custom palette", "appearance.text", "Text colour", v => a.Text = v, () => a.Text,
                "Primary interface text in Custom theme.", "theme foreground font colour", "appearance.thememode", "Custom"),
            Colour(Appearance, "Custom palette", "appearance.muted", "Muted text colour", v => a.Muted = v, () => a.Muted,
                "Secondary labels and hints in Custom theme.", "theme secondary grey", "appearance.thememode", "Custom"),
            Colour(Appearance, "Custom palette", "appearance.timelinescrollbar", "Timeline scrollbar thumb", v => a.TimelineScrollBarThumbColour = v, () => a.TimelineScrollBarThumbColour,
                "Colour of the arrangement timeline scrollbar thumb; the hover colour is derived from this value.", "timeline scrollbar scroll thumb navigation bar"),
            Text(Appearance, "Typography", "appearance.font", "Interface font", v => a.FontFamily = v, () => a.FontFamily,
                "Font family used by the interface.", "font family typeface"),
            Number(Appearance, "Typography", "appearance.fontsize", "Interface font size", v => a.FontSize = v, () => a.FontSize, 8, 24,
                "Base text size before UI scale is applied.", "font size text scale", "pt", 1, 0),
            Number(Appearance, "Typography", "appearance.iconsize", "Icon size", v => a.IconSize = v, () => a.IconSize, 10, 22,
                "Base size of toolbar and title-bar icons.", "icon glyph toolbar buttons", "px", 1, 0),
            Bool(Appearance, "Typography", "appearance.toolbaricons", "Show icons on the tool palette", v => a.ShowToolbarIcons = v, () => a.ShowToolbarIcons,
                "Use glyphs instead of text-only toolbar buttons.", "icons glyphs toolbar"),

        };
    }
}
