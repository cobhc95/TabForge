using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The Mixer card of the track properties: the volume and pan knobs with their labels and readouts, as in the dialog; the knob help is a tooltip on the row.
/// Owns: the two knobs and their card. Does not own: the knob (KnobControl), the texts (KnobText), writing the values (OnOk calls WriteMix).
/// The FX button sits full width beneath the knobs (the card is under the Details card) and opens the track's FX chain window (FxChainWindow); it is disabled while the track is being added.
/// Tests: TabForge.Avalonia.Tests.TrackPropertiesWindowTests.
/// </summary>
public sealed partial class TrackPropertiesWindow
{
    private const double MixerKnobSize = 34;
    private const string KnobHelp = "Drag or scroll a knob; double-click to type a value; Ctrl+click to reset.";
    private KnobControl? _volumeKnob, _panKnob;

    private Border MixerCard()
    {
        _volumeKnob = MixerKnob("VolumeKnob", "Volume", _track.Volume, 100, 0, KnobText.VolumePercent);
        _volumeKnob.FromDisplay = percent => percent * KnobText.VolumeUnitsPerPercent;
        _panKnob = MixerKnob("PanKnob", "Pan", _track.Pan, KnobText.PanCentre, KnobText.PanCentre, KnobText.Pan);
        _panKnob.Parse = KnobValueParser.ParsePan;
        var knobs = new UniformGrid { Columns = 2, Children = { KnobBlock(_volumeKnob), KnobBlock(_panKnob) } };
        var row = new StackPanel { Children = { knobs, FxChainButton() } };
        ToolTip.SetTip(row, KnobHelp);
        var card = Card("Mixer", row);
        card.Name = "MixerCard";
        return card;
    }

    private Button FxChainButton()
    {
        var button = new Button
        {
            Name = "FxChainButton", Content = "Effects & instruments (FX)…", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, IsEnabled = !_adding,
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
        var readout = new TextBlock { Name = knob.Label + "Readout", HorizontalAlignment = HorizontalAlignment.Center, FontSize = ChromeMetrics.TextSmall, Margin = new Thickness(0, 2, 0, 0), Text = knob.DisplayText() };
        knob.ValueChanged += (_, _) => readout.Text = knob.DisplayText();
        var caption = new TextBlock { Text = knob.Label, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 2, 0, 0) };
        return new StackPanel { Margin = new Thickness(0, 2, 0, 0), Children = { knob, caption, readout } };
    }

    // Runs inside the OK edit, after the fields.
    private void WriteMix()
    {
        _track.Volume = (int)_volumeKnob!.Value;
        _track.Pan = (int)_panKnob!.Value;
    }
}
