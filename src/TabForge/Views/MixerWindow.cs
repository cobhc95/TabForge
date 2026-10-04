using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>What the mixer needs from the main window; the mixer never reaches into it otherwise.</summary>
public interface IMixerHost
{
    SongProject Project { get; }
    /// <summary>Called once before a change (one undo step per gesture).</summary>
    void BeginMixerEdit();
    /// <summary>A mixer value changed. <paramref name="recompile"/>: pitch, mute/solo or source changed.</summary>
    void MixerChanged(bool recompile);
    /// <summary>A mute or solo toggled: heard at once, without recompiling playback.</summary>
    void MuteSoloChanged() => MixerChanged(false);
    void OpenFxChain(TrackModel track);
    void OpenAudioSettings();
    /// <summary>Colour every track of a mixer group (and remember it as that group's colour).</summary>
    void SetGroupColour(string group, string hex);
    string GroupColour(string group);
    /// <summary>Track-list options set from the mixer: "volume" / "pan" columns shown, groups shown.</summary>
    bool TrackListShows(string what);
    void SetTrackListShows(string what, bool on);
    /// <summary>Opens a group bus's FX chain (group name) or, with null, the master chain.</summary>
    void OpenBusFx(string? group);
    /// <summary>The monitoring effects chain in effect for the open song (app-wide, or the song's own); heard live only.</summary>
    BusChain MonitorChain { get; }
    /// <summary>Opens the monitoring effects chain window.</summary>
    void OpenMonitorFx();
    /// <summary>Master volume 0–100 % (the main window's master knob; setting it moves the knob).</summary>
    int MasterVolume { get; set; }
    /// <summary>The command bound to a key gesture (e.g. "Track.MoveUp"), or null; lets the mixer honour rebinding.</summary>
    string? HotkeyAction(string gesture);
    /// <summary>
    /// Reorders through the shared track ordering (<see cref="TrackOrdering"/>): one undo step, the track list and the mixer
    /// both refresh and play their movement animation together. Returns true when something changed.
    /// </summary>
    bool ReorderFromMixer(Func<SongProject, bool> apply, string status);
}

/// <summary>
/// The mixer: one strip per track, grouped (guitars, basses, keys, drums, other — or compact), with a group
/// strip in front of each group that sets level, pan and pitch for all its tracks at once. Each track strip
/// also chooses its sound source (Windows MIDI or its plug-in chain) and opens its FX chain.
/// Built once per structure change; slider drags only update values (no rebuild, no per-frame work).
/// </summary>
public sealed class MixerWindow : Window
{
    private readonly IMixerHost _host;
    private readonly StackPanel _groups = new() { Orientation = Orientation.Vertical };
    private readonly ComboBox _grouping = new() { Width = 150, ItemsSource = MixerGrouping.All };
    private bool _building;
    private bool _rebuildAfterDrag;   // a rebuild was asked for while a slider was dragged

    public MixerWindow(IMixerHost host, Window? owner)
    {
        _host = host;
        PreviewKeyDown += (_, e) =>
        {
            var typing = Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase || Keyboard.FocusedElement is ComboBox;
            if (e.Key == Key.Escape && !typing) { e.Handled = true; Close(); return; }
            // Move the selected row (Track.MoveUp / Track.MoveDown, Alt+Up / Alt+Down by default; follows rebinding).
            var action = typing ? null : _host.HotkeyAction(WpfHotkeyGestureAdapter.FromEvent(e));
            if (action is "Track.MoveUp" or "Track.MoveDown")
            {
                e.Handled = true;
                NudgeSelected(action == "Track.MoveDown" ? 1 : -1);
            }
        };
        Title = "Mixer";
        Owner = owner;
        Width = 900; Height = 620; MinWidth = 640; MinHeight = 320;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");

        var root = new DockPanel { Margin = new Thickness(10) };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(top, Dock.Top);
        var groupingRow = new StackPanel { Orientation = Orientation.Horizontal };
        groupingRow.Children.Add(new TextBlock { Text = "Group tracks", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        groupingRow.Children.Add(_grouping);
        _grouping.ToolTip = "By instrument: guitars, basses, keys (piano, organ, synth), drums, other.\nCompact: guitars, basses, drums, and all other instruments together.\nNo groups: one group for the whole song.";
        _grouping.SelectionChanged += (_, _) =>
        {
            if (_building || _grouping.SelectedItem is not string mode || mode == _host.Project.Mixer.Grouping) return;
            _host.BeginMixerEdit();
            _host.Project.Mixer.Grouping = mode;
            _host.MixerChanged(recompile: true);
            Rebuild();
        };
        top.Children.Add(groupingRow);
        // What the main track list shows (the track list itself stays unchanged unless these are changed).
        var shows = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0) };
        foreach (var (what, label, tip) in new[]
        {
            ("volume", "Volume in track list", "Show the volume column in the main track list"),
            ("pan", "Pan in track list", "Show the pan column in the main track list"),
            ("groups", "Groups in track list", "Show the mixer groups in the main track list: a box per group, which can be collapsed and dragged as a whole"),
        })
        {
            var box = new CheckBox { Content = label, IsChecked = _host.TrackListShows(what), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            TooltipShortcuts.Bind(box, tip, what == "groups" ? "View.ShowTrackGroups" : null);
            box.Click += (_, _) => _host.SetTrackListShows(what, box.IsChecked == true);
            _showBoxes.Add((what, box));
            shows.Children.Add(box);
        }
        groupingRow.Children.Add(shows);
        var settings = new Button { Content = "Audio & Plug-ins settings…", Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Right };
        settings.Click += (_, _) => _host.OpenAudioSettings();
        DockPanel.SetDock(settings, Dock.Right);
        top.Children.Insert(0, settings);
        root.Children.Add(top);

        var hint = new TextBlock
        {
            Text = "Group rows add to every track in the group (pan and pitch are offsets, volume is a percentage of each track's own). " +
                   "Drag a track or group row to reorder (the Move track up and down commands also move the selected row); right-click a track row to move it to another group. Double-click a slider to reset it.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), FontSize = 11
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        DockPanel.SetDock(hint, Dock.Bottom);
        root.Children.Add(hint);

        _scroller = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _groups };
        var body = new Grid();
        body.Children.Add(_scroller);
        body.Children.Add(_overlay);   // drop caret while dragging
        root.Children.Add(body);
        Content = root;
        Rebuild();
    }

    private readonly List<Action> _syncers = new();
    private string _signature = "";
    private ScrollViewer _scroller = null!;
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly List<(string What, CheckBox Box)> _showBoxes = new();
    private readonly Dictionary<TrackModel, Border> _trackRows = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TrackModel, string> _trackGroup = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, Border> _groupRows = new();
    private readonly Dictionary<string, Border> _groupPanels = new();
    private TrackModel? _selTrack;
    private string? _selGroup;

    private string Signature()
    {
        var project = _host.Project;
        var sb = new System.Text.StringBuilder(project.Mixer.Grouping).Append('|').Append(project.Tracks.Count);
        foreach (var t in project.Tracks)
            sb.Append('|').Append(t.Name).Append('/').Append(t.InstrumentName).Append('/').Append(MixerGroups.GroupOf(project, t))
              .Append('/').Append(t.SoundSource).Append('/').Append(t.MidiSound).Append('/').Append(t.Rig.Plugins.Count);
        return sb.ToString();
    }

    /// <summary>
    /// Lightweight refresh for value changes made elsewhere (track list drags, mute / solo): updates the existing
    /// sliders and toggles in place, and rebuilds the strips only when the track / group structure changed.
    /// </summary>
    public void SyncValues()
    {
        if (_building) return;
        foreach (var (what, box) in _showBoxes)   // the track list's own menu changes these too: both places show one setting
        {
            var on = _host.TrackListShows(what);
            if (box.IsChecked != on) box.IsChecked = on;
        }
        if (Signature() != _signature) { Rebuild(); return; }
        foreach (var sync in _syncers) sync();
    }

    // ---------- order: shared with the track list ----------

    /// <summary>Layout top of an element relative to <paramref name="ancestor"/>, ignoring render transforms (so a dragged or animating row still reports its slot).</summary>
    private static double LayoutTop(Visual element, Visual ancestor)
    {
        var y = 0.0;
        for (DependencyObject? d = element; d is not null && !ReferenceEquals(d, ancestor); d = VisualTreeHelper.GetParent(d))
            if (d is Visual v) y += VisualTreeHelper.GetOffset(v).Y;
        return y;
    }

    /// <summary>Where every group panel and track row is now (before an order change), for <see cref="AnimateReorder"/>.</summary>
    public Dictionary<object, double> CaptureLayout()
    {
        if (_pendingLayout is { } pending) { _pendingLayout = null; return pending; }   // a drop: from where the rows visibly are, not their slots
        var tops = new Dictionary<object, double>();
        static double Shift(FrameworkElement e) => e.RenderTransform is TranslateTransform t ? t.Y : 0;
        foreach (var (group, panel) in _groupPanels) tops[group] = LayoutTop(panel, _groups) + Shift(panel);
        foreach (var (track, row) in _trackRows)
            tops[track] = LayoutTop(row, _groups) + Shift(row) + (_trackGroup.TryGetValue(track, out var g) && _groupPanels.TryGetValue(g, out var panel) ? Shift(panel) : 0);
        return tops;
    }

    private Dictionary<object, double>? _pendingLayout;

    /// <summary>After <see cref="Rebuild"/> in the new order: panels and rows glide from their old slots (same duration as the track list).</summary>
    public void AnimateReorder(Dictionary<object, double>? old)
    {
        if (old is null) return;
        var duration = UiMotion.DurationMilliseconds(ArrangementPanel.OrderAnimationMilliseconds);
        if (duration <= 0) return;
        UpdateLayout();
        var panelDelta = new Dictionary<string, double>();
        void Glide(Border element, double from)
        {
            if (Math.Abs(from) < 0.5) return;
            if (element.RenderTransform is not TranslateTransform t) element.RenderTransform = t = new TranslateTransform();
            t.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(duration))
            {
                EasingFunction = ArrangementPanel.OrderEase   // one shared easing and duration with the track list
            });
        }
        foreach (var (group, panel) in _groupPanels)
        {
            var delta = old.TryGetValue(group, out var top) ? top - LayoutTop(panel, _groups) : 0;
            panelDelta[group] = delta;
            Glide(panel, delta);
        }
        foreach (var (track, row) in _trackRows)
        {
            if (!old.TryGetValue(track, out var top)) continue;
            // The row sits inside its panel, which is itself gliding: only the difference is the row's own move.
            var inPanel = _trackGroup.TryGetValue(track, out var group) && panelDelta.TryGetValue(group, out var pd) ? pd : 0;
            Glide(row, top - LayoutTop(row, _groups) - inPanel);
        }
    }

    /// <summary>Right-click on a group row in the track list: scroll to that group and flash it.</summary>
    public void RevealGroup(string group)
    {
        if (!_groupPanels.ContainsKey(group)) return;
        SelectRow(null, group);
        Activate();
        // After layout (the window may have just opened), scroll to the group and flash it.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            if (!_groupPanels.TryGetValue(group, out var panel)) return;
            panel.BringIntoView();
            var ms = UiMotion.DurationMilliseconds(900);
            if (ms > 0) panel.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(ms)));
        }));
    }

    private void SelectRow(TrackModel? track, string? group)
    {
        _selTrack = track; _selGroup = track is null ? group : null;
        foreach (var (t, row) in _trackRows) MarkSelected(row, ReferenceEquals(t, _selTrack), $"Mixer strip: {t.Name}");
        foreach (var (g, row) in _groupRows) MarkSelected(row, g == _selGroup, $"Mixer group: {g}");
    }

    private static void MarkSelected(Border row, bool on, string name)
    {
        // Selected rows get an accent outline; the automation name says so too (state is not colour alone).
        if (on) row.SetResourceReference(Border.BorderBrushProperty, "AccentBrush"); else row.BorderBrush = Brushes.Transparent;
        System.Windows.Automation.AutomationProperties.SetName(row, on ? name + ", selected" : name);
    }

    private void NudgeSelected(int direction)
    {
        if (_selTrack is { } track && _host.Project.Tracks.Contains(track))
            _host.ReorderFromMixer(p => TrackOrdering.Nudge(p, track, direction), $"Moved '{track.Name}' {(direction < 0 ? "up" : "down")} in the mixer");
        else if (_selGroup is { } group)
            _host.ReorderFromMixer(p => TrackOrdering.NudgeGroup(p, group, direction), $"Moved the {group.ToLowerInvariant()} group {(direction < 0 ? "up" : "down")}");
    }

    private static bool IsInteractive(DependencyObject? source, DependencyObject row)
    {
        for (var d = source; d is not null && !ReferenceEquals(d, row); d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is System.Windows.Controls.Primitives.ButtonBase or Slider or ComboBox or ComboBoxItem or System.Windows.Controls.Primitives.Thumb or FxSplitButton) return true;
            if (d is TextBoxBase) return true;
        }
        return false;
    }

    // Drag state (one gesture at a time). The dragged row (or group panel) follows the pointer; a caret shows the drop slot.
    private FrameworkElement? _dragMoving;
    private Border? _dragCapture;
    private Point _dragOrigin;
    private bool _dragArmed;
    private (string? Group, int Index, double CaretY)? _drop;
    private Border? _caret;

    private void AttachDrag(Border handle, TrackModel? track, string? group, FrameworkElement moving)
    {
        handle.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (IsInteractive(e.OriginalSource as DependencyObject, handle)) return;
            SelectRow(track, group);
            _dragCapture = handle; _dragMoving = moving; _dragArmed = false; _drop = null;
            _dragOrigin = PointerSource.Position(e, _groups);
            handle.CaptureMouse();
            e.Handled = true;
        };
        handle.PreviewMouseMove += (_, e) =>
        {
            if (!ReferenceEquals(_dragCapture, handle) || !handle.IsMouseCaptured) return;
            var p = PointerSource.Position(e, _groups);
            if (!_dragArmed && Math.Abs(p.Y - _dragOrigin.Y) < 4) return;
            _dragArmed = true;
            if (moving.RenderTransform is not TranslateTransform t) moving.RenderTransform = t = new TranslateTransform();
            t.BeginAnimation(TranslateTransform.YProperty, null);
            t.Y = p.Y - _dragOrigin.Y;
            Panel.SetZIndex(moving, 1000);
            moving.Opacity = 0.9;
            var pointer = PointerSource.Position(e, _scroller);
            if (pointer.Y < 16) _scroller.ScrollToVerticalOffset(Math.Max(0, _scroller.VerticalOffset - 14));
            else if (pointer.Y > _scroller.ViewportHeight - 16) _scroller.ScrollToVerticalOffset(_scroller.VerticalOffset + 14);
            _drop = ComputeDrop(track, group, p.Y);
            SlideNeighbours(track, group);   // the opened gap shows the landing slot, so no caret
            ShowCaret(null);
            e.Handled = true;
        };
        handle.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!ReferenceEquals(_dragCapture, handle)) return;
            var armed = _dragArmed; var drop = _drop;
            _pendingLayout = armed && drop is not null ? CaptureLayout() : null;   // before the slide offsets are cleared
            EndDrag();
            handle.ReleaseMouseCapture();
            if (!armed || drop is not { } target) return;
            e.Handled = true;
            if (track is not null && target.Group is { } targetGroup)
                _host.ReorderFromMixer(p => TrackOrdering.MoveTrackToGroup(p, track, targetGroup, target.Index),
                    $"Moved '{track.Name}' in the mixer");
            else if (group is not null)
                _host.ReorderFromMixer(p => TrackOrdering.MoveGroup(p, group, target.Index), $"Moved the {group.ToLowerInvariant()} group in the mixer");
            _pendingLayout = null;
        };
        handle.LostMouseCapture += (_, _) => { if (ReferenceEquals(_dragCapture, handle)) EndDrag(); };
    }

    private readonly Dictionary<FrameworkElement, double> _slideTargets = new();

    /// <summary>Slides an element to a vertical offset with the same short ease as the track list's gap-opening (transform only, no relayout).</summary>
    private void SlideTo(FrameworkElement element, double target)
    {
        if (_slideTargets.TryGetValue(element, out var current) ? Math.Abs(current - target) < 0.1 : Math.Abs(target) < 0.1) return;
        _slideTargets[element] = target;
        if (element.RenderTransform is not TranslateTransform t) element.RenderTransform = t = new TranslateTransform();
        var ms = UiMotion.DurationMilliseconds(105);
        if (ms <= 0) { t.BeginAnimation(TranslateTransform.YProperty, null); t.Y = target; return; }
        t.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(target, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ArrangementPanel.OrderEase },
            System.Windows.Media.Animation.HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>
    /// While dragging: the other rows (or panels) slide to open the gap at the drop slot, like the track list. The final layout is
    /// simulated from the row heights, so nothing is measured per frame; only transforms animate.
    /// </summary>
    private void SlideNeighbours(TrackModel? track, string? group)
    {
        if (_drop is not { } drop) return;
        var panels = _groups.Children.OfType<Border>().Where(b => b.Tag is string n && _groupPanels.ContainsKey(n)).OrderBy(b => LayoutTop(b, _groups)).ToList();
        if (panels.Count == 0) return;
        static double Slot(FrameworkElement e) => e.ActualHeight + e.Margin.Top + e.Margin.Bottom;
        var members = new Dictionary<string, List<TrackModel>>();
        foreach (var p in panels)
        {
            var name = (string)p.Tag;
            members[name] = _trackRows.Where(r => _trackGroup.TryGetValue(r.Key, out var g) && g == name).OrderBy(r => LayoutTop(r.Value, _groups)).Select(r => r.Key).ToList();
        }
        var order = panels.Select(p => (string)p.Tag).ToList();
        if (track is null && group is not null)
        {
            order.Remove(group);
            order.Insert(Math.Clamp(drop.Index, 0, order.Count), group);
        }
        else if (track is not null && drop.Group is { } target && members.ContainsKey(target))
        {
            foreach (var list in members.Values) list.Remove(track);
            members[target].Insert(Math.Clamp(drop.Index, 0, members[target].Count), track);
        }
        var y = LayoutTop(panels[0], _groups);
        foreach (var name in order)
        {
            var panel = panels.First(p => (string)p.Tag == name);
            var groupRow = _groupRows[name];
            var head = LayoutTop(groupRow, panel) - groupRow.Margin.Top;
            var actualSlots = _trackRows.Where(r => _trackGroup.TryGetValue(r.Key, out var g) && g == name).Sum(r => Slot(r.Value));
            var tail = panel.ActualHeight - head - Slot(groupRow) - actualSlots;
            var top = y;
            var cur = top + head + Slot(groupRow);
            foreach (var t in members[name])
            {
                var row = _trackRows[t];
                if (!ReferenceEquals(t, track)) SlideTo(row, (cur + row.Margin.Top - top) - LayoutTop(row, panel));
                cur += Slot(row);
            }
            if (name != group) SlideTo(panel, top - LayoutTop(panel, _groups));
            y = cur + tail + panel.Margin.Bottom;
        }
    }

    private void EndDrag()
    {
        foreach (var element in _slideTargets.Keys)
            if (element.RenderTransform is TranslateTransform slide) { slide.BeginAnimation(TranslateTransform.YProperty, null); slide.Y = 0; }
        _slideTargets.Clear();
        if (_dragMoving is not null)
        {
            if (_dragMoving.RenderTransform is TranslateTransform t) { t.BeginAnimation(TranslateTransform.YProperty, null); t.Y = 0; }
            Panel.SetZIndex(_dragMoving, 0);
            _dragMoving.Opacity = 1;
        }
        _dragMoving = null; _dragCapture = null; _dragArmed = false; _drop = null;
        ShowCaret(null);
    }

    /// <summary>Where a dragged track (into which group, at which index) or group (at which position) would land for a pointer at <paramref name="y"/> (in the strips' coordinates).</summary>
    private (string? Group, int Index, double CaretY)? ComputeDrop(TrackModel? track, string? group, double y)
    {
        var panels = _groups.Children.OfType<Border>().Where(b => b.Tag is string name && _groupPanels.ContainsKey(name)).ToList();
        if (panels.Count == 0) return null;
        if (track is null)
        {
            var others = panels.Where(p => (string)p.Tag != group).ToList();
            var index = others.Count(p => LayoutTop(p, _groups) + p.ActualHeight / 2 < y);
            var caret = others.Count == 0 ? 0
                : index < others.Count ? LayoutTop(others[index], _groups) : LayoutTop(others[^1], _groups) + others[^1].ActualHeight;
            return (null, index, caret);
        }
        var target = panels.OrderBy(p => { var top = LayoutTop(p, _groups); return y < top ? top - y : y > top + p.ActualHeight ? y - top - p.ActualHeight : 0; }).First();
        var name = (string)target.Tag;
        var rows = _trackRows.Where(r => _trackGroup.TryGetValue(r.Key, out var g) && g == name && !ReferenceEquals(r.Key, track))
            .Select(r => r.Value).OrderBy(r => LayoutTop(r, _groups)).ToList();
        var at = rows.Count(r => LayoutTop(r, _groups) + r.ActualHeight / 2 < y);
        var caretY = rows.Count == 0 ? LayoutTop(target, _groups) + 40
            : at < rows.Count ? LayoutTop(rows[at], _groups) : LayoutTop(rows[^1], _groups) + rows[^1].ActualHeight;
        return (name, at, caretY);
    }

    private void ShowCaret(double? groupsY)
    {
        if (groupsY is not { } y) { _overlay.Children.Remove(_caret); return; }
        if (_caret is null)
        {
            _caret = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), IsHitTestVisible = false };
            _caret.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        }
        if (!_overlay.Children.Contains(_caret)) _overlay.Children.Add(_caret);
        var at = _groups.TranslatePoint(new Point(0, y), _overlay);
        _caret.Width = Math.Max(20, _groups.ActualWidth - 8);
        Canvas.SetLeft(_caret, 4);
        Canvas.SetTop(_caret, at.Y - 1.5);
    }

    /// <summary>Rebuilds the strips (tracks added / removed / regrouped, or the song changed).</summary>
    /// <summary>A mixer slider is being dragged (it holds the mouse capture).</summary>
    public bool IsDraggingSlider => System.Windows.Input.Mouse.Captured is Slider dragged && IsAncestorOf(dragged);

    /// <summary>A mixer slider drag ended: the host runs the refresh it held back during the drag.</summary>
    public event Action? SliderDragEnded;

    public void Rebuild()
    {
        // Never replace the rows under a slider that is being dragged: the drag would continue on a detached slider
        // (its pointer mapping gives the minimum and it keeps the capture). Rebuild once the drag ends instead.
        if (IsDraggingSlider)
        {
            _rebuildAfterDrag = true;
            return;
        }
        _rebuildAfterDrag = false;
        _building = true;
        try
        {
            var project = _host.Project;
            _grouping.SelectedItem = project.Mixer.Grouping;
            _groups.Children.Clear();
            _syncers.Clear();
            _trackRows.Clear(); _trackGroup.Clear(); _groupRows.Clear(); _groupPanels.Clear();
            _signature = Signature();
            _groups.Children.Add(ColumnHeader());
            _groups.Children.Add(MasterPanel());   // Master sits at the top, directly under the column headers
            // The same order as the track list: groups where their first track is, tracks in track order.
            foreach (var (group, members) in TrackOrdering.Layout(project))
                _groups.Children.Add(GroupPanel(group, members));
            if (_groups.Children.Count == 2)
                _groups.Children.Add(new TextBlock { Text = "This song has no tracks.", Margin = new Thickness(8) });
        }
        finally { _building = false; }
    }

    // Columns shared by group and track rows: name | FX | pitch | pan | volume | M S.
    private static Grid RowGrid()
    {
        var grid = new Grid();
        foreach (var width in new[] { 190.0, 66, 58, 112, 150, -1, 84 })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
        return grid;
    }

    private static void Put(Grid grid, FrameworkElement element, int column)
    {
        Grid.SetColumn(element, column);
        element.VerticalAlignment = VerticalAlignment.Center;
        if (element.Margin == default) element.Margin = new Thickness(4, 0, 4, 0);
        grid.Children.Add(element);
    }

    private static FrameworkElement ColumnHeader()
    {
        var grid = RowGrid();
        grid.Margin = new Thickness(9, 0, 9, 4);
        var names = new[] { "TRACK / GROUP", "FX", "MIDI", "PITCH", "PAN", "VOLUME", "" };
        for (var i = 0; i < names.Length; i++)
        {
            var t = new TextBlock { Text = names[i], FontSize = Services.ThemeService.MinFontSize, FontWeight = FontWeights.SemiBold };
            t.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
            Put(grid, t, i);
        }
        return grid;
    }

    private Border GroupPanel(string group, IReadOnlyList<TrackModel> members)
    {
        var rows = new StackPanel();
        var groupRow = (Border)GroupRow(group, members.Count);
        rows.Children.Add(groupRow);
        foreach (var track in members)
        {
            var row = (Border)TrackRow(track);
            _trackGroup[track] = group;
            rows.Children.Add(row);
        }
        var panel = new Border
        {
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(4),
            Margin = new Thickness(0, 0, 0, 8), Child = rows, Tag = group, RenderTransform = new TranslateTransform()
        };
        panel.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        panel.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
        _groupPanels[group] = panel;
        _groupRows[group] = groupRow;
        AttachDrag(groupRow, null, group, panel);
        MarkSelected(groupRow, group == _selGroup, $"Mixer group: {group}");
        return panel;
    }

    private FrameworkElement GroupRow(string group, int count)
    {
        MixerGroupLevels Edit() => _host.Project.Mixer.Edit(group);
        var grid = RowGrid();
        var name = NameBlock(group, $"{count} track{(count == 1 ? "" : "s")}", accent: true);
        var colourRow = new DockPanel();
        var swatch = new Button
        {
            Width = 16, Height = 16, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Top,
            ToolTip = $"Colour of the {group.ToLowerInvariant()}: click to choose (colours every track in the group)",
            Content = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = BrushOf(_host.GroupColour(group)) }
        };
        swatch.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = swatch };
            foreach (var (colourName, hex) in TrackControlWidgets.TrackColourPalette)
            {
                var item = new MenuItem { Header = colourName, Icon = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Background = BrushOf(hex) } };
                item.Click += (_, _) => { _host.SetGroupColour(group, hex); Rebuild(); };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        };
        DockPanel.SetDock(swatch, Dock.Left);
        colourRow.Children.Add(swatch);
        colourRow.Children.Add(name);
        Put(grid, colourRow, 0);
        // Group effects bus: FX opens its chain (the group's tracks sum into it before the master); power bypasses it.
        Put(grid, BusButton(group), 1);
        Put(grid, Stepper(() => _host.Project.Mixer.Levels(group).Pitch, -24, 24, v => v == 0 ? "0 st" : $"{v:+0;-0} st", v => Edit().Pitch = v, recompile: true,
            "Shifts every track in the group by semitones"), 3);
        Put(grid, PanSlider(() => _host.Project.Mixer.Levels(group).Pan + 64, v => Edit().Pan = v - 64, "Moves every track in the group left or right"), 4);
        Put(grid, Fader(() => _host.Project.Mixer.Levels(group).Volume, 0, 200, 100, v => Edit().Volume = v, "Group level in percent of each track's own volume (100 = unchanged)"), 5);
        Put(grid, MuteSolo(() => _host.Project.Mixer.Levels(group).Mute, () => _host.Project.Mixer.Levels(group).Solo, v => Edit().Mute = v, v => Edit().Solo = v), 6);
        return RowBorder(grid, accent: true);
    }

    /// <summary>FX split button of a group bus (group name) or the master (null): FX opens the chain, power bypasses it.</summary>
    private FxSplitButton BusButton(string? group)
    {
        BusChain Bus() => group is null ? _host.Project.Mixer.Master : _host.Project.Mixer.Bus(group);
        var existing = group is null ? _host.Project.Mixer.Master : _host.Project.Mixer.Buses.GetValueOrDefault(group);
        var fx = new FxSplitButton
        {
            ChainOn = existing?.On ?? true, PluginCount = existing?.Rig.Plugins.Count ?? 0, Width = 54, Height = 22, HorizontalAlignment = HorizontalAlignment.Left,
        };
        TooltipShortcuts.Bind(fx, group is null
                ? "Master effects: FX opens the chain applied to the whole mix (e.g. reverb on everything) · power: bypass it"
                : "Group effects bus: FX opens the chain the group's tracks sum into before the master · power: bypass it",
            group is null ? "Mixer.MasterFx" : null);   // the group command acts on the selected track's group, not on this row's
        fx.OpenChain += (_, _) => _host.OpenBusFx(group);
        fx.TogglePower += (_, _) =>
        {
            _host.BeginMixerEdit();
            var bus = Bus();
            MixerBuses.SetOn(bus, !bus.On);
            fx.ChainOn = bus.On;
            _host.MixerChanged(recompile: false);
        };
        _syncers.Add(() =>
        {
            var bus = group is null ? _host.Project.Mixer.Master : _host.Project.Mixer.Buses.GetValueOrDefault(group);
            fx.ChainOn = bus?.On ?? true; fx.PluginCount = bus?.Rig.Plugins.Count ?? 0;
        });
        return fx;
    }

    private bool MonitorActive() => _host.MonitorChain is { On: true } c && c.Rig.Plugins.Any(p => p.Enabled);

    /// <summary>The Master row under the groups: master FX, master pan (offset on every track) and the master volume (the main window's knob).</summary>
    private Border MasterPanel()
    {
        var grid = RowGrid();
        Put(grid, NameBlock("Master", "whole mix", accent: true), 0);
        Put(grid, BusButton(null), 1);
        // Monitoring FX: a different look on purpose (amber outline, headphones, "MON"): live only, never part of a render.
        var mon = new MonitorFxButton
        {
            Active = MonitorActive(), Margin = new Thickness(2, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left
        };
        TooltipShortcuts.Bind(mon, "Monitoring effects (e.g. speaker calibration). Heard live only; never included in renders or exports.", "Mixer.MonitorFx");
        mon.Click += (_, _) => _host.OpenMonitorFx();
        _syncers.Add(() => mon.Active = MonitorActive());
        Put(grid, mon, 2);
        Put(grid, Fader(() => _host.Project.Mixer.MasterPan, -64, 63, 0, v => _host.Project.Mixer.MasterPan = Math.Clamp(v, -64, 63),
            "Master pan: moves the whole mix left or right (−64 left … 0 centre … +63 right; double-click: centre)"), 4);
        Put(grid, Fader(() => _host.MasterVolume, 0, 100, 100, v => _host.MasterVolume = v,
            "Master volume for all tracks (0–100 %, the same as the main window's master knob; double-click: 100)"), 5);
        var panel = new Border
        {
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(4),
            Margin = new Thickness(0, 0, 0, 8), Child = RowBorder(grid, accent: true)
        };
        panel.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        panel.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
        return panel;
    }

    private FrameworkElement TrackRow(TrackModel track)
    {
        var grid = RowGrid();
        var title = NameBlock(track.Name, track.InstrumentName, accent: false);
        title.Margin = new Thickness(18, 0, 4, 0); // indented under its group
        Put(grid, title, 0);

        // Split button: "FX" opens the chain, the power part switches chain / plain MIDI.
        var fx = new FxSplitButton
        {
            ChainOn = track.SoundSource == SoundSources.Plugins, PluginCount = track.Rig.Plugins.Count,
            Width = 54, Height = 22, HorizontalAlignment = HorizontalAlignment.Left
        };
        System.Windows.Automation.AutomationProperties.SetName(fx, $"FX chain for {track.Name}");
        fx.OpenChain += (_, _) => _host.OpenFxChain(track);
        fx.TogglePower += (_, _) =>
        {
            _host.BeginMixerEdit();
            track.SoundSource = track.SoundSource == SoundSources.Plugins ? SoundSources.Midi : SoundSources.Plugins;
            fx.ChainOn = track.SoundSource == SoundSources.Plugins;
            _host.MixerChanged(recompile: true);
        };
        Put(grid, fx, 1);
        // The track's General MIDI sound while its chain is on (effects shape it when there is no VST instrument).
        var midi = new CheckBox
        {
            IsChecked = track.MidiSound, IsEnabled = track.SoundSource == SoundSources.Plugins, HorizontalAlignment = HorizontalAlignment.Center,
            ToolTip = "MIDI sound: the track's General MIDI instrument plays while the FX chain is on (the effects shape it when there is no VST instrument). Off: only a VST instrument sounds."
        };
        midi.Click += (_, _) =>
        {
            _host.BeginMixerEdit();
            Change(() => { track.MidiSound = midi.IsChecked == true; track.MidiSoundAuto = false; track.MidiSoundManualOff = !track.MidiSound; }, recompile: true);
        };
        fx.TogglePower += (_, _) => midi.IsEnabled = track.SoundSource == SoundSources.Plugins;
        Put(grid, midi, 2);
        Put(grid, Stepper(() => track.Transpose, -24, 24, v => v == 0 ? "0 st" : $"{v:+0;-0} st", v => track.Transpose = v, recompile: true,
            "Transposes this track by semitones"), 3);
        // Full-resolution model units (the same as the Track Properties knobs and the group rows):
        // volume 0..127 (default 100), pan -64 (left) … 0 (centre) … +63 (right), one unit per step.
        Put(grid, Fader(() => Math.Clamp(track.Pan, 0, 127) - 64, -64, 63, 0, v => track.Pan = Math.Clamp(64 + v, 0, 127),
            "Track pan (−64 left … 0 centre … +63 right; mouse wheel: 1 step, Ctrl+wheel: 8; double-click: centre)"), 4);
        Put(grid, Fader(() => Math.Clamp(track.Volume, 0, 127), 0, 127, 100, v => track.Volume = Math.Clamp(v, 0, 127),
            "Track volume (0-127; mouse wheel: 1 step, Ctrl+wheel: 8; double-click: 100)"), 5);
        Put(grid, MuteSolo(() => track.Mute, () => track.Solo, v => track.Mute = v, v => track.Solo = v), 6);
        var row = RowBorder(grid, accent: false);
        row.RenderTransform = new TranslateTransform();
        _trackRows[track] = row;
        AttachDrag(row, track, null, row);

        // Right-click: move the track to another group (its choice is saved with the song).
        var menu = new ContextMenu();
        var project = _host.Project;
        foreach (var group in MixerGroups.Names(project.Mixer.Grouping))
        {
            var target = group;
            var item = new MenuItem { Header = $"Move to {group}", IsCheckable = true, IsChecked = MixerGroups.GroupOf(project, track) == group };
            item.Click += (_, _) =>
            {
                _host.BeginMixerEdit();
                track.MixerGroup = MixerGroups.Family(track) == target ? null : target;
                _host.MixerChanged(recompile: true);
                Dispatcher.BeginInvoke(Rebuild);
            };
            menu.Items.Add(item);
        }
        row.ContextMenu = menu;
        var stripName = $"Mixer strip: {track.Name}{(track.Mute ? ", muted" : "")}{(track.Solo ? ", solo" : "")}";
        MarkSelected(row, ReferenceEquals(track, _selTrack), stripName);
        return row;
    }

    private static Border RowBorder(Grid grid, bool accent)
    {
        var border = new Border
        {
            Child = grid, Padding = new Thickness(4, 5, 4, 5), Margin = new Thickness(0, 1, 0, 1), CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent   // the selection outline lives here (no layout shift)
        };
        border.SetResourceReference(Border.BackgroundProperty, accent ? "Panel2Brush" : "WindowBrush");
        return border;
    }

    private static StackPanel NameBlock(string title, string subtitle, bool accent)
    {
        var panel = new StackPanel();
        var name = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = title };
        if (accent) name.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        panel.Children.Add(name);
        var sub = new TextBlock { Text = subtitle, FontSize = Services.ThemeService.MinFontSize, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = subtitle };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        panel.Children.Add(sub);
        return panel;
    }

    private void Change(Action apply, bool recompile)
    {
        if (_building) return;
        apply();
        _host.MixerChanged(recompile);
    }

    /// <summary>Horizontal slider in the track-row style (value on the handle, drag anywhere, double-click resets).</summary>
    private FrameworkElement Fader(Func<int> get, int min, int max, int defaultValue, Action<int> set, string tip, bool snap = false)
    {
        var value = get();
        var slider = new Slider
        {
            Minimum = min, Maximum = max, Value = value, ToolTip = tip, SmallChange = 1, LargeChange = Math.Max(1, (max - min) / 10.0),
            IsSnapToTickEnabled = snap, TickFrequency = 1,
            VerticalAlignment = VerticalAlignment.Center, Style = (Style)Application.Current.FindResource("ArrangementSlider")
        };
        System.Windows.Automation.AutomationProperties.SetName(slider, tip);
        slider.PreviewMouseLeftButtonDown += (_, _) => { if (!_building) _host.BeginMixerEdit(); };
        slider.PreviewKeyDown += (_, e) => { if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End && !_building) _host.BeginMixerEdit(); };
        TrackControlWidgets.AttachSmoothDrag(slider, defaultValue);
        slider.PreviewMouseWheel += (_, e) => { if (e.Delta != 0 && !_building) _host.BeginMixerEdit(); };
        TrackControlWidgets.AttachWheelStep(slider); // 1 unit per notch (Ctrl: 8), never scrolls the mixer
        slider.LostMouseCapture += (_, _) =>
        {
            if (_rebuildAfterDrag) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() => { if (_rebuildAfterDrag) Rebuild(); }));
            SliderDragEnded?.Invoke();
        };
        var last = value;
        slider.ValueChanged += (_, e) =>
        {
            var v = (int)Math.Round(e.NewValue);
            if (v == last) return;   // only real steps reach the song (no flood of identical edits while dragging)
            last = v;
            Change(() => set(v), recompile: false);
        };
        // In-place sync from the model (change handler suppressed); skipped while this slider is being dragged.
        _syncers.Add(() =>
        {
            var v = Math.Clamp(get(), min, max);
            if (v == last || slider.IsMouseCaptured) return;
            var was = _building; _building = true;
            try { last = v; slider.Value = v; } finally { _building = was; }
        });
        return slider;
    }

    /// <summary>Pan as an offset from centre: -64 (left) … 0 … +63 (right), like the track list.</summary>
    private FrameworkElement PanSlider(Func<int> get, Action<int> set, string tip) =>
        Fader(() => get() - 64, -64, 63, 0, v => set(v + 64), tip + " (−64 left … 0 centre … +63 right)");

    private FrameworkElement Stepper(Func<int> get, int min, int max, Func<int, string> format, Action<int> set, bool recompile, string tip)
    {
        var value = get();
        var current = value;
        var text = new TextBlock { Text = format(value), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 38, TextAlignment = TextAlignment.Center };
        Button Step(string label, int delta)
        {
            var b = new Button { Content = label, Width = 22, Height = 22, Padding = new Thickness(0) };
            b.Click += (_, _) =>
            {
                var next = Math.Clamp(current + delta, min, max);
                if (next == current) return;
                _host.BeginMixerEdit();
                current = next;
                text.Text = format(current);
                Change(() => set(current), recompile);
            };
            return b;
        }
        var row = new DockPanel { ToolTip = tip, LastChildFill = true };
        var down = Step("−", -1); DockPanel.SetDock(down, Dock.Left); row.Children.Add(down);
        var up = Step("+", 1); DockPanel.SetDock(up, Dock.Right); row.Children.Add(up);
        row.Children.Add(text);
        _syncers.Add(() => { var v = get(); if (v == current) return; current = v; text.Text = format(v); });
        return row;
    }

    private FrameworkElement MuteSolo(Func<bool> getMute, Func<bool> getSolo, Action<bool> setMute, Action<bool> setSolo)
    {
        // The same themed mute / solo buttons as the track rows.
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        Button Toggle(string icon, bool on, Func<bool> get, Action<bool> set, string tip)
        {
            var b = TrackControlWidgets.ToggleIconButton(icon, on, () =>
            {
                _host.BeginMixerEdit();
                if (_building) return;
                set(!get());
                _host.MuteSoloChanged();
                Dispatcher.BeginInvoke(new Action(SyncValues));
            }, tip);
            b.Width = 34; b.Height = 26; b.Margin = new Thickness(2, 0, 2, 0);
            b.Tag = on;
            return b;
        }
        void Slot(int index, string icon, Func<bool> get, Action<bool> set, string tip)
        {
            row.Children.Add(Toggle(icon, get(), get, set, tip));
            // In-place sync: only a toggle whose state changed is swapped for a freshly styled button.
            _syncers.Add(() =>
            {
                var on = get();
                if (row.Children[index] is Button old && old.Tag is bool shown && shown == on) return;
                row.Children[index] = Toggle(icon, on, get, set, tip);
            });
        }
        Slot(0, "IconMute", getMute, setMute, "Mute");
        Slot(1, "IconSolo", getSolo, setSolo, "Solo");
        return row;
    }

    private static Brush BrushOf(string hex) => TabForge.Visualization.ColourText.BrushOr(hex);
}
