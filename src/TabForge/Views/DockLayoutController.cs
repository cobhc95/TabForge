using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TabForge.Docking;
using TabForge.Services;
using TabForge.Views;

namespace TabForge.Views;

/// <summary>What the dock layout controller needs from its window.</summary>
internal interface IDockLayoutHost : IPaneHost
{
    DockWorkspace? Dock { get; }
    MenuItem DockPanelsMenu { get; }
    InstrumentPanel Instrument { get; }
    Border InstrumentHost { get; }
}

// Owns: saved and built-in workspace layouts (the Layouts menu), the Panels menu, the side-panel hide/show toggle,
//   the fretboard pane's height limits and size lock, and full screen.
// Does not own: the dock workspace itself (DockWorkspace), panel registration, layout persistence on change.
// Tests: TestFretboardPaneSize, TestDockRatioNotRewrittenByAutoFit, TestViewMenuWording.
internal sealed class DockLayoutController
{
    public static readonly string[] BuiltInLayoutNames = { "Compose", "Practice", "Mix" };
    private static readonly string[] AllDockPanelIds =
        { "tools", "structure", "rhythm", "layout", "sections", "practice", "playback", "instrument", "timeline" };
    /// <summary>The panels on the side (tools, structure, rhythm, layout, sections, practice, metronome).</summary>
    private static readonly string[] SidePanelIds = { "tools", "structure", "rhythm", "layout", "sections", "practice", "playback" };
    private const int MaxSavedLayouts = 24;

    private readonly IDockLayoutHost _host;
    private List<string>? _hiddenSidePanels;
    // Saved bounds/state so exiting fullscreen restores the previous layout exactly (and keeps the
    // custom WindowChrome - setting SingleBorderWindow here used to add a native title bar).
    private Rect _preFullscreenBounds;
    private WindowState _preFullscreenState = WindowState.Normal;
    private bool _fullscreen;

    public DockLayoutController(IDockLayoutHost host) => _host = host;

    private AppSettings Settings => _host.Settings;

    /// <summary>The Layouts submenu (null until <see cref="BuildLayoutsMenu"/>).</summary>
    public MenuItem? LayoutsMenu { get; set; }

    // ---------- saved workspace layouts ----------

    private static DockWorkspaceState BuiltInLayout(string name)
    {
        static DockNodeState Tools() => DockWorkspace.Tabs("default-tool-palette", "tools", "structure", "rhythm", "layout");
        static DockNodeState Side(string selected, params string[] ids)
        {
            var host = DockWorkspace.Tabs("default-sections-practice-playback", ids);
            host.SelectedPanel = selected;
            return host;
        }
        var score = DockWorkspace.EditorNode();
        var instrument = DockWorkspace.Tabs("default-instrument", "instrument");
        var timeline = DockWorkspace.Tabs("default-timeline", "timeline");
        var root = name switch
        {
            // Score + tab editor large; fretboard/keyboard and tools beside it; arrangement small.
            "Compose" => DockWorkspace.Split("Vertical", 0.86,
                DockWorkspace.Split("Horizontal", 0.78, DockWorkspace.Split("Vertical", 0.68, score, instrument), Tools()),
                timeline),
            // Score/tab + fretboard large; transport and practice panels visible; arrangement and mixer window closed.
            "Practice" => DockWorkspace.Split("Horizontal", 0.78,
                DockWorkspace.Split("Vertical", 0.60, score, instrument),
                Side("playback", "playback", "sections", "practice")),
            // Arrangement + mixer large; score small.
            _ => DockWorkspace.Split("Vertical", 0.30, score,
                DockWorkspace.Split("Horizontal", 0.74, timeline, Side("practice", "practice", "sections", "playback")))
        };
        var state = new DockWorkspaceState { Root = root };
        var present = DockWorkspace.PanelsOf(state).ToHashSet(StringComparer.Ordinal);
        state.ClosedPanels = AllDockPanelIds.Where(id => !present.Contains(id)).ToList();
        return state;
    }

    private SavedLayout? FindSavedLayout(string name) =>
        Settings.SavedLayouts.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

    public void BuildLayoutsMenu()
    {
        if (_host.DockPanelsMenu.Parent is not ItemsControl parent) return;
        if (LayoutsMenu is null)
        {
            LayoutsMenu = new MenuItem { Header = "Layouts" };
            parent.Items.Insert(Math.Max(0, parent.Items.IndexOf(_host.DockPanelsMenu)), LayoutsMenu);
            LayoutsMenu.SubmenuOpened += (_, _) => RefreshLayoutsMenu();
        }
        RefreshLayoutsMenu();
    }

    public void RefreshLayoutsMenu()
    {
        if (LayoutsMenu is null) return;
        LayoutsMenu.Items.Clear();
        string GestureText(string id) => HotkeyCatalog.DisplayAll(Settings.Hotkeys, id);
        MenuItem Entry(string name, string? hotkeyId)
        {
            var item = new MenuItem
            {
                Header = name, IsCheckable = true,
                IsChecked = string.Equals(Settings.LastLayout, name, StringComparison.OrdinalIgnoreCase),
                InputGestureText = hotkeyId is null ? "" : GestureText(hotkeyId)
            };
            item.Click += (_, _) => SwitchLayout(name);
            return item;
        }
        LayoutsMenu.Items.Add(Entry("Compose", "View.LayoutCompose"));
        LayoutsMenu.Items.Add(Entry("Practice", "View.LayoutPractice"));
        LayoutsMenu.Items.Add(Entry("Mix", "View.LayoutMix"));
        var custom = Settings.SavedLayouts.Where(l => !BuiltInLayoutNames.Contains(l.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        if (custom.Count > 0) LayoutsMenu.Items.Add(new Separator());
        foreach (var l in custom) LayoutsMenu.Items.Add(Entry(l.Name, null));
        LayoutsMenu.Items.Add(new Separator());
        var save = new MenuItem { Header = "Save current layout as…" };
        save.Click += (_, _) => SaveCurrentLayoutAs();
        LayoutsMenu.Items.Add(save);
        var delete = new MenuItem { Header = "Delete layout", IsEnabled = Settings.SavedLayouts.Count > 0 };
        foreach (var l in Settings.SavedLayouts.ToList())
        {
            var name = l.Name;
            var d = new MenuItem { Header = BuiltInLayoutNames.Contains(name, StringComparer.OrdinalIgnoreCase) ? name + " (back to built-in)" : name };
            d.Click += (_, _) =>
            {
                Settings.SavedLayouts.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (string.Equals(Settings.LastLayout, name, StringComparison.OrdinalIgnoreCase) && !BuiltInLayoutNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    Settings.LastLayout = null;
                _host.SaveSettings();
                _host.SetStatus($"Layout \"{name}\" deleted");
            };
            delete.Items.Add(d);
        }
        LayoutsMenu.Items.Add(delete);
        var reset = new MenuItem { Header = "Reset built-in layouts" };
        reset.Click += (_, _) =>
        {
            Settings.SavedLayouts.RemoveAll(x => BuiltInLayoutNames.Contains(x.Name, StringComparer.OrdinalIgnoreCase));
            _host.SaveSettings();
            _host.SetStatus("Built-in layouts reset");
        };
        LayoutsMenu.Items.Add(reset);
    }

    /// <summary>Instantly applies a layout (panels, dock sizes, window state); documents are not touched.</summary>
    public void SwitchLayout(string name)
    {
        if (_host.Dock is not { } dock) return;
        var saved = FindSavedLayout(name);
        DockWorkspaceState? state = saved?.State;
        if (state?.Root is null)
        {
            if (!BuiltInLayoutNames.Contains(name, StringComparer.OrdinalIgnoreCase)) { _host.SetStatus($"Layout \"{name}\" not found"); return; }
            state = BuiltInLayout(BuiltInLayoutNames.First(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)));
            saved = null;
        }
        Settings.LastLayout = saved?.Name ?? BuiltInLayoutNames.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) ?? name;
        dock.ApplyLayout(state);
        if (saved is not null && saved.WindowWidth > 0)
        {
            var w = _host.Window;
            if (saved.Maximised) w.WindowState = WindowState.Maximized;
            else
            {
                w.WindowState = WindowState.Normal;
                w.Width = Math.Max(w.MinWidth, saved.WindowWidth);
                w.Height = Math.Max(w.MinHeight, saved.WindowHeight);
            }
        }
        _host.SaveSettings();
        _host.SetStatus($"Layout: {Settings.LastLayout}");
    }

    private void SaveCurrentLayoutAs()
    {
        if (_host.Dock is not { } dock) return;
        var name = GpDialogs.Prompt("Save layout", "Layout name:", Settings.LastLayout ?? "My layout")?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        if (name.Length > 40) name = name[..40];
        var existing = FindSavedLayout(name);
        if (existing is null && Settings.SavedLayouts.Count >= MaxSavedLayouts) { _host.SetStatus("Too many saved layouts; delete one first"); return; }
        if (existing is null) Settings.SavedLayouts.Add(existing = new SavedLayout());
        var w = _host.Window;
        existing.Name = BuiltInLayoutNames.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) ?? name;
        existing.State = dock.CaptureLayout();
        existing.Maximised = w.WindowState == WindowState.Maximized;
        existing.WindowWidth = w.WindowState == WindowState.Maximized ? w.RestoreBounds.Width : w.Width;
        existing.WindowHeight = w.WindowState == WindowState.Maximized ? w.RestoreBounds.Height : w.Height;
        Settings.LastLayout = existing.Name;
        _host.SaveSettings();
        _host.SetStatus($"Layout \"{existing.Name}\" saved");
    }

    public void BuildDockPanelsMenu()
    {
        var menu = _host.DockPanelsMenu;
        menu.Items.Clear();
        foreach (var (id, title) in new[]
        {
            ("tools", "Tools"), ("structure", "Structure"), ("rhythm", "Rhythm"), ("layout", "Layout"),
            ("sections", "Sections"), ("practice", "Practice / Mixer"), ("playback", "Zoom & speed"),
            ("instrument", "Fretboard"), ("timeline", "Arrangement")
        })
        {
            var item = new MenuItem { Header = title, IsCheckable = true, IsChecked = true, Tag = id };
            item.Click += (_, _) =>
            {
                if (item.Tag is string panelId) _host.Dock?.SetPanelVisible(panelId, item.IsChecked);
                RefreshDockPanelsMenu();
            };
            menu.Items.Add(item);
        }
        RefreshDockPanelsMenu();
    }

    public void RefreshDockPanelsMenu()
    {
        if (_host.Dock is not { } dock) return;
        foreach (var item in _host.DockPanelsMenu.Items.OfType<MenuItem>())
            if (item.Tag is string id) item.IsChecked = dock.IsPanelVisible(id);
    }

    // ---------- side panel ----------

    /// <summary>The whole layout as it was when the side panel was hidden, brought back exactly on show.</summary>
    private DockWorkspaceState? LayoutBeforeSideHide
    {
        get => Settings.WorkspaceBeforeSideHide;
        set => Settings.WorkspaceBeforeSideHide = value;
    }

    /// <summary>Side panel button / hotkey: hides every side panel, or brings back exactly the ones that were shown.</summary>
    public void ToggleSidePanel()
    {
        if (_host.Dock is not { } dock) return;
        var shown = SidePanelIds.Where(dock.IsPanelVisible).ToList();
        if (shown.Count > 0)
        {
            _hiddenSidePanels = shown;
            LayoutBeforeSideHide = dock.CaptureLayout();   // placement, tab order and sizes
            dock.HidePanels(shown);                          // one rebuild, not one per panel
            _host.SaveSettings();
            _host.SetStatus("Side panel hidden");
        }
        else if (LayoutBeforeSideHide is { } saved && OnlySidePanelsDiffer(dock, saved))
        {
            dock.ApplyLayout(saved);
            LayoutBeforeSideHide = null; _hiddenSidePanels = null;
            _host.SaveSettings();
            _host.SetStatus("Side panel shown");
        }
        else
        {
            // Other panels were opened or closed meanwhile: bring back the side panels to their places.
            var restore = _hiddenSidePanels is { Count: > 0 } list ? list : SidePanelIds.ToList();
            foreach (var id in restore) dock.SetPanelVisible(id, true);
            LayoutBeforeSideHide = null; _hiddenSidePanels = null;
            _host.SetStatus("Side panel shown");
        }
    }

    /// <summary>True when every panel outside the side panel is where it was (so the saved layout can be restored whole).</summary>
    private static bool OnlySidePanelsDiffer(DockWorkspace dock, DockWorkspaceState saved)
    {
        var visibleNow = dock.CaptureLayout();
        static HashSet<string> Others(DockWorkspaceState state, IEnumerable<string> side) =>
            DockWorkspace.PanelsOf(state).Where(p => !side.Contains(p)).ToHashSet();
        return Others(saved, SidePanelIds).SetEquals(Others(visibleNow, SidePanelIds));
    }

    // ---------- fretboard pane size ----------

    private double HostBorder => _host.InstrumentHost.BorderThickness.Top + _host.InstrumentHost.BorderThickness.Bottom;

    public void ApplyInstrumentMinHeight()
    {
        _host.Dock?.SetPanelContentMinHeight("instrument", _host.Instrument.RequiredHeight + HostBorder);
        ApplyInstrumentMaxHeight();
    }

    /// <summary>The fretboard pane cannot grow past its maximum stretch (no empty space above and below the board).</summary>
    public void ApplyInstrumentMaxHeight() =>
        _host.Dock?.SetPanelContentMaxHeight("instrument", double.IsPositiveInfinity(_host.Instrument.MaximumHeight) ? null
            : _host.Instrument.MaximumHeight + HostBorder);

    /// <summary>
    /// Applies Appearance.LockInstrumentSize: locked = the pane keeps its saved height (splitter not draggable);
    /// unlocked = resizable, the drawing scales with the pane. With no saved height yet, the current height
    /// is taken once the window has laid out.
    /// </summary>
    public void ApplyInstrumentSizeLock()
    {
        if (_host.Dock is not { } dock) return;
        var appearance = Settings.Appearance;
        if (!appearance.LockInstrumentSize) { dock.SetPanelFixedHeight("instrument", null); return; }
        if (appearance.InstrumentPaneHeight > 0)
        {
            dock.SetPanelFixedHeight("instrument", Math.Max(appearance.InstrumentPaneHeight, _host.Instrument.RequiredHeight + HostBorder));
            return;
        }
        _host.Window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (!Settings.Appearance.LockInstrumentSize || Settings.Appearance.InstrumentPaneHeight > 0) return;
            // Fresh profile: the "medium" size computed from the size model (about 1.1x natural), not the dock's split ratio.
            Settings.Appearance.InstrumentPaneHeight = Math.Ceiling(_host.Instrument.MediumHeight() + HostBorder);
            ApplyInstrumentSizeLock();
            _host.SaveSettings();
        });
    }

    /// <summary>Instrument right-click menu "Lock fretboard size" and the View.LockInstrumentSize hotkey.</summary>
    public void ToggleInstrumentSizeLock()
    {
        var appearance = Settings.Appearance;
        appearance.LockInstrumentSize = !appearance.LockInstrumentSize;
        // Locking keeps the size the pane has now (the size the user chose while unlocked).
        if (appearance.LockInstrumentSize && _host.InstrumentHost.ActualHeight > 0)
            appearance.InstrumentPaneHeight = Math.Ceiling(_host.InstrumentHost.ActualHeight);
        ApplyInstrumentSizeLock();
        _host.SaveSettings();
        _host.SetStatus(appearance.LockInstrumentSize
            ? "Fretboard size locked"
            : "Fretboard size unlocked: drag the pane's edge to resize (the drawing scales)");
    }

    // ---------- full screen ----------

    public void ToggleFullscreen()
    {
        var w = _host.Window;
        _fullscreen = !_fullscreen;
        if (_fullscreen)
        {
            _preFullscreenState = w.WindowState;
            _preFullscreenBounds = w.RestoreBounds;
            w.WindowState = WindowState.Maximized;
            _host.SetStatus(TooltipShortcuts.Append("Full screen", "View.Fullscreen"));
        }
        else
        {
            w.WindowState = WindowState.Normal;
            if (_preFullscreenBounds.Width > 1 && _preFullscreenBounds.Height > 1)
            {
                w.Left = _preFullscreenBounds.Left;
                w.Top = _preFullscreenBounds.Top;
                w.Width = _preFullscreenBounds.Width;
                w.Height = _preFullscreenBounds.Height;
            }
            if (_preFullscreenState == WindowState.Maximized) w.WindowState = WindowState.Maximized;
            _host.SetStatus("Full screen off");
        }
    }
}
