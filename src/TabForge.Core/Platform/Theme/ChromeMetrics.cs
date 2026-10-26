namespace TabForge.Platform.Theme;

// Owns: the shared sizes and opacities of window chrome and small swatch controls, in device-independent pixels, so every shell and view names one value.
// Does not own: the colours (Palette and ThemeColours), the layout of the title bar (the shells), or sizes that belong to one view only.
// Tests: TabForge.Avalonia.Tests (TitleBarTests pins the caption sizes through CaptionButtons).

/// <summary>Chrome sizes and opacities shared by the shells and the views that draw swatches and preview boxes.</summary>
public static class ChromeMetrics
{
    /// <summary>The height of the title-bar row that holds the caption buttons.</summary>
    public const double CaptionRowHeight = 44;

    /// <summary>The width of each caption icon button (minimise, maximise, and the like).</summary>
    public const double CaptionIconButtonWidth = 40;
    /// <summary>The Monitor FX headphones glyph box and stroke.</summary>
    public const double MonitorFxGlyphWidth = 16, MonitorFxGlyphHeight = 14, MonitorFxGlyphStroke = 1.4;

    /// <summary>The width of the minimise, maximise and close caption buttons.</summary>
    public const double CaptionSystemButtonWidth = 46;

    /// <summary>The width of each scroll arrow at the ends of the tab strip.</summary>
    public const double TabScrollArrowWidth = 22;

    /// <summary>The edge length of a small colour or toggle swatch.</summary>
    public const double SwatchSize = 22;

    /// <summary>The opacity of secondary text (key hints, notes and offsets) that sits under the primary text.</summary>
    public const double DimTextOpacity = 0.75;

    /// <summary>The opacity of the Global Tuning description line.</summary>
    public const double GlobalTuningDescriptionOpacity = 0.8;

    /// <summary>The opacity of the Global Tuning string numbers and offsets.</summary>
    public const double GlobalTuningStringLabelOpacity = 0.7;

    /// <summary>The width of the Settings page rail: the first column of the Settings window (222 DIP).</summary>
    public const double SettingsRailWidth = 222;

    /// <summary>The height of one Settings rail row: the navigation button height (44 DIP).</summary>
    public const double SettingsRailRowHeight = 44;

    /// <summary>The edge length of the glyph in a Settings rail row (25 DIP).</summary>
    public const double SettingsRailIconSize = 25;
    /// <summary>The folder-list editors of the Settings rows (Plug-in folders): the list's least width and tallest height before it scrolls.</summary>
    public const double FolderListMinWidth = 320, FolderListMaxHeight = 120;
    /// <summary>A dropdown is at most this wide (longer labels are cut); the chrome is a row's padding plus the scroll bar.</summary>
    public const double ComboPopupMaxWidth = 480, ComboItemChrome = 40;
    /// <summary>The Settings window's top row (the title block over the rail and the search strip beside it).</summary>
    public const double SettingsTopRowHeight = 78;
    /// <summary>The plug-ins-switched-off window: its opening size and the least it can shrink to.</summary>
    public const double QuarantineWidth = 640, QuarantineHeight = 360, QuarantineMinWidth = 420, QuarantineMinHeight = 220;
    /// <summary>The gap between the rows of the plug-ins-switched-off window.</summary>
    public const double QuarantineRowGap = 4;
    /// <summary>The small muted hint line under a tool window's control (11 DIP).</summary>
    public const double ToolHintFontSize = 11;

    /// <summary>The font size of the page name in a Settings rail row (15 DIP).</summary>
    public const double SettingsRailLabelFontSize = 15;

    /// <summary>The gap between the glyph and the page name in a Settings rail row (9 DIP).</summary>
    public const double SettingsRailLabelGap = 9;

    /// <summary>The left and right inset of a Settings rail row around its glyph (16 DIP, the navigation button padding).</summary>
    public const double SettingsRailRowInset = 16;

    /// <summary>The width of the accent bar on the selected Settings rail row (3 DIP, the selected navigation button left border).</summary>
    public const double SettingsRailAccentBarWidth = 3;

    /// <summary>The default and minimum size of the Settings window: compact enough for a 1920 x 1080 screen (1040 x 690, minimum 834 x 597 DIP).</summary>
    public const double SettingsWindowWidth = 1040, SettingsWindowHeight = 690, SettingsWindowMinWidth = 834, SettingsWindowMinHeight = 597;

    /// <summary>The band caption above each rail group (BASICS, MUSIC, ...): 11 DIP, indented 18 DIP, 12 DIP above and 4 DIP below.</summary>
    public const double SettingsRailCaptionFontSize = 11, SettingsRailCaptionIndent = 18, SettingsRailCaptionTop = 12, SettingsRailCaptionBottom = 4;

    /// <summary>The Song info (Project settings) window: 660 x 600 DIP, minimum 540 x 460.</summary>
    public const double SongInfoWindowWidth = 660, SongInfoWindowHeight = 600, SongInfoWindowMinWidth = 540, SongInfoWindowMinHeight = 460;
    /// <summary>The Song info window's label column: the field labels on the left, the editors in the column to their right.</summary>
    public const double SongInfoLabelColumn = 120;
    /// <summary>The Settings "Reset page…" link: 12 DIP left and 6 DIP above, so it sits on the title row, at 12 DIP text.</summary>
    public const double SettingsResetPageMarginLeft = 12, SettingsResetPageMarginTop = 6, SettingsResetPageFontSize = 12;

    /// <summary>The family of the drawn text of custom-painted controls (track rows, mixer buttons).</summary>
    /// <summary>The M and S boxes of a track header row: the letter size and weight, and the box corner.</summary>
    public const double TrackToggleFontSize = 11, TrackToggleCorner = 4;
    public const int TrackToggleWeight = 700;

    public const string UiFontFamily = "Segoe UI";

    /// <summary>The Mixer's drawn FX buttons: the split FX button's width, the Monitor FX button's width and their shared height.</summary>
    public const double FxSplitButtonWidth = 54, MonitorFxButtonWidth = 56, MixerFxButtonHeight = 22;

    /// <summary>The Tuner window: default size and minimum size.</summary>
    public const double TunerWindowWidth = 420, TunerWindowHeight = 330, TunerWindowMinWidth = 340, TunerWindowMinHeight = 280;

    /// <summary>The slim live input line under the FX window's menu row: its minimum height (one control row), corner, gap between parts, gap between groups, vertical inset of a row, edge inset and the font size of its small labels.</summary>
    public const double LiveStripHeight = 28, LiveStripCorner = 6, LiveStripGap = 4, LiveStripGroupGap = 10, LiveStripRowInset = 1, LiveStripInset = 6, LiveStripFontSize = 11;

    /// <summary>The live strip's parts: the caption, the two-bar meter, the note name, the cents scale (fixed width), the cents text, the Tuner toggle, the window dropdown (its width is its longest item plus the chrome), a slider label and a mini slider.</summary>
    public const double LiveStripMeterWidth = 40, LiveStripMeterBar = 4, LiveStripNoteWidth = 36, LiveStripNoteFontSize = 15, LiveStripCentsScaleWidth = 200, LiveStripCentsScaleMinWidth = 120, LiveStripTipMaxWidth = 240,
        LiveStripCentsTextWidth = 44, LiveStripTunerToggleWidth = 40, LiveStripWindowPad = 4, LiveStripWindowChrome = 44, LiveStripSliderLabelWidth = 26, LiveStripSliderWidth = 54, LiveStripToggleWidth = 23, LiveStripControlHeight = 22;

    /// <summary>How often (ms) the strip's cents text may change.</summary>
    public const double LiveStripTextIntervalMs = 250, LiveStripNameMaxWidth = 160;
}
