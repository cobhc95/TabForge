using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using TabForge.Controllers;
using TabForge.Docking;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Shell;
using TabForge.Views;
using TabForge.Visualization;

namespace TabForge;

// MainWindow, view: dock workspace, theme/notation/clipboard, zoom, fullscreen, practice and panel toggles.
public partial class MainWindow
{
    private void InitializeDockWorkspace()
    {
        _dockWorkspace = new DockWorkspace(this);

        // Move the fretboard out of the score surface so it is a normal registered dock panel.
        EditorWorkspace.Children.Remove(InstrumentHost);
        var editorSplitter = EditorWorkspace.Children.OfType<GridSplitter>().FirstOrDefault();
        if (editorSplitter is not null) EditorWorkspace.Children.Remove(editorSplitter);
        EditorWorkspace.RowDefinitions.Clear();
        Grid.SetRow(ScoreEditorHost, 0);
        Grid.SetColumn(ScoreEditorHost, 0);
        EditorWorkspace.ColumnDefinitions.Clear();
        // The score scrolls internally; on short windows it should give up height before
        // the arrangement's track controls are clipped below the workspace.
        _dockWorkspace.SetEditorContent(EditorWorkspace, 460, 90);

        // Hard minimum: the height the current instrument needs to draw completely (strings, markers,
        // fret-number row, legend; keyboard keys; drum map rows), kept up to date as the track, string
        // count or view mode changes, so no splitter, layout, resize or dock can clip it.
        foreach (var (pane, title) in new (FrameworkElement Element, string Title)[]
        {
            (InstrumentHost, "Fretboard pane"), (ArrangementHost, "Arrangement pane"), (ToolsPanelContent, "Tools pane"),
            (SectionsPanelContent, "Sections pane"), (LowerPanelScroll, "Practice and mixer pane"), (ControllerPanel, "Zoom and speed pane")
        })
            if (string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(pane))) System.Windows.Automation.AutomationProperties.SetName(pane, title);
        _dockWorkspace.RegisterPanel("instrument", "Fretboard", InstrumentHost, 360, 150, "instrument", "score-editor");
        ApplyInstrumentMinHeight();
        Instrument.RequiredHeightChanged += _ => ApplyInstrumentMinHeight();
        _dockWorkspace.RegisterPanel("timeline", "Arrangement", ArrangementHost, 440, 112, "timeline", "score-editor");
        _dockWorkspace.RegisterPanel("tools", "Tools", ToolsPanelContent, 210, 150, "tools", "structure");
        _dockWorkspace.RegisterPanel("structure", "Structure", _palettePanelContents["structure"], 210, 150, "tools", "tools");
        _dockWorkspace.RegisterPanel("rhythm", "Rhythm", _palettePanelContents["rhythm"], 210, 140, "tools", "tools");
        _dockWorkspace.RegisterPanel("layout", "Layout", _palettePanelContents["layout"], 210, 140, "tools", "tools");
        // The Sections pane fills its cell, or scrolls when the cell is shorter than the content's minimum (large UI scale).
        SectionsPanelContent.SizeChanged += (_, e) =>
            SectionsPanelGrid.Height = Math.Max(0, e.NewSize.Height - SectionsPanelContent.Padding.Top - SectionsPanelContent.Padding.Bottom);
        SectionsPanelGrid.Height = 0;
        _dockWorkspace.RegisterPanel("sections", "Sections", SectionsPanelContent, 190, 180, "side", "practice");
        _dockWorkspace.RegisterPanel("practice", "Practice / Mixer", LowerPanelScroll, 220, 180, "side", "sections");
        _dockWorkspace.RegisterPanel("playback", "Zoom & speed", ControllerPanel, 262, 44, "side", "sections");
        LowerPanelScroll.Visibility = Visibility.Visible;
        ControllerPanel.Visibility = Visibility.Visible;
        ControllerPanel.Width = double.NaN;
        ControllerPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
        ControllerPanel.VerticalAlignment = VerticalAlignment.Stretch;

        DockLayout.Children.Clear();
        DockLayout.RowDefinitions.Clear();
        DockLayout.ColumnDefinitions.Clear();
        DockLayout.Children.Add(_dockWorkspace);
        _dockWorkspace.LayoutChanged += (_, _) =>
        {
            InstrumentViewMenu.IsChecked = _dockWorkspace.IsPanelVisible("instrument");
            ArrangementMenu.IsChecked = _dockWorkspace.IsPanelVisible("timeline");
            RefreshDockPanelsMenu();
            if (!_suppressWorkspaceSave) SaveSettings();
        };
        BuildDockPanelsMenu();
        _dockWorkspace.RestoreLayout(null);
        _trackListFit = new TrackListFitController(this, this);
        BuildLayoutsMenu();
    }

    // ---------- saved workspace layouts ----------

    private static readonly string[] BuiltInLayoutNames = { "Compose", "Practice", "Mix" };
    private static readonly string[] AllDockPanelIds =
        { "tools", "structure", "rhythm", "layout", "sections", "practice", "playback", "instrument", "timeline" };
    private const int MaxSavedLayouts = 24;
    private MenuItem? _layoutsMenu;

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
        _settings.SavedLayouts.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

    private void BuildLayoutsMenu()
    {
        if (DockPanelsMenu.Parent is not ItemsControl parent) return;
        if (_layoutsMenu is null)
        {
            _layoutsMenu = new MenuItem { Header = "Layouts" };
            parent.Items.Insert(Math.Max(0, parent.Items.IndexOf(DockPanelsMenu)), _layoutsMenu);
            _layoutsMenu.SubmenuOpened += (_, _) => RefreshLayoutsMenu();
        }
        RefreshLayoutsMenu();
    }

    private void RefreshLayoutsMenu()
    {
        if (_layoutsMenu is null) return;
        _layoutsMenu.Items.Clear();
        string GestureText(string id) => HotkeyCatalog.Display(HotkeyCatalog.GestureFor(_settings.Hotkeys, id));
        MenuItem Entry(string name, string? hotkeyId)
        {
            var item = new MenuItem
            {
                Header = name, IsCheckable = true,
                IsChecked = string.Equals(_settings.LastLayout, name, StringComparison.OrdinalIgnoreCase),
                InputGestureText = hotkeyId is null ? "" : GestureText(hotkeyId)
            };
            item.Click += (_, _) => SwitchLayout(name);
            return item;
        }
        _layoutsMenu.Items.Add(Entry("Compose", "View.LayoutCompose"));
        _layoutsMenu.Items.Add(Entry("Practice", "View.LayoutPractice"));
        _layoutsMenu.Items.Add(Entry("Mix", "View.LayoutMix"));
        var custom = _settings.SavedLayouts.Where(l => !BuiltInLayoutNames.Contains(l.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        if (custom.Count > 0) _layoutsMenu.Items.Add(new Separator());
        foreach (var l in custom) _layoutsMenu.Items.Add(Entry(l.Name, null));
        _layoutsMenu.Items.Add(new Separator());
        var save = new MenuItem { Header = "Save current layout as…" };
        save.Click += (_, _) => SaveCurrentLayoutAs();
        _layoutsMenu.Items.Add(save);
        var delete = new MenuItem { Header = "Delete layout", IsEnabled = _settings.SavedLayouts.Count > 0 };
        foreach (var l in _settings.SavedLayouts.ToList())
        {
            var name = l.Name;
            var d = new MenuItem { Header = BuiltInLayoutNames.Contains(name, StringComparer.OrdinalIgnoreCase) ? name + " (back to built-in)" : name };
            d.Click += (_, _) =>
            {
                _settings.SavedLayouts.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (string.Equals(_settings.LastLayout, name, StringComparison.OrdinalIgnoreCase) && !BuiltInLayoutNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    _settings.LastLayout = null;
                SaveSettings();
                StatusText.Text = $"Layout \"{name}\" deleted";
            };
            delete.Items.Add(d);
        }
        _layoutsMenu.Items.Add(delete);
        var reset = new MenuItem { Header = "Reset built-in layouts" };
        reset.Click += (_, _) =>
        {
            _settings.SavedLayouts.RemoveAll(x => BuiltInLayoutNames.Contains(x.Name, StringComparer.OrdinalIgnoreCase));
            SaveSettings();
            StatusText.Text = "Built-in layouts reset";
        };
        _layoutsMenu.Items.Add(reset);
    }

    /// <summary>Instantly applies a layout (panels, dock sizes, window state); documents are not touched.</summary>
    private void SwitchLayout(string name)
    {
        if (_dockWorkspace is null) return;
        var saved = FindSavedLayout(name);
        DockWorkspaceState? state = saved?.State;
        if (state?.Root is null)
        {
            if (!BuiltInLayoutNames.Contains(name, StringComparer.OrdinalIgnoreCase)) { StatusText.Text = $"Layout \"{name}\" not found"; return; }
            state = BuiltInLayout(BuiltInLayoutNames.First(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)));
            saved = null;
        }
        _settings.LastLayout = saved?.Name ?? BuiltInLayoutNames.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) ?? name;
        _dockWorkspace.ApplyLayout(state);
        if (saved is not null && saved.WindowWidth > 0)
        {
            if (saved.Maximised) WindowState = WindowState.Maximized;
            else
            {
                WindowState = WindowState.Normal;
                Width = Math.Max(MinWidth, saved.WindowWidth);
                Height = Math.Max(MinHeight, saved.WindowHeight);
            }
        }
        SaveSettings();
        StatusText.Text = $"Layout: {_settings.LastLayout}";
    }

    private void SaveCurrentLayoutAs()
    {
        if (_dockWorkspace is null) return;
        var name = GpDialogs.Prompt("Save layout", "Layout name:", _settings.LastLayout ?? "My layout")?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        if (name.Length > 40) name = name[..40];
        var existing = FindSavedLayout(name);
        if (existing is null && _settings.SavedLayouts.Count >= MaxSavedLayouts) { StatusText.Text = "Too many saved layouts; delete one first"; return; }
        if (existing is null) _settings.SavedLayouts.Add(existing = new SavedLayout());
        existing.Name = BuiltInLayoutNames.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) ?? name;
        existing.State = _dockWorkspace.CaptureLayout();
        existing.Maximised = WindowState == WindowState.Maximized;
        existing.WindowWidth = WindowState == WindowState.Maximized ? RestoreBounds.Width : Width;
        existing.WindowHeight = WindowState == WindowState.Maximized ? RestoreBounds.Height : Height;
        _settings.LastLayout = existing.Name;
        SaveSettings();
        StatusText.Text = $"Layout \"{existing.Name}\" saved";
    }

    private void BuildDockPanelsMenu()
    {
        DockPanelsMenu.Items.Clear();
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
                if (item.Tag is string panelId) _dockWorkspace?.SetPanelVisible(panelId, item.IsChecked);
                RefreshDockPanelsMenu();
            };
            DockPanelsMenu.Items.Add(item);
        }
        RefreshDockPanelsMenu();
    }

    private void RefreshDockPanelsMenu()
    {
        if (_dockWorkspace is null) return;
        foreach (var item in DockPanelsMenu.Items.OfType<MenuItem>())
            if (item.Tag is string id) item.IsChecked = _dockWorkspace.IsPanelVisible(id);
    }


    // ---------- theme / notation / clipboard ----------

    private void ThemeDark_Click(object sender, RoutedEventArgs e) => SetPaper(dark: true);
    private void ThemeLight_Click(object sender, RoutedEventArgs e) => SetPaper(dark: false);

    private void SetPaper(bool dark)
    {
        Editor.DarkPaper = dark;
        Doc.DarkPaper = dark;
        _settings.Appearance.ScorePaper = dark ? "Dark" : "Light";
        ApplyScorePageBackground();
        Editor.InvalidateScoreLayout();
        SaveSettings();
        StatusText.Text = dark ? "Dark score page" : "Light score page";
    }

    private void NotationBoth_Click(object sender, RoutedEventArgs e) => SetNotation(NotationMode.TabAndStaff);
    private void NotationTab_Click(object sender, RoutedEventArgs e) => SetNotation(NotationMode.TabOnly);
    private void NotationStaff_Click(object sender, RoutedEventArgs e) => SetNotation(NotationMode.StaffOnly);

    private void SetNotation(NotationMode mode)
    {
        Editor.Notation = mode;
        Doc.Notation = mode;
        _settings.Notation = mode.ToString();
        _settings.NotationPreferenceSet = true;
        _appliedNotationPreference = mode;
        Editor.InvalidateMeasure();
        Editor.InvalidateScoreLayout();
        StatusText.Text = "Notation: " + mode switch
        {
            NotationMode.TabOnly => "tablature only",
            NotationMode.StaffOnly => "standard notation only",
            _ => "tablature + standard"
        };
        SaveSettings();
    }

    private void SetLedgerLines(LedgerLineMode mode)
    {
        Editor.LedgerLines = mode;
        _settings.Appearance.LedgerLines = mode.ToString();
        Editor.InvalidateScoreLayout();
        SaveSettings();
    }

    private void PreviewNotes_Click(object sender, RoutedEventArgs e)
    {
        _previewNotes = !_previewNotes;
        PreviewNotesMenu.IsChecked = _previewNotes;
        StatusText.Text = _previewNotes ? "Note preview on" : "Note preview off";
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (CopyScoreSelection(out _) is { } status) StatusText.Text = status;
    }

    /// <summary>Copies the score selection (or the cursor beat) to the shared clipboard; returns the status text.</summary>
    private string? CopyScoreSelection(out ScoreClip? clip)
    {
        clip = Editor.CaptureClip(out var error);
        if (clip is null) return error;
        // Another program can hold the Windows clipboard open; the clip then stays in TabForge's own clipboard.
        var written = ClipboardService.Shared.Copy(clip);
        var what = clip.Kind == ScoreClipKind.Bars
            ? (clip.BarCount == 1 ? "Copied 1 bar" : $"Copied {clip.BarCount} bars")
            : (clip.Tracks[0].Events.Count == 1 ? "Copied 1 beat" : $"Copied {clip.Tracks[0].Events.Count} beats");
        return written ? what : what + " (Windows clipboard busy: paste works in TabForge only)";
    }

    private void Cut_Click(object sender, RoutedEventArgs e)
    {
        var status = CopyScoreSelection(out var clip);
        if (clip is null) { if (status is not null) StatusText.Text = status; return; }
        Editor.CutSelection(clip);
        StatusText.Text = "Cut" + status!["Copied".Length..];
    }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        if (ClipboardService.Shared.TryGetClip(out var error) is not { } clip) { StatusText.Text = error ?? ClipboardService.NotTabForgeNotesMessage; return; }
        if (Editor.Track is null) return;
        var outcome = EditCommands.PasteWithUndo(_undo, _project, clip, Editor.PasteTarget, _settings.Editing,
            new WpfPasteQuestionAsker(this), out var capture);
        if (outcome.Asked.Count > 0) SaveSettings();   // a "Remember my choice" answer
        FinishPaste(outcome, capture);
    }

    /// <summary>Paste Special: one small dialog (repeat, mode, octave shift, keep string and fret, bar settings), then the same paste path with explicit answers.</summary>
    private void PasteSpecial_Click(object sender, RoutedEventArgs e)
    {
        if (ClipboardService.Shared.TryGetClip(out var error) is not { } clip) { StatusText.Text = error ?? ClipboardService.NotTabForgeNotesMessage; return; }
        if (Editor.Track is null) return;
        var dialog = new PasteSpecialDialog(clip.Kind);
        if (IsLoaded) dialog.Owner = this;
        if (DialogHost.ShowModal(dialog) != true || dialog.Result is not { } options) return;
        var outcome = EditCommands.PasteSpecialWithUndo(_undo, _project, clip, Editor.PasteTarget, options, _settings.Editing, out var capture);
        FinishPaste(outcome, capture);
    }

    private void FinishPaste(PasteOutcome outcome, UndoCapture? capture)
    {
        if (!outcome.Changed) { StatusText.Text = outcome.Status; return; }
        if (capture is { Stored: true } stored) Playback.RememberBarMapping(stored.Snapshot);
        if (outcome.BarMap is { } map) FinishSectionStructureEdit(outcome.Status, map);
        Editor.NotifyEdited(markTimeline: false);   // the paste (EditCommands.PasteWithUndo) already invalidated the timeline, once
        StatusText.Text = outcome.Status;
    }

    private void GoTo_Click(object sender, RoutedEventArgs e)
    {
        var txt = GpDialogs.Prompt("Go to (Ctrl+G)", "Bar number or section name:", (Editor.SelectedMeasure + 1).ToString());
        if (txt is null) return;
        if (int.TryParse(txt, out var bar)) { Editor.SetBar(Math.Clamp(bar - 1, 0, Math.Max(0, MaxMeasures() - 1))); ScrollToCursor(); return; }
        var marker = _project.Markers.FirstOrDefault(m => m.Title.Contains(txt, StringComparison.OrdinalIgnoreCase));
        if (marker is not null) { Editor.SetBar(Math.Clamp(marker.MeasureIndex, 0, Math.Max(0, MaxMeasures() - 1))); ScrollToCursor(); }
        else StatusText.Text = "No matching bar or section";
    }

    private void GlobalView_Click(object sender, RoutedEventArgs e)
    {
        ArrangementMenu.IsChecked = !ArrangementMenu.IsChecked;
        ToggleArrangement_Click(sender, e);
    }

    // ---------- creative workflow helpers ----------

    private void DuplicateBar_Click(object sender, RoutedEventArgs e)
    {
        // Bars are shared by every track, so the copy goes into every track (insert / delete bar do the same):
        // the cursor bar, or the whole selected bar range, is copied to right after itself in one undo step.
        var barCount = MaxMeasures();
        if (barCount == 0) return;
        var cursor = Math.Clamp(Editor.SelectedMeasure, 0, barCount - 1);
        var hadRange = _selection.HasRange;
        var (first, last) = hadRange ? (Math.Clamp(_selection.StartBar, 0, barCount - 1), Math.Clamp(_selection.EndBar, 0, barCount - 1)) : (cursor, cursor);
        var count = last - first + 1;
        if (_arrangementController.DuplicateBars(Doc, first, last).Value is not { } map) { StatusText.Text = "Cannot duplicate: the song would get too long"; return; }
        var at = last + 1;
        Editor.SetPosition(Math.Clamp(at, 0, Math.Max(0, MaxMeasures() - 1)), 0, Editor.SelectedString, seekPlayback: false);
        var status = count == 1 ? $"Duplicated bar {first + 1}" : $"Duplicated bars {first + 1}-{last + 1}";
        FinishSectionStructureEdit(status, map);
        if (hadRange) ApplyLoopRange(at, at + count - 1);   // after the remap: the copy is selected, like a paste
        StatusText.Text = status;
    }

    private void RepeatSelection_Click(object sender, RoutedEventArgs e)
    {
        var (ls, le) = GetLoopRange();
        if (le < ls) return;
        var count = GpDialogs.Prompt("Repeat selection", "How many times should the selected bars be repeated?", "1");
        if (count is null || !int.TryParse(count, out var times) || times < 1) return;
        times = Math.Clamp(times, 1, 16);
        DocumentEdits.Run(Doc, project => _arrangementController.RepeatRange(project, ls, le, times) > 0);
        RefreshArrangement();
        RefreshTabs();
        UpdateTitle();
        StatusText.Text = $"Repeated bars {ls + 1}-{le + 1} ×{times}";
    }


    // ---------- view ----------

    private void ZoomCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_zoomSync || !IsLoaded || _restoring || ZoomCombo.SelectedItem is not ComboBoxItem item) return;
        ApplyZoomText(item.Content?.ToString() ?? "Fit width");
    }

    private bool _zoomSync;

    private void ZoomCombo_ReSync(object sender, RoutedEventArgs e) => UpdateZoomControl();

    private void ZoomCombo_VisibleReSync(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) UpdateZoomControl();
    }

    private void ZoomCombo_LostFocus(object sender, RoutedEventArgs e) => CommitCustomZoom();

    private void ZoomCombo_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitCustomZoom();
        e.Handled = true;
    }

    private void CommitCustomZoom()
    {
        if (!IsLoaded || _restoring || ZoomCombo.SelectedItem is ComboBoxItem) return;
        ApplyZoomText(ZoomCombo.Text);
    }

    private void ApplyZoomText(string text, Point? zoomAnchor = null)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("Fit", StringComparison.OrdinalIgnoreCase))
        {
            _zoomFactor = 0;
        }
        else if (double.TryParse(trimmed.TrimEnd('%').Trim(), out var percent) && double.IsFinite(percent))
        {
            percent = Math.Clamp(percent, 50, 200);
            _zoomFactor = percent / 100.0;
        }
        else
        {
            UpdateZoomControl();
            return;
        }

        UpdateZoomControl();
        ApplyPageWidth(zoomAnchor);
        StatusText.Text = _zoomFactor <= 0 ? "Zoom: fit width" : $"Zoom: {_zoomFactor * 100:0}%";
        SaveSettings();
    }

    private void UpdateZoomControl()
    {
        if (ZoomCombo is null) return;
        // Root cause of the blank box: the editable text box only exists once the template is applied
        // (the box lives in a dock pane that starts collapsed), so Text set earlier was lost; startup
        // also forced SelectedIndex = 0 over the restored zoom. Apply the template first, guard
        // re-entrancy, and re-sync when the box loads or becomes visible.
        var was = _zoomSync;
        _zoomSync = true;
        try { ShowZoomOn(ZoomCombo, _zoomFactor); }
        finally { _zoomSync = was; }
    }

    /// <summary>Makes an editable zoom combo display the given zoom (0 = fit width); returns the text shown.</summary>
    internal static string ShowZoomOn(ComboBox combo, double zoomFactor)
    {
        combo.ApplyTemplate();
        var label = zoomFactor <= 0 ? "Fit width" : $"{Math.Clamp(zoomFactor * 100, 50, 200):0}%";
        var preset = combo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Content?.ToString(), label, StringComparison.Ordinal));
        combo.SelectedItem = preset;
        if (preset is null) combo.SelectedIndex = -1;
        combo.Text = label;
        return label;
    }

    private void ScoreScroll_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyPageWidth();

    /// <summary>Applies either fixed-paper zoom or continuous viewport reflow using one shared layout path.</summary>
    private void ApplyPageWidth(Point? zoomAnchor = null)
    {
        _follow.OnScoreLayoutChanging();
        ScorePage.HorizontalAlignment = HorizontalAlignment.Center;
        ScoreScroll.HorizontalContentAlignment = HorizontalAlignment.Center;
        // ViewportWidth still describes the previous layout during a resize. ActualWidth is the
        // new constraint here; using the old viewport leaves continuous systems at the narrow width.
        var viewport = ScoreScroll.ActualWidth > 1 ? ScoreScroll.ActualWidth : ScoreScroll.ViewportWidth;
        if (viewport <= 1) { Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(_follow.ReanchorAfterZoom)); return; }
        if (Editor.HorizontalScroll)
        {
            // One continuous line: the score sizes itself to the line (so edits that lengthen it just
            // grow the scroll range); it starts at the left and scrolls/follows horizontally.
            Editor.Zoom = Math.Clamp(_zoomFactor <= 0 ? 1.0 : _zoomFactor, 0.5, 2.0);
            Editor.Width = double.NaN;
            ScorePage.Width = double.NaN;
            ScorePage.HorizontalAlignment = HorizontalAlignment.Left;
            ScoreScroll.HorizontalContentAlignment = HorizontalAlignment.Left;
            ScoreScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            Editor.InvalidateScoreLayout();
            Editor.InvalidateMeasure();
            // Both page and seamless one-line layouts use the available vertical space.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(CentreHorizontalPage));
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(_follow.ReanchorAfterZoom));
            return;
        }
        ScorePage.Margin = new Thickness(0);
        var oldFactor = Editor.Zoom;
        var factor = Editor.CenterSystems
            ? Math.Clamp(_zoomFactor <= 0 ? 1.0 : _zoomFactor, 0.5, 2.0)
            : Math.Clamp(_zoomFactor <= 0
                ? Math.Max(1, viewport - 2) / TabEditorControl.BasePageWidth
                : _zoomFactor, 0.5, 2.0);
        // Page mode keeps a stable logical sheet and scales that sheet as one object. Continuous
        // mode instead changes composition width with zoom, then centres every resulting system.
        var pageWidth = Editor.CenterSystems
            ? Math.Max(380, Math.Max(1, viewport - 2) / factor)
            : TabEditorControl.BasePageWidth;
        var width = pageWidth * factor;
        var oldRenderedWidth = Editor.ActualWidth > 1 ? Editor.ActualWidth : TabEditorControl.BasePageWidth * oldFactor;
        var oldPageLeft = oldRenderedWidth + 2 < viewport
            ? (viewport - oldRenderedWidth - 2) / 2
            : 0;
        var newPageLeft = width + 2 < viewport ? (viewport - width - 2) / 2 : 0;
        var oldHorizontalOffset = ScoreScroll.HorizontalOffset;
        var oldVerticalOffset = ScoreScroll.VerticalOffset;
        var anchor = zoomAnchor ?? new Point(ScoreScroll.ViewportWidth / 2, 0);

        Editor.PageWidthOverride = pageWidth;
        Editor.Zoom = factor;
        Editor.Width = width;
        ScorePage.Width = width + 2;
        ScoreScroll.HorizontalScrollBarVisibility = width + 2 > viewport + 1 ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        Editor.InvalidateMeasure();
        Editor.InvalidateScoreLayout();
        if (Math.Abs(factor - oldFactor) > 0.001)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ScoreScroll.ScrollToHorizontalOffset((oldHorizontalOffset + anchor.X - oldPageLeft) * factor / oldFactor
                    - (anchor.X - newPageLeft));
                ScoreScroll.ScrollToVerticalOffset((oldVerticalOffset + anchor.Y) * factor / oldFactor - anchor.Y);
                // The zoom-induced scroll is not the user's: keep following and re-anchor on the playhead.
                Dispatcher.BeginInvoke(new Action(_follow.ReanchorAfterZoom), DispatcherPriority.Background);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
        else Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(_follow.ReanchorAfterZoom));

        var layoutLog = Environment.GetEnvironmentVariable("TABFORGE_LAYOUT_LOG") == "1" ? Path.Combine(Path.GetTempPath(), "tabforge-layout.log")
            : Diagnostics.Trace.IsOn(Diagnostics.Trace.Layout) ? Diagnostics.Trace.PathFor(Diagnostics.Trace.Layout) : null;
        if (layoutLog is not null)
        {
            try
            {
                DiagnosticFileService.AppendCappedLine(layoutLog,
                    $"dpi={VisualTreeHelper.GetDpi(this).DpiScaleX:0.##} window={ActualWidth:0}x{ActualHeight:0} " +
                    $"viewport={viewport:0} page={width:0} editorActual={Editor.ActualWidth:0} " +
                    $"hOffset={ScoreScroll.HorizontalOffset:0} extent={ScoreScroll.ExtentWidth:0}",
                    InputLimits.MaxLayoutLogBytes);
            }
            catch (Exception ex) { Debug.WriteLine($"Opt-in layout log write failed: {ex}"); }
        }
    }

    private void CentreHorizontalPage()
    {
        if (!Editor.HorizontalScroll) return;
        var viewport = ScoreScroll.ViewportHeight;
        var page = ScorePage.ActualHeight;
        var top = viewport > 1 && page > 1 ? Math.Max(0, Math.Floor((viewport - page) / 2)) : 0;
        if (Math.Abs(ScorePage.Margin.Top - top) > 0.5) ScorePage.Margin = new Thickness(0, top, 0, 0);
    }

    private void Zoom75_Click(object sender, RoutedEventArgs e) => SetZoom(75);
    private void Zoom100_Click(object sender, RoutedEventArgs e) => SetZoom(100);
    private void Zoom150_Click(object sender, RoutedEventArgs e) => SetZoom(150);
    private void SetZoom(int z)
    {
        ApplyZoomText($"{z}%");
    }

    // Saved bounds/state so exiting fullscreen restores the previous layout exactly (and keeps the
    // custom WindowChrome - setting SingleBorderWindow here used to add a native title bar).
    private Rect _preFullscreenBounds;
    private WindowState _preFullscreenState = WindowState.Normal;

    private void Fullscreen_Click(object sender, RoutedEventArgs e)
    {
        _fullscreen = !_fullscreen;
        if (_fullscreen)
        {
            _preFullscreenState = WindowState;
            _preFullscreenBounds = RestoreBounds;
            WindowState = WindowState.Maximized;
            StatusText.Text = "Full screen (F11 to exit)";
        }
        else
        {
            WindowState = WindowState.Normal;
            if (_preFullscreenBounds.Width > 1 && _preFullscreenBounds.Height > 1)
            {
                Left = _preFullscreenBounds.Left;
                Top = _preFullscreenBounds.Top;
                Width = _preFullscreenBounds.Width;
                Height = _preFullscreenBounds.Height;
            }
            if (_preFullscreenState == WindowState.Maximized) WindowState = WindowState.Maximized;
            StatusText.Text = "Full screen off";
        }
    }

    private void Multitrack_Click(object sender, RoutedEventArgs e) { TrackMixerGrid.BringIntoView(); StatusText.Text = "Multitrack: all tracks in the mixer; click a color block to jump"; }
    /// <summary>View > Mixer / VST: the same command as the Mixer button and hotkey (opens the Mixer window, or raises it); the docked Practice tab only shows practice tools.</summary>
    private void ShowMixer_Click(object sender, RoutedEventArgs e) => OpenMixer();

    private void Stylesheet_Click(object sender, RoutedEventArgs e) => Prefs_Click(sender, e);
    /// <summary>Opens Settings on one page (e.g. Audio & VST).</summary>
    private void OpenSettingsCategory(string category) => OpenSettings(category);

    /// <summary>Opens Settings on <paramref name="category"/> and, when given, scrolls to and highlights the row <paramref name="rowKey"/>
    /// (the "... settings..." entries of the right-click menus).</summary>
    private void OpenSettings(string category, string? rowKey = null)
    {
        Views.PreferencesWindow.SetTarget(category, rowKey);
        Prefs_Click(this, new RoutedEventArgs());
    }

    private void Prefs_Click(object sender, RoutedEventArgs e)
    {
        // Approvals can change while the dialog is open (another window, the Linked audio window): each preview / apply merges with the live list instead of replacing it.
        var approvals = new MediaApprovalMerge(() => _settings.Audio.ApprovedMedia);
        StatusText.Text = _settingsWindowHost.Show(_settings, staged => { approvals.Stage(staged); ApplyPreferences(staged); }, staged => { approvals.Stage(staged); PreviewPreferences(staged); }) switch
        {
            SettingsShowResult.Applied => "Settings updated",
            SettingsShowResult.Cancelled => "Settings cancelled",
            _ => "Settings could not be opened"
        };
    }

    private void ApplyPreferences(AppSettings settings)
    {
        ApplyFileAssociations(settings.General.AssociateFiles);
        var workspaceChanged = !string.Equals(JsonSerializer.Serialize(_settings.Workspace),
            JsonSerializer.Serialize(settings.Workspace), StringComparison.Ordinal);
        var fretboardVisibilityChanged = settings.Appearance.ShowFretboard != _settings.Appearance.ShowFretboard;
        var arrangementVisibilityChanged = settings.Appearance.ShowArrangementOverview != _settings.Appearance.ShowArrangementOverview;
        TabForge.Plugins.PluginQuarantine.KeepLive(settings.Plugins, _settings.Plugins);   // a crash while Preferences was open must survive Apply
        _settings = settings;
        _settingsStore.AcceptCurrentAsReplacement();   // reviewed and applied: it may now replace an unreadable settings file
        if (workspaceChanged) _dockWorkspace?.RestoreLayout(settings.Workspace);
        if (fretboardVisibilityChanged) _dockWorkspace?.SetPanelVisible("instrument", settings.Appearance.ShowFretboard);
        if (arrangementVisibilityChanged) _dockWorkspace?.SetPanelVisible("timeline", settings.Appearance.ShowArrangementOverview);
        SyncFromSettings(applyWindowSize: false);
        ScheduleFitTimelineToTracks();   // auto-fit or the row height may have changed
        if (Arrangement.PanKnobs != settings.Audio.PanKnobs || Arrangement.VolumeKnobs != settings.Audio.VolumeKnobs)
        {
            Arrangement.PanKnobs = settings.Audio.PanKnobs;
            Arrangement.VolumeKnobs = settings.Audio.VolumeKnobs;
            RefreshArrangement();
        }
        if (_visualSettingsChanged) RepaintAfterVisualSettings();
        SaveSettings();
    }

    private void PreviewPreferences(AppSettings settings)
    {
        var workspaceChanged = !string.Equals(JsonSerializer.Serialize(_settings.Workspace),
            JsonSerializer.Serialize(settings.Workspace), StringComparison.Ordinal);
        TabForge.Plugins.PluginQuarantine.KeepLive(settings.Plugins, _settings.Plugins);
        _settings = settings;
        _suppressWorkspaceSave = true;
        try
        {
            if (workspaceChanged) _dockWorkspace?.RestoreLayout(settings.Workspace);
            else
            {
                _dockWorkspace?.SetPanelVisible("instrument", settings.Appearance.ShowFretboard);
                _dockWorkspace?.SetPanelVisible("timeline", settings.Appearance.ShowArrangementOverview);
            }
            SyncFromSettings(applyWindowSize: false);
        if (Arrangement.PanKnobs != settings.Audio.PanKnobs || Arrangement.VolumeKnobs != settings.Audio.VolumeKnobs)
        {
            Arrangement.PanKnobs = settings.Audio.PanKnobs;
            Arrangement.VolumeKnobs = settings.Audio.VolumeKnobs;
            RefreshArrangement();
        }
            if (_visualSettingsChanged) RepaintAfterVisualSettings();
        }
        finally { _suppressWorkspaceSave = false; }
    }

    private static string ExePath => Environment.ProcessPath ?? "";

    /// <summary>Makes the registry match the Windows-integration setting (only when it differs).</summary>
    private void ApplyFileAssociations(bool wanted)
    {
        var exe = ExePath;
        if (exe.Length == 0 || FileAssociations.IsRegistered(exe) == wanted) return;
        try
        {
            if (wanted) FileAssociations.Register(exe); else FileAssociations.Unregister();
            StatusText.Text = wanted ? "TabForge now opens Guitar Pro and TabForge files" : "File associations removed";
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            StatusText.Text = "Windows refused the file-association change";
        }
    }

    private void RepaintAfterVisualSettings()
    {
        Editor.InvalidateVisual();
        Arrangement.RefreshAll();
        Playhead.InvalidateVisual();
        RefreshInstrument();
    }

    /// <summary>Steps the zoom combo (used by the configurable Zoom in/out commands).</summary>
    private void ZoomBy(int direction, Point? zoomAnchor = null)
    {
        var currentPercent = Editor.Zoom * 100;
        var nextPercent = Math.Clamp(Math.Round(currentPercent / 10, MidpointRounding.AwayFromZero) * 10 + direction * 10, 50, 200);
        ApplyZoomText($"{nextPercent:0}%", zoomAnchor);
    }

    private void Tutorial_Click(object sender, RoutedEventArgs e) => Views.TutorialWindow.ShowOrActivate(this);
    private void TutorialDetailed_Click(object sender, RoutedEventArgs e) => Views.TutorialWindow.ShowOrActivate(this, TabForge.Services.TutorialGuide.Detailed);

    private void Shortcuts_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "File: Ctrl+N/O/S, Ctrl+T new tab, Ctrl+Shift+S save as, Ctrl+P print\nEdit: Ctrl+Z/Y, Ins beat, Shift+Del, C copy to end\nBar: Ctrl+Ins/Del, Ctrl+Shift+T time signature, Ctrl+K key, [/] repeats, D directions\nNote: +/- duration, . dot, / triplet, R rest, L tie, F fermata\nEffects: X dead, O ghost, B bend, H HOPO, V vibrato, S slide, I let-ring, Y harmonic, N trill, A chord, T text, G grace\nSound: Space play, Ctrl+Space from start, F9 loop\nView: Ctrl+mouse wheel / Ctrl++ and Ctrl+- zoom, F3 multitrack, F6 fretboard/props, F11 fullscreen, F4 check bars",
            "Keyboard shortcuts", MessageBoxButton.OK, MessageBoxImage.Information);
    }


    // ---------- practice / view toggles ----------

    private void PracticeOption_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _showNoteNames = PracticeNamesCheck.IsChecked == true;
        _leftHanded = LeftHandedCheck.IsChecked == true;
        _previewHorizon = PracticePreviewCheck.IsChecked == true ? (int)Math.Round(PreviewHorizonSlider.Value) : 0;
        _scaleHighlight = ScaleHighlightCombo.SelectedIndex <= 0 ? null : ScaleHighlightCombo.SelectedItem?.ToString();
        _fretboardFrets = FretboardFretsCombo.SelectedIndex == 1 ? 12 : 24;
        RefreshInstrument();
        SaveSettings();
    }

    /// <summary>
    /// Right-click on the fretboard / keyboard / drum pads: the lean menu of <see cref="InstrumentMenus"/> (owner decisions
    /// 2026-09-30). Appearance, sizes, colours and look-ahead live in Preferences behind "Fretboard settings...".
    /// </summary>
    private void Instrument_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        ShowInstrumentContextMenu(fromKeyboard: false);
        e.Handled = true;
    }

    /// <summary>The fretboard / keyboard menu; from the keyboard (Shift+F10, the Menu key) it opens at the panel's top-left with the first item focused.</summary>
    private void ShowInstrumentContextMenu(bool fromKeyboard)
    {
        var track = SelectedTrack;
        if (track is null) return;
        var drums = !Instrument.CanRepositionFretboard && !Instrument.ShowsKeyboard;
        var current = _trackInstrumentViews.TryGetValue(track, out var own) ? own : _instrumentViewOverride ?? _settings.Editing.InstrumentView;
        var state = new InstrumentMenuState(Instrument.ShowsKeyboard, drums, track.Name, current, InstrumentViews.All.ToList(),
            MusicTheoryService.NoteNames.ToList(), MusicTheoryService.Scales.Keys.ToList(), _scaleHighlight, _showNoteNames,
            _previewHorizon > 0, _leftHanded, _settings.Appearance.LockInstrumentSize);
        var menu = NewSpecMenu("Fretboard options", InstrumentMenus.Build(state, MenuKey), spec => RunInstrumentCommand(spec, track), Instrument);
        Instrument.ContextMenu = menu;
        OpenContextMenu(menu, Instrument, new Point(8, 8), fromKeyboard);
    }

    private void RunInstrumentCommand(MenuSpec spec, TrackModel track)
    {
        switch (spec.Id)
        {
            case InstrumentMenus.ViewId: SetInstrumentView(spec.Arg, track); break;
            case InstrumentMenus.ScaleId: SetScaleHighlight(spec.Arg); break;
            case InstrumentMenus.FindScaleId: OpenScaleFinder(); break;
            case InstrumentMenus.ClearScaleId: ClearScaleHighlight(); break;
            case InstrumentMenus.NoteNamesId:
                PracticeNamesCheck.IsChecked = !spec.Checked;
                PracticeOption_Changed(PracticeNamesCheck, new RoutedEventArgs());
                break;
            case InstrumentMenus.PreviewId:
                PracticePreviewCheck.IsChecked = !spec.Checked;
                PracticeOption_Changed(PracticePreviewCheck, new RoutedEventArgs());
                break;
            case InstrumentMenus.LeftHandedId:
                LeftHandedCheck.IsChecked = !spec.Checked;
                PracticeOption_Changed(LeftHandedCheck, new RoutedEventArgs());
                break;
            case InstrumentMenus.LockId: ToggleInstrumentSizeLock(); break;
            case InstrumentMenus.SettingsId: OpenSettings(SettingsCatalog.Fretboard, InstrumentMenus.SettingsRow); break;
        }
    }


    /// <summary>The panels on the side (tools, structure, rhythm, layout, sections, practice, metronome).</summary>
    private static readonly string[] SidePanelIds = { "tools", "structure", "rhythm", "layout", "sections", "practice", "playback" };
    private List<string>? _hiddenSidePanels;

    /// <summary>Side panel button / hotkey: hides every side panel, or brings back exactly the ones that were shown.</summary>
    private void ToggleSidePanel_Click(object sender, RoutedEventArgs e) => ToggleSidePanel();

    /// <summary>The whole layout as it was when the side panel was hidden, brought back exactly on show.</summary>
    private TabForge.Docking.DockWorkspaceState? _layoutBeforeSideHide
    {
        get => _settings.WorkspaceBeforeSideHide;
        set => _settings.WorkspaceBeforeSideHide = value;
    }

    private void ToggleSidePanel()
    {
        if (_dockWorkspace is null) return;
        var shown = SidePanelIds.Where(_dockWorkspace.IsPanelVisible).ToList();
        if (shown.Count > 0)
        {
            _hiddenSidePanels = shown;
            _layoutBeforeSideHide = _dockWorkspace.CaptureLayout();   // placement, tab order and sizes
            _dockWorkspace.HidePanels(shown);                          // one rebuild, not one per panel
            SaveSettings();
            StatusText.Text = "Side panel hidden";
        }
        else if (_layoutBeforeSideHide is { } saved && OnlySidePanelsDiffer(saved))
        {
            _dockWorkspace.ApplyLayout(saved);
            _layoutBeforeSideHide = null; _hiddenSidePanels = null;
            SaveSettings();
            StatusText.Text = "Side panel shown";
        }
        else
        {
            // Other panels were opened or closed meanwhile: bring back the side panels to their places.
            var restore = _hiddenSidePanels is { Count: > 0 } list ? list : SidePanelIds.ToList();
            foreach (var id in restore) _dockWorkspace.SetPanelVisible(id, true);
            _layoutBeforeSideHide = null; _hiddenSidePanels = null;
            StatusText.Text = "Side panel shown";
        }
    }

    /// <summary>True when every panel outside the side panel is where it was (so the saved layout can be restored whole).</summary>
    private bool OnlySidePanelsDiffer(TabForge.Docking.DockWorkspaceState saved)
    {
        var visibleNow = _dockWorkspace!.CaptureLayout();
        static HashSet<string> Others(TabForge.Docking.DockWorkspaceState state, IEnumerable<string> side) =>
            Views.DockWorkspace.PanelsOf(state).Where(p => !side.Contains(p)).ToHashSet();
        return Others(saved, SidePanelIds).SetEquals(Others(visibleNow, SidePanelIds));
    }

    private void ApplyInstrumentMinHeight() =>
        _dockWorkspace?.SetPanelContentMinHeight("instrument",
            Instrument.RequiredHeight + InstrumentHost.BorderThickness.Top + InstrumentHost.BorderThickness.Bottom);

    /// <summary>
    /// Applies Appearance.LockInstrumentSize: locked = the pane keeps its saved height (splitter not draggable);
    /// unlocked = resizable, the drawing scales with the pane. With no saved height yet, the current height
    /// is taken once the window has laid out.
    /// </summary>
    private void ApplyInstrumentSizeLock()
    {
        if (_dockWorkspace is null) return;
        var appearance = _settings.Appearance;
        if (!appearance.LockInstrumentSize) { _dockWorkspace.SetPanelFixedHeight("instrument", null); return; }
        if (appearance.InstrumentPaneHeight > 0)
        {
            _dockWorkspace.SetPanelFixedHeight("instrument", Math.Max(appearance.InstrumentPaneHeight, InstrumentMinContentHeight()));
            return;
        }
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (!_settings.Appearance.LockInstrumentSize || _settings.Appearance.InstrumentPaneHeight > 0) return;
            _settings.Appearance.InstrumentPaneHeight = Math.Ceiling(InstrumentHost.ActualHeight > 0
                ? InstrumentHost.ActualHeight : Instrument.NaturalHeight + 1);
            ApplyInstrumentSizeLock();
            SaveSettings();
        });
    }

    private double InstrumentMinContentHeight() =>
        Instrument.RequiredHeight + InstrumentHost.BorderThickness.Top + InstrumentHost.BorderThickness.Bottom;

    /// <summary>Instrument right-click menu "Lock fretboard size" and the View.LockInstrumentSize hotkey.</summary>
    private void ToggleInstrumentSizeLock()
    {
        var appearance = _settings.Appearance;
        appearance.LockInstrumentSize = !appearance.LockInstrumentSize;
        // Locking keeps the size the pane has now (the size the user chose while unlocked).
        if (appearance.LockInstrumentSize && InstrumentHost.ActualHeight > 0)
            appearance.InstrumentPaneHeight = Math.Ceiling(InstrumentHost.ActualHeight);
        ApplyInstrumentSizeLock();
        SaveSettings();
        StatusText.Text = appearance.LockInstrumentSize
            ? "Fretboard size locked"
            : "Fretboard size unlocked: drag the pane's edge to resize (the drawing scales)";
    }

    /// <summary>View menu "Instrument view", the toolbar fretboard button and the View.InstrumentPanel hotkey.</summary>
    private void ToggleInstrumentView_Click(object sender, RoutedEventArgs e) => ToggleInstrumentPanel();

    private void ToggleInstrumentPanel()
    {
        if (_dockWorkspace is null) return;
        var show = !_dockWorkspace.IsPanelVisible("instrument");
        InstrumentViewMenu.IsChecked = show;
        _dockWorkspace.SetPanelVisible("instrument", show);   // LayoutChanged saves Appearance.ShowFretboard
        StatusText.Text = show ? "Fretboard / keyboard shown" : "Fretboard / keyboard hidden";
    }

    private void ToggleArrangement_Click(object sender, RoutedEventArgs e)
    {
        var show = !_dockWorkspace!.IsPanelVisible("timeline");
        ArrangementMenu.IsChecked = show;
        _dockWorkspace.SetPanelVisible("timeline", show);
    }

    private void ArrangementIndividualNotesMenu_Click(object sender, RoutedEventArgs e)
    {
        Arrangement.ShowIndividualNotes = ArrangementIndividualNotesMenu.IsChecked;
        ArrangementIndividualNotesMenu.IsChecked = Arrangement.ShowIndividualNotes;
        ArrangementContinuousBlocksMenu.IsChecked = Arrangement.ShowContinuousBlocks;
        SaveTimelineAppearance();
    }

    private void ArrangementContinuousBlocksMenu_Click(object sender, RoutedEventArgs e)
    {
        Arrangement.ShowContinuousBlocks = ArrangementContinuousBlocksMenu.IsChecked;
        ArrangementIndividualNotesMenu.IsChecked = Arrangement.ShowIndividualNotes;
        ArrangementContinuousBlocksMenu.IsChecked = Arrangement.ShowContinuousBlocks;
        SaveTimelineAppearance();
    }

    private void ShowPractice_Click(object sender, RoutedEventArgs e)
    {
        _dockWorkspace?.SetPanelVisible("practice", true);
        _dockWorkspace?.SelectPanel("practice");
        PracticePreviewCheck.IsChecked = true;
        StatusText.Text = "Practice and mixer panel";
    }
}
