using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using TabForge.Avalonia.Views;
using TabForge.Platform.Theme;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the one way a window follows the Appearance UI scale: a LayoutTransformControl around its content, the subscription to
//     ThemeSettingsBinding.UiScaleChanged with detach on close, and the outer-DIP size maths (scale, working area, default size).
// Does not own: applying the appearance to the window (ThemeSettingsBinding.ApplyWindow) or a window's own minimums (its code).
// Tests: TabForge.Avalonia.Tests.WindowUiScaleTests and WindowUiScaleHookTests.
/// <summary>Makes a window follow the UI scale setting like the main window, and converts inner DIP sizes to outer DIPs.</summary>
public static class WindowUiScale
{
    private sealed class Attached
    {
        public Attached(LayoutTransformControl root, AppearanceSettings? appearance, Action<double>? changed, bool scaleOnly)
        {
            Root = root;
            Appearance = appearance;
            Changed = changed;
            ScaleOnly = scaleOnly;
        }

        public LayoutTransformControl Root { get; }
        public AppearanceSettings? Appearance { get; }
        public Action<double>? Changed { get; }
        public bool ScaleOnly { get; }
        public Action? UiScaleHandler { get; set; }
        public EventHandler? ClosedHandler { get; set; }
        public bool Applying { get; set; }
        public bool AutoSize { get; set; }
        public bool UserSized { get; set; }
        public bool KeepOuterWidth { get; set; }
        public bool KeepOuterHeight { get; set; }
        public double InnerWidth { get; set; } = double.NaN;
        public double InnerHeight { get; set; } = double.NaN;
        public double InnerMinWidth { get; set; } = double.NaN;
        public double InnerMinHeight { get; set; } = double.NaN;
        public double InnerMaxWidth { get; set; } = double.NaN;
        public double InnerMaxHeight { get; set; } = double.NaN;
        public double LastAppliedWidth { get; set; } = double.NaN;
        public double LastAppliedHeight { get; set; } = double.NaN;
        public double LastScale { get; set; } = 1.0;
    }

    // Key: the window (weak); value: its wrapper and handlers, removed when it closes. UI thread only.
    private static readonly ConditionalWeakTable<Window, Attached> Windows = new();

    // Wrappers whose window has closed but which may still hold a view that the next window reuses; Attach releases it first.
    private static readonly ConditionalWeakTable<LayoutTransformControl, object> ClosedRoots = new();
    private static readonly object ClosedRootMarker = new();

    // True while Ensure/Rewrap change Window.Content, which raises ContentProperty.Changed again; that nested call must not wrap.
    private static bool _attaching;
    private static bool _hooked;

    /// <summary>The appearance new windows follow when no main window's settings are available; tests set its scale directly.</summary>
    internal static AppearanceSettings CurrentAppearance { get; set; } = new();

    /// <summary>
    /// Wraps every window's content when it is first set and reuses that wrapper when the window later sets a new view,
    /// so the scale is in place before Show and CenterOwner.
    /// MainWindow and MixerWindow already wrap their own content; the drag ghost sizes itself.
    /// </summary>
    public static void RegisterWindowHook()
    {
        if (_hooked) return;
        _hooked = true;
        Window.ContentProperty.Changed.AddClassHandler<Window>((window, _) => Ensure(window));
        // XAML windows can assign their content while loading; the second pass catches a content set after the property change.
        Control.LoadedEvent.AddClassHandler<Window>((window, _) => Ensure(window));
    }

    /// <summary>
    /// Wraps <paramref name="content"/> in a LayoutTransformControl, applies <paramref name="appearance"/> through
    /// <see cref="ThemeSettingsBinding.ApplyWindow"/> (no transform at scale 1.0), follows later scale changes, and detaches on close.
    /// With <paramref name="scaleOnly"/> it applies only the root's scale, leaving the window's theme, font and density, and the
    /// scale event, to the app; the global hook uses that so it never overrides a window's own appearance.
    /// Returns the control to set as <see cref="Window.Content"/>. A null appearance leaves the window at scale 1.0.
    /// With <paramref name="autoSize"/> the window's explicit sizes are scaled from the values it has now; set once only.
    /// </summary>
    public static Control Attach(Window window, Control content, AppearanceSettings? appearance = null, Action<double>? scaleChanged = null, bool autoSize = false, bool scaleOnly = false)
    {
        ReleaseFromClosedRoot(content);
        var root = new LayoutTransformControl { Child = content };
        var state = new Attached(root, appearance, scaleChanged, scaleOnly) { AutoSize = autoSize };
        if (Windows.TryGetValue(window, out var previous)) Detach(window, previous);
        Windows.Add(window, state);
        ApplyAppearance(window, state);
        state.LastScale = CurrentScale(window);
        if (autoSize)
        {
            CaptureInnerSizes(window, state);
            ApplyScaledSizes(window, state, state.LastScale, initial: true);
        }
        Action handler = () => Reapply(window, state);
        state.UiScaleHandler = handler;
        ThemeSettingsBinding.UiScaleChanged += handler;
        EventHandler closed = (_, _) => Detach(window, state, releaseContent: true);
        state.ClosedHandler = closed;
        window.Closed += closed;
        scaleChanged?.Invoke(state.LastScale);
        return root;
    }

    /// <summary>The scale being applied to the window's content; 1.0 when the window has no transform.</summary>
    public static double CurrentScale(Window window) =>
        Windows.TryGetValue(window, out var state) ? (state.Root.LayoutTransform as ScaleTransform)?.ScaleX ?? 1.0 : 1.0;

    /// <summary>True while the helper is changing this window's size; a window's size capture must ignore that change.</summary>
    internal static bool IsApplyingSize(Window window) =>
        Windows.TryGetValue(window, out var state) && state.Applying;

    /// <summary>True when <paramref name="size"/> is the size the helper applied last, not a resize by the user.</summary>
    internal static bool MatchesAppliedSize(Window window, Size size) =>
        Windows.TryGetValue(window, out var state)
        && double.IsFinite(state.LastAppliedWidth) && double.IsFinite(state.LastAppliedHeight)
        && Math.Abs(size.Width - state.LastAppliedWidth) <= 1 && Math.Abs(size.Height - state.LastAppliedHeight) <= 1;

    /// <summary>The outer-DIP size for content that is measured in inner DIPs at <paramref name="scale"/>.</summary>
    public static Size ToOuter(Size inner, double scale) => new(inner.Width * scale, inner.Height * scale);

    /// <summary>The size, never larger than the working area in DIPs of the screen the window is on, or of the primary screen.</summary>
    public static Size ClampToWorkingArea(Size outer, Window window) =>
        WorkingArea(window) is { } area ? ClampToWorkingArea(outer, area) : outer;

    /// <summary>The same clamp with plain sizes, so it can be tested without a screen.</summary>
    public static Size ClampToWorkingArea(Size outer, Size workingArea) =>
        new(Math.Min(outer.Width, workingArea.Width), Math.Min(outer.Height, workingArea.Height));

    /// <summary>
    /// A remembered size limited to the working area, then raised to the window's minimum, which wins only as far as the working
    /// area allows: a window that opens on a small screen stays reachable.
    /// </summary>
    public static Size ClampRememberedSize(Size remembered, Size minimum, Window window) =>
        WorkingArea(window) is { } area
            ? ClampRememberedSize(remembered, minimum, area)
            : new Size(Math.Max(remembered.Width, minimum.Width), Math.Max(remembered.Height, minimum.Height));

    /// <summary>The same remembered-size clamp with plain sizes, so it can be tested without a screen.</summary>
    public static Size ClampRememberedSize(Size remembered, Size minimum, Size workingArea)
    {
        var fit = ClampToWorkingArea(remembered, workingArea);
        return ClampToWorkingArea(new Size(Math.Max(fit.Width, minimum.Width), Math.Max(fit.Height, minimum.Height)), workingArea);
    }

    /// <summary>
    /// The size a window opens at without a remembered size: the preferred size at the scale, capped at 65% of the working-area
    /// width and 85% of its height, and never larger than the working area.
    /// </summary>
    public static Size DefaultSize(Window window, double scale, double preferredWidth, double preferredHeight) =>
        DefaultSize(WorkingArea(window), scale, preferredWidth, preferredHeight);

    /// <summary>The same default with an explicit working area, so it can be tested without a screen.</summary>
    public static Size DefaultSize(Size? workingArea, double scale, double preferredWidth, double preferredHeight)
    {
        var width = preferredWidth * scale;
        var height = preferredHeight * scale;
        if (workingArea is { } area)
        {
            width = Math.Min(width, area.Width * 0.65);
            height = Math.Min(height, area.Height * 0.85);
        }
        return workingArea is { } clamp ? ClampToWorkingArea(new Size(width, height), clamp) : new Size(width, height);
    }

    // The wrapper is in place and the window follows the scale from here; the content property change that asked for this must not re-enter.
    private static void Ensure(Window window)
    {
        if (_attaching) return;
        if (window is MainWindow or MixerWindow or Views.Docking.DockDragGhost) return;
        if (Windows.TryGetValue(window, out var state))
        {
            // Window code assigned a new view after the wrapper was installed: reuse the one wrapper instead of nesting another.
            if (ReferenceEquals(window.Content, state.Root)) return;
            if (window.Content is not Control replacement) return;
            Rewrap(window, state.Root, replacement);
            return;
        }
        // Another window or a test may already have wrapped the content; never wrap a LayoutTransformControl twice.
        if (window.Content is LayoutTransformControl) return;
        if (window.Content is not Control content) return;
        var appearance = AppearanceFor(window);
        _attaching = true;
        try
        {
            // The hook runs after the window has parented the new content to itself; detach it before the wrapper takes it.
            window.Content = null;
            var root = Attach(window, content, appearance, autoSize: true, scaleOnly: true);
            window.Content = root;
        }
        finally { _attaching = false; }
    }

    // Replaces the existing wrapper's child with the window's new content, detaching that content from the window first.
    private static void Rewrap(Window window, LayoutTransformControl root, Control content)
    {
        ReleaseFromClosedRoot(content);
        _attaching = true;
        try
        {
            window.Content = null;
            root.Child = content;
            window.Content = root;
        }
        finally { _attaching = false; }
    }

    private static AppearanceSettings AppearanceFor(Window window)
    {
        if (window is PreferencesWindow preferences) return preferences.Saved.Appearance;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is MainWindow main && main.Settings is { } settings)
            return settings.Settings.Appearance;
        return CurrentAppearance;
    }

    // The inner sizes are read once: every later scale change recomputes from them, so repeated changes never compound.
    private static void CaptureInnerSizes(Window window, Attached state)
    {
        state.InnerWidth = window.Width;
        state.InnerHeight = window.Height;
        state.InnerMinWidth = window.MinWidth;
        state.InnerMinHeight = window.MinHeight;
        state.InnerMaxWidth = window.MaxWidth;
        state.InnerMaxHeight = window.MaxHeight;
        // The Settings window keeps its remembered outer size, so a scale change must not multiply it again.
        if (window is PreferencesWindow preferences)
        {
            state.KeepOuterWidth = IsUsableSettingsSize(preferences.Saved.SettingsWindowWidth, ChromeMetrics.SettingsWindowMinWidth);
            state.KeepOuterHeight = IsUsableSettingsSize(preferences.Saved.SettingsWindowHeight, ChromeMetrics.SettingsWindowMinHeight);
        }
    }

    private static bool IsUsableSettingsSize(double value, double minimum) => double.IsFinite(value) && value >= minimum;

    private static void ApplyScaledSizes(Window window, Attached state, double scale, bool initial)
    {
        // A size that stopped matching the helper's last applied one while the window was open came from the user: keep it.
        // A window that was never shown, or has no laid-out bounds yet, has no user size: it always follows the inner base.
        var laidOut = window.IsVisible && window.Bounds.Width > 0 && window.Bounds.Height > 0;
        if (!initial && !state.UserSized && laidOut
            && ((double.IsFinite(state.LastAppliedWidth) && Math.Abs(window.Bounds.Width - state.LastAppliedWidth) > 1)
                || (double.IsFinite(state.LastAppliedHeight) && Math.Abs(window.Bounds.Height - state.LastAppliedHeight) > 1)))
            state.UserSized = true;

        state.Applying = true;
        try
        {
            SetScaledLimit(window, state.InnerMinWidth, scale, value => window.MinWidth = value);
            SetScaledLimit(window, state.InnerMinHeight, scale, value => window.MinHeight = value);
            if (!state.UserSized)
            {
                SetScaledLimit(window, state.InnerMaxWidth, scale, value => window.MaxWidth = value);
                SetScaledLimit(window, state.InnerMaxHeight, scale, value => window.MaxHeight = value);
                if (state.KeepOuterWidth)
                    SetRememberedOuter(window, state.InnerWidth, state.InnerMinWidth, scale, value => window.Width = value);
                else if (!IsAutomaticWidth(window))
                    SetScaledSize(window, state.InnerWidth, state.InnerMinWidth, scale, value => window.Width = value);
                if (state.KeepOuterHeight)
                    SetRememberedOuter(window, state.InnerHeight, state.InnerMinHeight, scale, value => window.Height = value);
                else if (!IsAutomaticHeight(window))
                    SetScaledSize(window, state.InnerHeight, state.InnerMinHeight, scale, value => window.Height = value);
            }
        }
        finally { state.Applying = false; }

        state.LastAppliedWidth = window.Width;
        state.LastAppliedHeight = window.Height;
    }

    // Min and max sizes follow the scale even on an axis whose size is automatic or kept at the remembered outer value.
    private static void SetScaledLimit(Window window, double inner, double scale, Action<double> set)
    {
        if (!double.IsFinite(inner)) return;
        set(ClampToWorkingArea(new Size(inner * scale, inner * scale), window).Width);
    }

    // The explicit size follows the scale, raised to the scaled minimum so the layout does not coerce it to a different value.
    private static void SetScaledSize(Window window, double inner, double innerMinimum, double scale, Action<double> set)
    {
        if (!double.IsFinite(inner)) return;
        var minimum = double.IsFinite(innerMinimum) ? innerMinimum * scale : 0;
        set(ClampRememberedSize(new Size(inner * scale, inner * scale), new Size(minimum, minimum), window).Width);
    }

    // The Settings window's remembered size is already outer: keep it, raised only to the scaled minimum and the working area.
    private static void SetRememberedOuter(Window window, double outer, double innerMinimum, double scale, Action<double> set)
    {
        if (!double.IsFinite(outer)) return;
        var minimum = double.IsFinite(innerMinimum) ? innerMinimum * scale : 0;
        set(ClampRememberedSize(new Size(outer, outer), new Size(minimum, minimum), window).Width);
    }

    private static bool IsAutomaticWidth(Window window) =>
        window.SizeToContent is SizeToContent.Width or SizeToContent.WidthAndHeight;

    private static bool IsAutomaticHeight(Window window) =>
        window.SizeToContent is SizeToContent.Height or SizeToContent.WidthAndHeight;

    // Re-applies the same appearance, which the settings object updates in place, so ThemeSettingsBinding.ApplyScale remains the only scale mechanism.
    private static void Reapply(Window window, Attached state)
    {
        if (state.Appearance is null || state.Applying) return;
        state.Applying = true;
        try { ApplyAppearance(window, state); }
        finally { state.Applying = false; }
        var scale = CurrentScale(window);
        if (Math.Abs(scale - state.LastScale) < 1e-9) return;
        state.LastScale = scale;
        if (state.AutoSize) ApplyScaledSizes(window, state, scale, initial: false);
        state.Changed?.Invoke(scale);
    }

    // Hook windows change only the scale; a window that owns its appearance (the Mixer) also applies theme, font and density.
    private static void ApplyAppearance(Window window, Attached state)
    {
        if (state.Appearance is null) return;
        if (state.ScaleOnly) ThemeSettingsBinding.ApplyScale(state.Root, state.Appearance.UiScale);
        else ThemeSettingsBinding.ApplyWindow(window, state.Root, state.Appearance);
    }

    // A closed window's wrapper is no longer the view's owner: detach a view that another window is about to reuse.
    private static void ReleaseFromClosedRoot(Control content)
    {
        if (content.Parent is not LayoutTransformControl root || !ClosedRoots.TryGetValue(root, out _)) return;
        if (ReferenceEquals(root.Child, content)) root.Child = null;
        ClosedRoots.Remove(root);
    }

    private static void Detach(Window window, Attached state, bool releaseContent = false)
    {
        if (state.UiScaleHandler is { } handler) ThemeSettingsBinding.UiScaleChanged -= handler;
        if (state.ClosedHandler is { } closed) window.Closed -= closed;
        if (releaseContent)
        {
            // Avalonia releases a closed window's content, but our wrapper would keep the view parented: release it for reuse.
            if (ReferenceEquals(window.Content, state.Root)) window.Content = null;
            state.Root.Child = null;
            ClosedRoots.Remove(state.Root);
            ClosedRoots.Add(state.Root, ClosedRootMarker);
        }
        Windows.Remove(window);
    }

    /// <summary>The owner screen working area in DIPs, or null when the window has no screen yet.</summary>
    public static Size? WorkingArea(Window window)
    {
        var screens = window.Screens;
        var screen = screens?.ScreenFromWindow(window) ?? screens?.Primary;
        if (screen is null) return null;
        var scaling = screen.Scaling > 0 ? screen.Scaling : window.RenderScaling;
        if (!(scaling > 0)) scaling = 1.0;
        return new Size(screen.WorkingArea.Width / scaling, screen.WorkingArea.Height / scaling);
    }
}
