using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views.Fx;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Views;

/// <summary>
/// One track's FX chain window (the FX button), the FX chain window: the menu bar (FX, Chains, Options), the options row, the
/// chain list (rows, containers, drag to reorder, several selected) with Add, New container, Remove and the CPU text, the selected plug-in's bar (presets,
/// Wiring, MIDI, volume, On) above its own editor docked in the window, and the status line.
/// Owns: the window's layout, its controls and their wiring, the focused row, and every edit of the bound track's chain (each is one undo step, through the host).
/// Does not own: the host (IFxChainHost: undo, dirty, engine sync), the texts and rules (FxChainPresenter, FxEditorPlan), the list (FxChainList), the containers
///   (FxChainContainers), the editor (FxEditorController, FxEditorArea), the chain files (FxChainFiles), the Add path (FxAddPlugin), the key map (FxChainKeys).
/// The window is split over FxChainWindow.*.cs files (menu, options, chain commands, bar, editor), one concern each.
/// Tests: TabForge.Avalonia.Tests.FxChainWindowTests and the Fx* test classes beside it.
/// </summary>
public sealed partial class FxChainWindow : Window
{
    private readonly IFxChainHost _host;
    private readonly TrackModel _track;
    private readonly FxChainKeys _keys;
    private readonly FxChainList _list = new();
    private readonly FxEditorArea _area;
    private readonly FxEditorController _editors;
    private readonly FxChainContainers _containers;
    private readonly FxContainerPresets _containerPresets;
    private readonly FxChainFiles _files;
    private readonly IUiTimer _cpuTimer;
    private readonly IUiThread _ui = AvaloniaUiThread.ForApplication();
    private readonly TextBlock _status = ToolWindowParts.Status("StatusLine");
    private readonly Button _add = Btn("Add…", "Add a plug-in to the end of the chain", "AddButton");
    private readonly Button _newContainer = FxChainRows.NewContainerButton();
    private readonly Button _remove = Btn("Remove", "Remove the selected plug-ins (Delete)", "RemoveButton");
    private readonly TextBlock _cpuStatus = new()
    {
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Name = "CpuStatus", TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis,
        [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted"),
    };
    private FxListSplitter? _splitter;
    private bool _building;
    private bool _closed;
    private bool _replaced;
    // Gap after each option of the options row, in DIP.
    private const double OptionGap = 14;
    private const double BodyMargin = 10, ListGap = 10, SplitterWidth = 6;
    private const int CpuTickSeconds = 1;

    /// <summary>The questions, messages and file pickers of this window; a test replaces members to answer without a modal box.</summary>
    public FxChainDialogs Dialogs { get; } = new();

    /// <param name="host">The song, settings and engine the window works with.</param>
    /// <param name="track">The track (or bus stand-in) whose chain the window edits.</param>
    public FxChainWindow(IFxChainHost host, TrackModel track)
    {
        _host = host;
        _track = track;
        Dialogs.Files = new AvaloniaFileDialogs(host.DialogFolders);
        _keys = new FxChainKeys(RunFxCommand, () => _host.Hotkeys);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Title = FxChainPresenter.WindowTitle(track.Name);
        Width = 980; Height = 620; MinWidth = 640; MinHeight = 380;
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        _area = new FxEditorArea(() => PluginEditorPopup);
        _parameters = new FxParameterPane(host, track, _ui);
        _editors = new FxEditorController(host, track, _area, _ui, () => OwnerHandle, () => ActualThemeVariant != ThemeVariant.Light,
            () => Selected, () => ShowSelected(), () => { ShowSelected(); if (IsVisible && WindowState != WindowState.Minimized) Activate(); }, () => PluginEditorPopup);
        _containers = new FxChainContainers(this, _list, () => _track.Rig,
            (change, switched) => EditTree(tree => { var had = HasInstrument; change(tree); KeepGmSound(had); }, refresh: true, switched: switched),
            focus => RefreshRows(focus), leaving => _editors.Leaving(leaving), () => Dialogs, () => _host.Hotkeys);
        _containerPresets = new FxContainerPresets(this, _containers.PresetBar, () => _containers.Shown, () => _track.Rig, EditKeepingGmSound,
            leaving => _editors.Leaving(leaving), slot => _host.Trust.IsTrusted(slot.Path, _host.PluginSettings, _host.PluginFiles),
            () => _host.Engine?.CollectStatesAsync(new[] { _track }, StateCollectMs) ?? System.Threading.Tasks.Task.CompletedTask, () => Dialogs,
            _host.PluginSettings, _host.SaveSettings);
        _files = new FxChainFiles(this, () => Dialogs, _host, _track, first => RefreshList(first), text => _status.Text = text, change => EditTree(change, refresh: true));
        TabForge.Avalonia.Platform.Controls.HotkeyTip.SetSource(this, () => _host.Hotkeys);   // tooltips with a key show the live binding
        _containers.LeadingItems = ListMenuItems;
        _containers.OpenActiveOn = OpenActiveOnForContainer;
        if (_host.Engine is { } engine) engine.OutputChanged += OnOutputChanged;   // re-match the Active on rules when a device opens or falls back
        BuildOptions();
        Content = BuildRoot();
        BuildBar();
        WireEvents();
        WireParameters();
        _cpuTimer = _ui.CreateTimer(TimeSpan.FromSeconds(CpuTickSeconds));
        _cpuTimer.Tick += (_, _) => UpdateCpu();
        AttachWindowEvents();
        UpdateCpu();
        RefreshList(0);
    }

    /// <summary>The bound track (or bus stand-in).</summary>
    public TrackModel Track => _track;

    /// <summary>Shows the window over <paramref name="owner"/>, then docks the plug-in editor area; a refusal by Windows keeps the window open and is said on the status line.</summary>
    public void ShowOver(Window owner)
    {
        Show(owner);
        if (!_area.DockSurface()) _status.Text = FxChainPresenter.EditorUnavailableNotice;
    }

    /// <summary>A sample track for the preview and the tests: a VST instrument with a transpose, an enabled reverb and a bypassed delay.</summary>
    public static TrackModel SampleTrack() => new() { Name = "Sample track", Rig = { Plugins = SampleChain() } };

    /// <summary>A sample chain: a VST instrument, an enabled reverb and a bypassed delay, with a transpose on the instrument.</summary>
    public static List<PluginSlot> SampleChain() => new()
    {
        new PluginSlot { Name = "Sample piano", Type = PluginSlotType.Instrument, Format = "VST3", Vendor = "Sample", MidiProcessors = new() { new PluginMidiProcessor { Type = "transpose" } } },
        new PluginSlot { Name = "Sample reverb", Type = PluginSlotType.Effect, Format = "VST3", Vendor = "Sample" },
        new PluginSlot { Name = "Sample delay", Type = PluginSlotType.Effect, Format = "VST3", Vendor = "Sample", Enabled = false },
    };

    /// <summary>The volume's text: "-inf dB" at the bottom of the slider, else the value with its sign.</summary>
    public static string VolumeText(double db) => db <= -59.9 ? "-inf dB" : $"{db:+0.0;-0.0;0.0} dB";

    /// <summary>The focused plug-in: the row clicked last, the one the pane shows (other rows may be selected with it).</summary>
    private PluginSlot? Selected => _list.Focused as PluginSlot;

    /// <summary>The plug-in the Wiring and MIDI commands mean: the focused one, else the first.</summary>
    private PluginSlot? FocusedOrFirst => Selected ?? _track.Rig.Plugins.FirstOrDefault();

    private Control BuildRoot()
    {
        var frame = new DockPanel();
        var strip = _host.LiveStrip ?? BuildTrackStrip();
        frame.Children.Add(BuildTopRow(strip));
        // The status line is the bottom row: it spans the window's content width, so long notices wrap at that width.
        var root = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,*,Auto"), Margin = new Thickness(BodyMargin) };
        if (strip is null)
        {
            var options = BuildOptionsRow();
            Grid.SetRow(options, 0);
            root.Children.Add(options);
        }
        var body = new Grid { ColumnDefinitions = ColumnDefinitions.Parse($"{FxListSplitter.Limit(_host.PluginSettings.FxListWidth, Width)},{SplitterWidth},*") };
        var chainPane = BuildChainPane();
        Grid.SetColumn(chainPane, 0);
        var splitter = _splitter = new FxListSplitter(body, _host.PluginSettings, _host.SaveSettings, () => Bounds.Width > 0 ? Bounds.Width : Width);
        Grid.SetColumn(splitter, 1);
        var pluginPane = BuildPluginPane();
        Grid.SetColumn(pluginPane, 2);
        body.Children.Add(chainPane);
        body.Children.Add(splitter);
        body.Children.Add(pluginPane);
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        Grid.SetRow(_status, 2);
        root.Children.Add(_status);
        frame.Children.Add(root);
        return frame;
    }

    /// <summary>A bus's or app-wide chain's row of plain options.</summary>
    private WrapPanel BuildOptionsRow()
    {
        var options = new WrapPanel { Name = "OptionsRow" };
        foreach (var option in OptionControls())
        {
            option.Margin = new Thickness(0, 0, OptionGap, 4);
            options.Children.Add(option);
        }
        if (_host.AppWideChainNote is { } note) options.Children.Add(note);   // an app-wide chain shows its note where the track options would be
        return options;
    }

    /// <summary>The menu bar, as in every FX window; a track or live input chain shows its slim strip in the same row, right of the menus after a divider (when narrow its groups wrap under the menus).</summary>
    private Control BuildTopRow(Control? strip)
    {
        var menu = BuildMenu();
        if (strip is null) return menu;
        var divider = new Border { Width = 1, [!Border.BackgroundProperty] = new DynamicResourceExtension("BorderSoftBrush") }.WithMargin(ListGap, ChromeMetrics.LiveStripGap, ListGap, ChromeMetrics.LiveStripGap);
        Grid.SetColumn(divider, 1);
        Grid.SetColumn(strip, 2);
        var row = new Grid { Name = "MenuRow", ColumnDefinitions = ColumnDefinitions.Parse("Auto,Auto,*") };
        row.Children.Add(menu);
        row.Children.Add(divider);
        strip.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(strip.WithMargin(0, ChromeMetrics.LiveStripGap, BodyMargin, ChromeMetrics.LiveStripGap));
        DockPanel.SetDock(row, Dock.Top);
        return row;
    }

    private DockPanel BuildChainPane()
    {
        var pane = new DockPanel { Margin = new Thickness(0, 0, ListGap, 0) };
        var buttons = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        foreach (var control in new Control[] { _add, _newContainer, _remove, _cpuStatus }) { DockPanel.SetDock(control, Dock.Left); buttons.Children.Add(control); }
        ToolTip.SetTip(_newContainer, "New container: an empty container behind the focused row; drag plug-ins onto it");
        pane.Children.Add(buttons);
        var frame = new Border
        {
            BorderThickness = new Thickness(1), Child = _list,
            [!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush"),
            [!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBackground"),
        };
        pane.Children.Add(frame);
        return pane;
    }

    private void WireEvents()
    {
        _list.FocusChanged += () => { if (!_building) ShowSelected(); };
        _list.KeyDown += (_, e) => { if (e.Key == Key.Delete && e.Source is not TextBox) { _ = RemoveSelected(); e.Handled = true; } };   // Delete removes the selected plug-ins, as Remove says
        _list.RowActivated += tag => { if (tag is PluginSlot slot && !BuiltinEditors.TryOpen(this, _host, _track, slot)) _editors.Float(slot); };
        _add.Click += (_, _) => _ = AddPlugin();
        _newContainer.Click += (_, _) => _containers.New();
        _remove.Click += (_, _) => _ = RemoveSelected();
        _area.ActionClicked += OnAreaAction;
        _area.SecondActionClicked += OnAreaSecondAction;
        _editors.ProgramsChanged += LoadPresetList;
    }

    private void UpdateCpu()
    {
        var engine = _host.Engine;
        var running = engine?.IsRunning == true;
        _cpuStatus.Text = FxChainPresenter.CpuStatusText(running, running ? engine!.CpuLoad : 0, Environment.ProcessorCount);
        ToolTip.SetTip(_cpuStatus, FxChainPresenter.CpuToolTip);
    }

    private static Button Btn(string text, string tip, string name)
    {
        var button = new Button
        {
            Content = text, Name = name, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 4, 0),
            [!Button.ForegroundProperty] = new DynamicResourceExtension("TextPrimary"),
        };
        // The bar is built after the window's content is attached, so the default-look hook has already run and its template guard would skip
        // these buttons; name the Core look here so the bar hovers with the same HoverBrush as every other button instead of staying flat.
        button.Classes.Add(AvaloniaDefaultLooks.ButtonLook);
        ToolTip.SetTip(button, tip);
        return button;
    }
}
