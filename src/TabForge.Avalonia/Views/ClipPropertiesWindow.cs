using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

/// <summary>
/// Audio clip properties: name, volume, pitch and speed sliders (a double-click resets one), Muted, the file note, OK and Cancel.
/// OK writes the values to the clip and closes with true; Cancel and Esc close with false and change nothing.
/// Owns: the window's controls and layout. Does not own: the texts, ranges and the write-back (<see cref="ClipPropertiesPresenter"/>).
/// Tests: TabForge.Avalonia.Tests.ClipSnapPluginDialogTests, TabForge.Avalonia.Tests.ClipPropertiesTests (Clip.Properties edits a copy).
/// </summary>
public sealed class ClipPropertiesWindow : Window
{
    /// <summary>A sample clip for the preview, which has no song.</summary>
    public static AudioClip SampleClip() => new() { Name = "Take 1", File = "take1.wav", GainDb = -3, Pitch = 2, Speed = 1.25 };

    public ClipPropertiesWindow(AudioClip clip)
    {
        Title = ClipPropertiesPresenter.Title;
        Width = DialogMetrics.SmallDialogWidth; SizeToContent = SizeToContent.Height; CanResize = false; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*,64") };
        void Row(string label, Control editor, Control? value = null)
        {
            var r = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 5, 8, 5) };
            Grid.SetRow(text, r); grid.Children.Add(text);
            Grid.SetRow(editor, r); Grid.SetColumn(editor, 1); grid.Children.Add(editor);
            if (value is null) return;
            Grid.SetRow(value, r); Grid.SetColumn(value, 2); grid.Children.Add(value);
        }
        var name = new TextBox { Text = clip.Name, Margin = new Thickness(0, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center };
        Row(ClipPropertiesPresenter.NameLabel, name);
        Slider Slide(ClipPropertiesPresenter.SliderRow spec, double value)
        {
            var slider = new Slider { Minimum = spec.Minimum, Maximum = spec.Maximum, Value = value, SmallChange = spec.Step, LargeChange = spec.Step * 4,
                IsSnapToTickEnabled = true, TickFrequency = spec.Step, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(slider, ClipPropertiesPresenter.ResetTip);
            var shown = new TextBlock { Text = spec.Format(value), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            slider.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) shown.Text = spec.Format(slider.Value); };
            AvaloniaSliderReset.OnDoubleTap(slider, spec.Reset);
            Row(spec.Label, slider, shown);
            return slider;
        }
        var gain = Slide(ClipPropertiesPresenter.Volume, clip.GainDb);
        var pitch = Slide(ClipPropertiesPresenter.Pitch, clip.Pitch);
        var speed = Slide(ClipPropertiesPresenter.Speed, clip.Speed);
        var muted = new CheckBox { Content = ClipPropertiesPresenter.MutedLabel, IsChecked = clip.Muted, Margin = new Thickness(0, 6, 0, 6), VerticalAlignment = VerticalAlignment.Center };
        Row("", muted);
        var info = new TextBlock { Text = ClipPropertiesPresenter.Note(clip), TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 10, 0, 10) };
        info[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");

        var ok = new Button { Content = ClipPropertiesPresenter.OkButton, IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = ClipPropertiesPresenter.CancelButton, IsCancel = true, MinWidth = 72 };
        ok.Click += (_, _) =>
        {
            ClipPropertiesPresenter.Apply(clip, name.Text ?? "", gain.Value, pitch.Value, speed.Value, muted.IsChecked == true);
            Close(true);
        };
        cancel.Click += (_, _) => Close(false);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } };
        Content = new StackPanel { Margin = new Thickness(16), Children = { grid, info, buttons } };
    }
}
