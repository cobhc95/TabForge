namespace TabForge.Platform.Theme;

// Owns: the shared sizes and opacities of window chrome and small swatch controls, in device-independent pixels, so every shell and view names one value.
// Does not own: the colours (Palette and ThemeColours), the layout of the title bar (the shells), or sizes that belong to one view only.
// Tests: TabForge.Avalonia.Tests (TitleBarTests pins the caption sizes through CaptionButtons).

/// <summary>Chrome sizes and opacities shared by the shells and the views that draw swatches and preview boxes.</summary>
public static class ChromeMetrics
{
    /// <summary>The app's type scale in DIP: use these instead of a literal size.</summary>
    public const double TextMicro = 9, TextCaption = 10, TextSmall = 11, TextBody = 12, TextDialog = 13, TextEmphasis = 14, TextHeading = 15, TextTitle = 16;

    /// <summary>The app's corner radii in DIP: use these instead of a literal radius.</summary>
    public const double RadiusTiny = 2, RadiusSmall = 3, RadiusMedium = 4, RadiusLarge = 6, RadiusXLarge = 8;

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
    /// <summary>The on/off switch (LargeSwitch): track width and height, and the round thumb inside it, sized like the popup close button's height.</summary>
    public const double LargeSwitchTrackWidth = 48, LargeSwitchTrackHeight = 26, LargeSwitchThumbSize = 20;
    /// <summary>Scale highlight labels (note names, degrees): the font as a share of the mark radius, its most (a share of the mark diameter, so it stays inside the cell), the halo width and the two weights.</summary>
    public const double ScaleLabelFontShare = 1.5, ScaleLabelMaxShareOfDiameter = 0.95, ScaleLabelHaloWidth = 2.2;
    public const int ScaleLabelWeight = 600, ScaleLabelRootWeight = 800;
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

    /// <summary>The M and S boxes of a track header row: the letter size and weight, and the box corner.</summary>
    public const double TrackToggleFontSize = 11, TrackToggleCorner = 4;
    public const int TrackToggleWeight = 700;

    /// <summary>The selected track row's left-edge strip: its width and corner in DIPs.</summary>
    public const double SelectedStripWidth = 4, SelectedStripCorner = 2;

    /// <summary>The experimental left strip: its width, the Composer panel's width, and the gap between the panel's rows, in DIPs.</summary>
    public const double LeftStripWidth = 36, ComposerPanelWidth = 220, ComposerRowGap = 6;

    /// <summary>The Composer panel's category cards: the instrument badge edge, the accent dot edge, and the least card height, in DIPs.</summary>
    public const double ComposerBadgeSize = 40, ComposerAccentSize = 10, ComposerCardMinHeight = 96;

    /// <summary>The Composer drum-sample rows: a step cell's height, the gap between steps and the wider gap between beats, the voice-label column,
    /// the audition button's edge and its icon size, in DIPs.</summary>
    public const double ComposerStepHeight = 6, ComposerStepGap = 1, ComposerBeatGap = 3, ComposerVoiceLabelWidth = 18, ComposerPlaySize = 26, ComposerPlayIcon = 12;

    /// <summary>The Customize strip window: its size, the width of each list, the gap between rows and the gap between columns, in DIPs.</summary>
    public const double LeftStripWindowWidth = 720, LeftStripWindowHeight = 480, LeftStripListWidth = 220, LeftStripGap = 6, LeftStripColumnGap = 12, LeftStripGlyphSize = 16;
    /// <summary>The Focus track settings window: its width, the gap between its sections and inside a row, and the roundness of its chips and bars.</summary>
    public const double FocusWindowWidth = 460, FocusGap = 12, FocusTightGap = 6, FocusCorner = 6;

    /// <summary>The Help window: default and minimum size, the chapter list's width, the reading column's widest line, and the search results' most rows.</summary>
    public const double HelpWindowWidth = 960, HelpWindowHeight = 680, HelpWindowMinWidth = 600, HelpWindowMinHeight = 420, HelpNavWidth = 248, HelpReadingWidth = 760;

    /// <summary>The Help window's text: the chapter title, a section heading and the reading text (DIP), and the key-cap chips' text.</summary>
    public const double HelpTitleSize = 28, HelpSectionSize = 18, HelpBodySize = 14, HelpKeyCapSize = 12;

    /// <summary>The Help window's spacing (DIP): between blocks, between sections, round the reading column, a card's inner padding, and between a list row's lines.</summary>
    public const double HelpBlockGap = 8, HelpSectionGap = 22, HelpPagePadding = 24, HelpCardPadding = 12, HelpRowGap = 2;

    /// <summary>The Help window's accent marks: a callout's left bar, a step badge and a bullet dot (DIP), and the opacity of a callout's tint (the colour behind the text at this strength).</summary>
    public const double HelpCalloutBar = 4, HelpBadgeSize = 20, HelpBulletDot = 6, HelpCalloutTint = 0.16, HelpNavRowHeight = 30, HelpNavBar = 3, HelpKeyCapPad = 6, HelpBulletTop = 8, HelpNavSize = 13, HelpIndent = 16, HelpNavIndent = 14, HelpPairGap = 4, HelpCalloutPadV = 6, HelpDivider = 1, HelpNavRowInset = 4, HelpBadgeCorner = 10;

    /// <summary>The strip's Separator line in the vertical strip: its width and height in DIPs, and its opacity.</summary>
    public const double LeftStripSeparatorWidth = 18, LeftStripSeparatorHeight = 1, LeftStripSeparatorOpacity = 0.4;

    /// <summary>The family of the drawn text of custom-painted controls (track rows, mixer buttons, the timeline and the fretboard).</summary>
    public const string UiFontFamily = FontProvider.WindowsSans;

    /// <summary>The Mixer's drawn FX buttons: the split FX button's width, the Monitor FX button's width and their shared height.</summary>
    public const double FxSplitButtonWidth = 54, MonitorFxButtonWidth = 56, MixerFxButtonHeight = 22;

    /// <summary>The Tuner window: default size and minimum size.</summary>
    public const double TunerWindowWidth = 420, TunerWindowHeight = 330, TunerWindowMinWidth = 340, TunerWindowMinHeight = 280;

    /// <summary>The slim live input line under the FX window's menu row: its minimum height (one control row), corner, gap between parts, gap between groups, vertical inset of a row, edge inset and the font size of its small labels.</summary>
    public const double LiveStripHeight = 28, LiveStripCorner = 6, LiveStripGap = 4, LiveStripGroupGap = 10, LiveStripRowInset = 1, LiveStripInset = 6, LiveStripFontSize = 11;

    /// <summary>The live strip's parts: the caption, the two-bar meter, the note name, the cents scale (fixed width), the cents text, the Tuner toggle, the window dropdown (its width is its longest item plus the chrome), a slider label and a mini slider.</summary>
    public const double LiveStripMeterWidth = 40, LiveStripMeterBar = 4, LiveStripCellPad = 4, LiveStripNoteFontSize = 15, LiveStripCentsScaleWidth = 160, LiveStripCentsScaleMinWidth = 100, LiveStripTipMaxWidth = 240,
        LiveStripTunerToggleWidth = 46, LiveStripWindowPad = 4, LiveStripWindowChrome = 44, LiveStripSliderWidth = 54, LiveStripToggleWidth = 23, LiveStripControlHeight = 22;

    /// <summary>How often (ms) the strip's cents text may change.</summary>
    public const double LiveStripTextIntervalMs = 250, LiveStripNameMaxWidth = 160;

    /// <summary>The track list's default width (every transport item on one row at normal button size) and the earlier default saved layouts may still hold.</summary>
    public const double TrackListDefaultWidth = 600, TrackListOldDefaultWidth = 532;

    /// <summary>The per-track meter in a track row: the column span, the cell's width and height, the extra width the volume and pan columns took back,
    /// the two stacked horizontal bars (height, gap, width with the number), the peak-hold tick, the clip end mark and the small number.</summary>
    public const double TrackMeterColumnWidth = 46, TrackMeterWidth = 46, TrackMeterHeight = 18, TrackVolumeColumnWidth = 96, TrackPanColumnWidth = 96,
        TrackMeterBarHeight = 7, TrackMeterGap = 3, TrackMeterInset = 2, TrackMeterBarsWidth = 28, TrackMeterClipMark = 2, TrackMeterHoldHeight = 1.5,
        TrackMeterCorner = 1.5, TrackMeterNumberSize = 8.5;

    /// <summary>The transport bar's snug width parts (DIPs): the side margin of a full-size button and of the master meter (scaled by density),
    /// the divider's own span and side margin (scaled), the master knob's left and right margins (scaled), the zoom column's own span and side
    /// margin (unscaled, stacked flush), the bar's own side padding (scaled) and border, and the bar's outer left and right margins.
    /// The values mirror TransportBarView's layout, so TransportWrapRules.SnugListWidth meets the master meter at the bar's right padding.</summary>
    public const double TransportSideMargin = 1, TransportDividerSpan = 1, TransportDividerSideMargin = 3,
        TransportKnobLeftMargin = 4, TransportKnobRightMargin = 6,
        TransportZoomSpan = 26, TransportZoomSideMargin = 1,
        TransportBarPadding = 1, TransportBarBorder = 1, TransportBarOuterLeft = 1, TransportBarOuterRight = 5;

    /// <summary>The generic plug-in parameter panel: the name and value columns of a row, the gap beside parts, the search box width, the slider's least width and how often (ms) the values are read while the panel is open.</summary>
    public const double ParamNameWidth = 190, ParamValueWidth = 110, ParamGap = 6, ParamSearchWidth = 240, ParamSliderMinWidth = 120, ParamPollMs = 500, BuiltinPollMs = 33;
}
