using TabForge.Models;

namespace TabForge.Services;

// Owns: the Score part of the settings catalogue (rows in Preferences order).
// Does not own: page layout (SettingsCatalog.Layout) or the stored values (AppSettings).
// Tests: TestSettingsFileSplitSnapshots.
public static partial class SettingsCatalog
{
    private static List<SettingDescriptor> ScoreRows(AppSettings s)
    {
        var a = s.Appearance!;
        return new List<SettingDescriptor>
        {
            // Score & notation
            Choice(Score, "Notation", "score.defaultnotation", "Default score display", v => { s.Notation = NotationNames.Normalize(v).ToString(); s.NotationPreferenceSet = true; },
                () => NotationNames.Display(s.NotationPreferenceSet ? NotationNames.Normalize(s.Notation) : NotationMode.TabAndStaff),
                NotationNames.All.ToArray(), "Default display for new scores: tablature with standard notation, tablature only, or standard notation only.", "notation tablature tab staff tabandstaff tabonly staffonly default display"),
            Text(Score, "Notation", "appearance.scorefont", "Score font", v => a.ScoreFontFamily = v, () => a.ScoreFontFamily,
                "Typeface for score text, fret numbers and annotations.", "score notation font family typeface"),
            Number(Score, "Notation", "appearance.scorefontsize", "Score text size", v => a.ScoreTextSize = v, () => a.ScoreTextSize, 8, 24,
                "Size of the text on the score page.", "score notation text size fret number", "px", 0.5, 1),
            Bool(Score, "Notation", "appearance.scorebold", "Bold score text", v => a.ScoreTextBold = v, () => a.ScoreTextBold,
                "Render score text in bold.", "score notation weight"),
            Bool(Score, "Notation", "appearance.scoreitalic", "Italic score text", v => a.ScoreTextItalic = v, () => a.ScoreTextItalic,
                "Render score text in italic.", "score notation style"),
            Number(Score, "Layout", "appearance.spacing", "Tablature spacing", v => a.ScoreSpacing = v, () => a.ScoreSpacing, 0.85, 1.6,
                "Spread the tab lines and fret numbers out or squeeze them together (1 = normal).", "tablature fret size string gap readability", "x", 0.05, 2),
            Number(Score, "Layout", "score.systemspacing", "Space between lines of music", v => a.SystemVerticalSpacing = v, () => a.SystemVerticalSpacing, 0.7, 1.6,
                "Stretch or shrink the gap between lines of music (1 = normal).", "system vertical staff system vertical gap spacing", "x", 0.05, 2),
            Number(Score, "Layout", "score.measurespacing", "Bar spacing", v => a.MeasureHorizontalSpacing = v, () => a.MeasureHorizontalSpacing, 0.8, 1.6,
                "Widen or tighten the bars (1 = normal) while keeping notes and markings clear of each other.", "measure horizontal bar width spacing", "x", 0.05, 2),
            Choice(Score, "Layout", "score.ledger", "Ledger lines", v => a.LedgerLines = v, () => a.LedgerLines,
                new[] { "Standard", "Minimal", "Hidden" }, "Choose the notation ledger-line style.", "staff ledger lines minimal standard hidden"),
            Number(Score, "Layout", "score.staffopacity", "Staff line strength", v => a.StaffLineOpacity = v / 100, () => a.StaffLineOpacity * 100, 0, 100,
                "How visible the staff, tab and ledger lines are. They always share one colour (set under Score colours) and one strength.", "staff tab string ledger line alpha percent opacity", "%", 1, 0),
            Bool(Score, "Labels", "score.barnumbers", "Show bar numbers", v => a.ShowScoreBarNumbers = v, () => a.ShowScoreBarNumbers,
                "Show measure numbers on the score page.", "measure number bar label"),
            Int(Score, "Labels", "score.barnumberfrequency", "Bar-number frequency", v => a.ScoreBarNumberFrequency = v, () => a.ScoreBarNumberFrequency, 1, 16,
                "Show a number every this many bars; 1 numbers every bar.", "bar numbers every frequency interval"),
            Bool(Score, "Labels", "score.sectionheadings", "Show section headings", v => a.ShowSectionHeadings = v, () => a.ShowSectionHeadings,
                "Show marker names above the score.", "section marker title heading label"),
            Bool(Score, "Labels", "score.dynamics", "Show dynamics", v => a.ShowDynamics = v, () => a.ShowDynamics,
                "Engrave dynamics markings (ppp to fff) under the staff where the dynamic changes.", "dynamics markings forte piano ppp mf fff loudness"),
            Number(Score, "Highlighting", "score.hoverintensity", "Hover-highlight intensity", v => a.HoverHighlightIntensity = v / 100, () => a.HoverHighlightIntensity * 100, 0, 100,
                "How strong the highlight under the mouse is.", "hover pointer highlight opacity percent", "%", 1, 0),
            Bool(Score, "Highlighting", "score.noteoutline", "Outline the spot under the mouse", v => a.ShowNoteOutline = v, () => a.ShowNoteOutline,
                "Draw a thin outline around the spot under the mouse; its width shows how long the note would be.", "hover outline cursor note length box cell"),
            Number(Score, "Highlighting", "score.selectionintensity", "Selection-highlight intensity", v => a.SelectionHighlightIntensity = v / 100, () => a.SelectionHighlightIntensity * 100, 0, 100,
                "How strongly the selected part of the score is highlighted.", "selected range highlight opacity percent", "%", 1, 0),
            Choice(Score, "Score colours", "appearance.paper", "Score paper", v => a.ScorePaper = a.Own("ScorePaper", v), () => a.ScorePaper,
                new[] { "Dark", "Light" }, "Choose dark or light score paper.", "page paper background print"),
            Colour(Score, "Score colours", "appearance.scorepaper.dark", "Dark score page", v => a.DarkScorePaperColour = a.Own("DarkScorePaperColour", v), () => a.DarkScorePaperColour,
                "Background of the dark score page.", "score paper dark background"),
            Colour(Score, "Score colours", "appearance.scorepaper.light", "Light score page", v => a.LightScorePaperColour = a.Own("LightScorePaperColour", v), () => a.LightScorePaperColour,
                "Background of the light score page.", "score paper light background"),
            Colour(Score, "Score colours", "appearance.scoreink.dark", "Dark-mode notation", v => a.DarkScoreInkColour = a.Own("DarkScoreInkColour", v), () => a.DarkScoreInkColour,
                "Staff, TAB and title text on dark score paper.", "score notation ink dark"),
            Colour(Score, "Score colours", "appearance.scoreink.light", "Light-mode notation", v => a.LightScoreInkColour = a.Own("LightScoreInkColour", v), () => a.LightScoreInkColour,
                "Staff, TAB and title text on light score paper.", "score notation ink light"),
            Colour(Score, "Score colours", "appearance.scorelines.dark", "Dark-mode staff lines", v => a.DarkScoreLinesColour = a.Own("DarkScoreLinesColour", v), () => a.DarkScoreLinesColour,
                "Staff and TAB line colour on dark score paper.", "score staff line dark"),
            Colour(Score, "Score colours", "appearance.scorelines.light", "Light-mode staff lines", v => a.LightScoreLinesColour = a.Own("LightScoreLinesColour", v), () => a.LightScoreLinesColour,
                "Staff and TAB line colour on light score paper.", "score staff line light"),
            Colour(Score, "Score colours", "appearance.cursor", "Edit cursor colour", v => a.CursorColour = a.Own("CursorColour", v), () => a.CursorColour,
                "Colour of the edit caret when playback is stopped.", "edit cursor caret"),
            // Page layout: the score right-click menu's Page / Continuous and Vertical / Horizontal choices (saved as the layout songs open with).
            Choice(Score, "Page layout", "score.pagelayout", "Score page layout", v => s.PreferredContinuousScoreView = v == "Continuous",
                () => s.PreferredContinuousScoreView ? "Continuous" : "Page", new[] { "Page", "Continuous" },
                "Page: the score is drawn as paper pages. Continuous: one seamless sheet. Used for every song you open; right-click the score to change the open song.",
                "page paper continuous seamless score layout view right click"),
            Choice(Score, "Page layout", "score.scrolling", "Score scrolling", v => s.PreferredHorizontalScoreView = v == "Horizontal",
                () => s.PreferredHorizontalScoreView ? "Horizontal" : "Vertical", new[] { "Vertical", "Horizontal" },
                "Vertical: lines of music wrap and you scroll down. Horizontal: one long line you scroll to the right. Used for every song you open; right-click the score to change the open song.",
                "vertical horizontal scroll wrap line score layout direction"),
            Button(Score, "Notation", "score.textfonts", "Text & fonts for each part of the score",
                "Choose the font, size and style of each kind of score text (title and header, chord names, lyrics, fret numbers, techniques, bar information) separately.",
                "score text fonts per area title lyrics chord names fret numbers style"),

        };
    }
}
