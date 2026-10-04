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
public partial class MainWindow : IToolPaletteHost
{
    private ToolPaletteController? _toolPalette;
    private ToolPaletteController ToolPalette => _toolPalette ??= new ToolPaletteController(this);
    private void RefreshToolsPalette() => ToolPalette.RefreshToolsPalette();
    private DockWorkspace? _dockWorkspace;

    static MainWindow() => ToolPaletteController.RegisterHotkeys();

    StackPanel IToolPaletteHost.ToolsPaletteHost => ToolsPaletteHost;
    Panel IToolPaletteHost.ToolsTabStrip => ToolsTabStrip;
    Panel IToolPaletteHost.PinnedToolStrip => PinnedToolStrip;
    MeasureModel? IToolPaletteHost.CurrentBar => CurBar();
    int IToolPaletteHost.MaxMeasures => MaxMeasures();
    void IToolPaletteHost.RunPaletteTool(object sender, RoutedEventArgs e) => ToolsPaletteButton_Click(sender, e);

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
            case "effect:grace_note": Editor.TryRunNoteCommand("Note.Grace"); break;
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
        var value = false;
        if (!DocumentEdits.Run(Doc, p => _arrangementController.TryToggleMeasureProperty(p, TrackMixerGrid.SelectedIndex,
                Editor.SelectedMeasure, getter, setter, out value)).Changed) return;
        RefreshAfterEdit(EditRefresh.Score | EditRefresh.Arrangement | EditRefresh.Palette);
        StatusText.Text = value ? $"{label} on" : $"{label} off";
    }

    private void CycleTripletFeel()
    {
        if (CurBar() is null) return;
        var next = "";
        if (!DocumentEdits.Run(Doc, p => _arrangementController.TryCycleTripletFeel(p, TrackMixerGrid.SelectedIndex,
                Editor.SelectedMeasure, out next)).Changed) return;
        RefreshAfterEdit(EditRefresh.Score | EditRefresh.Palette);
        StatusText.Text = next switch { "None" => "Straight feel", "Triplet8th" => "Eighth-note swing", _ => "Sixteenth-note swing" };
    }

    private void ToggleSimile(int barCount)
    {
        if (CurBar() is null || Editor.SelectedMeasure < barCount) return;
        var enabled = false;
        if (!DocumentEdits.Run(Doc, p => _arrangementController.TryToggleSimile(p, TrackMixerGrid.SelectedIndex,
                Editor.SelectedMeasure, barCount, out enabled)).Changed) return;
        RefreshAfterEdit(EditRefresh.Score | EditRefresh.Arrangement | EditRefresh.Palette);
        StatusText.Text = enabled ? $"Repeating previous {barCount} bar{(barCount == 1 ? "" : "s")}" : "Simile repeat removed";
    }

    private void ToggleLineBreak(bool force)
    {
        if (CurBar() is null) return;
        var enabled = false;
        if (!DocumentEdits.Run(Doc, p => _arrangementController.TryToggleLineBreak(p, TrackMixerGrid.SelectedIndex,
                Editor.SelectedMeasure, force, out enabled)).Changed) return;
        RefreshAfterEdit(EditRefresh.Score | EditRefresh.Palette);
        StatusText.Text = force
            ? enabled ? "System break forced before this measure" : "Forced system break removed"
            : enabled ? "Automatic system break prevented before this measure" : "System-break prevention removed";
    }

    private void ToggleInactiveVoiceGray()
    {
        DocumentEdits.Run(Doc, p => { p.GrayInactiveVoice = !p.GrayInactiveVoice; return true; });
        RefreshAfterEdit(EditRefresh.Repaint | EditRefresh.Palette);
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
        DocumentEdits.Run(Doc, p => { _arrangementController.TrySetTempoChange(p, Editor.SelectedMeasure, tempo); return true; });
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
