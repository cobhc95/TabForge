using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Services;
using WpfPath = System.Windows.Shapes.Path;
using static TabForge.Views.PreferencesCards;

namespace TabForge.Views;

/// <summary>The editor control of each setting row (switch, choice, number, text, colour, row button); changes go to the host.</summary>
internal sealed class SettingEditors
{
    private readonly IPreferencesHost _host;
    private readonly Window _window;

    internal SettingEditors(IPreferencesHost host)
    {
        _host = host;
        _window = host.Dialog;
    }

    private Button CreateButton(string text, string styleKey, string? tooltip = null) => PreferencesCards.CreateButton(_window, text, styleKey, tooltip);

    internal FrameworkElement BuildEditor(SettingDescriptor descriptor)
    {
        return descriptor.Kind switch
        {
            SettingKind.Bool => BuildBoolEditor(descriptor),
            SettingKind.Choice => BuildChoiceEditor(descriptor),
            SettingKind.Number => BuildNumberEditor(descriptor),
            SettingKind.Colour => BuildColourEditor(descriptor),
            SettingKind.Button => BuildRowButton(descriptor),
            _ => BuildTextEditor(descriptor)
        };
    }

    /// <summary>The button of a <see cref="SettingKind.Button"/> row: each key has its own action.</summary>
    private FrameworkElement BuildRowButton(SettingDescriptor descriptor)
    {
        switch (descriptor.Key)
        {
            case "score.textfonts":
                return RowButton(descriptor, "Text & fonts…", true, OpenScoreTextFonts);
            case "fretboard.showallas":
                return RowButton(descriptor, "Apply to all tracks", _host.Actions?.ShowAllTracksAs is not null,
                    () => _host.Actions?.ShowAllTracksAs?.Invoke(_host.Settings.Editing.InstrumentView));
            case "general.checknow":
                return RowButton(descriptor, "Check now", _host.Actions?.CheckForUpdatesNow is not null,
                    () => _host.Actions?.CheckForUpdatesNow?.Invoke(_window));
            case "mixer.grouprules":
                return RowButton(descriptor, "Group rules…", _host.Actions?.EditGroupRules is not null, () => _host.Actions?.EditGroupRules?.Invoke(_window));
            case "vst.quarantine":
                return BuildQuarantineButton(descriptor);
            default:
                return BuildLinkedAudioButton(descriptor);
        }
    }

    private FrameworkElement BuildQuarantineButton(SettingDescriptor descriptor)
    {
        var button = RowButton(descriptor, "Plug-ins switched off after a crash…", _host.Actions?.ManageQuarantine is not null, () =>
        {
            if (_host.Actions?.ManageQuarantine is not { } open) return;
            // Allow again changes the live settings straight away (like the linked-audio approvals); keep _window window's copy and baseline in step.
            var list = open(_window);
            _host.Settings.Plugins.Quarantined = list.ToList();
            _host.Baseline.Plugins.Quarantined = list.ToList();
        });
        return button;
    }

    private Button RowButton(SettingDescriptor descriptor, string label, bool enabled, Action click)
    {
        var button = new Button { Content = label, Padding = new Thickness(10, 3, 10, 3), ToolTip = descriptor.Tooltip(_host.Settings.Hotkeys), IsEnabled = enabled };
        AutomationProperties.SetName(button, descriptor.Title);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>Per-area score text styles, edited on _window window's staged settings and previewed live like any other row.</summary>
    private void OpenScoreTextFonts()
    {
        var areas = _host.Settings.Appearance.ScoreTextAreas ??= new();
        var before = JsonSerializer.Serialize(areas);
        if (ScoreTextStyleWindow.Show(_window, areas, () => { TabEditorControl.ConfigureTextAreas(areas); _host.SettingChanged(); })) { _host.SettingChanged(); return; }
        var restored = JsonSerializer.Deserialize<Dictionary<string, ScoreTextAreaStyle>>(before) ?? new();
        _host.Settings.Appearance.ScoreTextAreas = restored;
        TabEditorControl.ConfigureTextAreas(restored);
        _host.SettingChanged();
    }

    private FrameworkElement BuildLinkedAudioButton(SettingDescriptor descriptor)
    {
        var button = new Button { Content = "Manage approved folders…", Padding = new Thickness(10, 3, 10, 3), ToolTip = descriptor.Tooltip(_host.Settings.Hotkeys),
            IsEnabled = _host.Actions?.ManageLinkedAudio is not null };
        AutomationProperties.SetName(button, "Manage approved folders");
        button.Click += (_, _) =>
        {
            if (_host.Actions?.ManageLinkedAudio is not { } open) return;
            // Approvals change straight away (they are a safety decision); keep _window window's copy and its baseline in step
            // so Apply does not bring back a revoked folder and Cancel does not undo one.
            var approvals = open(_window);
            _host.Settings.Audio.ApprovedMedia = approvals.Select(a => new MediaApproval { Project = a.Project, Folder = a.Folder }).ToList();
            _host.Baseline.Audio.ApprovedMedia = approvals.Select(a => new MediaApproval { Project = a.Project, Folder = a.Folder }).ToList();
        };
        return button;
    }

    private FrameworkElement BuildBoolEditor(SettingDescriptor descriptor)
    {
        var check = new CheckBox { IsChecked = descriptor.Get() is true, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = descriptor.Tooltip(_host.Settings.Hotkeys), MinWidth = 24 };
        AutomationProperties.SetName(check, descriptor.Title);
        check.Checked += (_, _) => _host.SettingChanged(descriptor, true);
        check.Unchecked += (_, _) => _host.SettingChanged(descriptor, false);
        return check;
    }

    private FrameworkElement BuildChoiceEditor(SettingDescriptor descriptor)
    {
        var choices = descriptor.Choices;
        // Device names are shown exactly as Windows names them ("Audient iD4", not "Audient i D4").
        string Label(string value) => descriptor.Key is "vst.device" or "vst.input" or "vst.asio.in" or "vst.asio.out" or "vst.asio.outlast" ? value
            : descriptor.Key == "audio.fretboardstyle" && value.StartsWith("GP5: ", StringComparison.Ordinal) ? "Show " + value.Substring(5).ToLowerInvariant()
            : Prettify(value);
        var selected = choices.ToList().FindIndex(value => value.Equals(descriptor.Get()?.ToString(), StringComparison.OrdinalIgnoreCase));
        var combo = new ComboBox
        {
            Width = Math.Clamp(choices.Select(Label).DefaultIfEmpty("").Max(value => value.Length) * 8 + 36, 150, 300),
            ItemsSource = choices.Select(Label).ToArray(),
            SelectedIndex = Math.Max(0, selected),
            ToolTip = descriptor.Tooltip(_host.Settings.Hotkeys),
            MaxDropDownHeight = 330
        };
        AutomationProperties.SetName(combo, descriptor.Title);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex < 0 || combo.SelectedIndex >= choices.Count) return;
            descriptor.Set(choices[combo.SelectedIndex]);
            _host.SettingChanged();
            // A theme preset rewrites many colours: show their new values (after _window event finishes).
            if (descriptor.Key == "appearance.thememode") _window.Dispatcher.BeginInvoke(new Action(() => _host.RebuildPage()));
            // A new audio driver has its own devices: refresh the device list and the ASIO-only rows.
            if (descriptor.Key == "vst.driver")
                _window.Dispatcher.BeginInvoke(new Action(() => { _host.RefreshDescriptors(); _host.RebuildPage(); }));
        };
        if (descriptor.Key == "vst.buffer") return BufferSizeEditor(descriptor, combo, choices);
        if (descriptor.Key != "vst.device" || !string.Equals(_host.Settings.Plugins.Driver, AudioDrivers.Asio, StringComparison.Ordinal)) return combo;
        // ASIO: the driver's own panel sets the buffer size and routing.
        var configure = new Button { Content = "Configure…", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2),
            ToolTip = "Open the ASIO driver's own control panel (buffer size, sample rate, routing)" };
        configure.Click += (_, _) =>
        {
            if (!TabForge.Audio.AudioDevices.ShowAsioControlPanel(_host.Settings.Plugins.Device, out var why)) _host.SetStatus(why);
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(combo);
        panel.Children.Add(configure);
        return panel;
    }

    /// <summary>Buffer size: type any value (16 to 8192 samples) or pick a usual one from the list.</summary>
    private FrameworkElement BufferSizeEditor(SettingDescriptor descriptor, ComboBox presets, IReadOnlyList<string> choices)
    {
        var box = new TextBox { Width = 64, Text = descriptor.Get()?.ToString() ?? "256", VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Type a buffer size in samples (16 to 8192) and press Enter" };
        AutomationProperties.SetName(box, descriptor.Title);
        void Commit()
        {
            if (!int.TryParse(box.Text.Trim(), out var typed)) { box.Text = descriptor.Get()?.ToString() ?? "256"; return; }
            typed = Math.Clamp(typed, AudioDrivers.MinBuffer, AudioDrivers.MaxBuffer);
            box.Text = typed.ToString();
            if (typed.ToString() == descriptor.Get()?.ToString()) return;
            descriptor.Set(typed.ToString());
            _host.SettingChanged();
        }
        box.LostKeyboardFocus += (_, _) => Commit();
        box.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        // The list: picking one fills the box.
        presets.Width = 74;
        presets.SelectedIndex = -1;
        presets.SelectionChanged += (_, _) =>
        {
            if (presets.SelectedIndex < 0 || presets.SelectedIndex >= choices.Count) return;
            box.Text = choices[presets.SelectedIndex];
            Commit();
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(box);
        presets.Margin = new Thickness(6, 0, 0, 0);
        row.Children.Add(presets);
        return row;
    }

    private FrameworkElement BuildNumberEditor(SettingDescriptor descriptor)
    {
        var value = ToFiniteDouble(descriptor.Get(), descriptor.Min);
        value = Math.Clamp(value, descriptor.Min, descriptor.Max);
        var spinner = BuildNumberSpinner(descriptor, value);
        if (descriptor.Key == "vst.winmidilatency") return WithMeasureButton(descriptor, spinner);
        if (!descriptor.Unit.Equals("%", StringComparison.Ordinal)) return spinner;

        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider
        {
            Minimum = descriptor.Min,
            Maximum = descriptor.Max,
            Value = value,
            Width = 165,
            TickFrequency = descriptor.Step,
            SmallChange = descriptor.Step,
            LargeChange = descriptor.Step * 10,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = descriptor.Tooltip(_host.Settings.Hotkeys),
            IsSnapToTickEnabled = false
        };
        AutomationProperties.SetName(slider, descriptor.Title);
        panel.Children.Add(slider);
        spinner.Margin = new Thickness(8, 0, 0, 0);
        panel.Children.Add(spinner);
        var syncing = false;
        slider.ValueChanged += (_, args) =>
        {
            if (syncing) return;
            syncing = true;
            SetNumberSpinnerValue(spinner, descriptor, args.NewValue);
            syncing = false;
            descriptor.Set(args.NewValue);
            _host.SettingChanged();
        };
        spinner.Tag = new Action<double>(next =>
        {
            if (syncing) return;
            syncing = true;
            slider.Value = next;
            syncing = false;
            descriptor.Set(next);
            _host.SettingChanged();
        });
        return panel;
    }

    private FrameworkElement BuildNumberSpinner(SettingDescriptor descriptor, double initial)
    {
        var width = descriptor.Unit == "%" ? 82.0 : 118.0;
        var panel = new Grid { Width = width, Height = 32, ToolTip = descriptor.Tooltip(_host.Settings.Hotkeys),
            Tag = descriptor };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var text = new TextBox
        {
            Style = (Style)_window.FindResource("NumericValueTextBox"),
            Text = FormatNumber(initial, descriptor.Decimals),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(text, descriptor.Title);
        Grid.SetRowSpan(text, 2);
        panel.Children.Add(text);
        var up = CreateButton("", "NumericStepButton", $"Increase {descriptor.Title}.");
        var down = CreateButton("", "NumericStepButton", $"Decrease {descriptor.Title}.");
        up.Content = SpinnerArrow(upward: true);
        down.Content = SpinnerArrow(upward: false);
        AutomationProperties.SetName(up, $"Increase {descriptor.Title}");
        AutomationProperties.SetName(down, $"Decrease {descriptor.Title}");
        Grid.SetColumn(up, 1);
        Grid.SetColumn(down, 1);
        Grid.SetRow(down, 1);
        var outer = new Border { Background = (Brush)Application.Current.FindResource("Panel2Brush"), BorderBrush = (Brush)Application.Current.FindResource("BorderSoftBrush"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = panel };
        panel.Children.Add(up);
        panel.Children.Add(down);

        var valid = true;
        var settingText = false;
        void Change(double candidate, bool updateText)
        {
            var next = Math.Clamp(candidate, descriptor.Min, descriptor.Max);
            if (updateText)
            {
                settingText = true;
                text.Text = FormatNumber(next, descriptor.Decimals);
                settingText = false;
            }
            if (outer.Tag is Action<double> callback) callback(next);
            else
            {
                descriptor.Set(next);
                _host.SettingChanged();
            }
        }
        text.TextChanged += (_, _) =>
        {
            if (settingText) return;
            if (double.TryParse(text.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed) ||
                double.TryParse(text.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                valid = true;
                text.BorderBrush = Brushes.Transparent;
                if (double.IsFinite(parsed)) Change(parsed, false);
            }
            else
            {
                valid = false;
                text.BorderBrush = Brush("#C04D5B");
            }
        };
        text.LostFocus += (_, _) =>
        {
            var current = Math.Clamp(ToFiniteDouble(descriptor.Get(), descriptor.Min), descriptor.Min, descriptor.Max);
            if (!valid || !double.TryParse(text.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var entered) ||
                !double.IsFinite(entered) || Math.Abs(Math.Clamp(entered, descriptor.Min, descriptor.Max) - entered) > 1e-9)
            {
                settingText = true;
                text.Text = FormatNumber(current, descriptor.Decimals);
                settingText = false;
            }
            valid = true;
            text.BorderBrush = Brushes.Transparent;
        };
        text.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            _window.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        };
        up.Click += (_, _) => Change(ToFiniteDouble(descriptor.Get(), initial) + descriptor.Step, true);
        down.Click += (_, _) => Change(ToFiniteDouble(descriptor.Get(), initial) - descriptor.Step, true);
        AutomationProperties.SetName(outer, descriptor.Title);
        panel.Tag = null;
        return outer;
    }

    /// <summary>
    /// "Windows MIDI latency (ms)": the number box plus a Measure button. One press plays a very quiet hit five times through the Windows
    /// synth and reads it back (only when nothing else plays); the median is stored only when it is plausible (20-600 ms).
    /// </summary>
    private FrameworkElement WithMeasureButton(SettingDescriptor descriptor, FrameworkElement spinner)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var button = new Button { Content = "Measure", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Plays one very quiet hit five times through the Windows MIDI synth and reads it back from the Windows output. Stop playback first; other audio spoils the reading." };
        AutomationProperties.SetName(button, "Measure the Windows MIDI latency");
        var result = new TextBlock { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 };
        panel.Children.Add(spinner);
        panel.Children.Add(button);
        panel.Children.Add(result);
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            result.Text = "Measuring (about 5 s)…";
            (double? Ms, string Detail) outcome;
            try { outcome = await Task.Run(() => TabForge.Audio.WindowsMidiLatency.Measure(new TabForge.Playback.SharedMidiOutput())); }
            catch (Exception ex) { outcome = (null, ex.Message); } // Not logged: latency measurement: the detail is shown in the dialog
            if (outcome.Ms is { } ms)
            {
                var rounded = (int)Math.Round(ms);
                SetNumberSpinnerValue(spinner, descriptor, rounded);
                descriptor.Set(rounded);
                _host.SettingChanged();
                result.Text = $"{rounded} ms stored ({outcome.Detail})";
            }
            else result.Text = $"Not changed: {outcome.Detail}";
            button.IsEnabled = true;
        };
        return panel;
    }

    private static void SetNumberSpinnerValue(FrameworkElement spinner, SettingDescriptor descriptor, double value)
    {
        if (spinner is not Border { Child: Grid grid }) return;
        if (grid.Children.OfType<TextBox>().FirstOrDefault() is { } text)
            text.Text = FormatNumber(value, descriptor.Decimals);
    }

    private static WpfPath SpinnerArrow(bool upward) => new()
    {
        Data = Geometry.Parse(upward ? "M 1,4 L 4,1 L 7,4" : "M 1,1 L 4,4 L 7,1"),
        Stroke = (Brush)Application.Current.FindResource("MutedBrush"),
        StrokeThickness = 1.3,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        Width = 8,
        Height = 5,
        Stretch = Stretch.Fill,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private FrameworkElement BuildTextEditor(SettingDescriptor descriptor)
    {
        var text = new TextBox { Width = 210, Text = descriptor.Get()?.ToString() ?? "",
            ToolTip = descriptor.Tooltip(_host.Settings.Hotkeys), VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(text, descriptor.Title);
        text.TextChanged += (_, _) =>
        {
            descriptor.Set(text.Text);
            _host.SettingChanged();
        };
        if (descriptor.Key != "vst.folders") return text;
        // Folder lists: browse to a folder instead of typing its path.
        var browse = new Button { Content = "Browse…", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        browse.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Add a plug-in folder", Multiselect = true };
            if (dialog.ShowDialog(_window) != true) return;
            var folders = text.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            foreach (var folder in dialog.FolderNames)
                if (!folders.Contains(folder, StringComparer.OrdinalIgnoreCase)) folders.Add(folder);
            text.Text = string.Join("; ", folders);
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(text);
        row.Children.Add(browse);
        return row;
    }

    internal static readonly (string Name, string Hex)[] ColourPresets =
    {
        ("Glide blue", "#4C9AFF"), ("Cyan", "#22D3EE"), ("Teal", "#14B8A6"), ("Emerald", "#10B981"),
        ("Green", "#3FB950"), ("Lime", "#84CC16"), ("Violet", "#8B5CF6"), ("Indigo", "#6366F1"),
        ("Magenta", "#D946EF"), ("Rose", "#F43F5E"), ("Crimson", "#DC2626"), ("Orange", "#F97316"),
        ("Amber", "#F59E0B"), ("White", "#F2F4F6"), ("Silver", "#98A1AE"), ("Slate", "#4B5563"),
        ("Charcoal", "#23272D"), ("Black", "#111111")
    };

    /// <summary>
    /// Colour setting: a swatch (click for the full picker with hue and hex) next to a preset dropdown.
    /// The dropdown lists the setting's default, the named presets and, when needed, the current custom value.
    /// </summary>
    private FrameworkElement BuildColourEditor(SettingDescriptor descriptor)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var swatch = new Button
        {
            Style = (Style)_window.FindResource("ColourSwatchButton"),
            Background = SafeBrush(descriptor.Get()?.ToString()),
            ToolTip = "Open the colour picker (hue, shade and hex code)."
        };
        AutomationProperties.SetName(swatch, $"Choose {descriptor.Title}");
        var defaultHex = _host.DefaultsByKey.TryGetValue(descriptor.Key, out var original)
            ? original.Get()?.ToString() ?? "#FFFFFF" : "#FFFFFF";
        var combo = new ComboBox { Width = 170, Margin = new Thickness(8, 0, 0, 0), ToolTip = descriptor.Tooltip(_host.Settings.Hotkeys) };
        AutomationProperties.SetName(combo, descriptor.Title);
        var syncing = false;

        FrameworkElement Entry(string name, string hex)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Tag = hex };
            row.Children.Add(new Border
            {
                Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 8, 0),
                Background = SafeBrush(hex), BorderBrush = Brush("#55606B"), BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            return row;
        }

        void Populate(string current)
        {
            syncing = true;
            combo.Items.Clear();
            combo.Items.Add(Entry("Default", defaultHex));
            foreach (var (name, hex) in ColourPresets) combo.Items.Add(Entry(name, hex));
            var match = combo.Items.OfType<FrameworkElement>()
                .FirstOrDefault(item => string.Equals((string)item.Tag, current, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                match = Entry($"Custom ({current.ToUpperInvariant()})", current);
                combo.Items.Add(match);
            }
            combo.SelectedItem = match;
            syncing = false;
        }

        void Apply(string hex)
        {
            if (!TryColour(hex, out _)) return;
            swatch.Background = SafeBrush(hex);
            descriptor.Set(hex);
            _host.SettingChanged();
        }

        combo.SelectionChanged += (_, _) =>
        {
            if (syncing || combo.SelectedItem is not FrameworkElement { Tag: string hex }) return;
            Apply(hex);
        };
        swatch.Click += (_, _) =>
        {
            var current = descriptor.Get()?.ToString() ?? defaultHex;
            var picker = new ColourPickerWindow(current, defaultHex, _host.Settings.Appearance.RecentColours, _window);
            if (DialogHost.ShowModal(picker) != true || picker.SelectedColour is null) return;
            _host.AddRecentColour(picker.SelectedColour);
            Apply(picker.SelectedColour);
            Populate(picker.SelectedColour);
        };
        Populate(descriptor.Get()?.ToString() ?? defaultHex);
        panel.Children.Add(swatch);
        panel.Children.Add(combo);
        return panel;
    }

    private static double ToFiniteDouble(object? value, double fallback)
    {
        try
        {
            var result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return double.IsFinite(result) ? result : fallback;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { return fallback; } // Not logged: value parse fallback: the default is used
    }

    private static string FormatNumber(double value, int decimals)
    {
        var format = decimals > 0 ? "0." + new string('0', decimals) : "0";
        return value.ToString(format, CultureInfo.CurrentCulture);
    }

    private static string Prettify(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var result = new System.Text.StringBuilder(value.Length + 5);
        for (var i = 0; i < value.Length; i++)
        {
            if (i > 0 && char.IsUpper(value[i]) && !char.IsUpper(value[i - 1])) result.Append(' ');
            result.Append(value[i]);
        }
        return result.ToString();
    }
}
