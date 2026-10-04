using TabForge.Controllers;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Views.Score;

namespace TabForge;

public static partial class SelfTest
{
    /// <summary>
    /// Score note menu, score empty-area menu and fretboard menu (docs/CONTEXT_MENU_AUDIT.md 5-7): the top-level layouts, the
    /// paste items only with a clip, and a right-click inside the selection is recognised (so the selection is kept).
    /// </summary>
    private static void TestContextMenuLayouts()
    {
        var note = Views.ContextMenuLayouts.NoteMenu(canPaste: true);
        Check("note menu: Copy, Cut, Paste, Paste special, Delete, then the five submenus",
            note.SequenceEqual(new[] { "Copy", "Cut", "Paste", "Paste special…", "Delete", "-", "Duration", "Dynamics", "Effects", "Beat", "Pitch and string" }),
            string.Join(" | ", note));
        Eq("note menu has 10 top-level entries", 10, Views.ContextMenuLayouts.TopLevelCount(note));
        var noteEmpty = Views.ContextMenuLayouts.NoteMenu(canPaste: false);
        Check("note menu hides both paste items without a clip (hidden, not greyed)",
            !noteEmpty.Contains("Paste") && !noteEmpty.Contains("Paste special…") && Views.ContextMenuLayouts.TopLevelCount(noteEmpty) == 8);

        string[] Top(IEnumerable<Views.MenuSpec> m) => m.Where(x => !x.IsSeparator).Select(x => x.Header).ToArray();
        Func<string, string> noKeys = _ => "";
        var overBeat = Top(Views.ScoreMenus.Empty(new Views.ScoreEmptyState(true, true, true, true, false), noKeys));
        Check("score empty menu over a beat with a clip: Paste, Paste special, notation, Zoom, Page layout, Score settings (6 entries)",
            string.Join("|", overBeat) == "Paste|Paste special…|Show standard notation|Zoom|Page layout|Score settings…", string.Join("|", overBeat));
        Eq("score empty menu with the clipboard empty: 4 entries", 4, Top(Views.ScoreMenus.Empty(new Views.ScoreEmptyState(true, false, true, true, false), noKeys)).Length);
        Eq("score empty menu off a beat: 4 entries", 4, Top(Views.ScoreMenus.Empty(new Views.ScoreEmptyState(false, true, true, true, false), noKeys)).Length);

        var views = new[] { "Match the instrument", "Fretboard", "Keyboard", "Drums" };
        Views.InstrumentMenuState Instrument(bool keyboard, bool drums) => new(keyboard, drums, "Guitar", "Fretboard", views,
            new[] { "C", "D" }, new[] { "Major", "Minor" }, null, true, false, false, true);
        var fretboard = Top(Views.InstrumentMenus.Build(Instrument(false, false), noKeys));
        Check("fretboard menu: view, scale, note names, preview, left-handed, lock, settings (7 entries)",
            string.Join("|", fretboard) == "Show this track as|Scale|Note names|Preview next notes|Left-handed|Lock fretboard size|Fretboard settings…", string.Join("|", fretboard));
        Eq("keyboard menu: 6 entries (no left-handed)", 6, Top(Views.InstrumentMenus.Build(Instrument(true, false), noKeys)).Length);
        Eq("drum pads menu: 4 entries", 4, Top(Views.InstrumentMenus.Build(Instrument(false, true), noKeys)).Length);

        var editor = NewEditor(out _, out var selTrack);
        for (var i = 0; i < 3; i++) selTrack.Measures[0].Cells[i].Notes.Add(new TabNote { StringIndex = 0, Fret = 3, MidiValue = 67 });   // the cursor steps over real beats only
        editor.SetPosition(0, 0, 0);
        editor.BeginSelection();
        editor.ExtendSelection(1);
        editor.ExtendSelection(1);
        Check("a beat inside the selection is recognised", editor.IsInSelection(0, 1));
        Check("a beat past the selection is not", !editor.IsInSelection(0, 6) && !editor.IsInSelection(1, 0));
        editor.ClearSelection();
        Check("no selection means nothing is inside it", !editor.IsInSelection(0, 0));
    }

    /// <summary>
    /// Lean context menus (owner decisions 2026-09-30): at most two submenu levels, no submenu inside a same-named one, every
    /// setting-like (checkable) item either maps to a SettingsCatalog row or says why not, and each menu has ONE "settings..." door
    /// that deep-links to a real row on the right page. Also the deep-link API and the one menu separator (100 / 125 / 150%).
    /// </summary>
    private static void TestContextMenuLean()
    {
        Func<string, string> noKeys = _ => "";
        var catalog = Services.SettingsCatalog.Build(new Services.AppSettings()).ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);
        var views = new[] { "Match the instrument", "Fretboard", "Keyboard", "Drums" };
        var roots = Services.MusicTheoryService.NoteNames.ToList();
        var scales = Services.MusicTheoryService.Scales.Keys.ToList();
        Views.InstrumentMenuState Instrument(bool keyboard, bool drums) => new(keyboard, drums, "Guitar", "Fretboard", views, roots, scales, "C Major", true, true, true, true);
        var menus = new Dictionary<string, List<Views.MenuSpec>>
        {
            ["Fretboard"] = Views.InstrumentMenus.Build(Instrument(false, false), noKeys),
            ["Keyboard"] = Views.InstrumentMenus.Build(Instrument(true, false), noKeys),
            ["Drum pads"] = Views.InstrumentMenus.Build(Instrument(false, true), noKeys),
            ["Score empty area"] = Views.ScoreMenus.Empty(new Views.ScoreEmptyState(true, true, true, true, true), noKeys),
            ["Timeline bar"] = Views.TimelineMenus.Bar(new Views.BarMenuState(true, true, 3, true, true, true, true, true, true), noKeys),
            ["Timeline selection"] = Views.TimelineMenus.Selection(new Views.SelectionMenuState("Bars 1-2 selected", true, true, true, true), noKeys),
            ["Timeline section"] = Views.TimelineMenus.Section(new Views.SectionMenuState(2, true, true, true), noKeys),
            ["Timeline clip"] = Views.TimelineMenus.Clip(new Views.ClipMenuState(true, true, true, true), noKeys),
        };

        // The mapping table: every checkable (setting-like) item -> its Preferences row, or the reason it has none.
        var table = new List<(string Menu, string Item, string Key)>();
        var problems = new List<string>();
        foreach (var (name, menu) in menus)
        {
            if (Views.TimelineMenus.MaxDepth(menu) > 2) problems.Add($"{name}: depth {Views.TimelineMenus.MaxDepth(menu)}");
            foreach (var nested in Views.TimelineMenus.SameNameNesting(menu)) problems.Add($"{name}: same-name submenu {nested}");
            foreach (var item in Views.TimelineMenus.Checkables(menu))
            {
                var mapped = item.SettingKey is not null;
                if (mapped == (item.NoSetting is not null)) { problems.Add($"{name}: '{item.Header}' needs a setting row or a reason"); continue; }
                if (!mapped) continue;
                table.Add((name, item.Header, item.SettingKey!));
                if (!catalog.ContainsKey(item.SettingKey!)) problems.Add($"{name}: '{item.Header}' maps to missing row {item.SettingKey}");
            }
        }
        Check("every context menu is at most 2 submenu levels deep, has no same-named nested submenu, and every tick maps to a settings row or a reason",
            problems.Count == 0, string.Join("; ", problems));
        Check("the mapping table covers the fretboard, keyboard, score menus (Lock size, Page / Continuous, Vertical / Horizontal, notation, scale, note names)",
            new[] { "fretboard.locksize", "score.pagelayout", "score.scrolling", "score.defaultnotation", "editing.scale", "editing.notenames", "editing.horizon.enabled", "editing.lefthanded", "fretboard.instrumentview" }
                .All(k => table.Any(t => t.Key == k)), string.Join(", ", table.Select(t => t.Key).Distinct()));
        Check("the scale picker still reaches every scale of every key (Scale > key > scale)",
            Views.TimelineMenus.Leaves(menus["Fretboard"]).Count(i => i.Id == Views.InstrumentMenus.ScaleId) == roots.Count * scales.Count);

        // One "... settings..." door per menu; it names a real row, and that row's page is the page the menu promises.
        var doors = new (string Menu, string Row, string Page)[]
        {
            ("Fretboard", Views.InstrumentMenus.SettingsRow, Services.SettingsCatalog.Fretboard),
            ("Score empty area", Views.ScoreMenus.SettingsRow, Services.SettingsCatalog.Score),
            ("Timeline bar", Views.TimelineMenus.TimelineSettingsRow, Services.SettingsCatalog.Timeline),
            ("Timeline selection", Views.TimelineMenus.TimelineSettingsRow, Services.SettingsCatalog.Timeline),
            ("Timeline section", Views.TimelineMenus.SectionSettingsRow, Services.SettingsCatalog.Timeline),
        };
        foreach (var (menu, row, page) in doors)
        {
            var count = Views.TimelineMenus.Leaves(menus[menu]).Count(i => i.Header.EndsWith("settings…", StringComparison.Ordinal));
            Check($"{menu} menu has exactly one '... settings...' entry", count == 1, count.ToString());
            Check($"{menu} menu's settings entry scrolls to a real row on the {page} page",
                catalog.TryGetValue(row, out var d) && string.Equals(d.Category, page, StringComparison.OrdinalIgnoreCase), row);
        }
        Check("the clip menu has a single-item submenu nowhere (MIDI clip writes the notation from the top level)",
            menus["Timeline clip"].All(i => i.Children is null || i.Children.Count(c => !c.IsSeparator) > 1));

        // Every setting-like option that used to live only in a menu has a searchable Preferences row with a description.
        var newRows = new[]
        {
            "fretboard.locksize", "fretboard.showallas", "score.pagelayout", "score.scrolling", "score.textfonts",
            "timeline.individualnotes", "timeline.continuousline", "timeline.hideemptygrid", "timeline.barglow", "timeline.trackgroups",
            "appearance.groupcolour.guitars", "appearance.groupcolour.basses", "appearance.groupcolour.keys", "appearance.groupcolour.drums",
        };
        Check("the rows for the menu options that had none exist, are searchable and have a description",
            newRows.All(k => catalog.TryGetValue(k, out var d) && d.Description.Length > 20 && Services.SettingsCatalog.Matches(d, d.Title.ToLowerInvariant())),
            string.Join(", ", newRows.Where(k => !catalog.ContainsKey(k))));
        Check("'Appearance' is the word for looks: no Preferences group is called 'Display'",
            catalog.Values.All(d => d.Group != "Display"));

        // The deep-link API: the next Preferences window opens on the row's page and consumes the target.
        Views.PreferencesWindow.SetTarget(Services.SettingsCatalog.Timeline, "timeline.brackets");
        var deepLinked = new Views.PreferencesWindow(new Services.AppSettings());
        Check("PreferencesWindow.SetTarget opens the next window on the named page", deepLinked.SelectedCategory == Services.SettingsCatalog.Timeline, deepLinked.SelectedCategory);
        Check("the deep-link target is used once", Views.PreferencesWindow.InitialCategory is null && Views.PreferencesWindow.InitialRow is null);
        deepLinked.Close();
        Views.PreferencesWindow.SetTarget(Services.SettingsCatalog.General, "appearance.groupcolour.drums");
        var colourLink = new Views.PreferencesWindow(new Services.AppSettings());
        Check("a deep link to a row opens the row's own page (colour rows live on Appearance & colours)",
            colourLink.SelectedCategory == Services.SettingsCatalog.Appearance, colourLink.SelectedCategory);
        colourLink.Close();

        TestMenuSeparatorGeometry();
    }

    /// <summary>One separator style for every menu: the rule is 1px, sits inside its own slot (8,4 margin) and is not clipped at 100 / 125 / 150% UI scale.</summary>
    private static void TestMenuSeparatorGeometry()
    {
        var app = System.Windows.Application.Current;
        if (app?.TryFindResource(System.Windows.Controls.MenuItem.SeparatorStyleKey) is not System.Windows.Style style)
        {
            Check("the menu separator style is registered under MenuItem.SeparatorStyleKey", false, "Application resources are unavailable");
            return;
        }
        Check("the implicit Separator style is the same one (bare separators look identical)",
            app.TryFindResource(typeof(System.Windows.Controls.Separator)) is System.Windows.Style implicitStyle &&
            (ReferenceEquals(implicitStyle, style) || ReferenceEquals(implicitStyle.BasedOn, style)));
        foreach (var scale in new[] { 1.0, 1.25, 1.5 })
        {
            var separator = new System.Windows.Controls.Separator { Style = style };
            var host = new System.Windows.Controls.Border
            {
                Width = 240, Child = separator,
                LayoutTransform = new System.Windows.Media.ScaleTransform(scale, scale)   // the app scales menus with a LayoutTransform
            };
            host.Measure(new System.Windows.Size(240 * scale, 400));
            host.Arrange(new System.Windows.Rect(0, 0, host.DesiredSize.Width, host.DesiredSize.Height));
            host.UpdateLayout();
            var rule = System.Windows.Media.VisualTreeHelper.GetChildrenCount(separator) > 0
                ? System.Windows.Media.VisualTreeHelper.GetChild(separator, 0) as System.Windows.FrameworkElement : null;
            var top = rule is null ? -1 : rule.TransformToAncestor(separator).Transform(new System.Windows.Point(0, 0)).Y;
            Check($"menu separator at {scale * 100:0}%: a 1px rule with 4px above and below, not clipped",
                rule is not null && Math.Abs(separator.ActualHeight - 9) < 0.01 && Math.Abs(rule.ActualHeight - 1) < 0.01 && Math.Abs(top - 4) < 0.01 &&
                System.Windows.Media.VisualTreeHelper.GetClip(rule) is null,
                $"slot {separator.ActualHeight:0.##}, rule {rule?.ActualHeight:0.##} at y {top:0.##}");
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    /// <summary>
    /// Zooming out during playback must keep follow armed and put the playhead's system back in view at
    /// the new zoom (regression: the zoom's own scroll was treated as the user scrolling away).
    /// </summary>
    private static void TestFollowSurvivesZoom()
    {
        var editor = new Views.TabEditorControl();
        var project = new SongProject { Tempo = 120 };
        var track = new TrackModel { Name = "Guitar", Measures = TemplateFactory.Measures(48) };
        project.Tracks.Add(track);
        editor.Project = project;
        editor.SelectedTrackIndex = 0;
        var scroll = new System.Windows.Controls.ScrollViewer
        {
            Width = 900, Height = 320, Content = editor,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto
        };
        void Layout()
        {
            scroll.Measure(new System.Windows.Size(900, 320));
            scroll.Arrange(new System.Windows.Rect(0, 0, 900, 320));
            scroll.UpdateLayout();
        }
        Layout();
        const int bar = 40;
        var follow = new Views.ScoreFollowCoordinator(scroll, editor, () => true, () => false, () => bar, () => 0.0,
            () => 48, () => new Services.FollowSettings { Mode = Services.FollowModes.Jump });
        follow.ApplySettings(new Services.FollowSettings { Mode = Services.FollowModes.Jump });
        scroll.ScrollChanged += (_, e) => follow.OnScrollChanged(e); // as MainWindow wires it
        follow.ResetForPlayback();
        follow.OnPlayheadBar(bar);
        Layout();
        foreach (var zoom in new[] { 0.5, 2.0, 0.75 })
        {
            follow.OnScoreLayoutChanging();
            editor.Zoom = zoom;
            editor.InvalidateScoreLayout();
            editor.InvalidateMeasure();
            Layout();
            scroll.ScrollToVerticalOffset(0); // the zoom's own scroll rewrite, as ApplyPageWidth does
            Layout();
            follow.ReanchorAfterZoom();
            Layout();
            var top = editor.SystemTopForMeasure(bar);
            var offset = scroll.VerticalOffset;
            Check($"follow stays on after zoom {zoom:0.##}x", follow.IsFollowing);
            Check($"scroll target contains the playhead system after zoom {zoom:0.##}x",
                top >= offset - 0.5 && top < offset + scroll.ViewportHeight,
                $"systemTop={top:0} offset={offset:0} viewport={scroll.ViewportHeight:0}");
        }
        // A layout/panel pass that moves the offset after every timing window has expired (the fragile case:
        // follow used to infer "user" from ScrollChanged + timing) must not stop follow without a gesture.
        System.Threading.Thread.Sleep(700);
        scroll.ScrollToVerticalOffset(Math.Max(0, scroll.VerticalOffset - 150));
        Layout();
        Check("follow stays on after a late layout scroll with no user gesture", follow.IsFollowing);
        // A genuine gesture (wheel without Ctrl / scrollbar / scroll key) still stops it.
        System.Threading.Thread.Sleep(200);
        follow.NoteUserScrollGesture();
        scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 120);
        Layout();
        Check("a user scroll gesture still stops follow", !follow.IsFollowing);
    }

    /// <summary>The editor's selection state: a range needs two different beats, a range past the last bar is pulled back or dropped, hover clears once.</summary>
    private static void TestEditorSelectionState()
    {
        var state = new EditorSelectionState();
        Check("selection state: nothing selected at first", !state.HasSelection && !state.Selecting);
        state.Begin(2, 3);
        Check("selection state: a range of one beat is not a selection", state.Selecting && !state.HasSelection);
        state.SetEnd(2, 5);
        Check("selection state: two beats make a selection", state.HasSelection && state.Range() == (2, 3, 2, 5));
        state.Set(1, 0, 9, 4);
        state.Coerce(6, 15);
        Check("selection state: an end past the last bar moves to that bar's last cell", state.Range() == (1, 0, 5, 15));
        state.Set(8, 0, 9, 4);
        state.Coerce(6, 15);
        Check("selection state: a range whose bars are gone is dropped", !state.Selecting && state.AnchorMeasure == -1);
        state.HoverMeasure = 1; state.HoverCell = 2;
        Check("selection state: hover clears once", state.ClearHover() && !state.ClearHover() && state.HoverCell == -1);
    }

    /// <summary>
    /// Windows 11 Snap Layouts need WM_NCHITTEST at the maximise button to answer HTMAXBUTTON (9). A real shown borderless window
    /// built like the main window (same WindowChrome, same hooks) is asked at every caption button, normal and maximised.
    /// </summary>
    private static void TestSnapLayoutHitTest()
    {
        var min = new System.Windows.Controls.Button { Width = 46, Height = 44 };
        var max = new System.Windows.Controls.Button { Width = 46, Height = 44 };
        var close = new System.Windows.Controls.Button { Width = 46, Height = 44 };
        // The real caption styles (hover / pressed triggers) when the application resources are loaded.
        var app = System.Windows.Application.Current;
        if (app?.TryFindResource("ChromeCaptionButton") is System.Windows.Style captionStyle &&
            app.TryFindResource("ChromeCloseButton") is System.Windows.Style closeStyle)
        {
            min.Style = captionStyle; max.Style = captionStyle; close.Style = closeStyle;
        }
        var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Height = 44 };
        foreach (var b in new[] { min, max, close })
        {
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            buttons.Children.Add(b);
        }
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(buttons, true);
        var title = new System.Windows.Controls.Border { Height = 44, Child = new System.Windows.Controls.DockPanel() };
        System.Windows.Controls.DockPanel.SetDock(buttons, System.Windows.Controls.Dock.Right);
        ((System.Windows.Controls.DockPanel)title.Child).Children.Add(buttons);
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(title, true);
        var root = new System.Windows.Controls.DockPanel();
        System.Windows.Controls.DockPanel.SetDock(title, System.Windows.Controls.Dock.Top);
        root.Children.Add(title);
        var window = new System.Windows.Window
        {
            WindowStyle = System.Windows.WindowStyle.None, ResizeMode = System.Windows.ResizeMode.CanResize, ShowInTaskbar = false,
            Left = 80, Top = 80, Width = 900, Height = 500, Content = root
        };
        System.Windows.Shell.WindowChrome.SetWindowChrome(window, new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = 0, ResizeBorderThickness = new System.Windows.Thickness(6), CornerRadius = new System.Windows.CornerRadius(0),
            GlassFrameThickness = new System.Windows.Thickness(0), UseAeroCaptionButtons = false
        });
        Shell.WpfCaptionButtonFrame? frame = null;
        window.SourceInitialized += (_, _) =>
        {
            _ = new Shell.WpfResizeBorderFrame(window);
            frame = new Shell.WpfCaptionButtonFrame(window, min, max, close);
        };
        try
        {
            ShowTestWindow(window);
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            Check("snap layouts: the window has the maximise-box style", (GetWindowLong(hwnd, -16) & 0x00010000) != 0);

            static int HitAt(IntPtr hwnd, System.Windows.FrameworkElement element)
            {
                var rect = Shell.ScreenPoints.ScreenRect(element);
                var x = (int)Math.Round(rect.X + rect.Width / 2);
                var y = (int)Math.Round(rect.Y + rect.Height / 2);
                return (int)(SendMessage(hwnd, 0x0084, IntPtr.Zero, (IntPtr)(((y & 0xFFFF) << 16) | (x & 0xFFFF))).ToInt64() & 0xFFFF);
            }

            Check("snap layouts (normal window): WM_NCHITTEST at the buttons answers HTMINBUTTON / HTMAXBUTTON / HTCLOSE",
                HitAt(hwnd, min) == 8 && HitAt(hwnd, max) == 9 && HitAt(hwnd, close) == 20,
                $"min={HitAt(hwnd, min)} max={HitAt(hwnd, max)} close={HitAt(hwnd, close)}");
            // Hover: the native non-client mouse messages drive the button's hot state and background.
            static System.Windows.Media.Color? BackgroundOf(System.Windows.Controls.Button b)
            {
                b.ApplyTemplate();
                return (b.Template?.FindName("Bd", b) as System.Windows.Controls.Border)?.Background is System.Windows.Media.SolidColorBrush brush ? brush.Color : null;
            }
            var before = BackgroundOf(close);
            SendMessage(hwnd, 0x00A0, (IntPtr)20, IntPtr.Zero); // WM_NCMOUSEMOVE over HTCLOSE (checked at once: the cursor is not really there, so the poll would clear it)
            var hotColour = BackgroundOf(close);
            Check("caption hover: WM_NCMOUSEMOVE over HTCLOSE marks only the close button hot",
                Shell.CaptionButtonState.GetIsHot(close) && !Shell.CaptionButtonState.GetIsHot(max) && !Shell.CaptionButtonState.GetIsHot(min));
            if (before is not null)
                Check("caption hover: the close button turns red (its template background changes)",
                    hotColour is { } c && c != before && c.R > 150 && c.G < 90, $"before={before} hot={hotColour}");
            SendMessage(hwnd, 0x00A0, (IntPtr)9, IntPtr.Zero); // moving on to HTMAXBUTTON
            Check("caption hover: hot follows the pointer to the maximise button",
                Shell.CaptionButtonState.GetIsHot(max) && !Shell.CaptionButtonState.GetIsHot(close));
            SendMessage(hwnd, 0x02A2, IntPtr.Zero, IntPtr.Zero); // WM_NCMOUSELEAVE
            Check("caption hover: WM_NCMOUSELEAVE clears the hot state",
                !Shell.CaptionButtonState.GetIsHot(max) && !Shell.CaptionButtonState.GetIsHot(close) && !Shell.CaptionButtonState.GetIsHot(min));

            Shell.WindowPolish.Apply(window); // what the app-wide Loaded handler does to every window (caps Max size to the work area)
            window.WindowState = System.Windows.WindowState.Maximized;
            PumpUi(); window.UpdateLayout(); PumpUi();
            Check("snap layouts (maximised window): the maximise button still answers HTMAXBUTTON",
                HitAt(hwnd, max) == 9 && HitAt(hwnd, close) == 20 && HitAt(hwnd, min) == 8,
                $"min={HitAt(hwnd, min)} max={HitAt(hwnd, max)} close={HitAt(hwnd, close)}");
            CheckMaximisedFillsWorkArea(window, hwnd, "maximised");
            window.WindowState = System.Windows.WindowState.Normal;
            PumpUi(); window.UpdateLayout(); PumpUi();
            window.WindowState = System.Windows.WindowState.Maximized;
            PumpUi(); window.UpdateLayout(); PumpUi();
            CheckMaximisedFillsWorkArea(window, hwnd, "restored then re-maximised");
        }
        finally
        {
            frame?.Dispose();
            window.Close();
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ProbeRect { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ProbeMonitorInfo { public int Size; public ProbeRect Monitor, Work; public uint Flags; }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out ProbeRect rect);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref ProbeMonitorInfo info);

    /// <summary>A maximised window built like the main window must cover exactly its monitor's work area (+-1 px), and its content must fill it.</summary>
    private static void CheckMaximisedFillsWorkArea(System.Windows.Window window, IntPtr hwnd, string state)
    {
        GetWindowRect(hwnd, out var r);
        var info = new ProbeMonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<ProbeMonitorInfo>() };
        GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info);
        var w = info.Work;
        Check($"maximised fill ({state}): window rectangle equals the monitor work area",
            Math.Abs(r.Left - w.Left) <= 1 && Math.Abs(r.Top - w.Top) <= 1 && Math.Abs(r.Right - w.Right) <= 1 && Math.Abs(r.Bottom - w.Bottom) <= 1,
            $"window=({r.Left},{r.Top},{r.Right},{r.Bottom}) work=({w.Left},{w.Top},{w.Right},{w.Bottom})");
        if (window.Content is System.Windows.FrameworkElement root)
        {
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
            var pw = root.ActualWidth * dpi.DpiScaleX; var ph = root.ActualHeight * dpi.DpiScaleY;
            Check($"maximised fill ({state}): the content fills the work area",
                Math.Abs(pw - (w.Right - w.Left)) <= 2 && Math.Abs(ph - (w.Bottom - w.Top)) <= 2,
                $"content={pw:0}x{ph:0} work={w.Right - w.Left}x{w.Bottom - w.Top}");
        }
    }

    /// <summary>
    /// Menu and shortcut share one behaviour per command (EditCommands through the editor): repeat beat, double dot,
    /// repeat open/close and empty bar each take exactly one undo step and change the same model.
    /// </summary>
    private static void TestEditCommands()
    {
        static TabCell Beat(int fret) => new() { DurationDenominator = 8, Notes = { new TabNote { StringIndex = 1, Fret = fret } } };
        (Views.TabEditorControl Editor, SongProject Project, Func<int> Steps) Make()
        {
            var project = Presets.TemplateFactory.Create("Rock Band");
            var e = new Views.TabEditorControl { Project = project, SelectedTrackIndex = 0 };
            var steps = 0;
            e.EditStarting += (_, _) => steps++;
            return (e, project, () => steps);
        }

        // Repeat beat: cursor-relative, no clamping to 16, cell 15 is not overwritten, one undo step.
        var (ed, song, steps) = Make();
        var cells = song.Tracks[0].Measures[0].Cells;
        cells[2] = Beat(7);
        cells[15] = Beat(9);
        ed.SetPosition(0, 5, 1, false);
        ed.CopyLastBeat();
        Check("repeat beat: copies the previous beat onto the cursor slot, leaves slot 15 alone, one undo step",
            cells[5].Notes.Count == 1 && cells[5].Notes[0].Fret == 7 && cells[15].Notes[0].Fret == 9 && steps() == 1 && ed.SelectedCell > 5);
        var before = steps();
        ed.SetPosition(0, 1, 1, false);
        ed.CopyLastBeat();
        Check("repeat beat: nothing earlier to copy means no change and no undo step", cells[1].Notes.Count == 0 && steps() == before);

        // The shortcut id and the direct call give the same result.
        var (ed2, song2, _) = Make();
        song2.Tracks[0].Measures[0].Cells[2] = Beat(7);
        ed2.SetPosition(0, 5, 1, false);
        ed2.TryRunNoteCommand("Note.RepeatBeat");
        Check("repeat beat: the Note.RepeatBeat shortcut gives the same cell as CopyLastBeat",
            song2.Tracks[0].Measures[0].Cells[5].Notes.Count == 1 && song2.Tracks[0].Measures[0].Cells[5].Notes[0].Fret == 7);

        // The active voice is used; voice 1 stays untouched.
        var (ed3, song3, _) = Make();
        var bar3 = song3.Tracks[0].Measures[0];
        bar3.Cells[2] = Beat(3);
        bar3.CellsForVoice(1, create: true)[1] = Beat(12);
        ed3.SetActiveVoice(1);
        ed3.SetPosition(0, 4, 1, false);
        ed3.CopyLastBeat();
        Check("repeat beat: follows the active voice (voice 2 copy, voice 1 unchanged)",
            bar3.Voice2Cells[4].Notes.Count == 1 && bar3.Voice2Cells[4].Notes[0].Fret == 12 && bar3.Cells[4].Notes.Count == 0);

        // Safe on short bars and out-of-range cursors (model level).
        var shortBar = new List<TabCell> { Beat(5), new(), new(), new() };
        Check("repeat beat: a cursor past a short bar's end is a safe no-op",
            !Services.EditCommands.CopyLastBeat(shortBar, 9) && !Services.EditCommands.CopyLastBeat(shortBar, 0) &&
            Services.EditCommands.CopyLastBeat(shortBar, 3) && shortBar[3].Notes[0].Fret == 5 && shortBar.Count == 4);

        // Double dot goes through CanSetDots and takes one undo step (menu == Ctrl+.).
        var (ed4, song4, steps4) = Make();
        var bar4 = song4.Tracks[0].Measures[0];
        bar4.Cells[0] = Beat(4);
        ed4.SetPosition(0, 0, 1, false);
        ed4.TryRunNoteCommand("Note.DoubleDot");
        Check("double dot: sets two dots with exactly one undo step", bar4.Cells[0].Dots == 2 && steps4() == 1);
        ed4.PreventBarOverflow = true;
        bar4.Cells[0] = new TabCell { DurationDenominator = 1, Notes = { new TabNote { StringIndex = 1, Fret = 4 } } };
        var before4 = steps4();
        ed4.SetDots(2);
        Check("double dot: refused when the beat would not fit the bar (CanSetDots), nothing captured",
            !ed4.CanSetDots(2) && bar4.Cells[0].Dots == 0 && steps4() == before4);

        // Repeat open / close: same change in every track, one undo step, the menu's count honoured.
        var (ed5, song5, steps5) = Make();
        ed5.SetPosition(1, 0, 1, false);
        ed5.TryRunNoteCommand("Bar.RepeatOpen");
        Check("repeat open: toggles the bar in every track with one undo step",
            song5.Tracks.All(t => t.Measures[1].RepeatStart) && steps5() == 1);
        ed5.TryRunNoteCommand("Bar.RepeatOpen");
        Check("repeat open: toggling again removes it everywhere", song5.Tracks.All(t => !t.Measures[1].RepeatStart) && steps5() == 2);
        ed5.SetPosition(2, 0, 1, false);
        ed5.TryRunNoteCommand("Bar.RepeatClose");
        Check("repeat close (shortcut): turns the end on with a count of at least 2 in every track",
            song5.Tracks.All(t => t.Measures[2].RepeatEnd && t.Measures[2].RepeatCount >= 2) && steps5() == 3);
        ed5.ToggleRepeatClose();
        Check("repeat close: toggling off clears it", song5.Tracks.All(t => !t.Measures[2].RepeatEnd));
        ed5.ToggleRepeatClose(4);
        Check("repeat close (menu, count from the prompt): same change with the chosen count",
            song5.Tracks.All(t => t.Measures[2].RepeatEnd && t.Measures[2].RepeatCount == 4) && steps5() == 5);

        // Empty bar: active voice only, one undo step, none when already empty.
        var (ed6, song6, steps6) = Make();
        var bar6 = song6.Tracks[0].Measures[0];
        bar6.Cells[0] = Beat(1); bar6.Cells[3] = new TabCell { IsRest = true, DurationDenominator = 4 };
        bar6.CellsForVoice(1, create: true)[0] = Beat(8);
        ed6.SetPosition(0, 0, 1, false);
        ed6.EmptyBar();
        Check("empty bar: clears the active voice, keeps the other voice, one undo step",
            bar6.Cells.All(c => c.Notes.Count == 0 && !c.IsRest) && bar6.Voice2Cells[0].Notes.Count == 1 && steps6() == 1);
        ed6.EmptyBar();
        Check("empty bar: an already empty bar captures no undo step", steps6() == 1);

        // Note toggles and rhythm changes (A5-15 batch 2): the menu handler calls the editor method, the shortcut runs the
        // catalogued id; both must leave the same model and take exactly one undo step.
        static string Sig(TabCell c) => string.Join("|", c.DurationDenominator, c.Dots, c.IsTriplet, c.TupletNumerator, c.IsRest, c.IsTied,
            c.Staccato, c.Tenuto, c.Accent, string.Join(",", c.Notes.Select(n => $"{n.StringIndex}:{n.Fret}:{string.Join("+", n.Techniques.OrderBy(t => t))}")));
        var pairs = new (string Id, Action<Views.TabEditorControl> Menu)[]
        {
            ("Note.Staccato", e => e.ToggleStaccato()), ("Note.Tenuto", e => e.ToggleTenuto()),
            ("Note.Triplet", e => e.ToggleTriplet()), ("Note.Dot", e => e.ToggleDot()), ("Note.DoubleDot", e => e.SetDots(2)),
            ("Note.Tie", e => e.ToggleTie()), ("Note.Rest", e => e.ToggleRest()), ("Note.Dead", e => e.ToggleDead()),
            ("Note.Ghost", e => e.ToggleGhost()), ("Note.Accent", e => e.CycleAccent()),
            ("Note.LetRing", e => e.ToggleTechnique(TechniqueNames.LetRing)), ("Note.PalmMute", e => e.ToggleTechnique(TechniqueNames.PalmMute)),
            ("Note.Grace", e => e.ToggleTechnique("GraceBefore")),
        };
        foreach (var (id, menu) in pairs)
        {
            var (em, sm, stm) = Make(); var (es, ss, sts) = Make();
            foreach (var (e, s) in new[] { (em, sm), (es, ss) })
            {
                s.Tracks[0].Measures[0].Cells[0] = Beat(5);
                s.Tracks[0].Measures[0].Cells[1] = Beat(7);
                e.SetPosition(0, 1, 1, false);
            }
            menu(em);
            es.TryRunNoteCommand(id);
            Check($"{id}: menu and shortcut give the same beat, one undo step each",
                Sig(sm.Tracks[0].Measures[0].Cells[1]) == Sig(ss.Tracks[0].Measures[0].Cells[1]) && stm() == 1 && sts() == 1,
                $"menu={Sig(sm.Tracks[0].Measures[0].Cells[1])} ({stm()}) shortcut={Sig(ss.Tracks[0].Measures[0].Cells[1])} ({sts()})");
        }
        var (edd, sd, std) = Make();
        sd.Tracks[0].Measures[0].Cells[0] = Beat(5);
        edd.SetPosition(0, 0, 1, false);
        edd.Longer();
        var afterLonger = sd.Tracks[0].Measures[0].Cells[0].DurationDenominator;
        edd.Shorter(); edd.SetDuration(16);
        Check("duration: longer/shorter/set each change the beat with one undo step",
            afterLonger == 4 && sd.Tracks[0].Measures[0].Cells[0].DurationDenominator == 16 && std() == 3,
            $"longer={afterLonger} final={sd.Tracks[0].Measures[0].Cells[0].DurationDenominator} steps={std()}");
    }

    /// <summary>Colour drop-down entries read as their name (not an anonymous type's text) and carry a swatch brush.</summary>
    private static void TestColourChoiceEntries()
    {
        var gold = new GpDialogs.ColourChoice("Gold", "#D8A032");
        Check("colour choice: ToString is the colour name and the swatch is the colour",
            gold.ToString() == "Gold" && gold.Swatch is System.Windows.Media.SolidColorBrush b && b.Color == System.Windows.Media.Color.FromRgb(0xD8, 0xA0, 0x32) && b.IsFrozen);
    }

    /// <summary>The shared minimum font size and the higher-contrast secondary text token exist and agree with the code constant.</summary>
    private static void TestReadableTextTokens()
    {
        var app = System.Windows.Application.Current;
        if (app is null) { Skip("readable text tokens", "no application resources in this run"); return; }
        Check("MinFontSize resource equals ThemeService.MinFontSize and is at least 11",
            app.TryFindResource("MinFontSize") is double size && size == Services.ThemeService.MinFontSize && size >= 11);
        Check("SecondaryTextBrush is a theme brush, brighter than muted on dark and darker than muted on light",
            app.TryFindResource("SecondaryTextBrush") is System.Windows.Media.SolidColorBrush
            && Services.ThemeService.TryParse(Services.ThemeService.Blend("#98A1AE", "#E7EAEF", 0.45), out var dark) && dark.R > 0x98
            && Services.ThemeService.TryParse(Services.ThemeService.Blend("#3A3A3A", "#111111", 0.45), out var light) && light.R < 0x3A);
    }

    /// <summary>Automation peers: the score editor reports its cursor as a Value, track rows are list items, the fretboard is named.</summary>
    private static void TestAutomationPeers()
    {
        var project = Presets.TemplateFactory.Create("Blank");
        project.Tracks[0].Measures[0].Cells[0] = new TabCell { DurationDenominator = 4, Notes = { new TabNote { StringIndex = 2, Fret = 7 } } };
        var editor = new Views.TabEditorControl { Project = project, SelectedTrackIndex = 0 };
        editor.SetPosition(0, 0, 2, false);
        var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(editor);
        var value = (peer?.GetPattern(System.Windows.Automation.Peers.PatternInterface.Value) as System.Windows.Automation.Provider.IValueProvider)?.Value ?? "";
        Check("score editor peer: the Value names bar, beat, string, fret and duration",
            value.Contains("bar 1") && value.Contains("beat 1") && value.Contains("string 3") && value.Contains("fret 7") && value.Contains("quarter note"), value);
        var row = new Views.TrackRowBorder();
        System.Windows.Automation.AutomationProperties.SetName(row, "Track 2: Lead, muted");
        var rowPeer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(row);
        Check("arrangement track row peer: a list item named with its mute state",
            rowPeer?.GetAutomationControlType() == System.Windows.Automation.Peers.AutomationControlType.ListItem &&
            rowPeer.GetName() == "Track 2: Lead, muted" && rowPeer.GetItemStatus() == "muted");
        var fret = new Views.InstrumentPanel();
        Check("fretboard peer: named and reports its shown notes",
            System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(fret)?.GetName() == "Fretboard" &&
            System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(fret)?.GetItemStatus().StartsWith("Fretboard") == true);
    }

    /// <summary>A saved template keeps the setup (tracks, tunings, mixer, tempo, signatures) and nothing of the song.</summary>
    private static void TestTemplateKeepsSetupOnly()
    {
        var song = Presets.TemplateFactory.Create("Rock Band");
        song.Tempo = 97; song.TimeSignatureNumerator = 3; song.TimeSignatureDenominator = 4; song.KeySignature = 2;
        song.Lyrics = "la la"; song.Markers.Add(new MarkerModel { Title = "Verse" });
        song.Tracks[0].Capo = 3; song.Tracks[0].Volume = 77; song.Tracks[0].Name = "Rhythm";
        song.Tracks[0].Measures[0].Cells[0] = new TabCell { DurationDenominator = 4, Notes = { new TabNote { StringIndex = 1, Fret = 5 } } };
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-tpl-" + Guid.NewGuid().ToString("N"));
        var previous = Services.UserTemplates.FolderOverride;
        Services.UserTemplates.FolderOverride = folder;
        try
        {
            Services.UserTemplates.Save("Setup only", song);
            var loaded = Services.UserTemplates.Create("Setup only");
            var t = loaded.Tracks[0];
            Check("template keeps tracks, names, capo, volume, tempo and signatures",
                loaded.Tracks.Count == song.Tracks.Count && t.Name == "Rhythm" && t.Capo == 3 && t.Volume == 77 &&
                loaded.Tempo == 97 && loaded.TimeSignatureNumerator == 3 && loaded.KeySignature == 2);
            Check("template drops notes, lyrics and markers and has the default number of empty bars",
                loaded.Lyrics.Length == 0 && loaded.Markers.Count == 0 &&
                loaded.Tracks.All(x => x.Measures.Count == Services.UserTemplates.DefaultBars && x.Measures.All(m => m.Cells.All(c => c.Notes.Count == 0))));
            // An older template file that still holds the whole song is stripped when it is opened.
            Services.ProjectService.Save(System.IO.Path.Combine(folder, "Old.tforge"), song);
            var old = Services.UserTemplates.Create("Old");
            Check("an old template with notes loads as setup only",
                old.Tracks[0].Measures.Count == Services.UserTemplates.DefaultBars && old.Tracks.All(x => x.Measures.All(m => m.Cells.All(c => c.Notes.Count == 0))));
        }
        finally
        {
            Services.UserTemplates.FolderOverride = previous;
            try { System.IO.Directory.Delete(folder, true); } catch { }
        }
    }

    /// <summary>The layout audit finds no colliding text, marking or glyph on the technique test song (every track).</summary>
    private static void TestLayoutAuditTechniqueSong()
    {
        var song = Diagnostics.GmSongAudit.TechniqueSong();
        var found = new List<string>();
        for (var i = 0; i < song.Tracks.Count; i++)
            found.AddRange(Diagnostics.LayoutAudit.Run(song, i).Select(c => $"track {i + 1} {c}"));
        Check("layout audit: no text or marking collides on the technique song", found.Count == 0,
            string.Join(" | ", found.Take(5)));
    }

    /// <summary>Dynamics are engraved only where the dynamic changes (and on the first note), and the layout audit finds no collision with lyrics, beat text or palm mutes.</summary>
    private static void TestDynamicsEngraving()
    {
        var song = new SongProject { Title = "Dynamics", Tempo = 100 };
        var lead = new TrackModel { Name = "Lead", Kind = TrackKind.Guitar, MidiProgram = 29, MidiChannel = 0, Measures = TemplateFactory.Measures(4) };
        song.Tracks.Add(lead);
        int[] dynamicOf = { 5, 5, 2, 2, 2, 4, 4, 6, 6, 6, 6, 6, 6, 6, 1, 1 };   // f f p p | p mf mf ff | ff ff ff ff | ff ff pp pp
        var cells = new List<TabCell>();
        for (var k = 0; k < 16; k++)
        {
            var tech = k is >= 8 and <= 11 ? new[] { TechniqueNames.PalmMute } : Array.Empty<string>();
            cells.Add(RtPut(lead, k / 4, (k % 4) * 4, 4, 0, RtNote(lead, k % 3 + 1, 3 + k % 5, Dynamics.Velocities[dynamicOf[k]], tech)));
        }
        cells[1].Lyrics = "la"; cells[2].Lyrics = "ooh-ooh"; cells[5].Lyrics = "yeah"; cells[7].Text = "big hit"; cells[14].Lyrics = "end";

        var marks = ScorePassages.BuildDynamicMarks(lead);
        var expected = new (int Cell, string Name)[] { (0, "f"), (2, "p"), (5, "mf"), (7, "ff"), (14, "pp") };
        Check("dynamics are marked on the first note and only where the dynamic changes",
            marks.Count == expected.Length && expected.All(e => marks.TryGetValue(cells[e.Cell], out var n) && n == e.Name),
            string.Join(",", marks.Values));
        Check("a repeated dynamic is not marked again", !marks.ContainsKey(cells[1]) && !marks.ContainsKey(cells[6]) && !marks.ContainsKey(cells[15]));

        static bool HasText(System.Windows.Media.Drawing drawing, string text) => drawing switch
        {
            System.Windows.Media.DrawingGroup g => g.Children.Any(c => HasText(c, text)),
            System.Windows.Media.GlyphRunDrawing r => r.GlyphRun?.Characters is { } chars && new string(chars.ToArray()) == text,
            _ => false
        };
        bool Engraves(bool show)
        {
            var editor = new Views.TabEditorControl { Project = song, SelectedTrackIndex = 0, DarkPaper = false, HideCursor = true, Appearance = { ShowDynamics = show } };
            editor.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            editor.Arrange(new System.Windows.Rect(editor.DesiredSize));
            editor.UpdateLayout();
            return editor.AuditSystemDrawings().Any(s => HasText(s.Item2, "mf"));
        }
        Check("the marking is engraved when 'Show dynamics' is on and absent when it is off", Engraves(true) && !Engraves(false));

        var found = Diagnostics.LayoutAudit.Run(song, 0).Select(c => c.ToString()).ToList();
        Check("layout audit: dynamics with lyrics, beat text and palm mutes collide with nothing", found.Count == 0, string.Join(" | ", found.Take(5)));
    }

    /// <summary>Audit 3 section 6a engraving helpers (bends, whammy, tremolo, harmonics, trill, swing) and a render smoke test.</summary>
    private static void TestTechniqueEngraving()
    {
        Check("bend amounts use the conventional text (1/4, 1/2, 3/4, full, 1 1/2, 2)",
            ScoreMarkText.BendAmountLabel(1) == "1/4" && ScoreMarkText.BendAmountLabel(2) == "1/2" &&
            ScoreMarkText.BendAmountLabel(3) == "3/4" && ScoreMarkText.BendAmountLabel(4) == "full" &&
            ScoreMarkText.BendAmountLabel(6) == "1 1/2" && ScoreMarkText.BendAmountLabel(8) == "2");
        Check("whammy amounts are signed (-1/2, -1, -1 1/2)",
            ScoreMarkText.WhammyAmountLabel(-2) == "-1/2" && ScoreMarkText.WhammyAmountLabel(-4) == "-1" &&
            ScoreMarkText.WhammyAmountLabel(-6) == "-1 1/2");
        var bendNote = new TabNote { Techniques = { "Bend" }, BendTypeName = "Prebend" };
        var bendPoints = ScoreMarkText.EffectiveBendPoints(bendNote);
        Check("a pre-bend without points starts already bent; a plain bend starts at zero",
            bendPoints[0].Value > 0 && ScoreMarkText.EffectiveBendPoints(new TabNote { Techniques = { "Bend" } })[0].Value == 0);
        Check("tremolo slash count follows the picking speed",
            ScoreMarkText.TremoloSlashCount(new TabCell { TremoloPickDenominator = 8 }) == 1 &&
            ScoreMarkText.TremoloSlashCount(new TabCell { TremoloPickDenominator = 16 }) == 2 &&
            ScoreMarkText.TremoloSlashCount(new TabCell { TremoloPickDenominator = 32 }) == 3 &&
            ScoreMarkText.TremoloSlashCount(new TabCell()) == 0);
        var busy = new TabNote { Techniques = { "Bend", "Ghost", "Dead", "LetRing", "PickDown", "Harmonic", "TapHarmonic", "Slap" } };
        var label = ScoreMarkText.DrawnTechniqueLabel(new[] { busy });
        Check("TAB text labels drop what is drawn as geometry and never double a harmonic prefix",
            !label.Contains("b") && !label.Contains("G") && !label.Contains("X") && !label.Contains("let ring") &&
            label.Contains("T.H.") && !label.Contains("H T.H.") && !label.Contains("Harm.") && label.Contains("S"));
        Check("a natural harmonic is captioned Harm.", ScoreMarkText.HarmonicCaption(new HashSet<string> { "Harmonic" }) == "Harm.");
        Check("harmonic values use the conventional text: A.H. pitch name, T.H. tapped fret, nothing for natural",
            ScoreMarkText.HarmonicFretText(new TabNote { HarmonicFret = 12 }) == "" &&
            ScoreMarkText.HarmonicFretText(new TabNote { MidiValue = 64, Techniques = { "ArtificialHarmonic" } }) == "E" &&
            ScoreMarkText.HarmonicFretText(new TabNote { HarmonicFret = 5.8, Techniques = { "TapHarmonic" } }) == "5.8" &&
            ScoreMarkText.HarmonicFretText(new TabNote { HarmonicFret = 17, Techniques = { "TapHarmonic" } }) == "17" &&
            ScoreMarkText.HarmonicFretText(new TabNote()) == "");
        Check("swing symbols differ for eighths and sixteenths",
            ScoreMarkText.SwingSymbol(TripletFeels.Eighth) != ScoreMarkText.SwingSymbol(TripletFeels.Sixteenth));

        var project = Presets.TemplateFactory.Create("Blank");
        var cells = project.Tracks[0].Measures[0].Cells;
        cells[0] = new TabCell { DurationDenominator = 8, TremoloPickDenominator = 16, Notes = { new TabNote { StringIndex = 1, Fret = 7, Techniques = { "Bend", "TremoloPick" }, BendTypeName = "BendRelease" } } };
        cells[1] = new TabCell { DurationDenominator = 8, WhammyPoints = { new BendPointModel { Offset = 0, Value = 0 }, new BendPointModel { Offset = 30, Value = -4 }, new BendPointModel { Offset = 60, Value = 0 } },
            Notes = { new TabNote { StringIndex = 2, Fret = 5, IsGraceNote = true }, new TabNote { StringIndex = 2, Fret = 7, Techniques = { "TremBar", "Trill", "LetRing" }, LeftHandFinger = 1, RightHandFinger = 2 } } };
        cells[2] = new TabCell { DurationDenominator = 4, Accent = 2, Notes = { new TabNote { StringIndex = 3, Fret = 9, Ghost = true, Techniques = { "WahOpen", "ArpeggioDown", "PickUp", "LegatoSlide" } } } };
        var editor = new Views.TabEditorControl { Project = project, SelectedTrackIndex = 0, DarkPaper = false };
        try
        {
            editor.Measure(new System.Windows.Size(1000, 4000));
            editor.Arrange(new System.Windows.Rect(editor.DesiredSize));
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(600, 300, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bmp.Render(editor);
            Check("a bar full of bends, whammy, grace, trill, tremolo and fingering engraves without error", bmp.PixelWidth == 600);
        }
        catch (Exception ex) { Check("a bar full of technique marks engraves without error", false, ex.Message); }
    }

    /// <summary>Command palette scoring/catalogue, PDF pagination and file structure, editor cursor description.</summary>
    private static void TestCommandPaletteAndPdf()
    {
        Check("palette: subsequence matches, non-matches score 0",
            Views.CommandPalette.Score("sav", "File and tabs: Save") > 0 && Views.CommandPalette.Score("zzq", "File and tabs: Save") == 0);
        Check("palette: exact word beats scattered letters",
            Views.CommandPalette.Score("save", "File and tabs: Save as") > Views.CommandPalette.Score("save", "Sound: Volume reset"));
        Check("palette + PDF commands are in the catalogue and bound",
            Services.HotkeyCatalog.ById("App.CommandPalette")?.DefaultGesture == "Ctrl+Shift+A" &&
            Services.HotkeyCatalog.ById("File.ExportPdf") is not null &&
            Services.HotkeyCatalog.All.Count(a => a.DefaultGesture == "Ctrl+Shift+A") == 1);
        var tipped = new System.Windows.Controls.Button { ToolTip = "Zoom out (Ctrl+-)" };
        var texted = new System.Windows.Controls.Button { Content = "Refresh devices" };
        Views.AccessibleNames.Apply(tipped); Views.AccessibleNames.Apply(texted);
        var named = new System.Windows.Controls.Button { Name = "PlayButton", ToolTip = "Play or pause (Space)" };
        Views.AccessibleNames.Apply(named);
        Check("controls get an Area.Control automation id from their x:Name",
            System.Windows.Automation.AutomationProperties.GetAutomationId(named) == "Transport.Play" &&
            System.Windows.Automation.AutomationProperties.GetAutomationId(tipped).EndsWith(".ZoomOut") &&
            System.Windows.Automation.AutomationProperties.GetAutomationId(texted).EndsWith(".RefreshDevices"),
            System.Windows.Automation.AutomationProperties.GetAutomationId(named) + " / " + System.Windows.Automation.AutomationProperties.GetAutomationId(tipped));
        Check("controls without a name get one from their tooltip or text",
            System.Windows.Automation.AutomationProperties.GetName(tipped) == "Zoom out (Ctrl+-)" &&
            System.Windows.Automation.AutomationProperties.GetName(texted) == "Refresh devices");
        var pages = Views.ScorePdfExporter.Paginate(900, 100, 200, 10);
        Check("PDF pagination covers every system with header on page 1",
            pages.Count >= 2 && pages[0].Top == 0 && pages[0].Height >= 100 && pages.All(pg => pg.Height <= 842 / (595.0 - 56) * 900 + 1));
        var project = Presets.TemplateFactory.Create("Blank");
        var editor = new Views.TabEditorControl { Project = project, SelectedTrackIndex = 0 };
        Check("editor describes its cursor for screen readers",
            editor.DescribeCursor().Contains("bar 1") && editor.DescribeCursor().Contains("string 1"));
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-pdf-" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            var count = Views.ScorePdfExporter.Export(project, 0, path);
            var head = System.IO.File.ReadAllBytes(path);
            var text = System.Text.Encoding.ASCII.GetString(head);
            Check("PDF export writes a structurally valid file", count >= 1 && text.StartsWith("%PDF-1.4") && text.TrimEnd().EndsWith("%%EOF") && text.Contains("startxref"));
        }
        finally { try { System.IO.File.Delete(path); } catch { } }
    }

    /// <summary>User templates round-trip through the templates folder; a crashed plug-in marks its track's FX button.</summary>
    private static void TestUserTemplatesAndFaultedChain()
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-templates-" + Guid.NewGuid().ToString("N"));
        var previous = Services.UserTemplates.FolderOverride;
        Services.UserTemplates.FolderOverride = folder;
        try
        {
            Check("template names lose path characters", Services.UserTemplates.CleanName("a/b:c") == "abc");
            Check("an empty template name is refused", Services.UserTemplates.CleanName("  ") is null);
            Check("a built-in name cannot be overwritten", Services.UserTemplates.CleanName("Blank") == "Blank (custom)");
            Check("no user templates at first", Services.UserTemplates.List().Count == 0);
            var source = Presets.TemplateFactory.Create("Rock Band");
            source.IsDirty = true;
            var stored = Services.UserTemplates.Save("My band", source);
            Check("saving a template keeps the open score's dirty state", source.IsDirty && stored == "My band");
            Check("the template is listed", Services.UserTemplates.List().SequenceEqual(new[] { "My band" }));
            var loaded = Services.UserTemplates.Create("My band");
            Check("a new score from the template has the same tracks and is unsaved",
                loaded.Tracks.Count == source.Tracks.Count && loaded.IsDirty);
            Check("built-in templates still resolve", Services.UserTemplates.Create("Blank").Tracks.Count >= 1);
        }
        finally
        {
            Services.UserTemplates.FolderOverride = previous;
            try { System.IO.Directory.Delete(folder, true); } catch (System.IO.IOException) { }
        }

        var rig = new Plugins.RigPreset();
        rig.Plugins.Add(new Plugins.PluginSlot { Path = @"C:\VST\Amp.vst3" });
        Check("a chain with a quarantined plug-in is faulted",
            Views.ArrangementPanel.IsChainFaulted(rig, new List<string> { @"c:\vst\amp.vst3" }));
        Check("a healthy chain is not faulted", !Views.ArrangementPanel.IsChainFaulted(rig, new List<string> { @"C:\VST\Other.vst3" }));
    }

    /// <summary>Engraving rules from the audit: empty voice 2 is not drawn; key changes cancel with naturals.</summary>
    private static void TestEngravingHeader()
    {
        var empty = new MeasureModel { Voice2Cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList() };
        Check("an empty second voice is not engraved", !Views.Score.ScoreLayoutEngine.Voice2HasContent(empty));
        var withNote = new MeasureModel { Voice2Cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList() };
        withNote.Voice2Cells[3].Notes.Add(new TabNote { StringIndex = 1, Fret = 3, MidiValue = 50 });
        Check("a second voice with a note is engraved", Views.Score.ScoreLayoutEngine.Voice2HasContent(withNote));
        Check("key sig: G major from C has no naturals", ScoreClefKey.KeySignatureGlyphs(0, 1) == (0, 1));
        Check("key sig: C after A major cancels 3 sharps", ScoreClefKey.KeySignatureGlyphs(3, 0) == (3, 0));
        Check("key sig: A to E major adds without naturals", ScoreClefKey.KeySignatureGlyphs(3, 4) == (0, 4));
        Check("key sig: 4 sharps to 2 sharps cancels 2", ScoreClefKey.KeySignatureGlyphs(4, 2) == (2, 2));
        Check("key sig: sharps to flats cancels all", ScoreClefKey.KeySignatureGlyphs(2, -1) == (2, 1));
    }

    /// <summary>
    /// The shared score/timeline selection (SelectionModel + SelectionSync): each side mirrors the other,
    /// clearing either clears both, bar edits move the range, and no pair of views can loop.
    /// </summary>
    private static void TestSelectionModel()
    {
        // ---- pure model with a fake timeline view that mirrors it ----
        var model = new SelectionModel();
        (int Start, int End)? timeline = null;
        var timelineApplies = 0;
        model.Changed += (_, _) => { timeline = model.BarRange; timelineApplies++; };

        Check("model set from the editor: timeline state matches",
            model.SetRange(0, 2, 5, SelectionOrigin.Editor) && timeline == (2, 5));
        Check("setting the same range again is a no-op (no echo)",
            !model.SetRange(0, 2, 5, SelectionOrigin.Editor) && timelineApplies == 1);
        Check("the same bars from the timeline change the scope to every track",
            model.SetRange(0, 2, 5, SelectionOrigin.Timeline) && model.Scope == SelectionScope.AllTracks && timelineApplies == 2);
        Check("a backwards drag is normalised", model.SetRange(0, 7, 3, SelectionOrigin.Timeline) && timeline == (3, 7));
        Check("clear from the timeline clears the model", model.Clear(SelectionOrigin.Timeline) && !model.HasRange && timeline is null);
        Check("clearing an empty selection is a no-op", !model.Clear(SelectionOrigin.Editor));

        model.SetRange(0, 1, 2, SelectionOrigin.Editor, startCell: 4, endCell: 8);
        Check("switching track keeps the bars and drops the per-track cell bounds",
            model.SetTrack(1) && model.TrackIndex == 1 && model.BarRange == (1, 2) && model.StartCell == 0 && model.EndCell == -1);

        model.SetRange(1, 4, 6, SelectionOrigin.Timeline);
        model.Remap(SelectionModel.InsertMap(10, 2, 3), 13);
        Check("bars inserted before the range move it", model.BarRange == (7, 9), model.BarRange?.ToString());
        model.Remap(SelectionModel.RemoveMap(13, 8, 8), 12);
        Check("a bar deleted inside the range shrinks it", model.BarRange == (7, 8), model.BarRange?.ToString());
        model.Remap(SelectionModel.RemoveMap(12, 6, 9), 8);
        Check("deleting every selected bar clears the range in both views", !model.HasRange && timeline is null);
        model.SetRange(0, 5, 9, SelectionOrigin.Editor);
        model.ClampTo(7, 1);
        Check("undo to a shorter song clamps the range", model.BarRange == (5, 6), model.BarRange?.ToString());
        model.ClampTo(3, 1);
        Check("a range entirely past the song end is cleared", !model.HasRange);

        // ---- no infinite loop: two views that always answer with a different range ----
        var fight = new SelectionModel();
        var calls = 0;
        fight.Changed += (_, _) => { calls++; fight.SetRange(0, 0, calls, SelectionOrigin.Editor); };
        fight.Changed += (_, _) => { calls++; fight.SetRange(0, 0, calls + 100, SelectionOrigin.Timeline); };
        fight.SetRange(0, 1, 1, SelectionOrigin.Command);
        Check("two disagreeing views stop after a bounded number of rounds (no recursion, no endless loop)",
            fight.ChangeCount <= SelectionModel.MaxRounds && !fight.IsNotifying, $"{fight.ChangeCount} notifications");

        // ---- real score editor bound through SelectionSync (the window's glue) ----
        var editor = NewEditor(out var project, out var track);
        while (track.Measures.Count < 8) track.Measures.Add(new MeasureModel { Number = track.Measures.Count + 1 });
        project.Tracks.Add(new TrackModel { Name = "Bass", Measures = TemplateFactory.Measures(8) });
        var shared = new SelectionModel();
        (int Start, int End)? arrangement = null;
        var sync = new SelectionSync(shared, editor, _ => arrangement = shared.BarRange);
        var editorEvents = 0;
        editor.SelectionChanged += (_, _) => { editorEvents++; sync.PushFromEditor(); };

        editor.SelectMeasureRange(1, 3);   // as a score drag of whole bars
        Check("range on the score: model and arrangement show the same bars and track",
            shared.BarRange == (1, 3) && arrangement == (1, 3) && shared.TrackIndex == 0);

        editorEvents = 0;
        shared.SetRange(0, 4, 6, SelectionOrigin.Timeline);   // as a timeline drag
        Check("range on the timeline: the score selects the same bars",
            editor.HasSelection && editor.AffectedMeasureRange == (4, 6) && arrangement == (4, 6));
        Check("applying to the score raised one selection event and did not write back",
            editorEvents == 1 && shared.BarRange == (4, 6) && !sync.IsApplyingToEditor, $"{editorEvents} events");

        editor.SelectedTrackIndex = 1;
        shared.SetTrack(1);
        Check("switching the active track keeps the same bars selected on the new track",
            editor.SelectedTrackIndex == 1 && editor.HasSelection && editor.AffectedMeasureRange == (4, 6));

        shared.Clear(SelectionOrigin.Timeline);   // plain click on empty timeline space
        Check("clear from the timeline clears the score", !editor.HasSelection && arrangement is null);

        editor.SelectMeasureRange(0, 2);
        editor.ClearSelection();                  // click on empty score paper / Esc in the score
        Check("clear from the score clears the timeline", !shared.HasRange && arrangement is null);

        editor.SelectAll();
        Check("Ctrl+A in the score selects every bar on the timeline", shared.BarRange == (0, 7));

        shared.SetRange(1, 5, 7, SelectionOrigin.Command);
        project.Tracks[1].Measures.RemoveRange(6, 2);
        project.Tracks[0].Measures.RemoveRange(6, 2);
        sync.Reconcile(6, project.Tracks.Count);  // after an edit / undo that removed bars 7-8
        Check("after bars are removed under the range, model and score agree on the clamped range",
            shared.BarRange == (5, 5) && editor.HasSelection && editor.AffectedMeasureRange == (5, 5),
            $"model {shared.BarRange}, score {editor.AffectedMeasureRange}");
    }
}
