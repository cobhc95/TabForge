using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// Publishes the Core palette as brush resources with one dictionary per theme variant; switching the requested variant swaps them.
/// Owns: palette-to-brush resources for the Avalonia app. Does not own: which colour a view uses (views name a palette key or a brush name).
/// Tests: TabForge.Avalonia.Tests.ThemeTests.
/// </summary>
public static class AvaloniaPalette
{
    /// <summary>The brush that paints nothing, for chrome that must stay invisible until hovered.</summary>
    public static IBrush TransparentBrush { get; } = Brushes.Transparent;

    /// <summary>Theme brush name (the name a Core look uses) to the palette key it paints.</summary>
    public static IReadOnlyDictionary<string, string> BrushPaletteNames { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AccentBrush"] = "Accent",
        ["AccentSoftBrush"] = "AccentSoft",
        // Selected / checked toggles: a pale accent tint (light) with an accent outline; the glyph keeps the normal ink.
        ["SelectedTintBrush"] = "SelectedTint",
        ["SelectedOutlineBrush"] = "SelectedOutline",
        ["AccentHoverBrush"] = "AccentHover",
        // The icon master tokens: a glyph at rest, a glyph that is on or selected, and a glyph that is disabled.
        ["IconDefaultBrush"] = "IconDefault",
        ["IconActiveBrush"] = "IconActive",
        ["IconDisabledBrush"] = "IconDisabled",
        // The disabled label ink (the Button look dims the whole control to 0.45, so the ink itself is the strongest of the theme in light).
        ["DisabledInkBrush"] = "DisabledInk",
        ["AccentPressBrush"] = "AccentPress",
        ["SliderThumbFaceBrush"] = "SliderThumbFace",
        ["PrefsPrimaryHoverBrush"] = "PrefsPrimaryHover",
        ["PrefsPrimaryPressBrush"] = "PrefsPrimaryPress",
        ["PrefsPrimaryInkBrush"] = "PrefsPrimaryInk",   // the label ink on the Preferences primary fill (the accent, so it follows the accent)
        ["HoverBrush"] = "HoverSurface",
        ["PressBrush"] = "PressSurface",
        // The keyboard focus ring of every control, part and custom-drawn view (the focusRing semantic token).
        ["FocusRingBrush"] = "FocusRing",
        ["TextBrush"] = "TextPrimary",
        ["TextStrongBrush"] = "TextStrong",
        ["MutedBrush"] = "TextMuted",
        ["WindowBrush"] = "WindowBackground",
        ["PanelBrush"] = "PanelBackground",
        ["BorderBrush"] = "GeneralBorder",
        ["TabCloseHoverBrush"] = "TabCloseHover",
        ["CaptionHoverBrush"] = "CaptionHover",
        ["CaptionPressedBrush"] = "CaptionPressed",
        ["CaptionCloseHoverBrush"] = "CaptionCloseHover",
        ["CaptionClosePressedBrush"] = "CaptionClosePressed",
        ["ScrollThumbBrush"] = "ScrollThumb",
        ["ScrollThumbHoverBrush"] = "ScrollThumbHover",
        // The Preferences accent (the WPF PrefsAccent key), used by the selected Settings rail row's accent bar.
        ["PrefsAccentBrush"] = "PrefsAccent",
        // The Preferences primary (the WPF PrefsPrimary key), the deeper muted blue of the OK button (PrimaryActionButton in PreferencesWindow.xaml).
        ["PrefsPrimaryBrush"] = "PrefsPrimary",
        // The check box outline is the CheckBoxBorder palette value, as App.xaml paints it, not the derived panel shade.
        ["CheckBoxBorderBrush"] = "CheckBoxBorder",
        // The plain button edge is the CheckBoxBorder palette value in both themes, as the WPF boxed buttons paint it (the light general border is faint).
        ["ButtonBorderBrush"] = "CheckBoxBorder",
    };

    /// <summary>Theme brushes that come from the appearance settings, not the palette: the last published appearance paints them (the preset for the inactive variant).</summary>
    public static IReadOnlyList<string> AppearanceBrushNames { get; } = new[]
    {
        "HoverAccentBrush", "Panel2Brush", "Panel3Brush", "BorderSoftBrush", "LegibleBrush", "TabActiveBrush", "TabHoverBrush", "TabRestSurfaceBrush", "TabHoverSurfaceBrush", "TimelineScrollThumbBrush", "TimelineScrollThumbHoverBrush", "TimelineScrollThumbDragBrush",
        "CursorBrush", "OnAccentBrush", "MeterHotBrush",
    };

    /// <summary>True when a look can name this theme brush: it has a palette key or an appearance default.</summary>
    public static bool HasBrush(string name) => BrushPaletteNames.ContainsKey(name) || System.Linq.Enumerable.Contains(AppearanceBrushNames, name);

    /// <summary>Palette keys whose light value comes from the light theme's brushes (the palette holds only their dark value).</summary>
    private static readonly IReadOnlyDictionary<string, string> LightThemeBrushOfPalette = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["WindowBackground"] = "WindowBrush", ["PanelBackground"] = "PanelBrush", ["TextPrimary"] = "TextBrush",
        ["TextStrong"] = "TextStrongBrush", ["TextMuted"] = "MutedBrush", ["TextLegible"] = "LegibleBrush",
        ["TextSecondary"] = "SecondaryTextBrush", ["Accent"] = "AccentBrush", ["AccentSoft"] = "AccentSoftBrush",
        ["SoftBorder"] = "BorderSoftBrush", ["ChromeStrip"] = "ChromeStripBrush",
        // The general border is the BorderBrush the WPF menus, lists and dialogs paint; its dark palette value would frame light menus in near-black.
        ["GeneralBorder"] = "BorderBrush",
    };

    /// <summary>Fluent menu resource names to the theme brush they take, so the menu popups and items paint the WPF menu colours (App.xaml's MenuItem and ContextMenu) in both themes.</summary>
    public static IReadOnlyDictionary<string, string> FluentMenuBrushNames { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["MenuFlyoutPresenterBackground"] = "Panel2Brush",
        ["MenuFlyoutPresenterBorderBrush"] = "BorderBrush",
        ["MenuFlyoutItemForeground"] = "TextBrush",
        ["MenuFlyoutItemForegroundPointerOver"] = "TextBrush",
        ["MenuFlyoutItemForegroundPressed"] = "TextBrush",
        ["MenuFlyoutItemForegroundDisabled"] = "DisabledInkBrush",
        ["MenuFlyoutItemBackgroundPointerOver"] = "HoverBrush",
        ["MenuFlyoutItemBackgroundPressed"] = "PressBrush",
        ["MenuFlyoutItemKeyboardAcceleratorTextForeground"] = "MutedBrush",
        ["MenuFlyoutSubItemChevron"] = "MutedBrush",
        ["MenuFlyoutSubItemChevronPointerOver"] = "TextBrush",
    };

    /// <summary>Fluent text box and combo box resource names to the theme brush they take, so every text box and combo box paints the WPF colours (App.xaml TextBox: window fill, general border, focus border) and Fluent's own state fills never show. The combo box is the exception: its hover and press take the strong HoverBrush and PressBrush so the pointer-over fill is visible, as the owner expects of a hot control.</summary>
    public static IReadOnlyDictionary<string, string> FluentFieldBrushNames { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ListBoxItemForegroundDisabled"] = "DisabledInkBrush",
        // Fluent's own list row fills (SystemControlHighlightListMedium and friends) are the derived hover and press shades, so a pressed row never shows a darker grey.
        ["ListBoxItemBackgroundPointerOver"] = "HoverBrush", ["ListBoxItemBackgroundPressed"] = "PressBrush",
        ["ButtonForegroundDisabled"] = "DisabledInkBrush", ["ToggleButtonForegroundDisabled"] = "DisabledInkBrush", ["TabItemHeaderForegroundDisabled"] = "DisabledInkBrush",
        // The hover and press fills are shades of the panel kept legible for the primary text, so a hovered or pressed row and tab keep that text colour (Fluent swaps in its own).
        ["ListBoxItemForegroundPointerOver"] = "TextBrush", ["ListBoxItemForegroundPressed"] = "TextBrush",
        ["TabItemHeaderForegroundUnselectedPointerOver"] = "TextBrush", ["TabItemHeaderForegroundUnselectedPressed"] = "TextBrush",
        ["TextControlBackground"] = "WindowBrush", ["TextControlBackgroundPointerOver"] = "WindowBrush", ["TextControlBackgroundFocused"] = "WindowBrush", ["TextControlBackgroundDisabled"] = "WindowBrush",
        ["TextControlBorderBrush"] = "BorderBrush", ["TextControlBorderBrushPointerOver"] = "ButtonBorderBrush", ["TextControlBorderBrushFocused"] = "ButtonBorderBrush", ["TextControlBorderBrushDisabled"] = "BorderSoftBrush",
        ["TextControlForeground"] = "TextBrush", ["TextControlForegroundPointerOver"] = "TextBrush", ["TextControlForegroundFocused"] = "TextBrush", ["TextControlForegroundDisabled"] = "MutedBrush",
        ["TextControlPlaceholderForeground"] = "MutedBrush", ["TextControlPlaceholderForegroundPointerOver"] = "MutedBrush", ["TextControlPlaceholderForegroundFocused"] = "MutedBrush",
        ["TextControlSelectionHighlightColor"] = "AccentSoftBrush",
        // A combo box at rest is the window fill; its hover and press take the same strong shades as the buttons beside it (the owner could not see
        // the pointer-over fill when it was WindowBrush), while focus stays the field fill and shows the accent border.
        ["ComboBoxBackground"] = "WindowBrush", ["ComboBoxBackgroundPointerOver"] = "HoverBrush", ["ComboBoxBackgroundPressed"] = "PressBrush", ["ComboBoxBackgroundFocused"] = "WindowBrush", ["ComboBoxBackgroundFocusedPressed"] = "PressBrush", ["ComboBoxBackgroundDisabled"] = "WindowBrush",
        ["ComboBoxBorderBrush"] = "BorderBrush", ["ComboBoxBorderBrushPointerOver"] = "AccentBrush", ["ComboBoxBorderBrushPressed"] = "AccentBrush", ["ComboBoxBorderBrushFocused"] = "ButtonBorderBrush", ["ComboBoxBorderBrushDisabled"] = "BorderSoftBrush",
        ["ComboBoxForeground"] = "TextBrush", ["ComboBoxForegroundDisabled"] = "DisabledInkBrush", ["ComboBoxDropDownGlyphForeground"] = "TextBrush",
        // The WPF radio button is the system one: the field fill, a text-coloured ring and dot, an accent ring on hover.
        ["RadioButtonOuterEllipseFill"] = "WindowBrush", ["RadioButtonOuterEllipseFillPointerOver"] = "WindowBrush", ["RadioButtonOuterEllipseFillPressed"] = "WindowBrush",
        ["RadioButtonOuterEllipseStroke"] = "TextBrush", ["RadioButtonOuterEllipseStrokePointerOver"] = "AccentBrush", ["RadioButtonOuterEllipseStrokePressed"] = "AccentBrush",
        ["RadioButtonOuterEllipseCheckedFill"] = "WindowBrush", ["RadioButtonOuterEllipseCheckedFillPointerOver"] = "WindowBrush", ["RadioButtonOuterEllipseCheckedFillPressed"] = "WindowBrush",
        ["RadioButtonOuterEllipseCheckedStroke"] = "TextBrush", ["RadioButtonOuterEllipseCheckedStrokePointerOver"] = "AccentBrush", ["RadioButtonOuterEllipseCheckedStrokePressed"] = "AccentBrush",
        ["RadioButtonCheckGlyphFill"] = "TextBrush", ["RadioButtonCheckGlyphFillPointerOver"] = "TextBrush", ["RadioButtonCheckGlyphFillPressed"] = "TextBrush",
        ["RadioButtonForeground"] = "TextBrush", ["RadioButtonForegroundPointerOver"] = "TextBrush", ["RadioButtonForegroundPressed"] = "TextBrush", ["RadioButtonForegroundDisabled"] = "MutedBrush",
        ["ComboBoxDropDownBackground"] = "Panel2Brush", ["ComboBoxDropDownBorderBrush"] = "BorderBrush",
    };

    // The brushes of the last published appearance: rebuilt by Publish on every theme or settings change, never per frame.
    // Key: none (one published table per process); ceiling: one holder; thread: UI thread (the drawing code reads it).
    private sealed class ThemeState
    {
        public AppearanceSettings Appearance = new();
        public IReadOnlyDictionary<string, UiColor> Dark = new Dictionary<string, UiColor>();
        public IReadOnlyDictionary<string, UiColor> Light = new Dictionary<string, UiColor>();
        public ResourceDictionary? DarkResources;
        public ResourceDictionary? LightResources;
        public bool Publishing;
        public bool InvalidatePending;
        // Counts the publishes that changed a colour, so a view that caches colours can tell when to read them again.
        public int Version;
        // Raised once per coalesced repaint, after the windows are invalidated: views that set brushes from code repaint them here.
        public event Action? Published;
        public void RaisePublished() => Published?.Invoke();
        // The accent-derived colours computed since the last publish, so a drawn frame does not recompute them; cleared by Rebuild.
        public readonly System.Collections.Concurrent.ConcurrentDictionary<(string Name, bool Light), UiColor> Derived = new();
        // The palette lookup of each theme as a delegate, made once so a drawn frame passes it on without allocating.
        public readonly Func<string, UiColor> DarkInk = key => Get(key, false), LightInk = key => Get(key, true);
    }

    private static readonly ThemeState State = new();

    /// <summary>The number of accent-derived colours cached since the last publish.</summary>
    internal static int DerivedCacheCount => State.Derived.Count;

    /// <summary>Changes on every publish that changed a colour; a colour cache reads again when it differs from the one it filled at.</summary>
    internal static int PublishVersion => State.Version;

    /// <summary>True while a coalesced repaint of the open windows is queued.</summary>
    internal static bool InvalidateQueued => State.InvalidatePending;

    static AvaloniaPalette() => Rebuild();

    /// <summary>
    /// Publishes the appearance every brush lookup reads from now on and updates the registered theme dictionaries, so a live theme or custom
    /// colour change repaints every open window without a restart. Called from ThemeSettingsBinding.ApplyWindow on each settings change.
    /// The two tables are everything the appearance feeds into the brushes, so when a rebuild leaves both equal nothing is refilled or repainted
    /// (<paramref name="force"/> repaints anyway, for an OS theme change that alters no table).
    /// </summary>
    public static void Publish(AppearanceSettings appearance, bool force = false)
    {
        if (State.Publishing) return;   // a resource notification must never re-enter the publish
        State.Publishing = true;
        try
        {
            State.Appearance = appearance;
            var (previousDark, previousLight) = (State.Dark, State.Light);
            Rebuild();
            if (!force && SameColours(previousDark, State.Dark) && SameColours(previousLight, State.Light)) return;
            HoverSurfaces.Invalidate();
            if (State.DarkResources is { } dark && State.LightResources is { } light)
            {
                Fill(dark, light: false);
                Fill(light, light: true);
            }
            State.Version++;
            InvalidateOpenWindows();
        }
        finally { State.Publishing = false; }
    }

    private static bool SameColours(IReadOnlyDictionary<string, UiColor> before, IReadOnlyDictionary<string, UiColor> after)
    {
        if (before.Count != after.Count) return false;
        foreach (var (name, colour) in before)
            if (!after.TryGetValue(name, out var other) || other != colour) return false;
        return true;
    }

    /// <summary>The appearance whose brushes were published last.</summary>
    public static AppearanceSettings PublishedAppearance => State.Appearance;

    private static void Rebuild()
    {
        State.Derived.Clear();
        State.Dark = BuildTable(State.Appearance, light: false);
        State.Light = BuildTable(State.Appearance, light: true);
    }

    // The table a theme variant paints: the live settings for the variant in use, the matching preset for the other one, and both
    // presets while the theme is System (which follows the OS and writes no colour settings).
    private static IReadOnlyDictionary<string, UiColor> BuildTable(AppearanceSettings appearance, bool light)
    {
        var system = string.Equals(appearance.ThemeMode, "System", StringComparison.OrdinalIgnoreCase);
        var lightMode = string.Equals(appearance.ThemeMode, "Light", StringComparison.OrdinalIgnoreCase);
        var inUse = lightMode ? light : !light;   // Dark and Custom paint the dark variant
        var source = !system && inUse ? appearance : PresetAppearance(light);
        var brushes = new Dictionary<string, UiColor>(StringComparer.Ordinal);
        foreach (var (name, colour) in ThemeColours.Brushes(source, light, ParseColourText)) brushes[name] = colour;
        return brushes;
    }

    private static AppearanceSettings PresetAppearance(bool light)
    {
        var settings = new AppearanceSettings();
        ThemeColours.ApplyPreset(settings, light);
        return settings;
    }

    /// <summary>True when <paramref name="appearance"/> paints the light theme: Light always, System as the OS currently shows it (<paramref name="osLight"/>), Dark and Custom never.</summary>
    public static bool PaintsLight(AppearanceSettings appearance, bool osLight) =>
        string.Equals(appearance.ThemeMode, "System", StringComparison.OrdinalIgnoreCase) ? osLight : string.Equals(appearance.ThemeMode, "Light", StringComparison.OrdinalIgnoreCase);

    /// <summary>The colour of an appearance-settings theme brush (a name in <see cref="AppearanceBrushNames"/>) in the dark or the light theme.</summary>
    public static UiColor ThemeBrush(string name, bool light) => (light ? State.Light : State.Dark)[name];

    /// <summary>The palette lookup of the dark or the light theme as a delegate, built once: a drawn frame passes it on without allocating a closure.</summary>
    public static Func<string, UiColor> InkOf(bool light) => light ? State.LightInk : State.DarkInk;

    /// <summary>The brush of a palette key in the dark or the light theme: the published one, replaced on every Publish, so a drawn view never holds an older theme.</summary>
    public static IBrush PaletteBrush(string paletteName, bool light) =>
        (light ? State.LightResources : State.DarkResources) is { } resources && resources.TryGetValue(paletteName, out var brush) && brush is IBrush published
            ? published
            : BrushOf(Get(paletteName, light));

    // Custom-drawn views read Get/ThemeBrush directly; their cached drawing is only repainted when they are invalidated, so a published
    // change repaints every open window (a resource change alone repaints only the DynamicResource consumers).
    // Coalesced: any number of publishes before the next render pass invalidate the windows once.
    internal static void InvalidateOpenWindows()
    {
        if (State.InvalidatePending) return;
        State.InvalidatePending = true;
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            State.InvalidatePending = false;
            if (Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                foreach (var window in desktop.Windows) Invalidate(window);
            State.RaisePublished();
        }, global::Avalonia.Threading.DispatcherPriority.Render);
    }

    /// <summary>
    /// Runs <paramref name="refresh"/> when the theme variant changes and once after each published appearance, while <paramref name="control"/> is in the visual tree.
    /// For a view that sets brushes or colours from code: a resource change alone does not repaint those. Call it from the constructor.
    /// </summary>
    internal static void FollowTheme(this Control control, Action refresh)
    {
        control.ActualThemeVariantChanged += (_, _) => refresh();
        control.AttachedToVisualTree += (_, _) => State.Published += refresh;
        control.DetachedFromVisualTree += (_, _) => State.Published -= refresh;
    }

    private static void Invalidate(Visual visual)
    {
        if (visual is Control control) control.InvalidateVisual();
        foreach (var child in visual.GetVisualChildren()) Invalidate(child);
    }

    /// <summary>The colour of a palette key in the dark or the light theme, as the published brushes paint it.</summary>
    public static UiColor Get(string paletteName, bool light)
    {
        // The hover and press surfaces are the one palette pair Avalonia paints stronger than Core, so a hovered control stands off the window
        // and the cards; the Core values (shared with the WPF app) stay as they are.
        if (HoverSurfaces.IsOverridden(paletteName)) return HoverSurfaces.Value(paletteName, light);
        if (ThemeColours.DerivesFromAccent(paletteName))
            return State.Derived.GetOrAdd((paletteName, light), key => ThemeColours.FromAccent(key.Name, Get("Accent", key.Light), Get("AccentSoft", key.Light), Get("WindowBackground", key.Light), key.Light));
        if (light && LightThemeBrushOfPalette.TryGetValue(paletteName, out var brush) && State.Light.TryGetValue(brush, out var lightBrush)) return lightBrush;
        if (!light && DarkThemeBrushOfPalette.TryGetValue(paletteName, out var darkBrush) && State.Dark.TryGetValue(darkBrush, out var darkColour)) return darkColour;
        return Palette.Get(paletteName, light);
    }

    // Palette keys whose dark value is the dark preset's brush, as WPF paints them: the general border (its brighter palette value would frame
    // the dark menus too light), and the selection tint, the blend of accent and window that WPF paints (its Core palette value is another blue).
    // The keys whose dark preset brush equals the palette value are listed too, so a published Custom colour (accent, page, panel, text) paints
    // them live instead of leaving the palette's older dark value behind.
    private static readonly IReadOnlyDictionary<string, string> DarkThemeBrushOfPalette = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["WindowBackground"] = "WindowBrush",
        ["PanelBackground"] = "PanelBrush",
        ["TextPrimary"] = "TextBrush",
        ["TextStrong"] = "TextStrongBrush",
        ["TextMuted"] = "MutedBrush",
        ["Accent"] = "AccentBrush",
        ["ChromeStrip"] = "ChromeStripBrush",
        ["GeneralBorder"] = "BorderBrush",
        ["AccentSoft"] = "AccentSoftBrush",
    };

    /// <summary>Adds one brush per palette key, and one per theme brush name, to the dark and the light theme dictionary.</summary>
    public static void Register(IResourceDictionary resources)
    {
        var dark = new ResourceDictionary();
        var light = new ResourceDictionary();
        State.DarkResources = dark;
        State.LightResources = light;
        Fill(dark, light: false);
        Fill(light, light: true);
        resources.ThemeDictionaries[ThemeVariant.Dark] = dark;
        resources.ThemeDictionaries[ThemeVariant.Light] = light;
    }

    // Brings one theme dictionary to the published tables; Publish calls it again so the resources never hold an older appearance.
    // A brush already in the dictionary keeps its instance and takes the new colour (consumers repaint, nothing is replaced); the menu and field names alias the brush they name.
    private static void Fill(ResourceDictionary dictionary, bool light)
    {
        foreach (var name in Palette.Names) Put(dictionary, name, Get(name, light));
        foreach (var name in HoverSurfaces.Names) Put(dictionary, name, Get(name, light));
        foreach (var (brushName, paletteName) in BrushPaletteNames) Put(dictionary, brushName, Get(paletteName, light));
        foreach (var (name, colour) in SliderThumbStateColours(light)) Put(dictionary, name, colour);
        var brushes = light ? State.Light : State.Dark;
        foreach (var brushName in AppearanceBrushNames) Put(dictionary, brushName, brushes[brushName]);
        foreach (var (menuName, brushName) in FluentMenuBrushNames) dictionary.TryAdd(menuName, dictionary[brushName]!);
        foreach (var (fieldName, brushName) in FluentFieldBrushNames) dictionary.TryAdd(fieldName, dictionary[brushName]!);
    }

    private static void Put(ResourceDictionary dictionary, string name, UiColor colour)
    {
        if (dictionary.TryGetValue(name, out var existing) && existing is SolidColorBrush brush) brush.Color = ColorOf(colour);
        else dictionary[name] = BrushOf(colour);
    }

    // The slider thumb's state looks, derived from the SystemSliderThumb and SystemSliderThumbBorder keys: hover and pressed blend the fill and the
    // border toward the accent by these amounts; disabled lifts the border toward the fill.
    private const double ThumbHoverFillAmount = 0.12, ThumbHoverBorderAmount = 0.5, ThumbPressedFillAmount = 0.2, ThumbPressedBorderAmount = 0.7, ThumbDisabledBorderAmount = 0.4;

    private static IEnumerable<KeyValuePair<string, UiColor>> SliderThumbStateColours(bool light)
    {
        var fill = Get("SystemSliderThumb", light);
        var border = Get("SystemSliderThumbBorder", light);
        var accent = Get("Accent", light);
        KeyValuePair<string, UiColor> Entry(string name, UiColor colour) => new(name, colour);
        yield return Entry("SliderThumbHover", ThemeColours.Blend(fill, accent, ThumbHoverFillAmount));
        yield return Entry("SliderThumbHoverBorder", ThemeColours.Blend(border, accent, ThumbHoverBorderAmount));
        yield return Entry("SliderThumbPressed", ThemeColours.Blend(fill, accent, ThumbPressedFillAmount));
        yield return Entry("SliderThumbPressedBorder", ThemeColours.Blend(border, accent, ThumbPressedBorderAmount));
        yield return Entry("SliderThumbDisabledBorder", ThemeColours.Blend(border, fill, ThumbDisabledBorderAmount));
    }

    /// <summary>The tuning string fill: the palette's light-to-dark gradient down the string, as the WPF tuning editor draws it.</summary>
    public static IBrush TuningStringBrush(bool light) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(ColorOf(Get("TuningStringLight", light)), 0),
            new GradientStop(ColorOf(Get("TuningStringDark", light)), 1),
        },
    };

    /// <summary>The Avalonia colour of a Core colour (same channels).</summary>
    public static Color ColorOf(UiColor colour) => Color.FromArgb(colour.A, colour.R, colour.G, colour.B);
    public static Color ColorOf(TabForge.Models.Rgba colour) => ColorOf(colour.ToUi());

    /// <summary>The theme brush <paramref name="name"/> as a drawing-port colour in the owner's variant; default when it is missing or not a solid brush.</summary>
    public static UiColor BrushColour(this StyledElement owner, string name) =>
        owner.TryFindResource(name, owner.ActualThemeVariant, out var value) && value is ISolidColorBrush brush
            ? new UiColor(brush.Color.A, brush.Color.R, brush.Color.G, brush.Color.B)
            : default;

    // Appearance colour text through Avalonia's parser (hex or a named colour).
    private static UiColor? ParseColourText(string? text) =>
        Color.TryParse(text, out var c) ? new UiColor(c.A, c.R, c.G, c.B) : null;

    private static SolidColorBrush BrushOf(UiColor colour) => new(ColorOf(colour));
}
