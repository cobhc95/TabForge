using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: the small value controls of a mixer row: the fader (with its undo-step gesture hooks), the pitch stepper and the mute / solo toggles.
// Does not own: the rows that place them (MixerWindow.cs), the undo steps themselves (MixerWindow.Edits.cs), the values and their texts (MixerStripPresenter).
// Tests: TabForge.Avalonia.Tests.MixerWindowTests.
/// <summary>The controls the mixer rows are built from.</summary>
public sealed partial class MixerWindow
{
    /// <summary>A horizontal slider (double-click resets, the wheel steps 1 or 8 with Ctrl and never scrolls the mixer).</summary>
    /// <param name="songEdit">True for a fader of the song (its gestures are undo steps and its changes mark the song dirty); false for the master volume,
    /// which lives in the audio settings.</param>
    private Control Fader(Func<int> get, int min, int max, int defaultValue, Action<int> set, string tip, bool songEdit = true)
    {
        var slider = new Slider { Name = "Fader", Minimum = min, Maximum = max, Value = get(), SmallChange = 1, LargeChange = Math.Max(1, (max - min) / 10.0), IsSnapToTickEnabled = true, TickFrequency = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
        slider.Classes.Add(AvaloniaArrangementSlider.ClassName);
        ToolTip.SetTip(slider, tip);
        AutomationProperties.SetName(slider, tip);
        var stepLabel = StepLabel(tip);
        slider.ValueChanged += (_, e) =>
        {
            if (_building) return;
            var value = (int)Math.Round(e.NewValue);
            // A value the song already holds (the slider settling on its own value) is no change: it neither dirties the song nor adds a step.
            if (songEdit)
            {
                if (value == get()) return;
                // A change whose press, key or wheel never opened the gesture still commits as that gesture's one step.
                if (!_gestureOpen) BeginEdit(stepLabel);
                Mutate(() => set(value));
            }
            else
            {
                // The master volume is output level, not song content: it reaches the process on this pass and never dirties the song.
                if (value != get()) set(value);
            }
        };
        AvaloniaSliderReset.OnDoubleTap(slider, defaultValue, () => { if (songEdit) BeginEdit(stepLabel); });
        if (songEdit)
        {
            // A press, the first key of a run or a wheel tick opens the gesture's one undo step; the drag that follows adds no more steps.
            slider.AddHandler(InputElement.PointerPressedEvent, (_, _) => BeginEdit(stepLabel), RoutingStrategies.Tunnel);
            // Tunnelled, so the slider's own handling of the arrow keys (which marks them handled) cannot hide the gesture from this handler.
            slider.AddHandler(InputElement.KeyDownEvent, (_, e) => { if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End) BeginEdit(stepLabel); }, RoutingStrategies.Tunnel);
        }
        slider.PointerWheelChanged += (_, e) =>
        {
            if (e.Delta.Y == 0) return;
            if (songEdit) BeginEdit(stepLabel);
            slider.Value = MixerStripPresenter.WheelValue(slider.Value, min, max, e.Delta.Y > 0, e.KeyModifiers.HasFlag(KeyModifiers.Control));
            e.Handled = true;
        };
        return slider;
    }

    /// <summary>The readable undo label of a fader: the tip's head before the explanation ("Master pan: ..." becomes "Master pan").</summary>
    private static string StepLabel(string tip)
    {
        var end = tip.IndexOfAny(new[] { ':', '(' });
        return end > 0 ? tip[..end].Trim() : tip;
    }

    /// <summary>The pitch or transpose stepper: − and + around the semitone text from the presenter.</summary>
    private static Control Stepper(int value, Action<int> set, string tip)
    {
        var current = value;
        var text = new TextBlock { Name = "Semitones", Text = MixerStripPresenter.SemitoneText(value), VerticalAlignment = VerticalAlignment.Center, MinWidth = 38, TextAlignment = TextAlignment.Center };
        Button Step(string label, int delta)
        {
            var b = new Button { Content = label, Width = ChromeMetrics.SwatchSize, Height = ChromeMetrics.SwatchSize, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center };
            b.Click += (_, _) =>
            {
                var next = MixerStripPresenter.StepSemitones(current, delta);
                if (next == current) return;
                current = next;
                text.Text = MixerStripPresenter.SemitoneText(current);
                set(current);
            };
            return b;
        }
        var row = new DockPanel { LastChildFill = true };
        ToolTip.SetTip(row, tip);
        var down = Step("−", -1); DockPanel.SetDock(down, Dock.Left); row.Children.Add(down);
        var up = Step("+", 1); DockPanel.SetDock(up, Dock.Right); row.Children.Add(up);
        row.Children.Add(text);
        return row;
    }

    private static Control MuteSolo(bool mute, bool solo, Action<bool> setMute, Action<bool> setSolo, Action? changed)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(Toggle("M", "Mute", mute, setMute, changed));
        row.Children.Add(Toggle("S", "Solo", solo, setSolo, changed));
        return row;
    }

    /// <summary>The M and S toggle height in DIP.</summary>
    private const double MuteSoloToggleHeight = 26;

    private static ToggleButton Toggle(string label, string tip, bool on, Action<bool> set, Action? changed)
    {
        var b = new ToggleButton { Name = tip, Content = label, IsChecked = on, Width = 34, Height = MuteSoloToggleHeight, Margin = new Thickness(2, 0), HorizontalContentAlignment = HorizontalAlignment.Center, Classes = { AvaloniaFormControlStyles.MixerToggleClass } };
        ToolTip.SetTip(b, tip);
        b.IsCheckedChanged += (_, _) => { set(b.IsChecked == true); changed?.Invoke(); };
        return b;
    }
}
