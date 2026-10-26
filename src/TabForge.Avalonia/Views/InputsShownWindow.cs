using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Models;
using TabForge.Services;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

// Owns: the "Inputs shown" dialog of the Audio settings: one row per input of the running device, with a tick for the track input
//       pickers (Recording) and one for the live input menu (Live input), kept per device name in PluginSettings.HiddenInputs.
// Does not own: the list of inputs and the filtering (Core AudioInputs), or the Settings window's copy and baseline (PreferencesWindow.Buttons.cs
// writes what this returns). The copy and apply rules repeat the rules of the inputs-shown dialog.
// Tests: TabForge.Avalonia.Tests.PreferencesSettingsTransferTests.
public sealed class InputsShownWindow : Window
{
    private readonly PluginSettings _settings;
    private readonly PluginSettings _baseline;
    private readonly string _device = AudioInputs.Device;
    private readonly List<(string Id, CheckBox Recording, CheckBox Live)> _rows = new();

    private InputsShownWindow(PluginSettings settings, PluginSettings baseline)
    {
        _settings = settings;
        _baseline = baseline;
        Title = "Inputs shown";
        Width = 440; Height = 480; MinWidth = 360; MinHeight = 260; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBrush");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        Content = BuildContent();
    }

    /// <summary>The copy of a per-device choice list that shares no list with <paramref name="from"/>.</summary>
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

    /// <summary>Shows the dialog over <paramref name="owner"/>; true when OK changed the choices, which then apply at once (the baseline keeps them too).</summary>
    public static async Task<bool> EditAsync(Window owner, PluginSettings settings, PluginSettings baseline) =>
        await DialogLauncher.Ask<InputsShownWindow, bool?>(owner, () => new InputsShownWindow(settings, baseline)) == true;

    private Control BuildContent()
    {
        var intro = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
            Text = (_device.Length > 0 ? $"Inputs of {_device}." : "The audio device has not reported its inputs yet: these are the inputs every device has.")
                   + " Untick an input to leave it out of the track input pickers (Recording) or the live input menu (Live input). An input in use stays listed where it is used.",
        };
        var all = new Button { Content = "Show all", MinWidth = DialogMetrics.DialogButtonMinWidth, Margin = new Thickness(0, 0, 8, 0) };
        ToolTip.SetTip(all, "Tick every input in both columns");
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = DialogMetrics.DialogButtonMinWidth, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = DialogMetrics.DialogButtonMinWidth };
        all.Click += (_, _) => { foreach (var (_, recording, live) in _rows) recording.IsChecked = live.IsChecked = true; };
        ok.Click += (_, _) => Commit();
        cancel.Click += (_, _) => Close(false);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = DialogMetrics.FooterGap, Children = { all, ok, cancel } };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,90,90") };
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Children.Add(Header("Input", 0));
        grid.Children.Add(Header("Recording", 1));
        grid.Children.Add(Header("Live input", 2));
        var hidden = _settings.HiddenInputs.GetValueOrDefault(_device);
        for (var i = 0; i < AudioInputs.Audio.Length; i++)
        {
            var id = AudioInputs.Audio[i];
            var label = AudioInputs.Label(id);
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var recording = Tick(label, "recording", hidden?.Recording.Contains(id) != true, i + 1, 1, grid);
            var live = Tick(label, "live input", hidden?.LiveInput.Contains(id) != true, i + 1, 2, grid);
            var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 8, 3), TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetRow(name, i + 1); grid.Children.Add(name);
            _rows.Add((id, recording, live));
        }

        var dock = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(intro, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(intro);
        dock.Children.Add(buttons);
        dock.Children.Add(new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        return dock;
    }

    private static TextBlock Header(string text, int column)
    {
        var block = new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = column == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Center };
        Grid.SetColumn(block, column);
        return block;
    }

    private static CheckBox Tick(string label, string column, bool shown, int row, int col, Grid grid)
    {
        var box = new CheckBox { IsChecked = shown, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(box, $"Show {label} for {column}");
        AutomationProperties.SetName(box, $"{label}: {column}");
        Grid.SetRow(box, row); Grid.SetColumn(box, col);
        grid.Children.Add(box);
        return box;
    }

    private void Commit()
    {
        Apply(_settings, _device, _rows.Where(r => r.Recording.IsChecked != true).Select(r => r.Id), _rows.Where(r => r.Live.IsChecked != true).Select(r => r.Id));
        _baseline.HiddenInputs = Copy(_settings.HiddenInputs);
        Close(true);
    }
}
