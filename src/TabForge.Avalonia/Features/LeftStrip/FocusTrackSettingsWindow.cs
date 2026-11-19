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
public sealed partial class FocusTrackSettingsWindow : Window
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

}
