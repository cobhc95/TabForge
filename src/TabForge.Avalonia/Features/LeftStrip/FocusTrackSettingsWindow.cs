using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Models;
using TabForge.Platform.Theme;
using TabForge.Services;

namespace TabForge.Avalonia.Features.LeftStrip;

// Owns: the Focus track settings window (right-click the Focus track icon, or Focus track settings in the View menu): a narrow card with the on / off switch,
//   the selected track's boost (Decibels / Percent / Level with a slider), and the behaviour of the other tracks as one template: a choice of ready-made
//   templates and, for each mixer group (Guitars, Basses, Drums, Other instruments), whether its tracks are lowered like the boost, left as they are, or lowered or
//   raised by an amount of their own; a plain-language line for what it all does; Reset, Cancel, Apply. Changing a group by hand makes the template Custom.
//   The switch acts at once; Apply writes the settings and tells the app, which works the levels out again.
// Does not own: the settings and the levels they make (FocusTrackSettings, PracticeFocus), saving the settings file and the on / off state (the caller).
// Tests: TabForge.Avalonia.Tests.PracticeFocusTests (the rules), FocusTrackSettingsWindowTests (the window).
public sealed class FocusTrackSettingsWindow : Window
{
    private const double Gap = ChromeMetrics.FocusGap, Tight = ChromeMetrics.FocusTightGap, Corner = ChromeMetrics.FocusCorner;

    private static readonly IReadOnlyList<(FocusBoostMode Mode, string Label, double Max, double Step)> Modes = new[]
    {
        (FocusBoostMode.Decibels, "Decibels", FocusBoost.MaxDecibels, 0.5),
        (FocusBoostMode.Percent, "Percent", FocusBoost.MaxPercent, 5),
        (FocusBoostMode.ToLevel, "Level", FocusBoost.MaxLevel, 1),
    };

    private static readonly IReadOnlyList<(FocusOtherMode Mode, string Label)> Choices = new[]
    {
        (FocusOtherMode.SameAsBoost, "Lowered like the boost"),
        (FocusOtherMode.LeaveAlone, "Left as it is"),
        (FocusOtherMode.Lower, "Lowered by…"),
        (FocusOtherMode.Raise, "Raised by…"),
    };

    private readonly FocusTrackSettings _settings;
    private readonly Action _applied;
    private readonly RuleEditor _boost = new();
    private readonly ComboBox _preset = new() { ItemsSource = FocusTrackSettings.Presets, Name = "PresetBox", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _presetNote = new() { TextWrapping = TextWrapping.Wrap, FontSize = ChromeMetrics.TextSmall };
    private readonly TextBlock _preview = new() { TextWrapping = TextWrapping.Wrap };
    private readonly List<GroupRow> _rows = new();
    private bool _loading;

    /// <param name="settings">The saved settings this window edits (written on Apply).</param>
    /// <param name="on">Whether Focus track is on now.</param>
    /// <param name="setOn">Turns Focus track on or off, at once.</param>
    /// <param name="applied">Called after Apply has written the settings.</param>
    public FocusTrackSettingsWindow(FocusTrackSettings settings, bool on, Action<bool> setOn, Action applied)
    {
        _settings = settings;
        _applied = applied;
        Title = "Focus track settings";
        Width = ChromeMetrics.FocusWindowWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        DialogMetrics.CentreOnOwner(this);
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        FontSize = ChromeMetrics.TextDialog;

        var title = new TextBlock { Text = "Focus track", FontSize = ChromeMetrics.TextHeading, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var toggle = new ToggleSwitch { IsChecked = on, OnContent = "On", OffContent = "Off", Name = "FocusSwitch", HorizontalAlignment = HorizontalAlignment.Right };
        toggle.IsCheckedChanged += (_, _) => setOn(toggle.IsChecked == true);
        var header = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(toggle, Dock.Right);
        header.Children.Add(toggle);
        header.Children.Add(title);
        var headerBar = DialogMetrics.Padded(new Border { Child = header, BorderThickness = DialogMetrics.HairlineBottom }, Gap);
        headerBar[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");

        _boost.Changed += RefreshPreview;
        _preset.SelectionChanged += (_, _) => { if (!_loading && _preset.SelectedItem is string name && name != FocusTrackSettings.Custom) LoadPreset(name); RefreshPreview(); };
        _presetNote[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");

        var list = new StackPanel();
        foreach (var group in FocusTrackSettings.Groups)
        {
            var row = new GroupRow(group, () => { if (!_loading) { _loading = true; _preset.SelectedItem = FocusTrackSettings.Custom; _loading = false; } RefreshPreview(); });
            _rows.Add(row);
            list.Children.Add(row.Root);
        }
        var listBox = new Border { Child = list, BorderThickness = DialogMetrics.HairlineAll, CornerRadius = new CornerRadius(Corner) };
        listBox[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");

        var previewBox = DialogMetrics.Padded(new Border { Child = _preview, CornerRadius = new CornerRadius(Corner) }, Gap, Tight);
        previewBox[!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        _preview[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");

        var body = new StackPanel { Spacing = Gap };
        body.Children.Add(Section("SELECTED TRACK: HOW MUCH LOUDER", _boost.Root));
        body.Children.Add(Section("OTHER TRACKS: WHAT THEY DO", new StackPanel { Spacing = Tight, Children = { _preset, _presetNote, listBox } }));
        body.Children.Add(previewBox);

        var reset = new Button { Content = "Reset", Name = "ResetButton" };
        reset.Classes.Add("PrefsActionReset");
        reset.Click += (_, _) => Load(new FocusTrackSettings());
        var cancel = new Button { Content = "Cancel", IsCancel = true }.WithMargin(DialogMetrics.FocusButtonGap);
        cancel.Classes.Add("PrefsActionButton");
        cancel.Click += (_, _) => Close();
        var apply = new Button { Content = "Apply", IsDefault = true, Name = "ApplyButton" };
        apply.Classes.Add("PrefsActionButton");
        apply.Classes.Add(AvaloniaDefaultLooks.AccentLook);
        apply.Click += (_, _) => Apply();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancel, apply } };
        var footer = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(reset);
        var footerBar = DialogMetrics.Padded(new Border { Child = footer, BorderThickness = DialogMetrics.HairlineTop }, Gap);
        footerBar[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");

        var bodyBox = DialogMetrics.Padded(new Border { Child = body }, Gap);
        Content = new StackPanel { Children = { headerBar, bodyBox, footerBar } };
        Load(settings);
    }

    private static Control Section(string label, Control content)
    {
        var text = new TextBlock { Text = label, FontSize = ChromeMetrics.TextSmall }.WithMargin(DialogMetrics.FocusLabelGap);
        text[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        return new StackPanel { Children = { text, content } };
    }

    private void Load(FocusTrackSettings settings)
    {
        _loading = true;
        _boost.Show(settings.Boost);
        _preset.SelectedItem = FocusTrackSettings.Presets.Contains(settings.Preset) ? settings.Preset : FocusTrackSettings.Custom;
        foreach (var row in _rows) row.Show(settings.OtherFor(row.Group));
        _loading = false;
        RefreshPreview();
    }

    private void LoadPreset(string name)
    {
        var others = FocusTrackSettings.ForPreset(name);
        _loading = true;
        foreach (var row in _rows) row.Show(others[row.Group]);
        _loading = false;
    }

    private void Apply()
    {
        _settings.Boost = _boost.Read();
        _settings.Preset = _preset.SelectedItem as string ?? FocusTrackSettings.Spotlight;
        _settings.Others = _rows.ToDictionary(r => r.Group, r => r.Read());
        _applied();
        Close();
    }

    private void RefreshPreview()
    {
        var preset = _preset.SelectedItem as string ?? FocusTrackSettings.Custom;
        _presetNote.Text = FocusTrackSettings.Describe(preset);
        _preview.Text = Describe(_boost.Read(), _rows.ToDictionary(r => r.Group, r => r.Read()));
    }

    /// <summary>What the settings do, said for a track at level 100 (the fader's usual place).</summary>
    public static string Describe(FocusBoost boost, IReadOnlyDictionary<string, FocusOther> others)
    {
        const int Sample = 100;
        var wanted = boost.Apply(Sample);
        var first = wanted <= MidiLimits.Max
            ? string.Create(CultureInfo.InvariantCulture, $"The selected track at level {Sample} plays at {Math.Round(wanted)}.")
            : string.Create(CultureInfo.InvariantCulture, $"The selected track at level {Sample} plays at {MidiLimits.Max}, as loud as it can go.");
        var parts = FocusTrackSettings.Groups.Select(g => $"{g.ToLowerInvariant()} {Behaviour(others.TryGetValue(g, out var o) ? o : new FocusOther(), boost, wanted / Sample)}");
        return first + " Others: " + string.Join(", ", parts) + ".";
    }

    private static string Behaviour(FocusOther other, FocusBoost boost, double ratio) => other.Mode switch
    {
        FocusOtherMode.LeaveAlone => "unchanged",
        FocusOtherMode.Lower => LowerSummary(other.Amount),
        FocusOtherMode.Raise => Summary(other.Amount),
        _ => boost.Mode == FocusBoostMode.Percent ? LowerSummary(boost)
            : string.Create(CultureInfo.InvariantCulture, $"-{20 * Math.Log10(Math.Max(1, ratio)):0.#} dB"),
    };

    /// <summary>A raise in a few words: "+6 dB", "+50%", "level 110".</summary>
    public static string Summary(FocusBoost rule) => rule.Mode switch
    {
        FocusBoostMode.Decibels => string.Create(CultureInfo.InvariantCulture, $"+{rule.ClampedAmount:0.#} dB"),
        FocusBoostMode.Percent => string.Create(CultureInfo.InvariantCulture, $"+{rule.ClampedAmount:0}%"),
        _ => string.Create(CultureInfo.InvariantCulture, $"level {rule.ClampedAmount:0}"),
    };

    /// <summary>A lowering in a few words: "-3 dB", "-30%", "up to level 80".</summary>
    public static string LowerSummary(FocusBoost rule) => rule.Mode switch
    {
        FocusBoostMode.Decibels => string.Create(CultureInfo.InvariantCulture, $"-{rule.ClampedLowerAmount:0.#} dB"),
        FocusBoostMode.Percent => string.Create(CultureInfo.InvariantCulture, $"-{rule.ClampedLowerAmount:0}%"),
        _ => string.Create(CultureInfo.InvariantCulture, $"up to level {rule.ClampedLowerAmount:0}"),
    };

    /// <summary>One amount: three modes as a segmented choice, a slider and the value.</summary>
    private sealed class RuleEditor
    {
        private readonly Button[] _segments;
        private readonly Slider _slider = new() { Minimum = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly TextBlock _value = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold }.WithMargin(DialogMetrics.FocusValueGap);
        private readonly bool _lower;
        private FocusBoostMode _mode;
        private bool _loading;

        /// <param name="lower">A lowering, not a raise: its percentage tops out at 100, its sample amounts are smaller, its value reads with a minus.</param>
        public RuleEditor(bool lower = false)
        {
            _lower = lower;
            _segments = Modes.Select(m => new Button { Content = m.Label, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center }).ToArray();
            var strip = new UniformGrid { Rows = 1 };
            for (var i = 0; i < Modes.Count; i++)
            {
                var mode = Modes[i].Mode;
                _segments[i].Click += (_, _) => { _mode = mode; Sync(resetAmount: true); Changed?.Invoke(); };
                strip.Children.Add(_segments[i]);
            }
            var segBox = new Border { Child = strip, CornerRadius = new CornerRadius(Corner) };
            segBox[!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
            _slider.ValueChanged += (_, _) => { if (_loading) return; _value.Text = Text(); Changed?.Invoke(); };
            var amount = new DockPanel { LastChildFill = true }.WithMargin(DialogMetrics.FocusSectionTop);
            DockPanel.SetDock(_value, Dock.Right);
            amount.Children.Add(_value);
            amount.Children.Add(_slider);
            Root = new StackPanel { Children = { segBox, amount } };
        }

        public Control Root { get; }

        /// <summary>Raised when the mode or the amount changed by the user.</summary>
        public event Action? Changed;

        public void Show(FocusBoost rule)
        {
            _mode = rule.Mode;
            _loading = true;
            Sync(resetAmount: false);
            _slider.Value = _lower ? rule.ClampedLowerAmount : rule.ClampedAmount;
            _loading = false;
            _value.Text = Text();
        }

        public FocusBoost Read() => new(_mode, Math.Clamp(_slider.Value, 0, MaxOf(_mode)));

        private double MaxOf(FocusBoostMode mode) => _lower && mode == FocusBoostMode.Percent ? 100 : Modes.First(m => m.Mode == mode).Max;

        private string Text() => _lower ? LowerSummary(Read()) : Summary(Read());

        // The segments show the chosen mode; the slider takes the mode's range (and, on a change of mode by the user, its usual starting amount).
        private void Sync(bool resetAmount)
        {
            var wasLoading = _loading;
            _loading = true;
            var info = Modes.First(m => m.Mode == _mode);
            _slider.Maximum = MaxOf(_mode);
            _slider.SmallChange = info.Step;
            _slider.TickFrequency = info.Step;
            _slider.IsSnapToTickEnabled = true;
            if (resetAmount) _slider.Value = _lower
                ? _mode switch { FocusBoostMode.Decibels => 3, FocusBoostMode.Percent => 30, _ => FocusBoost.MaxLevel * 0.6 }
                : _mode switch { FocusBoostMode.Decibels => 6, FocusBoostMode.Percent => 50, _ => FocusBoost.MaxLevel * 0.8 };
            for (var i = 0; i < _segments.Length; i++)
            {
                var on = Modes[i].Mode == _mode;
                _segments[i][!Button.BackgroundProperty] = new DynamicResourceExtension(on ? "AccentSoftBrush" : "PanelBackground");
                _segments[i][!Button.ForegroundProperty] = new DynamicResourceExtension(on ? "TextPrimary" : "TextMuted");
                _segments[i].FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            }
            _loading = wasLoading;
            _value.Text = Text();
        }
    }

    /// <summary>A group's row: its name and what happens to its other tracks; "Lowered by…" and "Raised by…" open an amount.</summary>
    private sealed class GroupRow
    {
        private readonly ComboBox _choice = new() { ItemsSource = Choices.Select(c => c.Label).ToList(), HorizontalAlignment = HorizontalAlignment.Right };
        private readonly RuleEditor _lowerEditor = new(lower: true);
        private readonly RuleEditor _raiseEditor = new();
        private readonly Action _changed;
        private bool _loading;

        public GroupRow(string group, Action changed)
        {
            Group = group;
            _changed = changed;
            var name = new TextBlock { Text = group, VerticalAlignment = VerticalAlignment.Center };
            var head = new DockPanel { LastChildFill = false };
            DockPanel.SetDock(_choice, Dock.Right);
            head.Children.Add(_choice);
            head.Children.Add(name);
            var headBox = DialogMetrics.Padded(new Border { Child = head }, Gap, Tight);
            var amounts = DialogMetrics.Padded(new Border { Child = new StackPanel { Children = { _lowerEditor.Root, _raiseEditor.Root } } }, Gap, Tight);
            _choice.SelectionChanged += (_, _) => { Sync(); if (!_loading) _changed(); };
            _lowerEditor.Changed += () => { if (!_loading) _changed(); };
            _raiseEditor.Changed += () => { if (!_loading) _changed(); };
            Root = new StackPanel { Children = { headBox, amounts } };
            Sync();
        }

        public string Group { get; }

        public Control Root { get; }

        private FocusOtherMode Mode => Choices[Math.Max(0, _choice.SelectedIndex)].Mode;

        public FocusOther Read() => Mode switch
        {
            FocusOtherMode.Lower => new FocusOther(Mode, _lowerEditor.Read()),
            FocusOtherMode.Raise => new FocusOther(Mode, _raiseEditor.Read()),
            _ => new FocusOther(Mode),
        };

        public void Show(FocusOther other)
        {
            _loading = true;
            _choice.SelectedIndex = Choices.ToList().FindIndex(c => c.Mode == other.Mode);
            var amount = other.Amount ?? new FocusBoost(FocusBoostMode.Decibels, 3);
            _lowerEditor.Show(amount);
            _raiseEditor.Show(amount);
            Sync();
            _loading = false;
        }

        private void Sync()
        {
            _lowerEditor.Root.IsVisible = Mode == FocusOtherMode.Lower;
            _raiseEditor.Root.IsVisible = Mode == FocusOtherMode.Raise;
        }
    }
}
