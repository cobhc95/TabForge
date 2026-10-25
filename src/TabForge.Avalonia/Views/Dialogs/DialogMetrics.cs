// Owns: the geometry and spacing every dialog window repeats: the text size, the content inset, the gap above a footer row, the margin of a
//   row, the footer button padding and minimum widths, and the width of a small dialog. Each named value is the number the sites used before
//   the table existed, so the dialogs keep their exact layout; two button widths stay separate because the dialog families genuinely differ.
// Does not own: colours (theme keys), the dialogs' content, or the layout rules of a particular window (each window still builds its own).
// Tests: TabForge.Avalonia.Tests.DialogMetricsTests.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace TabForge.Avalonia.Views.Dialogs;

/// <summary>The named geometry and spacing of the app's dialog windows.</summary>
internal static class DialogMetrics
{
    /// <summary>The default text size of a dialog window (the app's other windows take the settings font size).</summary>
    public const double FontSize = 13;

    /// <summary>The width of a small, fixed-width dialog that sizes its height to the content.</summary>
    public const double SmallDialogWidth = 420;

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
}
