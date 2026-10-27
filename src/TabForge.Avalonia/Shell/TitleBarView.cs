using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Documents;
using TabForge.Services;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Services.Features;
using TabForge.Shell;
using Buttons = TabForge.Avalonia.Shell.CaptionButtons;

namespace TabForge.Avalonia.Shell;

/// <summary>
/// The 44 px title row: the document tab strip on the left, then Record video, Monitor FX, Settings, Minimise, Maximise and Close on the right. A press on
/// the empty caption area starts the window move; a double-click toggles maximise. It answers the native hit test (ICaptionButtonRects) from
/// its live layout, shows the native hover and press on the caption buttons, and opens a new tab on a native middle-click of the empty caption.
/// Owns: the row layout, the caption pointer rules and the rectangles. Does not own: the tabs (TabStripView), the buttons (CaptionButtons), the window
/// actions (IWindowChrome) or the hit rules and the window procedure (Core ChromeHitRules, the Win32 hook).
/// Tests: TabForge.Avalonia.Tests.TitleBarTests, TitleBarRectsTests.
/// </summary>
public sealed class TitleBarView : Border, ICaptionButtonRects
{
    /// <summary>The command the Record video button runs: the live recording toggle (the export window is File > Export video).</summary>
    public const string RecordCommandId = FeatureHotkeyIds.RecordVideo;
    private const string SettingsCommandId = "App.Preferences";

    // The buttons Windows answers natively (HTMINBUTTON, HTMAXBUTTON, HTCLOSE).
    private static readonly IReadOnlyList<(string Name, ChromeHit Kind)> NativeButtons = new (string Name, ChromeHit Kind)[]
        { ("Minimise", ChromeHit.MinButton), ("Maximise", ChromeHit.MaxButton), ("Close", ChromeHit.CloseButton) };

    private readonly StackPanel _buttons;
    private readonly TabStripView _tabs;
    private ChromeHit? _hotKind, _pressedKind;
    // The button the native hook last pressed: its Avalonia Click is skipped because the hook runs the action. A real pointer press clears it.
    private string? _nativePressed;
    private readonly ChromeOs _os;
    private readonly IWindowChrome _chrome;
    private readonly Action? _nativeMiddleClick;
    private readonly Func<bool>? _middleClickEnabled;

    public TitleBarView(TabStripView tabs, IWindowChrome chrome, Action recordVideo, Action openSettings, ChromeOs os = ChromeOs.Windows,
        Action? nativeMiddleClick = null, Func<bool>? middleClickEnabled = null, MonitorFxBinding? monitor = null)
    {
        _middleClickEnabled = middleClickEnabled;
        _os = os;
        _chrome = chrome;
        _nativeMiddleClick = nativeMiddleClick;
        Name = "TitleBar";
        Height = ChromeMetrics.CaptionRowHeight;
        BorderThickness = new Thickness(0, 0, 0, 1);
        this[!BackgroundProperty] = new DynamicResourceExtension("ChromeStrip");
        this[!BorderBrushProperty] = new DynamicResourceExtension("ChromeBorder");
        _tabs = tabs;
        // The buttons are docked first so a wide tab strip cannot push them off the right edge.
        _buttons = Buttons.Build(chrome, recordVideo, openSettings, name => name == _nativePressed, monitor);
        DockPanel.SetDock(_buttons, Dock.Right);
        if (os == ChromeOs.MacOs) UseMacTrafficLights(chrome);
        Child = new DockPanel { LastChildFill = true, Children = { _buttons, tabs } };
        AddHandler(PointerPressedEvent, (_, _) => _nativePressed = null, RoutingStrategies.Tunnel);
        PointerPressed += (_, e) => OnCaptionPressed(e, chrome);
    }

    // macOS keeps the system traffic lights: our Minimise, Maximise and Close hide and the tabs start after the lights (none in full screen).
    private void UseMacTrafficLights(IWindowChrome chrome)
    {
        foreach (var (name, _) in NativeButtons) Named(name).IsVisible = false;
        void Inset() => _tabs.Margin = new Thickness(chrome.State == WindowChromeState.FullScreen ? 0 : ChromeHitRules.LeadingInset(ChromeOs.MacOs), 0, 0, 0);
        chrome.StateChanged += (_, _) => Inset();
        Inset();
    }

    /// <summary>The window content for this OS: on Linux the resize grips and a 1 px border over it; elsewhere the content itself.</summary>
    public Control Frame(Control content) => _os == ChromeOs.Linux ? LinuxChromeAdapter.Frame(content, _chrome) : content;

    public IReadOnlyList<(UiRect Bounds, ChromeHit Kind)> CaptionButtons =>
        NativeButtons.Select(b => (RectOf(Named(b.Name)), b.Kind)).ToList();

    // Record, Monitor FX and Settings are client area: the native hit test leaves them to Avalonia, so they count as tab actions with the tab crosses and New tab.
    public IReadOnlyList<UiRect> TabActionBounds => _tabs.GetVisualDescendants().OfType<Control>()
        .Where(c => c.Name is "TabClose" or "NewTab" or "TabScrollLeft" or "TabScrollRight" && c.IsVisible).Select(RectOf)
        .Concat(new[] { "Record", MonitorFxCaptionButton.ButtonName, "Settings" }.Select(n => RectOf(Named(n)))).ToList();

    // A tab scrolled out of the strip's tab area is clipped away, so it never claims the empty caption or the arrows.
    public IReadOnlyList<UiRect> TabBounds
    {
        get
        {
            var view = RectOf(_tabs.TabViewport);
            return _tabs.Tabs.Select(RectOf).Select(r =>
            {
                var left = Math.Max(r.X, view.X);
                return new UiRect(left, r.Y, Math.Min(r.Right, view.Right) - left, r.Height);
            }).Where(r => r.Width > 0).ToList();
        }
    }

    public UiRect CaptionBounds => RectOf(this);

    /// <summary>Shows the red recording dot on the Record video button while a recording runs.</summary>
    public void ShowRecording(bool recording) => Buttons.ShowRecordingDot(Named("Record"), recording);

    public void SetHot(ChromeHit? button)
    {
        _hotKind = button;
        ShowNativeState();
    }

    public void SetPressed(ChromeHit? button)
    {
        _pressedKind = button;
        if (button is { } kind) _nativePressed = NativeButtons.First(b => b.Kind == kind).Name;
        ShowNativeState();
    }

    /// <summary>
    /// A native middle-click released on the empty caption (Windows sends WM_NCMBUTTONDOWN because the hit test answers HTCAPTION, so
    /// Avalonia sees no pointer event): opens a new tab through the Tabs middle-click setting. Tabs, tab actions and caption buttons
    /// never reach here, and on the other platforms (no native hook) the Avalonia press handler is the only path.
    /// </summary>
    public bool MiddleClickEnabled => _middleClickEnabled?.Invoke() ?? true;

    public void NativeMiddleClick()
    {
        if (_os != ChromeOs.Windows) return;
        _nativeMiddleClick?.Invoke();
    }

    private void ShowNativeState()
    {
        foreach (var (name, kind) in NativeButtons) Buttons.ShowState(Named(name), _hotKind == kind || _pressedKind == kind, _pressedKind == kind);
    }

    private Button Named(string name) => _buttons.Children.OfType<Button>().Single(b => b.Name == name);

    // A control's bounds in the window's client coordinates (DIPs): both corners are transformed, so a UI-scale LayoutTransform between the control
    // and the window scales the size too (Bounds is unscaled, so its width and height alone would be wrong at any scale but 1).
    private static UiRect RectOf(Control control)
    {
        var target = (Visual?)TopLevel.GetTopLevel(control) ?? control;
        var origin = control.TranslatePoint(new Point(0, 0), target) ?? default;
        var corner = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), target) ?? default;
        return new UiRect(origin.X, origin.Y, corner.X - origin.X, corner.Y - origin.Y);
    }

    // A press on a button or a tab belongs to it; anything else in the row is caption.
    private static void OnCaptionPressed(PointerPressedEventArgs e, IWindowChrome chrome)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed || e.Source is not Visual source) return;
        if (source.GetSelfAndVisualAncestors().Any(v => v is Button || v.GetVisualParent() is StackPanel { Name: "Tabs" })) return;
        if (chrome is AvaloniaWindowChrome adapter) adapter.NotePress(e);
        if (e.ClickCount == 2) chrome.ToggleMaximise();
        else chrome.BeginMove();
    }

    // The Monitor FX button of the main window: it reads the shown song's monitor chain and follows the settings, the shown song and that song's display state.
    private static MonitorFxBinding MonitorBinding(MainWindow main)
    {
        FxChainWindows? Chains() => FxChainWindows.For(main);
        var binding = new MonitorFxBinding(() => Chains()?.MonitorState() ?? default, () => Chains()?.OpenMonitor(), on => Chains()?.SetMonitorOn(on), global => Chains()?.SetMonitorUseGlobal(global));
        SongDocument? watched = null;
        EventHandler song = (_, _) => binding.RaiseChanged();
        void Watch()
        {
            if (watched is not null) watched.DisplayStateChanged -= song;
            watched = main.Songs.Active;
            watched.DisplayStateChanged += song;
            binding.RaiseChanged();
        }
        Action<AppSettings> settings = _ => binding.RaiseChanged();
        main.Songs.ActiveChanged += Watch;
        if (main.Settings is { } shared) shared.SettingsChanged += settings;
        main.Closed += (_, _) =>
        {
            main.Songs.ActiveChanged -= Watch;
            if (main.Settings is { } s) s.SettingsChanged -= settings;
            if (watched is not null) watched.DisplayStateChanged -= song;
        };
        Watch();
        return binding;
    }

    /// <summary>The title row of a preview window: makes the row the caption (extended client area, no system chrome) and wires the buttons.</summary>
    public static TitleBarView For(Window window, TabStripView tabs, MainWindowCommands commands)
    {
        window.ExtendClientAreaToDecorationsHint = true;
        if (WindowChromeFactory.CurrentOs != ChromeOs.MacOs) window.ExtendClientAreaChromeHints = global::Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
        window.ExtendClientAreaTitleBarHeightHint = ChromeMetrics.CaptionRowHeight;
        // Maximised, the system draws the window a few pixels past the screen; the content moves in by that margin so nothing is clipped
        // while the title row and its buttons still reach the screen edge.
        void KeepInsideScreen() => window.Padding = window.WindowState is WindowState.Maximized or WindowState.FullScreen ? window.OffScreenMargin : default;
        window.PropertyChanged += (_, e) => { if (e.Property == Window.WindowStateProperty || e.Property == Window.OffScreenMarginProperty) KeepInsideScreen(); };
        KeepInsideScreen();
        // The empty caption's native middle-click opens a tab in the main window; a preview window has no songs, so it has no action.
        Action? nativeMiddleClick = window is MainWindow main
            ? () => TabBehaviourSettings.OnTitleBarMiddleClicked(main.Settings, main.Songs)
            : null;
        var bar = new TitleBarView(tabs, WindowChromeFactory.Create(window), () => commands.Run(RecordCommandId), () => commands.Run(SettingsCommandId), WindowChromeFactory.CurrentOs, nativeMiddleClick,
            window is MainWindow m ? () => TabBehaviourSettings.TitleBarMiddleClickOpensTab(m.Settings) : null, window is MainWindow mw ? MonitorBinding(mw) : null);
        // Full screen shows the content only: the caption row (tabs and caption buttons) hides, and comes back with the window state.
        // The system caption area goes with it (height hint 0) so no drag or caption-button zone stays over the menu bar; exit restores the hint.
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property != Window.WindowStateProperty) return;
            var fullScreen = window.WindowState == WindowState.FullScreen;
            bar.IsVisible = !fullScreen;
            window.ExtendClientAreaTitleBarHeightHint = fullScreen ? 0 : ChromeMetrics.CaptionRowHeight;
        };
        return bar;
    }
}
