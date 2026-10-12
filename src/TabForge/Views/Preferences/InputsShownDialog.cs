using System.Windows;
using System.Windows.Controls;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

// Owns: the "Inputs shown" dialog of the Audio settings: one row per input of the running audio device with a tick for
//       the track input pickers ("Recording") and one for the live input menu ("Live input"), kept per device name.
// Does not own: the list of inputs and the filtering (AudioInputs), where the choices are stored (PluginSettings.HiddenInputs).
// Tests: TestAudioInputChannels (Apply, Copy).
internal static class InputsShownDialog
{
    /// <summary>A copy that shares no list with <paramref name="from"/>.</summary>
    public static Dictionary<string, HiddenAudioInputs> Copy(Dictionary<string, HiddenAudioInputs>? from) =>
        (from ?? new()).Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => new HiddenAudioInputs { Recording = new(kv.Value.Recording ?? new()), LiveInput = new(kv.Value.LiveInput ?? new()) });

    /// <summary>Stores one device's hidden ids (no entry when nothing is hidden) and applies every device's choices to the pickers.</summary>
    public static void Apply(PluginSettings settings, string device, IEnumerable<string> hiddenRecording, IEnumerable<string> hiddenLive)
    {
        var entry = new HiddenAudioInputs { Recording = hiddenRecording.ToList(), LiveInput = hiddenLive.ToList() };
        if (entry.Recording.Count + entry.LiveInput.Count == 0) settings.HiddenInputs.Remove(device);
        else settings.HiddenInputs[device] = entry;
        AudioInputs.SetHidden(Copy(settings.HiddenInputs));
    }

    /// <summary>The settings row's button: edits the running device's choices; on OK they apply at once and the settings window's copy and baseline keep them.</summary>
    public static void Edit(Window owner, PluginSettings settings, PluginSettings baseline)
    {
        var device = AudioInputs.Device;
        settings.HiddenInputs.TryGetValue(device, out var now);
        var w = new Window
        {
            Title = "Inputs shown", Owner = owner, Width = 440, Height = 480, MinHeight = 260, MinWidth = 360, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        var root = new DockPanel { Margin = new Thickness(14) };
        var intro = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
            Text = (device.Length > 0 ? $"Inputs of {device}." : "The audio device has not reported its inputs yet: these are the inputs every device has.")
                   + " Untick an input to leave it out of the track input pickers (Recording) or the live input menu (Live input). An input in use stays listed where it is used."
        };
        DockPanel.SetDock(intro, Dock.Top);
        root.Children.Add(intro);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var all = new Button { Content = "Show all", MinWidth = 80, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 2, 10, 2), ToolTip = "Tick every input in both columns" };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 2, 10, 2) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Padding = new Thickness(10, 2, 10, 2) };
        buttons.Children.Add(all); buttons.Children.Add(ok); buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        void Place(UIElement element, int row, int column)
        {
            Grid.SetRow(element, row); Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Place(new TextBlock { Text = "Input", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) }, 0, 0);
        Place(new TextBlock { Text = "Recording", FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center }, 0, 1);
        Place(new TextBlock { Text = "Live input", FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center }, 0, 2);
        var rows = new List<(string Id, CheckBox Recording, CheckBox Live)>();
        foreach (var id in AudioInputs.Audio)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = AudioInputs.Label(id);
            CheckBox Tick(string column, bool shown)
            {
                var box = new CheckBox { IsChecked = shown, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ToolTip = $"Show {label} for {column}" };
                System.Windows.Automation.AutomationProperties.SetName(box, $"{label}: {column}");
                return box;
            }
            var recording = Tick("recording", now?.Recording.Contains(id) != true);
            var live = Tick("live input", now?.LiveInput.Contains(id) != true);
            Place(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 8, 3), TextTrimming = TextTrimming.CharacterEllipsis }, rows.Count + 1, 0);
            Place(recording, rows.Count + 1, 1);
            Place(live, rows.Count + 1, 2);
            rows.Add((id, recording, live));
        }
        root.Children.Add(new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        all.Click += (_, _) => { foreach (var (_, recording, live) in rows) recording.IsChecked = live.IsChecked = true; };
        ok.Click += (_, _) =>
        {
            Apply(settings, device, rows.Where(r => r.Recording.IsChecked != true).Select(r => r.Id), rows.Where(r => r.Live.IsChecked != true).Select(r => r.Id));
            baseline.HiddenInputs = Copy(settings.HiddenInputs);
            w.DialogResult = true;
        };
        w.Content = root;
        DialogHost.ShowModal(w);
    }
}
