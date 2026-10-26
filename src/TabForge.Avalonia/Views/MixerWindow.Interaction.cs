using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Fx;
using TabForge.Models;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The interaction half of the Mixer window: the Master head and the static live input row, the group and track colour menus, the track and
/// group context menus, the FX chain openers and bus power, the selection outline and the drag reorder (through TrackOrdering).
/// Owns: the pointer gestures and menus of the mixer rows. Does not own: the row layout (MixerWindow.cs), the ordering rules (TrackOrdering),
/// the colour rules (TrackColouring), the chain editor (FxChainWindow).
/// Tests: TabForge.Avalonia.Tests.MixerWindowTests.
/// </summary>
public sealed partial class MixerWindow
{
    private const double DragStartDistance = 4, DragHighlightOpacity = 0.9;
    private const int DragZIndex = 1000;

    private readonly Dictionary<TrackModel, Border> _trackRows = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TrackModel, string> _trackGroup = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, Border> _groupRows = new();
    private readonly Dictionary<string, Border> _groupPanels = new();
    private bool _masterCollapsed;
    private TrackModel? _selectedTrack;
    private string? _selectedGroup;
    private Border? _dragHandle;
    private Control? _dragMoving;
    private Point _dragOrigin;
    private bool _dragArmed;

    // ---------- Master and the live input row ----------

    private Border MasterPanel()
    {
        var rows = new List<Control> { MasterRow() };
        if (!_masterCollapsed) rows.Add(LiveInputRow());
        return Panel(rows.ToArray());
    }

    private Control MasterHead(Control name)
    {
        var chevron = Chevron(_masterCollapsed, "Master", _masterCollapsed ? "Expand Master (shows the live input row)" : "Collapse Master (hides the live input row)",
            () => { _masterCollapsed = !_masterCollapsed; Rebuild(); });
        var head = new DockPanel();
        DockPanel.SetDock(chevron, Dock.Left);
        head.Children.Add(chevron);
        head.Children.Add(name);
        return head;
    }

    /// <summary>The live input row under Master, as the row with the input switched off (the preview has no live input).</summary>
    private Border LiveInputRow()
    {
        var grid = RowGrid();
        var mic = new Button
        {
            Name = "LiveInputArm", Width = 16, Height = 16, Padding = new Thickness(0), Margin = new Thickness(22, 0, 6, 0), VerticalAlignment = VerticalAlignment.Top,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Content = new global::Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse(MicGlyph), StrokeThickness = 1.4, Width = 16, Height = 16, Stretch = Stretch.None, IsHitTestVisible = false,
                [!global::Avalonia.Controls.Shapes.Shape.StrokeProperty] = new DynamicResourceExtension("MutedBrush"),
            },
        };
        ToolTip.SetTip(mic, "Live input on or off: hear the chosen input through the live FX chain (the microphone button in the track list does the same)");
        AutomationProperties.SetName(mic, "Live input, off");
        mic.Click += (_, _) => Report("Live input");
        var head = new DockPanel();
        DockPanel.SetDock(mic, Dock.Left);
        head.Children.Add(mic);
        head.Children.Add(NameBlock("Live input", "off", accent: false));
        Put(grid, head, MixerColumnLayout.Name);
        var fx = new FxSplitButton { Name = "LiveInputFx", ChainOn = true, HorizontalAlignment = HorizontalAlignment.Left };
        ToolTip.SetTip(fx, "Live input FX chain: FX opens the chain you hear while live input is on");
        AutomationProperties.SetName(fx, "FX chain for live input");
        fx.OpenChain += (_, _) => { if (FxChainWindows.For(this)?.OpenLiveChain is { } open) open(); else Report("Live input FX"); };
        Put(grid, fx, MixerColumnLayout.Fx);
        var pan = 0; var volume = 100;
        Put(grid, Fader(() => pan, -64, 63, MixerStripPresenter.PanDefault, v => pan = v,
            "Live input pan (−64 left … 0 centre … +63 right; mouse wheel: 1 step, Ctrl+wheel: 8; double-click: centre)"), MixerColumnLayout.Pan);
        Put(grid, Fader(() => volume, 0, MidiLimits.Max, MixerStripPresenter.VolumeDefault, v => volume = v, "Live input volume (0-127; mouse wheel: 1 step, Ctrl+wheel: 8; double-click: 100)"), MixerColumnLayout.Volume);
        var muteOnly = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        muteOnly.Children.Add(Toggle("M", "Mute", false, _ => { }, null));
        Put(grid, muteOnly, MixerColumnLayout.MuteSolo);
        return RowBorder(grid, accent: false, "Mixer row: Live input");
    }

    private const string MicGlyph = "M9,3.5 a3,3 0 0 1 6,0 V11 a3,3 0 0 1 -6,0 Z M6,10.5 a6,6 0 0 0 12,0 M12,16.5 V20 M8.5,20 H15.5";

    // ---------- FX chains and bus power ----------

    // Read only: a song file without a monitor chain is not given one by looking at the mixer; the chain is created when it is opened for editing.
    private BusChain MonitorChain() => _project.Mixer.MonitorFx ?? new BusChain();

    private bool MonitorActive() => MonitorChain() is { On: true } chain && chain.Rig.Plugins.Any(p => p.Plays);

    private void OpenChain(TrackModel track) => FxChainWindows.OpenFor(this, track);

    /// <summary>Mixer.MasterFx: opens the master bus chain.</summary>
    public void OpenMasterFx() => OpenChain(MixerBuses.MasterTrack(_project));

    /// <summary>Mixer.MonitorFx: opens the monitoring chain (live output only).</summary>
    public void OpenMonitorFx() => OpenChain(MixerBuses.MonitorTrack(_project, FxChainWindows.For(this)?.GlobalMonitorChain ?? (_project.Mixer.MonitorFx ??= new BusChain())));

    /// <summary>Mixer.GroupFx: opens the effects bus chain of the group.</summary>
    public void OpenGroupFx(string group) => OpenChain(MixerBuses.BusTrack(_project, group));

    /// <summary>FX split button of a group bus (group name) or the master (null): FX opens the chain, power bypasses it.</summary>
    private FxSplitButton BusButton(string? group)
    {
        // Read without creating the bus: an empty bus is a change of the song, so opening the mixer must not make one (a group without a bus shows as on, empty).
        var bus = group is null ? _project.Mixer.Master : _project.Mixer.Buses.GetValueOrDefault(group);
        var fx = new FxSplitButton { Name = "BusFx", ChainOn = bus?.On ?? true, PluginCount = bus?.Rig.Plugins.Count ?? 0, HorizontalAlignment = HorizontalAlignment.Left };
        ToolTip.SetTip(fx, group is null
            ? "Master effects: FX opens the chain applied to the whole mix (e.g. reverb on everything) · power: bypass it"
            : "Group effects bus: FX opens the chain the group's tracks sum into before the master · power: bypass it");
        AutomationProperties.SetName(fx, group is null ? "Master effects" : $"{group} effects bus");
        fx.OpenChain += (_, _) => { if (group is null) OpenMasterFx(); else OpenGroupFx(group); };
        // The power switch is one undo step; the group's bus is created by it, as the first change of that bus.
        fx.TogglePower += (_, _) => ClickEdit(() =>
        {
            var target = group is null ? _project.Mixer.Master : _project.Mixer.Bus(group);
            MixerBuses.SetOn(target, !target.On);
            fx.ChainOn = target.On;
        });
        return fx;
    }

    // ---------- the collapse arrow and the colour menus ----------

    /// <summary>The collapse arrow of a group-style row: a right triangle when collapsed, a down one when open.</summary>
    private static Button Chevron(bool collapsed, string name, string tip, Action toggle)
    {
        var path = new global::Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(collapsed ? "M0,0 L5,4 L0,8 Z" : "M0,0 L8,0 L4,5 Z"), Stretch = Stretch.Uniform,
            Width = collapsed ? 6 : 9, Height = collapsed ? 9 : 6,
            [!global::Avalonia.Controls.Shapes.Shape.FillProperty] = new DynamicResourceExtension("TextBrush"),
        };
        var button = new Button
        {
            Name = "Chevron", Width = 18, Height = 18, Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Top,
            Focusable = false, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Content = path,
        };
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetName(button, (collapsed ? "Expand " : "Collapse ") + name);
        button.Click += (_, _) => toggle();
        return button;
    }

    private string GroupColour(string group, IReadOnlyList<TrackModel> members) =>
        members.Count > 0 ? members[0].ColorHex : TrackColouring.ColourOf(group, null);

    private void PickTrackColour(TrackModel track, Control anchor) =>
        ColourMenuFor(anchor, track.ColorHex, hex => { ClickEdit(() => TrackColouring.SetColour(track, hex)); Rebuild(); });

    /// <summary>Opens the track colour palette under <paramref name="anchor"/>; <paramref name="apply"/> gets the chosen hex text.</summary>
    private static void ColourMenuFor(Control anchor, string? current, Action<string> apply)
    {
        var menu = MenuStability.CreateContextMenu();
        menu.PlacementTarget = anchor;
        foreach (var (name, hex) in NamedColourSwatches.All)
        {
            var item = new MenuItem
            {
                Header = name, ToggleType = MenuItemToggleType.CheckBox, IsChecked = string.Equals(hex, current, StringComparison.OrdinalIgnoreCase),
                Icon = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Background = SwatchBrush(hex) },
            };
            item.Click += (_, _) => apply(hex);
            menu.Items.Add(item);
        }
        menu.Open(anchor);
    }

    // ---------- context menus ----------

    /// <summary>Right-click on a track row: its colour, moving it to another group, and returning a manually moved track to its rule group.</summary>
    private ContextMenu TrackMenu(Guid trackId, Control chip)
    {
        void OnTrack(Action<TrackModel> apply) { if (_project.Tracks.FirstOrDefault(t => t.Id == trackId) is { } live) apply(live); }
        var menu = MenuStability.CreateContextMenu();
        var colour = new MenuItem { Header = "Track colour…" };
        colour.Click += (_, _) => OnTrack(live => PickTrackColour(live, chip));
        menu.Items.Add(colour);
        menu.Items.Add(new Separator());
        foreach (var group in MixerGroups.Names(_project.Mixer))
        {
            var target = group;
            var item = new MenuItem { Header = $"Move to {group}", ToggleType = MenuItemToggleType.CheckBox, IsChecked = _project.Tracks.FirstOrDefault(t => t.Id == trackId) is { } now && MixerGroups.GroupOf(_project, now) == group };
            item.Click += (_, _) => { OnTrack(live => TrackOrdering.AssignGroup(_project, live, target)); Rebuild(); };
            menu.Items.Add(item);
        }
        if (_project.Tracks.FirstOrDefault(t => t.Id == trackId)?.MixerGroup is null) return menu;
        menu.Items.Add(new Separator());
        var back = new MenuItem { Header = "Return to rule group" };
        ToolTip.SetTip(back, "This track was moved by hand; put it back in the group its instrument and the group rules give it");
        back.Click += (_, _) => { OnTrack(live => live.MixerGroup = null); Rebuild(); };
        menu.Items.Add(back);
        return menu;
    }

    /// <summary>Right-click on a group row: collapse or expand this group, or all of them.</summary>
    private void AttachGroupMenu(Border groupRow, string group)
    {
        var menu = MenuStability.CreateContextMenu();
        var toggle = new MenuItem { Header = IsCollapsed(group) ? "Expand this group" : "Collapse this group" };
        toggle.Click += (_, _) => SetCollapsed(new[] { group }, null);
        var collapseAll = new MenuItem { Header = "Collapse all groups" };
        collapseAll.Click += (_, _) => SetCollapsed(_groupNames, true);
        var expandAll = new MenuItem { Header = "Expand all groups" };
        expandAll.Click += (_, _) => SetCollapsed(_groupNames, false);
        menu.Items.Add(toggle);
        menu.Items.Add(collapseAll);
        menu.Items.Add(expandAll);
        groupRow.ContextMenu = menu;
        groupRow.Focusable = true;
        groupRow.KeyDown += (_, e) =>
        {
            if (!ReferenceEquals(e.Source, groupRow)) return;
            var collapsed = IsCollapsed(group);
            if (e.Key is Key.Enter or Key.Space || e.Key == Key.Left && !collapsed || e.Key == Key.Right && collapsed)
            {
                e.Handled = true;
                SetCollapsed(new[] { group }, null);
            }
        };
    }

    /// <summary>Escape closes the window (unless typing); Alt+Shift+Up / Down (the default Track.MoveUp / Track.MoveDown) moves the selected row.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || FocusManager?.GetFocusedElement() is TextBox or ComboBox) return;
        if (e.Key == Key.Escape) { e.Handled = true; Close(); return; }
        if (e.Key is not (Key.Up or Key.Down) || e.KeyModifiers != (KeyModifiers.Alt | KeyModifiers.Shift)) return;
        e.Handled = true;
        NudgeSelected(e.Key == Key.Down ? 1 : -1);
    }

    private void NudgeSelected(int direction)
    {
        var moved = _selectedTrack is { } track && _project.Tracks.Contains(track) ? TrackOrdering.Nudge(_project, track, direction)
            : _selectedGroup is { } group && TrackOrdering.NudgeGroup(_project, group, direction);
        if (moved) Rebuild();
    }

    // ---------- selection and drag reorder ----------

    private void Select(TrackModel? track, string? group)
    {
        _selectedTrack = track; _selectedGroup = track is null ? group : null;
        foreach (var (t, row) in _trackRows) MarkSelected(row, ReferenceEquals(t, _selectedTrack));
        foreach (var (g, row) in _groupRows) MarkSelected(row, g == _selectedGroup);
    }

    /// <summary>Selected rows get an accent outline (the border is always there, so nothing shifts).</summary>
    private static void MarkSelected(Border row, bool on)
    {
        if (on) row[!Border.BorderBrushProperty] = new DynamicResourceExtension("AccentBrush"); else row.BorderBrush = Brushes.Transparent;
    }

    private static bool IsInteractive(object? source, Control row)
    {
        for (var c = source as Visual; c is not null && !ReferenceEquals(c, row); c = c.GetVisualParent())
            if (c is Button or Slider or ComboBox or CheckBox or ToggleButton or MixerDrawnButton) return true;
        return false;
    }

    /// <summary>Dragging a track or group row moves it (<paramref name="moving"/> follows the pointer); the drop is applied through TrackOrdering.</summary>
    private void AttachDrag(Border handle, Guid? trackId, string? group, Control moving)
    {
        TrackModel? Track() => trackId is { } wanted ? _project.Tracks.FirstOrDefault(t => t.Id == wanted) : null;
        handle.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed || IsInteractive(e.Source, handle)) return;
            e.Handled = true;
            if (e.ClickCount == 2 && trackId is null && group is not null) { SetCollapsed(new[] { group }, null); return; }
            Select(Track(), group);
            _dragHandle = handle; _dragMoving = moving; _dragArmed = false; _dragOrigin = e.GetPosition(_groups);
            e.Pointer.Capture(handle);
        };
        handle.PointerMoved += (_, e) =>
        {
            if (!ReferenceEquals(_dragHandle, handle)) return;
            var dy = e.GetPosition(_groups).Y - _dragOrigin.Y;
            if (!_dragArmed && Math.Abs(dy) < DragStartDistance) return;
            _dragArmed = true;
            moving.RenderTransform = new TranslateTransform(0, dy);
            moving.ZIndex = DragZIndex;
            moving.Opacity = DragHighlightOpacity;
        };
        handle.PointerReleased += (_, e) =>
        {
            if (!ReferenceEquals(_dragHandle, handle)) return;
            var armed = _dragArmed;
            var y = e.GetPosition(_groups).Y;
            e.Pointer.Capture(null);
            EndDrag();
            if (!armed) return;
            var changed = trackId is not null
                ? Track() is { } dragged && DropTrack(dragged, y)
                : group is not null && TrackOrdering.MoveGroup(_project, group, GroupDropIndex(group, y));
            if (changed) Rebuild();
        };
        handle.PointerCaptureLost += (_, _) => { if (ReferenceEquals(_dragHandle, handle)) EndDrag(); };
    }

    private void EndDrag()
    {
        if (_dragMoving is not null) { _dragMoving.RenderTransform = null; _dragMoving.ZIndex = 0; _dragMoving.Opacity = 1; }
        _dragHandle = null; _dragMoving = null; _dragArmed = false;
    }

    private double TopOf(Control element) => element.TranslatePoint(default, _groups)?.Y ?? 0;

    private double MiddleOf(Control element) => TopOf(element) + element.Bounds.Height / 2;

    /// <summary>The position among the other groups where a dragged group lands for a pointer at <paramref name="y"/>.</summary>
    private int GroupDropIndex(string group, double y) =>
        _groupPanels.Where(p => p.Key != group).Count(p => MiddleOf(p.Value) < y);

    /// <summary>Moves a dragged track into the group under the pointer, at the slot the pointer is in; true when the order changed.</summary>
    private bool DropTrack(TrackModel track, double y)
    {
        if (_groupPanels.Count == 0) return false;
        double Gap(Control p) { var top = TopOf(p); return y < top ? top - y : y > top + p.Bounds.Height ? y - top - p.Bounds.Height : 0; }
        var target = _groupPanels.OrderBy(p => Gap(p.Value)).First().Key;
        var others = TrackOrdering.Layout(_project).First(g => g.Group == target).Tracks.Where(t => !ReferenceEquals(t, track)).ToList();
        var index = IsCollapsed(target) ? others.Count : others.Count(t => _trackRows.TryGetValue(t, out var row) && MiddleOf(row) < y);
        return TrackOrdering.MoveTrackToGroup(_project, track, target, index);
    }
}
