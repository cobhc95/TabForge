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
/// Size: over 300 lines because the window's three panes share one set of controls; the parameter controls and presets are in their own classes.
/// Tests: TabForge.Avalonia.Tests.MidiProcessingWindowTests.
/// </summary>
public sealed class MidiProcessingWindow : Window
{
    private const int MaxLogLines = 300, RawInputStage = 250;
    private const double CatalogWidth = 250, ListWidth = 250, DimmedOpacity = 0.4;

    private readonly IFxChainHost _host;
    private readonly TrackModel _track;
    private readonly PluginSlot _slot;
    private readonly Action<Action> _edit;
    private readonly MidiLearn _learn = new();
    private readonly MidiParamEditors _editors;
    private readonly MidiPresetBars _presets;
    private readonly TextBox _search = new() { Margin = new Thickness(0, 0, 0, 6), Name = "SearchBox" };
    private readonly ListBox _catalog = new() { BorderThickness = new Thickness(0), Name = "CatalogList" };
    private readonly ListBox _list = new() { BorderThickness = new Thickness(0), Name = "ProcessorList" };
    private readonly TextBlock _title = new() { FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Name = "TitleText" };
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8), Name = "DescriptionText" };
    private readonly TextBlock _parameterNote = new() { TextWrapping = TextWrapping.Wrap, Name = "ParameterNote", IsVisible = false };
    private readonly StackPanel _params = new() { Name = "ParamPanel" };
    private readonly Dictionary<string, (Border Box, MidiParamInfo Param)> _elems = new();
    private Expander? _advanced;
    private MidiNoteMapRows? _mapRows;
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
    private readonly TextBox _log = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), FontSize = 11, Height = 150, Name = "LogBox" };
    private readonly StackPanel _logPanel = new() { IsVisible = false, Name = "LogPanel" };
    private readonly List<string> _logLines = new();
    private readonly TextBlock _status = ToolWindowParts.Status("StatusLine");
    private bool _building, _watching;

    /// <summary>The questions and messages of this window; a test replaces members to answer without a modal box.</summary>
    public FxChainDialogs Dialogs { get; } = new();

    /// <param name="host">The song and engine the window works with.</param>
    /// <param name="track">The track that holds the plug-in.</param>
    /// <param name="slot">The plug-in whose MIDI processors the window edits.</param>
    /// <param name="edit">Runs a change of the slot as one undo step with the engine update.</param>
    public MidiProcessingWindow(IFxChainHost host, TrackModel track, PluginSlot slot, Action<Action> edit)
    {
        (_host, _track, _slot, _edit) = (host, track, slot, edit);
        _slot.MidiProcessors ??= new List<PluginMidiProcessor>();
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Title = $"MIDI processing: {slot.Name}";
        Width = 1020; Height = 620; MinWidth = 760; MinHeight = 420;
        ShowInTaskbar = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        _editors = new MidiParamEditors(host, track, slot, edit, _learn);
        _presets = new MidiPresetBars(this, () => Dialogs, () => Procs, list => { _edit(() => { Procs.Clear(); Procs.AddRange(list); }); RefreshList(0); },
            (proc, json) => { _edit(() => proc.Params = json); ShowSelected(); });
        Content = BuildRoot();
        WireEvents();
        _catalog.ItemsSource = MidiProcessorCatalog.SearchAll("");
        if (_catalog.ItemCount > 0) _catalog.SelectedIndex = 0;
        if (host.Engine is { } engine) { engine.MidiLogReceived += OnLog; engine.ChainLoaded += UpdateWatch; }
        _learn.Changed += UpdateWatch;
        Closed += (_, _) => OnClosed();
        RefreshList(0);
    }

    /// <summary>A sample plug-in with three processors: a known transpose, an unknown type (the row says so) and an enabled log.</summary>
    public static PluginSlot SampleSlot() => new()
    {
        Name = "Sample synth",
        Type = PluginSlotType.Instrument,
        MidiProcessors =
        [
            new PluginMidiProcessor { Type = "transpose" },
            new PluginMidiProcessor { Type = "legacyFilter" },
            new PluginMidiProcessor { Type = "log" },
        ],
    };

    private List<PluginMidiProcessor> Procs => _slot.MidiProcessors;
    private PluginMidiProcessor? Selected => _list.SelectedItem as PluginMidiProcessor;
    private bool HasLog => Procs.Any(p => p.Enabled && p.Type == "log");

    // ---------- layout ----------

    private Control BuildRoot()
    {
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        top.Children.Add(new TextBlock { Text = "Presets", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary") });
        top.Children.Add(_presets.ListBox);
        top.Children.Add(_presets.SaveList);
        top.Children.Add(_presets.DeleteList);
        var body = new Grid { ColumnDefinitions = ColumnDefinitions.Parse($"{CatalogWidth},{ListWidth},*") };
        var catalog = CatalogPane(); Grid.SetColumn(catalog, 0);
        var list = ListPane(); Grid.SetColumn(list, 1);
        var detail = DetailPane(); Grid.SetColumn(detail, 2);
        body.Children.Add(catalog); body.Children.Add(list); body.Children.Add(detail);
        var root = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,*,Auto"), Margin = new Thickness(10) };
        Grid.SetRow(top, 0); Grid.SetRow(body, 1); Grid.SetRow(_status, 2);
        root.Children.Add(top); root.Children.Add(body); root.Children.Add(_status);
        return root;
    }

    private DockPanel CatalogPane()
    {
        var pane = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        var heading = Heading("Add a processor");
        DockPanel.SetDock(heading, Dock.Top);
        DockPanel.SetDock(_search, Dock.Top);
        DockPanel.SetDock(_add, Dock.Bottom);
        ToolTip.SetTip(_search, "Search processors and every parameter (e.g. \"velocity\", \"jitter\", \"drum map\"). Enter adds the processor or jumps to the parameter");
        _add.HorizontalAlignment = HorizontalAlignment.Left;
        _add.Margin = new Thickness(0, 6, 0, 0);
        _catalog.ItemTemplate = new FuncDataTemplate<MidiProcessorCatalog.SearchHit>((hit, _) => new TextBlock { Text = hit?.Display ?? "", TextTrimming = TextTrimming.CharacterEllipsis }, supportsRecycling: true);
        // Perf: the catalog is virtualized (recycling template over a VirtualizingStackPanel): only the visible hits build controls.
        _catalog.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
        pane.Children.Add(heading); pane.Children.Add(_search); pane.Children.Add(_add); pane.Children.Add(Frame(_catalog));
        return pane;
    }

    private DockPanel ListPane()
    {
        var pane = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        var heading = Heading("In order (top runs first)");
        DockPanel.SetDock(heading, Dock.Top);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0), Children = { _up, _down, _remove } };
        DockPanel.SetDock(buttons, Dock.Bottom);
        _list.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
        _list.ItemTemplate = new FuncDataTemplate<PluginMidiProcessor>((proc, _) => ProcessorRow(proc), supportsRecycling: true);
        pane.Children.Add(heading); pane.Children.Add(buttons); pane.Children.Add(Frame(_list));
        return pane;
    }

    private DockPanel DetailPane()
    {
        var pane = new DockPanel();
        _logPanel.Children.Add(Heading("MIDI log (this window watches while it is open)"));
        _logPanel.Children.Add(_log);
        var clear = Btn("Clear log", null, "ClearLogButton");
        clear.Margin = new Thickness(0, 4, 0, 0);
        clear.HorizontalAlignment = HorizontalAlignment.Left;
        clear.Click += (_, _) => { _logLines.Clear(); _log.Text = ""; };
        _logPanel.Children.Add(clear);
        DockPanel.SetDock(_logPanel, Dock.Bottom);
        pane.Children.Add(_logPanel);
        _description[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("MutedBrush");
        _parameterNote[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("MutedBrush");
        _log[!BackgroundProperty] = new DynamicResourceExtension("PanelBrush");
        var detail = new StackPanel { Children = { _title, _description, _parameterNote, _presets.ProcessorRow, _params } };
        pane.Children.Add(new ScrollViewer { Content = detail, VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 8, 0) });
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
            if (_catalog.SelectedItem is MidiProcessorCatalog.SearchHit hit) ToolTip.SetTip(_catalog, hit.Param?.Tip is { Length: > 0 } tip ? tip : hit.Info.Description);
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
        var check = new CheckBox { IsChecked = proc?.Enabled == true, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Name = "ProcessorTick" };
        ToolTip.SetTip(check, "Enabled (untick to bypass)");
        check.IsCheckedChanged += (_, _) =>
        {
            if (_building || proc is null || check.IsChecked == proc.Enabled) return;
            _edit(() => proc.Enabled = check.IsChecked == true);
            UpdateWatch();
        };
        var label = new TextBlock
        {
            Text = MidiProcessingListPresenter.RowLabel(proc?.Type ?? "", info?.Name), VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary"),
        };
        var row = new DockPanel { Margin = new Thickness(2) };
        DockPanel.SetDock(check, Dock.Left);
        row.Children.Add(check);
        row.Children.Add(label);
        if (info is not null) ToolTip.SetTip(row, info.Description);
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
        _presets.Show(selected);
        _params.Children.Clear(); _elems.Clear(); _advanced = null; _mapRows = null;
        _editors.Reset();
        _learn.Stop();
        _parameterNote.IsVisible = false;
        if (selected is null || MidiProcessorCatalog.Find(selected.Type) is not { } info)
        {
            (_title.Text, _description.Text) = MidiProcessingListPresenter.UnknownDetail(selected?.Type, _slot.Type != PluginSlotType.Instrument && Procs.Count > 0);
            UpdateWatch();
            return;
        }
        _title.Text = info.Name;
        _description.Text = info.Description;
        var values = MidiProcessorCatalog.ParseParams(selected, info);
        if (info.Type == "noteMap") _mapRows = new MidiNoteMapRows(this, values, (key, name) => { _edit(() => { values[key] = name; selected.Params = values.ToJsonString(); }); _mapRows?.Refresh(); });
        foreach (var group in new[] { "Applies to", "Main", "Advanced" })
        {
            var ps = info.Params.Where(p => p.Group == group).ToList();
            if (ps.Count == 0) continue;
            var wrap = new WrapPanel();
            foreach (var p in ps)
            {
                var content = p.Kind == MidiParamKind.MapPreset && _mapRows is not null ? _mapRows.Row(p) : _editors.Build(selected, values, p);
                var box = new Border { Child = content, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(2), BorderBrush = AvaloniaPalette.TransparentBrush };
                _elems[p.Key] = (box, p);
                wrap.Children.Add(box);
            }
            if (group == "Advanced")
            {
                _advanced = new Expander { Header = $"Advanced ({ps.Count})", IsExpanded = false, Content = wrap, Margin = new Thickness(0, 8, 0, 0), Name = "AdvancedExpander", [!ForegroundProperty] = new DynamicResourceExtension("AccentBrush") };
                _params.Children.Add(_advanced);
            }
            else
            {
                var header = Heading(group);
                header.Margin = new Thickness(0, group == "Main" && info.Params.Any(p => p.Group == "Applies to") ? 10 : 0, 0, 2);
                _params.Children.Add(header);
                _params.Children.Add(wrap);
            }
        }
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
            if (match && p.Group == "Advanced" && _advanced is not null) _advanced.IsExpanded = true;
        }
    }

    /// <summary>Brings a parameter into view with an accent outline (the next search or selection clears it).</summary>
    private void Highlight(string key)
    {
        if (!_elems.TryGetValue(key, out var entry)) return;
        if (entry.Param.Group == "Advanced" && _advanced is not null) _advanced.IsExpanded = true;
        entry.Box.Opacity = 1;
        entry.Box[!Border.BorderBrushProperty] = new DynamicResourceExtension("AccentBrush");
        entry.Box.BorderThickness = new Thickness(2);
        AvaloniaUiThread.ForApplication().Post(() => entry.Box.BringIntoView(), UiPriority.Loaded);
    }

    // ---------- log ----------

    private void UpdateWatch()
    {
        _logPanel.IsVisible = HasLog;
        var want = HasLog || _learn.Active;
        if (want == _watching) return;
        _watching = want;
        _host.Engine?.WatchMidiLog(_track, want);
    }

    private void OnLog(int engineSlot, IReadOnlyList<MidiLogLine> lines)
    {
        if (!_watching || engineSlot != _host.Engine?.SlotOf(_track)) return;
        foreach (var l in lines)
        {
            if (l.Stage == RawInputStage)   // raw input the chain saw: only used by Learn
            {
                if (_learn.Active && (l.Status & 0xF0) == 0x90 && l.Data2 > 0) _learn.Sink?.Invoke(l.Data1);
                continue;
            }
            if (HasLog) _logLines.Add(Describe(l));
        }
        if (!HasLog) return;
        if (_logLines.Count > MaxLogLines) _logLines.RemoveRange(0, _logLines.Count - MaxLogLines);
        _log.Text = string.Join('\n', _logLines);
        _log.CaretIndex = _log.Text.Length;
    }

    /// <summary>One log line: song time, channel, decoded message, and the position of the log processor that saw it.</summary>
    public static string Describe(MidiLogLine l)
    {
        var kind = l.Status & 0xF0; var ch = (l.Status & 0x0F) + 1;
        string note(int n) => $"{MusicTheoryService.NoteName(n)} ({n})";
        var what = l.Status >= 0xF0 ? $"System {l.Status:X2} {l.Data1} {l.Data2}" : kind switch
        {
            0x90 when l.Data2 > 0 => $"ch{ch,-2} Note on   {note(l.Data1)} vel {l.Data2}",
            0x90 or 0x80 => $"ch{ch,-2} Note off  {note(l.Data1)}",
            0xA0 => $"ch{ch,-2} Poly pressure {note(l.Data1)} {l.Data2}",
            0xB0 => $"ch{ch,-2} CC {l.Data1} = {l.Data2}",
            0xC0 => $"ch{ch,-2} Program {l.Data1}",
            0xD0 => $"ch{ch,-2} Channel pressure {l.Data1}",
            0xE0 => $"ch{ch,-2} Pitch bend {(l.Data2 << 7 | l.Data1) - 8192}",
            _ => $"{l.Status:X2} {l.Data1} {l.Data2}",
        };
        return $"{l.Time,8:0.000}s  {what}   @{l.Stage + 1}";
    }

    private void OnClosed()
    {
        _searchDebounce.Stop();
        try { if (_watching) _host.Engine?.WatchMidiLog(_track, false); }
        finally
        {
            if (_host.Engine is { } engine) { engine.MidiLogReceived -= OnLog; engine.ChainLoaded -= UpdateWatch; }
        }
    }

    // ---------- look ----------

    private static Button Btn(string text, string? tip, string name)
    {
        var button = new Button { Content = text, Name = name, Padding = new Thickness(10, 3), Margin = new Thickness(0, 0, 4, 0), [!Button.ForegroundProperty] = new DynamicResourceExtension("TextPrimary") };
        if (tip is not null) ToolTip.SetTip(button, tip);
        return button;
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 4),
        [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("AccentBrush"),
    };

    private static Border Frame(Control child) => new()
    {
        BorderThickness = new Thickness(1), Child = child,
        [!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush"), [!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBrush"),
    };
}
