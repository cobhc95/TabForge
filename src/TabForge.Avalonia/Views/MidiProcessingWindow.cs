using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Avalonia.Views.Fx;
using TabForge.Avalonia.Views.Midi;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

/// <summary>
/// One plug-in's MIDI processing list (the FX chain window's MIDI... button), the MIDI processing window:
/// the whole-chain preset bar under the tabs; a searchable catalog grouped under collapsible headings on the left; the chain in the middle
/// (drag handle, icon, name and power toggle per step, drag to reorder, right-click or Delete to remove, a dashed add slot at the end); and the
/// selected processor's header (icon, name, preset row), description and parameter cards on the right, with the log panel while a log
/// processor is on; a status line and Close at the foot.
/// Owns: the layout, the list and catalog wiring, the parameter search highlight, the log view and the engine's log watch.
/// Does not own: what a processor does (MidiProcessorCatalog and the engine), the list rules and texts (MidiProcessingListPresenter), the parameter controls
/// (MidiParamEditors, MidiNoteMapRows), the preset rows (MidiPresetBars), the adaptive card layout (MidiCardLayout), the icons (MidiIcons),
/// the undo step and engine update (the edit callback the FX window passes).
/// Partials: .Catalog (catalog, search, add, drag in), .Detail (the editor column), .ChainSelect (selection, drag to reorder, menu, keys),
/// .Log (the MIDI log) and .Repaint, so no file passes 500 lines.
/// Tests: TabForge.Avalonia.Tests.MidiProcessingWindowTests, MidiClipLayoutTests, MidiSmallLayoutTests, MidiProcessingChainMultiSelectTests.
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
    private readonly ListBox _list = new() { BorderThickness = DialogMetrics.PresetNoOutline, Name = "ProcessorList", ClipToBounds = true, SelectionMode = SelectionMode.Multiple };
    private readonly TextBlock _title = new() { FontSize = ChromeMetrics.TextHeading, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Name = "TitleText", VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentControl _titleIcon = new() { Name = "TitleIcon", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap, Name = "DescriptionText" };
    private readonly TextBlock _parameterNote = new() { TextWrapping = TextWrapping.Wrap, Name = "ParameterNote", IsVisible = false };
    private readonly StackPanel _params = new() { Name = "ParamPanel" };
    private readonly Dictionary<string, (Border Box, MidiParamInfo Param)> _elems = new();
    private readonly List<(Control Card, MidiCardPanel Rows)> _sections = new();
    private System.Text.Json.Nodes.JsonObject? _shownValues;
    private MidiNoteMapRows? _mapRows;
    private ScrollViewer? _detailViewer;
    private string[] _queryWords = Array.Empty<string>();
    private string? _jumpKey;
    // Perf: typing re-applies the search at once on the first keystroke and once more when it rests (~100 ms),
    // so a fast query pays two passes, not one per keystroke.
    private readonly IUiTimer _searchDebounce = AvaloniaUiThread.ForApplication().CreateTimer(TimeSpan.FromMilliseconds(UiTiming.FilterDebounceMs));
    private bool _searchDirty;
    private readonly Button _add = Btn("Add ›", "Add the selected processor after the selected one in the chain (Enter in the search box, a double-click or a drag does the same)", "AddButton");
    private readonly Button _close = DialogButtons.Close();
    private readonly TextBox _log = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = TabForge.Avalonia.Platform.AvaloniaFonts.Family("Consolas"), FontSize = ChromeMetrics.TextSmall, Height = 150, Name = "LogBox" };
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
        this.UseWindowColours();
        _close.IsDefault = false;   // Enter belongs to the search box and the parameter boxes; Escape still closes
        _close.Click += (_, _) => Close();
        if (slot is not null)
        {
            _editors = new MidiParamEditors(host, track, slot, edit, _learn);
            _presets = new MidiPresetBars(this, () => Dialogs, () => Procs, slot,
                (list, applied) => { _edit(() => { Procs.Clear(); Procs.AddRange(list); slot.AppliedMidiPreset = applied; }); RefreshList(0); },
                (proc, json, applied) => { _edit(() => { proc.Params = json; proc.AppliedPreset = applied; }); ShowSelected(); });
        }
        Content = BuildRoot();
        InitChainSelect();
        WireEvents();
        WireChainSelect();
        WireCatalogDrag();
        if (slot is not null) FillCatalog();
        if (host.Engine is { } engine) { engine.MidiLogReceived += OnLog; engine.ChainLoaded += UpdateWatch; }
        _learn.Changed += UpdateWatch;
        WireRepaint();
        Closed += (_, _) => OnClosed();
        if (slot is not null) RefreshList(0);
        else _status.Text = "";
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
    private PluginMidiProcessor? Selected => FocusedProc ?? _list.SelectedItem as PluginMidiProcessor;
    private bool HasLog => Procs.Any(p => p.Enabled && p.Type == "log");

    // ---------- layout ----------

    private Control BuildRoot()
    {
        if (_slot is null) return BuildNoSlotRoot();
        var top = new StackPanel { Orientation = Orientation.Horizontal, Name = "ChainPresetBar" };
        top.WithMargin(DialogMetrics.MidiSearchGap);
        var presetsLabel = new TextBlock { Text = "Chain preset", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary") };
        presetsLabel.WithMargin(DialogMetrics.MidiInputGap);
        _presets!.ListBox.MaxWidth = DialogMetrics.MidiCatalogWidth + DialogMetrics.MidiListWidth;
        _presets.ListBox.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(presetsLabel);
        top.Children.Add(_presets.ListBox);
        top.Children.Add(_presets.SaveList);
        top.Children.Add(_presets.DeleteList);
        var body = MidiSmallLayout.BodyGrid();
        var catalog = CatalogPane(); Grid.SetColumn(catalog, 0);
        var list = ListPane(); Grid.SetColumn(list, 1);
        var detail = DetailPane(); Grid.SetColumn(detail, 2);
        body.Children.Add(catalog); body.Children.Add(list); body.Children.Add(detail);
        var root = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,*,Auto") };
        root.WithMargin(DialogMetrics.MidiContentInset);
        var footer = Footer();
        Grid.SetRow(top, 0); Grid.SetRow(body, 1); Grid.SetRow(footer, 2);
        root.Children.Add(top); root.Children.Add(body); root.Children.Add(footer);
        if (_slot!.Type == PluginSlotType.Instrument) _midiTabs = Articulations.ArticulationsTab.Install(root, top, body, () => new Articulations.ArticulationsPane(_host, _track, _slot, _edit, this, () => Dialogs));   // the Articulations tab
        return root;
    }

    /// <summary>The status line ("N processors") and the one Close button.</summary>
    private Grid Footer()
    {
        var footer = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), Name = "MidiFooter" };
        footer.WithMargin(DialogMetrics.MidiFooterGap);
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.TextWrapping = TextWrapping.Wrap;
        Grid.SetColumn(_close, 1);
        footer.Children.Add(_status);
        footer.Children.Add(_close);
        return footer;
    }

    /// <summary>A track without a plug-in: Processors says why they cannot run, and the Articulations tab edits the track's own map.</summary>
    private Control BuildNoSlotRoot()
    {
        var root = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,*,Auto") };
        root.WithMargin(DialogMetrics.MidiContentInset);
        var note = new TextBlock { Text = MidiProcessingListPresenter.NoInstrumentDescription, TextWrapping = TextWrapping.Wrap, Name = "NoInstrumentNote" };
        note.UseMutedText();
        var processors = new StackPanel { Children = { Heading("Processors"), note } };
        var footer = Footer();
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
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
        _search.WithMargin(DialogMetrics.MidiSearchGap);
        MidiSharedRows.Tip(_search, "Search processors and every parameter (e.g. \"velocity\", \"jitter\", \"drum map\"). Enter adds the processor or jumps to the parameter");
        global::Avalonia.Automation.AutomationProperties.SetName(_search, "Search processors");
        _add.HorizontalAlignment = HorizontalAlignment.Left;
        _add.WithMargin(DialogMetrics.MidiBarGap);
        // Two row kinds (group headings and processors); rows bake their text, so containers are not recycled across kinds.
        _catalog.ItemTemplate = new FuncDataTemplate<object>((item, _) => CatalogRow(item), supportsRecycling: false);
        // Perf: the catalog is virtualized (a VirtualizingStackPanel): only the visible rows build controls.
        _catalog.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
        pane.Children.Add(heading); pane.Children.Add(_search); pane.Children.Add(_add); pane.Children.Add(Frame(_catalog));
        return pane;
    }

    private DockPanel ListPane()
    {
        var pane = new DockPanel { Name = "ListPane", ClipToBounds = true };
        var heading = Heading("Chain · top runs first");
        DockPanel.SetDock(heading, Dock.Top);
        var slot = AddSlot();
        DockPanel.SetDock(slot, Dock.Bottom);
        _list.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
        _list.ItemTemplate = new FuncDataTemplate<PluginMidiProcessor>((proc, _) => ProcessorRow(proc), supportsRecycling: false);
        pane.Children.Add(heading); pane.Children.Add(slot); pane.Children.Add(WrapChainList(Frame(_list)));
        return pane;
    }

    /// <summary>The dashed slot at the end of the chain: adds the processor selected in the catalog (a drop on it adds at the end).</summary>
    private Button AddSlot()
    {
        var dash = new Rectangle
        {
            StrokeThickness = DialogMetrics.MidiDashStroke, StrokeDashArray = new global::Avalonia.Collections.AvaloniaList<double> { DialogMetrics.MidiDashOn, DialogMetrics.MidiDashOff },
            RadiusX = DialogMetrics.MidiFrameCorner, RadiusY = DialogMetrics.MidiFrameCorner, [!Shape.StrokeProperty] = new DynamicResourceExtension("BorderSoftBrush"),
        };
        var text = new TextBlock { Text = "+ Add processor", TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
        text.UseMutedText();
        text.WithMargin(DialogMetrics.MidiChainRowPad);
        var button = new Button
        {
            Name = "AddSlotButton", Content = new Grid { Children = { dash, text } }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch, MinHeight = DialogMetrics.MidiAddSlotMinHeight, Background = AvaloniaPalette.TransparentBrush, BorderThickness = DialogMetrics.PresetNoOutline,
        };
        DialogMetrics.WithPadding(button, DialogMetrics.PresetNoOutline);
        button.WithMargin(DialogMetrics.MidiAddSlotGap);
        global::Avalonia.Automation.AutomationProperties.SetName(button, "Add processor");
        MidiSharedRows.Tip(button, "Add the processor selected in the catalog at the end of the chain (or drag one from the catalog here)");
        button.Click += (_, _) => { _chainSel.Clear(); SyncChainFromHelper(); AddSelected(Procs.Count); };
        _addSlot = button;
        return button;
    }

    private DockPanel DetailPane()
    {
        var pane = new DockPanel { Name = "DetailPane", MinWidth = DialogMetrics.MidiEditorMinWidth, ClipToBounds = true };
        _logPanel.Children.Add(Heading("MIDI log (this window watches while it is open)"));
        _logPanel.Children.Add(_log);
        var clear = Btn("Clear log", null, "ClearLogButton");
        clear.WithMargin(DialogMetrics.MidiBarGap);
        clear.HorizontalAlignment = HorizontalAlignment.Left;
        clear.Click += (_, _) => { _logLines.Clear(); _log.Text = ""; };
        _logPanel.Children.Add(clear);
        DockPanel.SetDock(_logPanel, Dock.Bottom);
        pane.Children.Add(_logPanel);
        _description[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("MutedBrush");
        _parameterNote[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("MutedBrush");
        _log[!BackgroundProperty] = new DynamicResourceExtension("PanelBrush");
        var lead = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,*") };
        _titleIcon.WithMargin(DialogMetrics.MidiIconGap);
        Grid.SetColumn(_title, 1);
        lead.Children.Add(_titleIcon); lead.Children.Add(_title);
        var header = new MidiHeaderRow(lead, _presets!.ProcessorRow) { Name = "ProcessorHeader" };
        header.WithMargin(DialogMetrics.MidiHeaderPad);
        var detail = new StackPanel { Children = { header, _description, _parameterNote, _params }, Name = "DetailStack" };
        _description.WithMargin(DialogMetrics.MidiCardGap);
        var viewer = new ScrollViewer { Content = detail, Name = "DetailViewer", ClipToBounds = true, VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        // No sideways scrolling: the cards lay out to the column's width (MidiCardLayout), so nothing needs it and no width binding feeds back into layout.
        _detailViewer = viewer;
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
        _catalog.DoubleTapped += (_, _) => { if (_catalog.SelectedItem is MidiProcessorCatalog.SearchHit) AddSelected(); };
        _catalog.SelectionChanged += (_, _) => CatalogSelectionChanged();
        _add.Click += (_, _) => AddSelected();
        _list.SelectionChanged += (_, _) => SyncChainFromList();
        WireChainKeys();
    }

    private Control ProcessorRow(PluginMidiProcessor? proc)
    {
        var info = proc is null ? null : MidiProcessorCatalog.Find(proc.Type);
        var power = new global::Avalonia.Controls.Primitives.ToggleButton
        {
            Content = MidiIcons.Icon(IconGeometry.Power, DialogMetrics.MidiIconSize), IsChecked = proc?.Enabled == true, VerticalAlignment = VerticalAlignment.Center,
            Name = "ProcessorPower", Focusable = false, MinWidth = DialogMetrics.MidiPowerSize, MinHeight = DialogMetrics.MidiPowerSize,
        };
        DialogMetrics.WithPadding(power, DialogMetrics.MidiPowerPad);
        PianoRollToolLook.Apply(power);
        global::Avalonia.Automation.AutomationProperties.SetName(power, "Enabled");
        MidiSharedRows.Tip(power, "On: the processor runs. Off: it is bypassed and keeps its settings");
        var name = MidiProcessingListPresenter.RowLabel(proc?.Type ?? "", info?.Name);
        var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Name = "ProcessorName" };
        void Paint() => label[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(power.IsChecked == true ? "TextPrimary" : "MutedBrush");
        Paint();
        power.IsCheckedChanged += (_, _) =>
        {
            Paint();
            if (_building || proc is null || power.IsChecked == proc.Enabled) return;
            _edit(() => proc.Enabled = power.IsChecked == true);
            UpdateWatch();
        };
        var handle = MidiIcons.Icon(IconGeometry.DragHandle, DialogMetrics.MidiIconSize, "MutedBrush");
        handle.Name = "DragHandle";
        handle.WithMargin(DialogMetrics.MidiIconGap);
        var icon = MidiIcons.Icon(MidiIcons.ForType(proc?.Type ?? ""), DialogMetrics.MidiIconSize);
        icon.WithMargin(DialogMetrics.MidiIconGap);
        var row = new DockPanel { ClipToBounds = true, Background = AvaloniaPalette.TransparentBrush };
        row.WithMargin(DialogMetrics.MidiChainRowPad);
        DockPanel.SetDock(handle, Dock.Left); DockPanel.SetDock(icon, Dock.Left); DockPanel.SetDock(power, Dock.Right);
        power.WithMargin(DialogMetrics.MidiButtonGap);
        row.Children.Add(handle); row.Children.Add(icon); row.Children.Add(power); row.Children.Add(label);
        MidiSharedRows.Tip(row, info is null ? name : info.Description + " (drag to reorder; right-click for more)");
        return row;
    }

    // ---------- look ----------

    private static Button Btn(string text, string? tip, string name)
    {
        var button = new Button { Content = text, Name = name, [!Button.ForegroundProperty] = new DynamicResourceExtension("TextPrimary") };
        DialogMetrics.WithPadding(button, DialogMetrics.MidiItemPad);
        button.WithMargin(DialogMetrics.MidiButtonGap);
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
        head.WithMargin(DialogMetrics.MidiHeadGap);
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
