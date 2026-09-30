using System.Windows;
using System.Windows.Controls;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>
/// standard Mix Table (F10): choose which parameters change from the selected beat on, their new
/// values, how quickly they change and whether they apply to every track. Returns null on Cancel;
/// an empty MixChange means "Clear".
/// </summary>
public static class MixTableWindow
{
    public sealed record Result(MixChange? Mix, int? Tempo);

    public static Result? Show(Window owner, MixChange? current, int currentProgram, int currentVolume, int currentPan, int? currentTempo, int songTempo)
    {
        var w = new Window
        {
            Title = "Mix Table", Width = 470, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = owner, ShowInTaskbar = false,
        };
        w.SetResourceReference(Control.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = "Choose the parameters to change from this beat and when these changes shall be applied:",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });

        var grid = new Grid();
        foreach (var width in new[] { 110.0, 180, 1 })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width == 1 ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
        var row = 0;
        (CheckBox Check, FrameworkElement Editor) Row(string label, FrameworkElement editor, bool enabled)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var check = new CheckBox { Content = label, IsChecked = enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
            editor.IsEnabled = enabled;
            editor.Margin = new Thickness(0, 4, 8, 4);
            check.Click += (_, _) => editor.IsEnabled = check.IsChecked == true;
            Grid.SetRow(check, row); Grid.SetRow(editor, row); Grid.SetColumn(editor, 1);
            grid.Children.Add(check); grid.Children.Add(editor);
            row++;
            return (check, editor);
        }
        (Slider Slider, TextBlock Value) Scale(double min, double max, double value)
        {
            var slider = new Slider { Minimum = min, Maximum = max, Value = value, IsSnapToTickEnabled = true, TickFrequency = 1, Width = 140 };
            var text = new TextBlock { Width = 26, VerticalAlignment = VerticalAlignment.Center, Text = ((int)value).ToString() };
            slider.ValueChanged += (_, e) => text.Text = ((int)e.NewValue).ToString();
            return (slider, text);
        }
        StackPanel WithValue((Slider Slider, TextBlock Value) s) => new() { Orientation = Orientation.Horizontal, Children = { s.Slider, s.Value } };

        var instruments = new ComboBox();
        foreach (var entry in InstrumentCatalog.All.Where(e => !e.IsDrumKit).GroupBy(e => e.Program).Select(g => g.First()))
            instruments.Items.Add(new ComboBoxItem { Content = $"{entry.Program + 1} - {entry.Name}", Tag = entry.Program });
        instruments.SelectedItem = instruments.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == (current?.Program ?? currentProgram));
        var instrument = Row("Instrument", instruments, current?.Program is not null);

        var volume = Scale(0, 16, current?.Volume ?? Math.Clamp((int)Math.Round(currentVolume / 8.0), 0, 16));
        var volumeRow = Row("Volume", WithValue(volume), current?.Volume is not null);
        var pan = Scale(-8, 8, current?.Pan ?? Math.Clamp((int)Math.Round((currentPan - 64) / 8.0), -8, 8));
        var panRow = Row("Pan", WithValue(pan), current?.Pan is not null);
        var chorus = Scale(0, 16, current?.Chorus ?? 0); var chorusRow = Row("Chorus", WithValue(chorus), current?.Chorus is not null);
        var reverb = Scale(0, 16, current?.Reverb ?? 0); var reverbRow = Row("Reverb", WithValue(reverb), current?.Reverb is not null);
        var phaser = Scale(0, 16, current?.Phaser ?? 0); var phaserRow = Row("Phaser", WithValue(phaser), current?.Phaser is not null);
        var tremolo = Scale(0, 16, current?.Tremolo ?? 0); var tremoloRow = Row("Tremolo", WithValue(tremolo), current?.Tremolo is not null);
        var tempoBox = new TextBox { Width = 70, HorizontalAlignment = HorizontalAlignment.Left, Text = (currentTempo ?? songTempo).ToString() };
        var tempoRow = Row("Tempo (bar)", tempoBox, currentTempo is not null);
        root.Children.Add(grid);

        var options = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        options.Children.Add(new TextBlock { Text = "Transition", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var transition = new ComboBox { Width = 130 };
        transition.Items.Add("Immediately");
        for (var b = 1; b <= 8; b++) transition.Items.Add(b == 1 ? "Over 1 beat" : $"Over {b} beats");
        transition.SelectedIndex = Math.Clamp(current?.TransitionBeats ?? 0, 0, 8);
        options.Children.Add(transition);
        var allTracks = new CheckBox { Content = "All tracks", IsChecked = current?.AllTracks ?? false, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        options.Children.Add(allTracks);
        root.Children.Add(options);

        var buttons = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var clear = new Button { Content = "Clear", Width = 80 };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        DockPanel.SetDock(clear, Dock.Left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } };
        buttons.Children.Add(clear); buttons.Children.Add(right);
        root.Children.Add(buttons);

        Result? result = null;
        clear.Click += (_, _) => { result = new Result(new MixChange(), null); w.DialogResult = true; };
        ok.Click += (_, _) =>
        {
            int? Pick((CheckBox Check, FrameworkElement Editor) r, Slider s) => r.Check.IsChecked == true ? (int)s.Value : null;
            var mix = new MixChange
            {
                Program = instrument.Check.IsChecked == true && instruments.SelectedItem is ComboBoxItem { Tag: int program } ? program : null,
                Volume = Pick(volumeRow, volume.Slider), Pan = Pick(panRow, pan.Slider),
                Chorus = Pick(chorusRow, chorus.Slider), Reverb = Pick(reverbRow, reverb.Slider),
                Phaser = Pick(phaserRow, phaser.Slider), Tremolo = Pick(tremoloRow, tremolo.Slider),
                TransitionBeats = transition.SelectedIndex, AllTracks = allTracks.IsChecked == true,
            };
            int? tempo = tempoRow.Check.IsChecked == true && int.TryParse(tempoBox.Text.Trim(), out var bpm) ? Math.Clamp(bpm, 20, 400) : null;
            result = new Result(mix, tempo);
            w.DialogResult = true;
        };
        w.Content = root;
        return DialogHost.ShowModal(w) == true ? result : null;
    }
}
