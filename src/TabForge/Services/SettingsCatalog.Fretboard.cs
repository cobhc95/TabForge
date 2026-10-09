using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Services;

// Owns: the Fretboard part of the settings catalogue (rows in Preferences order).
// Does not own: page layout (SettingsCatalog.Layout) or the stored values (AppSettings).
// Tests: TestPreferencesCatalog, TestSettingsFileSplitSnapshots.
public static partial class SettingsCatalog
{
    private static List<SettingDescriptor> FretboardRows(AppSettings s)
    {
        var a = s.Appearance!;
        var au = s.Audio!;
        var ed = s.Editing!;
        var fv = s.Follow!;
        return new List<SettingDescriptor>
        {
            // Fretboard (legacy values remain in Editing for file compatibility)
            Bool(Fretboard, "Appearance","appearance.fretboard", "Show the fretboard", v => a.ShowFretboard = v, () => a.ShowFretboard,
                "Show the instrument/fretboard panel.", "instrument panel hide"),
            Choice(Fretboard, "Appearance","fretboard.position", "Fretboard position", v => a.FretboardPosition = v, () => a.FretboardPosition,
                new[] { "Left", "Centre", "Right" }, "Snap the fretboard horizontally.", "fretboard alignment position"),
            Choice(Fretboard, "Appearance","editing.frets", "Fretboard frets", v => ed.FretboardFrets = int.Parse(v), () => ed.FretboardFrets.ToString(),
                new[] { "12", "24" }, "Show 12 or 24 frets.", "fret count range"),
            Bool(Fretboard, "Appearance","editing.lefthanded", "Left-handed fretboard", v => ed.LeftHanded = v, () => ed.LeftHanded,
                "Mirror the fretboard for left-handed playing.", "left handed mirror orientation"),
            Bool(Fretboard, "Appearance","editing.notenames", "Show note names on the fretboard", v => ed.ShowNoteNames = v, () => ed.ShowNoteNames,
                "Label frets with their note names.", "pitch labels names"),
            Bool(Fretboard, "Preview", "editing.horizon.enabled", "Show look-ahead notes", v => ed.PreviewNotesEnabled = v, () => ed.PreviewNotesEnabled,
                "Show upcoming notes on the fretboard.", "preview next future notes"),
            Choice(Fretboard, "Appearance","fretboard.instrumentview", "Default instrument view", v => ed.InstrumentView = v, () => ed.InstrumentView,
                InstrumentViews.All,
                "Match the instrument (default): stringed instruments get a fretboard with the track's own strings, drums get drum pads, and piano, winds and everything else get a keyboard. Or always show one view. Right-click the panel to change it for one track or all tracks.",
                "instrument view fretboard keyboard piano drums pads default show match"),
            Choice(Fretboard, "Appearance","fretboard.keyboardkeys", "Keyboard size", v => ed.KeyboardKeys = int.TryParse(v, out var k) ? k : 88, () => ed.KeyboardKeys.ToString(),
                InstrumentViews.KeyboardSizes.Select(k => k.ToString()).ToArray(),
                "Keys on the keyboard view: 88 is a full piano. Smaller keyboards follow the notes being played.",
                "keyboard piano keys size 88 76 61 49 37 25"),
            Choice(Fretboard, "Appearance","fretboard.keyboardcolours", "Keyboard key colours", v => ed.KeyboardKeyColours = v, () => ed.KeyboardKeyColours,
                KeyboardKeyStyles.All,
                "The keyboard view's white keys: match the theme (soft grey in the dark theme, white in the light theme), or always grey or always white.",
                "keyboard piano keys colour color grey gray white appearance"),
            Choice(Fretboard, "Appearance","fretboard.scalestyle", "Scale highlight style", v => ed.ScaleHighlightStyle = v, () => ed.ScaleHighlightStyle,
                ScaleHighlightStyles.All,
                "How the notes of a highlighted scale are marked on the fretboard and keyboard: shaded cells, small circles or rings. The root is always marked more strongly.",
                "scale highlight style circles dots rings shaded appearance"),
            Choice(Fretboard, "Appearance","fretboard.scalecolour", "Scale highlight colour", v => ed.ScaleHighlightColour = v, () => ed.ScaleHighlightColour,
                ScaleHighlightStyles.Colours,
                "Colour of the scale highlight on the fretboard and keyboard.",
                "scale highlight colour color blue green amber purple red teal grey"),
            Int(Fretboard, "Appearance", "fretboard.scalestrength", "Scale highlight strength", v => ed.ScaleHighlightStrength = v, () => ed.ScaleHighlightStrength,
                ScaleHighlightStyles.MinStrength, ScaleHighlightStyles.MaxStrength,
                "How strong the scale highlight looks on the fretboard and keyboard: lower is dimmer, higher is brighter (100% is the standard look). The root note always stays stronger than the other scale notes.",
                "scale highlight strength opacity brightness dimmer brighter intensity transparency", unit: "%", step: ScaleHighlightStyles.StrengthStep),
            Choice(Fretboard, "Appearance","fretboard.markercolour", "Fret marker colour", v => ed.FretMarkerColour = v, () => ed.FretMarkerColour,
                FretMarkerLevels.Colours,
                "Colour of the position dots on the fretboard (frets 3, 5, 7, 9, 12...). White is the standard; Default uses the theme's own dot colour.",
                "fret marker dots inlay position colour color"),
            Choice(Fretboard, "Appearance","fretboard.markerbrightness", "Fret marker brightness", v => ed.FretMarkerBrightness = v, () => ed.FretMarkerBrightness,
                FretMarkerLevels.All,
                "How bright the fretboard position dots are. Original (the standard) is the plain, dimmer look; the other levels lift the dots towards white.",
                "fret marker dots inlay position brightness bright dim"),
            Choice(Fretboard, "Appearance","fretboard.numbersize", "Fret number size", v => ed.FretNumberSize = v, () => ed.FretNumberSize,
                FretNumberSizes.All,
                "Size of the fret numbers, technique tags and note bubbles on the fretboard (Large is the standard size).",
                "fret number size small medium large bubble label"),
            Int(Fretboard, "Appearance", "fretboard.markersize", "Note marker size", v => ed.FretMarkerSizePercent = v, () => ed.FretMarkerSizePercent, 60, 160,
                "Size of the note circles and their numbers on the fretboard (90% is the standard; 100% is the original size). Large sizes stop growing at the gap between strings.",
                "note marker size circle bubble number bigger smaller", "%"),
            Choice(Fretboard, "Appearance","fretboard.stringspacing", "String spacing", v => ed.FretStringSpacing = v, () => ed.FretStringSpacing,
                FretStringSpacings.All,
                "How far apart the strings are drawn relative to the fret width. Natural keeps real-fretboard proportions in any window shape; Wide stretches up to 1.5x natural.",
                "fretboard string spacing stretch compact natural wide tall portrait"),
            Choice(Fretboard, "Appearance","audio.fretboardstyle", "Fretboard style", v => au.FretboardStyle = v, () => au.FretboardStyle,
                new[] { "TabForge", "GP5: Beat", "GP5: Beat + next beat", "GP5: Beat + bar", "GP5: Bar" },
                "TabForge previews a set number of upcoming notes. The beat and bar layouts follow the score instead: the current beat, the next beat, or every note of the current bar.",
                "fretboard style gp5 look preview red show beat bar next"),
            Int(Fretboard, "Preview", "editing.horizon", "Look-ahead notes", v => ed.PreviewHorizon = v, () => ed.PreviewHorizon, 1, 10,
                "Number of upcoming notes to display.", "horizon preview upcoming", dependsOn: "editing.horizon.enabled"),
            Choice(Fretboard, "Preview", "editing.scale", "Scale highlight", v => ed.ScaleHighlight = v == "Off" ? null : v,
                () => ed.ScaleHighlight ?? "Off", new[] { "Off" }.Concat(from root in MusicTheoryService.NoteNames from scale in new[] { "Major", "Natural Minor", "Minor Pentatonic", "Major Pentatonic", "Dorian", "Mixolydian", "Blues" } select $"{root} {scale}").ToArray(),
                "Highlight scale tones on the fretboard.", "scale key root highlight notes"),
            Bool(Fretboard, "Playback", "follow.fretboard", "Update the fretboard during playback", v => fv.FollowFretboard = v, () => fv.FollowFretboard,
                "Update fretboard note positions as playback moves.", "playback live update"),
            // Size and per-track views: the fretboard right-click menu's "Lock fretboard size", "Show this track as" and "Show all tracks as".
            Choice(Fretboard, "Size", "fretboard.dockposition", "Pane position (top / bottom)", v => a.FretboardAtBottom = v == "Bottom", () => a.FretboardAtBottom ? "Bottom" : "Top",
                new[] { "Top", "Bottom" }, "Top: above the score. Bottom: below the score, above the timeline. Dragging the pane still works.",
                "fretboard keyboard instrument position top bottom above below score timeline move dock"),
            Bool(Fretboard, "Size", "fretboard.locksize", "Lock fretboard size", v => a.LockInstrumentSize = v, () => a.LockInstrumentSize,
                "Locked: dragging the pane's edge does not resize the fretboard or keyboard. Unlocked: the drawing scales with the pane (between 0.7x and 2x). The pane never clips: it scrolls when it is too small.",
                "lock fretboard keyboard size resize pane splitter height scale drag", hotkey: "View.LockInstrumentSize"),
            Button(Fretboard, "Instrument view", "fretboard.showallas", "Show all tracks as",
                "Use the default instrument view above for every track of the open song, replacing any view you chose for a single track (right-click the fretboard > Show this track as).",
                "show all tracks as instrument view fretboard keyboard drums apply every track reset per track"),

        };
    }
}
