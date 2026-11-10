using TabForge.Models;

namespace TabForge.Services;

// Owns: the Fretboard part of the settings catalogue (rows in Preferences order).
// Does not own: page layout (SettingsCatalog.Layout) or the stored values (AppSettings).
// Tests: TestSettingsFileSplitSnapshots.
public static partial class SettingsCatalog
{
    private static List<SettingDescriptor> FretboardRows(AppSettings s)
    {
        var rows = FretboardDisplayRows(s);
        rows.AddRange(FretboardNoteColourRows(s));
        rows.AddRange(FretboardSizeRows(s));
        rows.AddRange(ScaleReadoutRows(s));
        return rows;
    }

    private static List<SettingDescriptor> FretboardDisplayRows(AppSettings s)
    {
        var a = s.Appearance!;
        var au = s.Audio!;
        var ed = s.Editing!;
        var fv = s.Follow!;
        return new List<SettingDescriptor>
        {
            // Fretboard (legacy values remain in Editing for file compatibility)
            Bool(Fretboard, "Appearance","appearance.fretboard", "Show the fretboard (Tab mode)", v => a.ShowFretboard = v, () => a.ShowFretboard,
                "Show the instrument/fretboard panel in Tab mode (the score). Its own value: the piano roll keeps its own.", "instrument panel hide tab score"),
            Bool(Fretboard, "Appearance","appearance.fretboard.pianoroll", "Show the fretboard in the piano roll", v => a.ShowFretboardInPianoRoll = v, () => a.ShowFretboardInPianoRoll,
                "Show the instrument/fretboard panel in the piano roll. Off by default and remembered separately, so it never changes Tab mode.", "instrument panel hide piano roll"),
            Choice(Fretboard, "Appearance","fretboard.atstartup", "Show fretboard at startup", v => a.FretboardAtStartup = v, () => a.FretboardAtStartup,
                FretboardStartupModes.All, "Remember last: start with the fretboard as you left it. Always: show it at every start. Never: start with it hidden. The toolbar toggle works as usual.", "fretboard startup launch show hide remember always never"),
            Choice(Fretboard, "Appearance","fretboard.position", "Fretboard position", v => a.FretboardPosition = v, () => a.FretboardPosition,
                new[] { "Left", "Centre", "Right" }, "Snap the fretboard horizontally.", "fretboard alignment position"),
            Choice(Fretboard, "Appearance","editing.frets", "Fretboard frets", v => ed.FretboardFrets = int.Parse(v), () => ed.FretboardFrets.ToString(),
                new[] { "12", "24" }, "Show 12 or 24 frets.", "fret count range frets shown"),
            Choice(Fretboard, "Appearance","fretboard.look", "Fretboard look", v => FretboardStyles.Apply(ed, FretboardStyles.OfPreset(v)), () => FretboardStyles.PresetOf(FretboardStyles.LookOf(ed)),
                FretboardStyles.PresetNames, "A ready-made fretboard: wood, binding, inlays, capo and strings together. Changing any of the rows below makes it Custom.", "fretboard look preset style theme wood rosewood maple ebony pearl blocks"),
            Choice(Fretboard, "Appearance","fretboard.wood", "Fretboard wood", v => ed.FretboardWood = v, () => ed.FretboardWood,
                FretboardStyles.Woods, "The wood the board is made of.", "fretboard wood rosewood ebony maple walnut pau ferro composite"),
            Int(Fretboard, "Appearance", "fretboard.brightness", "Fretboard brightness", v => ed.FretboardBrightness = v, () => ed.FretboardBrightness,
                FretboardStyles.MinBrightness, FretboardStyles.MaxBrightness,
                "Brightness of the physical neck, including wood, inlays, strings and capo. 65% is the darker default; 100% is the original. Does not dim notes or scale highlights.",
                "fretboard darkness brightness contrast wood strings", unit: "%", step: FretboardStyles.BrightnessStep),
            Int(Fretboard, "Appearance", "fretboard.notebrightness", "Note brightness", v => ed.FretboardNoteBrightness = v, () => ed.FretboardNoteBrightness,
                FretboardStyles.MinBrightness, FretboardStyles.MaxBrightness,
                "Brightness of cursor and playback notes, including their legend colours. Keeps the now/next/upcoming/recent colours distinct; independent of inlay brightness and scale highlights.",
                "fretboard note brightness contrast marker", unit: "%", step: FretboardStyles.BrightnessStep),
            Choice(Fretboard, "Appearance","fretboard.binding", "Fretboard binding", v => ed.FretboardBinding = v, () => ed.FretboardBinding,
                FretboardStyles.Bindings, "The strip along the board's top and bottom edges. None leaves the bare wood edge.", "fretboard binding edge purfling cream ivory gold tortoise"),
            Choice(Fretboard, "Appearance","fretboard.inlayshape", "Inlay shape", v => ed.FretboardInlayShape = v, () => ed.FretboardInlayShape,
                FretboardStyles.InlayShapes, "The position markers on frets 3, 5, 7, 9, 12, 15, 17, 19, 21 and 24 (double at 12 and 24).", "fretboard inlay markers dots blocks trapezoids shark fins"),
            Choice(Fretboard, "Appearance","fretboard.inlaymaterial", "Inlay material", v => ed.FretboardInlayMaterial = v, () => ed.FretboardInlayMaterial,
                FretboardStyles.InlayMaterials, "What the inlays are cut from: pearl, ivory, abalone or paua.", "fretboard inlay material pearl ivory abalone paua"),
            Choice(Fretboard, "Appearance","fretboard.capolook", "Capo finish", v => ed.FretboardCapoLook = v, () => ed.FretboardCapoLook,
                FretboardStyles.CapoLooks, "The finish of the capo drawn on the fretboard.", "fretboard capo black bronze steel"),
            Choice(Fretboard, "Appearance","fretboard.stringlook", "String finish", v => ed.FretboardStringLook = v, () => ed.FretboardStringLook,
                FretboardStyles.StringLooks, "The colour of the strings.", "fretboard strings steel nickel gold black"),
            Choice(Fretboard, "Appearance","fretboard.vibration", "String vibration", v => ed.FretboardVibration = v, () => ed.FretboardVibration,
                FretboardStyles.VibrationLevels, "How far a sounding string swings. Mild by default; Off keeps every string still.", "fretboard string vibration shake swing off mild medium strong"),
            Choice(Fretboard, "Appearance","fretboard.vibrationspeed", "String vibration speed", v => ed.FretboardVibrationSpeed = v, () => ed.FretboardVibrationSpeed,
                FretboardStyles.VibrationSpeeds, "How quickly a sounding string swings.", "fretboard string vibration speed slow normal fast"),
            Bool(Fretboard, "Appearance","fretboard.stringnumbers", "Show string numbers", v => ed.FretboardStringNumbers = v, () => ed.FretboardStringNumbers,
                "Number the strings in the gutter left of the nut.", "fretboard string numbers labels gutter"),
            Bool(Fretboard, "Appearance","editing.lefthanded", "Left-handed fretboard", v => ed.LeftHanded = v, () => ed.LeftHanded,
                "Mirror the fretboard for left-handed playing.", "left handed mirror orientation"),
            Bool(Fretboard, "Appearance","editing.notenames", "Show note names on the fretboard", v => ed.ShowNoteNames = v, () => ed.ShowNoteNames,
                "Label cursor, playback and pointer-preview note dots with pitch names instead of fret numbers. Does not change the fret ruler or the separate scale-highlight style.", "pitch labels names"),
            Bool(Fretboard, "Preview", "editing.horizon.enabled", "Show look-ahead notes", v => ed.PreviewNotesEnabled = v, () => ed.PreviewNotesEnabled,
                "Show upcoming notes on the fretboard (not on the drum map).", "preview next future notes"),
            Bool(Fretboard, "Preview", "editing.horizon.previous", "Preview previous notes", v => ed.PreviewPreviousNotesEnabled = v, () => ed.PreviewPreviousNotesEnabled,
                "Show the grey trail of notes just played on the fretboard, keyboard and drum map.", "preview previous recent past notes trail faint show"),
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
            Choice(Fretboard, "Appearance","fretboard.scalestyle", "Scale highlight style", v => ed.ScaleHighlightStyle = v, () => ScaleHighlightStyles.Normalize(ed.ScaleHighlightStyle),
                ScaleHighlightStyles.All,
                "How the notes of a highlighted scale are marked on the fretboard and keyboard: soft dots, rings, shaded cells, note names or scale degrees. More options are in the fretboard's Scales popup.",
                "scale highlight style circles dots rings shaded cell tint names degrees appearance"),
            Choice(Fretboard, "Appearance","fretboard.scalecolour", "Scale highlight colour", v => ed.ScaleHighlightColour = v, () => ed.ScaleHighlightColour,
                ScaleHighlightStyles.Colours,
                "Colour of the scale highlight on the fretboard and keyboard.",
                "scale highlight colour color blue green amber purple red teal grey"),
            Int(Fretboard, "Appearance", "fretboard.scalestrength", "Scale highlight strength", v => ed.ScaleHighlightStrength = v, () => ed.ScaleHighlightStrength,
                ScaleHighlightStyles.MinStrength, ScaleHighlightStyles.MaxStrength,
                "How strong the scale highlight looks on the fretboard and keyboard: lower is dimmer, higher is brighter (100% is the standard look). The root note always stays stronger than the other scale notes.",
                "scale highlight strength opacity brightness dimmer brighter intensity transparency", unit: "%", step: ScaleHighlightStyles.StrengthStep),
            Bool(Fretboard, "Appearance", "fretboard.scaleroot", "Emphasise the root", v => ed.ScaleEmphasiseRoot = v, () => ed.ScaleEmphasiseRoot,
                "Draw the root note of a highlighted scale stronger than its other notes on the fretboard and keyboard.",
                "scale highlight root tonic emphasise emphasize stronger key note"),
            Bool(Fretboard, "Appearance", "fretboard.scaledimoutside", "Dim notes outside the scale", v => ed.ScaleDimOutside = v, () => ed.ScaleDimOutside,
                "While a scale is highlighted, dim the fretboard and keyboard notes that are not in it.",
                "scale highlight dim outside out of key notes fade grey"),
            Bool(Fretboard, "Appearance", "fretboard.scaleonlyinview", "Only on the frets in view", v => ed.ScaleOnlyInView = v, () => ed.ScaleOnlyInView,
                "Mark the scale only on the frets the fretboard shows now, not on every fret.",
                "scale highlight only frets in view visible shown range"),
            Choice(Fretboard, "Appearance","fretboard.markerbrightness", "Position inlay brightness", v => ed.FretMarkerBrightness = v, () => ed.FretMarkerBrightness,
                FretMarkerLevels.All,
                "How bright the fretboard position dots are. Original (the standard) is the plain, dimmer look; the other levels lift the dots towards white.",
                "fret marker dots inlay position brightness bright dim"),
            Choice(Fretboard, "Appearance","fretboard.numbersize", "Fret number size", v => ed.FretNumberSize = v, () => ed.FretNumberSize,
                FretNumberSizes.All,
                "Size of the fret numbers, technique tags and note bubbles on the fretboard (Large is the standard size).",
                "fret number size small medium large bubble label"),
            Int(Fretboard, "Appearance", "fretboard.markersize", "Note marker size", v => ed.FretMarkerSizePercent = v, () => ed.FretMarkerSizePercent, 60, 160,
                "Size of the note circles and their numbers on the fretboard (90% is the standard; 100% is the original size). Large sizes stop growing at the gap between strings.",
                "note marker size dot circle bubble number bigger smaller", "%"),
            Choice(Fretboard, "Appearance","fretboard.stringspacing", "String spacing", v => ed.FretStringSpacing = v, () => ed.FretStringSpacing,
                FretStringSpacings.All,
                "How far apart the strings are drawn relative to the fret width. Natural keeps real-fretboard proportions in any window shape; Wide stretches up to 1.5x natural.",
                "fretboard string spacing stretch compact natural wide tall portrait"),
            Int(Fretboard, "Preview", "editing.horizon", "Look-ahead notes", v => ed.PreviewHorizon = v, () => ed.PreviewHorizon, 1, 10,
                "Number of upcoming notes to display (not on the drum map).", "horizon preview upcoming next notes shown ahead", dependsOn: "editing.horizon.enabled"),
            Choice(Fretboard, "Preview", "editing.scale", "Scale highlight", v => ed.ShowScale(v == "Off" ? null : v),
                () => ed.ScaleHighlight ?? "Off", new[] { "Off" }.Concat(from root in MusicTheoryService.NoteNames from scale in new[] { "Major", "Natural Minor", "Minor Pentatonic", "Major Pentatonic", "Dorian", "Mixolydian", "Blues" } select $"{root} {scale}").ToArray(),
                "Highlight scale tones on the fretboard.", "scale key root highlight notes clear off none"),
            Bool(Fretboard, "Playback", "follow.fretboard", "Update the fretboard during playback", v => fv.FollowFretboard = v, () => fv.FollowFretboard,
                "Update fretboard note positions as playback moves.", "playback live update"),
        };
    }

    /// <summary>
    /// The legend's now, next and upcoming colours and the dot style (the Fretboard settings window's Note colours card). A colour row shows
    /// <see cref="FretNoteColours.ThemeDefault"/> while nothing is stored and writes through FretNoteColours.Set, so a reset or a picked
    /// default stores empty and the theme keeps choosing the colour.
    /// </summary>
    private static List<SettingDescriptor> FretboardNoteColourRows(AppSettings s)
    {
        var ed = s.Editing!;
        var rows = new List<SettingDescriptor>();
        foreach (var role in FretNoteColours.Roles)
        {
            var name = FretNoteColours.Name(role);
            rows.Add(Colour(Fretboard, "Appearance", FretNoteColours.SettingKey(role), name + " note colour",
                v => FretNoteColours.Set(ed, role, v), () => FretNoteColours.Get(ed, role) is { Length: > 0 } hex ? hex : FretNoteColours.ThemeDefault,
                $"The colour of the {name.ToLowerInvariant()} playing-note dots and their row in the legend beside the fretboard. Default follows the theme.",
                "fretboard legend note colour color dot playing now next upcoming look-ahead green amber blue reset default"));
        }
        rows.Add(Choice(Fretboard, "Appearance", "fretboard.dotstyle", "Note dot style", v => ed.FretDotStyle = FretNoteColours.NormalizeStyle(v), () => FretNoteColours.NormalizeStyle(ed.FretDotStyle),
            FretNoteColours.DotStyles, "Fill draws the playing-note dots solid; Ring draws an outline in the note's colour. The legend shows the same style.",
            "fretboard legend note dot style fill ring outline hollow solid circle playing now next upcoming"));
        return rows;
    }

    private static List<SettingDescriptor> FretboardSizeRows(AppSettings s)
    {
        var a = s.Appearance!;
        return new List<SettingDescriptor>
        {
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
