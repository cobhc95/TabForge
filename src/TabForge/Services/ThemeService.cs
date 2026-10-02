using System.Windows;
using System.Windows.Media;

namespace TabForge.Services;

// Owns: applying the appearance settings to the application resources at runtime.
// Does not own: the stored settings and the XAML styles.
// Tests: TestTabUi, TestReadableTextTokens.
/// <summary>
/// Applies the appearance settings to the application resources at runtime. The XAML references these
/// brushes with <c>DynamicResource</c>, so changing a colour here restyles the live UI immediately.
/// </summary>
public static class ThemeService
{
    /// <summary>The smallest font size (device-independent px) any label may use; App.xaml's <c>MinFontSize</c> carries the same value.</summary>
    public const double MinFontSize = 11;

    public static void Apply(AppearanceSettings a, Window? window = null)
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;

        // Dark / Light / System are presets: choosing one copies its palette into the settings (see
        // ApplyPreset), and every colour stays user-editable. Apply always paints the stored colours.
        var light = string.Equals(a.ThemeMode, "Custom", StringComparison.OrdinalIgnoreCase)
            ? IsLightColour(a.Background) : IsLightTheme(a.ThemeMode);
        // Application resources are frozen by WPF, so a theme change replaces brush instances. Remember
        // the current ones so controls that captured them (StaticResource / FindResource in code) can be
        // re-linked to the new theme below.
        var previous = new Dictionary<Brush, string>(ReferenceEqualityComparer.Instance);
        foreach (var key in resources.Keys.OfType<string>())
            if (resources[key] is Brush b && !previous.ContainsKey(b)) previous[b] = key;
        var background = a.Background;
        var panel = a.Panel;
        var titleBar = a.TitleBarColour;
        var activeTab = a.ActiveTabColour;
        var hoverTab = a.TabHoverColour;
        var text = a.Text;
        var muted = a.Muted;
        Set(resources, "WindowBrush", background);
        Set(resources, "PanelBrush", panel);
        Set(resources, "Panel2Brush", Shade(panel, light ? 0.97 : 1.10));
        Set(resources, "Panel3Brush", Shade(panel, light ? 0.92 : 1.22));
        Set(resources, "ChromeStripBrush", titleBar);
        Set(resources, "TabActiveBrush", activeTab);
        Set(resources, "TabHoverBrush", hoverTab);
        Set(resources, "BorderBrush", Shade(panel, light ? 0.78 : 1.55));
        Set(resources, "BorderSoftBrush", Shade(panel, light ? 0.88 : 1.28));
        // Light theme hover/press need a stronger step than dark to be visible on grey panels.
        Set(resources, "HoverBrush", Shade(panel, light ? 0.86 : 1.35));
        Set(resources, "PressBrush", Shade(panel, light ? 0.76 : 1.50));
        Set(resources, "TextBrush", text);
        Set(resources, "TextStrongBrush", light ? "#111111" : "#FFFFFF");
        Set(resources, "MutedBrush", muted);
        Set(resources, "LegibleBrush", Blend(muted, text, 0.2));   // mock: lifted muted text for tiny labels
        Set(resources, "SecondaryTextBrush", Blend(muted, text, 0.45));
        Set(resources, "AccentBrush", a.Accent);
        Set(resources, "AccentSoftBrush", Blend(a.Accent, background, 0.55));
        Set(resources, "SelectionBrush", a.SelectionColour);
        Set(resources, "HoverAccentBrush", a.HoverColour);
        Set(resources, "TimelineScrollTrackBrush", Shade(panel, light ? 0.88 : 0.82));
        Set(resources, "TimelineScrollThumbBrush", a.TimelineScrollBarThumbColour);
        Set(resources, "TimelineScrollThumbHoverBrush", Blend(a.TimelineScrollBarThumbColour, "#FFFFFF", 0.20));
        Set(resources, "TimelineScrollThumbDragBrush", Blend(a.TimelineScrollBarThumbColour, "#FFFFFF", 0.34));
        Set(resources, "CursorBrush", a.CursorColour);
        Set(resources, "PlayBrush", a.PlayheadColour);
        // Level meters (input and track): the usual green -> amber -> red, tuned for the background.
        Set(resources, "MeterBrush", light ? "#2E9E4F" : "#3FCB6A");
        Set(resources, "MeterHotBrush", light ? "#C99A1E" : "#E6B93A");
        Set(resources, "PaperDarkBrush", a.DarkScorePaperColour);
        Set(resources, "PaperLightBrush", a.LightScorePaperColour);

        TabForge.Visualization.VisualTheme.IsLight = light;
        Set(resources, "WorkspaceBrush", light ? "#8C8C8C" : "#0B0D10"); // workspace grey
        Set(resources, "InstrumentHostBrush", light ? "#C0C0C0" : "#12151A");
        ApplyDerivedBrushes(resources, light);
        var density = a.Density;
        resources["ToolButtonHeight"] = density == "Compact" ? 23.0 : density == "Spacious" ? 32.0 : 27.0;
        resources["ToolButtonFontSize"] = density == "Compact" ? 12.0 : density == "Spacious" ? 14.0 : 13.0;
        resources["ToolButtonMinWidth"] = density == "Compact" ? 26.0 : density == "Spacious" ? 36.0 : 30.0;
        UiMotion.Configure(a.ReduceAnimations, a.AnimationSpeed);
        // Settings-window palette aliases (the window uses these names).
        foreach (var (alias, source) in new[] { ("BrushWindow", "WindowBrush"), ("BrushRail", "PanelBrush"), ("BrushCard", "Panel2Brush"), ("BrushInput", "PanelBrush"), ("BrushSearch", "PanelBrush"), ("BrushBorder", "BorderBrush"), ("BrushBorderSoft", "BorderSoftBrush"), ("BrushInputBorder", "BorderBrush"), ("BrushText", "TextBrush"), ("BrushMuted", "MutedBrush"), ("BrushMuted2", "MutedBrush"), ("BrushAccentSoft", "AccentSoftBrush"), ("BrushNavHover", "HoverBrush"), ("BrushNavSelected", "AccentSoftBrush"), ("BrushFooter", "PanelBrush"), ("BrushSecondary", "Panel3Brush"), ("BrushSecondaryHover", "HoverBrush") })
            resources[alias] = resources[source];
        RebindStaleBrushes(resources, previous);
        if (Application.Current is not null)
            foreach (Window w in Application.Current.Windows) EnsureReadableText(w);
        Shell.WindowPolish.RefreshTitleBars();

        if (window is not null)
        {
            try { window.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(a.FontFamily) ? "Segoe UI" : a.FontFamily); }
            catch (ArgumentException) { window.FontFamily = new FontFamily("Segoe UI"); }
            // UI scale and density scale the whole window (layout, text, icons, spacing) as vectors, so
            // nothing is left at the old size and icons stay sharp; font size is the unscaled base.
            window.FontSize = Math.Clamp(a.FontSize, 8, 36);
            ApplyLayoutScale(window, LayoutScale(a));
        }
    }

    private static readonly DependencyProperty[] BrushProperties =
    {
        System.Windows.Controls.Control.BackgroundProperty, System.Windows.Controls.Control.ForegroundProperty,
        System.Windows.Controls.Control.BorderBrushProperty, System.Windows.Controls.Border.BackgroundProperty,
        System.Windows.Controls.Border.BorderBrushProperty, System.Windows.Controls.Panel.BackgroundProperty,
        System.Windows.Controls.TextBlock.ForegroundProperty, System.Windows.Shapes.Shape.FillProperty,
        System.Windows.Shapes.Shape.StrokeProperty,
    };

    // Walks every open window once (only on a theme change) and turns captured theme brushes into live
    // resource references, so everything follows this and later theme changes.
    private static void RebindStaleBrushes(ResourceDictionary resources, Dictionary<Brush, string> previous)
    {
        var stale = new Dictionary<Brush, string>(ReferenceEqualityComparer.Instance);
        foreach (var (brush, key) in previous)
            if (!ReferenceEquals(resources[key], brush)) stale[brush] = key;
        if (stale.Count == 0 || Application.Current is null) return;
        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        void Visit(DependencyObject node)
        {
            if (!visited.Add(node)) return;
            foreach (var property in BrushProperties)
            {
                if (node.ReadLocalValue(property) is Brush brush && stale.TryGetValue(brush, out var key) && node is FrameworkElement fe)
                    fe.SetResourceReference(property, key);
                else if (node.ReadLocalValue(property) is Brush b2 && stale.TryGetValue(b2, out var key2) && node is FrameworkContentElement fce)
                    fce.SetResourceReference(property, key2);
            }
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) Visit(child);
            if (node is Visual or System.Windows.Media.Media3D.Visual3D)
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Visit(VisualTreeHelper.GetChild(node, i));
            if (node is FrameworkElement { ContextMenu: { } menu }) Visit(menu);
        }
        foreach (Window window in Application.Current.Windows) Visit(window);
    }

    private static bool _contrastHooked;

    /// <summary>Runs the readability pass on every window when it loads (and again after theme changes).</summary>
    public static void HookReadability()
    {
        if (_contrastHooked) return;
        _contrastHooked = true;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) => { if (s is Window w) EnsureReadableText(w); }));
    }

    private static double Luminance(Color c)
    {
        double Ch(byte v) { var x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }

    private static double Contrast(Color a, Color b)
    {
        var la = Luminance(a); var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>
    /// Readability guard: any text whose colour is too close to the surface behind it (e.g. dark text on a
    /// blue primary button in the light theme) is switched to white or near-black, whichever reads better.
    /// Only fixes hard-to-read pairs (contrast below 4:1); intended colours are left alone.
    /// </summary>
    public static void EnsureReadableText(DependencyObject root)
    {
        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        void Visit(DependencyObject node)
        {
            if (!visited.Add(node)) return;
            if (node is System.Windows.Controls.TextBlock text && text.Foreground is SolidColorBrush fg && fg.Color.A > 200 &&
                SurfaceBehind(text) is { } surface && Contrast(fg.Color, surface) < 4.0)
            {
                var white = Colors.White; var dark = Color.FromRgb(0x14, 0x14, 0x14);
                text.SetCurrentValue(System.Windows.Controls.TextBlock.ForegroundProperty,
                    new SolidColorBrush(Contrast(white, surface) >= Contrast(dark, surface) ? white : dark));
            }
            var count = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(node) : 0;
            for (var i = 0; i < count; i++) Visit(VisualTreeHelper.GetChild(node, i));
        }
        Visit(root);
    }

    // First opaque solid background actually painted behind the element. A Control's Background only shows
    // if its template draws it (through a Border/Panel, which is checked anyway); reading it directly made
    // CheckBox labels "sit on" the unused default white and get forced to near-black in dark dialogs.
    private static Color? SurfaceBehind(DependencyObject element)
    {
        for (var node = VisualTreeHelper.GetParent(element); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            var brush = node switch
            {
                System.Windows.Controls.Border b => b.Background,
                System.Windows.Controls.Panel p => p.Background,
                Window w => w.Background,
                _ => null,
            };
            if (brush is SolidColorBrush s && s.Color.A > 200 && s.Opacity > 0.8) return s.Color;
            if (node is Window) break;
        }
        return null;
    }

    private static Dictionary<string, Color>? _darkDefaults;

    /// <summary>
    /// Before any UI is built: make every app brush an unfrozen instance (so theme changes recolour it in
    /// place for controls that captured it in code) and remember the dark defaults for light derivation.
    /// </summary>
    public static void PrepareMutableBrushes()
    {
        var resources = Application.Current?.Resources;
        HookReadability();
        if (resources is null || _darkDefaults is not null) return;
        _darkDefaults = new();
        foreach (var key in resources.Keys.OfType<string>().ToList())
            if (resources[key] is SolidColorBrush brush)
            {
                _darkDefaults[key] = brush.Color;
                if (brush.IsFrozen) resources[key] = new SolidColorBrush(brush.Color);
            }
    }

    // Chrome, playbar and track-toggle brushes are not user colours: derive light versions from the dark set.
    private static void ApplyDerivedBrushes(ResourceDictionary resources, bool light)
    {
        if (_darkDefaults is null) return;
        void Put(string key, Color c)
        {
            if (resources[key] is SolidColorBrush b && !b.IsFrozen) b.Color = c; else resources[key] = new SolidColorBrush(c);
        }
        Color Mix(Color a, Color b, double t) => Color.FromArgb(
            (byte)Math.Round(a.A + (b.A - a.A) * t), (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
        foreach (var (key, dark) in _darkDefaults)
        {
            if (!(key.StartsWith("Playbar") || key.StartsWith("Track") || key.StartsWith("Caption") || key.StartsWith("Chrome") ||
                  key.StartsWith("Tab") || key.StartsWith("AddTrack") || key.StartsWith("ScrollThumb") || key == "DangerBrush")) continue;
            if (key is "ChromeStripBrush" or "TabHoverBrush" or "TabActiveBrush") continue; // user colours
            if (!light) { Put(key, dark); continue; }
            var light2 = key switch
            {
                "ChromeGlyphBrush" => Color.FromArgb(0xD9, 0x1E, 0x24, 0x2B),
                "TabIdleGlyphBrush" => Color.FromRgb(0x5B, 0x66, 0x73),
                "CaptionHoverBrush" => Color.FromArgb(0x18, 0, 0, 0),
                "CaptionPressedBrush" => Color.FromArgb(0x26, 0, 0, 0),
                "ChromeBorderBrush" => Color.FromRgb(0xC9, 0xCF, 0xD6),
                "TabCloseHoverBrush" => Color.FromRgb(0xD5, 0xDA, 0xE0),
                // Follows the theme's panel colour (was a fixed near-white that stood out on the grey theme).
                "PlaybarSurfaceBrush" => resources["Panel2Brush"] is SolidColorBrush panel
                    ? Color.FromArgb(0xE6, panel.Color.R, panel.Color.G, panel.Color.B) : Color.FromArgb(0xE6, 0xCC, 0xD1, 0xD7),
                "PlaybarBorderBrush" => Color.FromArgb(0x70, 0x8C, 0x96, 0xA3),
                "TrackToggleIdleBrush" => Color.FromRgb(0xD6, 0xDA, 0xDF),
                // Mute/audible stay recognisably green/red but as mid tints that suit the grey theme.
                "TrackAudibleBrush" => Color.FromRgb(0x7F, 0xC6, 0x9C),
                "TrackMutedBrush" => Color.FromRgb(0xDB, 0x7C, 0x88),
                "TrackSoloBrush" => Color.FromRgb(0xF0, 0xD6, 0x86),
                "TrackSliderTrackBrush" => Color.FromRgb(0x9C, 0xA5, 0xB0),
                "TrackSliderTrackBorderBrush" => Color.FromRgb(0x86, 0x8F, 0x9A),
                "TrackSoloTextBrush" => Color.FromRgb(0x6A, 0x4B, 0x00),
                "ScrollThumbBrush" => Color.FromRgb(0x8E, 0x96, 0x9F),
                "ScrollThumbHoverBrush" => Color.FromRgb(0x74, 0x7C, 0x86),
                // Transport borders: a deeper shade of the button's hue, matching its pastel fill.
                "PlaybarRewindBorderBrush" or "PlaybarPlayBorderBrush" or "PlaybarStopBorderBrush" or "PlaybarNextBorderBrush"
                    => Mix(dark, Colors.Black, 0.18),
                _ when key.EndsWith("BorderBrush") || key is "CaptionCloseHoverBrush" or "CaptionClosePressedBrush" or "DangerBrush"
                    or "AddTrackSurfaceBrush" => dark,
                // Transport buttons: clean pastel of each button's own accent (a tint of the dark fill looked muddy).
                "PlaybarRewindBrush" or "PlaybarPlayBrush" or "PlaybarStopBrush" or "PlaybarNextBrush"
                    or "PlaybarMetroBrush" or "PlaybarBeatBrush" or "PlaybarLoopBrush" => Mix(PlaybarAccent(key), Colors.White, 0.74),
                _ => Mix(dark, Colors.White, 0.62), // coloured button surfaces become light tints of themselves
            };
            Put(key, light2);
        }
    }

    private static Color PlaybarAccent(string key) => key switch
    {
        "PlaybarRewindBrush" => Color.FromRgb(0x80, 0x5E, 0xBD),
        "PlaybarPlayBrush" => Color.FromRgb(0x18, 0xB9, 0x66),
        "PlaybarStopBrush" => Color.FromRgb(0xB9, 0x5A, 0x38),
        "PlaybarNextBrush" => Color.FromRgb(0xD9, 0x9E, 0x16),
        "PlaybarMetroBrush" => Color.FromRgb(0xD0, 0x6A, 0x36),
        "PlaybarBeatBrush" => Color.FromRgb(0x8C, 0x66, 0xD6),
        _ => Color.FromRgb(0x18, 0xA0, 0x70), // loop
    };

    public static double LayoutScale(AppearanceSettings a)
    {
        var density = a.Density == "Compact" ? 0.92 : a.Density == "Spacious" ? 1.1 : 1.0;
        return Math.Clamp(Math.Clamp(a.UiScale, 0.8, 1.5) * density, 0.7, 1.7);
    }

    /// <summary>Current UI-scale transform, shared with popups and context menus (they live outside the window
    /// tree) through the "UiScaleTransform" resource.</summary>
    public static ScaleTransform UiScaleTransform { get; private set; } = new(1, 1);

    public static void ApplyLayoutScale(Window window, double scale)
    {
        // A new transform each time: once WPF has frozen the shared instance (resources and templates freeze
        // Freezables), changing ScaleX threw and the UI scale setting silently stopped working.
        var transform = new ScaleTransform(scale, scale);
        transform.Freeze();
        UiScaleTransform = transform;
        if (window.Content is FrameworkElement root)
            root.LayoutTransform = Math.Abs(scale - 1) < 0.001 ? Transform.Identity : transform;
        if (Application.Current?.Resources is { } resources) resources["UiScaleTransform"] = transform;
    }

    /// <summary>
    /// Theme presets. Picking Dark, Light or System writes the matching palette into the appearance
    /// settings in one go (interface, accent, score page); the user can then change any single colour.
    /// </summary>
    public static void ApplyPreset(AppearanceSettings a, string mode)
    {
        a.ThemeMode = mode;
        if (string.Equals(mode, "Custom", StringComparison.OrdinalIgnoreCase)) return;
        if (IsLightTheme(mode))
        {
            // Mid light grey all round (not white): grey chrome, darker workspace, off-white score page.
            a.Background = "#B4B4B4"; a.Panel = "#C6C6C6"; a.TitleBarColour = "#BABABA";
            a.ActiveTabColour = "#D2D2D2"; a.TabHoverColour = "#BEBEBE"; a.Text = "#111111"; a.Muted = "#3A3A3A";
            a.Accent = "#2F6FC2"; a.SelectionColour = "#2F6FC2"; a.HoverColour = "#5B6570";
            a.ScorePaper = "Light"; a.LightScorePaperColour = "#E6E6E6"; a.LightScoreLinesColour = "#B2B2B2";
        }
        else
        {
            a.Background = "#14161A"; a.Panel = "#1C1F24"; a.TitleBarColour = "#161616";
            a.ActiveTabColour = "#333333"; a.TabHoverColour = "#292929"; a.Text = "#E7EAEF"; a.Muted = "#98A1AE";
            a.Accent = "#4C9AFF"; a.SelectionColour = "#4C9AFF"; a.HoverColour = "#98A1AE";
            a.ScorePaper = "Dark";
        }
    }

    private static bool IsLightColour(string hex)
    {
        return TryParse(hex, out var colour) && Luminance(colour) > 0.35;
    }

    public static bool IsLightTheme(string mode)
    {
        if (string.Equals(mode, "Light", StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.Equals(mode, "System", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light != 0;
        }
        // Registry unavailable or locked down: fall back to dark.
        catch (System.Security.SecurityException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (System.IO.IOException) { return false; }
    }

    private static void Set(ResourceDictionary resources, string key, string hex)
    {
        if (!TryParse(hex, out var colour)) return;
        // Recolour the existing brush in place: controls built in code captured this brush instance with
        // FindResource, so replacing it would leave them on the old theme. Mutating updates them all.
        if (resources[key] is SolidColorBrush existing && !existing.IsFrozen) existing.Color = colour;
        else resources[key] = new SolidColorBrush(colour);
    }

    /// <summary>Multiplies the colour towards white (factor &gt; 1) or black (factor &lt; 1).</summary>
    public static string Shade(string hex, double factor)
    {
        if (!TryParse(hex, out var c)) return hex;
        byte Channel(byte v) => (byte)Math.Clamp(Math.Round(v * factor), 0, 255);
        return $"#{Channel(c.R):X2}{Channel(c.G):X2}{Channel(c.B):X2}";
    }

    /// <summary>Blends <paramref name="hex"/> towards <paramref name="other"/> by <paramref name="amount"/> (0-1).</summary>
    public static string Blend(string hex, string other, double amount)
    {
        if (!TryParse(hex, out var a) || !TryParse(other, out var b)) return hex;
        amount = Math.Clamp(amount, 0, 1);
        byte Mix(byte x, byte y) => (byte)Math.Clamp(Math.Round(x + (y - x) * amount), 0, 255);
        return $"#{Mix(a.R, b.R):X2}{Mix(a.G, b.G):X2}{Mix(a.B, b.B):X2}";
    }

    public static bool TryParse(string? hex, out Color colour) => TabForge.Visualization.ColourText.TryParse(hex, out colour);

    /// <summary>The scale highlight colour for a <see cref="ScaleHighlightStyles.Colours"/> name (dark / light theme variants).</summary>
    public static Color ScaleHighlightColour(string? name, bool light) => (name ?? "Blue") switch
    {
        "Green" => light ? Rgb(0x3E, 0x8E, 0x4A) : Rgb(0x3F, 0xA0, 0x5A),
        "Amber" => light ? Rgb(0xC0, 0x84, 0x10) : Rgb(0xE0, 0xA5, 0x30),
        "Purple" => light ? Rgb(0x7A, 0x50, 0xB8) : Rgb(0x9C, 0x72, 0xE0),
        "Red" => light ? Rgb(0xB8, 0x40, 0x40) : Rgb(0xD8, 0x5A, 0x5A),
        "Teal" => light ? Rgb(0x1E, 0x8C, 0x8C) : Rgb(0x2F, 0xB0, 0xAA),
        "Grey" => light ? Rgb(0x70, 0x78, 0x84) : Rgb(0x9A, 0xA2, 0xAE),
        _ => light ? Rgb(0x3F, 0x72, 0xC0) : Rgb(0x4C, 0x8A, 0xE0),
    };

    /// <summary>The fret marker colour for a <see cref="FretMarkerLevels.Colours"/> name; null = the theme default.</summary>
    public static Color? FretMarkerColour(string? name) => name switch
    {
        "White" => Rgb(0xF2, 0xF2, 0xF2),
        "Silver" => Rgb(0xB8, 0xBE, 0xC6),
        "Amber" => Rgb(0xE0, 0xA5, 0x30),
        "Blue" => Rgb(0x5C, 0x9A, 0xF0),
        "Green" => Rgb(0x50, 0xB8, 0x60),
        _ => null,
    };

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}
