using System;
using System.Text.Json;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Services;
using TabForge.Views;
using TabForge.Views.Score;

namespace TabForge.Avalonia;

// Owns: copying the Score page's settings rows onto the score view: the score text style (ScoreText.ConfigureStyle), the layout spacing,
//   labels and staff lines, the paper, ink, staff-line and cursor colours, the highlight values, and the page layout (continuous, horizontal).
//   It lays the score out again only when a layout row changed and repaints it for every other change.
// Does not own: the settings file and its validation (MainWindowSettings, SettingsValidator), the score's drawing and layout (Core
//   ScoreRenderer, ScoreLayoutEngine), the right-click rows that set the same page layout (MainWindowScoreRows).
// Tests: TabForge.Avalonia.Tests.ScoreSettingsBindingTests.

/// <summary>The score rows whose change needs the layout again (spacing, text style, labels, staff lines).</summary>
internal readonly record struct ScoreLayoutRows(double Spacing, double SystemVerticalSpacing, double MeasureHorizontalSpacing,
    LedgerLineMode LedgerLines, double StaffLineOpacity, bool ShowBarNumbers, int BarNumberFrequency, bool ShowSectionHeadings,
    bool ShowDynamics, string FontFamily, double TextSize, bool TextBold, bool TextItalic);

/// <summary>The colours, highlight values and page flags Apply wrote onto the score, read back after the write: a change in any of them repaints it.</summary>
internal readonly record struct ScoreLookValues(UiColor DarkPaper, UiColor LightPaper, UiColor DarkInk, UiColor LightInk, UiColor DarkStaffLine,
    UiColor LightStaffLine, UiColor Cursor, UiColor Hover, double HoverIntensity, bool NoteOutline, double SelectionIntensity, bool CenterSystems, bool HorizontalScroll,
    UiColor Accent, UiColor Selection);

/// <summary>Keeps the score view in step with the Score page of the Settings window.</summary>
internal sealed class ScoreSettingsBinding
{
    private readonly ScorePreviewView _score;
    private ScoreLayoutRows? _appliedLayout;
    // The per-area text styles as the JSON last applied; key: the JSON of the styles; ceiling: one string; thread: the UI thread.
    private string? _appliedTextAreas;
    private bool? _appliedContinuous;
    private bool? _appliedHorizontal;
    private ScoreLookValues? _appliedLook;
    private int _refreshCount;

    /// <summary>How many times the score was repainted by a settings change (a save that changes no score value repaints nothing).</summary>
    internal int RefreshCount => _refreshCount;

    /// <summary>Applies <paramref name="settings"/> to <paramref name="score"/> at once.</summary>
    public ScoreSettingsBinding(ScorePreviewView score, AppSettings settings)
    {
        _score = score;
        Apply(settings);
    }

    /// <summary>Copies the Score page rows from <paramref name="settings"/>; lays the score out again only when a layout row changed.</summary>
    public void Apply(AppSettings settings)
    {
        var appearance = settings.Appearance;
        var look = _score.Probe.Appearance;
        ScoreText.ConfigureStyle(appearance, new AvaloniaPathsAndText());
        // The per-area text styles change the text's layout, so a change of them lays the score out again (compared as the JSON of the styles).
        var textAreas = JsonSerializer.Serialize(appearance.ScoreTextAreas);
        ScoreText.ConfigureAreas(appearance.ScoreTextAreas);
        var textAreasChanged = textAreas != _appliedTextAreas;
        _appliedTextAreas = textAreas;
        ApplyColours(appearance, look);
        look.HoverHighlightIntensity = appearance.HoverHighlightIntensity;
        look.ShowNoteOutline = appearance.ShowNoteOutline;
        look.SelectionHighlightIntensity = appearance.SelectionHighlightIntensity;

        var layout = new ScoreLayoutRows(appearance.ScoreSpacing, appearance.SystemVerticalSpacing, appearance.MeasureHorizontalSpacing,
            LedgerLinesOf(appearance.LedgerLines), appearance.StaffLineOpacity, appearance.ShowScoreBarNumbers, appearance.ScoreBarNumberFrequency,
            appearance.ShowSectionHeadings, appearance.ShowDynamics, ScoreText.FontFamily, ScoreText.TextSize, ScoreText.TextBold, ScoreText.TextItalic);
        var layoutChanged = layout != _appliedLayout || textAreasChanged;
        if (layoutChanged)
        {
            _appliedLayout = layout;
            look.ScoreSpacing = layout.Spacing;
            look.SystemVerticalSpacing = layout.SystemVerticalSpacing;
            look.MeasureHorizontalSpacing = layout.MeasureHorizontalSpacing;
            look.LedgerLines = layout.LedgerLines;
            look.ShowBarNumbers = layout.ShowBarNumbers;
            look.BarNumberFrequency = layout.BarNumberFrequency;
            look.ShowSectionHeadings = layout.ShowSectionHeadings;
            look.ShowDynamics = layout.ShowDynamics;
        }

        var pageChanged = ApplyPageLayout(settings, look);
        if (layoutChanged || pageChanged) _score.Probe.InvalidateLayout();
        var values = new ScoreLookValues(look.DarkPaperColor, look.LightPaperColor, look.DarkInkColor, look.LightInkColor, look.DarkStaffLineColor,
            look.LightStaffLineColor, look.CursorColor, look.HoverColor, look.HoverHighlightIntensity, look.ShowNoteOutline, look.SelectionHighlightIntensity,
            look.CenterSystems, _score.Probe.HorizontalScroll, look.AccentColor, look.SelectionColor);
        if (!layoutChanged && !pageChanged && values == _appliedLook) return;
        _appliedLook = values;
        _refreshCount++;
        _score.Refresh();
    }

    /// <summary>Sets the paper, ink, staff-line and cursor colours of both papers.</summary>
    internal static void ApplyColours(AppearanceSettings appearance, ScoreAppearance look)
    {
        look.DarkPaperColor = ParseOr(appearance.DarkScorePaperColour, Palette.Get("ScoreDarkPaperDefault"));
        look.LightPaperColor = ParseOr(appearance.LightScorePaperColour, Palette.Get("ScoreLightPaperDefault"));
        look.DarkInkColor = ParseOr(appearance.DarkScoreInkColour, Palette.Get("ScoreDarkInkDefault"));
        look.LightInkColor = ParseOr(appearance.LightScoreInkColour, Palette.Get("ScoreLightInkDefault"));
        look.DarkStaffLineColor = ThemeColours.WithOpacity(ParseOr(appearance.DarkScoreLinesColour, Palette.Get("ScoreDarkStaffLineDefault")), appearance.StaffLineOpacity);
        look.LightStaffLineColor = ThemeColours.WithOpacity(ParseOr(appearance.LightScoreLinesColour, Palette.Get("ScoreLightStaffLineDefault")), appearance.StaffLineOpacity);
        look.CursorColor = ParseOr(appearance.CursorColour, Palette.Get("DefaultCursor"));
        look.HoverColor = ParseOr(appearance.HoverColour, Palette.Get("DefaultHover"));
        look.AccentColor = ParseOr(appearance.Accent, Palette.Get("DefaultAccent"));
        look.SelectionColor = ParseOr(appearance.SelectionColourInUse(), Palette.Get("DefaultSelection"));
    }

    /// <summary>Continuous and horizontal follow their settings when the setting changes, as the other views do; true when either changed.</summary>
    private bool ApplyPageLayout(AppSettings settings, ScoreAppearance look)
    {
        var continuous = settings.PreferredContinuousScoreView;
        var horizontal = settings.PreferredHorizontalScoreView;
        var changed = continuous != _appliedContinuous || horizontal != _appliedHorizontal;
        if (continuous != _appliedContinuous) { _appliedContinuous = continuous; look.CenterSystems = continuous; }
        if (horizontal != _appliedHorizontal) { _appliedHorizontal = horizontal; _score.Probe.HorizontalScroll = horizontal; }
        return changed;
    }

    /// <summary>The ledger line mode named in the settings; anything unknown gives the default, Minimal.</summary>
    private static LedgerLineMode LedgerLinesOf(string name) =>
        Enum.TryParse<LedgerLineMode>(name, true, out var mode) && Enum.IsDefined(mode) ? mode : LedgerLineMode.Minimal;

    /// <summary>The colour from its settings text (hex or a name); the fallback when the text is blank or not a colour.</summary>
    internal static UiColor ParseOr(string? text, UiColor fallback) =>
        Color.TryParse(text, out var colour) ? new UiColor(colour.A, colour.R, colour.G, colour.B) : fallback;
}
