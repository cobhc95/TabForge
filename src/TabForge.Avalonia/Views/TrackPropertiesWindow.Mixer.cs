using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Platform;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The Mixer card of the track properties: the volume and pan knobs with their labels and readouts, as in the dialog.
/// Owns: the two knobs and their card. Does not own: the knob (KnobControl), the texts (KnobText), writing the values (OnOk calls WriteMix).
/// The FX button opens the track's FX chain window (FxChainWindow); it is disabled while the track is being added.
/// Tests: TabForge.Avalonia.Tests.TrackPropertiesWindowTests.
/// </summary>
public sealed partial class TrackPropertiesWindow
{
    private const double MixerKnobSize = 44;
    private const double KnobBlockGap = 26;
    private KnobControl? _volumeKnob, _panKnob;

    private Border MixerCard()
    {
        _volumeKnob = MixerKnob("VolumeKnob", "Volume", _track.Volume, 100, 0, KnobText.VolumePercent);
        _volumeKnob.FromDisplay = percent => percent * KnobText.VolumeUnitsPerPercent;
        _panKnob = MixerKnob("PanKnob", "Pan", _track.Pan, KnobText.PanCentre, KnobText.PanCentre, KnobText.Pan);
        _panKnob.Parse = KnobValueParser.ParsePan;
        var knobs = new StackPanel { Orientation = Orientation.Horizontal, Children = { KnobBlock(_volumeKnob), KnobBlock(_panKnob) } };
        return Card("Mixer", new StackPanel { Children = { knobs, Hint("Drag or scroll a knob; double-click to type a value; Ctrl+click to reset."), FxChainButton() } });
    }

    private Button FxChainButton()
    {
        var button = new Button
        {
            Name = "FxChainButton", Content = "Effects & instruments (FX)…", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = !_adding,
        };
        ToolTip.SetTip(button, _adding ? "Available once the track has been added" : "Open this track's FX chain window (plug-in instruments and effects)");
        button.Click += (_, _) => TabForge.Avalonia.Views.Fx.FxChainWindows.OpenFor(this, _track);
        return button;
    }

    private static KnobControl MixerKnob(string name, string label, double value, double defaultValue, double origin, System.Func<double, string> format) => new()
    {
        Name = name, Minimum = 0, Maximum = MidiLimits.Max, Value = value, DefaultValue = defaultValue, Origin = origin, Label = label, Format = format,
        Width = MixerKnobSize, Height = MixerKnobSize, HorizontalAlignment = HorizontalAlignment.Center,
    };

    private static StackPanel KnobBlock(KnobControl knob)
    {
        var readout = new TextBlock { Name = knob.Label + "Readout", HorizontalAlignment = HorizontalAlignment.Center, FontSize = 11.5, Margin = new Thickness(0, 4, 0, 0), Text = knob.DisplayText() };
        knob.ValueChanged += (_, _) => readout.Text = knob.DisplayText();
        var caption = new TextBlock { Text = knob.Label, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0) };
        return new StackPanel { Margin = new Thickness(0, 4, KnobBlockGap, 0), Children = { knob, caption, readout } };
    }

    // Runs inside the OK edit, after the fields.
    private void WriteMix()
    {
        _track.Volume = (int)_volumeKnob!.Value;
        _track.Pan = (int)_panKnob!.Value;
    }
}
