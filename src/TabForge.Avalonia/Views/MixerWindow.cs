using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The Mixer (Tools > Mixer…), the mixer window: the grouping choice, the
/// track-list options, the column headers, the Master row, then one panel per group (a group row and its track strips) in the
/// TrackOrdering.Layout order. Each track strip has its colour, name, FX power, MIDI sound, pitch, pan, volume with a meter, and
/// mute / solo. Values change the song in place; the preview has no engine, so the meters show a static sample level and the FX,
/// and audio settings button opens the Settings window on Audio &amp; Plug-ins (the preview reports "needs the full app").
/// Owns: the Avalonia layout of the mixer and its in-place edits. Does not own: the strip texts and rules (MixerStripPresenter), the
/// order (TrackOrdering), the groups (MixerGroups).
/// The preview has no engine or main window: the live input row is a static twin and the FX buttons open the chain windows on the song's own chains.
/// The interaction half (colour menus, context menus, drag reorder, selection, FX openers) is in MixerWindow.Interaction.cs.
/// Tests: TabForge.Avalonia.Tests.MixerWindowTests.
/// </summary>
public sealed partial class MixerWindow : Window
{
    /// <summary>The column headers: name | FX | MIDI | pitch | pan | volume | mute and solo.</summary>
    public static readonly string[] ColumnNames = { "TRACK / GROUP", "FX", "MIDI", "PITCH", "PAN", "VOLUME", "" };

    /// <summary>The top of the chorus and reverb send sliders (the MIDI controller range 0-127).</summary>
    private const int SendMax = MidiLimits.Max;

    public const string Hint =
        "Group rows add to every track in the group (pan and pitch are offsets, volume is a percentage of each track's own). " +
        "Drag a track or group row to reorder (the Move track up and down commands also move the selected row); right-click a track row to move it to another group. Double-click a slider to reset it.";

    private readonly SongDocument _document;
    // The document's song now: an undo or redo replaces the project object, so it is read each time, never kept.
    private SongProject _project => _document.Project;
    // Listens on the DOCUMENT while the window is open: a replaced song rebuilds the strips for its tracks.
    private readonly DocumentWatch _watch = new();
    private readonly IUiThread _ui;
    private readonly NextFrameCoalescer? _liveMixChanged; // pushes the track levels to the engine; UI thread only
    private readonly Func<int>? _masterPercent; // the process's master volume (the transport knob's value); null: the window keeps its own
    private readonly Action<int>? _setMasterPercent;
    private readonly Action? _openAudioSettings; // opens the Settings window on Audio & Plug-ins; null in the preview
    private readonly StackPanel _groups = new() { Name = "MixerGroups" };
    private readonly ComboBox _grouping = new() { Name = "Grouping", Width = 150, ItemsSource = MixerGrouping.Offered };
    private readonly Button _collapseAll = new() { Name = "CollapseAll", Padding = new Thickness(8, 3), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = ToolWindowParts.Status("MixerStatus");
    private bool _building;
    private Size? _openingSize; // the size ApplyInitialSize chose; opening at it is not a user resize to remember

    /// <param name="document">The song the mixer edits: each gesture is one undo step of it, and its changes mark it dirty.</param>
    /// <param name="liveMixChanged">Called after a track's pan, volume, mute or solo changes, so the engine takes the level now; null does nothing.</param>
    /// <param name="masterPercent">Reads the master volume (0-100) the Master slider shows; null keeps the window's own value.</param>
    /// <param name="setMasterPercent">Sets the master volume everywhere (the playback engines and the transport knob); null keeps the window's own value.</param>
    /// <param name="openAudioSettings">Opens the Settings window on Audio &amp; Plug-ins; null (the preview) makes that button say it needs the full app.</param>
    /// <param name="sizing">The saved settings whose Mixer size the window opens at and writes back as it is resized; null keeps the default size.</param>
    /// <param name="saveSettings">Saves <paramref name="sizing"/> after a resize (the debounced store, as the Settings window does).</param>
    public MixerWindow(SongDocument document, Action? liveMixChanged = null, Func<int>? masterPercent = null, Action<int>? setMasterPercent = null,
        Action? openAudioSettings = null, AppSettings? sizing = null, Action? saveSettings = null)
    {
        _document = document;
        _watch.ProjectChanged = (_, current) =>
        {
            _selectedTrack = _selectedTrack is { } selected ? current.Tracks.FirstOrDefault(track => track.Id == selected.Id) : null;   // the same track in the restored song
            if (_stripsBuilt) Rebuild();
        };
        _watch.Marked = _ => SyncValues();   // a plain edit elsewhere (a rename, a mute from the track list) shows here too
        _watch.Bind(document);
        Closed += (_, _) => { _watch.Dispose(); _liveMixChanged?.Cancel(); };
        _ui = AvaloniaUiThread.ForApplication();
        _masterPercent = masterPercent;
        _setMasterPercent = setMasterPercent;
        _openAudioSettings = openAudioSettings;
        _liveMixChanged = liveMixChanged is null ? null : new NextFrameCoalescer(_ui, liveMixChanged);
        Title = "Mixer";
        if (sizing is not null) PropertyChanged += (_, e) => RememberSize(sizing, saveSettings, e);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");

        var top = BuildTopBar();
        var footer = BuildFooter();

        var root = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(footer);
        // The rows keep their compact minimum width; below it the viewport scrolls sideways instead of clipping them.
        _groups.MinWidth = MixerColumnLayout.MinCompactWidth;
        // Kept in the field (not looked up in the logical tree) so the compact decision reads this rows host's own width, not the
        // content's (whose MinWidth is the compact floor and which may scroll sideways below it).
        _rowsViewport = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _groups };
        root.Children.Add(_rowsViewport);
        Content = WindowUiScale.Attach(this, root, sizing?.Appearance, ApplyUiScale);
        ApplyInitialSize(sizing);
        // A theme switch repaints the existing strips (every strip brush is a DynamicResource); it never recreates them.
        ActualThemeVariantChanged += (_, _) => _groups.InvalidateVisual();
        AttachCompactLayout();
        // The window opens with its shell first (the column header, Master and the loading placeholder); the strips build on the next UI pass.
        BuildShell();
        _ui.Post(EnsureStripsBuilt, UiPriority.Background);
    }

    /// <summary>Showing the window builds the strips when the deferred pass has not run yet, so the rows exist on open.</summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        EnsureStripsBuilt();
    }

    /// <summary>The grouping row (group choice, Group rules, collapse and the column toggles) with the Audio and Plug-ins settings button on its right.</summary>
    private DockPanel BuildTopBar()
    {
        var groupingRow = new WrapPanel { Orientation = Orientation.Horizontal };
        var groupLabel = new TextBlock { Text = "Group tracks", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        _topBarLabel = groupLabel;
        groupingRow.Children.Add(groupLabel);
        ToolTip.SetTip(_grouping, "By instrument: guitars, basses, keys (piano, organ, synth), drums, other.\nCompact: guitars, basses, drums, and all other instruments together.\nNo groups: one group for the whole song.");
        _grouping.SelectionChanged += (_, _) =>
        {
            if (_building || _grouping.SelectedItem is not string mode || mode == _project.Mixer.Grouping) return;
            ClickEdit(() => _project.Mixer.Grouping = mode);
            Rebuild();
        };
        groupingRow.Children.Add(_grouping);
        var rules = new Button { Name = "GroupRules", Content = "Group rules…", Padding = new Thickness(8, 3), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(rules, "Choose which instruments go in which group: add, rename, reorder groups and set their rules");
        rules.Click += (_, _) => OpenGroupRules();
        groupingRow.Children.Add(rules);
        _collapseAll.Click += (_, _) => SetCollapsed(_groupNames, !_groupNames.All(IsCollapsed));
        groupingRow.Children.Add(_collapseAll);
        foreach (var (what, label, tip) in new[]
        {
            ("volume", "Volume in track list", "Show the volume column in the main track list"),
            ("pan", "Pan in track list", "Show the pan column in the main track list"),
            ("groups", "Groups in track list", "Show the mixer groups in the main track list: a box per group, which can be collapsed and dragged as a whole"),
        })
        {
            var box = new CheckBox { Name = $"Show-{what}", Content = label, IsChecked = what != "groups" || _project.Mixer.ShowGroupsInTrackList, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(what == "volume" ? 16 : 0, 3, 12, 3) };
            ToolTip.SetTip(box, tip);
            if (what == "groups") box.IsCheckedChanged += (_, _) => ClickEdit(() => _project.Mixer.ShowGroupsInTrackList = box.IsChecked == true);
            _topBarChecks.Add(box);
            groupingRow.Children.Add(box);
        }
        var settings = new Button { Name = "AudioSettings", Content = "Audio & Plug-ins settings…", Padding = new Thickness(10, 3), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        settings.Click += (_, _) => { if (_openAudioSettings is null) Report("Audio & Plug-ins settings"); else _openAudioSettings(); };
        ToolWindowParts.NotAvailable(settings, _openAudioSettings is null);
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = groupingRow };
        var menuButton = TopBarMenuButton();
        _topBarScroll = scroll;
        _topBarMenu = menuButton;
        DockPanel.SetDock(menuButton, Dock.Right);
        DockPanel.SetDock(settings, Dock.Right);
        top.Children.Add(menuButton);
        top.Children.Add(settings);
        top.Children.Add(scroll);
        ApplyTopBarMode();
        return top;
    }

    /// <summary>The hint under the groups and the status line.</summary>
    private StackPanel BuildFooter()
    {
        var footer = new StackPanel();
        footer.Children.Add(new TextBlock
        {
            Name = "MixerHint", Text = Hint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), FontSize = 11,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted"),
        });
        footer.Children.Add(_status);
        return footer;
    }

    private readonly List<string> _groupNames = new();

    private bool IsCollapsed(string group) => _project.Mixer.CollapsedGroups.Contains(group);

    private void SetCollapsed(IReadOnlyList<string> groups, bool? collapsed)
    {
        foreach (var group in groups.ToList())
        {
            var on = collapsed ?? !IsCollapsed(group);
            _project.Mixer.CollapsedGroups.Remove(group);
            if (on) _project.Mixer.CollapsedGroups.Add(group);
        }
        Rebuild();
    }

    private void Report(string work) => _status.Text = ToolWindowParts.NeedsFullApp(work);

    /// <summary>Writes the window's size into the saved settings when it is a normal, usable size that changed, then saves them (as the Settings window does).</summary>
    private void RememberSize(AppSettings sizing, Action? save, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Visual.BoundsProperty || WindowState != WindowState.Normal) return;
        if (_updatingSizeLimits) return;
        var width = Bounds.Width; var height = Bounds.Height;
        // Opening at the clamped remembered size (or the default) is not a size the user chose to remember.
        if (_openingSize is { } opening && Math.Abs(width - opening.Width) < 1.0 && Math.Abs(height - opening.Height) < 1.0) return;
        if (width < MinWidth || height < MinHeight) return;
        // A scale change grows the minimum and the window with it; that is not a size the user chose to remember.
        if (Math.Abs(width - MinWidth) < 1.0 || Math.Abs(height - MinHeight) < 1.0) return;
        if (sizing.MixerWindowWidth == width && sizing.MixerWindowHeight == height) return;
        sizing.MixerWindowWidth = width;
        sizing.MixerWindowHeight = height;
        save?.Invoke();
    }

    /// <summary>Mixer.GroupRules and the Group rules button: the group rules editor over this window.</summary>
    public void OpenGroupRules() => new GroupRulesWindow(_project.Mixer).ShowDialog<GroupRulesResult?>(this);

    /// <summary>Rebuilds every strip: once per structure change (grouping, collapse), never per value change.</summary>
    public void Rebuild()
    {
        _building = true;
        try
        {
            BuildHead();
            BuildGroupPanels();
        }
        finally { _building = false; }
        _stripsBuilt = true;
        FinishBuild();
    }

    /// <summary>The shared head of a build: the grouping choice, the column header and the Master row.</summary>
    private void BuildHead()
    {
        _grouping.SelectedItem = _project.Mixer.Grouping == MixerGrouping.Compact ? MixerGrouping.ByInstrument : _project.Mixer.Grouping;
        _groups.Children.Clear();
        _groupNames.Clear();
        _trackRows.Clear(); _trackGroup.Clear(); _groupRows.Clear(); _groupPanels.Clear();
        _groups.Children.Add(ColumnHeader());
        _groups.Children.Add(MasterPanel());
    }

    /// <summary>The group panels of a build: one panel per group in <see cref="TrackOrdering.Layout"/> order, or the no-tracks message.</summary>
    private void BuildGroupPanels()
    {
        foreach (var (group, members) in TrackOrdering.Layout(_project))
        {
            _groupNames.Add(group);
            _groups.Children.Add(BuildGroupPanel(group, members));
        }
        if (MixerStripPresenter.ShowsNoTracksMessage(_project))
            _groups.Children.Add(new TextBlock { Name = "NoTracks", Text = MixerStripPresenter.NoTracksMessage, Margin = new Thickness(8) });
    }

    /// <summary>One group's panel (its group row and, unless collapsed, its track strips); registers them for selection and drag.</summary>
    private Border BuildGroupPanel(string group, IReadOnlyList<TrackModel> members)
    {
        var groupRow = GroupRow(group, members);
        var rows = new List<Control> { groupRow };
        if (!IsCollapsed(group))
            foreach (var track in members) { var row = TrackRow(track); _trackRows[track] = row; _trackGroup[track] = group; rows.Add(row); }
        var panel = Panel(rows.ToArray());
        panel.Name = $"Group: {group}";
        _groupPanels[group] = panel;
        _groupRows[group] = groupRow;
        AttachDrag(groupRow, null, group, panel);
        AttachGroupMenu(groupRow, group);
        return panel;
    }

    /// <summary>Builds the shell the window opens with: the head and the loading placeholder the group strips replace on the next UI pass.</summary>
    private void BuildShell()
    {
        _building = true;
        try
        {
            BuildHead();
            _groups.Children.Add(new TextBlock
            {
                Name = "MixerLoading",
                Text = "Loading the mixer strips…",
                Margin = new Thickness(8),
                [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted"),
            });
        }
        finally { _building = false; }
        FinishBuild();
    }

    /// <summary>Ends a build: the selection, the collapse-all button, the layout signature and the compact mode.</summary>
    private void FinishBuild()
    {
        Select(_selectedTrack, _selectedGroup);
        var all = _groupNames.Count > 0 && _groupNames.All(IsCollapsed);
        _collapseAll.Content = all ? "Expand all" : "Collapse all";
        _collapseAll.IsEnabled = _groupNames.Count > 0;
        ToolTip.SetTip(_collapseAll, all ? "Show the tracks of every group" : "Show only the group rows");
        _builtLayout = LayoutSignature();
        _builtValues = ValuesSignature();
        EvaluateCompact();
    }

    /// <summary>The group names and track order the strips were last built from; <see cref="SyncValues"/> compares against it.</summary>
    private List<object> _builtLayout = new();

    private List<object> LayoutSignature() =>
        TrackOrdering.Layout(_project).SelectMany(g => g.Tracks.Cast<object>().Prepend(g.Group)).ToList();

    private static List<object> LayoutSignature(IReadOnlyList<(string Group, List<TrackModel> Tracks)> layout) =>
        layout.SelectMany(g => g.Tracks.Cast<object>().Prepend(g.Group)).ToList();

    /// <summary>The values the strips show (names, colours, mute, solo, levels) as last built; <see cref="SyncValues"/> compares against it.</summary>
    private List<object> _builtValues = new();

    private List<object> ValuesSignature() =>
        ValuesSignature(TrackOrdering.Layout(_project));

    private List<object> ValuesSignature(IReadOnlyList<(string Group, List<TrackModel> Tracks)> layout) =>
        _project.Tracks.Select(t => (object)(t.Id, t.Name, t.ColorHex, t.Mute, t.Solo, t.Volume, t.Pan, t.Reverb, t.Chorus, t.Transpose, t.MidiSound, t.SoundSource))
            .Prepend(_project.Mixer.MasterPan)
            .Concat(GroupLevelsSignature(layout)).ToList();

    // The group rows' own values (volume, pan, pitch, mute, solo), after the tracks in the signature: an edit made to a group elsewhere shows on its row.
    private IEnumerable<object> GroupLevelsSignature() =>
        TrackOrdering.Layout(_project).Select(g => { var l = _project.Mixer.Levels(g.Group); return (object)(g.Group, l.Volume, l.Pan, l.Pitch, l.Mute, l.Solo); });

    private IEnumerable<object> GroupLevelsSignature(IReadOnlyList<(string Group, List<TrackModel> Tracks)> layout) =>
        layout.Select(g => { var l = _project.Mixer.Levels(g.Group); return (object)(g.Group, l.Volume, l.Pan, l.Pitch, l.Mute, l.Solo); });

    private bool _stripsBuilt;

    /// <summary>Test seam: true once the group strips exist. The window opens with the shell only; they build on the next UI pass.</summary>
    internal bool StripsBuilt => _stripsBuilt;

    /// <summary>Test seam: builds the group strips now when the deferred pass has not run yet; a second call is free and keeps the same strips.</summary>
    internal void EnsureStripsBuilt()
    {
        if (_stripsBuilt) return;
        _building = true;
        try
        {
            for (var i = _groups.Children.Count - 1; i >= 0; i--)
                if (_groups.Children[i] is TextBlock { Name: "MixerLoading" })
                    _groups.Children.RemoveAt(i);
            BuildGroupPanels();
        }
        finally { _building = false; }
        _stripsBuilt = true;
        FinishBuild();
    }

    /// <summary>Rebuilds the strips when the song's groups, track order or shown values (names, colours, mute, solo, volume, pan) changed since the last build; otherwise keeps them.</summary>
    internal void SyncValues()
    {
        if (!_stripsBuilt) { EnsureStripsBuilt(); return; }
        var layout = TrackOrdering.Layout(_project);
        if (!LayoutSignature(layout).SequenceEqual(_builtLayout)) { Rebuild(); return; }
        var now = ValuesSignature(layout);
        if (now.SequenceEqual(_builtValues)) return;
        if (!RebuildChangedGroups(now, layout)) Rebuild();
    }

    /// <summary>Rebuilds only the group panels holding a track whose shown values changed (the layout is the same); false when something else changed (the master pan), so the caller rebuilds everything.</summary>
    private bool RebuildChangedGroups(List<object> now) => RebuildChangedGroups(now, TrackOrdering.Layout(_project));

    private bool RebuildChangedGroups(List<object> now, IReadOnlyList<(string Group, List<TrackModel> Tracks)> layout)
    {
        if (now.Count != _builtValues.Count || !Equals(now[0], _builtValues[0])) return false;
        var changed = new HashSet<TrackModel>(ReferenceEqualityComparer.Instance);
        var changedGroups = new HashSet<string>();
        var trackEnd = _project.Tracks.Count + 1;
        for (var i = 1; i < now.Count; i++)
        {
            if (Equals(now[i], _builtValues[i])) continue;
            if (i < trackEnd) changed.Add(_project.Tracks[i - 1]);
            else changedGroups.Add(((ValueTuple<string, int, int, int, bool, bool>)now[i]).Item1);
        }
        _building = true;
        try
        {
            foreach (var (group, members) in layout)
            {
                if (!(members.Any(changed.Contains) || changedGroups.Contains(group)) || !_groupPanels.TryGetValue(group, out var old)) continue;
                var index = _groups.Children.IndexOf(old);
                if (index < 0) return false;
                foreach (var track in members) { _trackRows.Remove(track); _trackGroup.Remove(track); }
                _groups.Children[index] = BuildGroupPanel(group, members);
            }
        }
        finally { _building = false; }
        _builtValues = now;
        Select(_selectedTrack, _selectedGroup);
        return true;
    }

}
