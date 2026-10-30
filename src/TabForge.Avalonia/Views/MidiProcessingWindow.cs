using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Audio;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Avalonia.Views.Fx;
using TabForge.Avalonia.Views.Midi;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

/// <summary>
/// One plug-in's MIDI processing list (the FX chain window's MIDI... button), the MIDI processing window:
/// a searchable catalog of every processor and every parameter on the left, the ordered list in the middle (tick to enable, move, remove) and the selected
/// processor's presets, description and parameters on the right, with the log panel while a log processor is on.
/// Owns: the layout, the list and catalog wiring, the parameter search highlight, the log view and the engine's log watch.
/// Does not own: what a processor does (MidiProcessorCatalog and the engine), the list rules and texts (MidiProcessingListPresenter), the parameter controls
/// (MidiParamEditors, MidiNoteMapRows), the preset rows (MidiPresetBars), the undo step and engine update (the edit callback the FX window passes).
/// Not ported: drag to reorder the list (the Up and Down buttons move a processor).
/// The MIDI log half lives in MidiProcessingWindow.Log.cs and the repaint half in MidiProcessingWindow.Repaint.cs, so no file passes 500 lines.
/// Tests: TabForge.Avalonia.Tests.MidiProcessingWindowTests, MidiClipLayoutTests.
/// </summary>
public sealed partial class MidiProcessingWindow : Window
{
    private const double DimmedOpacity = 0.4;
    private readonly IFxChainHost _host;
    private readonly TrackModel _track;
    private readonly PluginSlot? _slot;
    private readonly Action<Action> _edit;
    private readonly MidiWindowRepaint _repaint;
    private readonly List<PluginMidiProcessor> _noSlotProcessors = new();
    private readonly MidiLearn _learn = new();
    private readonly MidiParamEditors? _editors;
    private readonly MidiPresetBars? _presets;
    private readonly TextBox _search = new() { Name = "SearchBox", ClipToBounds = true };
    private readonly ListBox _catalog = new() { BorderThickness = DialogMetrics.PresetNoOutline, Name = "CatalogList", ClipToBounds = true };
    private readonly ListBox _list = new() { BorderThickness = DialogMetrics.PresetNoOutline, Name = "ProcessorList", ClipToBounds = true };
    private readonly TextBlock _title = new() { FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Name = "TitleText" };
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap, Name = "DescriptionText" };
    private readonly TextBlock _parameterNote = new() { TextWrapping = TextWrapping.Wrap, Name = "ParameterNote", IsVisible = false };
    private readonly StackPanel _params = new() { Name = "ParamPanel" };
    private readonly Dictionary<string, (Border Box, MidiParamInfo Param)> _elems = new();
    private MidiNoteMapRows? _mapRows;
    private ScrollViewer? _detailViewer;
    private string[] _queryWords = Array.Empty<string>();
    private string? _jumpKey;
    // Perf: typing re-applies the search at once on the first keystroke and once more when it rests (~100 ms),
    // so a fast query pays two passes, not one per keystroke.
    private readonly IUiTimer _searchDebounce = AvaloniaUiThread.ForApplication().CreateTimer(TimeSpan.FromMilliseconds(UiTiming.FilterDebounceMs));
    private bool _searchDirty;
    private readonly Button _add = Btn("Add ›", "Add the selected processor after the selected one in the list", "AddButton");
    private readonly Button _up = Btn("▲", "Move up (earlier in the chain)", "UpButton");
    private readonly Button _down = Btn("▼", "Move down (later in the chain)", "DownButton");
    private readonly Button _remove = Btn("Remove", "Remove the selected processor (Delete)", "RemoveButton");
    private readonly TextBox _log = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = TabForge.Avalonia.Platform.AvaloniaFonts.Family("Consolas"), FontSize = 11, Height = 150, Name = "LogBox" };
    private readonly StackPanel _logPanel = new() { IsVisible = false, Name = "LogPanel" };
    private readonly List<string> _logLines = new();
    private readonly TextBlock _status = ToolWindowParts.Status("StatusLine");
    private TabControl? _midiTabs;
    private bool _building, _watching;

    /// <summary>The questions and messages of this window; a test replaces members to answer without a modal box.</summary>
    public FxChainDialogs Dialogs { get; } = new();

    /// <param name="host">The song and engine the window works with.</param>
    /// <param name="track">The track that holds the plug-in.</param>
    /// <param name="slot">The plug-in whose MIDI processors the window edits; null opens the Articulations tab for a track with no plug-in.</param>
    /// <param name="edit">Runs a change of the slot (or track) as one undo step with the engine update.</param>
    public MidiProcessingWindow(IFxChainHost host, TrackModel track, PluginSlot? slot, Action<Action> edit)
        : this(host, track, slot, edit, PlatformAdapters.NativeWindows, AvaloniaUiThread.ForApplication()) { }

    /// <summary>The MIDI processing window of a track with no plug-in: Processors explains why it cannot run; Articulations edits the track's map.</summary>
    public MidiProcessingWindow(IFxChainHost host, TrackModel track, Action<Action> edit)
        : this(host, track, null, edit, PlatformAdapters.NativeWindows, AvaloniaUiThread.ForApplication()) { }

    /// <param name="native">The platform's native window calls; a test passes a fake to record the defensive repaint.</param>
    /// <param name="ui">The UI thread the repaint is posted to.</param>
    internal MidiProcessingWindow(IFxChainHost host, TrackModel track, PluginSlot? slot, Action<Action> edit, INativeWindowHandles native, IUiThread ui)
    {
        (_host, _track, _slot, _edit) = (host, track, slot, edit);
        _repaint = new MidiWindowRepaint(native, ui, () => native.Of(this));
        Dialogs.Files = new AvaloniaFileDialogs(host.DialogFolders);
        if (slot is not null) slot.MidiProcessors ??= new List<PluginMidiProcessor>();
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Title = slot is null ? $"MIDI processing: {track.Name}" : $"MIDI processing: {slot.Name}";
        Width = DialogMetrics.MidiWindowWidth; Height = DialogMetrics.MidiWindowHeight; MinWidth = DialogMetrics.MidiWindowMinWidth; MinHeight = DialogMetrics.MidiWindowMinHeight;
        ShowInTaskbar = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        if (slot is not null)
        {
            _editors = new MidiParamEditors(host, track, slot, edit, _learn);
            _presets = new MidiPresetBars(this, () => Dialogs, () => Procs, slot,
                (list, applied) => { _edit(() => { Procs.Clear(); Procs.AddRange(list); slot.AppliedMidiPreset = applied; }); RefreshList(0); },
                (proc, json, applied) => { _edit(() => { proc.Params = json; proc.AppliedPreset = applied; }); ShowSelected(); });
        }
        Content = BuildRoot();
        WireEvents();
        if (slot is not null)
        {
            _catalog.ItemsSource = MidiProcessorCatalog.SearchAll("");
            if (_catalog.ItemCount > 0) _catalog.SelectedIndex = 0;
        }
        if (host.Engine is { } engine) { engine.MidiLogReceived += OnLog; engine.ChainLoaded += UpdateWatch; }
        _learn.Changed += UpdateWatch;
        WireRepaint();
        Closed += (_, _) => OnClosed();
        if (slot is not null) RefreshList(0);
    }

    /// <summary>A sample plug-in with a known processor and an enabled log. Unknown types are not shown here: tests that cover them build their own slot.</summary>
    public static PluginSlot SampleSlot() => new()
    {
        Name = "Sample synth",
        Type = PluginSlotType.Instrument,
        MidiProcessors =
        [
            new PluginMidiProcessor { Type = "transpose" },
            new PluginMidiProcessor { Type = "log" },
        ],
    };

    private List<PluginMidiProcessor> Procs => _slot?.MidiProcessors ?? _noSlotProcessors;
    private PluginMidiProcessor? Selected => _list.SelectedItem as PluginMidiProcessor;
    private bool HasLog => Procs.Any(p => p.Enabled && p.Type == "log");

    // ---------- layout ----------

    private Control BuildRoot()
    {
        if (_slot is null) return BuildNoSlotRoot();
        var top = new WrapPanel { Orientation = Orientation.Horizontal };
        top.Spaced(DialogMetrics.MidiSearchGap);
        var presetsLabel = new TextBlock { Text = "Presets", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary") };
        presetsLabel.Spaced(DialogMetrics.MidiInputGap);
        top.Children.Add(presetsLabel);
        top.Children.Add(_presets!.ListBox);
        top.Children.Add(_presets.SaveList);
        top.Children.Add(_presets.DeleteList);
        var body = MidiSmallLayout.BodyGrid();
        var catalog = CatalogPane(); Grid.SetColumn(catalog, 0);
        var list = ListPane(); Grid.SetColumn(list, 1);
        var detail = DetailPane(); Grid.SetColumn(detail, 2);
        body.Children.Add(catalog); body.Children.Add(list); body.Children.Add(detail);
        var root = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,*,Auto") };
        root.Spaced(DialogMetrics.MidiContentInset);
        Grid.SetRow(top, 0); Grid.SetRow(body, 1); Grid.SetRow(_status, 2);
        root.Children.Add(top); root.Children.Add(body); root.Children.Add(_status);
        if (_slot!.Type == PluginSlotType.Instrument) _midiTabs = Articulations.ArticulationsTab.Install(root, top, body, () => new Articulations.ArticulationsPane(_host, _track, _slot, _edit, this, () => Dialogs));   // the Articulations tab
        return root;
    }

    /// <summary>A track without a plug-in: Processors says why they cannot run, and the Articulations tab edits the track's own map.</summary>
    private Control BuildNoSlotRoot()
    {
        var root = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,*,Auto") };
        root.Spaced(DialogMetrics.MidiContentInset);
        var note = new TextBlock { Text = MidiProcessingListPresenter.NoInstrumentDescription, TextWrapping = TextWrapping.Wrap, Name = "NoInstrumentNote" };
        note[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        var processors = new StackPanel { Children = { Heading("Processors"), note } };
        Grid.SetRow(_status, 2);
        root.Children.Add(_status);
        _midiTabs = Articulations.ArticulationsTab.Install(root, processors,
            () => new Articulations.ArticulationsPane(_host, _track, null, _edit, this, () => Dialogs));
        return root;
    }

    /// <summary>Shows the Articulations tab (the Export MIDI "View / edit" link opens the same pane).</summary>
    public void OpenArticulationsTab()
    {
        if (_midiTabs?.Items.OfType<TabItem>().FirstOrDefault(tab => tab.Name == "ArticulationsTab") is { } tab) _midiTabs.SelectedItem = tab;
    }

    private DockPanel CatalogPane()
    {
        var pane = new DockPanel { Name = "CatalogPane", ClipToBounds = true };
        var heading = Heading("Add a processor");
        DockPanel.SetDock(heading, Dock.Top);
        DockPanel.SetDock(_search, Dock.Top);
        DockPanel.SetDock(_add, Dock.Bottom);
        _search.Spaced(DialogMetrics.MidiSearchGap);
        MidiSharedRows.Tip(_search, "Search processors and every parameter (e.g. \"velocity\", \"jitter\", \"drum map\"). Enter adds the processor or jumps to the parameter");
        _add.HorizontalAlignment = HorizontalAlignment.Left;
        _add.Spaced(DialogMetrics.MidiBarGap);
        _catalog.ItemTemplate = new FuncDataTemplate<MidiProcessorCatalog.SearchHit>((hit, _) => MidiSmallLayout.CatalogText(hit?.Display ?? ""), supportsRecycling: true);
        // Perf: the catalog is virtualized (recycling template over a VirtualizingStackPanel): only the visible hits build controls.
        _catalog.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
        pane.Children.Add(heading); pane.Children.Add(_search); pane.Children.Add(_add); pane.Children.Add(Frame(_catalog));
        return pane;
    }

    private DockPanel ListPane()
    {
        var pane = new DockPanel { Name = "ListPane", ClipToBounds = true };
        var heading = Heading("In order (top runs first)");
        DockPanel.SetDock(heading, Dock.Top);
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, Name = "ListButtonRow", ClipToBounds = true };
        buttons.Spaced(DialogMetrics.MidiBarGap);
        buttons.Children.Add(_up); buttons.Children.Add(_down); buttons.Children.Add(_remove);
        DockPanel.SetDock(buttons, Dock.Bottom);
        _list.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
        _list.ItemTemplate = new FuncDataTemplate<PluginMidiProcessor>((proc, _) => ProcessorRow(proc), supportsRecycling: true);
        pane.Children.Add(heading); pane.Children.Add(buttons); pane.Children.Add(Frame(_list));
        return pane;
    }

    private DockPanel DetailPane()
    {
        var pane = new DockPanel { Name = "DetailPane", MinWidth = DialogMetrics.MidiEditorMinWidth, ClipToBounds = true };
        _logPanel.Children.Add(Heading("MIDI log (this window watches while it is open)"));
        _logPanel.Children.Add(_log);
        var clear = Btn("Clear log", null, "ClearLogButton");
        clear.Spaced(DialogMetrics.MidiBarGap);
        clear.HorizontalAlignment = HorizontalAlignment.Left;
        clear.Click += (_, _) => { _logLines.Clear(); _log.Text = ""; };
        _logPanel.Children.Add(clear);
        DockPanel.SetDock(_logPanel, Dock.Bottom);
        pane.Children.Add(_logPanel);
        _description[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("MutedBrush");
        _parameterNote[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("MutedBrush");
        _log[!BackgroundProperty] = new DynamicResourceExtension("PanelBrush");
        var detail = new StackPanel { Children = { _title, _description, _parameterNote, _presets!.ProcessorRow, _params } };
        _title.Spaced(DialogMetrics.MidiHeadGap);
        _description.Spaced(DialogMetrics.MidiNoteGap);
        var viewer = new ScrollViewer { Content = detail, Name = "DetailViewer", ClipToBounds = true, VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        _detailViewer = viewer;
        MidiSharedRows.ConstrainToViewport(_title, viewer);
        MidiSharedRows.ConstrainToViewport(_description, viewer);
        MidiSharedRows.ConstrainToViewport(_parameterNote, viewer);
        MidiSharedRows.ConstrainToViewport(_presets.ProcessorRow, viewer);
        pane.Children.Add(viewer);
        return pane;
    }

    private void WireEvents()
    {
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            if (!_searchDirty) return;
            _searchDirty = false;
            ApplySearch();
        };
        _search.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            if (_searchDebounce.IsEnabled) { _searchDirty = true; return; }   // a trailing pass is already queued
            ApplySearch();
            _searchDebounce.Start();
        };
        _search.AddHandler(KeyDownEvent, (_, e) => SearchKey(e), global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _catalog.DoubleTapped += (_, _) => AddSelected();
        _catalog.SelectionChanged += (_, _) =>
        {
            if (_catalog.SelectedItem is MidiProcessorCatalog.SearchHit hit) MidiSharedRows.Tip(_catalog, hit.Param?.Tip is { Length: > 0 } tip ? tip : hit.Info.Description);
        };
        _add.Click += (_, _) => AddSelected();
        _list.SelectionChanged += (_, _) => { if (!_building) ShowSelected(); };
        _list.KeyDown += (_, e) => { if (e.Key == Key.Delete) RemoveSelected(); };
        _up.Click += (_, _) => Move(-1);
        _down.Click += (_, _) => Move(1);
        _remove.Click += (_, _) => RemoveSelected();
    }

    private void SearchKey(KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { AddSelected(); e.Handled = true; }
        else if (e.Key == Key.Down && _catalog.ItemCount > 0) { _catalog.SelectedIndex = ScoreEditorGeometry.ClampIndex(_catalog.SelectedIndex + 1, _catalog.ItemCount); e.Handled = true; }
        else if (e.Key == Key.Up && _catalog.ItemCount > 0) { _catalog.SelectedIndex = ScoreEditorGeometry.ClampIndex(_catalog.SelectedIndex - 1, _catalog.ItemCount); e.Handled = true; }
    }

    // ---------- list ----------

    private void AddSelected()
    {
        FlushSearch();
        if (_catalog.SelectedItem is not MidiProcessorCatalog.SearchHit hit) return;
        var info = hit.Info;
        if (hit.Param is not null)
        {
            // A parameter match: reuse the selected processor of that type, else any of that type, else add one.
            var existing = Selected is { } s && s.Type == info.Type ? s : Procs.FirstOrDefault(p => p.Type == info.Type);
            _jumpKey = hit.Param.Key;
            if (existing is not null)
            {
                if (Selected == existing) ShowSelected(); else _list.SelectedIndex = Procs.IndexOf(existing);
                return;
            }
        }
        if (!MidiProcessingListPresenter.CanAdd(Procs.Count)) { _jumpKey = null; return; }
        int? selectedIndex = Selected is { } sel ? Procs.IndexOf(sel) : null;
        var created = MidiProcessorCatalog.Create(info);
        _edit(() => Procs.Insert(MidiProcessingListPresenter.InsertIndex(selectedIndex, Procs.Count), created));
        RefreshList(Procs.IndexOf(created));
    }

    private void RemoveSelected()
    {
        if (Selected is not { } p) return;
        var index = _list.SelectedIndex;
        _edit(() => Procs.Remove(p));
        RefreshList(index);
    }

    private void Move(int delta)
    {
        if (Selected is not { } p) return;
        var from = Procs.IndexOf(p);
        if (MidiProcessingListPresenter.MoveTarget(from, delta, Procs.Count) is not { } to) return;
        _edit(() => { Procs.RemoveAt(from); Procs.Insert(to, p); });
        RefreshList(to);
    }

    /// <summary>Rebuilds the rows from the slot's processors and selects <paramref name="select"/> (clamped into the list).</summary>
    private void RefreshList(int select)
    {
        _building = true;
        try
        {
            // Perf: virtualized (ItemsSource + recycling template): only visible rows build controls.
            _list.ItemsSource = Procs.ToList();
            if (Procs.Count > 0) _list.SelectedIndex = ScoreEditorGeometry.ClampIndex(select, Procs.Count);
        }
        finally { _building = false; }
        ShowSelected();
        UpdateWatch();
    }

    /// <summary>Applies the search text to the catalog and the parameter highlight now.</summary>
    private void ApplySearch()
    {
        _queryWords = (_search.Text ?? "").ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _catalog.ItemsSource = MidiProcessorCatalog.SearchAll(_search.Text ?? "");
        if (_catalog.ItemCount > 0) _catalog.SelectedIndex = 0;
        ApplyFilter();
    }

    /// <summary>Runs a queued trailing search now (Enter, Add: the list shows the latest text).</summary>
    private void FlushSearch()
    {
        _searchDebounce.Stop();
        if (!_searchDirty) return;
        _searchDirty = false;
        ApplySearch();
    }

    private Control ProcessorRow(PluginMidiProcessor? proc)
    {
        var info = proc is null ? null : MidiProcessorCatalog.Find(proc.Type);
        var check = new CheckBox { IsChecked = proc?.Enabled == true, VerticalAlignment = VerticalAlignment.Center, Name = "ProcessorTick" };
        check.Spaced(DialogMetrics.MidiCheckGap);
        MidiSharedRows.Tip(check, "Enabled (untick to bypass)");
        check.IsCheckedChanged += (_, _) =>
        {
            if (_building || proc is null || check.IsChecked == proc.Enabled) return;
            _edit(() => proc.Enabled = check.IsChecked == true);
            UpdateWatch();
        };
        var label = new TextBlock
        {
            Text = MidiProcessingListPresenter.RowLabel(proc?.Type ?? "", info?.Name), VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, ClipToBounds = true, [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary"),
        };
        MidiSharedRows.Tip(label, MidiProcessingListPresenter.RowLabel(proc?.Type ?? "", info?.Name));
        var row = new DockPanel { ClipToBounds = true };
        row.Spaced(DialogMetrics.MidiFrameGap);
        DockPanel.SetDock(check, Dock.Left);
        row.Children.Add(check);
        row.Children.Add(label);
        if (info is not null) MidiSharedRows.Tip(row, info.Description);
        return row;
    }

    // ---------- detail ----------

    private void ShowSelected()
    {
        var selected = Selected;
        var listButtons = MidiProcessingListPresenter.ButtonStates(selected is null ? null : _list.SelectedIndex, Procs.Count);
        _up.IsEnabled = listButtons.Up;
        _down.IsEnabled = listButtons.Down;
        _remove.IsEnabled = listButtons.Remove;
        _presets?.Show(selected);
        _params.Children.Clear(); _elems.Clear(); _mapRows = null;
        _editors?.Reset();
        _learn.Stop();
        _parameterNote.IsVisible = false;
        if (selected is null || MidiProcessorCatalog.Find(selected.Type) is not { } info)
        {
            (_title.Text, _description.Text) = MidiProcessingListPresenter.UnknownDetail(selected?.Type, _slot?.Type != PluginSlotType.Instrument && Procs.Count > 0);
            UpdateWatch();
            return;
        }
        _title.Text = info.Name;
        _description.Text = info.Description;
        var values = MidiProcessorCatalog.ParseParams(selected, info);
        _editors!.Viewer = _detailViewer;
        if (info.Type == "noteMap") _mapRows = new MidiNoteMapRows(this, values, (key, name) => { _edit(() => { values[key] = name; selected.Params = values.ToJsonString(); }); _mapRows?.Refresh(); }) { Viewer = _detailViewer };
        foreach (var group in new[] { "Applies to", "Main" })
        {
            var ps = info.Params.Where(p => p.Group == group).ToList();
            if (ps.Count == 0) continue;
            var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
            if (_detailViewer is { } viewer) MidiSharedRows.ConstrainToViewport(wrap, viewer);
            foreach (var p in ps)
            {
                var content = p.Kind == MidiParamKind.MapPreset && _mapRows is not null ? _mapRows.Row(p) : _editors!.Build(selected, values, p);
                var box = new Border { Child = content, BorderThickness = DialogMetrics.MidiFrameBorder, CornerRadius = new CornerRadius(DialogMetrics.MidiFrameCorner), BorderBrush = AvaloniaPalette.TransparentBrush };
                _elems[p.Key] = (box, p);
                wrap.Children.Add(box);
            }
            var header = Heading(group);
            if (group == "Main" && info.Params.Any(p => p.Group == "Applies to")) header.Spaced(DialogMetrics.MidiSectionGap); else header.Spaced(DialogMetrics.MidiHeadGap);
            _params.Children.Add(header);
            _params.Children.Add(wrap);
        }
        if (_detailViewer is { } scrolled && _editors!.NoteEditor is { } noted) MidiSharedRows.ConstrainToViewport(noted, scrolled);
        ApplyFilter();
        UpdateWatch();
        if (_jumpKey is { } key) { _jumpKey = null; Highlight(key); }
    }

    private static string ParamText(MidiParamInfo p) =>
        $"{p.Label} {p.Tip} {(p.Choices is null ? "" : string.Join(' ', p.Choices.Select(c => c.Item2)))} {p.Key}".ToLowerInvariant();

    /// <summary>While a search is active, parameters that match it get an accent outline and the others are dimmed (no rebuild, no timer).</summary>
    private void ApplyFilter()
    {
        foreach (var (box, p) in _elems.Values)
        {
            var match = _queryWords.Length > 0 && _queryWords.All(w => ParamText(p).Contains(w, StringComparison.Ordinal));
            box.Opacity = _queryWords.Length == 0 || match ? 1 : DimmedOpacity;
            if (match) box[!Border.BorderBrushProperty] = new DynamicResourceExtension("AccentBrush");
            else box.BorderBrush = AvaloniaPalette.TransparentBrush;
        }
    }

    /// <summary>Brings a parameter into view with an accent outline (the next search or selection clears it).</summary>
    private void Highlight(string key)
    {
        if (!_elems.TryGetValue(key, out var entry)) return;
        entry.Box.Opacity = 1;
        entry.Box[!Border.BorderBrushProperty] = new DynamicResourceExtension("AccentBrush");
        entry.Box.BorderThickness = DialogMetrics.MidiHighlightBorder;
        AvaloniaUiThread.ForApplication().Post(() => entry.Box.BringIntoView(), UiPriority.Loaded);
    }

    // ---------- log (see MidiProcessingWindow.Log.cs) ----------

    // ---------- look ----------

    private static Button Btn(string text, string? tip, string name)
    {
        var button = new Button { Content = text, Name = name, [!Button.ForegroundProperty] = new DynamicResourceExtension("TextPrimary") };
        DialogMetrics.WithPadding(button, DialogMetrics.MidiItemPad);
        button.Spaced(DialogMetrics.MidiButtonGap);
        if (tip is not null) MidiSharedRows.Tip(button, tip);
        return button;
    }

    private static TextBlock Heading(string text)
    {
        var head = new TextBlock
        {
            Text = text, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("AccentBrush"),
        };
        head.Spaced(DialogMetrics.MidiHeadGap);
        return head;
    }

    private static Border Frame(Control child)
    {
        var frame = new Border
        {
            Child = child, ClipToBounds = true,
            BorderThickness = DialogMetrics.MidiFrameBorder,
            [!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush"), [!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBrush"),
        };
        return frame;
    }
}
