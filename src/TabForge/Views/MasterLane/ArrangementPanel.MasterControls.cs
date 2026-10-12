using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using TabForge.Models;
using TabForge.Services;
using static TabForge.Views.TrackColumnLayout;

namespace TabForge.Views;

// Owns: the controls of the "Master" row in the track list: the master FX button, master volume and master pan, placed in the
//   track rows' FX / VOLUME / PAN columns and wired to the state the Mixer's Master row uses.
// Does not own: the row itself and its height (ArrangementPanel.MasterLane.cs), the master state (the song's mixer: Mixer.Master,
//   Mixer.MasterPan; the master volume knob), applying a mixer edit (MixerHost).
// Tests: TestMasterLane.

public sealed partial class ArrangementPanel
{
    private FxSplitButton? _masterFx;
    private Slider? _masterVolume, _masterPan;
    private bool _masterKnobHooked;

    /// <summary>The mixer's host: master pan and master FX edits go through it, exactly as the Mixer's Master row does.</summary>
    internal MixerHost? MasterMix { get; set; }

    /// <summary>The Master row's controls (test probe).</summary>
    internal (FxSplitButton? Fx, Slider? Volume, Slider? Pan) MasterLaneWidgets => (_masterFx, _masterVolume, _masterPan);

    /// <summary>The row's content: the label in the name column, then FX, volume and pan in the track rows' columns.</summary>
    private Grid MasterLaneControls(FrameworkElement label)
    {
        var project = _project!;
        var grid = new Grid { Margin = new Thickness(RowGridLeft, 0, RowGridRight, 0) };
        _columns.AddRowGrid(grid);
        var cells = new Dictionary<string, FrameworkElement> { ["name"] = label };

        var fx = new FxSplitButton
        {
            ChainOn = project.Mixer.Master.On, PluginCount = project.Mixer.Master.Rig.Plugins.Count,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Width = 50, Height = 22,
            ToolTip = "Master effects: FX opens the chain applied to the whole mix · power: bypass it"
        };
        fx.OpenChain += (_, _) => MasterMix?.OpenBusFx(null);
        fx.TogglePower += (_, _) =>
        {
            MasterMix?.BeginMixerEdit();
            MixerBuses.SetOn(project.Mixer.Master, !project.Mixer.Master.On);
            fx.ChainOn = project.Mixer.Master.On;
            MasterMixChanged();
        };
        cells["fx"] = _masterFx = fx;

        // The master volume is the transport's master knob (0-100 %): the slider and the knob move each other.
        cells["volume"] = _masterVolume = MasterSlider(0, 100, MasterVolumeKnob.Value, 100, v => MasterVolumeKnob.Value = v,
            "Master volume for all tracks (0–100 %, the same as the master knob; double-click: 100)");
        if (!_masterKnobHooked)
        {
            _masterKnobHooked = true;
            MasterVolumeKnob.ValueChanged += (_, e) => { if (_masterVolume is { } slider && slider.Value != e.NewValue) slider.Value = e.NewValue; };
        }

        cells["pan"] = _masterPan = MasterSlider(-64, 63, project.Mixer.MasterPan, 0, v =>
        {
            if (project.Mixer.MasterPan == v) return;
            project.Mixer.MasterPan = v;
            MasterMixChanged();
        }, "Master pan: moves the whole mix left or right (−64 left … 0 centre … +63 right; double-click: centre)");
        _mixSliders.Add((_masterPan, () => project.Mixer.MasterPan));   // follows the Mixer's master pan with the track sliders

        _columns.PlaceCells(grid, cells);
        return grid;
    }

    /// <summary>A master edit made here: the sound, the title and the open Mixer follow.</summary>
    private void MasterMixChanged()
    {
        if (MasterMix is not { } host) return;
        host.MixerChanged(recompile: false);
        host.Windows.Mixer?.SyncValues();
    }

    /// <summary>A slider like a track row's (same style, drag and wheel steps); a gesture starts a mixer edit.</summary>
    private Slider MasterSlider(int min, int max, double value, int defaultValue, Action<int> set, string tip)
    {
        var slider = new Slider
        {
            Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), IsSnapToTickEnabled = true, TickFrequency = 1,
            SmallChange = 1, LargeChange = 8, Margin = new Thickness(6, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.FindResource("ArrangementSlider"), ToolTip = tip
        };
        AutomationProperties.SetName(slider, tip);
        slider.PreviewMouseLeftButtonDown += (_, _) => MasterMix?.BeginMixerEdit();
        slider.PreviewKeyDown += (_, e) => { if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End) MasterMix?.BeginMixerEdit(); };
        slider.PreviewMouseWheel += (_, e) => { if (e.Delta != 0) MasterMix?.BeginMixerEdit(); };
        slider.ValueChanged += (_, e) => set((int)Math.Round(e.NewValue));
        TrackControlWidgets.AttachSmoothDrag(slider, defaultValue);
        TrackControlWidgets.AttachWheelStep(slider);
        return slider;
    }
}
