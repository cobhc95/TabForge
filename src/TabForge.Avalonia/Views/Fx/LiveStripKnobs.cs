using System;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Avalonia.Platform;
using TabForge.LiveInput;
using TabForge.Platform.Theme;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the levels part of the live input strip: the Gain, Vol and Pan mini sliders (the track list slider painter; a double-click returns each to its declared default)
//   and the M and S buttons (the track list toggle painter).
// Does not own: what the values do (LiveInputSession), their defaults (LiveStripRules, MixerStripPresenter) or the drawing (TrackRowCells, MiniSlider, LiveStripToggle).
// Tests: TabForge.Avalonia.Tests.LiveInputStripTests, LiveStripSliderTests.
/// <summary>The levels of the strip. It raises one event per control when the user (or a reset) changes it; <see cref="Show"/> moves them without raising anything.</summary>
public sealed class LiveStripKnobs : StackPanel
{
    public const string GainLabel = "Gain", VolumeLabel = "Vol", PanLabel = "Pan";
    /// <summary>Pan is shown from -64 (left) to +63 (right), like the track list; the live input stores 0..127.</summary>
    public const int PanOffset = 64;

    /// <param name="subject">What the levels belong to, as the tooltips say it ("Live input", "Track").</param>
    public LiveStripKnobs(string subject = "Live input")
    {
        Name = "LiveKnobs";
        Orientation = Orientation.Horizontal;
        Spacing = ChromeMetrics.LiveStripGap;
        VerticalAlignment = VerticalAlignment.Center;
        Gain = Add(GainLabel, LiveStripRules.MinGainDb, LiveStripRules.MaxGainDb, LiveStripRules.DefaultGainDb, "Gain into the FX chain in dB (double-click: 0)", v => GainChanged?.Invoke(v));
        Volume = Add(VolumeLabel, 0, MidiLimits.Max, MixerStripPresenter.VolumeDefault, $"{subject} volume in the output (0-127; double-click: 100)", v => VolumeChanged?.Invoke(v));
        Pan = Add(PanLabel, -PanOffset, PanOffset - 1, MixerStripPresenter.PanDefault, $"{subject} pan in the output (-64 left, 0 centre, +63 right; double-click: centre)", v => PanChanged?.Invoke(v + PanOffset));
        Mute = Toggle(LiveStripToggle.Look.Mute, "M", "Mute: silence the live input in the output (the chain and the tuner keep listening)", on => MuteChanged?.Invoke(on));
        Solo = Toggle(LiveStripToggle.Look.Solo, "S", "Solo: hear only the live input (the song tracks stay silent unless soloed too)", on => SoloChanged?.Invoke(on));
    }

    /// <summary>The M and S buttons: a second group of the strip, so the strip can wrap it apart from the sliders.</summary>
    public StackPanel Buttons { get; } = new() { Name = "LiveButtons", Orientation = Orientation.Horizontal, Spacing = ChromeMetrics.LiveStripGap, VerticalAlignment = VerticalAlignment.Center };

    public MiniSlider Gain { get; }
    public MiniSlider Volume { get; }
    public MiniSlider Pan { get; }
    public LiveStripToggle Mute { get; }
    public LiveStripToggle Solo { get; }

    public event Action<int>? GainChanged, VolumeChanged, PanChanged;
    public event Action<bool>? MuteChanged, SoloChanged;

    /// <summary>Moves the controls to these values (<paramref name="pan"/> is the stored 0..127) without raising the events.</summary>
    public void Show(int gainDb, int volume, int pan, bool muted, bool soloed)
    {
        Gain.Value = gainDb; Volume.Value = volume; Pan.Value = pan - PanOffset; Mute.Display(muted); Solo.Display(soloed);
    }

    /// <summary>Hides the Gain label and slider (a song track has no input gain).</summary>
    public void HideGain() { Children[0].IsVisible = false; Gain.IsVisible = false; }

    private MiniSlider Add(string label, int min, int max, int reset, string tip, Action<int> changed)
    {
        var name = new TextBlock { Text = label, Width = ChromeMetrics.LiveStripSliderLabelWidth, FontSize = ChromeMetrics.LiveStripFontSize, VerticalAlignment = VerticalAlignment.Center, [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted") };
        var slider = new MiniSlider(min, max, reset, ChromeMetrics.LiveStripSliderWidth, ChromeMetrics.LiveStripControlHeight) { Name = label + "Slider", VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(slider, tip);
        ToolTip.SetTip(name, tip);
        slider.ValueChanged += changed;
        Children.Add(name); Children.Add(slider);
        return slider;
    }

    private LiveStripToggle Toggle(LiveStripToggle.Look look, string letter, string tip, Action<bool> toggled)
    {
        var toggle = new LiveStripToggle(look, letter, ChromeMetrics.LiveStripToggleWidth, ChromeMetrics.LiveStripControlHeight) { Name = look == LiveStripToggle.Look.Mute ? "LiveMute" : "LiveSolo", VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(toggle, tip);
        toggle.Toggled += toggled;
        Buttons.Children.Add(toggle);
        return toggle;
    }
}
