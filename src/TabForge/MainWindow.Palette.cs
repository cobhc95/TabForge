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
// Owns: the tool palette's host side: tool definitions, palette panels, the pinned strip and tool actions.
// Does not own: the palette controller (ToolPaletteController).
// Tests: listed in docs/feature-map/editing-and-notation.md.
public partial class MainWindow : IToolActionsHost
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
        if (!ToolActions.TryRun(id)) switch (id)
        {
            case "edit:pointer": Editor.ClearSelection(); Editor.Focus(); break;
            case "edit:erase_note": Editor.Effects.DeleteNote(); break;
            case "gp:key_signature": KeySig_Click(sender, e); break;
            case "gp:double_barline": DoubleBar_Click(sender, e); break;
            case "gp:directions": Directions_Click(sender, e); break;
            case "gp:insert_bar": InsertBar_Click(sender, e); break;
            case "gp:append_bar": AppendBar(); break;
            case "gp:duplicate_bar": DuplicateBar_Click(sender, e); break;
            case "gp:delete_bar": DeleteBar_Click(sender, e); break;
            case "gp:step_back": StepBeat(-1); break;
            case "gp:step_forward": StepBeat(1); break;
            case "gp:add_marker": AddMarker_Click(sender, e); break;
            case "gp:previous_marker": JumpSection(-1); break;
            case "gp:next_marker": JumpSection(1); break;
            case "gp:tie_note": Editor.Effects.TieSelectedNote(); break;
            case "gp:tie_beat": Editor.Effects.TieSelectedBeat(); break;
            case "gp:octave_8va": Editor.Effects.SetOctaveShift(12); break;
            case "gp:octave_8vb": Editor.Effects.SetOctaveShift(-12); break;
            case "gp:octave_15ma": Editor.Effects.SetOctaveShift(24); break;
            case "gp:octave_15mb": Editor.Effects.SetOctaveShift(-24); break;
            case "gp:voice_1": Editor.SetActiveVoice(0); break;
            case "gp:voice_2": Editor.SetActiveVoice(1); break;
            case "gp:beam_auto": Editor.Effects.SetBeamMode(BeamMode.Auto); Editor.Effects.SetSecondaryBeamBreak(false); break;
            case "gp:beam_force": Editor.Effects.SetBeamMode(BeamMode.Force); break;
            case "gp:beam_break": Editor.Effects.SetBeamMode(BeamMode.Break); break;
            case "gp:beam_break_secondary": Editor.Effects.SetSecondaryBeamBreak(Editor.Effects.GetNoteCellToolState(cell => cell.BreakSecondaryBeamBefore) != true); break;
            case "gp:stem_auto": Editor.Effects.SetStemDirection(StemDirection.Auto); break;
            case "composition:time_signature": TimeSig_Click(sender, e); break;
            case "composition:repeat_open": RepeatOpen_Click(sender, e); break;
            case "composition:repeat_close": RepeatClose_Click(sender, e); break;
            case "composition:alternate_ending": Directions_Click(sender, e); break;
            case "duration:whole": Editor.Effects.SetDuration(1); break;
            case "duration:half": Editor.Effects.SetDuration(2); break;
            case "duration:quarter": Editor.Effects.SetDuration(4); break;
            case "duration:eighth": Editor.Effects.SetDuration(8); break;
            case "duration:sixteenth": Editor.Effects.SetDuration(16); break;
            case "duration:thirtysecond": Editor.Effects.SetDuration(32); break;
            case "duration:sixtyfourth": Editor.Effects.SetDuration(64); break;
            case "duration:dotted": Editor.Effects.SetDots(1); break;
            case "duration:double-dotted": Editor.Effects.SetDots(2); break;
            case "duration:tie": Editor.Effects.ToggleTie(); break;
            case "duration:tuplet": Editor.Effects.ToggleTriplet(); break;
            case "dynamic:ppp": Editor.Effects.SetDynamicVelocity(16); break;
            case "dynamic:pp": Editor.Effects.SetDynamicVelocity(33); break;
            case "dynamic:p": Editor.Effects.SetDynamicVelocity(49); break;
            case "dynamic:mp": Editor.Effects.SetDynamicVelocity(64); break;
            case "dynamic:mf": Editor.Effects.SetDynamicVelocity(80); break;
            case "dynamic:f": Editor.Effects.SetDynamicVelocity(Dynamics.Forte); break;
            case "dynamic:ff": Editor.Effects.SetDynamicVelocity(112); break;
            case "dynamic:fff": Editor.Effects.SetDynamicVelocity(127); break;
            case "effect:vibrato": Editor.Effects.ToggleTechnique(TechniqueNames.Vibrato); break;
            case "effect:bend": OpenEffectEditor(Views.EffectEditors.EffectEditorKind.Bend); break;
            case "effect:tremolo_bar": OpenEffectEditor(Views.EffectEditors.EffectEditorKind.TremoloBar); break;
            case "effect:slides": Editor.Effects.ToggleTechnique(TechniqueNames.LegatoSlide); break;
            case "effect:dead_note": Editor.Effects.ToggleDead(); break;
            case "effect:hammer_on_pull_off": Editor.Effects.ToggleTechnique(TechniqueNames.Hopo); break;
            case "effect:ghost_note": Editor.Effects.ToggleGhost(); break;
            case "effect:accent": Editor.Effects.SetAccent(1); break;
            case "effect:heavy_accent": Editor.Effects.SetAccent(2); break;
            case "effect:let_ring": Editor.Effects.ToggleTechnique(TechniqueNames.LetRing); break;
            case "effect:natural_harmonic": OpenEffectEditor(Views.EffectEditors.EffectEditorKind.Harmonic); break;
            case "effect:grace_note": OpenEffectEditor(Views.EffectEditors.EffectEditorKind.Grace); break;
            case "effect:trill": OpenEffectEditor(Views.EffectEditors.EffectEditorKind.Trill); break;
            case "effect:tremolo_picking": Editor.Effects.ToggleTechnique(TechniqueNames.TremoloPick); break;
            case "effect:palm_mute": Editor.Effects.ToggleTechnique(TechniqueNames.PalmMute); break;
            case "effect:staccato": Editor.Effects.ToggleStaccato(); break;
            case "effect:tapping": Editor.Effects.ToggleTechnique(TechniqueNames.Tapping); break;
            case "effect:slapping": Editor.Effects.ToggleTechnique(TechniqueNames.Slap); break;
            case "effect:popping": Editor.Effects.ToggleTechnique(TechniqueNames.Pop); break;
            case "effect:fade_in": Editor.Effects.ToggleTechnique(TechniqueNames.FadeIn); break;
            case "effect:chord":
            case "effect:chord_menu": Chord_Click(sender, e); break;
            case "effect:text": Text_Click(sender, e); break;
            case "effect:stroke_down": Editor.Effects.ToggleTechnique(TechniqueNames.BrushDown); break;
            case "effect:stroke_up": Editor.Effects.ToggleTechnique(TechniqueNames.BrushUp); break;
            case "effect:pickstroke_down": Editor.Effects.ToggleTechnique("PickDown"); break;
            case "effect:pickstroke_up": Editor.Effects.ToggleTechnique("PickUp"); break;
        }
        RefreshToolsPalette();
    }

    private ToolActionsFlow? _toolActions;
    private ToolActionsFlow ToolActions => _toolActions ??= new ToolActionsFlow(this);
    DocumentSession IToolActionsHost.ActiveDocument => Doc;
    int IToolActionsHost.SelectedTrackRow => TrackMixerGrid.SelectedIndex;
    ArrangementController IToolActionsHost.Arrangement => _arrangementController;
    void IToolActionsHost.FocusMarkerList(MarkerModel? marker)
    {
        if (marker is not null)
        {
            MarkerList.SelectedItem = marker;
            MarkerList.ScrollIntoView(marker);
        }
        Keyboard.Focus(MarkerList);
    }

    void IToolActionsHost.RefreshAfterEdit(ToolRefresh refresh) => RefreshAfterEdit(
        (refresh.HasFlag(ToolRefresh.Score) ? EditRefresh.Score : 0) | (refresh.HasFlag(ToolRefresh.Repaint) ? EditRefresh.Repaint : 0) |
        (refresh.HasFlag(ToolRefresh.Arrangement) ? EditRefresh.Arrangement : 0) | (refresh.HasFlag(ToolRefresh.Palette) ? EditRefresh.Palette : 0) |
        (refresh.HasFlag(ToolRefresh.Instrument) ? EditRefresh.Instrument : 0));

    void IToolActionsHost.RefreshAfterTempoChange()
    {
        RebuildVisualTimeline();
        _midi.Rebuild(_project);
        RefreshArrangement();
        RefreshStatus();
        RefreshToolsPalette();
        UpdateTitle();
    }
}
