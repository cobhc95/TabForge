using System;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The master base look of the form controls, from the WPF build's default templates (App.xaml): the sizes and brush names of buttons, the default (accent) button,
/// text boxes, combo boxes, list boxes and separators. Every window inherits these; Core looks only add the hover, pressed, selected and disabled states on top
/// (AvaloniaDefaultLooks gives plain controls their look class), and the field colours of Fluent's own states are replaced through AvaloniaPalette.FluentFieldBrushNames.
/// Owns: those base setters for the Avalonia app. Does not own: the state looks (AvaloniaLooks), the brushes (AvaloniaPalette).
/// Tests: TabForge.Avalonia.Tests.ParityShotsTests (A/B renders of the dialogs that use these controls), PreferencesControlHoverTests.
/// Size: over 300 lines because every shared form control's base look is one ordered style list; the Settings field rules sit with the others.
/// </summary>
public static partial class AvaloniaFormControlStyles
{
    /// <summary>The corner radius of every control surface (App.xaml: 3 DIP); Fluent's own is 4.</summary>
    public static readonly CornerRadius ControlCornerRadius = new(3);

    /// <summary>The class of the Preferences page rows in the settings rail (the nav rows of the WPF Preferences window).</summary>
    public const string PrefsNavRowClass = "PrefsNavRow";

    /// <summary>The class of the Preferences "More options" expanders: plain text with no fill or edge, as the WPF expander.</summary>
    public const string PrefsMoreOptionsClass = "PrefsMoreOptions";

    /// <summary>The class of the Mixer mute and solo toggles: the Core look "MixerToggle" plus the dark base style below.</summary>
    public const string MixerToggleClass = "MixerToggle";

    /// <summary>The class of the Global tuning step buttons (the minus and plus of each string and of all strings).</summary>
    public const string StepButtonClass = "StepButton";

    /// <summary>The class of every Settings input: text, combo and number boxes, the search box and the card buttons. They share one height and one edge rule.</summary>
    public const string PrefsFieldClass = "PrefsField";

    /// <summary>The height of every Settings input and button (the footer buttons, the shortcut key boxes and the fields alike).</summary>
    public const double PrefsFieldHeight = 32;

    private const double BorderThickness = 1;
    // Every field and button is 24 DIP high with a 1 DIP margin; buttons pad 8,3, text boxes 5,3, combo boxes 6,2, list rows 5,3.
    private const double ControlMinHeight = 24;
    private static readonly Thickness ControlMargin = new(1);
    private static readonly Thickness ButtonPadding = new(8, 3, 8, 3);
    private static readonly Thickness TextBoxPadding = new(5, 3, 5, 3);
    private static readonly Thickness ComboBoxPadding = new(6, 2, 6, 2);
    private static readonly Thickness ListBoxItemPadding = new(5, 3, 5, 3);
    private static readonly Thickness TabHeaderPadding = new(12, 0, 12, 0);
    private const double TabHeaderFontSize = 13, TabHeaderHeight = 34, TabHeaderAccentWidth = 2;
    // A list row sits 4 DIP inside the pane edge, so its selected tile is inset and rounded as the WPF row.
    private static readonly Thickness ListBoxItemMargin = new(4, 0);
    // The separator is a 1 DIP rule in the soft border colour, inset 8 DIP at the sides, as the WPF menu separator.
    private const double MenuSeparatorHeight = 1;
    private static readonly Thickness MenuSeparatorMargin = new(8, 4);

    // PreferencesWindow.xaml BaseActionButton, SecondaryActionButton and ResetActionButton: Panel3 fill, soft edge, the control corner.
    // The Preferences rail row radius: the WPF navigation tile's 4 DIP.
    private static readonly CornerRadius PrefsNavCornerRadius = new(4);
    private static readonly Thickness PrefsActionPadding = new(14, 7, 14, 7);
    private const double PrefsActionMinWidth = 90, PrefsActionFontSize = 13;
    private const double PrefsResetMinWidth = 52, PrefsResetHeight = 26, PrefsResetFontSize = 11;

    // App.xaml ComboToggle: a filled 8x4 triangle, 7 DIP from the right edge (the WPF picture shows the filled triangle).
    private const string ComboArrowPath = "M 0 0 L 4 4 L 8 0 Z";
    private const double ComboArrowWidth = 8, ComboArrowHeight = 4, ComboArrowRightGap = 7;

    private static Style DisabledPresenter(string buttonClass, string ink) => StatePresenter(buttonClass, ":disabled", ink);

    // Fluent's hover and pressed ink on the label presenter (a state style outranks the button's own foreground) is put back to the look's ink, so an accent fill keeps its readable label.
    private static Style StatePresenter(string buttonClasses, string state, string ink) =>
        new(x => buttonClasses.Split('.').Aggregate(x.OfType<Button>(), (selector, cls) => selector.Class(cls)).Class(state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
        {
            Setters = { Bound(ContentPresenter.ForegroundProperty, ink) },
        };

    // The button states whose Fluent styles paint a fill on the label presenter (a state style outranks the template's own value).
    private static readonly string[] PresenterFillStates = { ":pointerover", ":pressed", ":disabled", ":checked", ":indeterminate", ":focus-visible" };

    // Every button's label presenter stays unfilled in every state: the template's outer border carries the fill, so a presenter fill
    // shows as a second box inside the button, the size of the label area inside the padding.
    private static Style PresenterWithoutFill(string state, bool listRow = false) =>
        new(x => (listRow ? x.Is<ListBoxItem>() : x.Is<Button>()).Class(state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
        {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, Brushes.Transparent), new Setter(ContentPresenter.BorderBrushProperty, Brushes.Transparent) },
        };

    // The popup the hook found per combo, so a headless test can assert on it without walking the template's visual tree.
    private static readonly ConditionalWeakTable<ComboBox, ComboHook> ComboHooks = new();

    static AvaloniaFormControlStyles() => TemplatedControl.TemplateAppliedEvent.AddClassHandler<ComboBox>(ReshapeComboPopup);

    public static Styles Build()
    {
        var styles = FormStyles();
        foreach (var state in PresenterFillStates) styles.Add(PresenterWithoutFill(state));
        // A list row paints its fill on the template's border too: Fluent's own state fill on the presenter would darken the label box a second time.
        foreach (var state in new[] { ":pointerover", ":pressed", ":disabled", ":selected" }) styles.Add(PresenterWithoutFill(state, listRow: true));
        return styles;
    }

    private static Styles FormStyles()
    {
        var styles = new Styles();
        styles.AddRange(ButtonStyles());
        styles.AddRange(PrefsButtonStyles());
        styles.AddRange(FieldStyles());
        styles.AddRange(WindowAndTabStyles());
        styles.AddRange(PresenterStateStyles());
        styles.AddRange(ListStyles());
        styles.AddRange(ToggleAndComboStyles());
        return styles;
    }

    /// <summary>The form buttons, the Preferences action buttons and their classes.</summary>
    private static Style[] ButtonStyles() => new Style[]
    {
        // Every button centres its content both ways, the unclassed ones too: the Fluent button template leaves a short label such as the FX bar's "+" at the left.
        new Style(x => x.OfType<Button>())
        {
            Setters =
            {
                new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Center),
                new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center),
            },
        },
        // Only the plain form button (class "Button" or "AccentButton" from AvaloniaDefaultLooks): chrome buttons with their own look or template keep their own size.
        new Style(x => Selectors.Or(x.OfType<Button>().Class("Button"), x.OfType<Button>().Class("AccentButton")))
        {
            Setters =
            {
                Bound(TemplatedControl.BackgroundProperty, "Panel3Brush"),
                Bound(TemplatedControl.BorderBrushProperty, "BorderBrush"),
                Bound(TemplatedControl.ForegroundProperty, "TextBrush"),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(BorderThickness)),
                new Setter(TemplatedControl.CornerRadiusProperty, ControlCornerRadius),
                new Setter(TemplatedControl.PaddingProperty, new DynamicResourceExtension("DensButtonPad")),
                new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Center),
                new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center),
                new Setter(Layoutable.MarginProperty, ControlMargin),
                new Setter(Layoutable.MinHeightProperty, new DynamicResourceExtension("DensControlMinHeight")),
            },
        },
        // The default (OK) button: accent fill with label ink from OnAccentBrush; its state rules are the Core look "AccentButton".
        new Style(x => x.OfType<Button>().Class("AccentButton"))
        {
            Setters =
            {
                Bound(TemplatedControl.BackgroundProperty, "AccentBrush"),
                Bound(TemplatedControl.BorderBrushProperty, "AccentBrush"),
                Bound(TemplatedControl.ForegroundProperty, "OnAccentBrush"),
            },
        },
    };

    /// <summary>The Preferences action buttons, their default (OK) look, the More options toggle and the shortcut key boxes.</summary>
    private static Style[] PrefsButtonStyles() => new Style[]
    {
        // Preferences action buttons: the Core look "PrefsActionButton" (class) plus these WPF sizes; the Secondary and Reset classes refine it.
        new Style(x => x.OfType<Button>().Class("PrefsActionButton"))
        {
            Setters =
            {
                Bound(TemplatedControl.BackgroundProperty, "Panel3Brush"),
                Bound(TemplatedControl.BorderBrushProperty, "BorderBrush"),
                Bound(TemplatedControl.ForegroundProperty, "TextBrush"),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(BorderThickness)),
                new Setter(TemplatedControl.CornerRadiusProperty, ControlCornerRadius),
                new Setter(TemplatedControl.PaddingProperty, PrefsActionPadding),
                new Setter(TemplatedControl.FontSizeProperty, PrefsActionFontSize),
                new Setter(Layoutable.MinWidthProperty, PrefsActionMinWidth),
                new Setter(Layoutable.MinHeightProperty, PrefsFieldHeight),
                new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Center),
                new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center),
            },
        },
        // The Preferences default (OK) button takes the WPF PrefsPrimary blue (PrimaryActionButton): a compound selector outranks the PrefsActionButton fill for it.
        new Style(x => x.OfType<Button>().Class("PrefsActionButton").Class("AccentButton"))
        {
            Setters =
            {
                Bound(TemplatedControl.BackgroundProperty, "PrefsPrimaryBrush"),
                Bound(TemplatedControl.BorderBrushProperty, "PrefsPrimaryBrush"),
                Bound(TemplatedControl.ForegroundProperty, "PrefsPrimaryInkBrush"),
            },
        },
        new Style(x => x.OfType<ToggleButton>().Class(PrefsMoreOptionsClass))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                Bound(TemplatedControl.ForegroundProperty, "TextBrush"),
            },
        },
        // Shortcut key boxes and the Clear links: the action button with the field height, so a key box is as tall as a field.
        new Style(x => x.OfType<Button>().Class("PrefsActionSecondary"))
        {
            Setters = { new Setter(Layoutable.HeightProperty, PrefsFieldHeight) },
        },
        new Style(x => x.OfType<Button>().Class("PrefsActionReset"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                Bound(TemplatedControl.ForegroundProperty, "TextBrush"),
                new Setter(TemplatedControl.FontSizeProperty, PrefsResetFontSize),
                new Setter(Layoutable.MinWidthProperty, PrefsResetMinWidth),
                new Setter(Layoutable.HeightProperty, PrefsResetHeight),
            },
        },
    };

    /// <summary>Settings fields: the text, combo and number boxes with their hover and focus edges.</summary>
    private static Style[] FieldStyles() => new Style[]
    {
        // Settings fields: one height for every input, and the same states for each kind. At rest the field is Panel2Brush with the general
        // border; hover and keyboard focus turn the edge to the accent (the combo box's own hover edge), and the fill never changes.
        new Style(x => x.Is<TemplatedControl>().Class(PrefsFieldClass))
        {
            Setters = { new Setter(Layoutable.HeightProperty, PrefsFieldHeight), new Setter(TemplatedControl.FocusAdornerProperty, null) },
        },
        new Style(x => x.OfType<TextBox>().Class(PrefsFieldClass).Template().OfType<Border>().Name("PART_BorderElement"))
        {
            Setters = { Bound(Border.BackgroundProperty, "Panel2Brush"), Bound(Border.BorderBrushProperty, "BorderBrush") },
        },
        new Style(x => x.OfType<TextBox>().Class(PrefsFieldClass).Class(":pointerover").Template().OfType<Border>().Name("PART_BorderElement"))
        {
            Setters = { Bound(Border.BorderBrushProperty, "AccentBrush") },
        },
        new Style(x => x.OfType<TextBox>().Class(PrefsFieldClass).Class(":focus").Template().OfType<Border>().Name("PART_BorderElement"))
        {
            Setters = { Bound(Border.BorderBrushProperty, "AccentBrush") },
        },
        new Style(x => x.OfType<ComboBox>().Class(PrefsFieldClass).Template().OfType<Border>().Name("Background"))
        {
            Setters = { Bound(Border.BackgroundProperty, "Panel2Brush"), Bound(Border.BorderBrushProperty, "BorderBrush") },
        },
        new Style(x => x.OfType<ComboBox>().Class(PrefsFieldClass).Class(":pointerover").Template().OfType<Border>().Name("Background"))
        {
            Setters = { Bound(Border.BorderBrushProperty, "AccentBrush") },
        },
        new Style(x => x.OfType<ComboBox>().Class(PrefsFieldClass).Class(":focus").Template().OfType<Border>().Name("Background"))
        {
            Setters = { Bound(Border.BorderBrushProperty, "AccentBrush") },
        },
        // A number field is its spinner's frame: the edge is the control's BorderBrush (the spinner follows it) and turns accent on hover or focus.
        new Style(x => x.OfType<NumericUpDown>().Class(PrefsFieldClass))
        {
            Setters = { Bound(TemplatedControl.BackgroundProperty, "Panel2Brush"), Bound(TemplatedControl.BorderBrushProperty, "BorderBrush") },
        },
        new Style(x => x.OfType<NumericUpDown>().Class(PrefsFieldClass).Class(":pointerover"))
        {
            Setters = { Bound(TemplatedControl.BorderBrushProperty, "AccentBrush") },
        },
        new Style(x => x.OfType<NumericUpDown>().Class(PrefsFieldClass).Class(":focus-within"))
        {
            Setters = { Bound(TemplatedControl.BorderBrushProperty, "AccentBrush") },
        },
        // The text inside a number field is right-aligned and draws no edge of its own; the spinner frame is the only edge.
        new Style(x => x.OfType<NumericUpDown>().Class(PrefsFieldClass).Template().OfType<TextBox>().Name("PART_TextBox"))
        {
            Setters = { new Setter(TextBox.TextAlignmentProperty, TextAlignment.Right) },
        },
        new Style(x => x.OfType<NumericUpDown>().Class(PrefsFieldClass).Template().OfType<TextBox>().Name("PART_TextBox").Template().OfType<Border>().Name("PART_BorderElement"))
        {
            Setters = { new Setter(Border.BorderBrushProperty, Brushes.Transparent), new Setter(Border.BackgroundProperty, Brushes.Transparent) },
        },
        new Style(x => x.OfType<NumericUpDown>().Class(PrefsFieldClass).Template().OfType<Button>())
        {
            Setters =
            {
                Bound(TemplatedControl.BackgroundProperty, "Panel2Brush"),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                new Setter(TemplatedControl.FontWeightProperty, FontWeight.Normal),
            },
        },
    };

    /// <summary>Windows, tab controls and tabs.</summary>
    private static Style[] WindowAndTabStyles() => new Style[]
    {
        // Every window is WindowBrush, as App.xaml's Window style; panels and cards opt in to Panel2Brush.
        new Style(x => x.OfType<Window>())
        {
            Setters = { Bound(TemplatedControl.BackgroundProperty, "WindowBrush"), Bound(TemplatedControl.ForegroundProperty, "TextBrush") },
        },
        // Tab controls are the WindowBrush frame shade, so a dialog body runs flat to the window edge with no lifted panel, as the WPF dialogs are.
        new Style(x => x.OfType<TabControl>())
        {
            Setters = { Bound(TemplatedControl.BackgroundProperty, "WindowBrush") },
        },
        // Tabs are a slim flat strip with no separators: unselected tabs sit on the window in muted text, the selected one is coloured by
        // the Looks "Tab" rules (Panel2 fill joined to the page, accent top line). Only the top edge carries a border.
        new Style(x => x.OfType<TabItem>())
        {
            Setters =
            {
                new Setter(Layoutable.HeightProperty, new DynamicResourceExtension("DensTabHeight")),
                new Setter(Layoutable.MinHeightProperty, new DynamicResourceExtension("DensTabHeight")),
                new Setter(TemplatedControl.FontSizeProperty, TabHeaderFontSize),
                new Setter(TemplatedControl.PaddingProperty, new DynamicResourceExtension("DensTabPad")),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0, TabHeaderAccentWidth, 0, 0)),
                new Setter(Layoutable.MarginProperty, new Thickness(0)),
                Bound(TemplatedControl.BorderBrushProperty, "WindowBrush"),
                Bound(TemplatedControl.ForegroundProperty, "MutedBrush"),
            },
        },
    };

    /// <summary>The presenter states that put Fluent ink back to the theme brushes, and the text box.</summary>
    private static Style[] PresenterStateStyles() => new Style[]
    {
        // Disabled: the Core look dims the whole button once (opacity); Fluent's own disabled ink on the presenter is put back to the enabled brush.
        DisabledPresenter("Button", "TextBrush"),
        DisabledPresenter("AccentButton", "OnAccentBrush"),
        DisabledPresenter("PrefsActionButton.AccentButton", "DisabledInkBrush"),
        // A disabled list row takes the DisabledInk token on its label (the Core look dims the row once, by opacity).
        new Style(x => x.OfType<ListBoxItem>().Class(":disabled").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
        {
            Setters = { Bound(ContentPresenter.ForegroundProperty, "DisabledInkBrush") },
        },
        // The "More options" header keeps the theme text in every state (Fluent's checked / hover ink on the presenter was near-white on the light panel).
        StatePresenter(PrefsMoreOptionsClass, ":pointerover", "TextBrush"), StatePresenter(PrefsMoreOptionsClass, ":pressed", "TextBrush"),
        StatePresenter(PrefsMoreOptionsClass, ":checked", "TextBrush"), StatePresenter(PrefsMoreOptionsClass, ":disabled", "DisabledInkBrush"),
        StatePresenter("AccentButton", ":pointerover", "OnAccentBrush"), StatePresenter("AccentButton", ":pressed", "OnAccentBrush"),
        StatePresenter("PrefsActionButton.AccentButton", ":pointerover", "PrefsPrimaryInkBrush"), StatePresenter("PrefsActionButton.AccentButton", ":pressed", "PrefsPrimaryInkBrush"),
        new Style(x => x.OfType<TextBox>())
        {
            Setters =
            {
                Bound(TemplatedControl.ForegroundProperty, "TextBrush"),
                Bound(TextBox.CaretBrushProperty, "AccentBrush"),
                Bound(TextBox.SelectionBrushProperty, "AccentSoftBrush"),
                new Setter(TemplatedControl.PaddingProperty, new DynamicResourceExtension("DensTextBoxPad")),
                new Setter(Layoutable.MarginProperty, ControlMargin),
                new Setter(Layoutable.MinHeightProperty, new DynamicResourceExtension("DensControlMinHeight")),
                new Setter(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center),
            },
        },
    };

    /// <summary>List boxes, their rows, and the Preferences navigation rail rows.</summary>
    private static Style[] ListStyles() => new Style[]
    {
        // The WPF list box border is the general border, and its text is the primary text colour.
        new Style(x => x.OfType<ListBox>())
        {
            Setters =
            {
                Bound(TemplatedControl.BackgroundProperty, "WindowBrush"),
                Bound(TemplatedControl.ForegroundProperty, "TextBrush"),
                Bound(TemplatedControl.BorderBrushProperty, "BorderBrush"),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(BorderThickness)),
            },
        },
        new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                Bound(TemplatedControl.ForegroundProperty, "TextBrush"),
                new Setter(TemplatedControl.PaddingProperty, new DynamicResourceExtension("DensListPad")),
                new Setter(Layoutable.MarginProperty, ListBoxItemMargin),
                new Setter(TemplatedControl.CornerRadiusProperty, ControlCornerRadius),
            },
        },
        // A selected row takes the AccentSoft fill on its outer edge too, so Fluent's own square selected fill never shows (as the WPF row).
        new Style(x => x.OfType<ListBoxItem>().Class(":selected"))
        {
            Setters = { Bound(TemplatedControl.BackgroundProperty, "SelectedTintBrush") },
        },
        // Preferences rail rows: stretched across the rail, drawn by PrefsNavRowTemplate (its own border, so the row's fill is the item's), rounded as the WPF navigation tile.
        new Style(x => x.OfType<ListBoxItem>().Class(PrefsNavRowClass))
        {
            Setters =
            {
                new Setter(TemplatedControl.TemplateProperty, PrefsNavRowTemplate.Create()),
                new Setter(Layoutable.HorizontalAlignmentProperty, HorizontalAlignment.Stretch),
                new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch),
                new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center),
                new Setter(TemplatedControl.CornerRadiusProperty, PrefsNavCornerRadius),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(ChromeMetrics.SettingsRailRowInset, 0)),
                new Setter(Layoutable.MarginProperty, new Thickness(0)),
            },
        },
        // The selected rail row carries the 3 DIP accent bar on its left edge, as the WPF SelectedNavigationButton.
        new Style(x => x.OfType<ListBoxItem>().Class(PrefsNavRowClass).Class(":selected"))
        {
            Setters =
            {
                Bound(TemplatedControl.BorderBrushProperty, "PrefsAccentBrush"),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(ChromeMetrics.SettingsRailAccentBarWidth, 0, 0, 0)),
            },
        },
        // The selected rail row's label is semi-bold, as the WPF selected navigation tile.
        new Style(x => x.OfType<ListBoxItem>().Class(PrefsNavRowClass).Class(":selected").Descendant().OfType<TextBlock>())
        {
            Setters = { new Setter(TextBlock.FontWeightProperty, FontWeight.SemiBold) },
        },
        // A hovered rail row takes the hover fill of the WPF PrefsNavItem look; it also covers a selected row, as the WPF trigger does.
        new Style(x => x.OfType<ListBoxItem>().Class(PrefsNavRowClass).Class(":pointerover"))
        {
            Setters = { Bound(TemplatedControl.BackgroundProperty, "HoverBrush") },
        },
    };

    /// <summary>Mixer toggles, step buttons, separators and combo boxes.</summary>
    private static Style[] ToggleAndComboStyles() => new Style[]
    {
        // Mixer mute and solo toggles: the Button base look (dark panel fill, soft edge, control radius) with the muted ink of the WPF toggles.
        new Style(x => x.OfType<ToggleButton>().Class(MixerToggleClass))
        {
            Setters =
            {
                Bound(TemplatedControl.BackgroundProperty, "Panel2Brush"),
                Bound(TemplatedControl.BorderBrushProperty, "BorderBrush"),
                Bound(TemplatedControl.ForegroundProperty, "TextBrush"),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(BorderThickness)),
                new Setter(TemplatedControl.CornerRadiusProperty, ControlCornerRadius),
                new Setter(TemplatedControl.PaddingProperty, new DynamicResourceExtension("DensButtonPad")),
                new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Center),
                new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center),
            },
        },
        // Global tuning step buttons: the form button with the plain button outline and regular glyphs, as the WPF step buttons.
        new Style(x => x.OfType<Button>().Class(StepButtonClass))
        {
            Setters =
            {
                Bound(TemplatedControl.BorderBrushProperty, "BorderBrush"),
                new Setter(TemplatedControl.FontWeightProperty, FontWeight.Normal),
            },
        },
        new Style(x => x.OfType<Separator>())
        {
            Setters =
            {
                Bound(TemplatedControl.BackgroundProperty, "BorderSoftBrush"),
                new Setter(Layoutable.HeightProperty, MenuSeparatorHeight),
                new Setter(Layoutable.MarginProperty, MenuSeparatorMargin),
            },
        },
        new Style(x => x.OfType<ComboBox>())
        {
            Setters =
            {
                Bound(TemplatedControl.BackgroundProperty, "WindowBrush"),
                Bound(TemplatedControl.BorderBrushProperty, "BorderBrush"),
                Bound(TemplatedControl.ForegroundProperty, "TextBrush"),
                new Setter(Layoutable.MinHeightProperty, new DynamicResourceExtension("DensControlMinHeight")),
                new Setter(Layoutable.MarginProperty, ControlMargin),
                new Setter(TemplatedControl.PaddingProperty, new DynamicResourceExtension("DensComboPad")),
            },
        },
    };

    // Fluent sets the glyph's size and shape as local values in its template, which no style can override, so the arrow is set once per applied template.
    // The combo popup gets the same treatment: it follows the window's UI scale (InheritsTransform) and its minimum width is raised to the box
    // width whenever it opens, so a dropdown is never narrower than its closed box at any scale.
}
