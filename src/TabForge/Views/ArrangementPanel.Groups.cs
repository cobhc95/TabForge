using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TabForge.Controllers;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Visualization;

namespace TabForge.Views;

// ArrangementPanel: group headers in the track list (collapse, drag the whole group).
public sealed partial class ArrangementPanel
{
    // ---------- groups in the track list: a header per run of one group (collapse; drag the whole group) ----------
    private int _groupDragStart = -1, _groupDragCount, _groupDragTarget = -1;
    private Point _groupDragOrigin;
    private readonly List<UIElement> _groupDragElements = new();
    private Border? _groupDragCaret;

    private FrameworkElement GroupHeader(SongProject project, int start)
    {
        var group = MixerGroups.GroupOf(project, project.Tracks[start]);
        var count = 1;
        while (start + count < project.Tracks.Count && MixerGroups.GroupOf(project, project.Tracks[start + count]) == group) count++;
        var collapsed = project.Mixer.CollapsedGroups.Contains(group);
        var header = new Border
        {
            Height = GroupHeaderHeight, CornerRadius = new CornerRadius(6, 6, 0, 0), Margin = new Thickness(2, 3, 2, 0),
            BorderThickness = new Thickness(1, 1, 1, 0), Cursor = Cursors.Arrow, RenderTransform = new TranslateTransform(),
            ToolTip = $"{group}: click the arrow to {(collapsed ? "expand" : "collapse")} · drag to move the whole group"
        };
        header.SetResourceReference(Border.BackgroundProperty, "Panel2Brush");
        header.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        // Laid out on the same columns as the track rows (the chevron sits in the settings column, the name in the name column).
        var dock = new Grid { Margin = new Thickness(RowGridLeft - 3, 0, RowGridRight - 8, 0) };
        AddColumnDefinitions(dock);
        _rowGrids.Add(dock);
        var cells = new Dictionary<string, FrameworkElement>();
        var chevron = new Button
        {
            Width = 18, Height = 18, Padding = new Thickness(0), Margin = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Center, Focusable = false,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Center,
            Content = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(collapsed ? "M0,0 L5,4 L0,8 Z" : "M0,0 L8,0 L4,5 Z"), Stretch = Stretch.Uniform,
                Width = collapsed ? 6 : 9, Height = collapsed ? 9 : 6, Fill = (Brush)Application.Current.FindResource("TextBrush")
            },
            ToolTip = collapsed ? "Expand the group" : "Collapse the group"
        };
        chevron.Click += (_, _) => GroupCollapseToggled?.Invoke(group);
        cells["settings"] = chevron;
        var title = new TextBlock { Text = group, FontWeight = FontWeights.SemiBold, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var sub = new TextBlock { Text = $"  {count} track{(count == 1 ? "" : "s")}", FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        titleRow.Children.Add(title);
        titleRow.Children.Add(sub);
        cells["name"] = titleRow;

        // Group controls, same semantics as the mixer's group rows (offsets on top of each track's own values).
        var members = Enumerable.Range(start, count).Select(k => project.Tracks[k]).ToList();
        var levels = project.Mixer.Levels(group);
        void GroupEdit(Action<MixerGroupLevels> apply, bool muteSolo = false)
        {
            MixEditStarting?.Invoke(this, EventArgs.Empty);   // one undo step per gesture, like the track sliders
            apply(project.Mixer.Edit(group));
            MixChanged?.Invoke(this, EventArgs.Empty);
            MixEditEnded?.Invoke(this, EventArgs.Empty);
            if (muteSolo) MuteSoloChanged?.Invoke(this, EventArgs.Empty);
        }

        // No record-arm on a group row (only tracks are armed); the slot stays empty so the columns line up.

        // FX: the group's effects bus (the same button as the mixer's group row): FX opens its chain, power bypasses it.
        var bus = project.Mixer.Buses.GetValueOrDefault(group);
        var fx = new FxSplitButton
        {
            ChainOn = bus?.On ?? true, PluginCount = bus?.Rig.Plugins.Count ?? 0, Width = 50, Height = 18,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            ToolTip = "Group effects bus: FX opens the chain this group's tracks sum into before the master · power: bypass it"
        };
        fx.OpenChain += (_, _) => BusFxRequested?.Invoke(this, group);
        fx.TogglePower += (_, _) => BusPowerRequested?.Invoke(this, group);
        cells["fx"] = fx;

        var mute = ToggleIconButton("IconMute", levels.Mute, () => GroupEdit(l => l.Mute = !l.Mute, muteSolo: true), "Mute every track in the group");
        var solo = ToggleIconButton("IconSolo", levels.Solo, () => GroupEdit(l => l.Solo = !l.Solo, muteSolo: true), "Solo every track in the group");
        mute.Height = solo.Height = 20;
        cells["mute"] = mute;
        cells["solo"] = solo;

        Slider GroupSlider(int min, int max, int value, int def, Action<MixerGroupLevels, int> set, Func<MixerGroupLevels, int> get, string tip)
        {
            var slider = new Slider
            {
                Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), IsSnapToTickEnabled = true, TickFrequency = 1,
                SmallChange = 1, LargeChange = 8, Margin = new Thickness(6, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)Application.Current.FindResource("ArrangementSlider"), ToolTip = tip
            };
            slider.ValueChanged += (_, e) =>
            {
                var v = (int)Math.Round(e.NewValue);
                if (get(project.Mixer.Levels(group)) == v) return;
                set(project.Mixer.Edit(group), v);
                MixChanged?.Invoke(this, EventArgs.Empty);
            };
            AttachMixEditGestures(slider);
            _mixSliders.Add((slider, () => get(project.Mixer.Levels(group))));
            AttachSmoothDrag(slider, def);
            AttachWheelStep(slider);
            return slider;
        }
        cells["volume"] = GroupSlider(0, 200, levels.Volume, 100, (l, v) => l.Volume = v, l => l.Volume,
            "Group level in percent of each track's own volume (100 = unchanged)");
        cells["pan"] = GroupSlider(-64, 63, levels.Pan, 0, (l, v) => l.Pan = v, l => l.Pan,
            "Moves every track in the group left or right (−64 … +63)");
        PlaceCells(dock, cells);
        header.Child = dock;
        header.MouseEnter += (_, _) => header.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
        header.MouseLeave += (_, _) => header.SetResourceReference(Border.BackgroundProperty, "Panel2Brush");
        header.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (IsInside(e.OriginalSource as DependencyObject, chevron) || IsInteractiveTrackControl(e.OriginalSource as DependencyObject, header)) return;
            _groupDragStart = start; _groupDragCount = count; _groupDragTarget = -1;
            _groupDragOrigin = e.GetPosition(_controls);
            header.CaptureMouse();
            e.Handled = true;
        };
        header.MouseMove += (_, e) =>
        {
            if (_groupDragStart != start || !header.IsMouseCaptured) return;
            var p = e.GetPosition(_controls);
            var dy = p.Y - _groupDragOrigin.Y;
            if (_groupDragTarget < 0 && Math.Abs(dy) < 4) return;
            // The header and its rows follow the pointer; a caret shows where the group will land.
            if (_groupDragElements.Count == 0)
            {
                var from = _controls.Children.IndexOf(header);
                for (var k = from; k < _controls.Children.Count; k++)
                {
                    var el = _controls.Children[k];
                    if (k > from && el is Border { Tag: "group-header" }) break;
                    _groupDragElements.Add(el);
                    if (el is UIElement u) { u.Opacity = 0.85; Panel.SetZIndex(u, 10); }
                }
            }
            foreach (var el in _groupDragElements)
                if (el.RenderTransform is TranslateTransform t) t.Y = dy; else el.RenderTransform = new TranslateTransform(0, dy);
            _groupDragTarget = GroupDropIndex(project, p.Y);
            ShowGroupCaret(project, _groupDragTarget);
            e.Handled = true;
        };
        header.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_groupDragStart != start) return;
            var target = _groupDragTarget;
            EndGroupDrag();
            header.ReleaseMouseCapture();
            if (target >= 0 && (target < start || target > start + count)) GroupMoved?.Invoke(start, count, target);
            e.Handled = true;
        };
        header.LostMouseCapture += (_, _) => { if (_groupDragStart == start) EndGroupDrag(); };
        // Right-click anywhere on the row opens the Mixer at this group, except on a control with its own menu.
        header.MouseRightButtonUp += (_, e) =>
        {
            for (var d = e.OriginalSource as DependencyObject; d is not null && !ReferenceEquals(d, header); d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d is FrameworkElement { ContextMenu: not null }) return;
            GroupMixerRequested?.Invoke(group);
            e.Handled = true;
        };
        header.Tag = "group-header";
        return header;
    }

    /// <summary>
    /// The menu for empty space in the track list header strip, its column header row and the empty rows area:
    /// "Show tracks in groups" first (the same setting as the Mixer's "Groups in track list"), then the area's own
    /// items, then the dock pane's (reset / close panel).
    /// </summary>
    internal ContextMenu BuildEmptyAreaMenu(IEnumerable<Control>? own = null)
    {
        var menu = new ContextMenu { Style = (Style)Application.Current.FindResource(typeof(ContextMenu)) };
        var groups = new MenuItem
        {
            Header = "Show tracks in groups", IsCheckable = true, IsChecked = GroupsShownState?.Invoke() ?? ShowsGroups(_project),
            Style = (Style)Application.Current.FindResource(typeof(MenuItem)),
            ToolTip = "Show a header per mixer group (guitars, basses...) in the track list, which can be collapsed and dragged as a whole"
        };
        groups.Click += (_, _) => GroupsToggleRequested?.Invoke(groups.IsChecked);
        menu.Items.Add(groups);
        var ownItems = own?.ToList() ?? new List<Control>();
        if (ownItems.Count > 0) { menu.Items.Add(new Separator()); foreach (var item in ownItems) menu.Items.Add(item); }
        var dock = DockMenuItems?.Invoke().ToList();
        if (dock is { Count: > 0 }) { menu.Items.Add(new Separator()); foreach (var item in dock) menu.Items.Add(item); }
        return menu;
    }

    /// <summary>Each track row's and group header's top before an order change (see <see cref="AnimateReorder"/>).</summary>
    internal Dictionary<object, double> CaptureRowTops()
    {
        var tops = new Dictionary<object, double>();
        if (_project is null) return tops;
        for (var i = 0; i < _project.Tracks.Count; i++) tops[_project.Tracks[i]] = RowTopOf(_project, i);
        if (ShowsGroups(_project)) foreach (var (key, top) in HeaderTops(_project)) tops[key] = top;
        return tops;
    }

    /// <summary>Group headers keyed "group|name|n" (n-th run of that group) with their top in the rows area.</summary>
    private static IEnumerable<(string Key, double Top)> HeaderTops(SongProject project)
    {
        var seen = new Dictionary<string, int>();
        foreach (var run in GroupRuns(project))
        {
            seen[run.Group] = seen.GetValueOrDefault(run.Group) + 1;
            yield return ($"group|{run.Group}|{seen[run.Group]}", RowTopOf(project, run.Start) - GroupHeaderHeight);
        }
    }

    /// <summary>Self-test hook: the timeline is gliding its lanes.</summary>
    internal bool TimelineAnimatingLanes => _timeline.IsAnimatingLanes;

    /// <summary>The shared movement easing (track list, timeline lanes and Mixer).</summary>
    internal static readonly IEasingFunction OrderEase = new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>
    /// After the rows were rebuilt in the new order: every moved row, group header and timeline lane glides from where it was to
    /// where it is now (one short transform animation each, no relayout per frame).
    /// </summary>
    internal void AnimateReorder(Dictionary<object, double> oldTops)
    {
        if (_project is null) return;
        var duration = UiMotion.DurationMilliseconds(OrderAnimationMilliseconds);
        if (duration <= 0) return;
        void Glide(TranslateTransform t, double delta)
        {
            if (Math.Abs(delta) < 0.5) return;
            t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(delta, 0, TimeSpan.FromMilliseconds(duration)) { EasingFunction = OrderEase });
        }
        var laneDeltas = new double[_project.Tracks.Count];
        for (var i = 0; i < _project.Tracks.Count; i++)
        {
            if (!oldTops.TryGetValue(_project.Tracks[i], out var oldTop)) continue;
            laneDeltas[i] = oldTop - RowTopOf(_project, i);
            if (i < _rowTransforms.Count) Glide(_rowTransforms[i], laneDeltas[i]);
        }
        if (ShowsGroups(_project))
        {
            var headers = _controls.Children.OfType<Border>().Where(b => b.Tag as string == "group-header").ToList();
            var k = 0;
            foreach (var (key, top) in HeaderTops(_project))
            {
                if (k < headers.Count && oldTops.TryGetValue(key, out var oldTop) && headers[k].RenderTransform is TranslateTransform t) Glide(t, oldTop - top);
                k++;
            }
        }
        _timeline.AnimateLanes(laneDeltas, duration);
    }

    /// <summary>The track list and the Mixer play their reorder animation with this duration, started in the same UI turn.</summary>
    internal const double OrderAnimationMilliseconds = 160;

    /// <summary>Where a dragged group goes: before the group header (or the end) nearest the pointer.</summary>
    private static int GroupDropIndex(SongProject project, double y)
    {
        var best = project.Tracks.Count; var bestDistance = double.MaxValue;
        foreach (var run in GroupRuns(project).Select(r => r.Start).Append(project.Tracks.Count))
        {
            var top = run < project.Tracks.Count ? RowTopOf(project, run) - GroupHeaderHeight : RowsHeight(project);
            var distance = Math.Abs(y - top);
            if (distance < bestDistance) { bestDistance = distance; best = run; }
        }
        return best;
    }

    private void ShowGroupCaret(SongProject project, int target)
    {
        if (_groupDragCaret is null)
        {
            _groupDragCaret = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), IsHitTestVisible = false, Margin = new Thickness(4, 0, 4, 0) };
            _groupDragCaret.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        }
        _controls.Children.Remove(_groupDragCaret);
        var before = target < project.Tracks.Count ? RowTopOf(project, target) - GroupHeaderHeight : RowsHeight(project);
        _groupDragCaret.RenderTransform = new TranslateTransform(0, 0);
        // Put the caret into the panel next to the element at that height (StackPanel order).
        var index = 0; double y = 0;
        foreach (UIElement el in _controls.Children)
        {
            if (y >= before - 0.5) break;
            y += el.Visibility == Visibility.Collapsed ? 0 : (el as FrameworkElement)?.ActualHeight + ((el as FrameworkElement)?.Margin.Top ?? 0) ?? 0;
            index++;
        }
        _controls.Children.Insert(Math.Min(index, _controls.Children.Count), _groupDragCaret);
    }

    private void EndGroupDrag()
    {
        foreach (var el in _groupDragElements) { el.Opacity = 1; Panel.SetZIndex(el, 0); if (el.RenderTransform is TranslateTransform t) t.Y = 0; }
        _groupDragElements.Clear();
        if (_groupDragCaret is not null) _controls.Children.Remove(_groupDragCaret);
        _groupDragStart = -1;
        _groupDragTarget = -1;
    }

    // The track keeps its own instrument (guitar, bass...) whatever plug-ins its FX chain holds.
    private static string InstrumentLabel(TrackModel track) => track.InstrumentName;
}
