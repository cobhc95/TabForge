using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Platform.Theme;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: applying the Appearance settings to the preview window (theme variant, where System follows the OS; UI scale; interface font family and size;
//     the density's control padding) and to each arrangement pane (track row tint and muted track dimming). Applied at start-up and on every settings change.
// Does not own: the palette colours (AvaloniaPalette and ThemeColours), the saved settings (MainWindowSettings), the arrangement drawing (TimelineView, TrackHeaderColumn).
// Tests: TabForge.Avalonia.Tests.ThemeSettingsBindingTests.
public sealed class ThemeSettingsBinding : IDisposable
{
    private const string LightMode = "Light";
    private const string SystemMode = "System";
    private const string CompactDensity = "Compact";
    private const double ComfortableMinHeight = 24, CompactMinHeight = 20;
    private const double ComfortablePaddingY = 3, CompactPaddingY = 1, ControlPaddingX = 5;

    private readonly Window _window;
    private readonly LayoutTransformControl _root;
    private readonly MainWindowSettings _settings;
    private readonly MainWindowDocumentPanes _panes;
    private AppearanceSettings _appearance;
    private IPlatformSettings? _osSettings;
    private bool _osThemeHooked;

    /// <summary>
    /// Raised on the UI thread after <see cref="ApplyWindow"/> changed the root's effective scale, so views that size themselves from their
    /// viewer (the score) can re-derive their width once the new layout has arranged. Subscribers unsubscribe when they leave the visual tree.
    /// </summary>
    internal static event Action? UiScaleChanged;

    public ThemeSettingsBinding(Window window, LayoutTransformControl root, MainWindowSettings settings, MainWindowDocumentPanes panes)
    {
        _window = window;
        _root = root;
        _settings = settings;
        _panes = panes;
        _appearance = settings.Settings.Appearance;
        ApplyWindow(_window, _root, _appearance);
        _window.Opened += OnWindowOpened;
        UpdateSystemListener();
        panes.ArrangementCreated += ApplyArrangement;
        if (panes.Arrangement is { } shown) ApplyArrangement(shown);
        settings.SettingsChanged += OnSettingsChanged;
    }

    /// <summary>Stops following the settings and the arrangement panes.</summary>
    public void Dispose()
    {
        _settings.SettingsChanged -= OnSettingsChanged;
        _panes.ArrangementCreated -= ApplyArrangement;
        _window.Opened -= OnWindowOpened;
        UnhookSystemTheme();
    }

    /// <summary>Sets the window's theme variant, scale, font and density from <paramref name="appearance"/>. Any thread that owns the window.</summary>
    public static void ApplyWindow(Window window, LayoutTransformControl root, AppearanceSettings appearance)
    {
        // One publish per settings change: every brush lookup and every registered resource reads this appearance until the next change.
        AvaloniaPalette.Publish(appearance);
        var variant = window.RequestedThemeVariant;
        window.RequestedThemeVariant = appearance.ThemeMode switch
        {
            _ when string.Equals(appearance.ThemeMode, LightMode, StringComparison.OrdinalIgnoreCase) => ThemeVariant.Light,
            _ when string.Equals(appearance.ThemeMode, SystemMode, StringComparison.OrdinalIgnoreCase) => ThemeVariant.Default,
            _ => ThemeVariant.Dark,
        };
        // Windows without their own variant (Preferences, FX, dialogs) inherit the application's, so it follows the theme too.
        if (Application.Current is { } app && app.RequestedThemeVariant != window.RequestedThemeVariant) app.RequestedThemeVariant = window.RequestedThemeVariant;
        // A variant change repaints the custom-drawn views even when both palette tables stayed equal (Publish then skipped its own invalidate).
        if (window.RequestedThemeVariant != variant) AvaloniaPalette.InvalidateOpenWindows();
        // A layout transform: the content is measured at the window size divided by the scale, so the scaled content fits the window.
        var previous = (root.LayoutTransform as ScaleTransform)?.ScaleX ?? UiDensity.DefaultScale;   // the scale ApplyWindow set last time
        var scale = ApplyScale(root, appearance.UiScale);
        // Only a real scale change needs the score to re-derive its page width; an unchanged scale (another setting saved) leaves the layout alone.
        if (Math.Abs(previous - scale) > 1e-9) UiScaleChanged?.Invoke();
        var fonts = TabForge.Avalonia.Platform.AvaloniaFonts.Provider;
        var fontsChanged = fonts.Configure(appearance.FontSource);
        var family = TabForge.Avalonia.Platform.AvaloniaFonts.Family(appearance.FontFamily, fonts);
        if (fontsChanged || !Equals(window.FontFamily, family)) App.SetWindowFont(family);   // the other windows follow the style; one change, not per frame
        window.FontFamily = family;
        // The custom-drawn text keys its caches by the real family, so a switch needs only one repaint of every open window.
        if (fontsChanged) AvaloniaPalette.InvalidateOpenWindows();
        window.FontSize = appearance.FontSize;
        window.Resources["ControlContentThemeFontSize"] = appearance.FontSize;
        UiDensityResources.Publish(window.Resources, appearance);   // the window's own copy, for windows hosted without the application resources
        if (Application.Current is { } current) UiDensityResources.Publish(current.Resources, appearance);
    }

    /// <summary>
    /// Sets only the root's UI scale (the default scale sets no transform) and returns the clamped scale. The one scale mechanism
    /// shared by <see cref="ApplyWindow"/> and the window hook, so a hook window never changes its theme, font or density.
    /// </summary>
    internal static double ApplyScale(LayoutTransformControl root, double uiScale)
    {
        var scale = UiDensity.ClampScale(uiScale);
        root.LayoutTransform = scale == UiDensity.DefaultScale ? null : new ScaleTransform(scale, scale);
        return scale;
    }

    private void OnSettingsChanged(AppSettings settings)
    {
        _appearance = settings.Appearance;
        ApplyWindow(_window, _root, _appearance);
        UpdateSystemListener();
        if (_panes.Arrangement is { } shown) ApplyArrangement(shown);
    }

    private void OnWindowOpened(object? sender, EventArgs e) => UpdateSystemListener();

    // System follows the OS: while the theme is System, listen to the platform colour values (some platforms change the Fluent variant
    // without raising ActualThemeVariantChanged) and republish so the custom drawing repaints. Leaving System drops the listener.
    private void UpdateSystemListener()
    {
        var platform = _window.PlatformSettings;
        var wanted = string.Equals(_appearance.ThemeMode, SystemMode, StringComparison.OrdinalIgnoreCase) && platform is not null;
        if (wanted && _osThemeHooked && ReferenceEquals(platform, _osSettings)) return;
        UnhookSystemTheme();
        if (!wanted) return;
        _osSettings = platform;
        platform!.ColorValuesChanged += OnOsColourValuesChanged;
        _osThemeHooked = true;
    }

    private void UnhookSystemTheme()
    {
        if (!_osThemeHooked) return;
        if (_osSettings is not null) _osSettings.ColorValuesChanged -= OnOsColourValuesChanged;
        _osSettings = null;
        _osThemeHooked = false;
    }

    private void OnOsColourValuesChanged(object? sender, PlatformColorValues e)
    {
        if (!string.Equals(_appearance.ThemeMode, SystemMode, StringComparison.OrdinalIgnoreCase)) return;
        // Publish rebuilds the System tables from the presets and repaints every open window; the Fluent variant follows the OS itself.
        AvaloniaPalette.Publish(_appearance, force: true);
        _window.InvalidateVisual();
    }

    private void ApplyArrangement(ArrangementView arrangement)
    {
        arrangement.Timeline.TrackTintPercent = _appearance.TrackTintPercent;
        arrangement.Timeline.MutedTrackDimPercent = _appearance.MutedTrackDimPercent;
        arrangement.Timeline.InvalidateVisual();
        arrangement.Header.InvalidateVisual();
    }
}
