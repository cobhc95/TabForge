using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Plugins;
using EM = TabForge.AudioEngine.Midi;

namespace TabForge.Views;

/// <summary>
/// One plug-in's MIDI processing list (opened from the wiring window's "Configure MIDI input…" and by the Track.MidiProcessing hotkey):
/// a searchable catalog on the left, the ordered list in the middle (tick to enable, move, remove), and the selected processor's
/// parameters on the right, built from <see cref="MidiProcessorCatalog"/>. Every change goes through the FX window's Edit (undo +
/// engine update); knob drags are one undo step and only send a light engine message until they end. The log view polls nothing:
/// the engine pushes its entries about 20 times a second, and only while a log processor is in the list and this window is open.
/// </summary>
public sealed class MidiProcessingWindow : Window
{
    private const int MaxProcessors = 32;
    private const int MaxLogLines = 300;

    private readonly IFxChainHost _host;
    private readonly TrackModel _track;
    private readonly PluginSlot _slot;
    private readonly Action<Action> _edit;
    private readonly TextBox _search = new() { Margin = new Thickness(0, 0, 0, 6), ToolTip = "Search processors and every parameter (e.g. \"velocity\", \"jitter\", \"drum map\"). Enter adds the processor or jumps to the parameter" };
    private readonly ListBox _catalog = new() { BorderThickness = new Thickness(0) };
    private readonly ListBox _list = new() { BorderThickness = new Thickness(0) };
    private readonly TextBlock _title = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8) };
    private readonly StackPanel _params = new();
    private readonly Dictionary<string, (Border Box, MidiParamInfo Param)> _elems = new();
    private Expander? _advanced;
    private string[] _queryWords = Array.Empty<string>();
    private bool _learning;
    private Action<int>? _learnSink;
    private Border? _noteEditor;
    private readonly ComboBox _presets = new() { Width = 230, ToolTip = "Load a preset: replaces this plug-in's whole processor list (undo with Ctrl+Z in the FX window)" };
    private readonly Button _savePreset = new() { Content = "Save preset…", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Save the whole processor list as a preset" };
    private readonly Button _deletePreset = new() { Content = "Delete", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0), IsEnabled = false, ToolTip = "Delete the selected saved preset" };
    private readonly TextBox _log = new() { IsReadOnly = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 11, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 150 };
    private readonly StackPanel _logPanel = new() { Visibility = Visibility.Collapsed };
    private readonly List<string> _logLines = new();
    private readonly Button _up = Small("▲", "Move up (earlier in the chain)"), _down = Small("▼", "Move down (later in the chain)"), _remove = Small("Remove", "Remove the selected processor (Delete)");
    private bool _building, _dragging, _watching;

    public MidiProcessingWindow(IFxChainHost host, TrackModel track, PluginSlot slot, Window? owner, Action<Action> edit)
    {
        _host = host; _track = track; _slot = slot; _edit = edit;
        _slot.MidiProcessors ??= new List<PluginMidiProcessor>();
        Owner = owner;
        Title = $"MIDI processing: {slot.Name}";
        Width = 1020; Height = 620; MinWidth = 760; MinHeight = 420;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");

        var grid = new Grid { Margin = new Thickness(10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left: catalog with search.
        var left = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        var searchHeader = Header("Add a processor");
        DockPanel.SetDock(searchHeader, Dock.Top);
        left.Children.Add(searchHeader);
        DockPanel.SetDock(_search, Dock.Top);
        left.Children.Add(_search);
        var add = new Button { Content = "Add ›", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Add the selected processor after the selected one in the list" };
        DockPanel.SetDock(add, Dock.Bottom);
        left.Children.Add(add);
        left.Children.Add(Frame(_catalog));
        grid.Children.Add(left);

        // Middle: the ordered list.
        var middle = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        Grid.SetColumn(middle, 1);
        var listHeader = Header("In order (top runs first)");
        DockPanel.SetDock(listHeader, Dock.Top);
        middle.Children.Add(listHeader);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        buttons.Children.Add(_up); buttons.Children.Add(_down); buttons.Children.Add(_remove);
        middle.Children.Add(buttons);
        middle.Children.Add(Frame(_list));
        grid.Children.Add(middle);

        // Right: the selected processor's parameters, and the log.
        var right = new DockPanel();
        Grid.SetColumn(right, 2);
        _logPanel.Children.Add(Header("MIDI log (this window watches while it is open)"));
        _logPanel.Children.Add(_log);
        var clearLog = new Button { Content = "Clear log", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        clearLog.Click += (_, _) => { _logLines.Clear(); _log.Clear(); };
        _logPanel.Children.Add(clearLog);
        DockPanel.SetDock(_logPanel, Dock.Bottom);
        right.Children.Add(_logPanel);
        var detail = new StackPanel();
        detail.Children.Add(_title);
        detail.Children.Add(_description);
        detail.Children.Add(_params);
        right.Children.Add(new ScrollViewer { Content = detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 8, 0) });
        grid.Children.Add(right);
        var top = new DockPanel { Margin = new Thickness(10, 10, 10, 0), LastChildFill = false };
        DockPanel.SetDock(top, Dock.Top);
        var presetLabel = Header("Presets");
        presetLabel.Margin = new Thickness(0, 0, 8, 0); presetLabel.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(presetLabel); top.Children.Add(_presets); top.Children.Add(_savePreset); top.Children.Add(_deletePreset);
        var root = new DockPanel();
        root.Children.Add(top); root.Children.Add(grid);
        Content = root;
        RefreshPresets(null);
        _presets.SelectionChanged += (_, _) => OnPresetPicked();
        _savePreset.Click += (_, _) => SavePreset();
        _deletePreset.Click += (_, _) => DeletePreset();

        _description.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _log.SetResourceReference(BackgroundProperty, "PanelBrush");
        _log.SetResourceReference(ForegroundProperty, "TextBrush");

        _search.TextChanged += (_, _) =>
        {
            _queryWords = _search.Text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            _catalog.ItemsSource = MidiProcessorCatalog.SearchAll(_search.Text);
            if (_catalog.Items.Count > 0) _catalog.SelectedIndex = 0;
            ApplyFilter();
        };
        _search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { AddSelected(); e.Handled = true; }
            else if (e.Key == Key.Down && _catalog.Items.Count > 0) { _catalog.SelectedIndex = Math.Min(_catalog.Items.Count - 1, _catalog.SelectedIndex + 1); e.Handled = true; }
            else if (e.Key == Key.Up && _catalog.Items.Count > 0) { _catalog.SelectedIndex = Math.Max(0, _catalog.SelectedIndex - 1); e.Handled = true; }
        };
        _catalog.ItemTemplate = null;
        _catalog.DisplayMemberPath = nameof(MidiProcessorCatalog.SearchHit.Display);
        _catalog.ItemsSource = MidiProcessorCatalog.SearchAll("");
        _catalog.ItemContainerStyle = ItemStyle();
        _catalog.MouseDoubleClick += (_, e) => { if (FindItem(e.OriginalSource) is not null) AddSelected(); };
        _catalog.SelectionChanged += (_, _) => { if (_catalog.SelectedItem is MidiProcessorCatalog.SearchHit hit) _catalog.ToolTip = hit.Param?.Tip is { Length: > 0 } tip ? tip : hit.Info.Description; };
        add.Click += (_, _) => AddSelected();
        _list.SelectionChanged += (_, _) => { if (!_building) ShowSelected(); };
        _list.KeyDown += (_, e) => { if (e.Key == Key.Delete) RemoveSelected(); };
        AttachDragReorder();
        _up.Click += (_, _) => Move(-1);
        _down.Click += (_, _) => Move(1);
        _remove.Click += (_, _) => RemoveSelected();

        _host.Engine.MidiLogReceived += OnLog;
        _host.Engine.ChainLoaded += UpdateWatch;
        OwnerActivation.Attach(this);
        Closed += (_, _) =>
        {
            try { if (_watching) _host.Engine.WatchMidiLog(_track, false); }
            finally
            {
                _host.Engine.MidiLogReceived -= OnLog;
                _host.Engine.ChainLoaded -= UpdateWatch;
            }
        };
        RefreshList(0);
        _search.Focus();
    }

    // Capture hooks (--capture scripts only): build a processor list and type a search without a mouse.
    internal void CaptureAdd(string type)
    {
        if (MidiProcessorCatalog.Find(type) is not { } info) throw new InvalidOperationException($"unknown MIDI processor '{type}'");
        var created = MidiProcessorCatalog.Create(info);
        Procs.Add(created);
        RefreshList(Procs.IndexOf(created));
    }
    internal void CaptureSelect(int index) { if (index >= 0 && index < _list.Items.Count) _list.SelectedIndex = index; }
    internal void CaptureSearch(string text) => _search.Text = text;
    internal void CaptureParam(string key, double value)
    {
        if (Selected is not { } proc || MidiProcessorCatalog.Find(proc.Type) is not { } info) throw new InvalidOperationException("no processor selected");
        var values = MidiProcessorCatalog.ParseParams(proc, info);
        values[key] = value;
        Store(proc, values);
        ShowSelected();
    }

    private List<PluginMidiProcessor> Procs => _slot.MidiProcessors;
    private PluginMidiProcessor? Selected => _list.SelectedItem is ListBoxItem { Tag: PluginMidiProcessor p } ? p : null;

    // ---------- list ----------
    private void AddSelected()
    {
        if (_catalog.SelectedItem is not MidiProcessorCatalog.SearchHit hit) return;
        var info = hit.Info;
        if (hit.Param is not null)
        {
            // A parameter match: reuse the selected processor of that type, else any of that type, else add one.
            var existing = Selected is { } s && s.Type == info.Type ? s : Procs.FirstOrDefault(p => p.Type == info.Type);
            if (existing is not null)
            {
                _jumpKey = hit.Param.Key;
                if (Selected == existing) { ShowSelected(); } else _list.SelectedIndex = Procs.IndexOf(existing);
                return;
            }
            _jumpKey = hit.Param.Key;
        }
        if (Procs.Count >= MaxProcessors) { _jumpKey = null; return; }
        var index = Selected is { } sel ? Procs.IndexOf(sel) + 1 : Procs.Count;
        var created = MidiProcessorCatalog.Create(info);
        _edit(() => Procs.Insert(Math.Clamp(index, 0, Procs.Count), created));
        RefreshList(Procs.IndexOf(created));
    }

    private string? _jumpKey;

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
        var from = Procs.IndexOf(p); var to = from + delta;
        if (from < 0 || to < 0 || to >= Procs.Count) return;
        _edit(() => { Procs.RemoveAt(from); Procs.Insert(to, p); });
        RefreshList(to);
    }

    // ---------- drag to reorder (click, hold, drag; a line shows where it will land; one undo step per drop) ----------
    private Point _dragStart;
    private ListBoxItem? _indicated;

    private void ClearIndicator()
    {
        if (_indicated is null) return;
        _indicated.BorderThickness = new Thickness(0);
        _indicated = null;
    }

    /// <summary>The row under the pointer and whether the drop lands after it (lower half); null over empty space (= the end).</summary>
    private (ListBoxItem? Item, bool After) DropTarget(DragEventArgs e)
    {
        var item = FindItem(e.OriginalSource);
        if (item is null) return (null, true);
        return (item, e.GetPosition(item).Y > item.ActualHeight / 2);
    }

    private void AttachDragReorder()
    {
        _list.PreviewMouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(_list);
        _list.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || Selected is not { } proc) return;
            var delta = e.GetPosition(_list) - _dragStart;
            if (Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance && Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance) return;
            for (var n = e.OriginalSource as DependencyObject; n is not null && n != _list; n = System.Windows.Media.VisualTreeHelper.GetParent(n) ?? (n as FrameworkElement)?.Parent)
                if (n is CheckBox or System.Windows.Controls.Primitives.ScrollBar) return;
            try { DragDrop.DoDragDrop(_list, new DataObject(typeof(PluginMidiProcessor), proc), DragDropEffects.Move); }
            finally { ClearIndicator(); }
        };
        _list.DragOver += (_, e) =>
        {
            var ok = e.Data.GetDataPresent(typeof(PluginMidiProcessor));
            e.Effects = ok ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
            ClearIndicator();
            if (!ok) return;
            var (item, after) = DropTarget(e);
            item ??= _list.Items.Count > 0 ? _list.Items[^1] as ListBoxItem : null;
            if (item is null) return;
            item.BorderThickness = after ? new Thickness(0, 0, 0, 2) : new Thickness(0, 2, 0, 0);
            item.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            _indicated = item;
        };
        _list.DragLeave += (_, _) => ClearIndicator();
        _list.Drop += (_, e) =>
        {
            ClearIndicator();
            if (e.Data.GetData(typeof(PluginMidiProcessor)) is not PluginMidiProcessor moving) return;
            var (item, after) = DropTarget(e);
            var from = Procs.IndexOf(moving);
            if (from < 0) return;
            var target = item?.Tag is PluginMidiProcessor t ? Procs.IndexOf(t) : Procs.Count - 1;
            if (target < 0) return;
            var insert = after ? target + 1 : target;   // position in the list before removal
            if (insert > from) insert--;
            insert = Math.Clamp(insert, 0, Procs.Count - 1);
            if (insert == from) return;
            _edit(() => { Procs.RemoveAt(from); Procs.Insert(insert, moving); });
            RefreshList(insert);
        };
    }

    private void RefreshList(int select)
    {
        _building = true;
        try
        {
            _list.Items.Clear();
            foreach (var proc in Procs)
            {
                var info = MidiProcessorCatalog.Find(proc.Type);
                var check = new CheckBox { IsChecked = proc.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Enabled (untick to bypass)" };
                var captured = proc;
                check.Click += (_, _) => _edit(() => captured.Enabled = check.IsChecked == true);
                var row = new DockPanel { Margin = new Thickness(2) };
                row.Children.Add(check);
                row.Children.Add(new TextBlock { Text = info?.Name ?? $"{proc.Type} (unknown)", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                _list.Items.Add(new ListBoxItem { Content = row, Tag = proc, ToolTip = info?.Description });
            }
            if (Procs.Count > 0) _list.SelectedIndex = Math.Clamp(select, 0, Procs.Count - 1);
        }
        finally { _building = false; }
        ShowSelected();
        UpdateWatch();
    }

    private void ShowSelected()
    {
        var selected = Selected;
        var index = _list.SelectedIndex;
        _up.IsEnabled = selected is not null && index > 0;
        _down.IsEnabled = selected is not null && index < Procs.Count - 1;
        _remove.IsEnabled = selected is not null;
        _params.Children.Clear(); _elems.Clear(); _advanced = null; _noteEditor = null;
        if (_learning) { _learning = false; _learnSink = null; }
        if (selected is null || MidiProcessorCatalog.Find(selected.Type) is not { } info)
        {
            _title.Text = selected is null ? "No processors yet" : selected.Type;
            _description.Text = selected is null
                ? "Search the catalog on the left and add a processor. They run in order, before the plug-in, on everything it plays: the song, live keys and other tracks routed to it."
                : "This processor type is not known to this version of TabForge; it is kept in the song and skipped.";
            if (_slot.Type != PluginSlotType.Instrument && Procs.Count > 0) _description.Text += " On an effect they shape the MIDI this plug-in and the ones after it receive (the General MIDI synth hears processed MIDI only from a first-slot instrument's list).";
            UpdateWatch();
            return;
        }
        _title.Text = info.Name;
        _description.Text = info.Description;
        var values = MidiProcessorCatalog.ParseParams(selected, info);
        foreach (var group in new[] { "Applies to", "Main", "Advanced" })
        {
            var ps = info.Params.Where(p => p.Group == group).ToList();
            if (ps.Count == 0) continue;
            var wrap = new WrapPanel();
            foreach (var p in ps)
            {
                var box = new Border { Child = BuildParam(selected, info, values, p), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(2), BorderBrush = System.Windows.Media.Brushes.Transparent };
                _elems[p.Key] = (box, p);
                wrap.Children.Add(box);
            }
            if (group == "Advanced")
            {
                _advanced = new Expander { Header = $"Advanced ({ps.Count})", IsExpanded = false, Content = wrap, Margin = new Thickness(0, 8, 0, 0) };
                _advanced.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
                _params.Children.Add(_advanced);
            }
            else
            {
                var header = Header(group);
                header.Margin = new Thickness(0, group == "Main" && info.Params.Any(p => p.Group == "Applies to") ? 10 : 0, 0, 2);
                _params.Children.Add(header);
                _params.Children.Add(wrap);
            }
        }
        if (info.Type == "noteMap") _params.Children.Add(MapButtons());
        ApplyFilter();
        UpdateWatch();
        if (_jumpKey is { } key) { _jumpKey = null; Highlight(key); }
    }

    private static string ParamText(MidiParamInfo p) =>
        $"{p.Label} {p.Tip} {(p.Choices is null ? "" : string.Join(' ', p.Choices.Select(c => c.Label)))} {p.Key}".ToLowerInvariant();

    /// <summary>While a search is active, parameters that match it get an accent outline and the others are dimmed (no rebuild, no timer).</summary>
    private void ApplyFilter()
    {
        foreach (var (box, p) in _elems.Values)
        {
            var match = _queryWords.Length > 0 && _queryWords.All(w => ParamText(p).Contains(w, StringComparison.Ordinal));
            box.Opacity = _queryWords.Length == 0 || match ? 1 : 0.4;
            if (match) box.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            else box.BorderBrush = System.Windows.Media.Brushes.Transparent;
            if (match && p.Group == "Advanced" && _advanced is not null) _advanced.IsExpanded = true;
        }
    }

    /// <summary>Scrolls to a parameter and flashes an accent border that fades out over about two seconds (one animation).</summary>
    private void Highlight(string key)
    {
        if (!_elems.TryGetValue(key, out var entry)) return;
        if (entry.Param.Group == "Advanced" && _advanced is not null) _advanced.IsExpanded = true;
        entry.Box.Opacity = 1;
        var color = (TryFindResource("AccentBrush") as System.Windows.Media.SolidColorBrush)?.Color ?? System.Windows.Media.Colors.Orange;
        var brush = new System.Windows.Media.SolidColorBrush(color);
        entry.Box.BorderThickness = new Thickness(2); entry.Box.Padding = new Thickness(1);
        entry.Box.BorderBrush = brush;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            entry.Box.BringIntoView();
            var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromSeconds(2)) { BeginTime = TimeSpan.FromSeconds(0.4) };
            fade.Completed += (_, _) => { entry.Box.BorderBrush = System.Windows.Media.Brushes.Transparent; entry.Box.BorderThickness = new Thickness(1); entry.Box.Padding = new Thickness(2); ApplyFilter(); };
            brush.BeginAnimation(System.Windows.Media.Brush.OpacityProperty, fade);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ---------- parameters ----------
    private void Store(PluginMidiProcessor proc, JsonObject values) => proc.Params = values.ToJsonString();

    private FrameworkElement BuildParam(PluginMidiProcessor proc, MidiProcessorInfo info, JsonObject values, MidiParamInfo p)
    {
        var item = new StackPanel { Orientation = Orientation.Horizontal, Width = 310, Margin = new Thickness(0, 3, 12, 3) };
        if (p.Kind == MidiParamKind.Check)
        {
            var box = new CheckBox { Content = p.Label, IsChecked = values[p.Key]!.GetValue<double>() >= 0.5, ToolTip = p.Tip.Length > 0 ? p.Tip : null, VerticalAlignment = VerticalAlignment.Center };
            box.Click += (_, _) => _edit(() => { values[p.Key] = box.IsChecked == true ? 1 : 0; Store(proc, values); });
            item.Children.Add(box);
            return item;
        }
        var label = new TextBlock { Text = p.Label, Width = 140, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = p.Tip.Length > 0 ? p.Tip : p.Label };
        label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        item.Children.Add(label);
        switch (p.Kind)
        {
            case MidiParamKind.Number:
            {
                var scale = Math.Max(1, p.Scale);
                var knob = new KnobControl
                {
                    Minimum = p.Min * scale, Maximum = p.Max * scale, DefaultValue = p.Default * scale, Origin = Math.Clamp(0, p.Min, p.Max) * scale, Width = 28, Height = 28, Label = p.Label, DisplayScale = scale,
                    Format = v => p.Format(v / scale), ToolTip = p.Tip.Length > 0 ? p.Tip : p.Label,
                };
                var text = new TextBlock { Width = 90, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                knob.Value = Math.Round(values[p.Key]!.GetValue<double>() * scale);
                text.Text = p.Format(knob.Value / scale);
                knob.EditStarted += (_, _) => { _dragging = true; _host.BeginChainEdit(); };
                knob.ValueChanged += (_, e) =>
                {
                    text.Text = p.Format(e.NewValue / scale);
                    if (_building) return;
                    var v = e.NewValue / scale;
                    if (_dragging)
                    {
                        values[p.Key] = v;
                        Store(proc, values);
                        _host.Engine.SetMidiProcessors(_track, _slot);   // light message while turning; the full update follows on release
                    }
                    else _edit(() => { values[p.Key] = v; Store(proc, values); });   // e.g. double-click reset: its own undo step
                };
                knob.EditEnded += (_, _) => { _dragging = false; _host.ChainChanged(_track); };
                item.Children.Add(knob);
                item.Children.Add(text);
                break;
            }
            case MidiParamKind.Choice:
            {
                var combo = new ComboBox { Width = 150, ToolTip = p.Tip.Length > 0 ? p.Tip : null };
                var current = (int)Math.Round(values[p.Key]!.GetValue<double>());
                foreach (var (value, name) in p.Choices ?? Array.Empty<(int, string)>())
                {
                    var entry = new ComboBoxItem { Content = name, Tag = value };
                    combo.Items.Add(entry);
                    if (value == current) combo.SelectedItem = entry;
                }
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedItem is not ComboBoxItem { Tag: int value }) return;
                    if (p.Key == "NoteMode" && _noteEditor is not null) _noteEditor.Visibility = value == 0 ? Visibility.Collapsed : Visibility.Visible;
                    _edit(() => { values[p.Key] = value; Store(proc, values); });
                };
                item.Children.Add(combo);
                break;
            }
            case MidiParamKind.MapPreset:
            {
                var combo = new ComboBox { Width = 200, ToolTip = p.Tip.Length > 0 ? p.Tip : null };
                var current = values[p.Key]!.GetValue<string>();
                combo.Items.Add(new ComboBoxItem { Content = "(none: notes unchanged)", Tag = DrumMapLibrary.None });
                foreach (var map in DrumMapLibrary.All()) combo.Items.Add(new ComboBoxItem { Content = map.IsUser ? $"★ {map.Name}" : map.Name, Tag = map.Name });
                combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == current) ?? combo.Items[0];
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedItem is not ComboBoxItem { Tag: string name }) return;
                    _edit(() => { values[p.Key] = name; Store(proc, values); });
                };
                item.Children.Add(combo);
                break;
            }
            case MidiParamKind.NoteSet:
            {
                // Chips (number + name), an add box ("36, 38, 40-45, C2, D#3"), and Learn (adds the next note played into this track).
                var frame = new Border { Width = 600, Padding = new Thickness(6), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3) };
                frame.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
                frame.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
                frame.Visibility = values["NoteMode"]!.GetValue<double>() < 0.5 ? Visibility.Collapsed : Visibility.Visible;
                _noteEditor = frame;
                var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
                var empty = new TextBlock { Text = "No notes yet: type some below or press Learn and play them.", FontSize = 11 };
                empty.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                List<int> Current() { var set = EM.NoteSetText.Parse(values[p.Key]!.GetValue<string>()); return Enumerable.Range(0, 128).Where(n => set[n]).ToList(); }
                void Commit(IEnumerable<int> notes) { var text = EM.NoteSetText.Format(notes); _edit(() => { values[p.Key] = text; Store(proc, values); }); Rebuild(); }
                void Rebuild()
                {
                    chips.Children.Clear();
                    var notes = Current();
                    if (notes.Count == 0) chips.Children.Add(empty);
                    foreach (var n in notes)
                    {
                        var chip = new Border { Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(6, 1, 6, 1), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Cursor = Cursors.Hand, ToolTip = "Click to remove" };
                        chip.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                        chip.Child = new TextBlock { Text = $"{n} {NoteLabel(n)}  ×", FontSize = 11 };
                        var captured = n;
                        chip.MouseLeftButtonUp += (_, _) => Commit(Current().Where(x => x != captured));
                        chips.Children.Add(chip);
                    }
                }
                var input = new TextBox { Width = 200, ToolTip = "Notes to add: 36, 38, 40-45, C2, D#3 (Enter adds)" };
                void AddText()
                {
                    var set = EM.NoteSetText.Parse(input.Text);
                    var added = Enumerable.Range(0, 128).Where(n => set[n]).ToList();
                    if (added.Count == 0) return;
                    input.Clear();
                    Commit(Current().Concat(added));
                }
                input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { AddText(); e.Handled = true; } };
                var addButton = new Button { Content = "Add", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
                addButton.Click += (_, _) => AddText();
                var learn = new System.Windows.Controls.Primitives.ToggleButton { Content = "Learn", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Play a note (keyboard or song): it is added to the list. Needs the track to be sounding through this chain." };
                learn.Checked += (_, _) => { _learning = true; _learnSink = n => { Commit(Current().Append(n)); }; UpdateWatch(); };
                learn.Unchecked += (_, _) => { _learning = false; _learnSink = null; UpdateWatch(); };
                var clear = new Button { Content = "Clear", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
                clear.Click += (_, _) => Commit(Array.Empty<int>());
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(input); row.Children.Add(addButton); row.Children.Add(learn); row.Children.Add(clear);
                var stack = new StackPanel();
                stack.Children.Add(chips); stack.Children.Add(row);
                frame.Child = stack;
                Rebuild();
                return frame;
            }
            case MidiParamKind.Text:
            {
                item.Width = 460;
                var box = new TextBox { Width = 300, Text = values[p.Key]!.GetValue<string>(), MaxLength = 512, ToolTip = p.Tip.Length > 0 ? p.Tip : null };
                void Commit()
                {
                    if (box.Text == values[p.Key]!.GetValue<string>()) return;
                    _edit(() => { values[p.Key] = box.Text; Store(proc, values); });
                }
                box.LostFocus += (_, _) => Commit();
                box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
                item.Children.Add(box);
                break;
            }
        }
        return item;
    }

    private FrameworkElement MapButtons()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0), Width = 640 };
        var open = new Button { Content = "Open my drum maps folder", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0), ToolTip = "Save your own maps here as .json files (same format as the shipped ones), then press Reload maps" };
        open.Click += (_, _) =>
        {
            try { Directory.CreateDirectory(DrumMapLibrary.UserFolder); Process.Start(new ProcessStartInfo(DrumMapLibrary.UserFolder) { UseShellExecute = true }); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
        };
        var reload = new Button { Content = "Reload maps", Padding = new Thickness(10, 3, 10, 3), ToolTip = "Read the drum map files again" };
        reload.Click += (_, _) => { DrumMapLibrary.Reload(); ShowSelected(); };
        panel.Children.Add(open); panel.Children.Add(reload);
        var note = new TextBlock { Text = "Maps marked (unverified) come from memory of vendor documentation; check them against the vendor's MIDI map.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 0, 0, 0), Width = 280, FontSize = 11 };
        note.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        panel.Children.Add(note);
        return panel;
    }


    // ---------- note labels ----------
    private string NoteLabel(int n)
    {
        var drums = _track.Kind == TrackKind.Drums || _track.MidiChannel == 9;
        if (drums && DrumMapLibrary.Find("General MIDI") is { } gm && gm.Notes.TryGetValue(n, out var piece)) return piece;
        return EM.NoteSetText.NoteName(n);
    }

    // ---------- presets (whole processor list, %APPDATA%\TabForge\MidiPresets) ----------
    private static string PresetFolder => System.IO.Path.Combine(PluginLibrary.Root, "MidiPresets");
    private const string PresetHint = "Load preset…";

    private static string SafeName(string name)
    {
        return TabForge.Audio.Contracts.SafeFileNames.SafeFileName(name, "preset", 64);
    }

    private static List<(string Name, string File)> UserPresets()
    {
        var list = new List<(string, string)>();
        try
        {
            if (Directory.Exists(PresetFolder))
                foreach (var file in Directory.EnumerateFiles(PresetFolder, "*.json").Take(200)) list.Add((System.IO.Path.GetFileNameWithoutExtension(file), file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return list.OrderBy(p => p.Item1, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private bool _presetBusy;

    private void RefreshPresets(string? select)
    {
        _presetBusy = true;
        try
        {
            _presets.Items.Clear();
            _presets.Items.Add(new ComboBoxItem { Content = PresetHint, Tag = null });
            foreach (var (name, build) in MidiProcessorCatalog.BuiltInPresets) _presets.Items.Add(new ComboBoxItem { Content = name, Tag = build });
            foreach (var (name, file) in UserPresets()) _presets.Items.Add(new ComboBoxItem { Content = $"★ {name}", Tag = file });
            _presets.SelectedItem = _presets.Items.Cast<ComboBoxItem>().FirstOrDefault(i => select is not null && i.Tag as string == select) ?? _presets.Items[0];
            _deletePreset.IsEnabled = _presets.SelectedItem is ComboBoxItem { Tag: string };
        }
        finally { _presetBusy = false; }
    }

    private void OnPresetPicked()
    {
        if (_presetBusy || _presets.SelectedItem is not ComboBoxItem { Tag: { } tag }) { _deletePreset.IsEnabled = false; return; }
        _deletePreset.IsEnabled = tag is string;
        List<PluginMidiProcessor>? list = null;
        if (tag is Func<List<PluginMidiProcessor>> build) list = build();
        else if (tag is string file)
        {
            try
            {
                if (new FileInfo(file).Length <= 512 * 1024)
                    list = System.Text.Json.JsonSerializer.Deserialize<List<PluginMidiProcessor>>(File.ReadAllText(file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
        }
        if (list is null) { MessageBox.Show(this, "That preset could not be read.", Title, MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        list = list.Where(p => p is not null && p.Params.Length <= PluginMidiProcessor.MaxParamsChars).Take(MaxProcessors).ToList();
        _edit(() => { Procs.Clear(); Procs.AddRange(list); });
        RefreshList(0);
    }

    private void SavePreset()
    {
        var name = AskName();
        if (name is null) return;
        try
        {
            Directory.CreateDirectory(PresetFolder);
            var file = System.IO.Path.Combine(PresetFolder, SafeName(name) + ".json");
            if (File.Exists(file) && MessageBox.Show(this, $"Replace the preset \"{name}\"?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(Procs, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            TabForge.Services.FilePathPolicy.WriteAtomically(file, s => s.Write(bytes));   // a failed write leaves the previous preset intact
            RefreshPresets(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void DeletePreset()
    {
        if (_presets.SelectedItem is not ComboBoxItem { Tag: string file }) return;
        if (MessageBox.Show(this, $"Delete the preset \"{System.IO.Path.GetFileNameWithoutExtension(file)}\"?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { File.Delete(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        RefreshPresets(null);
    }

    private string? AskName()
    {
        var box = new TextBox { Margin = new Thickness(0, 0, 0, 10), MaxLength = 64 };
        var ok = new Button { Content = "Save", IsDefault = true, Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(14, 3, 14, 3) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        var stack = new StackPanel { Margin = new Thickness(12) };
        stack.Children.Add(new TextBlock { Text = "Preset name", Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(box); stack.Children.Add(buttons);
        var dialog = new Window { Title = "Save preset", Content = stack, Width = 320, SizeToContent = SizeToContent.Height, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false };
        dialog.SetResourceReference(BackgroundProperty, "WindowBrush");
        dialog.SetResourceReference(ForegroundProperty, "TextBrush");
        ok.Click += (_, _) => { if (box.Text.Trim().Length > 0) dialog.DialogResult = true; };
        dialog.Loaded += (_, _) => box.Focus();
        return dialog.ShowDialog() == true ? box.Text.Trim() : null;
    }
    // ---------- log ----------
    private bool HasLog => Procs.Any(p => p.Enabled && p.Type == "log");

    private void UpdateWatch()
    {
        var want = HasLog || _learning;
        _logPanel.Visibility = HasLog ? Visibility.Visible : Visibility.Collapsed;
        if (want == _watching && !want) return;
        _watching = want;
        _host.Engine.WatchMidiLog(_track, want);
    }

    private void OnLog(int engineSlot, IReadOnlyList<MidiLogLine> lines)
    {
        if (!_watching || engineSlot != _host.Engine.SlotOf(_track)) return;
        foreach (var l in lines)
        {
            if (l.Stage == 250)   // raw input the chain saw: only used by Learn
            {
                if (_learning && (l.Status & 0xF0) == 0x90 && l.Data2 > 0) _learnSink?.Invoke(l.Data1);
                continue;
            }
            if (HasLog) _logLines.Add(Describe(l));
        }
        if (!HasLog) return;
        if (_logLines.Count > MaxLogLines) _logLines.RemoveRange(0, _logLines.Count - MaxLogLines);
        _log.Text = string.Join('\n', _logLines);
        _log.ScrollToEnd();
    }

    private static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    /// <summary>One log line: song time, channel, decoded message, and the position of the log processor that saw it.</summary>
    public static string Describe(MidiLogLine l)
    {
        var kind = l.Status & 0xF0; var ch = (l.Status & 0x0F) + 1;
        string note(int n) => $"{NoteNames[n % 12]}{n / 12 - 1} ({n})";
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

    // ---------- look ----------
    private static Button Small(string text, string tip) => new() { Content = text, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(10, 3, 10, 3), ToolTip = tip };

    private static TextBlock Header(string text)
    {
        var block = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        return block;
    }

    private static Border Frame(UIElement child)
    {
        var frame = new Border { BorderThickness = new Thickness(1), Child = child };
        frame.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
        frame.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        return frame;
    }

    private static Style ItemStyle()
    {
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 3, 6, 3)));
        return style;
    }

    private static ListBoxItem? FindItem(object source)
    {
        var node = source as DependencyObject;
        while (node is not null and not ListBoxItem) node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        return node as ListBoxItem;
    }
}
