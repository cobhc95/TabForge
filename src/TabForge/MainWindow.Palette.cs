using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using TabForge.Controllers;
using TabForge.Docking;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Shell;
using TabForge.Views;
using TabForge.Visualization;

namespace TabForge;

// MainWindow, tool palette: tool definitions, palette panels, pinned strip and tool actions.
public partial class MainWindow
{
    private sealed record PaletteTool(string Group, string Id, string Icon, string Label, bool Supported = true);
    private readonly Dictionary<string, (Button Button, SvgIconView Icon)> _paletteButtons = new(StringComparer.Ordinal);
    private readonly List<PaletteTool> _visiblePaletteTools = new();
    private readonly Dictionary<string, FrameworkElement> _palettePanelContents = new(StringComparer.Ordinal);
    private DockWorkspace? _dockWorkspace;
    private static readonly PaletteTool[] PaletteTools =
    {
        new("Edit", "edit:pointer", "Edit/select_pointer", "Selection cursor"),
        new("Edit", "edit:erase_note", "Edit/erase_note", "Erase note"),
        new("Edit", "edit:change_accidental", "Edit/change_accidental", "Change accidental", false),
        new("Composition", "composition:time_signature", "Composition/time_signature", "Time signature"),
        new("Composition", "composition:tempo", "Composition/tempo", "Tempo change"),
        new("Composition", "composition:repeat_open", "Composition/repeat_open", "Repeat start"),
        new("Composition", "composition:repeat_close", "Composition/repeat_close", "Repeat end / count"),
        new("Composition", "composition:alternate_ending", "Composition/alternate_ending", "Alternate ending"),

        new("Duration", "duration:whole", "Duration/whole_note", "Whole note"),
        new("Duration", "duration:half", "Duration/half_note", "Half note"),
        new("Duration", "duration:quarter", "Duration/quarter_note", "Quarter note"),
        new("Duration", "duration:eighth", "Duration/eighth_note", "Eighth note"),
        new("Duration", "duration:sixteenth", "Duration/sixteenth_note", "16th note"),
        new("Duration", "duration:thirtysecond", "Duration/thirty_second_note", "32nd note"),
        new("Duration", "duration:sixtyfourth", "Duration/sixty_fourth_note", "64th note"),
        new("Duration", "duration:dotted", "Duration/dotted_note", "Dotted duration"),
        new("Duration", "duration:double-dotted", "Duration/double_dotted_note", "Double-dotted duration"),
        new("Duration", "duration:tie", "Duration/tied_note", "Tie"),
        new("Duration", "duration:tuplet", "Duration/tuplet", "Triplet"),
        new("Duration", "duration:tuplet-menu", "Duration/tuplet_menu", "Choose tuplet ratio"),

        new("Dynamic", "dynamic:ppp", "Dynamic/ppp", "ppp"),
        new("Dynamic", "dynamic:pp", "Dynamic/pp", "pp"),
        new("Dynamic", "dynamic:p", "Dynamic/p", "p"),
        new("Dynamic", "dynamic:mp", "Dynamic/mp", "mp"),
        new("Dynamic", "dynamic:mf", "Dynamic/mf", "mf"),
        new("Dynamic", "dynamic:f", "Dynamic/f", "f"),
        new("Dynamic", "dynamic:ff", "Dynamic/ff", "ff"),
        new("Dynamic", "dynamic:fff", "Dynamic/fff", "fff"),

        new("Effects", "effect:vibrato", "Effects/vibrato", "Vibrato"),
        new("Effects", "effect:bend", "Effects/bend", "Bend"),
        new("Effects", "effect:tremolo_bar", "Effects/tremolo_bar", "Tremolo bar"),
        new("Effects", "effect:slides", "Effects/slides", "Slide"),
        new("Effects", "effect:dead_note", "Effects/dead_note", "Dead note"),
        new("Effects", "effect:hammer_on_pull_off", "Effects/hammer_on_pull_off", "Hammer-on / pull-off"),
        new("Effects", "effect:ghost_note", "Effects/ghost_note", "Ghost note"),
        new("Effects", "effect:accent", "Effects/accent", "Accent"),
        new("Effects", "effect:heavy_accent", "Effects/heavy_accent", "Heavy accent"),
        new("Effects", "effect:let_ring", "Effects/let_ring", "Let ring"),
        new("Effects", "effect:natural_harmonic", "Effects/natural_harmonic", "Natural harmonic"),
        new("Effects", "effect:grace_note", "Effects/grace_note", "Grace note"),
        new("Effects", "effect:trill", "Effects/trill", "Trill"),
        new("Effects", "effect:tremolo_picking", "Effects/tremolo_picking", "Tremolo picking"),
        new("Effects", "effect:palm_mute", "Effects/palm_mute", "Palm mute"),
        new("Effects", "effect:staccato", "Effects/staccato", "Staccato"),
        new("Effects", "effect:tapping", "Effects/tapping", "Tapping"),
        new("Effects", "effect:slapping", "Effects/slapping", "Slapping"),
        new("Effects", "effect:popping", "Effects/popping", "Popping"),
        new("Effects", "effect:fade_in", "Effects/fade_in", "Fade in"),

        new("Beat", "effect:chord", "Beat/chord", "Chord"),
        new("Beat", "effect:chord_menu", "Beat/chord_menu", "Choose chord"),
        new("Beat", "effect:text", "Beat/text", "Text annotation"),
        new("Beat", "effect:stroke_down", "Beat/stroke_down", "Brush down"),
        new("Beat", "effect:stroke_up", "Beat/stroke_up", "Brush up"),
        new("Beat", "effect:pickstroke_down", "Beat/pickstroke_down", "Pickstroke down"),
        new("Beat", "effect:pickstroke_up", "Beat/pickstroke_up", "Pickstroke up")
    };

    private static readonly PaletteTool[] StructurePaletteTools =
    {
        new("Bar editing", "gp:insert_bar", "More/insert_bar", "Insert bar before the cursor"),
        new("Bar editing", "gp:append_bar", "More/append_bar", "Add bar at the end"),
        new("Bar editing", "gp:duplicate_bar", "More/duplicate_bar", "Duplicate bar"),
        new("Bar editing", "gp:delete_bar", "More/delete_bar", "Delete bar"),
        new("Bar editing", "gp:check_bars", "More/check_bar", "Check bar durations"),
        new("Step through", "gp:step_back", "More/step_back", "Step back one beat"),
        new("Step through", "gp:step_forward", "More/step_forward", "Step forward one beat"),
        new("Bars", "composition:time_signature", "Composition/time_signature", "Time signature"),
        new("Bars", "composition:tempo", "Composition/tempo", "Tempo change"),
        new("Bars", "composition:repeat_open", "Composition/repeat_open", "Repeat start"),
        new("Bars", "composition:repeat_close", "Composition/repeat_close", "Repeat end / count"),
        new("Key and bars", "gp:key_signature", "More/key_signature", "Key signature"),
        new("Key and bars", "gp:triplet_feel", "More/triplet_feel", "Triplet feel"),
        new("Key and bars", "gp:free_time", "More/free_time", "Free time"),
        new("Key and bars", "gp:double_barline", "More/double_barline", "Double barline"),
        new("Repeats and directions", "gp:repeat_one_bar", "More/repeat_one_bar", "One-bar repeat"),
        new("Repeats and directions", "gp:repeat_two_bars", "More/repeat_two_bars", "Two-bar repeat"),
        new("Repeats and directions", "gp:directions", "More/directions", "Score directions"),
        new("Markers", "gp:add_marker", "More/add_marker", "Add marker"),
        new("Markers", "gp:marker_list", "More/marker_list", "Marker list"),
        new("Markers", "gp:previous_marker", "More/previous_marker", "Previous marker"),
        new("Markers", "gp:next_marker", "More/next_marker", "Next marker")
    };

    private static readonly PaletteTool[] RhythmPaletteTools =
    {
        new("Tuplets and ties", "gp:custom_ntuplet", "More/custom_ntuplet", "N-tuplet"),
        new("Tuplets and ties", "gp:tie_note", "More/tie_note", "Tie note"),
        new("Tuplets and ties", "gp:tie_beat", "More/tie_beat", "Tie beat / chord"),
        new("Sounding pitch and duration", "gp:sound_duration", "More/sound_duration", "Sound duration"),
        new("Sounding pitch and duration", "gp:octave_8va", "More/octave_8va", "8va — octave above"),
        new("Sounding pitch and duration", "gp:octave_8vb", "More/octave_8vb", "8vb — octave below"),
        new("Sounding pitch and duration", "gp:octave_15ma", "More/octave_15ma", "15ma — two octaves above"),
        new("Sounding pitch and duration", "gp:octave_15mb", "More/octave_15mb", "15mb — two octaves below")
    };

    private static readonly PaletteTool[] LayoutPaletteTools =
    {
        new("Voices", "gp:voice_1", "More/voice_lead", "Voice 1"),
        new("Voices", "gp:voice_2", "More/voice_bass", "Voice 2"),
        new("Voices", "gp:inactive_voice_gray", "More/inactive_voice_gray", "Gray inactive voice"),
        new("System layout", "gp:force_line_break", "More/force_line_break", "Force line break"),
        new("System layout", "gp:prevent_line_break", "More/prevent_line_break", "Prevent line break"),
        new("Beaming", "gp:beam_auto", "More/beam_auto", "Automatic beaming"),
        new("Beaming", "gp:beam_force", "More/beam_force", "Force beam group"),
        new("Beaming", "gp:beam_break", "More/beam_break", "Break primary beam"),
        new("Beaming", "gp:beam_break_secondary", "More/beam_break_secondary", "Break secondary beam"),
        new("Stems", "gp:stem_auto", "More/stem_auto", "Automatic stem direction"),
        new("Stems", "gp:stem_invert", "More/stem_invert", "Invert stem direction")
    };


    private void BuildToolsPalette()
    {
        var tools = PaletteTools.Where(tool => tool.Group != "Composition" &&
            !tool.Id.StartsWith("edit:voice_", StringComparison.Ordinal) &&
            !tool.Id.StartsWith("edit:rhythm_beam_", StringComparison.Ordinal)).ToArray();
        var all = tools.Concat(StructurePaletteTools).Concat(RhythmPaletteTools).Concat(LayoutPaletteTools).ToArray();
        _visiblePaletteTools.Clear();
        _visiblePaletteTools.AddRange(all);
        ToolsPaletteHost.Children.Clear();
        _paletteButtons.Clear();
        _palettePanelContents.Clear();

        if (ToolsTabStrip.Parent is Panel tabParent) tabParent.Children.Remove(ToolsTabStrip);
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
            if (id == "tools") host = ToolsPaletteHost;
            else
            {
                host = new StackPanel();
                var scroll = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = host
                };
                _palettePanelContents[id] = new Border
                {
                    Background = (Brush)FindResource("Panel2Brush"),
                    BorderBrush = (Brush)FindResource("BorderSoftBrush"),
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
                    Foreground = (Brush)FindResource("SecondaryTextBrush"),
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
                        Style = (Style)FindResource("ToolButton"),
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
                    button.Click += ToolsPaletteButton_Click;
                    var pinTool = tool;
                    button.ContextMenuOpening += (_, _) => button.ContextMenu = PinMenu(pinTool);
                    button.ContextMenu = PinMenu(tool);
                    buttons.Children.Add(button);
                    _paletteButtons[tool.Id] = (button, icon);
                }
                section.Children.Add(buttons);
                host.Children.Add(section);
            }
        }
        RefreshToolsPalette();
    }

    private string PaletteToolTip(PaletteTool tool)
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
        // "(key)" after the text, from the live key map, so a rebind shows immediately.
        var suffix = HotkeyCatalog.TooltipSuffix(_settings.Hotkeys, PaletteHotkeyId(tool.Id));
        return $"{tool.Label}{suffix}: {description}";
    }

    /// <summary>The command a palette tool runs: a shared command where one exists, else its own Tool.* id.</summary>
    private static string PaletteHotkeyId(string toolId) => toolId switch
    {
        "gp:key_signature" => "Bar.KeySignature",
        "gp:directions" => "Bar.Directions",
        "gp:add_marker" => "Section.Add",
        "gp:previous_marker" => "Section.Previous",
        "gp:next_marker" => "Section.Next",
        "gp:insert_bar" => "Bar.Insert",
        "gp:delete_bar" => "Bar.Delete",
        "gp:check_bars" => "Bar.Check",
        "composition:time_signature" => "Bar.TimeSignature",
        "composition:repeat_open" => "Bar.RepeatOpen",
        "composition:repeat_close" => "Bar.RepeatClose",
        "effect:vibrato" => "Note.Vibrato",
        "effect:bend" => "Note.Bend",
        "effect:slides" => "Note.Slide",
        "effect:dead_note" => "Note.Dead",
        "effect:hammer_on_pull_off" => "Note.HammerPull",
        "effect:ghost_note" => "Note.Ghost",
        "effect:let_ring" => "Note.LetRing",
        "effect:natural_harmonic" => "Note.Harmonic",
        "effect:grace_note" => "Note.Grace",
        "effect:trill" => "Note.Trill",
        "effect:palm_mute" => "Note.PalmMute",
        "effect:staccato" => "Note.Staccato",
        "effect:tremolo_bar" => "Note.TremoloBar",
        "effect:fade_in" => "Note.FadeIn",
        "effect:chord" => "Note.Chord",
        "effect:text" => "Note.Text",
        "duration:dotted" => "Note.Dot",
        "duration:double-dotted" => "Note.DoubleDot",
        "duration:tie" => "Note.Tie",
        "duration:tuplet" => "Note.Triplet",
        _ => "Tool." + toolId,
    };

    private Color PaletteBrushColor(string key, Color fallback)
        => (FindResource(key) as SolidColorBrush)?.Color ?? fallback;

    private bool? PaletteToolState(string id)
    {
        if (!id.StartsWith("gp:", StringComparison.Ordinal)) return Editor.GetToolState(id);
        var bar = CurBar();
        var cell = Editor.CurrentCell();
        var selectedNote = cell?.Notes.FirstOrDefault(note => note.StringIndex == Editor.SelectedString);
        return id switch
        {
            "gp:key_signature" => bar is not null && ((bar.KeySignature ?? _project.KeySignature) != 0 || (bar.KeySignatureMinor ?? _project.KeySignatureMinor)),
            "gp:triplet_feel" => bar is not null && (bar.TripletFeel || bar.TripletFeelKind != "None"),
            "gp:free_time" => bar?.FreeTime,
            "gp:double_barline" => bar?.IsDoubleBar,
            "gp:repeat_one_bar" => bar?.SimileOneBar,
            "gp:repeat_two_bars" => bar?.SimileTwoBar,
            "gp:directions" => bar is not null && (!string.IsNullOrWhiteSpace(bar.Directions) || bar.AlternateEnding > 0),
            "gp:force_line_break" => bar?.ForceLineBreak,
            "gp:prevent_line_break" => bar?.PreventLineBreak,
            "gp:add_marker" or "gp:marker_list" or "gp:previous_marker" or "gp:next_marker" => false,
            "gp:custom_ntuplet" => Editor.GetToolState("duration:tuplet"),
            "gp:tie_note" => selectedNote is not null && (selectedNote.Tied || cell!.IsTied),
            "gp:tie_beat" => cell is { Notes.Count: > 0 } && cell.Notes.All(note => note.Tied || cell.IsTied),
            "gp:sound_duration" => Editor.GetNoteCellToolState(noteCell => noteCell.SoundDurationPercent != 100),
            "gp:octave_8va" => Editor.GetNoteCellToolState(noteCell => noteCell.OctaveShiftSemitones == 12),
            "gp:octave_8vb" => Editor.GetNoteCellToolState(noteCell => noteCell.OctaveShiftSemitones == -12),
            "gp:octave_15ma" => Editor.GetNoteCellToolState(noteCell => noteCell.OctaveShiftSemitones == 24),
            "gp:octave_15mb" => Editor.GetNoteCellToolState(noteCell => noteCell.OctaveShiftSemitones == -24),
            "gp:voice_1" => Editor.ActiveVoiceIndex == 0,
            "gp:voice_2" => Editor.ActiveVoiceIndex == 1,
            "gp:inactive_voice_gray" => _project.GrayInactiveVoice,
            "gp:beam_auto" => Editor.GetNoteCellToolState(noteCell => noteCell.BeamMode == BeamMode.Auto && !noteCell.BreakSecondaryBeamBefore),
            "gp:beam_force" => Editor.GetNoteCellToolState(noteCell => noteCell.BeamMode == BeamMode.Force),
            "gp:beam_break" => Editor.GetNoteCellToolState(noteCell => noteCell.BeamMode == BeamMode.Break),
            "gp:beam_break_secondary" => Editor.GetNoteCellToolState(noteCell => noteCell.BreakSecondaryBeamBefore),
            "gp:stem_auto" => Editor.GetNoteCellToolState(noteCell => noteCell.StemDirection == StemDirection.Auto),
            "gp:stem_invert" => Editor.GetNoteCellToolState(noteCell => noteCell.StemDirection == StemDirection.Invert),
            _ => null
        };
    }

    private bool PaletteToolEnabled(string id)
    {
        var bar = CurBar();
        return id switch
        {
            "gp:key_signature" or "gp:triplet_feel" or "gp:free_time" or "gp:double_barline" or
            "gp:directions" or "gp:force_line_break" or "gp:prevent_line_break" => bar is not null,
            "gp:repeat_one_bar" => bar is not null && Editor.SelectedMeasure > 0,
            "gp:repeat_two_bars" => bar is not null && Editor.SelectedMeasure > 1,
            "gp:add_marker" => Editor.SelectedMeasure >= 0 && Editor.SelectedMeasure < MaxMeasures(),
            "gp:marker_list" => true,
            "gp:previous_marker" => _project.Markers.Any(marker => marker.MeasureIndex < Editor.SelectedMeasure),
            "gp:next_marker" => _project.Markers.Any(marker => marker.MeasureIndex > Editor.SelectedMeasure),
            "gp:custom_ntuplet" => Editor.CanSetTuplet((3, 2)),
            "gp:tie_note" => Editor.CanTieSelectedNote(),
            "gp:tie_beat" => Editor.CanTieSelectedBeat(),
            "gp:sound_duration" or "gp:octave_8va" or "gp:octave_8vb" or "gp:octave_15ma" or "gp:octave_15mb" => Editor.HasEditableNotes,
            "gp:voice_1" or "gp:voice_2" => SelectedTrack is not null,
            "gp:inactive_voice_gray" => SelectedTrack is not null,
            "gp:beam_auto" or "gp:beam_force" or "gp:beam_break" or "gp:stem_auto" or "gp:stem_invert" => Editor.HasEditableNotes,
            "gp:beam_break_secondary" => Editor.HasSecondaryBeamEligibleNotes,
            _ => true
        };
    }

    private void RefreshToolsPalette()
    {
        if (_paletteButtons.Count == 0) return;
        var accent = PaletteBrushColor("AccentBrush", Color.FromRgb(0x4C, 0x9A, 0xFF));
        var accentSoft = FindResource("AccentSoftBrush") as Brush ?? Brushes.Transparent;
        var idle = PaletteBrushColor("TextBrush", Color.FromRgb(0xC7, 0xCF, 0xDA));
        var muted = PaletteBrushColor("MutedBrush", Color.FromRgb(0x98, 0xA1, 0xAE));
        foreach (var tool in _visiblePaletteTools)
        {
            if (!_paletteButtons.TryGetValue(tool.Id, out var controls)) continue;
            var active = PaletteToolState(tool.Id) == true;
            var durationKey = tool.Id.StartsWith("duration:", StringComparison.Ordinal)
                ? tool.Id[9..] : null;
            controls.Button.IsEnabled = tool.Supported &&
                (durationKey is null || Editor.CanSetDurationForTool(durationKey)) && PaletteToolEnabled(tool.Id);
            controls.Button.Background = active ? accentSoft : Brushes.Transparent;
            controls.Button.BorderBrush = active ? new SolidColorBrush(accent) : Brushes.Transparent;
            // The active tool is marked by shape as well as colour: a thick underline, and "active" in its automation name.
            controls.Button.BorderThickness = active ? new Thickness(1, 1, 1, 3) : new Thickness(1);
            System.Windows.Automation.AutomationProperties.SetName(controls.Button, active ? tool.Label + ", active" : tool.Label);
            controls.Button.Foreground = new SolidColorBrush(active ? accent : tool.Supported ? idle : muted);
            controls.Icon.IconColor = active ? accent : tool.Supported ? idle : muted;
            var showIcons = _settings.Appearance.ShowToolbarIcons;
            controls.Icon.Visibility = showIcons ? Visibility.Visible : Visibility.Collapsed;
            controls.Button.Content = showIcons ? controls.Icon : tool.Label;
            controls.Button.Width = showIcons ? 36 : double.NaN;
            controls.Button.Height = showIcons ? 36 : 28;
            controls.Button.MinWidth = showIcons ? 36 : 48;
            controls.Button.Padding = showIcons ? new Thickness(0) : new Thickness(5, 1, 5, 1);
        }
    }

    // Palette tools that are the same command as an existing hotkey action are not registered twice.
    private static readonly HashSet<string> PaletteToolsCoveredByCommands = new(StringComparer.Ordinal)
    {
        "effect:vibrato", "effect:bend", "effect:slides", "effect:dead_note", "effect:hammer_on_pull_off",
        "effect:ghost_note", "effect:let_ring", "effect:natural_harmonic", "effect:grace_note", "effect:trill",
        "effect:palm_mute", "effect:staccato", "effect:tremolo_bar", "effect:fade_in", "effect:chord", "effect:text",
        "duration:dotted", "duration:double-dotted", "duration:tie", "duration:tuplet",
        "composition:time_signature", "composition:repeat_open", "composition:repeat_close",
        "gp:key_signature", "gp:directions", "gp:insert_bar", "gp:delete_bar", "gp:check_bars",
        "gp:previous_marker", "gp:next_marker"
    };

    static MainWindow()
    {
        // Every palette icon becomes a bindable hotkey command (no default key), so tools added to a
        // palette later are mappable - and follow the presets - automatically.
        HotkeyCatalog.Register(PaletteTools.Concat(StructurePaletteTools).Concat(RhythmPaletteTools).Concat(LayoutPaletteTools)
            .Where(t => t.Supported && !PaletteToolsCoveredByCommands.Contains(t.Id))
            .GroupBy(t => t.Id).Select(g => g.First())
            .Select(t => new HotkeyAction("Tool." + t.Id, "Tool palette: " + t.Group, t.Label, "", $"Palette tool: {t.Label}.")));
    }

    private IEnumerable<PaletteTool> AllPaletteTools() =>
        PaletteTools.Concat(StructurePaletteTools).Concat(RhythmPaletteTools).Concat(LayoutPaletteTools)
            .GroupBy(t => t.Id).Select(g => g.First());

    private ContextMenu PinMenu(PaletteTool tool)
    {
        var pins = _settings.Appearance.PinnedTools;
        var pinned = pins.Contains(tool.Id);
        var menu = new ContextMenu { Style = (Style)FindResource(typeof(ContextMenu)) };
        var item = new MenuItem
        {
            Header = pinned ? "Unpin from toolbar" : "Pin to toolbar",
            Style = (Style)FindResource(typeof(MenuItem))
        };
        item.Click += (_, _) => SetToolPinned(tool.Id, !pinned);
        menu.Items.Add(item);
        return menu;
    }

    private void SetToolPinned(string id, bool pinned)
    {
        var pins = _settings.Appearance.PinnedTools;
        pins.Remove(id);
        if (pinned) pins.Add(id);
        BuildPinnedToolStrip();
        SaveSettings();
        StatusText.Text = pinned ? "Pinned to the toolbar" : "Removed from the toolbar";
    }

    /// <summary>The menu-row strip of tools the user pinned from any palette (same actions, no duplicates).</summary>
    private void BuildPinnedToolStrip()
    {
        PinnedToolStrip.Children.Clear();
        KeyboardNavigation.SetTabNavigation(PinnedToolStrip, KeyboardNavigationMode.Once);
        KeyboardNavigation.SetDirectionalNavigation(PinnedToolStrip, KeyboardNavigationMode.Contained);
        var tools = AllPaletteTools().ToDictionary(t => t.Id);
        foreach (var id in _settings.Appearance.PinnedTools.Distinct().ToList())
        {
            if (!tools.TryGetValue(id, out var tool)) continue;
            var button = new Button
            {
                Style = (Style)FindResource("ToolButton"),
                Content = new SvgIconView
                {
                    Icon = "tool:" + tool.Icon, ShowFrame = false, Width = 20, Height = 20, IsHitTestVisible = false,
                    IconColor = PaletteBrushColor("TextBrush", Color.FromRgb(0xC7, 0xCF, 0xDA))
                },
                Tag = tool.Id, Width = 26, Height = 24, MinWidth = 26, Padding = new Thickness(0), Margin = new Thickness(1, 0, 1, 0),
                ToolTip = PaletteToolTip(tool) + "\nRight-click to unpin."
            };
            System.Windows.Automation.AutomationProperties.SetName(button, tool.Label);
            WirePaletteButtonFocus(button);
            button.Click += ToolsPaletteButton_Click;
            button.ContextMenu = PinMenu(tool);
            button.ContextMenuOpening += (_, _) => button.ContextMenu = PinMenu(tool);
            PinnedToolStrip.Children.Add(button);
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
            if (Editor.IsVisible) Editor.Focus();
        };
    }

    private void ToolsPaletteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        switch (id)
        {
            case "edit:pointer": Editor.ClearSelection(); Editor.Focus(); break;
            case "edit:erase_note": Editor.DeleteNote(); break;
            case "gp:key_signature": KeySig_Click(sender, e); break;
            case "gp:triplet_feel": CycleTripletFeel(); break;
            case "gp:free_time": ToggleMeasureProperty(measure => measure.FreeTime,
                (measure, value) => measure.FreeTime = value, "Free-time measure"); break;
            case "gp:double_barline": DoubleBar_Click(sender, e); break;
            case "gp:repeat_one_bar": ToggleSimile(1); break;
            case "gp:repeat_two_bars": ToggleSimile(2); break;
            case "gp:directions": Directions_Click(sender, e); break;
            case "gp:insert_bar": InsertBar_Click(sender, e); break;
            case "gp:append_bar": AppendBar(); break;
            case "gp:duplicate_bar": DuplicateBar_Click(sender, e); break;
            case "gp:delete_bar": DeleteBar_Click(sender, e); break;
            case "gp:check_bars": CheckBars_Click(sender, e); break;
            case "gp:step_back": StepBeat(-1); break;
            case "gp:step_forward": StepBeat(1); break;
            case "gp:force_line_break": ToggleLineBreak(force: true); break;
            case "gp:prevent_line_break": ToggleLineBreak(force: false); break;
            case "gp:add_marker": AddMarker_Click(sender, e); break;
            case "gp:marker_list": FocusMarkerList(); break;
            case "gp:previous_marker": JumpSection(-1); break;
            case "gp:next_marker": JumpSection(1); break;
            case "gp:custom_ntuplet": ChooseTuplet(); break;
            case "gp:tie_note": Editor.TieSelectedNote(); break;
            case "gp:tie_beat": Editor.TieSelectedBeat(); break;
            case "gp:sound_duration": SetSoundDuration(); break;
            case "gp:octave_8va": Editor.SetOctaveShift(12); break;
            case "gp:octave_8vb": Editor.SetOctaveShift(-12); break;
            case "gp:octave_15ma": Editor.SetOctaveShift(24); break;
            case "gp:octave_15mb": Editor.SetOctaveShift(-24); break;
            case "gp:voice_1": Editor.SetActiveVoice(0); break;
            case "gp:voice_2": Editor.SetActiveVoice(1); break;
            case "gp:inactive_voice_gray": ToggleInactiveVoiceGray(); break;
            case "gp:beam_auto": Editor.SetBeamMode(BeamMode.Auto); Editor.SetSecondaryBeamBreak(false); break;
            case "gp:beam_force": Editor.SetBeamMode(BeamMode.Force); break;
            case "gp:beam_break": Editor.SetBeamMode(BeamMode.Break); break;
            case "gp:beam_break_secondary": Editor.SetSecondaryBeamBreak(Editor.GetNoteCellToolState(cell => cell.BreakSecondaryBeamBefore) != true); break;
            case "gp:stem_auto": Editor.SetStemDirection(StemDirection.Auto); break;
            case "gp:stem_invert": ToggleStemDirection(); break;
            case "composition:time_signature": TimeSig_Click(sender, e); break;
            case "composition:tempo": SetMeasureTempo(); break;
            case "composition:repeat_open": RepeatOpen_Click(sender, e); break;
            case "composition:repeat_close": RepeatClose_Click(sender, e); break;
            case "composition:alternate_ending": Directions_Click(sender, e); break;
            case "duration:whole": Editor.SetDuration(1); break;
            case "duration:half": Editor.SetDuration(2); break;
            case "duration:quarter": Editor.SetDuration(4); break;
            case "duration:eighth": Editor.SetDuration(8); break;
            case "duration:sixteenth": Editor.SetDuration(16); break;
            case "duration:thirtysecond": Editor.SetDuration(32); break;
            case "duration:sixtyfourth": Editor.SetDuration(64); break;
            case "duration:dotted": Editor.SetDots(1); break;
            case "duration:double-dotted": Editor.SetDots(2); break;
            case "duration:tie": Editor.ToggleTie(); break;
            case "duration:tuplet": Editor.ToggleTriplet(); break;
            case "duration:tuplet-menu": ChooseTuplet(); break;
            case "dynamic:ppp": Editor.SetDynamicVelocity(16); break;
            case "dynamic:pp": Editor.SetDynamicVelocity(33); break;
            case "dynamic:p": Editor.SetDynamicVelocity(49); break;
            case "dynamic:mp": Editor.SetDynamicVelocity(64); break;
            case "dynamic:mf": Editor.SetDynamicVelocity(80); break;
            case "dynamic:f": Editor.SetDynamicVelocity(Dynamics.Forte); break;
            case "dynamic:ff": Editor.SetDynamicVelocity(112); break;
            case "dynamic:fff": Editor.SetDynamicVelocity(127); break;
            case "effect:vibrato": Editor.ToggleTechnique(TechniqueNames.Vibrato); break;
            case "effect:bend": Editor.ToggleTechnique(TechniqueNames.Bend); break;
            case "effect:tremolo_bar": Editor.ToggleTechnique(TechniqueNames.TremoloBar); break;
            case "effect:slides": Editor.ToggleTechnique(TechniqueNames.LegatoSlide); break;
            case "effect:dead_note": Editor.ToggleDead(); break;
            case "effect:hammer_on_pull_off": Editor.ToggleTechnique(TechniqueNames.Hopo); break;
            case "effect:ghost_note": Editor.ToggleGhost(); break;
            case "effect:accent": Editor.SetAccent(1); break;
            case "effect:heavy_accent": Editor.SetAccent(2); break;
            case "effect:let_ring": Editor.ToggleTechnique(TechniqueNames.LetRing); break;
            case "effect:natural_harmonic": Editor.ToggleTechnique(TechniqueNames.Harmonic); break;
            case "effect:grace_note": Editor.ToggleGrace(); break;
            case "effect:trill": Editor.ToggleTechnique(TechniqueNames.Trill); break;
            case "effect:tremolo_picking": Editor.ToggleTechnique(TechniqueNames.TremoloPick); break;
            case "effect:palm_mute": Editor.ToggleTechnique(TechniqueNames.PalmMute); break;
            case "effect:staccato": Editor.ToggleStaccato(); break;
            case "effect:tapping": Editor.ToggleTechnique(TechniqueNames.Tapping); break;
            case "effect:slapping": Editor.ToggleTechnique(TechniqueNames.Slap); break;
            case "effect:popping": Editor.ToggleTechnique(TechniqueNames.Pop); break;
            case "effect:fade_in": Editor.ToggleTechnique(TechniqueNames.FadeIn); break;
            case "effect:chord":
            case "effect:chord_menu": Chord_Click(sender, e); break;
            case "effect:text": Text_Click(sender, e); break;
            case "effect:stroke_down": Editor.ToggleTechnique(TechniqueNames.BrushDown); break;
            case "effect:stroke_up": Editor.ToggleTechnique(TechniqueNames.BrushUp); break;
            case "effect:pickstroke_down": Editor.ToggleTechnique("PickDown"); break;
            case "effect:pickstroke_up": Editor.ToggleTechnique("PickUp"); break;
        }
        RefreshToolsPalette();
    }

    private void ToggleMeasureProperty(Func<MeasureModel, bool> getter, Action<MeasureModel, bool> setter, string label)
    {
        if (CurBar() is null) return;
        CaptureUndo();
        if (!_arrangementController.TryToggleMeasureProperty(_project, TrackMixerGrid.SelectedIndex,
                Editor.SelectedMeasure, getter, setter, out var value)) return;
        CommitEdit(EditRefresh.Score | EditRefresh.Arrangement | EditRefresh.Palette);
        StatusText.Text = value ? $"{label} on" : $"{label} off";
    }

    private void CycleTripletFeel()
    {
        if (CurBar() is null) return;
        CaptureUndo();
        if (!_arrangementController.TryCycleTripletFeel(_project, TrackMixerGrid.SelectedIndex,
                Editor.SelectedMeasure, out var next)) return;
        CommitEdit(EditRefresh.Score | EditRefresh.Palette);
        StatusText.Text = next switch { "None" => "Straight feel", "Triplet8th" => "Eighth-note swing", _ => "Sixteenth-note swing" };
    }

    private void ToggleSimile(int barCount)
    {
        if (CurBar() is null || Editor.SelectedMeasure < barCount) return;
        CaptureUndo();
        if (!_arrangementController.TryToggleSimile(_project, TrackMixerGrid.SelectedIndex,
                Editor.SelectedMeasure, barCount, out var enabled)) return;
        CommitEdit(EditRefresh.Score | EditRefresh.Arrangement | EditRefresh.Palette);
        StatusText.Text = enabled ? $"Repeating previous {barCount} bar{(barCount == 1 ? "" : "s")}" : "Simile repeat removed";
    }

    private void ToggleLineBreak(bool force)
    {
        if (CurBar() is null) return;
        CaptureUndo();
        if (!_arrangementController.TryToggleLineBreak(_project, TrackMixerGrid.SelectedIndex,
                Editor.SelectedMeasure, force, out var enabled)) return;
        CommitEdit(EditRefresh.Score | EditRefresh.Palette);
        StatusText.Text = force
            ? enabled ? "System break forced before this measure" : "Forced system break removed"
            : enabled ? "Automatic system break prevented before this measure" : "System-break prevention removed";
    }

    private void ToggleInactiveVoiceGray()
    {
        CaptureUndo();
        _project.GrayInactiveVoice = !_project.GrayInactiveVoice;
        CommitEdit(EditRefresh.Repaint | EditRefresh.Palette);
        StatusText.Text = _project.GrayInactiveVoice ? "Inactive voice dimmed" : "Inactive voice at normal brightness";
    }

    private void ToggleStemDirection()
    {
        var cell = Editor.CurrentCell();
        if (cell is null || !Editor.HasEditableNotes) return;
        Editor.SetStemDirection(cell.StemDirection == StemDirection.Invert ? StemDirection.Auto : StemDirection.Invert);
    }

    private void SetSoundDuration()
    {
        var cell = Editor.CurrentCell();
        if (cell is null || !Editor.HasEditableNotes) return;
        var text = GpDialogs.Prompt("Sound duration", "Sounding duration (% of written value, 1–200):",
            cell.SoundDurationPercent.ToString());
        if (text is null || !int.TryParse(text, out var percent)) return;
        Editor.SetSoundDurationPercent(Math.Clamp(percent, 1, 200));
        StatusText.Text = $"Sound duration {Math.Clamp(percent, 1, 200)}%";
    }

    private void FocusMarkerList()
    {
        var selected = _project.Markers.OrderBy(marker => marker.MeasureIndex)
            .FirstOrDefault(marker => marker.MeasureIndex >= Editor.SelectedMeasure)
            ?? _project.Markers.OrderBy(marker => marker.MeasureIndex).LastOrDefault();
        if (selected is not null)
        {
            MarkerList.SelectedItem = selected;
            MarkerList.ScrollIntoView(selected);
        }
        Keyboard.Focus(MarkerList);
        StatusText.Text = _project.Markers.Count == 0 ? "No markers yet" : "Marker list focused";
    }

    private void ChooseTuplet()
    {
        var current = Editor.CurrentCell()?.Tuplet ?? (3, 2);
        var numeratorText = GpDialogs.Prompt("Tuplet", "Notes in the tuplet (numerator):", current.Numerator.ToString());
        if (numeratorText is null || !int.TryParse(numeratorText, out var numerator)) return;
        var denominatorText = GpDialogs.Prompt("Tuplet", "Normal note value (denominator):", current.Denominator.ToString());
        if (denominatorText is null || !int.TryParse(denominatorText, out var denominator)) return;
        Editor.SetTuplet(numerator, denominator);
    }

    private void SetMeasureTempo()
    {
        var measure = Editor.CurrentMeasure();
        if (measure is null) return;
        var value = GpDialogs.Prompt("Tempo change", "Tempo in beats per minute (20–400):",
            (measure.TempoChange ?? _project.Tempo).ToString());
        if (value is null || !int.TryParse(value, out var tempo)) return;
        tempo = Math.Clamp(tempo, 20, 400);
        CaptureUndo();
        _arrangementController.TrySetTempoChange(_project, Editor.SelectedMeasure, tempo);
        _project.IsDirty = true;
        _project.MarkTimelineChanged();
        Editor.InvalidateScoreLayout();
        RebuildVisualTimeline();
        _midi.Rebuild(_project);
        RefreshArrangement();
        RefreshStatus();
        RefreshToolsPalette();
        UpdateTitle();
        StatusText.Text = $"Tempo change: ♩={tempo}";
    }
}
