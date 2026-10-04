using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge.Views;

/// <summary>What the tool palette needs from its window.</summary>
internal interface IToolPaletteHost : IPaneHost
{
    StackPanel ToolsPaletteHost { get; }
    Panel ToolsTabStrip { get; }
    Panel PinnedToolStrip { get; }
    MeasureModel? CurrentBar { get; }
    int MaxMeasures { get; }
    /// <summary>Runs the tool named by the clicked button's Tag.</summary>
    void RunPaletteTool(object sender, RoutedEventArgs e);
}

// Owns: the tool definitions of the Tools, Structure, Rhythm and Layout palettes, their buttons (state, enabled, tooltip,
//   icon or text look), the pin menu and the pinned-tool strip, and the Tool.* hotkey registration.
// Does not own: what a tool does (the window's tool commands), the dock panels that host the palettes.
// Tests: TestRuntimeIconAndResourceKeys, TestMenuGestureTextFollowsBindings.
internal sealed partial class ToolPaletteController
{
    private readonly IToolPaletteHost _host;

    public ToolPaletteController(IToolPaletteHost host) => _host = host;

    private readonly Dictionary<string, (Button Button, SvgIconView Icon)> _buttons = new(StringComparer.Ordinal);
    private readonly List<PaletteTool> _visiblePaletteTools = new();
    internal readonly Dictionary<string, FrameworkElement> PanelContents = new(StringComparer.Ordinal);
    public void BuildToolsPalette()
    {
        var tools = PaletteTools.Where(tool => tool.Group != "Composition" &&
            !tool.Id.StartsWith("edit:voice_", StringComparison.Ordinal) &&
            !tool.Id.StartsWith("edit:rhythm_beam_", StringComparison.Ordinal)).ToArray();
        var all = tools.Concat(StructurePaletteTools).Concat(RhythmPaletteTools).Concat(LayoutPaletteTools).ToArray();
        _visiblePaletteTools.Clear();
        _visiblePaletteTools.AddRange(all);
        _host.ToolsPaletteHost.Children.Clear();
        _buttons.Clear();
        PanelContents.Clear();

        if (_host.ToolsTabStrip.Parent is Panel tabParent) tabParent.Children.Remove(_host.ToolsTabStrip);
        var toolTabs = new (string Id, PaletteTool[] Tools)[]
        {
            ("tools", tools),
            ("structure", StructurePaletteTools),
            ("rhythm", RhythmPaletteTools),
            ("layout", LayoutPaletteTools)
        };
        foreach (var (id, paletteTools) in toolTabs)
        {
            StackPanel host;
            if (id == "tools") host = _host.ToolsPaletteHost;
            else
            {
                host = new StackPanel();
                var scroll = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = host
                };
                PanelContents[id] = new Border
                {
                    Background = (Brush)_host.Window.FindResource("Panel2Brush"),
                    BorderBrush = (Brush)_host.Window.FindResource("BorderSoftBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(7),
                    Margin = new Thickness(8),
                    Child = scroll
                };
            }

            host.Children.Clear();
            foreach (var group in paletteTools.GroupBy(tool => tool.Group))
            {
                var section = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
                section.Children.Add(new TextBlock
                {
                    Text = group.Key,
                    FontSize = TabForge.Services.ThemeService.MinFontSize,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)_host.Window.FindResource("SecondaryTextBrush"),
                    Margin = new Thickness(2, 2, 2, 3)
                });
                var buttons = new WrapPanel();
                // One Tab stop per group; the arrow keys move between its buttons (across wrapped rows too).
                KeyboardNavigation.SetTabNavigation(buttons, KeyboardNavigationMode.Once);
                KeyboardNavigation.SetDirectionalNavigation(buttons, KeyboardNavigationMode.Contained);
                System.Windows.Automation.AutomationProperties.SetName(buttons, group.Key + " tools");
                foreach (var tool in group)
                {
                    var icon = new SvgIconView
                    {
                        Icon = "tool:" + tool.Icon,
                        ShowFrame = false,
                        Width = 27,
                        Height = 27,
                        IconColor = PaletteBrushColor("TextBrush", Color.FromRgb(0xC7, 0xCF, 0xDA)),
                        IsHitTestVisible = false
                    };
                    var button = new Button
                    {
                        Style = (Style)_host.Window.FindResource("ToolButton"),
                        Content = icon,
                        Tag = tool.Id,
                        Width = 36,
                        Height = 36,
                        MinWidth = 36,
                        Padding = new Thickness(0),
                        Margin = new Thickness(1),
                        IsEnabled = tool.Supported,
                        ToolTip = PaletteToolTip(tool)
                    };
                    System.Windows.Automation.AutomationProperties.SetName(button, tool.Label);
                    WirePaletteButtonFocus(button);
                    button.Click += _host.RunPaletteTool;
                    var pinTool = tool;
                    button.ContextMenuOpening += (_, _) => button.ContextMenu = PinMenu(pinTool);
                    button.ContextMenu = PinMenu(tool);
                    buttons.Children.Add(button);
                    _buttons[tool.Id] = (button, icon);
                }
                section.Children.Add(buttons);
                host.Children.Add(section);
            }
        }
        RefreshToolsPalette();
    }

    public string PaletteToolTip(PaletteTool tool, string? extraLine = null)
    {
        var description = tool.Id switch
        {
            "gp:key_signature" => "Edit the effective key signature and major/minor mode for this measure.",
            "gp:triplet_feel" => "Cycle bar-level eighth-note swing, sixteenth-note swing, and straight feel.",
            "gp:free_time" => "Mark this measure as a free-time/cadenza bar.",
            "gp:double_barline" => "Toggle the structural double barline at this measure boundary.",
            "gp:repeat_one_bar" => "Repeat the preceding measure semantically during playback.",
            "gp:repeat_two_bars" => "Repeat the preceding two measures semantically during playback.",
            "gp:directions" => "Edit Segno, Coda, Double Coda, Fine, Da Capo, Dal Segno, and To Coda directions.",
            "gp:force_line_break" => "Start a new displayed score system at this measure.",
            "gp:prevent_line_break" => "Keep this measure with the preceding measure when the system wraps.",
            "gp:add_marker" => "Insert a named, colour-coded marker at the current measure.",
            "gp:marker_list" => "Focus the marker list to navigate and manage score markers.",
            "gp:previous_marker" => "Move to the previous marker using normal score navigation.",
            "gp:next_marker" => "Move to the next marker using normal score navigation.",
            "gp:custom_ntuplet" => "Apply a validated n:m rhythmic tuplet to the current beat or selection.",
            "gp:tie_note" => "Tie this note to the immediately preceding compatible note on the same string and voice.",
            "gp:tie_beat" => "Tie every compatible note in this chord to the preceding beat as one edit.",
            "gp:sound_duration" => "Set sounding gate length independently of the written note value.",
            "gp:voice_1" => "Select the independent lead voice for editing.",
            "gp:voice_2" => "Select the independent second voice for editing.",
            "gp:inactive_voice_gray" => "Display the inactive voice in gray without changing its notes.",
            "gp:beam_auto" => "Restore the normal automatic beaming for the selection.",
            "gp:beam_force" => "Force the selected compatible notes into a beam group.",
            "gp:beam_break" => "Break the primary beam at the selected beat boundary.",
            "gp:beam_break_secondary" => "Break secondary beams only; keep the primary beam intact.",
            "gp:stem_auto" => "Restore automatic stem direction for the selection.",
            "gp:stem_invert" => "Invert the selected stem direction from its automatic rule.",
            _ when tool.Id.StartsWith("gp:octave_", StringComparison.Ordinal) => "Apply an octave-transposition marking to the selected beat(s).",
            _ => tool.Label
        };
        // "(key)" last, from the live key map, so a rebind shows immediately.
        var text = description == tool.Label ? tool.Label : $"{tool.Label}: {description}";
        if (!string.IsNullOrEmpty(extraLine)) text += "\n" + extraLine;
        return Views.TooltipShortcuts.Append(text, PaletteHotkeyId(tool.Id));
    }


    private Color PaletteBrushColor(string key, Color fallback)
        => (_host.Window.FindResource(key) as SolidColorBrush)?.Color ?? fallback;

    public bool? PaletteToolState(string id)
    {
        if (!id.StartsWith("gp:", StringComparison.Ordinal)) return _host.Editor.GetToolState(id);
        var bar = _host.CurrentBar;
        var cell = _host.Editor.CurrentCell();
        var selectedNote = cell?.Notes.FirstOrDefault(note => note.StringIndex == _host.Editor.SelectedString);
        return id switch
        {
            "gp:key_signature" => bar is not null && ((bar.KeySignature ?? _host.Project.KeySignature) != 0 || (bar.KeySignatureMinor ?? _host.Project.KeySignatureMinor)),
            "gp:triplet_feel" => bar is not null && (bar.TripletFeel || bar.TripletFeelKind != "None"),
            "gp:free_time" => bar?.FreeTime,
            "gp:double_barline" => bar?.IsDoubleBar,
            "gp:repeat_one_bar" => bar?.SimileOneBar,
            "gp:repeat_two_bars" => bar?.SimileTwoBar,
            "gp:directions" => bar is not null && (!string.IsNullOrWhiteSpace(bar.Directions) || bar.AlternateEnding > 0),
            "gp:force_line_break" => bar?.ForceLineBreak,
            "gp:prevent_line_break" => bar?.PreventLineBreak,
            "gp:add_marker" or "gp:marker_list" or "gp:previous_marker" or "gp:next_marker" => false,
            "gp:custom_ntuplet" => _host.Editor.GetToolState("duration:tuplet"),
            "gp:tie_note" => selectedNote is not null && (selectedNote.Tied || cell!.IsTied),
            "gp:tie_beat" => cell is { Notes.Count: > 0 } && cell.Notes.All(note => note.Tied || cell.IsTied),
            "gp:sound_duration" => _host.Editor.GetNoteCellToolState(noteCell => noteCell.SoundDurationPercent != 100),
            "gp:octave_8va" => _host.Editor.GetNoteCellToolState(noteCell => noteCell.OctaveShiftSemitones == 12),
            "gp:octave_8vb" => _host.Editor.GetNoteCellToolState(noteCell => noteCell.OctaveShiftSemitones == -12),
            "gp:octave_15ma" => _host.Editor.GetNoteCellToolState(noteCell => noteCell.OctaveShiftSemitones == 24),
            "gp:octave_15mb" => _host.Editor.GetNoteCellToolState(noteCell => noteCell.OctaveShiftSemitones == -24),
            "gp:voice_1" => _host.Editor.ActiveVoiceIndex == 0,
            "gp:voice_2" => _host.Editor.ActiveVoiceIndex == 1,
            "gp:inactive_voice_gray" => _host.Project.GrayInactiveVoice,
            "gp:beam_auto" => _host.Editor.GetNoteCellToolState(noteCell => noteCell.BeamMode == BeamMode.Auto && !noteCell.BreakSecondaryBeamBefore),
            "gp:beam_force" => _host.Editor.GetNoteCellToolState(noteCell => noteCell.BeamMode == BeamMode.Force),
            "gp:beam_break" => _host.Editor.GetNoteCellToolState(noteCell => noteCell.BeamMode == BeamMode.Break),
            "gp:beam_break_secondary" => _host.Editor.GetNoteCellToolState(noteCell => noteCell.BreakSecondaryBeamBefore),
            "gp:stem_auto" => _host.Editor.GetNoteCellToolState(noteCell => noteCell.StemDirection == StemDirection.Auto),
            "gp:stem_invert" => _host.Editor.GetNoteCellToolState(noteCell => noteCell.StemDirection == StemDirection.Invert),
            _ => null
        };
    }

    public bool PaletteToolEnabled(string id)
    {
        var bar = _host.CurrentBar;
        return id switch
        {
            "gp:key_signature" or "gp:triplet_feel" or "gp:free_time" or "gp:double_barline" or
            "gp:directions" or "gp:force_line_break" or "gp:prevent_line_break" => bar is not null,
            "gp:repeat_one_bar" => bar is not null && _host.Editor.SelectedMeasure > 0,
            "gp:repeat_two_bars" => bar is not null && _host.Editor.SelectedMeasure > 1,
            "gp:add_marker" => _host.Editor.SelectedMeasure >= 0 && _host.Editor.SelectedMeasure < _host.MaxMeasures,
            "gp:marker_list" => true,
            "gp:previous_marker" => _host.Project.Markers.Any(marker => marker.MeasureIndex < _host.Editor.SelectedMeasure),
            "gp:next_marker" => _host.Project.Markers.Any(marker => marker.MeasureIndex > _host.Editor.SelectedMeasure),
            "gp:custom_ntuplet" => _host.Editor.CanSetTuplet((3, 2)),
            "gp:tie_note" => _host.Editor.CanTieSelectedNote(),
            "gp:tie_beat" => _host.Editor.CanTieSelectedBeat(),
            "gp:sound_duration" or "gp:octave_8va" or "gp:octave_8vb" or "gp:octave_15ma" or "gp:octave_15mb" => _host.Editor.HasEditableNotes,
            "gp:voice_1" or "gp:voice_2" => _host.SelectedTrack is not null,
            "gp:inactive_voice_gray" => _host.SelectedTrack is not null,
            "gp:beam_auto" or "gp:beam_force" or "gp:beam_break" or "gp:stem_auto" or "gp:stem_invert" => _host.Editor.HasEditableNotes,
            "gp:beam_break_secondary" => _host.Editor.HasSecondaryBeamEligibleNotes,
            _ => true
        };
    }

    public void RefreshToolsPalette()
    {
        if (_buttons.Count == 0) return;
        var accent = PaletteBrushColor("AccentBrush", Color.FromRgb(0x4C, 0x9A, 0xFF));
        var accentSoft = _host.Window.FindResource("AccentSoftBrush") as Brush ?? Brushes.Transparent;
        var idle = PaletteBrushColor("TextBrush", Color.FromRgb(0xC7, 0xCF, 0xDA));
        var muted = PaletteBrushColor("MutedBrush", Color.FromRgb(0x98, 0xA1, 0xAE));
        foreach (var tool in _visiblePaletteTools)
        {
            if (!_buttons.TryGetValue(tool.Id, out var controls)) continue;
            var active = PaletteToolState(tool.Id) == true;
            var durationKey = tool.Id.StartsWith("duration:", StringComparison.Ordinal)
                ? tool.Id[9..] : null;
            controls.Button.IsEnabled = tool.Supported &&
                (durationKey is null || _host.Editor.CanSetDurationForTool(durationKey)) && PaletteToolEnabled(tool.Id);
            controls.Button.Background = active ? accentSoft : Brushes.Transparent;
            controls.Button.BorderBrush = active ? new SolidColorBrush(accent) : Brushes.Transparent;
            // The active tool is marked by shape as well as colour: a thick underline, and "active" in its automation name.
            controls.Button.BorderThickness = active ? new Thickness(1, 1, 1, 3) : new Thickness(1);
            System.Windows.Automation.AutomationProperties.SetName(controls.Button, active ? tool.Label + ", active" : tool.Label);
            controls.Button.Foreground = new SolidColorBrush(active ? accent : tool.Supported ? idle : muted);
            controls.Icon.IconColor = active ? accent : tool.Supported ? idle : muted;
            var showIcons = _host.Settings.Appearance.ShowToolbarIcons;
            controls.Icon.Visibility = showIcons ? Visibility.Visible : Visibility.Collapsed;
            controls.Button.Content = showIcons ? controls.Icon : tool.Label;
            controls.Button.Width = showIcons ? 36 : double.NaN;
            controls.Button.Height = showIcons ? 36 : 28;
            controls.Button.MinWidth = showIcons ? 36 : 48;
            controls.Button.Padding = showIcons ? new Thickness(0) : new Thickness(5, 1, 5, 1);
        }
    }


    public static void RegisterHotkeys()
    {
        // Every palette icon becomes a bindable hotkey command (no default key), so tools added to a
        // palette later are mappable - and follow the presets - automatically.
        HotkeyCatalog.Register(PaletteTools.Concat(StructurePaletteTools).Concat(RhythmPaletteTools).Concat(LayoutPaletteTools)
            .Where(t => t.Supported && !PaletteToolsCoveredByCommands.Contains(t.Id))
            .GroupBy(t => t.Id).Select(g => g.First())
            .Select(t => new HotkeyAction("Tool." + t.Id, "Tool palette: " + t.Group, t.Label, "", $"Palette tool: {t.Label}.")));
    }

    /// <summary>Palette tooltips carry their key too; rebuilt so a rebind shows at once.</summary>
    public void RefreshTooltips()
    {
        foreach (var tool in AllPaletteTools())
            if (_buttons.TryGetValue(tool.Id, out var palette)) palette.Button.ToolTip = PaletteToolTip(tool);
    }

    private static IEnumerable<PaletteTool> AllPaletteTools() =>
        PaletteTools.Concat(StructurePaletteTools).Concat(RhythmPaletteTools).Concat(LayoutPaletteTools)
            .GroupBy(t => t.Id).Select(g => g.First());

    private ContextMenu PinMenu(PaletteTool tool)
    {
        var pins = _host.Settings.Appearance.PinnedTools;
        var pinned = pins.Contains(tool.Id);
        var menu = new ContextMenu { Style = (Style)_host.Window.FindResource(typeof(ContextMenu)) };
        var item = new MenuItem
        {
            Header = pinned ? "Unpin from toolbar" : "Pin to toolbar",
            Style = (Style)_host.Window.FindResource(typeof(MenuItem))
        };
        item.Click += (_, _) => SetToolPinned(tool.Id, !pinned);
        menu.Items.Add(item);
        return menu;
    }

    private void SetToolPinned(string id, bool pinned)
    {
        var pins = _host.Settings.Appearance.PinnedTools;
        pins.Remove(id);
        if (pinned) pins.Add(id);
        BuildPinnedToolStrip();
        _host.SaveSettings();
        _host.SetStatus(pinned ? "Pinned to the toolbar" : "Removed from the toolbar");
    }

    /// <summary>The menu-row strip of tools the user pinned from any palette (same actions, no duplicates).</summary>
    public void BuildPinnedToolStrip()
    {
        _host.PinnedToolStrip.Children.Clear();
        KeyboardNavigation.SetTabNavigation(_host.PinnedToolStrip, KeyboardNavigationMode.Once);
        KeyboardNavigation.SetDirectionalNavigation(_host.PinnedToolStrip, KeyboardNavigationMode.Contained);
        var tools = AllPaletteTools().ToDictionary(t => t.Id);
        foreach (var id in _host.Settings.Appearance.PinnedTools.Distinct().ToList())
        {
            if (!tools.TryGetValue(id, out var tool)) continue;
            var button = new Button
            {
                Style = (Style)_host.Window.FindResource("ToolButton"),
                Content = new SvgIconView
                {
                    Icon = "tool:" + tool.Icon, ShowFrame = false, Width = 20, Height = 20, IsHitTestVisible = false,
                    IconColor = PaletteBrushColor("TextBrush", Color.FromRgb(0xC7, 0xCF, 0xDA))
                },
                Tag = tool.Id, Width = 26, Height = 24, MinWidth = 26, Padding = new Thickness(0), Margin = new Thickness(1, 0, 1, 0),
                ToolTip = PaletteToolTip(tool, "Right-click to unpin.")
            };
            System.Windows.Automation.AutomationProperties.SetName(button, tool.Label);
            WirePaletteButtonFocus(button);
            button.Click += _host.RunPaletteTool;
            button.ContextMenu = PinMenu(tool);
            button.ContextMenuOpening += (_, _) => button.ContextMenu = PinMenu(tool);
            _host.PinnedToolStrip.Children.Add(button);
        }
    }

    /// <summary>
    /// Palette buttons are keyboard-focusable (arrow keys roam a group, Enter/Space run the tool), but a mouse click
    /// must not steal the editor's typing focus: after a mouse click the focus goes back to the score.
    /// </summary>
    private void WirePaletteButtonFocus(Button button)
    {
        button.Focusable = true;
        var byMouse = false;
        button.PreviewMouseLeftButtonDown += (_, _) => byMouse = true;
        button.PreviewKeyDown += (_, _) => byMouse = false;
        button.Click += (_, _) =>
        {
            if (!byMouse) return;
            byMouse = false;
            if (_host.Editor.IsVisible) _host.Editor.Focus();
        };
    }
}
