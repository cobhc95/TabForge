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

using static TabForge.Views.TrackControlWidgets;
using static TabForge.Views.TrackColumnLayout;

using static TabForge.Views.TrackRowWidgets;

namespace TabForge.Views;

// Owns: the track list's group headers: the group header rows, the empty-area menu, the group-move and reset-height events, and
//   the animation that slides rows to their new tops after a reorder.
// Does not own: the group drag gesture (GroupDragController.cs) and the group runs and row heights
//   (ArrangementPanel.RowGeometry.cs).
// Tests: TestTrackListGroupRows, TestTrackListCollapse.

public sealed partial class ArrangementPanel : IGroupDragHost
{
    // ---------- groups in the track list: a header per run of one group (collapse; drag the whole group) ----------
    Panel IGroupDragHost.TrackListPanel => _controls;
    void IGroupDragHost.RaiseGroupMoved(int start, int count, int target) => GroupMoved?.Invoke(start, count, target);

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
        _columns.AddRowGrid(dock);
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
        var sub = new TextBlock { Text = $"  {count} track{(count == 1 ? "" : "s")}", FontSize = Services.ThemeService.MinFontSize, VerticalAlignment = VerticalAlignment.Center };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
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
            InMuteSoloGesture = muteSolo;
            try { MixChanged?.Invoke(this, EventArgs.Empty); }
            finally { InMuteSoloGesture = false; }
            MixEditEnded?.Invoke(this, EventArgs.Empty);
            if (muteSolo) { ApplyMuteVisualsNow(); MuteSoloChanged?.Invoke(this, EventArgs.Empty); }
        }

        // No record-arm on a group row (only tracks are armed); the slot stays empty so the columns line up.

        // FX: the group's effects bus (the same button as the mixer's group row): FX opens its chain, power bypasses it.
        var bus = project.Mixer.Buses.GetValueOrDefault(group);
        var fx = new FxSplitButton
        {
            ChainOn = bus?.On ?? true, PluginCount = bus?.Rig.Plugins.Count ?? 0, Width = 50, Height = 18,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
        };
        TooltipShortcuts.Bind(fx, "Group effects bus: FX opens the chain this group's tracks sum into before the master · power: bypass it", null);
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
            _rowWidgets.AttachMixEditGestures(slider);
            _mixSliders.Add((slider, () => get(project.Mixer.Levels(group))));
            AttachSmoothDrag(slider, def);
            AttachWheelStep(slider);
            return slider;
        }
        cells["volume"] = GroupSlider(0, 200, levels.Volume, 100, (l, v) => l.Volume = v, l => l.Volume,
            "Group level in percent of each track's own volume (100 = unchanged)");
        cells["pan"] = GroupSlider(-64, 63, levels.Pan, 0, (l, v) => l.Pan = v, l => l.Pan,
            "Moves every track in the group left or right (−64 … +63)");
        _columns.PlaceCells(dock, cells);
        header.Child = dock;
        header.MouseEnter += (_, _) => header.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
        header.MouseLeave += (_, _) => header.SetResourceReference(Border.BackgroundProperty, "Panel2Brush");
        _groupDrag.Attach(header, project, start, count, e => IsInside(e.OriginalSource as DependencyObject, chevron) || IsInteractiveTrackControl(e.OriginalSource as DependencyObject, header));
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
    /// <summary>"Reset track list height" in the track list and timeline menus (TrackListFitController resets it).</summary>
    public event Action? ResetTrackListHeightRequested;
    public void RequestResetTrackListHeight() => ResetTrackListHeightRequested?.Invoke();

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
        // Track colours live here, not under "+ Track": they colour existing tracks (the colour per group is a Preferences row).
        var colours = new MenuItem { Header = "Colours", Style = (Style)Application.Current.FindResource(typeof(MenuItem)) };
        var byGroup = new MenuItem
        {
            Header = "Colour tracks by group", Style = (Style)Application.Current.FindResource(typeof(MenuItem)),
            ToolTip = "Give every track the colour of its group (guitars, basses, drums...). The colour of each group is set in Settings > Appearance > Track colours."
        };
        byGroup.Click += (_, _) => ColourByGroupRequested?.Invoke();
        var pick = new MenuItem { Header = "Colour tracks…", Style = (Style)Application.Current.FindResource(typeof(MenuItem)) };
        pick.Click += (_, _) => ColourTracksRequested?.Invoke();
        colours.Items.Add(byGroup);
        colours.Items.Add(pick);
        menu.Items.Add(colours);
        var ownItems = own?.ToList() ?? new List<Control>();
        if (ownItems.Count > 0) { menu.Items.Add(new Separator()); foreach (var item in ownItems) menu.Items.Add(item); }
        menu.Items.Add(new Separator());
        var resetHeight = new MenuItem
        {
            Header = "Reset track list height", Style = (Style)Application.Current.FindResource(typeof(MenuItem)),
            ToolTip = "Back to the default row height with every track in view (the same as double-clicking the border above the track list)"
        };
        resetHeight.Click += (_, _) => ResetTrackListHeightRequested?.Invoke();
        menu.Items.Add(resetHeight);
        var settings = new MenuItem
        {
            Header = "Track list settings…", Style = (Style)Application.Current.FindResource(typeof(MenuItem)),
            ToolTip = "Open Settings > Timeline & Tracks (groups in new songs, automatic sizing, pan and volume controls)"
        };
        settings.Click += (_, _) => TrackListSettingsRequested?.Invoke();
        menu.Items.Add(settings);
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
}
