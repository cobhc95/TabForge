// Owns: the geometry and spacing every dialog window repeats: the text size, the content inset, the gap above a footer row, the margin of a
//   row, the footer button padding and minimum widths, and the width of a small dialog. Each named value is the number the sites used before
//   the table existed, so the dialogs keep their exact layout; two button widths stay separate because the dialog families genuinely differ.
// Does not own: colours (theme keys), the dialogs' content, or the layout rules of a particular window (each window still builds its own).
// Tests: TabForge.Avalonia.Tests.DialogMetricsTests.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace TabForge.Avalonia.Views.Dialogs;

/// <summary>The named geometry and spacing of the app's dialog windows.</summary>
internal static class DialogMetrics
{
    /// <summary>The default text size of a dialog window (the app's other windows take the settings font size).</summary>
    public const double FontSize = 13;

    /// <summary>The vibrato settings window: field column width, slider, value label and name widths, and the row and hint gaps.</summary>
    public const double VibratoFieldsWidth = 330, VibratoSliderWidth = 170, VibratoValueWidth = 64, VibratoNameWidth = 56, VibratoRowGap = 4, VibratoHintGap = 8;
    public static void SpaceVibratoRow(global::Avalonia.Controls.Control control) => control.Margin = new(0, VibratoRowGap);
    public static void SpaceVibratoHint(global::Avalonia.Controls.Control control) => control.Margin = new(0, VibratoHintGap, 0, 0);

    /// <summary>The width of a small, fixed-width dialog that sizes its height to the content.</summary>
    public const double SmallDialogWidth = 420;

    /// <summary>The size of a window that shows a long read-only text (the third-party licences).</summary>
    public const double TextWindowWidth = 760;
    public const double TextWindowHeight = 560;

    /// <summary>Gives <paramref name="control"/> the uniform dialog inset as its margin.</summary>
    public static T Inset<T>(T control) where T : Layoutable { control.Margin = ContentInset; return control; }

    /// <summary>Opens <paramref name="window"/> centred on its owner.</summary>
    public static void CentreOnOwner(Window window) => window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

    /// <summary>The minimum width of a dialog's footer action button (OK, Cancel and the like).</summary>
    public const double DialogButtonMinWidth = 80;

    /// <summary>The minimum width of a prompt dialog's OK/Cancel buttons, the tighter of the two dialog button widths.</summary>
    public const double PromptButtonMinWidth = 76;

    /// <summary>The uniform inset of a dialog's content (and of a docked footer beside it).</summary>
    public static readonly Thickness ContentInset = new(12);

    /// <summary>The gap between a dialog's content and its footer row of buttons.</summary>
    public static readonly Thickness FooterGap = new(0, 10, 0, 0);

    /// <summary>The top and bottom margin of a dialog row, so rows in a column keep their two-pixel rhythm.</summary>
    public static readonly Thickness RowMargin = new(0, 2, 0, 2);

    /// <summary>The padding inside a dialog's footer action button.</summary>
    public static readonly Thickness ActionButtonPadding = new(12, 3, 12, 3);

    /// <summary>The padding inside the status notice action button (10, 2), keeping its exact size.</summary>
    public static readonly Thickness NoticeActionPadding = new(10, 2);

    /// <summary>The padding inside the status notice close button (8, 2), keeping its exact size.</summary>
    public static readonly Thickness NoticeClosePadding = new(8, 2);

    /// <summary>The left gap before each status notice button (10, 0, 0, 0), keeping its exact spacing.</summary>
    public static readonly Thickness NoticeButtonGap = new(10, 0, 0, 0);

    /// <summary>The Sections panel action button: padding and the gap below and right of it.</summary>
    public static readonly Thickness SectionActionPadding = new(10, 3), SectionActionGap = new(0, 0, 4, 4);

    /// <summary>The Mix Table row: toggle column width, value readout width, accent bar width and minimum row height.</summary>
    public const double MixToggleWidth = 120, MixReadoutWidth = 64, MixBarWidth = 3, MixRowMinHeight = 32;

    /// <summary>The Mix Table row gaps: around the readout, around the control, around the accent bar and between rows.</summary>
    public static readonly Thickness MixReadoutMargin = new(8, 0, 6, 0), MixEditorMargin = new(0, 4), MixBarMargin = new(0, 3), MixRowGap = new(0, 1);

    /// <summary>The "Unsaved changes" question: its width range, content inset, the gaps between its parts and lines, and the colour swatch (size and corner).</summary>
    public const double DiscardMinWidth = 380, DiscardMaxWidth = 560, DiscardLineSpacing = 4, DiscardLineGap = 6, DiscardButtonSpacing = 8, DiscardButtonHeight = 28, SwatchSize = 18, SwatchCorner = 4;

    /// <summary>The "Unsaved changes" question: the content inset, the gap above the list and above the buttons, and the swatch outline.</summary>
    public static readonly Thickness DiscardInset = new(18), DiscardListGap = new(0, 12, 0, 0), DiscardActionsGap = new(0, 16, 0, 0), SwatchOutline = new(1);

    /// <summary>The Choose instrument family header row: its padding, the gap above it, its corner radius, the chevron gap, the closed and open chevron turn in degrees.</summary>
    public static readonly Thickness PickerHeaderPadding = new(8, 6), PickerHeaderGap = new(0, 6, 0, 2), PickerChevronGap = new(0, 0, 8, 0);
    public const double PickerHeaderCorner = 6, PickerChevronClosed = 0, PickerChevronOpen = 90;

    /// <summary>The colour picker popup: its width, the square and hue strip sizes, a colour cell, the corner, the position marker (radius and line) and the code box width.</summary>
    public const double ColourPopupWidth = 272, ColourSquareWidth = 200, ColourSquareHeight = 140, HueStripWidth = 22, ColourCellSize = 22, ColourPopupCorner = 6, MarkerRadius = 6, MarkerLine = 2, CodeBoxWidth = 120;

    /// <summary>The colour picker popup: colours per row of the ready-made grid.</summary>
    public const int ColourPerRow = 8;

    /// <summary>The colour picker popup: the inset, the gap around a colour cell, the gap above each part, between buttons and beside the square.</summary>
    public static readonly Thickness ColourPopupInset = new(10);
    public static readonly Thickness ColourCellGap = new(2);
    public static readonly Thickness ColourPartGap = new(0, 8, 0, 0);
    public static readonly Thickness ColourButtonGap = new(0, 0, 6, 0);

    /// <summary>The gaps beside the swatch and before the arrow inside a colour button.</summary>
    public static readonly Thickness ColourButtonSwatchGap = new(0, 0, 8, 0), ColourButtonArrowGap = new(8, 0, 0, 0);
    public static readonly Thickness ColourSquareGap = new(0, 0, 8, 0);
    public static readonly Thickness SelectedOutline = new(2);

    /// <summary>Score text &amp; fonts window: the area list, form rows, read-outs and button row.</summary>
    public const double TextStyleAreasWidth = 190, TextStyleSizeWidth = 140, TextStyleOutlineWidth = 100, TextStyleReadOutWidth = 44, TextStyleLabelWidth = 90, TextStyleButtonWidth = 80, TextStyleWindowWidth = 520;
    public static readonly Thickness TextStyleAreasGap = new(0, 0, 12, 0), TextStyleItalicGap = new(12, 0, 0, 0), TextStyleOutlineGap = new(8, 0, 0, 0), TextStyleInset = new(14),
        TextStyleInheritGap = new(6, 0, 0, 0), TextStyleResetGap = new(0, 8, 0, 0), TextStyleRowGap = new(0, 3, 0, 3), TextStyleOkGap = new(0, 0, 6, 0), TextStyleButtonsGap = new(0, 12, 0, 0);

    /// <summary>Preferences window: the rail's right border, the page cards' margin, a row group's gap above and the "More options" toggle's pull to the left.</summary>
    public static readonly Thickness PrefsRailBorder = new(0, 0, 1, 0), PrefsCardsMargin = new(16, 8, 16, 16), PrefsRowsGap = new(0, 4, 0, 0), PrefsMoreToggleMargin = new(-6, 0, 0, 0);

    /// <summary>The media windows (copy question, copy progress, find missing media): widths, list heights, bar and button height, small text, corner.</summary>
    public const double MediaPromptWidth = 580, MediaTreeIndent = 12, MediaProgressWidth = 420, MediaRelinkWidth = 600, MediaRelinkMinWidth = 420,
        MediaListMaxHeight = 150, MediaRelinkListMaxHeight = 220, MediaBarHeight = 8, MediaButtonHeight = 28, MediaSmallText = 12, MediaCorner = 4;

    /// <summary>The media windows: the gaps above the Remember box, around the file list, above the bar, a list row's inset, the gap before a detail and a status, the button padding, the outline.</summary>
    public static readonly Thickness MediaRememberGap = new(0, 8, 0, 0), MediaListGap = new(0, 8, 0, 4), MediaBarGap = new(0, 10, 0, 4), MediaRowInset = new(6, 3),
        MediaDetailGap = new(10, 0, 0, 0), MediaStatusGap = new(8, 0), MediaButtonPadding = new(14, 0), MediaFrameGap = new(0, 0, 0, 6), MediaOutline = new(1),
        MediaLayoutHeadingGap = new(0, 10, 0, 4), MediaCardLeftGap = new(0, 0, 4, 8), MediaCardRightGap = new(4, 0, 0, 8);

    /// <summary>The left indent of a line of a folder-tree sketch, <paramref name="depth"/> levels in.</summary>
    public static Thickness MediaTreeIndentOf(int depth) => new(depth * MediaTreeIndent, 0, 0, 0);

    /// <summary>Centres a dialog over its owner window.</summary>
    public static void CenterOnOwner(global::Avalonia.Controls.Window window) => window.WindowStartupLocation = global::Avalonia.Controls.WindowStartupLocation.CenterOwner;

    /// <summary>Sets the button's padding and returns it (builder code outside the metrics owner does not assign layout).</summary>
    public static global::Avalonia.Controls.Button WithPadding(this global::Avalonia.Controls.Button button, Thickness padding)
    {
        button.Padding = padding;
        return button;
    }

    /// <summary>A light-dismiss popup shown at <paramref name="placement"/> (under by default) the anchor; the one place anchored pickers are built.</summary>
    public static Popup AnchoredPopup(Control anchor, Control child, PlacementMode placement = PlacementMode.Bottom) =>
        new() { PlacementTarget = anchor, Placement = placement, IsLightDismissEnabled = true, Child = child };

    /// <summary>Gives a Choose instrument family header its padding, gap above and corner radius.</summary>
    public static void StylePickerHeader(global::Avalonia.Controls.Border header)
    {
        header.Padding = PickerHeaderPadding;
        header.Margin = PickerHeaderGap;
        header.CornerRadius = new CornerRadius(PickerHeaderCorner);
    }

    /// <summary>Sets the control's outer margin and returns it, so builder code outside the metrics owner does not spell layout numbers.</summary>
    public static Thickness WithTop(this Thickness t, double top) => new(t.Left, top, t.Right, t.Bottom);

    /// <summary>The margin with its left side set (the other sides as given).</summary>
    public static Thickness WithLeft(this Thickness t, double left) => new(left, t.Top, t.Right, t.Bottom);

    public static T WithMargin<T>(this T control, Thickness margin) where T : global::Avalonia.Controls.Control
    {
        control.Margin = margin;
        return control;
    }

    /// <summary>The bend and tremolo-bar editor's side panel: its width, the height range of the list of the person's presets and the space between its buttons.</summary>
    public const double CurveSideWidth = 240, CurvePresetListMinHeight = 90, CurvePresetListMaxHeight = 150, CurveSideSpacing = 6;

    /// <summary>The shape icon before a curve type in the bend and tremolo-bar editors' type list: its side and the gap to the name.</summary>
    public const double CurveIconSize = 16, CurveIconSpacing = 8;

    /// <summary>The bend and tremolo-bar editor's side panel: the gap left of it, under the first heading, above and under the second heading, and above the buttons.</summary>
    public static readonly Thickness CurveSideGap = new(16, 0, 0, 0), CurveHeadingGap = new(0, 0, 0, 4), CurveSecondHeadingGap = new(0, 12, 0, 4), CurveButtonsGap = new(0, 8, 0, 0);

    /// <summary>The "Insert several bars" dialog: its width and the bar-count box width.</summary>
    public const double InsertBarsWidth = 340, InsertBarsCountWidth = 72;

    /// <summary>The "Audio crosses this section" prompt: its width, the card corner, the gaps below the question and between cards, the card inset and outline, the hint indent, the gap above the Remember row.</summary>
    public const double SectionAudioWidth = 480, SectionAudioCardCorner = 6;
    public static readonly Thickness SectionAudioQuestionGap = new(0, 0, 0, 10), SectionAudioCardGap = new(0, 0, 0, 6), SectionAudioCardInset = new(10, 6),
        SectionAudioCardOutline = new(1), SectionAudioHintIndent = new(22, 1, 0, 0), SectionAudioRememberGap = new(0, 6, 0, 0);

    /// <summary>The shared prompt chrome (PromptChrome): heading size, muted line size, action button height and minimum width, the gap between buttons.</summary>
    public const double PromptHeadingSize = 16, PromptMutedSize = 12, PromptButtonHeight = 26, PromptActionMinWidth = 68, PromptButtonSpacing = 8;

    /// <summary>The shared prompt chrome: the content inset, the gaps under the heading and the muted line, above the footer, before the Remember hint, and the action button padding.</summary>
    public static readonly Thickness PromptInset = new(16, 14), PromptHeadingGap = new(0, 0, 0, 4), PromptMutedGap = new(0, 0, 0, 10), PromptFooterGap = new(0, 8, 0, 0),
        PromptHintGap = new(6, 0, 0, 0), PromptActionPadding = new(12, 0);

    /// <summary>The Articulations tab of the MIDI processing window: column widths, the row height and the gaps between its parts.</summary>
    public const double ArticulationActionWidth = 140, ArticulationValueWidth = 90, ArticulationValue2Width = 64, ArticulationModeWidth = 100,
        ArticulationPresetWidth = 240, ArticulationLeadWidth = 56, ArticulationRowMinHeight = 34;
    public static readonly Thickness ArticulationCellGap = new(4, 0), ArticulationLabelGap = new(0, 0, 8, 0), ArticulationButtonPadding = new(10, 3),
        ArticulationButtonGap = new(6, 0, 0, 0), ArticulationPaneGap = new(0, 6, 0, 0), ArticulationTextGap = new(0, 6, 0, 6), ArticulationTableGap = new(0, 0, 8, 0),
        ArticulationLabelEnd = new(0, 0, 6, 0), ArticulationHeadFirst = new(0, 0, 0, 4), ArticulationHead = new(4, 0, 0, 4);

    /// <summary>Preset rows (the drop-downs and the preset lists): the icon and grip sizes, the stroke of a drawn icon, the least row height, the header text size,
    /// the share of the strongest ink a pressed icon keeps, and the corner and width of the Undo message.</summary>
    public const double PresetIconSize = 14, PresetIconStroke = 1.4, PresetGripWidth = 14, PresetGripDot = 1.3, PresetRowMinHeight = 22, PresetHeaderSize = 11,
        PresetPressedInk = 0.7, PresetEditorHeight = 22, UndoToastCorner = 6, UndoToastMaxWidth = 460, UndoToastBottomGap = 24;

    /// <summary>Preset rows: the row's inset, the gap before the icons, the room above a group header, the text box padding, the gap above an error line,
    /// the gap before a row's label, and the Undo message's inset and the gap before its button.</summary>
    public static readonly Thickness PresetRowInset = new(2, 1), PresetToolsGap = new(6, 0, 0, 0), PresetHeaderGap = new(0, 4, 0, 1), PresetEditorPadding = new(2, 0),
        PresetErrorGap = new(0, 2), PresetNoOutline = new(0),
        UndoToastOutline = new(1), UndoToastInset = new(14, 8), UndoToastButtonGap = new(14, 0, 0, 0), PresetLabelGap = new(4, 0, 0, 0);

    /// <summary>Gives a preset row its inset and returns it.</summary>
    public static Border WithPresetInset(this Border row) { row.Padding = PresetRowInset; return row; }

    /// <summary>Gives an in-place rename box its height and padding and returns it.</summary>
    public static TextBox AsPresetEditor(this TextBox box) { box.Height = PresetEditorHeight; box.Padding = PresetEditorPadding; return box; }

    /// <summary>Gives the "Deleted ... Undo" message its inset, outline, corner, width limit and gap above the window edge.</summary>
    public static Border AsUndoToast(this Border toast)
    {
        toast.Padding = UndoToastInset;
        toast.MaxWidth = UndoToastMaxWidth;
        toast.BorderThickness = UndoToastOutline;
        toast.CornerRadius = new CornerRadius(UndoToastCorner);
        return toast;
    }

    /// <summary>Sets the outer margin of the control and returns it (builder code outside the metrics owner does not assign layout).</summary>
    public static T Spaced<T>(this T control, Thickness margin) where T : Layoutable
    {
        control.Margin = margin;
        return control;
    }

    /// <summary>Gives a radio card its inset, outline and corner radius.</summary>
    public static void StyleChoiceCard(global::Avalonia.Controls.Border card)
    {
        card.Padding = SectionAudioCardInset;
        card.BorderThickness = SectionAudioCardOutline;
        card.CornerRadius = new CornerRadius(SectionAudioCardCorner);
        card.Margin = SectionAudioCardGap;
    }

    /// <summary>The IR Loader window (a built-in effect's editor): its size, the file label column, the slider and value widths, the picture height and its strokes.</summary>
    public const double IrLoaderWidth = 660, IrLoaderHeight = 600, IrLoaderMinWidth = 520, IrLoaderMinHeight = 460, IrLabelWidth = 120, IrSliderWidth = 320,
        IrValueWidth = 70, IrGraphMinHeight = 120, IrCurveStroke = 1.5, IrGridStroke = 1;

    /// <summary>The IR Loader window: the gap under a row, left of a button, around the picture, and a button's padding.</summary>
    public static readonly Thickness IrRowGap = new(0, 0, 0, 6), IrButtonGap = new(8, 0, 0, 0), IrSectionGap = new(0, 8, 0, 8), IrButtonPadding = new(10, 2);

    /// <summary>The built-in effect windows (EQ, Compressor, Limiter, Delay, Noise Gate): window sizes, the label and value columns of a slider row, the graph and meter sizes and strokes.</summary>
    public const double EqWidth = 960, EqHeight = 700, EqMinWidth = 780, EqMinHeight = 600, CompressorWidth = 780, CompressorHeight = 440, CompressorMinWidth = 660, CompressorMinHeight = 400,
        LimiterWidth = 560, LimiterHeight = 340, LimiterMinWidth = 460, LimiterMinHeight = 300, DelayWidth = 580, DelayHeight = 330, DelayMinWidth = 480, DelayMinHeight = 290,
        GateWidth = 560, GateHeight = 340, GateMinWidth = 460, GateMinHeight = 300,
        FxLabelWidth = 112, FxValueWidth = 90, FxBandLabelWidth = 40, FxBandValueWidth = 66, FxGraphMinHeight = 120, FxTransferMinSize = 200, FxBandColumnMinWidth = 220, FxCutColumnMinWidth = 300, FxCompressorColumnMinWidth = 380, FxMeterHeight = 14, FxMeterLabelWidth = 128,
        FxLightSize = 16, FxDotRadius = 6, FxDotSelectedRadius = 8.5, FxDotHit = 12, FxCurveStroke = 2, FxGridStroke = 1, FxMarkerStroke = 1.5, FxMeterRadius = 3,
        FxPlotLeft = 38, FxPlotTop = 10, FxPlotRight = 12, FxPlotBottom = 22, FxLabelInset = 4, FxQStep = 1.12;

    /// <summary>The built-in effect windows: the gap under a row, between columns and sections, and the padding of a small button or box.</summary>
    /// <summary>The built-in editors' on/off button: its padding, outline thickness and smallest height.</summary>
    public static readonly Thickness FxTogglePadding = new(10, 2, 10, 2), FxToggleOutline = new(1);
    public const double FxToggleMinHeight = 22;

    public static readonly Thickness FxRowGap = new(0, 0, 0, 6), FxColumnGap = new(0, 0, 14, 0), FxSectionGap = new(0, 10, 0, 6), FxBandHeadGap = new(0, 0, 0, 4), FxButtonGap = new(10, 0, 0, 0);

    /// <summary>The small action button beside a status line (Add plug-in: Pause, Resume, Stop): the notice button gap and padding.</summary>
    public static Button StatusAction(Button button)
    {
        button.Margin = NoticeButtonGap;
        button.Padding = NoticeActionPadding;
        return button;
    }
}
