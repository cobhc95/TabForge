using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views;
using TabForge.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the knob's value rules (clamp and step, typed text, edit gestures), KnobMath and the knobs in the FX chain and track properties windows.
/// Does not own: the drawing (render identity and captures).
/// Tests: this class.
/// </summary>
public class KnobControlTests
{
    [Fact]
    public void KnobMathSnapsClampsAndMapsAngles()
    {
        Assert.Equal(1.3, KnobMath.Clamp(1.26, -60, 12, 0.1));
        Assert.Equal(12, KnobMath.Clamp(double.PositiveInfinity, -60, 12, 0.1));
        Assert.Equal(-135, KnobMath.AngleOf(0, 0, 127));
        Assert.Equal(135, KnobMath.AngleOf(127, 0, 127));
        Assert.True(KnobMath.DragByAngle(10, 0.5, 0, 127, false) > 10);
    }

    [AvaloniaFact]
    public void ValueIsClampedAndEditsAreGrouped()
    {
        var knob = new KnobControl { Minimum = 0, Maximum = 127, Origin = 64, DefaultValue = 64, Parse = KnobValueParser.ParsePan };
        var events = new List<string>();
        knob.EditStarted += (_, _) => events.Add("start");
        knob.ValueChanged += (_, v) => events.Add("value " + v);
        knob.EditEnded += (_, _) => events.Add("end");
        knob.Value = 500;
        Assert.Equal(127, knob.Value);
        events.Clear();
        Assert.True(knob.TryParseInput("L 20", out var parsed));
        Assert.Equal(44, parsed);
        Assert.False(knob.TryParseInput("nonsense", out _));
        knob.BeginTypeValue();
        knob.Editor!.Text = "R 5";
        knob.FinishEdit(true);
        Assert.Equal(69, knob.Value);
        Assert.Equal(new[] { "start", "value 69", "end" }, events);
    }

    [AvaloniaFact]
    public void FxChainVolumeIsAKnobThatDrivesTheText()
    {
        var window = FxTestKit.Window(FxTestKit.Track(FxChainWindow.SampleChain(), "Track"));
        window.Show();
        var knob = DialogTestKit.Named<KnobControl>(window, "VolumeSlider");
        var text = DialogTestKit.Named<TextBlock>(window, "VolumeText");
        knob.Value = 2;
        Assert.Equal("+2.0 dB", text.Text);
    }

    [AvaloniaFact]
    public void TrackPropertiesMixerKnobsWriteVolumeAndPan()
    {
        var track = new TabForge.Models.TrackModel { Name = "T", Volume = 100, Pan = 64 };
        var window = new TrackPropertiesWindow(track);
        window.Show();
        DialogTestKit.Named<KnobControl>(window, "VolumeKnob").Value = 50;
        DialogTestKit.Named<KnobControl>(window, "PanKnob").Value = 30;
        Assert.Equal("L 34", DialogTestKit.Named<TextBlock>(window, "PanReadout").Text);
    }
}
