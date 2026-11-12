using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Models;
using TabForge.Platform.Theme;
using TabForge.Services;

namespace TabForge.Avalonia.Features.LeftStrip;

// Owns: the Focus track settings window (right-click the Focus track icon, or Settings in its View menu row): the default rule (decibels, percent or a level)
//   and, for each mixer group (Guitars, Basses, Drums, Other instruments), the choice to use the default or a rule of its own; Reset to default, Cancel, Apply.
//   Apply writes the rules into the saved settings and tells the app, which works the levels out again at once.
// Does not own: the rules and the levels they make (FocusTrackSettings, PracticeFocus), saving the settings file (the caller), the on / off.
// Tests: TabForge.Avalonia.Tests.PracticeFocusTests.
public sealed class FocusTrackSettingsWindow : Window
{
    private const double Gap = ChromeMetrics.LeftStripGap, ColumnGap = ChromeMetrics.LeftStripColumnGap;
    private const string SameAsDefault = "Same as default";

    private static readonly (FocusBoostMode Mode, string Label, string Unit, double Max)[] Modes =
    {
        (FocusBoostMode.Decibels, "Louder by (dB)", "dB", FocusBoost.MaxDecibels),
        (FocusBoostMode.Percent, "Louder by (%)", "%", FocusBoost.MaxPercent),
        (FocusBoostMode.ToLevel, "Play at level (0-127)", "", FocusBoost.MaxLevel),
    };

    /// <summary>The mixer groups that can have a rule of their own, in the order the window lists them.</summary>
    public static readonly IReadOnlyList<string> Groups = new[] { MixerGroups.Guitars, MixerGroups.Basses, MixerGroups.Drums, MixerGroups.AllButGuitarsAndBass };

    private readonly FocusTrackSettings _rules;
    private readonly Action _applied;
    private readonly Row _default;
    private readonly Dictionary<string, Row> _groups = new();

    public FocusTrackSettingsWindow(FocusTrackSettings rules, Action applied)
    {
        _rules = rules;
        _applied = applied;
        Title = "Focus track settings";
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = false;
        DialogMetrics.CentreOnOwner(this);
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        FontSize = ChromeMetrics.TextDialog;

        _default = new Row(allowSame: false);
        var body = new StackPanel { Spacing = Gap };
        body.Children.Add(new TextBlock
        {
            Text = "When Focus track is on, the selected track plays louder by the rule below. If that would pass the loudest level, the other tracks are lowered by the difference, so nothing distorts. Your faders and saved song are never changed.",
            TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
        });
        body.Children.Add(Heading("Any selected track"));
        body.Children.Add(_default.Panel);
        body.Children.Add(Heading("Rules for a group of tracks"));
        foreach (var group in Groups)
        {
            var row = new Row(allowSame: true);
            _groups[group] = row;
            body.Children.Add(new StackPanel { Spacing = Gap, Children = { new TextBlock { Text = group }, row.Panel } });
        }

        var reset = new Button { Content = "Reset to default", Name = "ResetButton" };
        reset.Click += (_, _) => Load(new FocusTrackSettings());
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 0, Gap, 0) };
        cancel.Click += (_, _) => Close();
        var apply = new Button { Content = "Apply", IsDefault = true, Name = "ApplyButton" };
        apply.Click += (_, _) => Apply();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, apply } };
        var footer = new DockPanel { Margin = DialogMetrics.FooterGap, Children = { reset, buttons } };
        DockPanel.SetDock(reset, Dock.Left);

        var dock = new DockPanel { Margin = DialogMetrics.ContentInset };
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);
        dock.Children.Add(body);
        Content = dock;
        Load(rules);
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = global::Avalonia.Media.FontWeight.SemiBold, Margin = new Thickness(0, Gap, 0, 0) };

    private void Load(FocusTrackSettings rules)
    {
        _default.Show(rules.Boost, same: false);
        foreach (var (group, row) in _groups)
        {
            var own = rules.Groups.TryGetValue(group, out var rule) ? rule : null;
            row.Show(own ?? rules.Boost, same: own is null);
        }
    }

    private void Apply()
    {
        _rules.Boost = _default.Read();
        _rules.Groups.Clear();
        foreach (var (group, row) in _groups)
            if (!row.IsSame) _rules.Groups[group] = row.Read();
        _applied();
        Close();
    }

    /// <summary>One rule: the mode choice (a group row also has Same as default) and the amount.</summary>
    private sealed class Row
    {
        private readonly ComboBox _mode = new();
        private readonly NumericUpDown _amount = new() { Minimum = 0, Increment = 1, FormatString = "0.#" };
        private readonly TextBlock _unit = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly bool _allowSame;

        public Row(bool allowSame)
        {
            _allowSame = allowSame;
            var items = new List<string>();
            if (allowSame) items.Add(SameAsDefault);
            items.AddRange(Modes.Select(m => m.Label));
            _mode.ItemsSource = items;
            _mode.SelectionChanged += (_, _) => Sync();
            Panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = ColumnGap, Children = { _mode, _amount, _unit } };
        }

        public StackPanel Panel { get; }

        public bool IsSame => _allowSame && _mode.SelectedIndex == 0;

        public void Show(FocusBoost rule, bool same)
        {
            var index = Array.FindIndex(Modes, m => m.Mode == rule.Mode);
            _mode.SelectedIndex = same && _allowSame ? 0 : Math.Max(0, index) + (_allowSame ? 1 : 0);
            _amount.Value = (decimal)rule.ClampedAmount;
            Sync();
        }

        public FocusBoost Read()
        {
            var mode = Modes[Math.Max(0, _mode.SelectedIndex - (_allowSame ? 1 : 0))];
            return new FocusBoost(mode.Mode, Math.Clamp((double)(_amount.Value ?? 0), 0, mode.Max));
        }

        private void Sync()
        {
            var same = IsSame;
            _amount.IsVisible = _unit.IsVisible = !same;
            if (same) return;
            var mode = Modes[Math.Max(0, _mode.SelectedIndex - (_allowSame ? 1 : 0))];
            _amount.Maximum = (decimal)mode.Max;
            _unit.Text = mode.Unit;
        }
    }
}
