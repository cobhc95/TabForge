using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
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
    private readonly LargeSwitch _switch;
    private readonly TextBlock _switchLabel;
    private readonly StackPanel _body;
    private readonly RuleEditor _boost = new();
    private readonly ComboBox _preset = new() { ItemsSource = FocusTrackSettings.Presets, Name = "PresetBox", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _presetNote = new() { TextWrapping = TextWrapping.Wrap, FontSize = ChromeMetrics.TextSmall };
    private readonly TextBlock _preview = new() { TextWrapping = TextWrapping.Wrap, FontSize = ChromeMetrics.TextSmall };
    private readonly MixBar[] _mixBars = new MixBar[5];
    private readonly List<GroupRow> _rows = new();
    private bool _loading;
    private bool _syncingSwitch;

    /// <param name="settings">The saved settings this window edits (written on Apply).</param>
    /// <param name="on">Whether Focus track is on now.</param>
    /// <param name="setOn">Turns Focus track on or off, at once.</param>
    /// <param name="applied">Called after Apply has written the settings.</param>
    public FocusTrackSettingsWindow(FocusTrackSettings settings, bool on, Action<bool> setOn, Action applied)
    {
        _settings = settings;
        _applied = applied;
        Title = "Focus track settings";
        Width = ChromeMetrics.FocusWindowWidth + 24;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        DialogMetrics.CentreOnOwner(this);
        this.UseWindowColours();
        FontSize = ChromeMetrics.TextDialog;

        var iconGlyph = new global::Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M12,2 A10,10 0 1,0 22,12 A10,10 0 1,0 12,2 M12,7 A5,5 0 1,1 7,12 A5,5 0 0,1 12,7 M12,10 A2,2 0 1,0 14,12 A2,2 0 0,0 12,10"),
            Width = ChromeMetrics.FocusIconGlyph,
            Height = ChromeMetrics.FocusIconGlyph,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        iconGlyph[!global::Avalonia.Controls.Shapes.Shape.FillProperty] = new DynamicResourceExtension("AccentBrush");
        var iconBadge = new Border
        {
            Width = ChromeMetrics.FocusIconBadge,
            Height = ChromeMetrics.FocusIconBadge,
            CornerRadius = new CornerRadius(ChromeMetrics.RadiusXLarge),
            BorderThickness = DialogMetrics.HairlineAll,
            Child = iconGlyph,
            VerticalAlignment = VerticalAlignment.Center,
        };
        iconBadge[!Border.BackgroundProperty] = new DynamicResourceExtension("AccentSoftBrush");
        iconBadge[!Border.BorderBrushProperty] = new DynamicResourceExtension("SelectedOutlineBrush");

        var title = new TextBlock { Text = "Focus track", FontSize = ChromeMetrics.TextHeading, FontWeight = FontWeight.SemiBold };
        var subtitle = new TextBlock { Text = "Highlight the active track during playback", FontSize = ChromeMetrics.TextSmall };
        subtitle.UseMutedText();
        var titleStack = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { title, subtitle } };
        var titleLeft = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { iconBadge, titleStack } };

        _switchLabel = new TextBlock
        {
            Text = on ? "On" : "Off",
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _switchLabel[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(on ? "AccentBrush" : "TextMuted");
        _switch = new LargeSwitch
        {
            IsChecked = on,
            Name = "FocusSwitch",
            VerticalAlignment = VerticalAlignment.Center,
        }.Named("Focus track", "Turn Focus track on or off");
        var toggleGroup = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _switchLabel, _switch },
        };
        var header = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(toggleGroup, Dock.Right);
        header.Children.Add(toggleGroup);
        header.Children.Add(titleLeft);
        var headerBar = DialogMetrics.Padded(new Border { Child = header, BorderThickness = DialogMetrics.HairlineBottom }, Gap);
        headerBar[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");

        _boost.Changed += RefreshPreview;
        _preset.SelectionChanged += (_, _) => { if (!_loading && _preset.SelectedItem is string name && name != FocusTrackSettings.Custom) LoadPreset(name); RefreshPreview(); };
        _presetNote.UseMutedText();

        var list = new StackPanel();
        for (var i = 0; i < FocusTrackSettings.Groups.Count; i++)
        {
            var group = FocusTrackSettings.Groups[i];
            var row = new GroupRow(group, () => { if (!_loading) { _loading = true; _preset.SelectedItem = FocusTrackSettings.Custom; _loading = false; } RefreshPreview(); });
            _rows.Add(row);
            if (i > 0)
            {
                var sep = new Border { Height = 1 };
                sep[!Border.BackgroundProperty] = new DynamicResourceExtension("BorderSoftBrush");
                list.Children.Add(sep);
            }
            list.Children.Add(row.Root);
        }
        var listBox = new Border { Child = list, BorderThickness = DialogMetrics.HairlineAll, CornerRadius = new CornerRadius(Corner + 2) };
        listBox[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");
        listBox[!Border.BackgroundProperty] = new DynamicResourceExtension("WindowBackground");

        var mixGrid = new UniformGrid { Rows = 1, Columns = 5 };
        var barLabels = new[] { "Selected", "Guitars", "Basses", "Drums", "Other" };
        for (var i = 0; i < _mixBars.Length; i++)
        {
            _mixBars[i] = new MixBar(barLabels[i], isAccent: i == 0);
            mixGrid.Children.Add(_mixBars[i].Root);
        }
        var mixTitle = new TextBlock { Text = "LIVE MIX BALANCE", FontSize = ChromeMetrics.TextSmall, FontWeight = FontWeight.SemiBold };
        mixTitle.UseMutedText();
        var mixHint = new TextBlock { Text = "From fader 100", FontSize = ChromeMetrics.TextSmall };
        mixHint.UseMutedText();
        var mixHead = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(mixHint, Dock.Right);
        mixHead.Children.Add(mixHint);
        mixHead.Children.Add(mixTitle);

        var previewDivider = new Border { Height = 1 };
        previewDivider[!Border.BackgroundProperty] = new DynamicResourceExtension("BorderSoftBrush");
        _preview.UseMutedText();
        var previewCard = Card(new StackPanel { Spacing = Tight + 2, Children = { mixHead, mixGrid, previewDivider, _preview } });

        var boostCard = Card(Section("SELECTED TRACK: HOW MUCH LOUDER", _boost.Root));
        var othersCard = Card(Section("OTHER TRACKS: WHAT THEY DO", new StackPanel { Spacing = Tight, Children = { _preset, _presetNote, listBox } }));

        _body = new StackPanel { Spacing = Tight + 4, Opacity = on ? 1.0 : 0.55 };
        _body.Children.Add(boostCard);
        _body.Children.Add(othersCard);
        _body.Children.Add(previewCard);
        _switch.IsCheckedChanged += (_, _) =>
        {
            var isOn = _switch.IsChecked == true;
            UpdateSwitchVisuals(isOn);
            if (!_syncingSwitch) setOn(isOn);
        };

        var reset = new Button { Content = "Reset", Name = "ResetButton" };
        reset.Classes.Add("PrefsActionReset");
        reset.Click += (_, _) => Load(new FocusTrackSettings());
        var cancel = new Button { Content = CommonText.Cancel, IsCancel = true }.WithMargin(DialogMetrics.FocusButtonGap);
        cancel.Classes.Add("PrefsActionButton");
        cancel.Click += (_, _) => Close();
        var apply = DialogButtons.Apply();
        apply.Classes.Add("PrefsActionButton");
        apply.Click += (_, _) => Apply();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancel, apply } };
        var footer = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(reset);
        var footerBar = DialogMetrics.Padded(new Border { Child = footer, BorderThickness = DialogMetrics.HairlineTop }, Gap);
        footerBar[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");

        var bodyBox = DialogMetrics.Padded(new Border { Child = _body }, Gap);
        Content = new StackPanel { Children = { headerBar, bodyBox, footerBar } };
        Load(settings);
    }

    /// <summary>Updates the header switch and dimmed state to match an external toggle (such as the left strip icon or hotkey).</summary>
    public void SyncSwitch(bool isOn)
    {
        _syncingSwitch = true;
        _switch.IsChecked = isOn;
        UpdateSwitchVisuals(isOn);
        _syncingSwitch = false;
    }

    private void UpdateSwitchVisuals(bool isOn)
    {
        _switchLabel.Text = isOn ? "On" : "Off";
        _switchLabel[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(isOn ? "AccentBrush" : "TextMuted");
        _body.Opacity = isOn ? 1.0 : 0.55;
    }

    private static Control Card(Control content)
    {
        var card = DialogMetrics.Padded(new Border
        {
            Child = content,
            BorderThickness = DialogMetrics.HairlineAll,
            CornerRadius = new CornerRadius(Corner + 4),
        }, Gap - 2, Tight + 4);
        card[!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        card[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");
        return card;
    }

    private static Control Section(string label, Control content)
    {
        var text = new TextBlock { Text = label, FontSize = ChromeMetrics.TextSmall, FontWeight = FontWeight.SemiBold }.WithMargin(DialogMetrics.FocusLabelGap);
        text.UseMutedText();
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
        var boost = _boost.Read();
        var others = _rows.ToDictionary(r => r.Group, r => r.Read());
        _preview.Text = Describe(boost, others);

        const int Sample = 100;
        var selectedLevel = (int)MidiLimits.Clamp(Math.Round(boost.Apply(Sample)));
        _mixBars[0].SetLevel(selectedLevel);
        for (var i = 0; i < FocusTrackSettings.Groups.Count && i + 1 < _mixBars.Length; i++)
        {
            var group = FocusTrackSettings.Groups[i];
            var other = others.TryGetValue(group, out var o) ? o : new FocusOther();
            var level = other.Mode switch
            {
                FocusOtherMode.LeaveAlone => Sample,
                FocusOtherMode.Lower => (int)MidiLimits.Clamp(Math.Round(other.Amount.ApplyLower(Sample))),
                FocusOtherMode.Raise => (int)MidiLimits.Clamp(Math.Round(other.Amount.Apply(Sample))),
                _ => (int)MidiLimits.Clamp(Math.Round(boost.ApplyLower(Sample))),
            };
            _mixBars[i + 1].SetLevel(level);
        }
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
        private readonly Slider _slider;
        private readonly TextBlock _value = new() { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Right };
        private readonly bool _lower;
        private FocusBoostMode _mode;
        private bool _loading;

        /// <param name="lower">A lowering, not a raise: its percentage tops out at 100, its sample amounts are smaller, its value reads with a minus.</param>
        public RuleEditor(bool lower = false)
        {
            _lower = lower;
            _slider = CreateModernSlider();
            _segments = Modes.Select(m => CreateSegmentButton(m.Label)).ToArray();
            var strip = new UniformGrid { Rows = 1 };
            for (var i = 0; i < Modes.Count; i++)
            {
                var mode = Modes[i].Mode;
                _segments[i].Click += (_, _) => { _mode = mode; Sync(resetAmount: true); Changed?.Invoke(); };
                strip.Children.Add(_segments[i]);
            }
            var segBox = new Border
            {
                Child = strip,
                CornerRadius = new CornerRadius(ChromeMetrics.RadiusXLarge),
                BorderThickness = DialogMetrics.HairlineAll,
            }.WithPadding(DialogMetrics.FocusStripPad);
            segBox[!Border.BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
            segBox[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");

            var valueBadge = new Border
            {
                Child = _value,
                MinWidth = ChromeMetrics.FocusValueMinWidth,
                CornerRadius = new CornerRadius(ChromeMetrics.RadiusLarge),
                BorderThickness = DialogMetrics.HairlineAll,
                VerticalAlignment = VerticalAlignment.Center,
            }.WithPadding(DialogMetrics.FocusValuePad).WithMargin(DialogMetrics.FocusValueGap);
            valueBadge[!Border.BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
            valueBadge[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");

            _slider.ValueChanged += (_, _) => { if (_loading) return; _value.Text = Text(); Changed?.Invoke(); };
            var amount = new DockPanel { LastChildFill = true }.WithMargin(DialogMetrics.FocusSectionTop);
            DockPanel.SetDock(valueBadge, Dock.Right);
            amount.Children.Add(valueBadge);
            amount.Children.Add(_slider);
            Root = new StackPanel { Spacing = Tight, Children = { segBox, amount } };
        }

        private static Button CreateSegmentButton(string label) => new Button
        {
            Content = label,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(ChromeMetrics.RadiusLarge),
            MinHeight = ChromeMetrics.FocusSegmentMinHeight,
            FontSize = ChromeMetrics.TextSmall,
            Template = new FuncControlTemplate<Button>((btn, scope) =>
            {
                var presenter = new ContentPresenter
                {
                    Name = "PART_ContentPresenter",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                presenter.Bind(ContentPresenter.ContentProperty, btn.GetObservable(ContentControl.ContentProperty), BindingPriority.Template);
                presenter.Bind(ContentPresenter.ContentTemplateProperty, btn.GetObservable(ContentControl.ContentTemplateProperty), BindingPriority.Template);
                presenter.Bind(ContentPresenter.ForegroundProperty, btn.GetObservable(TemplatedControl.ForegroundProperty), BindingPriority.Template);
                presenter.RegisterInNameScope(scope);

                var pill = new Border
                {
                    Child = presenter,
                    CornerRadius = btn.CornerRadius,
                };
                pill.Bind(Decorator.PaddingProperty, btn.GetObservable(TemplatedControl.PaddingProperty), BindingPriority.Template);
                pill.Bind(Border.BackgroundProperty, btn.GetObservable(TemplatedControl.BackgroundProperty), BindingPriority.Template);
                return pill;
            }),
        }.WithPadding(DialogMetrics.FocusSegmentPad);

        private static Slider CreateModernSlider() => new()
        {
            Minimum = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Height = ChromeMetrics.FocusSliderHeight,
            MinHeight = ChromeMetrics.FocusSliderHeight,
            Background = AvaloniaPalette.TransparentBrush,
            Template = new FuncControlTemplate<Slider>((slider, scope) =>
            {
                var less = new RepeatButton
                {
                    Name = "PART_DecreaseButton",
                    Focusable = false,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Template = new FuncControlTemplate<RepeatButton>((_, _) =>
                    {
                        var fill = new Border { Height = ChromeMetrics.FocusSliderTrack, CornerRadius = new CornerRadius(ChromeMetrics.RadiusSmall, 0, 0, ChromeMetrics.RadiusSmall), VerticalAlignment = VerticalAlignment.Center };
                        fill[!Border.BackgroundProperty] = new DynamicResourceExtension("AccentBrush");
                        return fill;
                    }),
                };
                var more = new RepeatButton
                {
                    Name = "PART_IncreaseButton",
                    Focusable = false,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Template = new FuncControlTemplate<RepeatButton>((_, _) =>
                    {
                        var rest = new Border { Height = ChromeMetrics.FocusSliderTrack, CornerRadius = new CornerRadius(0, ChromeMetrics.RadiusSmall, ChromeMetrics.RadiusSmall, 0), VerticalAlignment = VerticalAlignment.Center };
                        rest[!Border.BackgroundProperty] = new DynamicResourceExtension("BorderSoftBrush");
                        return rest;
                    }),
                };
                var thumb = new Thumb
                {
                    Name = "thumb",
                    Width = ChromeMetrics.FocusSliderThumb,
                    Height = ChromeMetrics.FocusSliderThumb,
                    Template = new FuncControlTemplate<Thumb>((_, _) =>
                    {
                        var dot = new Border
                        {
                            Width = ChromeMetrics.FocusSliderThumb,
                            Height = ChromeMetrics.FocusSliderThumb,
                            CornerRadius = new CornerRadius(ChromeMetrics.FocusSliderThumb / 2),
                            BorderThickness = DialogMetrics.FocusThumbRing,
                        };
                        dot[!Border.BackgroundProperty] = new DynamicResourceExtension("AccentBrush");
                        dot[!Border.BorderBrushProperty] = new DynamicResourceExtension("PanelBackground");
                        return dot;
                    }),
                };
                var track = new Track
                {
                    Name = "PART_Track",
                    Orientation = Orientation.Horizontal,
                    Thumb = thumb,
                    DecreaseButton = less,
                    IncreaseButton = more,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                track.Bind(RangeBase.MinimumProperty, slider.GetObservable(RangeBase.MinimumProperty), BindingPriority.Template);
                track.Bind(RangeBase.MaximumProperty, slider.GetObservable(RangeBase.MaximumProperty), BindingPriority.Template);
                track.Bind(RangeBase.ValueProperty, new Binding { Path = nameof(RangeBase.Value), Source = slider, Mode = BindingMode.TwoWay });
                track.RegisterInNameScope(scope);
                thumb.RegisterInNameScope(scope);
                less.RegisterInNameScope(scope);
                more.RegisterInNameScope(scope);
                return new Grid { Height = ChromeMetrics.FocusSliderHeight, Background = AvaloniaPalette.TransparentBrush, Children = { track } };
            }),
        };

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
                if (on)
                {
                    _segments[i][!Button.BackgroundProperty] = new DynamicResourceExtension("AccentBrush");
                    _segments[i][!Button.ForegroundProperty] = new DynamicResourceExtension("OnAccentBrush");
                }
                else
                {
                    _segments[i].Background = AvaloniaPalette.TransparentBrush;
                    _segments[i][!Button.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
                }
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
            var amounts = new Border
            {
                BorderThickness = DialogMetrics.FocusAmountRule,
                Child = new StackPanel { Children = { _lowerEditor.Root, _raiseEditor.Root } },
            }.WithPadding(DialogMetrics.FocusAmountPad).WithMargin(DialogMetrics.FocusAmountMargin);
            amounts[!Border.BorderBrushProperty] = new DynamicResourceExtension("AccentBrush");
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
            var hasCustom = Mode is FocusOtherMode.Lower or FocusOtherMode.Raise;
            _lowerEditor.Root.IsVisible = Mode == FocusOtherMode.Lower;
            _raiseEditor.Root.IsVisible = Mode == FocusOtherMode.Raise;
            if (Root is StackPanel sp && sp.Children.Count > 1)
                sp.Children[1].IsVisible = hasCustom;
        }
    }

    private sealed class MixBar
    {
        private const double BarHeight = ChromeMetrics.FocusMixBarHeight;
        private readonly Border _fill;
        private readonly TextBlock _value;

        public MixBar(string label, bool isAccent)
        {
            _fill = new Border
            {
                CornerRadius = new CornerRadius(ChromeMetrics.RadiusSmall),
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = BarHeight,
            };
            _fill[!Border.BackgroundProperty] = new DynamicResourceExtension(isAccent ? "AccentBrush" : "TextMuted");

            var track = new Border
            {
                Height = BarHeight + 4,
                CornerRadius = new CornerRadius(ChromeMetrics.RadiusSmall + 2),
                BorderThickness = DialogMetrics.HairlineAll,
                Child = _fill,
            }.WithPadding(DialogMetrics.FocusMixTrackPad);
            track[!Border.BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
            track[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");

            var name = new TextBlock
            {
                Text = label,
                FontSize = ChromeMetrics.TextSmall,
                FontWeight = isAccent ? FontWeight.SemiBold : FontWeight.Normal,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            name[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(isAccent ? "AccentBrush" : "TextPrimary");

            _value = new TextBlock
            {
                Text = "100",
                FontSize = ChromeMetrics.TextSmall,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            _value.UseMutedText();

            Root = new StackPanel
            {
                Spacing = 3,
                Children = { track, name, _value },
            }.WithMargin(DialogMetrics.FocusMixColumnGap);
        }

        public Control Root { get; }

        public void SetLevel(int level)
        {
            _value.Text = level.ToString(CultureInfo.InvariantCulture);
            _fill.Height = Math.Max(3, BarHeight * MidiLimits.Fraction(level));
        }
    }
}
